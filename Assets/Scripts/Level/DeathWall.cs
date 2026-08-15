using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // The purple challenge hazard: any player contact raises PlayerTouched - the challenge
    // stage controller respawns the player and resets every hazard. Optionally creeps along
    // the level at moveSpeed (the chasing-wall stage); seal walls leave it at 0.
    public class DeathWall : MonoBehaviour
    {
        [Tooltip("Metres per second the wall advances along Move Direction. 0 = a static wall.")]
        public float moveSpeed = 0f;
        [Tooltip("World-space direction of travel (normalised at use).")]
        public Vector3 moveDirection = Vector3.right;

        // Raised once per touch with the wall itself; the stage controller subscribes.
        public static event System.Action<DeathWall> PlayerTouched;

        Rigidbody rb;
        Vector3 startPosition;
        bool startCaptured;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            CaptureStart();
        }

        // The start pose may be needed before Awake has run - the stage controller resets
        // walls that begin the scene deactivated (their Awake is deferred until first
        // activation, and a blind reset would send them to the world origin).
        void CaptureStart()
        {
            if (startCaptured) return;
            startPosition = transform.position;
            startCaptured = true;
        }

        void FixedUpdate()
        {
            if (moveSpeed <= 0f) return;
            // A world mover: advances on WorldMotionTime, so the player's aim slow-mo
            // never freezes the threat (the standing rule for every non-player mover).
            Vector3 step = moveDirection.normalized * moveSpeed * WorldMotionTime.FixedDeltaTime;
            if (rb != null) rb.MovePosition(rb.position + step);
            else transform.position += step;
        }

        public void ResetToStart()
        {
            CaptureStart();
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
