using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A floating wall that spins in place, so its landing face keeps turning away from
    // you - the shot has to be timed as well as aimed. Pair it with StickySurface and the
    // face holds you once you hit it (and keeps turning with you attached).
    //
    // A world mover: it advances on WorldMotionTime, so the midair aim's bullet-time slows
    // it exactly like every other non-player mover, and a pause freezes it cleanly.
    public class RotatingWall : MonoBehaviour
    {
        [Tooltip("Degrees per (world-motion) second. Negative spins the other way.")]
        public float degreesPerSecond = 35f;
        [Tooltip("Axis to spin around, in the wall's own space. Y = the face sweeps horizontally.")]
        public Vector3 spinAxis = Vector3.up;
        [Tooltip("Degrees of head start, so a row of walls doesn't turn in lockstep.")]
        public float startAngleOffset = 0f;

        Rigidbody body;
        Quaternion startRotation;
        float angle;
        KineticCubeController player;

        void Awake()
        {
            startRotation = transform.rotation;
            angle = startAngleOffset;

            // KINEMATIC + INTERPOLATED, like MovingPlatform: rotating a plain transform
            // steps once per physics tick with nothing drawn between, which reads as a
            // stutter while you are stuck to the face and riding it round.
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            ApplyRotation();
        }

        void FixedUpdate()
        {
            float step = degreesPerSecond * WorldMotionTime.FixedDeltaTime;
            angle += step;
            ApplyRotation();
            CarryStuckRider(step);
        }

        // Anything crash-stuck to this wall RIDES it round. Without this the player stays
        // pinned at the world position they hit, and the face simply turns away from
        // underneath them.
        void CarryStuckRider(float stepDegrees)
        {
            if (Mathf.Abs(stepDegrees) < 0.0001f) return;
            if (player == null) player = FindAnyObjectByType<KineticCubeController>();
            if (player == null || !player.IsStuck) return;

            // Only the rider on THIS wall - the crash surface records what they hit.
            Collider stuckTo = player.LastCrashSurface;
            if (stuckTo == null) return;
            if (stuckTo.transform != transform && !stuckTo.transform.IsChildOf(transform)) return;

            Vector3 axis = spinAxis.sqrMagnitude > 0.0001f ? spinAxis.normalized : Vector3.up;
            Vector3 worldAxis = transform.TransformDirection(axis);
            Quaternion spinDelta = Quaternion.AngleAxis(stepDegrees, worldAxis);

            // The rider's TANGENTIAL VELOCITY (omega x r) rather than a target position -
            // the controller hands this straight to the stick's velocity pin, so the
            // physics step moves the player and nothing teleports. Converted to real
            // seconds, since the wall itself advances on world-motion time.
            float realDegreesPerSecond = stepDegrees / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            Vector3 omega = worldAxis * (realDegreesPerSecond * Mathf.Deg2Rad);
            Vector3 offset = player.transform.position - transform.position;
            player.CarryStuckRider(Vector3.Cross(omega, offset), spinDelta);
        }

        void ApplyRotation()
        {
            Vector3 axis = spinAxis.sqrMagnitude > 0.0001f ? spinAxis.normalized : Vector3.up;
            Quaternion rotation = startRotation * Quaternion.AngleAxis(angle, axis);
            if (body != null) body.MoveRotation(rotation);
            else transform.rotation = rotation;
        }

        // Returned to its placed facing by a respawn, so a retry sees the same opening.
        public void ResetToStart()
        {
            angle = startAngleOffset;
            ApplyRotation();
        }
    }
}
