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
        public float maxDeltaTime = 0.05f;

        [Header("Input")]
        public InputActionReference lookAction;

        float yaw;
        float pitch = 15f;
        Vector3 velocity;
        bool yawInitialized;

        void Start()
        {
            if (target == null) return;
            if (yawInitialized) return; // already set externally (e.g. LevelGenerator facing the finish) before this ran

            Vector3 offset = transform.position - (target.position + Vector3.up * height);
            if (offset.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
        }

        // Called from LevelGenerator.Awake() - guaranteed to run before this component's own
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

            // The frame right after a scene reload (Restart, or the new fall-reset) can have an
            // abnormally large Time.deltaTime - loading everything (Player/Camera/PauseSystem,
            // plus Level1's platform generation) takes real time before the next frame renders.
            // Multiplied straight into this accumulator, holding the stick at that exact moment
            // (plausible right after falling or hitting Restart) could snap yaw/pitch to a
            // garbage value in one frame, making the camera look broken/unresponsive afterward.
            float dt = Mathf.Min(Time.deltaTime, maxDeltaTime);

            yaw += look.x * rotationSpeed * dt;
            float pitchDelta = (invertY ? look.y : -look.y) * rotationSpeed * dt;
            pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);

            // Traditional 3rd-person platformer orbit: position swings around the target on
            // both yaw and pitch, always framing it, rather than tilting/panning in place.
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;
            Vector3 desiredPosition = focusPoint - rotation * Vector3.forward * distance;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, positionSmoothTime);

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
    }
}
