using UnityEngine;
using UnityEngine.InputSystem;

namespace KineticEnergy.Player
{
    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeController : MonoBehaviour
    {
        [Header("Launch Force")]
        public float minLaunchForce = 4f;
        public float maxLaunchForce = 20f;
        public float maxChargeTime = 1.5f;

        [Header("Aiming")]
        [Range(0f, 1f)] public float aimDeadzone = 0.15f;
        public Transform cameraTransform;
        public AimArrowIndicator aimArrow;

        [Header("Input")]
        public InputActionReference moveAction;
        public InputActionReference launchAction;

        Rigidbody rb;
        bool isAiming;
        float chargeTime;
        Vector3 aimDirection = Vector3.forward;

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
        }

        void OnDisable()
        {
            moveAction?.action?.Disable();
            launchAction?.action?.Disable();
        }

        void Update()
        {
            bool held = launchAction != null && launchAction.action != null && launchAction.action.IsPressed();

            if (held)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;
                    aimDirection = FlattenedForward(cameraTransform);
                    aimArrow?.SetVisible(true);
                }

                chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);

                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;

                if (stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {
                    aimDirection = CameraRelativeDirection(stick);
                }

                aimArrow?.SetAim(aimDirection, ChargeFraction());
            }
            else if (isAiming)
            {
                isAiming = false;
                queuedDirection = aimDirection;
                queuedForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, ChargeFraction());
                launchQueued = true;
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

        Vector3 CameraRelativeDirection(Vector2 stick)
        {
            Vector3 forward = FlattenedForward(cameraTransform);
            Vector3 right = FlattenedRight(cameraTransform);
            Vector3 dir = forward * stick.y + right * stick.x;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : aimDirection;
        }

        static Vector3 FlattenedForward(Transform cam)
        {
            if (cam == null) return Vector3.forward;
            Vector3 f = cam.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        static Vector3 FlattenedRight(Transform cam)
        {
            if (cam == null) return Vector3.right;
            Vector3 r = cam.right;
            r.y = 0f;
            return r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
        }
    }
}
