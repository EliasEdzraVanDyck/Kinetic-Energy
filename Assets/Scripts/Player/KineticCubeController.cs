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

        [Header("Input")]
        public InputActionReference moveAction;
        public InputActionReference launchAction;
        public InputActionReference fireAction;

        Rigidbody rb;
        bool isAiming;
        bool waitingForLtRelease;
        float chargeTime;
        float aimYaw;
        float aimPitch;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            moveAction?.action?.Enable();
            launchAction?.action?.Enable();
            fireAction?.action?.Enable();
        }

        void OnDisable()
        {
            moveAction?.action?.Disable();
            launchAction?.action?.Disable();
            fireAction?.action?.Disable();
        }

        void Update()
        {
            // Time.timeScale freezes deltaTime-scaled logic (like charge accumulation) for free,
            // but not this raw edge-detected input - without this guard, aiming/firing could
            // still start or complete while the pause menu is up.
            if (Time.timeScale <= 0f) return;

            bool ltHeld = launchAction != null && launchAction.action != null && launchAction.action.IsPressed();

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
                aimArrow?.SetAim(dir, ChargeFraction());

                bool rtPressed = fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();
                if (rtPressed)
                {
                    queuedDirection = dir;
                    queuedForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, ChargeFraction());
                    launchQueued = true;

                    isAiming = false;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    waitingForLtRelease = true;
                }
            }
            else if (isAiming)
            {
                // LT released without firing - cancel, no launch.
                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
            }
        }

        void FixedUpdate()
        {
            if (launchQueued)
            {
                launchQueued = false;
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);
            }
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
