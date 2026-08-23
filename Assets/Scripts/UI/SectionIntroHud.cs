using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Level;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{

    public class SectionIntroHud : MonoBehaviour
    {
        [Tooltip("Master switch: one checkbox hides every section's HUD element at once.")]
        public bool showHud = true;

        [Tooltip("The scene's section controller. Empty = found at Start.")]
        public LevelSectionController sections;

        [Tooltip("One entry per section, same order as the controller's sections array. The first also carries the controls primer.")]
        [TextArea(2, 8)] public string[] sectionTexts = new string[0];

        [Tooltip("Optional per-section controller wording, same indexing as sectionTexts. An empty or missing entry falls back to the keyboard text, so only sections that name buttons need filling in.")]
        [TextArea(2, 8)] public string[] sectionTextsGamepad = new string[0];

        [System.Serializable]
        public class ProximityStep
        {
            [Tooltip("Editor-only note - which beat this is.")]
            public string label = "step";
            [Tooltip("What the player has to be near. Empty = the step never fires.")]
            public Transform target;
            [Tooltip("Optional second anchor. Set it and the step covers the whole STRETCH between the two - both ends and everything between - instead of a single point.")]
            public Transform targetEnd;
            [Tooltip("World-unit radius around the target (or around the line between the two), measured horizontally so height does not matter.")]
            public float radius = 15f;
            [TextArea(2, 8)] public string text;
            [Tooltip("Shown instead while a controller is the active input. Empty = the keyboard text is used for both.")]
            [TextArea(2, 8)] public string gamepadText;

            public string For(bool gamepad) => gamepad && !string.IsNullOrEmpty(gamepadText) ? gamepadText : text;
        }

        [Tooltip("Beats inside a section, shown when the player is near their target. The NEAREST triggered step wins; outside them all, the section's own text returns.")]
        public ProximityStep[] proximitySteps = new ProximityStep[0];

        [Tooltip("Panel width in pixels; height follows the text.")]
        public float panelWidth = 480f;

        KineticCubeController player;
        Text label;
        GameObject panel;
        string shownText;

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

            int current = orderedByX[0];
            float x = player.transform.position.x;
            for (int i = 0; i < orderedByX.Count; i++)
            {
                if (x >= sections.sections[orderedByX[i]].spawnPoint.position.x - 0.5f) current = orderedByX[i];
            }

            UpdateActiveDevice();

            string next = current < sectionTexts.Length && !string.IsNullOrEmpty(sectionTexts[current])
                ? sectionTexts[current]
                : sections.sections[current].label;
            if (usingGamepad && current < sectionTextsGamepad.Length
                && !string.IsNullOrEmpty(sectionTextsGamepad[current]))
            {
                next = sectionTextsGamepad[current];
            }

            float bestSqr = float.MaxValue;
            foreach (ProximityStep step in proximitySteps)
            {
                if (step == null || step.target == null || string.IsNullOrEmpty(step.text)) continue;

                Vector3 here = player.transform.position;
                Vector3 a = step.target.position;
                here.y = 0f;
                a.y = 0f;

                Vector3 closest = a;
                if (step.targetEnd != null)
                {

                    Vector3 b = step.targetEnd.position;
                    b.y = 0f;
                    Vector3 span = b - a;
                    float lengthSqr = span.sqrMagnitude;
                    if (lengthSqr > 0.0001f)
                    {
                        float t = Mathf.Clamp01(Vector3.Dot(here - a, span) / lengthSqr);
                        closest = a + span * t;
                    }
                }

                float distanceSqr = (here - closest).sqrMagnitude;
                if (distanceSqr > step.radius * step.radius || distanceSqr >= bestSqr) continue;
                bestSqr = distanceSqr;
                next = step.For(usingGamepad);
            }

            if (next == shownText) return;
            shownText = next;
            label.text = next;
        }

        [Tooltip("Stick deflection that counts as deliberate controller input, so drift on a resting stick cannot flip the prompts.")]
        public float gamepadStickDeadzone = 0.35f;

        bool usingGamepad;

        void UpdateActiveDevice()
        {
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                bool padActed = pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
                    || pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame
                    || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
                    || pad.startButton.wasPressedThisFrame
                    || pad.leftTrigger.ReadValue() > 0.2f || pad.rightTrigger.ReadValue() > 0.2f
                    || pad.leftStick.ReadValue().sqrMagnitude > gamepadStickDeadzone * gamepadStickDeadzone
                    || pad.rightStick.ReadValue().sqrMagnitude > gamepadStickDeadzone * gamepadStickDeadzone;
                if (padActed) usingGamepad = true;
            }

            Keyboard keys = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool deskActed = (keys != null && keys.anyKey.wasPressedThisFrame)
                || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                    || mouse.scroll.ReadValue().sqrMagnitude > 0.01f
                    || mouse.delta.ReadValue().sqrMagnitude > 4f));
            if (deskActed) usingGamepad = false;
        }

        void BuildPanel()
        {
            GameObject canvasGo = new GameObject("SectionIntroCanvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
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

            label.fontSize = 34;
            label.color = new Color(0.92f, 0.92f, 0.92f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}

