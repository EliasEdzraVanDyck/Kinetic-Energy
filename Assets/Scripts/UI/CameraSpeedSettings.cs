using UnityEngine;

namespace KineticEnergy.UI
{
    // The player-facing camera speed factors, set by the pause menu's two sliders and
    // read by ThirdPersonOrbitCamera every frame. ONE factor per device multiplies every
    // form of camera speed that device drives (orbit, midair aim, grounded WASD aim),
    // so the whole camera scales together instead of drifting out of balance.
    // Static + PlayerPrefs: the choice survives scene loads and restarts, which is the
    // only sane behaviour for a settings slider.
    public static class CameraSpeedSettings
    {
        public const float MinScale = 0.5f;   // 50%
        public const float MaxScale = 1.5f;   // 150%
        public const float Step = 0.05f;      // 5% increments

        const string MousePrefKey = "CameraSpeed.Mouse";
        const string GamepadPrefKey = "CameraSpeed.Gamepad";

        static float mouseScale = -1f;
        static float gamepadScale = -1f;

        public static float MouseScale
        {
            get
            {
                if (mouseScale < 0f) mouseScale = Load(MousePrefKey);
                return mouseScale;
            }
            set
            {
                mouseScale = Snap(value);
                PlayerPrefs.SetFloat(MousePrefKey, mouseScale);
            }
        }

        public static float GamepadScale
        {
            get
            {
                if (gamepadScale < 0f) gamepadScale = Load(GamepadPrefKey);
                return gamepadScale;
            }
            set
            {
                gamepadScale = Snap(value);
                PlayerPrefs.SetFloat(GamepadPrefKey, gamepadScale);
            }
        }

        static float Load(string key)
        {
            return Snap(PlayerPrefs.GetFloat(key, 1f));
        }

        // Clamped to the 50-150% range and quantised to whole 5% steps.
        public static float Snap(float value)
        {
            float clamped = Mathf.Clamp(value, MinScale, MaxScale);
            return Mathf.Round(clamped / Step) * Step;
        }
    }
}
