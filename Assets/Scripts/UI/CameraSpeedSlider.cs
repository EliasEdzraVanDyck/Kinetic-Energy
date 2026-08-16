using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KineticEnergy.UI
{
    // One pause-menu camera-speed slider (mouse or gamepad). The Slider itself runs on
    // WHOLE NUMBERS 10..30, each step being 5% - that way Unity's own dragging AND its
    // navigation handling (left stick left/right while the slider is selected) both move
    // in exact 5% increments without any custom input code.
    [RequireComponent(typeof(Slider))]
    public class CameraSpeedSlider : MonoBehaviour
    {
        [Tooltip("This slider drives the GAMEPAD factor when on, the mouse/keyboard factor when off.")]
        public bool gamepadSlider;
        [Tooltip("The '120%' readout beside the bar.")]
        public Text valueLabel;
        [Tooltip("The row's name label - tinted along with the bar while selected.")]
        public Text nameLabel;
        [Tooltip("Bar graphics tinted while this slider holds the gamepad's selection.")]
        public Image fillImage;
        public Image handleImage;

        public Color idleColor = new Color(1f, 1f, 1f, 0.75f);
        [Tooltip("Colour of the bar and labels while SELECTED - the controller's 'you are editing this one' cue.")]
        public Color selectedColor = new Color(1f, 0.82f, 0.2f);

        Slider slider;
        bool wasSelected;

        void Awake()
        {
            slider = GetComponent<Slider>();
            slider.wholeNumbers = true;
            slider.minValue = Mathf.Round(CameraSpeedSettings.MinScale / CameraSpeedSettings.Step); // 10 = 50%
            slider.maxValue = Mathf.Round(CameraSpeedSettings.MaxScale / CameraSpeedSettings.Step); // 30 = 150%
            slider.value = Mathf.Round(CurrentScale / CameraSpeedSettings.Step);
            slider.onValueChanged.AddListener(OnSliderChanged);
            ApplySelectionTint(false);
            RefreshLabel();
        }

        void OnDestroy()
        {
            if (slider != null) slider.onValueChanged.RemoveListener(OnSliderChanged);
        }

        float CurrentScale => gamepadSlider ? CameraSpeedSettings.GamepadScale : CameraSpeedSettings.MouseScale;

        void OnSliderChanged(float steps)
        {
            float scale = steps * CameraSpeedSettings.Step;
            if (gamepadSlider) CameraSpeedSettings.GamepadScale = scale;
            else CameraSpeedSettings.MouseScale = scale;
            RefreshLabel();
        }

        void Update()
        {
            // Selection can change from anywhere (gamepad navigation, a mouse click), so
            // the tint follows the EventSystem rather than the pointer events.
            bool selected = EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == gameObject;
            if (selected != wasSelected)
            {
                ApplySelectionTint(selected);
                wasSelected = selected;
            }
        }

        void ApplySelectionTint(bool selected)
        {
            Color tint = selected ? selectedColor : idleColor;
            if (fillImage != null) fillImage.color = tint;
            if (handleImage != null) handleImage.color = tint;
            if (nameLabel != null) nameLabel.color = tint;
            if (valueLabel != null) valueLabel.color = tint;
        }

        void RefreshLabel()
        {
            if (valueLabel == null) return;
            valueLabel.text = Mathf.RoundToInt(CurrentScale * 100f) + "%";
        }
    }
}
