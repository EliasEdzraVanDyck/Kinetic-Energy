using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum ControlSchemeVariant
    {
        NewControls, // A - camera follows wide grounded aims, grounded dial, LB/RB energy
        Classic,     // B - the current default controls, untouched
    }

    // The control-scheme playtest harness (QuarryAim scene): V / D-pad Right and C /
    // D-pad Left toggle between the NEW control package (variant A) and the classic
    // controls (variant B). The package, all applied via the controller's public flags:
    //   - Grounded aim past 60 degrees to either side slowly pans the camera after it.
    //   - The grounded launch strength is DIALLED like the midair aim (wheel / bumpers)
    //     instead of charging over held time.
    //   - Controller energy on the bumpers everywhere: RB adds, LB removes (LB stops
    //     being charge-cancel while active).
    public class ControlSchemeVariantController : MonoBehaviour
    {
        [Tooltip("The scheme active at scene start. A = the new package, B = classic.")]
        public ControlSchemeVariant currentVariant = ControlSchemeVariant.NewControls;

        [Header("A - New controls")]
        [Tooltip("Degrees of horizontal aim deviation before the camera starts following.")]
        public float followThresholdDegrees = 60f;
        [Tooltip("The follow band - the aim clamps hard at threshold+band (65) and the pan ramps to full speed across it.")]
        public float followBandDegrees = 5f;
        [Tooltip("Full pan speed at the clamp edge.")]
        public float followSpeed = 45f;
        [Tooltip("Variant A's mouse orbit-camera speed multiplier (the 15%-slower experiment lives HERE now - every other scene/variant keeps the classic 0.6).")]
        [Range(0.1f, 1f)] public float newControlsMouseOrbitMultiplier = 0.51f;

        KineticCubeController controller;
        KineticEnergy.Camera.AimRefinementSettings refinement;
        KineticEnergy.Camera.ThirdPersonOrbitCamera orbitCamera;
        Text hudLabel;

        void Start()
        {
            refinement = FindAnyObjectByType<KineticEnergy.Camera.AimRefinementSettings>(FindObjectsInactive.Include);
            orbitCamera = FindAnyObjectByType<KineticEnergy.Camera.ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            controller = FindAnyObjectByType<KineticCubeController>();
            if (controller == null)
            {
                Debug.LogError("ControlSchemeVariantController: no KineticCubeController in the scene.");
                enabled = false;
                return;
            }
            BuildHudTag();
            ApplyVariant();
        }

        void Update()
        {
            if (Time.timeScale <= 0f || controller == null) return;
            if (controller.IsAimingOrCharging) return;

            bool forward = (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame);
            bool back = (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);
            if (forward || back)
            {
                currentVariant = currentVariant == ControlSchemeVariant.NewControls
                    ? ControlSchemeVariant.Classic
                    : ControlSchemeVariant.NewControls;
                ApplyVariant();
            }
        }

        void ApplyVariant()
        {
            bool newControls = currentVariant == ControlSchemeVariant.NewControls;
            controller.groundedAimCameraFollow = newControls;
            controller.groundedAimFollowThreshold = followThresholdDegrees;
            controller.groundedAimFollowBand = followBandDegrees;
            controller.groundedAimFollowSpeed = followSpeed;
            controller.groundedDialControls = newControls;
            controller.bumperEnergyDial = newControls;

            // The aim refinements (stick curve, One-Euro smoothing, ...) belong to the NEW
            // package - Classic must be byte-identical to every other scene's controls.
            if (refinement != null) refinement.enabled = newControls;

            // The slower mouse orbit camera is variant A's experiment only - Classic (and
            // by code default, every other scene) keeps the original 0.6.
            if (orbitCamera != null)
            {
                orbitCamera.mouseOrbitSpeedMultiplier = newControls ? newControlsMouseOrbitMultiplier : 0.6f;
            }

            if (hudLabel != null) hudLabel.text = CurrentLabel;
        }

        string CurrentLabel => currentVariant == ControlSchemeVariant.NewControls
            ? "Variant A - New controls (dial aim, RB/LB energy)"
            : "Variant B - Classic controls";

        void BuildHudTag()
        {
            GameObject root = new GameObject("ControlSchemeVariantTag");
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
            rt.anchoredPosition = new Vector2(-24f, 16f);
            rt.sizeDelta = new Vector2(620f, 34f);

            hudLabel = textGo.AddComponent<Text>();
            hudLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudLabel.fontSize = 22;
            hudLabel.alignment = TextAnchor.LowerRight;
            hudLabel.color = new Color(1f, 1f, 1f, 0.55f);
        }
    }
}
