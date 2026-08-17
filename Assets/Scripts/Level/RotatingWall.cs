using UnityEngine;

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
            angle += degreesPerSecond * WorldMotionTime.FixedDeltaTime;
            ApplyRotation();
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
