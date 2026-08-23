using UnityEngine;

namespace KineticEnergy.Camera
{

    public class AimRefinementSettings : MonoBehaviour
    {
        public static AimRefinementSettings Active { get; private set; }

        [Header("Stick conditioning (gamepad only)")]
        [Tooltip("Radial deadzone; travel beyond it is re-scaled to the full 0..1 range.")]
        [Range(0f, 0.5f)] public float stickDeadzone = 0.15f;
        [Tooltip("Response exponent: 1 = linear, 1.5-2 = much finer control in the lower stick range.")]
        [Range(1f, 3f)] public float stickResponseExponent = 1.8f;

        [Header("One-Euro smoothing (midair aim)")]
        public bool smoothingEnabled = true;
        [Tooltip("Mouse: baseline cutoff Hz - LOWER = smoother when still. Mouse gets a higher value (less smoothing) than stick.")]
        public float mouseMinCutoff = 2f;
        [Tooltip("Mouse: how quickly smoothing fades with aim speed - higher = less lag on sweeps.")]
        public float mouseBeta = 0.03f;
        public float stickMinCutoff = 1.2f;
        public float stickBeta = 0.015f;

        [Header("Grounded arrow fine aim (mouse)")]
        public bool groundedFineAimEnabled = true;
        [Tooltip("Arrow speed factor at a barely-moving mouse; ramps to 1 at the reference delta.")]
        [Range(0.1f, 1f)] public float groundedFineAimMinFactor = 0.4f;
        [Tooltip("Mouse delta (px/frame) that counts as a full-speed movement.")]
        public float groundedFineAimMouseReference = 8f;

        [Header("Zoom precision")]
        [Tooltip("Extra sensitivity reduction at FULL aim zoom (0.15 = 15% slower than the geometrically-correct compensation).")]
        [Range(0f, 0.5f)] public float zoomExtraPrecision = 0.15f;

        void OnEnable() { Active = this; }
        void OnDisable() { if (Active == this) Active = null; }

        public Vector2 ConditionStick(Vector2 stick)
        {
            float magnitude = stick.magnitude;
            if (magnitude <= stickDeadzone) return Vector2.zero;
            float normalized = Mathf.Clamp01((magnitude - stickDeadzone) / (1f - stickDeadzone));
            float curved = Mathf.Pow(normalized, stickResponseExponent);
            return stick / magnitude * curved;
        }
    }

    public class OneEuroFilter
    {
        float previous;
        float derivativePrevious;
        bool initialized;

        public void Reset() { initialized = false; }

        public float Filter(float value, float dt, float minCutoff, float beta)
        {
            if (dt <= 0f) return value;
            if (!initialized)
            {
                initialized = true;
                previous = value;
                derivativePrevious = 0f;
                return value;
            }

            float derivative = (value - previous) / dt;
            derivativePrevious = Mathf.Lerp(derivativePrevious, derivative, Alpha(1f, dt));
            float cutoff = minCutoff + beta * Mathf.Abs(derivativePrevious);
            previous = Mathf.Lerp(previous, value, Alpha(cutoff, dt));
            return previous;
        }

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 0.01f));
            return 1f / (1f + tau / dt);
        }
    }
}

