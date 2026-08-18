using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KineticEnergy.Level;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{
    // The bottom-left section explainer: one line of teaching per course section, shown for
    // as long as the player is INSIDE that section - not just at its start. Which section
    // that is comes from the player's position measured against the section spawn points
    // (sorted by x, the course's long axis), so it tracks walking backwards, launches that
    // skip a checkpoint, and the pause menu's section jumps alike.
    //
    // The whole HUD hangs off ONE checkbox (showHud) - untick it in the inspector and every
    // section's element is gone in one go.
    public class SectionIntroHud : MonoBehaviour
    {
        [Tooltip("Master switch: one checkbox hides every section's HUD element at once.")]
        public bool showHud = true;

        [Tooltip("The scene's section controller. Empty = found at Start.")]
        public LevelSectionController sections;

        [Tooltip("One entry per section, same order as the controller's sections array. The first also carries the controls primer.")]
        [TextArea(2, 8)] public string[] sectionTexts = new string[0];

        [Tooltip("Panel width in pixels; height follows the text.")]
        public float panelWidth = 480f;

        KineticCubeController player;
        Text label;
        GameObject panel;
        int shownIndex = -1;
        // Section indices sorted by their spawn's x - the course runs along x, so "which
        // section am I in" is "the last spawn I have passed".
        readonly List<int> orderedByX = new List<int>();

        void Start()
        {
            if (sections == null) sections = FindAnyObjectByType<LevelSectionController>();
            player = FindAnyObjectByType<KineticCubeController>();
            if (sections == null || player == null) return;

            for (int i = 0; i < sections.sections.Length; i++)
            {
                if (sections.sections[i] != null && sections.sections[i].spawnPoint != null) orderedByX.Add(i);
            }
            orderedByX.Sort((a, b) => sections.sections[a].spawnPoint.position.x
                .CompareTo(sections.sections[b].spawnPoint.position.x));

            BuildPanel();
        }

        void Update()
        {
            if (panel == null) return;
            bool visible = showHud && orderedByX.Count > 0;
            if (panel.activeSelf != visible) panel.SetActive(visible);
            if (!visible) return;

            // The last section spawn the player has passed along x - before the first one,
            // the first section is still the answer.
            int current = orderedByX[0];
            float x = player.transform.position.x;
            for (int i = 0; i < orderedByX.Count; i++)
            {
                if (x >= sections.sections[orderedByX[i]].spawnPoint.position.x - 0.5f) current = orderedByX[i];
            }

            if (current == shownIndex) return;
            shownIndex = current;
            label.text = current < sectionTexts.Length && !string.IsNullOrEmpty(sectionTexts[current])
                ? sectionTexts[current]
                : sections.sections[current].label;
        }

        // Overlay canvas of its own, so no scene canvas needs surgery: a dark backdrop in
        // the bottom-left corner whose height follows the text.
        void BuildPanel()
        {
            GameObject canvasGo = new GameObject("SectionIntroCanvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // under the pause menu, over the world
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            panel = new GameObject("SectionPanel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.zero;
            panelRect.pivot = Vector2.zero;
            panelRect.anchoredPosition = new Vector2(14f, 14f);
            panelRect.sizeDelta = new Vector2(panelWidth, 100f);
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject textGo = new GameObject("SectionText", typeof(RectTransform));
            textGo.transform.SetParent(panel.transform, false);
            label = textGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // A dynamic font's glyphs are rasterized AT this point size, then whatever the
            // canvas scaler does happens on top - 22 baked a low-resolution bitmap that then
            // got stretched, reading as blurry. A bigger point size means a sharper source
            // bitmap to begin with.
            label.fontSize = 34;
            label.color = new Color(0.92f, 0.92f, 0.92f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
