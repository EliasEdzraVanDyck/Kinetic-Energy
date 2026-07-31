using UnityEngine;
using UnityEngine.InputSystem;

namespace KineticEnergy.Player
{
    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeController : MonoBehaviour
    {
        [Header("Launch Force")]
        public float minLaunchForce = 8f;
        public float maxLaunchForce = 40f;
        public float maxChargeTime = 1.5f;

        [Header("Aiming")]
        [Range(0f, 1f)] public float aimDeadzone = 0.15f;
        public float aimRotationSpeed = 90f;
        public float minAimPitch = -80f;
        public float maxAimPitch = 80f;
        public Transform cameraTransform;
        public AimArrowIndicator aimArrow;

        [Header("Landing")]
        [Range(0f, 1f)] public float groundNormalDot = 0.5f;
        public float groundLevel = 0f;
        public int maxPredictionSteps = 3000;
        public float previewLineHeight = 0.65f;
        public float restVelocityThreshold = 0.05f;
        public float groundCheckDistance = 0.6f;
        public LandingPreviewController landingPreview;

        [Header("Input")]
        public InputActionReference moveAction;
        public InputActionReference launchAction;
        public InputActionReference fireAction;
        public InputActionReference selectGhostAction;
        public InputActionReference selectTrailAction;
        public InputActionReference selectCrosshairAction;
        public InputActionReference selectNoneAction;

        Rigidbody rb;
        bool isAiming;
        bool waitingForLtRelease;
        bool hasLaunched;
        bool isGrounded;
        float chargeTime;
        float aimYaw;
        float aimPitch;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;

        Vector3[] trajectoryBuffer;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            trajectoryBuffer = new Vector3[Mathf.Max(maxPredictionSteps, 1)];
        }

        void OnEnable()
        {
            moveAction?.action?.Enable();
            launchAction?.action?.Enable();
            fireAction?.action?.Enable();
            selectGhostAction?.action?.Enable();
            selectTrailAction?.action?.Enable();
            selectCrosshairAction?.action?.Enable();
            selectNoneAction?.action?.Enable();
        }

        void OnDisable()
        {
            moveAction?.action?.Disable();
            launchAction?.action?.Disable();
            fireAction?.action?.Disable();
            selectGhostAction?.action?.Disable();
            selectTrailAction?.action?.Disable();
            selectCrosshairAction?.action?.Disable();
            selectNoneAction?.action?.Disable();
        }

        void Update()
        {
            // Time.timeScale freezes deltaTime-scaled logic (like charge accumulation) for free,
            // but not this raw edge-detected input - without this guard, aiming/firing could
            // still start or complete while the pause menu is up.
            if (Time.timeScale <= 0f) return;

            HandlePreviewModeSwitch();

            // Only one launch allowed per landing (hasLaunched), AND launching only ever starts
            // from a currently-grounded state (isGrounded, the same real-time raycast check
            // FixedUpdate uses) - checking both directly here, rather than trusting hasLaunched
            // alone to have been reset at the right moment, is what actually guarantees you can
            // never begin aiming/firing while airborne.
            bool ltHeld = !hasLaunched && isGrounded && launchAction != null && launchAction.action != null && launchAction.action.IsPressed();

            // One-shot-per-hold: once a launch fires, LT must be fully released before it can gate another.
            if (waitingForLtRelease)
            {
                if (!ltHeld) waitingForLtRelease = false;
                return;
            }

            if (ltHeld)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;
                    SeedAimFromCamera();
                    aimArrow?.SetVisible(true);
                    landingPreview?.SetVisible(true);
                }

                chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);

                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;

                if (stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {
                    aimYaw = Mathf.Repeat(aimYaw + stick.x * aimRotationSpeed * Time.deltaTime, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - stick.y * aimRotationSpeed * Time.deltaTime, minAimPitch, maxAimPitch);
                }

                Vector3 dir = AimDirection();
                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(dir, chargeFraction);

                float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    Vector3 initialVelocity = rb.linearVelocity + dir * previewForce / rb.mass;
                    Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                    Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, out int stepCount);
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount);
                }

                bool rtPressed = fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();
                if (rtPressed)
                {
                    queuedDirection = dir;
                    queuedForce = previewForce;
                    launchQueued = true;
                    hasLaunched = true;

                    isAiming = false;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);
                    waitingForLtRelease = true;
                }
            }
            else if (isAiming)
            {
                // LT released without firing - cancel, no launch.
                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }
        }

        void FixedUpdate()
        {
            if (launchQueued)
            {
                launchQueued = false;
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);
            }

            // Grounded state comes from a direct downward check each step, not accumulated
            // OnCollisionEnter/Stay/Exit state - Continuous collision detection (needed so a fast
            // launch can't tunnel through the floor) can keep reporting contact slightly after the
            // cube has genuinely left the ground, which was letting hasLaunched clear near a lob
            // shot's apex (low velocity, stale "grounded") and allowing a mid-air relaunch. A fresh
            // raycast has no such lag: it's simply true or false for exactly this instant.
            isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);

            // Re-arm the single launch once the cube has actually come to rest on the ground.
            // Grounded alone isn't enough (a launch fired while already touching the floor never
            // triggers a fresh OnCollisionEnter, since contact was never broken - it just slides
            // to a stop via drag/friction), so this also waits for velocity to settle rather than
            // relying only on the hard OnCollisionEnter stop below.
            if (hasLaunched && isGrounded && rb.linearVelocity.sqrMagnitude < restVelocityThreshold * restVelocityThreshold)
            {
                hasLaunched = false;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsGroundContact(collision)) return;

            // Stop dead the instant it lands - only on a roughly-upward contact normal, so this
            // reads as "touched the ground" and not "bumped into a wall".
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        bool IsGroundContact(Collision collision)
        {
            foreach (ContactPoint contact in collision.contacts)
            {
                if (Vector3.Dot(contact.normal, Vector3.up) > groundNormalDot) return true;
            }
            return false;
        }

        void HandlePreviewModeSwitch()
        {
            if (landingPreview == null) return;

            if (selectGhostAction != null && selectGhostAction.action != null && selectGhostAction.action.WasPressedThisFrame())
            {
                landingPreview.SetMode(PredictionMode.Ghost);
            }
            else if (selectTrailAction != null && selectTrailAction.action != null && selectTrailAction.action.WasPressedThisFrame())
            {
                landingPreview.SetMode(PredictionMode.Trail);
            }
            else if (selectCrosshairAction != null && selectCrosshairAction.action != null && selectCrosshairAction.action.WasPressedThisFrame())
            {
                landingPreview.SetMode(PredictionMode.Crosshair);
            }
            else if (selectNoneAction != null && selectNoneAction.action != null && selectNoneAction.action.WasPressedThisFrame())
            {
                landingPreview.SetMode(PredictionMode.None);
            }
        }

        // Steps the same physics the Rigidbody actually integrates (gravity + Unity's linear
        // damping formula) until the simulated position crosses groundLevel, so the preview
        // matches where the cube will really stop (see OnCollisionEnter above). Records every
        // simulated position into trajectoryBuffer (preallocated in Awake - a fixed reusable
        // buffer avoids a GC allocation every frame while aiming) so the trail preview can
        // follow the real arc instead of a straight line.
        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, out int stepCount)
        {
            Vector3 pos = startPos;
            Vector3 vel = initialVelocity;
            float dt = Time.fixedDeltaTime;
            Vector3 gravity = Physics.gravity;
            float drag = rb.linearDamping;
            stepCount = 0;

            for (int i = 0; i < maxPredictionSteps; i++)
            {
                vel += gravity * dt;
                vel *= 1f / (1f + drag * dt);
                Vector3 nextPos = pos + vel * dt;

                if (nextPos.y <= groundLevel && pos.y > groundLevel)
                {
                    float t = (pos.y - groundLevel) / (pos.y - nextPos.y);
                    Vector3 landing = Vector3.Lerp(pos, nextPos, t);
                    if (stepCount < trajectoryBuffer.Length) trajectoryBuffer[stepCount++] = landing;
                    return landing;
                }

                pos = nextPos;
                if (stepCount < trajectoryBuffer.Length) trajectoryBuffer[stepCount++] = pos;
            }

            // Guaranteed fallback - not a raycast, so there's no "max distance" to raise, but the
            // equivalent fix: never let the preview end up floating above the ground. If gravity
            // hasn't pulled it back down to groundLevel within maxPredictionSteps (a very strong,
            // steep shot - stacking repeated mid-air launches can push initial velocity well past
            // what a single max-charge launch alone would reach), snap the last simulated XZ onto
            // the ground plane instead of returning a still-airborne position.
            Vector3 fallback = new Vector3(pos.x, groundLevel, pos.z);
            if (stepCount < trajectoryBuffer.Length) trajectoryBuffer[stepCount++] = fallback;
            return fallback;
        }

        float ChargeFraction()
        {
            return maxChargeTime > 0f ? Mathf.Clamp01(chargeTime / maxChargeTime) : 1f;
        }

        Vector3 AimDirection()
        {
            return Quaternion.Euler(aimPitch, aimYaw, 0f) * Vector3.forward;
        }

        void SeedAimFromCamera()
        {
            if (cameraTransform == null)
            {
                aimYaw = 0f;
                aimPitch = 0f;
                return;
            }

            Vector3 euler = cameraTransform.eulerAngles;
            aimYaw = euler.y;
            float rawPitch = euler.x > 180f ? euler.x - 360f : euler.x;
            aimPitch = Mathf.Clamp(rawPitch, minAimPitch, maxAimPitch);
        }
    }
}
