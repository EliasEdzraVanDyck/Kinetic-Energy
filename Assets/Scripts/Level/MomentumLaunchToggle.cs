using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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

