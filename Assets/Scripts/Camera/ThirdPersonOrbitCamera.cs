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
        public float minPitch = -20f;
        public float maxPitch = 60f;
        public bool invertY = false;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.08f;

        [Header("Input")]
        public InputActionReference lookAction;

        float yaw;
        float pitch = 15f;
        Vector3 velocity;

        void Start()
        {
            if (target == null) return;

            Vector3 offset = transform.position - (target.position + Vector3.up * height);
            if (offset.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
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

            Vector2 look = lookAction != null && lookAction.action != null
                ? lookAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            yaw += look.x * rotationSpeed * Time.deltaTime;
            float pitchDelta = (invertY ? look.y : -look.y) * rotationSpeed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;
            Vector3 desiredPosition = focusPoint - rotation * Vector3.forward * distance;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, positionSmoothTime);
            transform.rotation = rotation;
        }
    }
}
