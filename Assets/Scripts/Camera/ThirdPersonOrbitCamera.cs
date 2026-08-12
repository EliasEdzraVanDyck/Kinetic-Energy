using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KineticEnergy.Camera
{
    public class ThirdPersonOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        public float height = 2.5f;

        [Header("Orbit")]
        public float distance = 6f;
        public float rotationSpeed = 120f;
        // Raised in magnitude from -20 - direct request: "allow the camera to be pointed upwards
        // as well, for the vertical segment it was hard not being able to see what was above me".
        // Negative pitch swings the camera BELOW the target, looking UP (see the position formula
        // below: desiredPosition = focusPoint - rotation*forward*distance, and positive pitch
        // tilts forward toward -Y, so negative pitch tilts it toward +Y, putting the camera
        // underneath) - -20 only allowed a shallow 20-degree glance upward, nowhere near enough to
        // see a platform directly overhead. -75 mirrors maxPitch's own margin from the 90-degree
        // gimbal-adjacent instability described below, applied to the opposite pole.
        public float minPitch = -75f;
        // Pulled back from an earlier 89 - that was close enough to true top-down (90) that
        // Quaternion.LookRotation(lookDir, Vector3.up) started reading as visibly "spinning"
        // while rotating: as lookDir approaches anti-parallel to the Vector3.up hint, tiny
        // positional differences (SmoothDamp lag makes the ACTUAL camera position lag behind the
        // theoretical orbit spot, so this isn't just the exact 90-degree instant) flip which way
        // LookRotation resolves roll, which reads as spin. 75 keeps a real margin from that
        // degenerate zone while still allowing a dramatically steep, near-top-down view.
        public float maxPitch = 75f;
        public bool invertY = false;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.08f;
        [Tooltip("Position smoothing while a launch is in flight - larger than positionSmoothTime, so the camera briefly trails the launch and then catches up.")]
        public float launchFollowSmoothTime = 0.25f;
        [Tooltip("Same trailing effect for VERTICAL launches (up-charge and ground pound) - slightly tighter, so the camera hangs back a touch less on straight up/down flights.")]
        public float verticalLaunchFollowSmoothTime = 0.18f;
        [Tooltip("Seconds over which the follow relaxes back to normal tightness after a flight ends - stops the camera from lunging at the player the instant a launch lands.")]
        public float followLagRecoverySeconds = 0.5f;
        public float maxDeltaTime = 0.05f;

        float followSmoothTime; // the eased, currently-active follow smoothing

        // Set per frame by KineticCubeController - true for the whole launch flight, so the
        // orbit follow uses the lazier launch smoothing while the cube rockets away.
        // Vertical flights (up-charge / pound) use their own slightly tighter value.
        bool launchInFlight;
        bool launchIsVertical;

        public void SetLaunchInFlight(bool inFlight, bool vertical)
        {
            launchInFlight = inFlight;
            launchIsVertical = vertical;
        }

        [Header("Auto Recenter")]
        // Used by forward hold-charge launches to swing the camera back behind the player
        // after firing. Cancels itself the instant the player provides any manual look input,
        // so it never fights the player's own camera control.
        public float recenterSpeed = 240f;

        [Header("Input")]
        public InputActionReference lookAction;

        // While the midair aim is open the LEFT stick steers the camera (the right stick is
        // the energy dial there): any non-mouse look input is substituted by the stick value
        // fed in here each frame. Mouse aiming is deliberately unaffected - the substitution
        // only applies when the look action isn't mouse-driven.
        bool aimStickOverrideActive;
        Vector2 aimStickOverrideValue;
        // While the grounded aim's "Aim: Mouse" option is steering the launch direction with
        // mouse delta (KineticCubeController.groundedAimWithMouse), mouse-driven look input is
        // ignored so one hand motion doesn't rotate the camera and the aim arrow together.
        bool mouseLookSuppressed;

        public void SetAimStickOverride(bool active, Vector2 stick)
        {
            aimStickOverrideActive = active;
            aimStickOverrideValue = active ? stick : Vector2.zero;
        }

        public void SetMouseLookSuppressed(bool suppressed)
        {
            mouseLookSuppressed = suppressed;
        }

        // EnergyEconomy1's straight-up/ground-pound charges: the camera keeps FULL speed while
        // the game runs slow ("the camera should not be bound to the gamespeed" - direct
        // request) - set per frame by KineticCubeController.
        bool ignoreSlowMo;

        public void SetIgnoreSlowMo(bool ignore)
        {
            ignoreSlowMo = ignore;
        }

        // While a launch is aimed MIDAIR the orbit frames the TRAJECTORY instead of the player
        // (direct request: "the visual line should be in the middle of the screen") - the focus
        // point becomes the line's midpoint outright, so the line is genuinely centered and the
        // player simply falls out of frame on a long arc, which is the intent. Distance from
        // First person sits at the centre of the player's FRONT FACE, pushed this far further
        // along the view direction so the cube itself is never in shot (direct request,
        // replacing the look-at-the-landing-point framing, which whipped around whenever the
        // predicted landing jumped). The cube is 1 unit across, so 0.5 reaches its front face
        // and the rest is clearance.
        public float firstPersonForwardOffset = 0.75f;
        // Position smoothing used for the frame(s) right after a first-person <-> third-person
        // switch (direct request: the change should be near instant). Ordinary movement keeps
        // positionSmoothTime; only the mode change uses this much snappier value.
        public float modeSwitchSmoothTime = 0.02f;

        bool modeSwitching;

        // First-person aim looks AT the predicted landing point (the cursor at the end of the
        // dotted line) rather than along the raw launch ray - the arc drops under gravity, so
        // the two differ. Snapped into place the instant aiming starts, then eased whenever
        // the landing point MOVES (new target, changed energy), which is what stops the
        // violent whipping when a target jumps (direct request).
        public float framingTurnSpeed = 300f;
        // How close (degrees, per axis) the cursor must be to the AIM for the view to centre
        // on it. Past this the view stays glued to the aim - never pulled partway - so a
        // steeply-up shot, which arcs over and lands far BELOW you, can't drag the view down
        // and read as an upward pitch cap (direct report, twice).
        public float framingMaxDeviation = 45f;

        bool framingActive;
        Vector3 framingPoint;
        bool framingJustStarted;

        // The first-person VIEW's own yaw/pitch, eased toward the framing target. Kept as
        // separate angles (not a quaternion slerp) on purpose: interpolating between two
        // level rotations along the shortest quaternion arc rolls the horizon mid-way, which
        // read as the camera's Z rotation changing while moving between two targets. Building
        // the rotation from yaw/pitch alone keeps roll at exactly zero on every frame.
        float viewYaw;
        float viewPitch;
        bool viewAnglesSeeded;

        public void SetTrajectoryFraming(bool active, Vector3 worldPoint)
        {
            if (active && !framingActive) framingJustStarted = true;
            framingActive = active;
            framingPoint = worldPoint;
        }

        [Header("Fine Aim")]
        // "Slow down the speed of aiming if you make fine adjustments with your mouse or stick,
        // if you make wider less fine movements, the speed should be the same as now" (direct
        // request) - rotation speed scales with how hard the input is being pushed, from
        // fineAimMinFactor at a barely-moving input up to full speed at/beyond the reference
        // magnitude. The reference is per-device because the two input types live on completely
        // different scales: a stick deflection maxes out around 1.0, while a mouse delta is
        // pixels-per-frame and routinely reads 10+ during a fast sweep - a single shared
        // threshold would either make the stick never reach full speed or make every mouse
        // movement count as "wide".
        [Range(0f, 1f)] public float fineAimMinFactor = 0.3f;
        public float fineAimStickReference = 0.9f;
        public float fineAimMouseReference = 8f;

        [Header("First Person Aim")]
        // FastPaced scheme only (see KineticCubeController.UpdateFastPacedScheme) -
        // SetFirstPersonMode collapses the orbit to sit exactly at the focus point instead of
        // orbiting at `distance`, and SetAimZoom narrows the field of view as charge builds so a
        // long-charged shot's distant landing spot stays legible instead of shrinking to a speck
        // - direct request: "the longer you charge the more you need to zoom in on the landing
        // spot". Both are no-ops for every other scheme, which never calls them.
        public float normalFov = 60f;
        public float maxZoomFov = 20f;
        // Pitch limits while first person is active - near-vertical is SAFE there (first person
        // applies the raw rotation directly, none of the LookRotation-at-target degeneracy the
        // +/-75 orbit limits guard against), and the midair aim needs to look almost straight
        // down to line up pounds.
        public float firstPersonMinPitch = -89f;
        public float firstPersonMaxPitch = 89f;

        UnityEngine.Camera cam;
        bool firstPerson;

        // World-space direction this camera is currently looking - the midair aim fires
        // exactly along this, so the shot always goes exactly where the first-person view points.
        public Vector3 AimForward => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        // Called the instant a MIDAIR launch fires: the camera jumps straight to its third-
        // person orbit slot behind the player and follows from there. Without this the
        // camera exits first person AT the player - the launch simply flies past it and the
        // catch-up reads as instant, no matter the smooth time. Starting from the orbit slot
        // makes the launch trailing develop exactly like a grounded launch's.
        public void SnapToThirdPersonOrbit()
        {
            firstPerson = false;
            modeSwitching = false;
            if (target == null) return;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;
            transform.position = focusPoint - rotation * Vector3.forward * distance;
            velocity = Vector3.zero;

            Vector3 lookDir = focusPoint - transform.position;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }

        public void SetFirstPersonMode(bool enabled)
        {
            if (firstPerson != enabled) modeSwitching = true; // near-instant transition, see modeSwitchSmoothTime
            firstPerson = enabled;
            if (!enabled)
            {
                SetAimZoom(0f);
                viewAnglesSeeded = false;
            }
        }

        public void SetAimZoom(float chargeFraction01)
        {
            if (cam == null) cam = GetComponent<UnityEngine.Camera>();
            if (cam == null) return;
            cam.fieldOfView = Mathf.Lerp(normalFov, maxZoomFov, Mathf.Clamp01(chargeFraction01));
        }

        [Header("Wall Occlusion")]
        // "If the camera is looking at a wall from the outside, you should be able to look
        // through the wall" (direct request - commonly called camera occlusion culling). There
        // was no camera-collision handling here at all before this - a wall ending up between
        // the orbit position and the player (e.g. the camera orbits to just outside one of
        // Level2's hallway walls) just blocked the view outright. Hides (doesn't fade) whichever
        // renderers are directly between the camera and the player each frame, restoring them
        // the instant they're no longer in the way.
        public LayerMask occlusionMask = ~0;
        public float occlusionCheckRadius = 0.25f;

        float yaw;
        float pitch = 15f;
        Vector3 velocity;
        bool yawInitialized;
        bool recentering;
        float recenterTargetYaw;

        readonly List<Renderer> occludedRenderers = new List<Renderer>();
        readonly List<Renderer> stillOccludedThisFrame = new List<Renderer>();

        void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
            if (cam != null) cam.fieldOfView = normalFov;
            followSmoothTime = positionSmoothTime;
        }

        void Start()
        {
            if (target == null) return;
            if (yawInitialized) return; // already set externally (CameraStartFacing) before this ran

            Vector3 offset = transform.position - (target.position + Vector3.up * height);
            if (offset.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
        }

        // Called from CameraStartFacing.Awake() - guaranteed to run before this component's own
        // Start() (Unity runs every Awake() in the scene before any Start()), so it always wins
        // over the offset-based auto-calculation above. Also snaps position immediately rather
        // than letting LateUpdate's SmoothDamp ease into the new orbit spot over a few frames,
        // so the camera is already correctly framed on the very first rendered frame instead of
        // visibly sliding into place right as the level appears.
        public void SetInitialYaw(float yawDegrees)
        {
            yaw = yawDegrees;
            yawInitialized = true;

            if (target == null) return;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;
            transform.position = focusPoint - rotation * Vector3.forward * distance;

            Vector3 lookDir = focusPoint - transform.position;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }

        // Starts a smooth (not instant) swing of the orbit yaw back to directly behind
        // targetYawDegrees (the player's new facing) - "move behind the player again", not
        // "snap" (that's what SetInitialYaw is for, at level load). Actual interpolation happens
        // in LateUpdate so it can be interrupted cleanly by manual look input at any point.
        public void RecenterBehindTarget(float targetYawDegrees)
        {
            recenterTargetYaw = targetYawDegrees;
            recentering = true;
        }

        void OnEnable()
        {
            lookAction?.action?.Enable();
        }

        void OnDisable()
        {
            lookAction?.action?.Disable();
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (Time.timeScale <= 0f) return;

            Vector2 look = lookAction != null && lookAction.action != null
                ? lookAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            bool lookIsMouseDriven = lookAction != null && lookAction.action != null
                && lookAction.action.activeControl != null
                && lookAction.action.activeControl.device is Mouse;

            // Left-stick aim substitution (see SetAimStickOverride) - replaces stick-driven
            // look input only; a mouse-driven frame keeps the mouse delta untouched.
            if (aimStickOverrideActive && !lookIsMouseDriven) look = aimStickOverrideValue;

            // Mouse-driven look is dropped while the mouse is busy steering the grounded aim -
            // see SetMouseLookSuppressed.
            if (mouseLookSuppressed && lookIsMouseDriven) look = Vector2.zero;

            // Unscaled, not Time.deltaTime - Time.deltaTime already shrinks 1:1 with
            // Time.timeScale, which would otherwise make the camera merely ride along with
            // whatever fraction chargeTimeScale happens to be. Instead the camera runs at a
            // flat HALF speed whenever the game is running slow. Strictly < 1, not != 1 - a
            // launch in flight speeds the game UP (KineticCubeController.launchFlightTimeScale)
            // and camera/aiming must be unaffected by that, which unscaled time gives for free.
            //
            // The frame right after a scene reload (Restart, or the new fall-reset) can have an
            // abnormally large deltaTime - loading everything (Player/Camera/PauseSystem, plus
            // Level1's platform generation) takes real time before the next frame renders.
            // Multiplied straight into this accumulator, holding the stick at that exact moment
            // (plausible right after falling or hitting Restart) could snap yaw/pitch to a
            // garbage value in one frame, making the camera look broken/unresponsive afterward -
            // still a risk with unscaled time, so the clamp stays.
            bool gameRunningSlow = Time.timeScale < 1f && !ignoreSlowMo;
            float dt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime) * (gameRunningSlow ? 0.5f : 1f);

            // Fine-aim scaling (see the Fine Aim header comment): a gentle input rotates at
            // fineAimMinFactor of normal speed, ramping linearly up to full speed at the active
            // device's reference magnitude. The device is read from whichever control is
            // actually driving the action THIS frame, so switching between mouse and gamepad
            // mid-session picks the right scale automatically.
            float fineAimScale = 1f;
            if (look.sqrMagnitude > 0.0001f)
            {
                bool mouseDriven = lookAction != null && lookAction.action != null
                    && lookAction.action.activeControl != null
                    && lookAction.action.activeControl.device is Mouse;
                float reference = mouseDriven ? fineAimMouseReference : fineAimStickReference;
                float t = reference > 0.0001f ? Mathf.Clamp01(look.magnitude / reference) : 1f;
                fineAimScale = Mathf.Lerp(fineAimMinFactor, 1f, t);
            }

            // Manual input always wins outright, the instant there is any - recentering only
            // ever happens while the player isn't already telling the camera what to do.
            if (look.sqrMagnitude > 0.0001f) recentering = false;

            if (recentering)
            {
                yaw = Mathf.MoveTowardsAngle(yaw, recenterTargetYaw, recenterSpeed * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, recenterTargetYaw)) < 0.5f) recentering = false;
            }
            else
            {
                yaw += look.x * rotationSpeed * fineAimScale * dt;
            }
            float pitchDelta = (invertY ? look.y : -look.y) * rotationSpeed * fineAimScale * dt;
            pitch = Mathf.Clamp(pitch + pitchDelta,
                firstPerson ? firstPersonMinPitch : minPitch,
                firstPerson ? firstPersonMaxPitch : maxPitch);

            // Traditional 3rd-person platformer orbit: position swings around the target on
            // both yaw and pitch, always framing it, rather than tilting/panning in place.
            // firstPerson (the midair aim's mode - see SetFirstPersonMode) collapses this to
            // sit exactly at the focus point instead of orbiting at `distance`, using the raw
            // look rotation directly rather than LookRotation-at-target (which degenerates at
            // zero distance, where focusPoint - transform.position is ~zero and has no reliable
            // direction). Reuses the same SmoothDamp position glide either way, so switching in
            // or out of first person eases smoothly rather than snapping.
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;

            // First person: the player's own centre pushed forward past its front face - NOT
            // the focus point, which carries the third-person `height` lift and was what put
            // the first-person view up at a raised Y. Third person keeps its ordinary orbit.
            Vector3 desiredPosition = firstPerson
                ? target.position + rotation * Vector3.forward * firstPersonForwardOffset
                : focusPoint - rotation * Vector3.forward * distance;

            // A mode switch uses the much shorter smooth time until the camera has essentially
            // arrived - so first <-> third person reads as a snap without the hard teleport
            // (and without the leftover SmoothDamp velocity that a teleport would keep).
            // Priority: an in-flight launch's lazy trailing beats everything (including the
            // mode-switch snap - firing out of the midair aim IS a mode switch, and the snap
            // was eating the launch lag there); the snap still covers aim open/cancel.
            // Engaging the lag is INSTANT (the launch moment should trail immediately), but
            // releasing it EASES over followLagRecoverySeconds - snapping straight back to
            // the tight follow made the camera lunge at the player the frame a flight ended.
            float activeLaunchFollow = launchIsVertical ? verticalLaunchFollowSmoothTime : launchFollowSmoothTime;
            float targetFollow = launchInFlight && !firstPerson ? activeLaunchFollow : positionSmoothTime;
            if (targetFollow > followSmoothTime)
            {
                followSmoothTime = targetFollow;
            }
            else
            {
                float relaxRate = (launchFollowSmoothTime - positionSmoothTime) / Mathf.Max(followLagRecoverySeconds, 0.01f);
                followSmoothTime = Mathf.MoveTowards(followSmoothTime, targetFollow, relaxRate * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
            }

            float smoothTime;
            if (launchInFlight && !firstPerson) smoothTime = followSmoothTime;
            else if (modeSwitching) smoothTime = modeSwitchSmoothTime;
            else smoothTime = followSmoothTime;
            // Explicit UNSCALED delta time: SmoothDamp's default is Time.deltaTime, which the
            // in-flight game-speed-up inflates 2-3x - the camera was catching up that much
            // faster than the smooth time promised, which read as a near-instant snap on
            // midair launches. Real-seconds smoothing keeps the trailing consistent at any
            // game speed (slow-mo included, matching how the rotation input already works).
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime,
                Mathf.Infinity, Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
            if (modeSwitching && (transform.position - desiredPosition).sqrMagnitude < 0.0025f) modeSwitching = false;

            if (firstPerson)
            {
                // The view target in yaw/pitch: the aim itself, or the cursor when it's
                // CLOSE to the aim. Centering only engages while the cursor sits within
                // framingMaxDeviation of the aim on both axes - a cursor further off (an
                // up-aimed shot always lands far BELOW the aim) no longer drags the view at
                // all, so a steep upward aim shows exactly where you're aiming instead of
                // reading as a pitch cap. Never pulled partway: it's the cursor or the aim.
                float targetYaw = yaw;
                float targetPitch = pitch;
                Vector3 framingDir = framingPoint - transform.position;
                if (framingActive && framingDir.sqrMagnitude > 0.0001f)
                {
                    float framingYaw = Mathf.Atan2(framingDir.x, framingDir.z) * Mathf.Rad2Deg;
                    float framingPitch = -Mathf.Asin(Mathf.Clamp(framingDir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float framingYawDelta = Mathf.DeltaAngle(yaw, framingYaw);
                    float framingPitchDelta = framingPitch - pitch;
                    if (Mathf.Abs(framingYawDelta) <= framingMaxDeviation && Mathf.Abs(framingPitchDelta) <= framingMaxDeviation)
                    {
                        targetYaw = yaw + framingYawDelta;
                        targetPitch = pitch + framingPitchDelta;
                    }
                }

                if (framingJustStarted || !viewAnglesSeeded)
                {
                    // Aiming just began - land on the cursor immediately.
                    viewYaw = targetYaw;
                    viewPitch = targetPitch;
                    viewAnglesSeeded = true;
                    framingJustStarted = false;
                }
                else
                {
                    // The landing point moved (retarget, energy change) - ease across at a
                    // capped rate, per axis. Unscaled so it feels the same during the aim's
                    // bullet-time.
                    float turnStep = framingTurnSpeed * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);
                    viewYaw = Mathf.MoveTowardsAngle(viewYaw, targetYaw, turnStep);
                    viewPitch = Mathf.MoveTowards(viewPitch, targetPitch, turnStep);
                }

                // Built from yaw/pitch alone - the roll (Z) component is always exactly zero.
                transform.rotation = Quaternion.Euler(viewPitch, viewYaw, 0f);
            }
            else
            {
                // Look directly at the target from wherever the camera ACTUALLY is, rather than
                // reusing the theoretical orbit rotation - position lags behind via SmoothDamp, so
                // during fast stick movement the two used to disagree and the camera briefly didn't
                // point exactly at the player.
                Vector3 lookDir = focusPoint - transform.position;
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
            }

            // Always called, even in first person - UpdateWallOcclusion's own distance check
            // already no-ops the sphere-cast when the camera sits ~on top of the target (exactly
            // the first-person case), but the loop that RESTORES a renderer hidden just before
            // switching into first person still needs to run every frame, or a wall occluded the
            // instant before RMB was pressed would stay disabled for the entire aim.
            UpdateWallOcclusion(focusPoint);
        }

        // Hides any renderer whose collider sits directly between the camera and the player,
        // restoring it the instant it no longer does - see occlusionMask's own comment.
        void UpdateWallOcclusion(Vector3 focusPoint)
        {
            stillOccludedThisFrame.Clear();

            Vector3 origin = transform.position;
            Vector3 toTarget = focusPoint - origin;
            float distance = toTarget.magnitude;

            if (distance > 0.0001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(origin, occlusionCheckRadius, toTarget / distance, distance, occlusionMask, QueryTriggerInteraction.Ignore);
                foreach (RaycastHit hit in hits)
                {
                    // Only interested in geometry actually BETWEEN the camera and the player -
                    // never hide the player's own collider/visual.
                    if (target != null && (hit.collider.transform == target || hit.collider.transform.IsChildOf(target))) continue;

                    Renderer hitRenderer = hit.collider.GetComponent<Renderer>();
                    if (hitRenderer == null) continue;

                    stillOccludedThisFrame.Add(hitRenderer);
                    if (!occludedRenderers.Contains(hitRenderer))
                    {
                        hitRenderer.enabled = false;
                        occludedRenderers.Add(hitRenderer);
                    }
                }
            }

            for (int i = occludedRenderers.Count - 1; i >= 0; i--)
            {
                if (stillOccludedThisFrame.Contains(occludedRenderers[i])) continue;

                if (occludedRenderers[i] != null) occludedRenderers[i].enabled = true;
                occludedRenderers.RemoveAt(i);
            }
        }
    }
}
