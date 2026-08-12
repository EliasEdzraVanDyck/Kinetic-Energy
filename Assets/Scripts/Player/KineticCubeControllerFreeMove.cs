using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using KineticEnergy.Level;

namespace KineticEnergy.Player
{
    /// <summary>
    /// Complementary movement layered on top of KineticCubeController's charge-and-launch
    /// mechanic, not a replacement for it - both run simultaneously. Outside of actively aiming,
    /// the left stick directly drives movement: while grounded it walks the cube around on the
    /// X/Z plane, and while airborne (having launched, walked off a ledge, or fallen any other
    /// way) it applies a subtle, continuous nudge to the current trajectory instead: stick
    /// up/down extends/shortens how far it travels (pushes more or less force along the camera's
    /// forward direction), stick left/right drifts the landing spot sideways (force along the
    /// camera's right direction). "Subtle" is load-bearing here - airControlAcceleration
    /// stays below gravity so this can only ever nudge an already-falling arc, never replace
    /// it with full player-directed flight.
    ///
    /// The cube visually leans into whichever way the stick is pushed while airborne (a
    /// snowboard/surfboard-style bank), which is why the mesh lives on a separate `visual` child
    /// transform instead of directly on this object: the root Rigidbody keeps
    /// RigidbodyConstraints.FreezeRotation (needed for the BoxCast-based ground check and clean,
    /// predictable landings, exactly as in KineticCubeController), so the lean has to be a purely
    /// cosmetic rotation on a child that physics never touches.
    ///
    /// Coordinates with KineticCubeController (see launchController / AllowGroundedMovement /
    /// AllowAirborneNudge) rather than being toggled on/off by it - this stays enabled the whole
    /// time and simply goes passive on its own, per-FixedUpdate, per branch, based on what the
    /// launch controller is currently doing. The two branches are gated differently on purpose:
    /// grounded movement directly SETS velocity, which must stay blocked for a launch's entire
    /// flight (not just a brief post-launch window) or it can silently overwrite the launch
    /// itself; airborne nudging only ADDS a small force, which can't meaningfully stomp anything,
    /// so it only needs to wait out that brief window.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeControllerFreeMove : MonoBehaviour
    {
        [Header("Ground Movement")]
        public float moveSpeed = 4f;
        [Range(0f, 1f)] public float moveDeadzone = 0.15f;

        [Header("Air Correction")]
        // Doubled from 7 (direct request: "increase air control significantly") - still
        // below gravity (30), so this steers an existing fall rather than replacing it
        // with player-directed flight.
        [Tooltip("Max acceleration (m/s^2) applied from stick input while airborne - kept below gravity so this only ever nudges the existing fall, never overrides it.")]
        public float airControlAcceleration = 14f;
        [Range(0f, 1f)] public float airControlDeadzone = 0.1f;

        [Header("Leaning")]
        [Tooltip("How far (degrees) the visual leans at full stick deflection while airborne.")]
        public float maxLeanAngle = 22f;
        [Tooltip("How fast the visual eases toward its current lean target (and back to level on landing).")]
        public float leanSpeed = 8f;
        [Tooltip("Child mesh transform that leans - wired by setup. Physics stays upright (FreezeRotation) regardless of this.")]
        public Transform visual;

        [Header("Grounding")]
        public float groundCheckDistance = 0.6f;

        [Header("Fall Reset")]
        public float fallResetY = -30f;

        [Header("Input")]
        public InputActionReference moveAction;
        public Transform cameraTransform;

        [Header("Test Movement Toggle")]
        // Walking and air-nudging are being phased out of the control scheme - kept only
        // for testing. OFF at start; the M key toggles them during play.
        [Tooltip("Grounded WASD/stick walking and midair nudging. Disabled by default; press M in play mode to toggle. Aiming controls are unaffected either way.")]
        public bool movementInputEnabled = false;

        Rigidbody rb;
        BoxCollider boxCollider;
        KineticCubeController launchController;
        bool isGrounded;
        bool wasGrounded;
        // Velocity of the (kinematic) moving platform currently under the player's feet -
        // added into the grounded velocity so movers CARRY their rider smoothly. Read by
        // KineticCubeController too, so a grounded aim frozen on a mover rides along.
        public Vector3 GroundPlatformVelocity { get; private set; }
        Quaternion visualTargetRotation = Quaternion.identity;
        float launchFacingYaw;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            boxCollider = GetComponent<BoxCollider>();
            // Same Player object as KineticCubeController - the two now run together rather than
            // one disabling the other, so this needs to know when it's safe to drive velocity
            // directly (see AllowFreeMovement's own comment for why).
            launchController = GetComponent<KineticCubeController>();
        }

        void OnEnable()
        {
            moveAction?.action?.Enable();
        }

        void OnDisable()
        {
            moveAction?.action?.Disable();
        }

        void Update()
        {
            // Time.timeScale freezes physics/FixedUpdate for free, but not this raw check -
            // without this guard a fall-reset could still trigger while the pause menu is up.
            if (Time.timeScale <= 0f) return;

            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                movementInputEnabled = !movementInputEnabled;
                Debug.Log($"[FreeMove] movement input {(movementInputEnabled ? "ENABLED" : "disabled")} (M to toggle)");
            }

            if (transform.position.y < fallResetY)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        void FixedUpdate()
        {
            UpdateGrounded();

            // With movement input disabled (the default - M toggles it for testing), the
            // stick reads as centered here: no walking, no air nudge. Everything else this
            // component does - platform carry, launch facing, lean/level-out - stays live,
            // and the aiming schemes read this action themselves, unaffected.
            Vector2 stick = movementInputEnabled && moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            Vector3 forward = CameraRelativeForward();
            Vector3 right = CameraRelativeRight();

            if (isGrounded)
            {
                // Gated on the FULL flight (AllowGroundedMovement), not just the brief post-launch
                // window - this branch SETS velocity directly every tick it runs, which must never
                // happen while a real launch is still in progress, no matter what this component's
                // own isGrounded check thinks at this instant. A shallow shot staying close to the
                // ground for longer than a short fixed window was exactly what let this silently
                // overwrite real launches before - see AllowGroundedMovement's own comment.
                if (launchController != null && !launchController.AllowGroundedMovement)
                {
                    wasGrounded = isGrounded;
                    return;
                }

                Vector3 moveDirection = stick.sqrMagnitude > moveDeadzone * moveDeadzone
                    ? (forward * stick.y + right * stick.x).normalized
                    : Vector3.zero;

                // Walking speed rides ON TOP of whatever the platform underfoot is doing -
                // standing still on a moving platform means moving WITH it.
                Vector3 horizontalVelocity = moveDirection * moveSpeed + new Vector3(GroundPlatformVelocity.x, 0f, GroundPlatformVelocity.z);
                rb.linearVelocity = new Vector3(horizontalVelocity.x, rb.linearVelocity.y, horizontalVelocity.z);

                // Face the movement direction instantly while walking - "launch forward"
                // means the way the cube is visibly pointing, so walking must keep facing
                // honest at all times.
                if (moveDirection.sqrMagnitude > 0.0001f)
                {
                    launchFacingYaw = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
                    if (visual != null) visual.localRotation = Quaternion.Euler(0f, launchFacingYaw, 0f);
                }

                // Pitch/roll (lean) reset to level, but yaw (launchFacingYaw) is deliberately
                // left alone - landing shouldn't re-orient which way the cube is facing, only
                // level it back out. On the exact frame landing is detected, snap pitch/roll to
                // 0 immediately instead of letting the Slerp below ease into it - the target
                // here already matches that snap, so the Slerp step this same frame is a no-op.
                if (!wasGrounded && visual != null)
                {
                    visual.localRotation = Quaternion.Euler(0f, launchFacingYaw, 0f);
                }
                visualTargetRotation = Quaternion.Euler(0f, launchFacingYaw, 0f);
            }
            else
            {
                // Only needs to wait out the brief post-launch grace window (AllowAirborneNudge),
                // not the whole flight - this branch only ADDS a small force on top of whatever
                // velocity already exists, it can't stomp the launch the way directly setting
                // velocity could, so there's no reason to also suppress it for the entire flight.
                if (launchController != null && !launchController.AllowAirborneNudge)
                {
                    wasGrounded = isGrounded;
                    return;
                }

                if (stick.sqrMagnitude > airControlDeadzone * airControlDeadzone)
                {
                    // Force, not velocity - this ADDS to whatever the fall's existing velocity
                    // already is (from gravity, and from however the cube left the ground) rather
                    // than overriding it, so it always reads as "steering an existing fall".
                    //
                    // Divided back out by timeScale when the game is running FAST (the in-flight
                    // speed-up, KineticCubeController.launchFlightTimeScale): at timeScale 2 the
                    // physics steps twice as much game-time per real second, so an unadjusted
                    // acceleration would integrate into twice the nudge per real second of
                    // stick-holding. Nudge strength per real second must not depend on the
                    // speed-up. Slow-motion (charging) is deliberately left uncompensated:
                    // nothing is in flight to nudge during a charge anyway.
                    float speedUpCompensation = Time.timeScale > 1f ? 1f / Time.timeScale : 1f;
                    Vector3 correction = (forward * stick.y + right * stick.x) * (airControlAcceleration * speedUpCompensation);
                    rb.AddForce(correction, ForceMode.Acceleration);
                }

                // Leans "into" the stick: pushing forward (distance+) dips the nose forward,
                // pushing sideways banks that way - a snowboard/surfboard-style tilt. Signs here
                // are a best guess (no way to visually verify from this environment) - if it
                // reads as leaning the wrong way in the Editor, flip the sign on the offending
                // axis below rather than the stick input itself.
                float pitchLean = stick.y * maxLeanAngle;
                float rollLean = -stick.x * maxLeanAngle;
                // launchFacingYaw keeps the cube facing the direction it was launched in for the
                // rest of the flight - without it, this target's yaw would default back to 0
                // every tick and the next Slerp step would visibly un-rotate whatever
                // FaceLaunchDirection just snapped it to.
                visualTargetRotation = Quaternion.Euler(pitchLean, launchFacingYaw, rollLean);
            }

            if (visual != null)
            {
                visual.localRotation = Quaternion.Slerp(visual.localRotation, visualTargetRotation, leanSpeed * Time.fixedDeltaTime);
            }

            wasGrounded = isGrounded;
        }

        // Called by KineticCubeController the instant it applies a launch impulse. Snaps the
        // visual to face the launch direction immediately (not eased through Slerp like the
        // lean, which is the whole point - the player should see the cube committed to its new
        // heading the same physics tick it launches, not ease into facing it over a few frames),
        // and remembers the yaw so the ongoing airborne lean (FixedUpdate above) keeps facing
        // that direction, with pitch/roll lean layered on top, for the rest of the flight.
        public void FaceLaunchDirection(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.0001f) return;

            launchFacingYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            if (visual != null)
            {
                visual.localRotation = Quaternion.Euler(0f, launchFacingYaw, 0f);
            }
        }

        // Called by KineticCubeController.OnCollisionEnter the instant a crash sticks the cube -
        // direct request: "the cubes surface should align with the surface it just hit, so they
        // are parallel". Aligning local up to the surface's own outward normal reproduces the
        // IDENTICAL rotation the cube already has resting on ordinary flat ground (whose normal
        // IS world up), so this is a no-op there and only visibly kicks in for walls/ceilings/
        // ramps. Instant snap, not eased through the lean Slerp, same reasoning as
        // FaceLaunchDirection above - and it needs no explicit reset: FixedUpdate returns early
        // (skipping the Slerp entirely) for the whole time AllowGroundedMovement/
        // AllowAirborneNudge are both false, which is exactly the isStuck window, so this holds
        // untouched until the next launch calls FaceLaunchDirection and naturally overwrites it.
        public void AlignVisualToSurface(Vector3 surfaceNormal)
        {
            if (visual == null) return;
            visual.localRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
        }

        // Same BoxCast-across-the-footprint approach as KineticCubeController.FixedUpdate, and
        // for the same reason: a single center ray can miss when the cube is resting right at a
        // platform's edge.
        void UpdateGrounded()
        {
            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out RaycastHit hit, transform.rotation, groundCheckDistance);

            // Moving platform underfoot? Its velocity becomes the rider's base velocity.
            MovingPlatform platform = isGrounded && hit.collider != null && hit.collider.attachedRigidbody != null
                ? hit.collider.attachedRigidbody.GetComponent<MovingPlatform>()
                : null;
            GroundPlatformVelocity = platform != null ? platform.CurrentVelocity : Vector3.zero;
        }

        Vector3 CameraRelativeForward()
        {
            if (cameraTransform == null) return Vector3.forward;
            Vector3 f = cameraTransform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        Vector3 CameraRelativeRight()
        {
            if (cameraTransform == null) return Vector3.right;
            Vector3 r = cameraTransform.right;
            r.y = 0f;
            return r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
        }
    }
}
