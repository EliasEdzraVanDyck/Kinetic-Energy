using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Player
{
    public enum PredictionMode
    {
        Ghost,
        Trail,
        Crosshair,
        None
    }

    public class LandingPreviewController : MonoBehaviour
    {
        [Header("Visual Groups (wired by setup)")]
        public GameObject ghostGroup;
        public float ghostGroundOffset = 0.5f;
        public GameObject trailGroup;
        public GameObject crosshairGroup;
        public Transform[] trailDots;

        [Header("HUD (cross-instance wired after both prefabs save)")]
        public Text modeLabel;

        PredictionMode currentMode = PredictionMode.Ghost;
        bool isVisible;

        public PredictionMode CurrentMode => currentMode;

        void Start()
        {
            UpdateLabel();
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
            UpdateLabel();
        }

        // trajectory/trajectoryCount: the actual simulated arc (see KineticCubeController.
        // PredictLandingPoint) - the trail follows this arc rather than a straight line to
        // the landing point. Falls back to a straight lerp if no trajectory data is supplied.
        public void SetLandingPoint(Vector3 lineStart, Vector3 landingPoint, Vector3[] trajectory, int trajectoryCount)
        {
            if (ghostGroup != null) ghostGroup.transform.position = landingPoint + Vector3.up * ghostGroundOffset;
            if (crosshairGroup != null) crosshairGroup.transform.position = landingPoint;

            if (trailDots == null || trailDots.Length == 0) return;

            for (int i = 0; i < trailDots.Length; i++)
            {
                if (trailDots[i] == null) continue;

                float t = (float)(i + 1) / (trailDots.Length + 1);
                Vector3 point;

                if (trajectory != null && trajectoryCount > 0)
                {
                    int index = Mathf.Clamp(Mathf.RoundToInt(t * (trajectoryCount - 1)), 0, trajectoryCount - 1);
                    point = trajectory[index];
                }
                else
                {
                    point = Vector3.Lerp(lineStart, landingPoint, t);
                }

                trailDots[i].position = point;
            }
        }

        void ApplyModeVisibility()
        {
            ghostGroup?.SetActive(isVisible && currentMode == PredictionMode.Ghost);
            trailGroup?.SetActive(isVisible && currentMode == PredictionMode.Trail);
            crosshairGroup?.SetActive(isVisible && currentMode == PredictionMode.Crosshair);
        }

        void UpdateLabel()
        {
            if (modeLabel == null) return;
            modeLabel.text = $"West: Ghost   North: Trail   East: Crosshair   South: None   (current: {currentMode})";
        }
    }
}
