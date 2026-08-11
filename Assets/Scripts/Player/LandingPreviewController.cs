using UnityEngine;

namespace KineticEnergy.Player
{
    public enum PredictionMode
    {
        None,
        // The dotted arc alone - the grounded aim's default preview.
        Trail,
        // The dotted arc plus the cross-and-ring reticle on the landing face - forced on by
        // the midair first-person aim.
        TrailAndCrosshair,
    }

    // Drives the launch preview visuals: a chain of dots laid along the ACTUAL simulated
    // trajectory (see KineticCubeController.PredictLandingPoint) and a cross-and-ring marker
    // lying flat against whatever face the shot would land on.
    public class LandingPreviewController : MonoBehaviour
    {
        [Header("Visual Groups (wired by setup)")]
        public GameObject trailGroup;
        public GameObject crosshairGroup;
        [Tooltip("Offset along the landing face's normal - negative moves the marker from the resting center onto the surface.")]
        public float markerGroundOffset = -0.5f;
        public Transform[] trailDots;
        [Tooltip("Maximum gap between adjacent trail dots, in meters of real arc length.")]
        public float maxDotSpacing = 1f;

        [Header("Marker Smoothing")]
        // The prediction recomputes every frame, so the landing point can legitimately jump
        // between platforms as charge changes - smoothing glides between successive targets
        // instead of popping. snapDistance still teleports across a genuinely large jump
        // (aim swung to a different part of the level) rather than flying the marker there.
        public float positionSmoothTime = 0.05f;
        public float snapDistance = 25f;

        [Tooltip("What the preview shows before any runtime SetMode call.")]
        public PredictionMode initialMode = PredictionMode.Trail;

        PredictionMode currentMode = PredictionMode.Trail;
        bool isVisible;
        bool hasLanding = true;
        Vector3 crosshairSmoothVelocity;

        public PredictionMode CurrentMode => currentMode;

        void Awake()
        {
            currentMode = initialMode;
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            ApplyModeVisibility();
        }

        public void SetMode(PredictionMode mode)
        {
            currentMode = mode;
            ApplyModeVisibility();
        }

        // trajectory/trajectoryCount: the simulated arc the dots follow. didLand: false when
        // the shot trails off into a bottomless gap - the crosshair marks an actual landing
        // SPOT, which doesn't exist then, so it hides; the trail still shows the arc, since
        // "here's the path, and it lands nowhere" is still meaningful. landingNormal orients
        // the marker flush against the landing face (wall, floor, ceiling alike).
        public void SetLandingPoint(Vector3 lineStart, Vector3 landingPoint, Vector3[] trajectory, int trajectoryCount, bool didLand, Vector3 landingNormal = default)
        {
            hasLanding = didLand;
            ApplyModeVisibility();

            if (hasLanding && crosshairGroup != null)
            {
                Vector3 normal = landingNormal.sqrMagnitude > 0.0001f ? landingNormal.normalized : Vector3.up;
                MoveSmoothly(crosshairGroup.transform, landingPoint + normal * markerGroundOffset, ref crosshairSmoothVelocity);
                crosshairGroup.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            }

            if (trailDots == null || trailDots.Length == 0) return;

            // Real arc length (summed segment distance), not the straight-line chord - a
            // lofted shot's path is meaningfully longer than the chord between its endpoints.
            float totalLength = 0f;
            if (trajectory != null && trajectoryCount > 1)
            {
                for (int i = 1; i < trajectoryCount; i++)
                {
                    totalLength += Vector3.Distance(trajectory[i - 1], trajectory[i]);
                }
            }
            else
            {
                totalLength = Vector3.Distance(lineStart, landingPoint);
            }

            int neededDots = Mathf.Clamp(Mathf.CeilToInt(totalLength / Mathf.Max(maxDotSpacing, 0.01f)), 1, trailDots.Length);

            // A small FIXED buffer keeps the last dot from sitting exactly on the marker,
            // without growing with trajectory length.
            float endGap = Mathf.Min(0.2f, totalLength * 0.5f);
            float usableLength = Mathf.Max(totalLength - endGap, 0f);

            if (trajectory != null && trajectoryCount > 1)
            {
                PlaceDotsAlongTrajectory(trajectory, trajectoryCount, usableLength, neededDots);
            }
            else
            {
                for (int i = 0; i < trailDots.Length; i++)
                {
                    if (trailDots[i] == null) continue;

                    if (i >= neededDots)
                    {
                        trailDots[i].gameObject.SetActive(false);
                        continue;
                    }
                    trailDots[i].gameObject.SetActive(true);

                    float t = totalLength > 0.0001f ? (usableLength * (i + 1) / neededDots) / totalLength : 0f;
                    trailDots[i].position = Vector3.Lerp(lineStart, landingPoint, t);
                }
            }
        }

        // Places each dot exactly ON the simulated trajectory, evenly spaced by REAL distance
        // travelled - sampling by array index would be uniform in simulation time instead,
        // bunching dots where the cube moves slowly (the apex) and spreading them where it
        // moves fast.
        void PlaceDotsAlongTrajectory(Vector3[] trajectory, int trajectoryCount, float usableLength, int neededDots)
        {
            int segmentEnd = 1;
            float segmentStartLength = 0f;
            float segmentLength = Vector3.Distance(trajectory[0], trajectory[1]);

            for (int d = 0; d < trailDots.Length; d++)
            {
                if (trailDots[d] == null) continue;

                if (d >= neededDots)
                {
                    trailDots[d].gameObject.SetActive(false);
                    continue;
                }
                trailDots[d].gameObject.SetActive(true);

                float targetLength = usableLength * (d + 1) / neededDots;

                while (segmentEnd < trajectoryCount - 1 && segmentStartLength + segmentLength < targetLength)
                {
                    segmentStartLength += segmentLength;
                    segmentEnd++;
                    segmentLength = Vector3.Distance(trajectory[segmentEnd - 1], trajectory[segmentEnd]);
                }

                float segmentT = segmentLength > 0.0001f
                    ? Mathf.Clamp01((targetLength - segmentStartLength) / segmentLength)
                    : 0f;

                trailDots[d].position = Vector3.Lerp(trajectory[segmentEnd - 1], trajectory[segmentEnd], segmentT);
            }
        }

        void MoveSmoothly(Transform t, Vector3 target, ref Vector3 velocity)
        {
            float jump = Vector3.Distance(t.position, target);

            if (jump > snapDistance)
            {
                t.position = target;
                velocity = Vector3.zero;
                return;
            }
            t.position = Vector3.SmoothDamp(t.position, target, ref velocity, positionSmoothTime);
        }

        void ApplyModeVisibility()
        {
            trailGroup?.SetActive(isVisible && (currentMode == PredictionMode.Trail || currentMode == PredictionMode.TrailAndCrosshair));
            crosshairGroup?.SetActive(isVisible && currentMode == PredictionMode.TrailAndCrosshair && hasLanding);
        }
    }
}
