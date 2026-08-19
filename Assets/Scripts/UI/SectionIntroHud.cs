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

        // A beat WITHIN a section: while the player is inside its radius it replaces the
        // section's own text. Lets one section teach several things in the order the player
        // actually meets them - charge here, re-aim on the next platform, buttons at the
        // first button - instead of one wall of text at the section pad. Sections with no
        // steps behave exactly as before.
        [System.Serializable]
        public class ProximityStep
        {
            [Tooltip("Editor-only note - which beat this is.")]
            public string label = "step";
            [Tooltip("What the player has to be near. Empty = the step never fires.")]
            public Transform target;
            [Tooltip("World-unit radius around the target, measured horizontally so height does not matter.")]
            public float radius = 15f;
            [TextArea(2, 8)] public string text;
        }

        [Tooltip("Beats inside a section, shown when the player is near their target. The NEAREST triggered step wins; outside them all, the section's own text returns.")]
        public ProximityStep[] proximitySteps = new ProximityStep[0];

        [Tooltip("Panel width in pixels; height follows the text.")]
        public float panelWidth = 480f;

        KineticCubeController player;
        Text label;
        GameObject panel;
        string shownText;
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

            string next = current < sectionTexts.Length && !string.IsNullOrEmpty(sectionTexts[current])
                ? sectionTexts[current]
                : sections.sections[current].label;

            // A step the player is standing in outranks the section text. Nearest wins, so
            // overlapping radii resolve to whichever beat they are actually at. Compared
            // FLAT: standing on a platform and hanging above it are the same beat.
            float bestSqr = float.MaxValue;
            foreach (ProximityStep step in proximitySteps)
            {
                if (step == null || step.target == null || string.IsNullOrEmpty(step.text)) continue;
                Vector3 offset = player.transform.position - step.target.position;
                offset.y = 0f;
                float distanceSqr = offset.sqrMagnitude;
                if (distanceSqr > step.radius * step.radius || distanceSqr >= bestSqr) continue;
                bestSqr = distanceSqr;
                next = step.text;
            }

            // Compared by CONTENT, not by index - a step and a section can hold the same
            // string, and rebuilding the label every frame would fight the layout fitter.
            if (next == shownText) return;
            shownText = next;
            label.text = next;
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
