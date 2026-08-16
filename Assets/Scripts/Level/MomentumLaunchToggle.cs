using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // Level1Economy's momentum experiment (a scene object, never a prefab): pressing 1
    // toggles whether midair launches ADD the velocity the cube carried into the aim
    // (the controller's addPreAimVelocityToLaunch) or fire the pure impulse as usual.
    // A small HUD line above the variant tag names the active mode.
    public class MomentumLaunchToggle : MonoBehaviour
    {
        [Tooltip("Starting state - off = the default pure-impulse midair launch.")]
        public bool momentumLaunch = false;

        KineticCubeController controller;
        Text hudLabel;

        void Start()
        {
            controller = FindAnyObjectByType<KineticCubeController>();
            if (controller == null)
            {
                Debug.LogError("MomentumLaunchToggle: no KineticCubeController in the scene.");
                enabled = false;
                return;
            }
            BuildHudTag();
            Apply();
        }

        void Update()
        {
            if (Time.timeScale <= 0f || controller == null) return;
            // Not while an aim is open - flipping mid-aim would shift the live cursor.
            if (controller.IsAimingOrCharging) return;

            if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                momentumLaunch = !momentumLaunch;
                Apply();
            }
        }

        void Apply()
        {
            controller.addPreAimVelocityToLaunch = momentumLaunch;
            if (hudLabel != null)
            {
                hudLabel.text = momentumLaunch
                    ? "1: Momentum launches ON (aim carries your speed)"
                    : "1: Momentum launches off";
            }
        }

        void BuildHudTag()
        {
            GameObject root = new GameObject("MomentumLaunchTag");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            // One line ABOVE the merged-economy variant tag.
            rt.anchoredPosition = new Vector2(-24f, 52f);
            rt.sizeDelta = new Vector2(620f, 30f);

            hudLabel = textGo.AddComponent<Text>();
            hudLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudLabel.fontSize = 20;
            hudLabel.alignment = TextAnchor.LowerRight;
            hudLabel.color = new Color(1f, 1f, 1f, 0.55f);
        }
    }
}
