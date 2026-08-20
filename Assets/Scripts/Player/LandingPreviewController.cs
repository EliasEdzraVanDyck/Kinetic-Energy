using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Player
{
    public enum PredictionMode
    {
        Ghost,
        Trail,
        Crosshair,

        TrailAndCrosshair,
        None
    }

    public class LandingPreviewController : MonoBehaviour
    {
        [Header("Visual Groups (wired by setup)")]
        public GameObject ghostGroup;
        public float ghostGroundOffset = 0f;
        public GameObject trailGroup;
        public GameObject crosshairGroup;
        public float markerGroundOffset = -0.5f;
        public Transform[] trailDots;
        public float maxDotSpacing = 1f;

        [Header("HUD (cross-instance wired after both prefabs save)")]
        public Text modeLabel;

        public float positionSmoothTime = 0.05f;
        public float snapDistance = 25f;

        [Header("Temporary mode restriction")]

        public bool ghostAndCrosshairEnabled = false;

        public PredictionMode initialMode = PredictionMode.Trail;

        PredictionMode currentMode = PredictionMode.Trail;
        bool isVisible;

        void Awake()
        {
            currentMode = initialMode;
        }
        bool hasLanding = true;
        Vector3 ghostSmoothVelocity;
        Vector3 crosshairSmoothVelocity;

        public PredictionMode CurrentMode => currentMode;

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            ApplyModeVisibility();
        }

        public void SetMode(PredictionMode mode)
        {
            bool needsCrosshair = mode == PredictionMode.Ghost || mode == PredictionMode.Crosshair || mode == PredictionMode.TrailAndCrosshair;
            if (!ghostAndCrosshairEnabled && needsCrosshair) return;

            currentMode = mode;
            ApplyModeVisibility();
        }

        public void SetLandingPoint(Vector3 lineStart, Vector3 landingPoint, Vector3[] trajectory, int trajectoryCount, bool didLand, Vector3 landingNormal = default)
        {

            hasLanding = didLand;
            ApplyModeVisibility();

            if (hasLanding)
            {
                Vector3 normal = landingNormal.sqrMagnitude > 0.0001f ? landingNormal.normalized : Vector3.up;
                if (ghostGroup != null)
                {
                    MoveSmoothly(ghostGroup.transform, landingPoint + Vector3.up * ghostGroundOffset, ref ghostSmoothVelocity);
                }
                if (crosshairGroup != null)
                {

                    MoveSmoothly(crosshairGroup.transform, landingPoint + normal * markerGroundOffset, ref crosshairSmoothVelocity);
                    crosshairGroup.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
                }
            }

            if (trailDots == null || trailDots.Length == 0) return;

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
            ghostGroup?.SetActive(isVisible && currentMode == PredictionMode.Ghost && hasLanding);
            trailGroup?.SetActive(isVisible && (currentMode == PredictionMode.Trail || currentMode == PredictionMode.TrailAndCrosshair));
            crosshairGroup?.SetActive(isVisible && (currentMode == PredictionMode.Crosshair || currentMode == PredictionMode.TrailAndCrosshair) && hasLanding);
        }
    }
}
