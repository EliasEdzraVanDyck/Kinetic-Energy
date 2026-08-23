using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using KineticEnergy.Level;

namespace KineticEnergy.Player
{

    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeControllerFreeMove : MonoBehaviour
    {
        [Header("Ground Movement")]
        public float moveSpeed = 4f;
        [Range(0f, 1f)] public float moveDeadzone = 0.15f;

        [Header("Air Correction")]

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

        [Tooltip("Grounded WASD/stick walking and midair nudging. Disabled by default; press M in play mode to toggle. Aiming controls are unaffected either way.")]
        public bool movementInputEnabled = false;

        Rigidbody rb;
        BoxCollider boxCollider;
        KineticCubeController launchController;
        bool isGrounded;
        bool wasGrounded;

        public Vector3 GroundPlatformVelocity { get; private set; }

        public bool OnMovingPlatform { get; private set; }

        public MovingPlatform GroundPlatform { get; private set; }
        Quaternion visualTargetRotation = Quaternion.identity;
        float launchFacingYaw;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            boxCollider = GetComponent<BoxCollider>();

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

            Vector2 stick = movementInputEnabled && moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            Vector3 forward = CameraRelativeForward();
            Vector3 right = CameraRelativeRight();

            if (isGrounded)
            {

                if (launchController != null && !launchController.AllowGroundedMovement)
                {
                    wasGrounded = isGrounded;
                    return;
                }

                Vector3 moveDirection = stick.sqrMagnitude > moveDeadzone * moveDeadzone
                    ? (forward * stick.y + right * stick.x).normalized
                    : Vector3.zero;

                Vector3 horizontalVelocity = moveDirection * moveSpeed + new Vector3(GroundPlatformVelocity.x, 0f, GroundPlatformVelocity.z);

                float verticalVelocity = OnMovingPlatform ? GroundPlatformVelocity.y : rb.linearVelocity.y;
                rb.linearVelocity = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);

                if (moveDirection.sqrMagnitude > 0.0001f)
                {
                    launchFacingYaw = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
                    if (visual != null) visual.localRotation = Quaternion.Euler(0f, launchFacingYaw, 0f);
                }

                if (!wasGrounded && visual != null)
                {
                    visual.localRotation = Quaternion.Euler(0f, launchFacingYaw, 0f);
                }
                visualTargetRotation = Quaternion.Euler(0f, launchFacingYaw, 0f);
            }
            else
            {

                if (launchController != null && !launchController.AllowAirborneNudge)
                {
                    wasGrounded = isGrounded;
                    return;
                }

                if (stick.sqrMagnitude > airControlDeadzone * airControlDeadzone)
                {

                    float speedUpCompensation = Time.timeScale > 1f ? 1f / Time.timeScale : 1f;
                    Vector3 correction = (forward * stick.y + right * stick.x) * (airControlAcceleration * speedUpCompensation);
                    rb.AddForce(correction, ForceMode.Acceleration);
                }

                float pitchLean = stick.y * maxLeanAngle;
                float rollLean = -stick.x * maxLeanAngle;

                visualTargetRotation = Quaternion.Euler(pitchLean, launchFacingYaw, rollLean);
            }

            if (visual != null)
            {
                visual.localRotation = Quaternion.Slerp(visual.localRotation, visualTargetRotation, leanSpeed * Time.fixedDeltaTime);
            }

            wasGrounded = isGrounded;
        }

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

        public void AlignVisualToSurface(Vector3 surfaceNormal)
        {
            if (visual == null) return;
            visual.localRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
        }

        void UpdateGrounded()
        {
            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);

            float descentReach = OnMovingPlatform && GroundPlatformVelocity.y < 0f
                ? -GroundPlatformVelocity.y * Time.fixedDeltaTime + 0.05f
                : 0f;
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out RaycastHit hit,
                transform.rotation, groundCheckDistance + descentReach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            MovingPlatform platform = isGrounded && hit.collider != null && hit.collider.attachedRigidbody != null
                ? hit.collider.attachedRigidbody.GetComponent<MovingPlatform>()
                : null;
            OnMovingPlatform = platform != null;
            GroundPlatform = platform;
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

