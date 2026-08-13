using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Camera
{
    // The depth-perception playtest harness: holds the three aim-camera presets (A/B/C),
    // applies the active one to ThirdPersonOrbitCamera, and cycles A -> B -> C -> A on
    // V / D-pad Right. Cycling is blocked while any aim/charge is open so the camera never
    // pops mid-aim. Also owns the small bottom-right HUD tag naming the active variant
    // (built at runtime, so every scene gets it without per-scene UI edits).
    //
    // Lives on the Player prefab. Scene references (camera rig, controller) are found at
    // runtime - prefabs can't hold cross-hierarchy references.
    public class AimCameraVariantController : MonoBehaviour
    {
        public AimCameraVariant currentVariant = AimCameraVariant.Baseline;
        public AimCameraPreset baselinePreset;
        public AimCameraPreset otsParallaxPreset;
        public AimCameraPreset baselinePipPreset;
        public AimCameraPreset otsParallaxPipPreset;
        public AimCameraPreset freeLookFirstPersonPreset;
        public AimCameraPreset freeLookOtsPreset;

        ThirdPersonOrbitCamera cameraOrbit;
        KineticCubeController controller;
        Text hudLabel;
        Text hudEnergyNote;

        // Raised on every variant change - the pause menu label and the logger listen.
        public event Action<AimCameraVariant, AimCameraPreset> VariantChanged;

        public AimCameraPreset ActivePreset => PresetFor(currentVariant);
        public string CurrentLabel => LabelFor(currentVariant, ActivePreset);

        // The controller-energy warning, shown as its OWN text element ABOVE the variant
        // label (HUD and pause menu alike) - empty for variants with the normal dial.
        public string EnergyControlsNote => ActivePreset != null && ActivePreset.UsesFreeLook
            ? "Controller energy: RB adds / LB removes (this variant only)"
            : "";

        AimCameraPreset PresetFor(AimCameraVariant variant) => variant switch
        {
            AimCameraVariant.OtsParallax => otsParallaxPreset,
            AimCameraVariant.BaselinePip => baselinePipPreset,
            AimCameraVariant.OtsParallaxPip => otsParallaxPipPreset,
            AimCameraVariant.FreeLookFirstPerson => freeLookFirstPersonPreset,
            AimCameraVariant.FreeLookOts => freeLookOtsPreset,
            _ => baselinePreset,
        };

        LandingPipCamera pipCamera;

        void Start()
        {
            controller = GetComponent<KineticCubeController>();
            cameraOrbit = FindAnyObjectByType<ThirdPersonOrbitCamera>();
            pipCamera = LandingPipCamera.Create(controller);
            BuildHudTag();
            SetVariant(currentVariant);
        }

        void Update()
        {
            if (Time.timeScale <= 0f) return; // paused - the pause menu button handles it there

            // Shoulder swap (Q / Right Stick Click) - the standard third-person-shooter
            // answer to "the player always hangs on one side". Deliberately works DURING
            // the aim (that's its whole point) and is remembered between aims.
            if (currentVariant != AimCameraVariant.Baseline && cameraOrbit != null)
            {
                bool swapPressed = (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame);
                if (swapPressed) cameraOrbit.ToggleAimShoulder();
            }

            // Variant CYCLING stays blocked during any aim/charge window - swapping the
            // whole camera mid-aim pops.
            if (controller != null && controller.IsAimingOrCharging) return;

            bool forwardPressed = (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame);
            bool backPressed = (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);
            if (forwardPressed) CycleVariant();
            else if (backPressed) CycleVariantBack();
        }

        // Also wired to the pause menu's selector button (PauseController.OnCameraVariantClicked).
        // The aim-window block lives HERE so the pause-menu path obeys it too.
        public void CycleVariant()
        {
            if (controller != null && controller.IsAimingOrCharging) return;
            SetVariant((AimCameraVariant)(((int)currentVariant + 1) % 6));
        }

        // C / D-pad Left steps BACK through the cycle - same aim-window block.
        public void CycleVariantBack()
        {
            if (controller != null && controller.IsAimingOrCharging) return;
            SetVariant((AimCameraVariant)(((int)currentVariant + 5) % 6));
        }

        public void SetVariant(AimCameraVariant variant)
        {
            currentVariant = variant;
            AimCameraPreset preset = ActivePreset;
            if (cameraOrbit != null) cameraOrbit.SetAimCameraPreset(preset);
            if (pipCamera != null) pipCamera.SetPreset(preset);
            if (hudLabel != null) hudLabel.text = CurrentLabel;
            if (hudEnergyNote != null) hudEnergyNote.text = EnergyControlsNote;
            VariantChanged?.Invoke(variant, preset);
        }

        static string LabelFor(AimCameraVariant variant, AimCameraPreset preset)
        {
            string letter = variant switch
            {
                AimCameraVariant.OtsParallax => "B",
                AimCameraVariant.BaselinePip => "C",
                AimCameraVariant.OtsParallaxPip => "D",
                AimCameraVariant.FreeLookFirstPerson => "E",
                AimCameraVariant.FreeLookOts => "F",
                _ => "A",
            };
            string name = preset != null ? preset.displayName : variant.ToString();
            return $"Variant {letter} - {name}";
        }

        // Small, visually quiet tag in the bottom-right corner - it must never compete
        // with the energy bar (top-right).
        void BuildHudTag()
        {
            GameObject root = new GameObject("AimCameraVariantTag");
            UnityEngine.Canvas canvas = root.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // under the pause canvas
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
            rt.sizeDelta = new Vector2(520f, 34f);

            hudLabel = textGo.AddComponent<Text>();
            hudLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudLabel.fontSize = 22;
            hudLabel.alignment = TextAnchor.LowerRight;
            hudLabel.color = new Color(1f, 1f, 1f, 0.55f);
            hudLabel.text = CurrentLabel;

            // The controller-energy note sits in its OWN box directly above the label.
            GameObject noteGo = new GameObject("EnergyNote", typeof(RectTransform));
            noteGo.transform.SetParent(root.transform, false);
            RectTransform noteRect = noteGo.GetComponent<RectTransform>();
            noteRect.anchorMin = new Vector2(1f, 0f);
            noteRect.anchorMax = new Vector2(1f, 0f);
            noteRect.pivot = new Vector2(1f, 0f);
            noteRect.anchoredPosition = new Vector2(-24f, 52f);
            noteRect.sizeDelta = new Vector2(620f, 30f);

            hudEnergyNote = noteGo.AddComponent<Text>();
            hudEnergyNote.font = hudLabel.font;
            hudEnergyNote.fontSize = 20;
            hudEnergyNote.alignment = TextAnchor.LowerRight;
            hudEnergyNote.color = new Color(1f, 0.82f, 0.2f, 0.85f); // accent - it's a warning
            hudEnergyNote.text = EnergyControlsNote;
        }
    }
}
