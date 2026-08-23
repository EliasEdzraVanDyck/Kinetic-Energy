using UnityEngine;

namespace KineticEnergy.UI
{

    public static class CameraSpeedSettings
    {
        public const float MinScale = 0.5f;
        public const float MaxScale = 1.5f;
        public const float Step = 0.05f;

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

        public static float Snap(float value)
        {
            float clamped = Mathf.Clamp(value, MinScale, MaxScale);
            return Mathf.Round(clamped / Step) * Step;
        }
    }
}

