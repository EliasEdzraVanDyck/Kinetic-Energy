using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.UI
{

    public class EnergyCrankUI : MonoBehaviour
    {
        [Tooltip("Ring radius in reference-resolution pixels.")]
        public float ringRadius = 70f;
        [Tooltip("Popup center's offset from the screen's bottom-left corner.")]
        public Vector2 cornerOffset = new Vector2(180f, 180f);
        public int ringSegments = 24;
        public Color ringColor = new Color(1f, 1f, 1f, 0.35f);
        public Color dotColor = new Color(1f, 0.82f, 0.2f, 1f);

        GameObject root;
        RectTransform dot;

        public void SetVisible(bool visible)
        {
            if (visible && root == null) Build();
            if (root != null && root.activeSelf != visible) root.SetActive(visible);
        }

        public void SetDotAngle(float angleDeg)
        {
            if (dot == null) return;
            float rad = angleDeg * Mathf.Deg2Rad;
            dot.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * ringRadius;
        }

        void Build()
        {
            GameObject canvasGo = new GameObject("EnergyCrankCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            root = new GameObject("CrankRing", typeof(RectTransform));
            root.transform.SetParent(canvasGo.transform, false);
            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.zero;
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = cornerOffset;
            rootRt.sizeDelta = new Vector2(ringRadius * 2f + 40f, ringRadius * 2f + 40f);

            for (int i = 0; i < ringSegments; i++)
            {
                float rad = i * (2f * Mathf.PI / ringSegments);
                GameObject segment = new GameObject("RingSegment" + i, typeof(RectTransform));
                segment.transform.SetParent(root.transform, false);
                RectTransform segmentRt = segment.GetComponent<RectTransform>();
                segmentRt.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * ringRadius;
                segmentRt.sizeDelta = new Vector2(8f, 8f);
                segment.AddComponent<Image>().color = ringColor;
            }

            GameObject dotGo = new GameObject("CrankDot", typeof(RectTransform));
            dotGo.transform.SetParent(root.transform, false);
            dot = dotGo.GetComponent<RectTransform>();
            dot.sizeDelta = new Vector2(22f, 22f);
            dot.anchoredPosition = new Vector2(ringRadius, 0f);
            dotGo.AddComponent<Image>().color = dotColor;

            root.SetActive(false);
        }
    }
}
