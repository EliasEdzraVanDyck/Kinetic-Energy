using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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

        void CarryStuckRider(float stepDegrees)
        {
            if (Mathf.Abs(stepDegrees) < 0.0001f) return;
            if (player == null) player = FindAnyObjectByType<KineticCubeController>();
            if (player == null || !player.IsStuck) return;

            Collider stuckTo = player.LastCrashSurface;
            if (stuckTo == null) return;
            if (stuckTo.transform != transform && !stuckTo.transform.IsChildOf(transform)) return;

            Vector3 axis = spinAxis.sqrMagnitude > 0.0001f ? spinAxis.normalized : Vector3.up;
            Vector3 worldAxis = transform.TransformDirection(axis);
            Quaternion spinDelta = Quaternion.AngleAxis(stepDegrees, worldAxis);

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

        public void ResetToStart()
        {
            angle = startAngleOffset;
            ApplyRotation();
        }
    }
}

