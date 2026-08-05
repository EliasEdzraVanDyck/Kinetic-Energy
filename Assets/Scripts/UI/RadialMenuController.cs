using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{
    // "You should be able to switch between the four control schemes using your Dpad to open a
    // radial menu" (direct request). Holding any Dpad direction opens the menu and highlights
    // whichever of the 4 labels that direction currently points at; letting go of the Dpad
    // commits to whatever was last highlighted and closes the menu. Mapping (arbitrary but
    // documented in the controls text): Up = Launch Instantly, Right = Stick Aim, Down = Mixed,
    // Left = Defy Gravity.
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

        // Cross-hierarchy reference (this lives on PauseSystem, the player controller lives on
        // Player) - wired by KineticEnergySetup after both are instantiated, same pattern as
        // every other Player<->PauseSystem cross-wire in this project.
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
            // Same pause guard every other input-reading Update in this project uses - without
            // it the menu could still open/select while the pause menu is up.
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

                // Whichever axis has the bigger deflection picks the cardinal direction - a
                // diagonal-ish dpad read still resolves to exactly one of the four.
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
