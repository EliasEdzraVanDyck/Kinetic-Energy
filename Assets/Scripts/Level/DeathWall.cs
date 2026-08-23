using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

    public class DeathWall : MonoBehaviour
    {
        [Tooltip("Metres per second the wall advances along Move Direction. 0 = a static wall. With Move Acceleration set, this is the STARTING speed.")]
        public float moveSpeed = 0f;
        [Tooltip("Metres per second SQUARED the wall gains while it travels - the chase tightens the longer it runs. 0 = constant speed.")]
        public float moveAcceleration = 0f;
        [Tooltip("Speed ceiling in metres per second (0 = no cap). The wall stops gaining once it reaches this.")]
        public float maxMoveSpeed = 0f;
        [Tooltip("World-space direction of travel (normalised at use).")]
        public Vector3 moveDirection = Vector3.right;

        float currentSpeed;

        public static event System.Action<DeathWall> PlayerTouched;

        Rigidbody rb;
        Vector3 startPosition;
        bool startCaptured;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            currentSpeed = moveSpeed;
            CaptureStart();
        }

        void CaptureStart()
        {
            if (startCaptured) return;
            startPosition = transform.position;
            startCaptured = true;
        }

        void FixedUpdate()
        {
            if (moveSpeed <= 0f && moveAcceleration <= 0f) return;

            float dt = WorldMotionTime.FixedDeltaTime;
            currentSpeed += moveAcceleration * dt;
            if (maxMoveSpeed > 0f) currentSpeed = Mathf.Min(currentSpeed, maxMoveSpeed);

            Vector3 step = moveDirection.normalized * currentSpeed * dt;
            if (rb != null) rb.MovePosition(rb.position + step);
            else transform.position += step;
        }

        public void ResetToStart()
        {
            CaptureStart();
            currentSpeed = moveSpeed;
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb != null) rb.position = startPosition;
            transform.position = startPosition;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<KineticCubeController>() == null) return;
            PlayerTouched?.Invoke(this);
        }
    }
}

