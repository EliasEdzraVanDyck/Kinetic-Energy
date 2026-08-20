using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{

    public class RadialMenuController : MonoBehaviour
    {
        [Header("Input")]
        public InputActionReference radialMenuAction;
        [Range(0f, 1f)] public float directionDeadzone = 0.5f;

        [Header("Menu")]
        public GameObject menuRoot;
        public Text upLabel;
        public Text rightLabel;
        public Text downLabel;
        public Text leftLabel;
        public Color normalColor = Color.white;
        public Color highlightColor = new Color(1f, 0.82f, 0.2f);

        public KineticCubeController controller;

        readonly ControlScheme upScheme = ControlScheme.LaunchInstantly;
        readonly ControlScheme rightScheme = ControlScheme.StickAim;
        readonly ControlScheme downScheme = ControlScheme.Mixed;
        readonly ControlScheme leftScheme = ControlScheme.DefyGravity;

        bool isOpen;
        bool hasPending;
        ControlScheme pendingSelection;

        void OnEnable()
        {
            radialMenuAction?.action?.Enable();
        }

        void OnDisable()
        {
            radialMenuAction?.action?.Disable();
        }

        void Update()
        {

            if (Time.timeScale <= 0f) return;
            if (controller == null || !controller.schemeSwitchingEnabled)
            {
                if (isOpen) CloseMenu(commit: false);
                return;
            }

            Vector2 dir = radialMenuAction != null && radialMenuAction.action != null
                ? radialMenuAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            bool held = dir.sqrMagnitude > directionDeadzone * directionDeadzone;

            if (held)
            {
                if (!isOpen)
                {
                    isOpen = true;
                    menuRoot?.SetActive(true);
                }

                ControlScheme highlighted = Mathf.Abs(dir.x) > Mathf.Abs(dir.y)
                    ? (dir.x > 0f ? rightScheme : leftScheme)
                    : (dir.y > 0f ? upScheme : downScheme);

                pendingSelection = highlighted;
                hasPending = true;
                UpdateHighlight(highlighted);
            }
            else if (isOpen)
            {
                CloseMenu(commit: true);
            }
        }

        void CloseMenu(bool commit)
        {
            isOpen = false;
            menuRoot?.SetActive(false);
            if (commit && hasPending && controller != null)
            {
                controller.SetControlSchemeFromMenu(pendingSelection);
            }
            hasPending = false;
        }

        void UpdateHighlight(ControlScheme highlighted)
        {
            SetLabelColor(upLabel, highlighted == upScheme);
            SetLabelColor(rightLabel, highlighted == rightScheme);
            SetLabelColor(downLabel, highlighted == downScheme);
            SetLabelColor(leftLabel, highlighted == leftScheme);
        }

        void SetLabelColor(Text label, bool highlighted)
        {
            if (label != null) label.color = highlighted ? highlightColor : normalColor;
        }
    }
}
