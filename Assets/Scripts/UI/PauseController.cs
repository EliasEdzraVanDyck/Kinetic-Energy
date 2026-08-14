using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KineticEnergy.UI
{
    public class PauseController : MonoBehaviour
    {
        [Header("Input")]
        public InputActionReference pauseAction;

        [Header("Panels")]
        public GameObject pausePanel;
        public GameObject controlsPanel;
        public GameObject scenesPanel;
        public GameObject firstPauseButton;
        public GameObject firstControlsButton;
        public GameObject firstScenesButton;

        [Header("Corner Hint (wired by setup)")]
        // The top-left "Open the pause menu ..." label - pointless while the menu is
        // actually open, so pausing hides it and resuming brings it back.
        public GameObject controlsHintLabel;
        [Tooltip("Scene-local hint objects (found by name at Start) that also hide while the menu is open - e.g. QuarryNew's QuarryIntroHud.")]
        public string[] sceneHintObjectNames = { "QuarryIntroHud" };

        readonly List<GameObject> hintObjects = new List<GameObject>();

        [Header("Controls Text")]
        // Content is no longer static - KineticCubeController writes into this directly
        // (UpdateControlsText) whenever the active control scheme changes, so the panel always
        // matches whichever scheme is actually active instead of a fixed string baked in here.
        public Text controlsBodyText;

        [Header("Feedback")]
        [Tooltip("Opened in the system browser by the pause menu's Feedback button.")]
        public string feedbackFormUrl = "https://forms.gle/c7TVCoLzkktTWJFc7";

        [Header("Aim Camera Variant (wired by setup)")]
        // The pause menu's variant-selector button label - the button cycles A -> B -> C
        // (blocked while an aim is open, same as the V hotkey), the label names the active
        // one so testers who never find the hotkey can still switch.
        public Text cameraVariantLabel;
        // Its own text box ABOVE the variant button: the controller-energy warning for the
        // free-look variants (empty otherwise).
        public Text cameraVariantEnergyNote;
        // The key-hint line - hidden together with the button in scenes where camera
        // variant switching is locked off (the economy test scene).
        public GameObject cameraVariantHint;

        [Header("Win")]
        // Hidden by default, inside PausePanel - The Gauntlet's finish line
        // (GauntletFinishLine) shows it via ShowWin(). Lives here rather than on its own
        // component since the win screen IS the pause screen, just with this one extra label.
        public Text winLabel;

        bool isPaused;

        void OnEnable()
        {
            pauseAction?.action?.Enable();
        }

        void OnDisable()
        {
            pauseAction?.action?.Disable();
        }

        void Start()
        {
            pausePanel?.SetActive(false);
            controlsPanel?.SetActive(false);
            scenesPanel?.SetActive(false);
            winLabel?.gameObject.SetActive(false);

            // Everything that should vanish while the menu is open: the prefab's wired
            // corner hint plus any scene-local hint canvases found by name.
            hintObjects.Clear();
            if (controlsHintLabel != null) hintObjects.Add(controlsHintLabel);
            foreach (string hintName in sceneHintObjectNames)
            {
                GameObject sceneHint = GameObject.Find(hintName);
                if (sceneHint != null) hintObjects.Add(sceneHint);
            }
        }

        void SetHintsVisible(bool visible)
        {
            foreach (GameObject hint in hintObjects)
            {
                if (hint != null) hint.SetActive(visible);
            }
        }

        // The finish line's win state - the ordinary pause screen with the win label showing.
        // Not one-shot-guarded here; GauntletFinishLine only ever calls it once.
        public void ShowWin()
        {
            winLabel?.gameObject.SetActive(true);
            if (!isPaused) Pause();
        }

        void Update()
        {
            // The direct Start-button read is the Always Mouse escape hatch: that mode masks
            // every gamepad binding on the shared action asset (KineticCubeController.
            // ApplyGamepadBlock), but the MENUS must stay controller-usable - including
            // OPENING this one to turn the mode back off. Same frame as an unmasked action
            // press it's still a single toggle (one if).
            // The first-boot intro overlay owns ALL input while it shows.
            if (AimIntroScreen.InputBlocked) return;

            bool startPressed = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if (startPressed || (pauseAction != null && pauseAction.action != null && pauseAction.action.WasPressedThisFrame()))
            {
                TogglePause();
            }
        }

        public void TogglePause()
        {
            if (isPaused) Resume();
            else Pause();
        }

        void Pause()
        {
            isPaused = true;
            Time.timeScale = 0f;
            controlsPanel?.SetActive(false);
            pausePanel?.SetActive(true);
            SetHintsVisible(false);
            RefreshCameraVariantLabel();
            infoButton?.SetActive(FindAnyObjectByType<AimIntroScreen>(FindObjectsInactive.Include) != null);
            Select(firstPauseButton);
        }

        void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;
            pausePanel?.SetActive(false);
            controlsPanel?.SetActive(false);
            scenesPanel?.SetActive(false);
            SetHintsVisible(true);
            // Un-pausing after winning keeps playing in the finished level, which is fine - but
            // the win label shouldn't stick around on the NEXT pause after that.
            winLabel?.gameObject.SetActive(false);
            Select(null);
        }

        public void OnRestartClicked()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void OnControlsClicked()
        {
            pausePanel?.SetActive(false);
            controlsPanel?.SetActive(true);
            Select(firstControlsButton);
        }

        public void OnControlsBackClicked()
        {
            controlsPanel?.SetActive(false);
            pausePanel?.SetActive(true);
            Select(firstPauseButton);
        }

        // Opens the playtest feedback form in the system browser. The game keeps running
        // paused underneath - testers alt-tab back when done.
        public void OnFeedbackClicked()
        {
            Application.OpenURL(feedbackFormUrl);
        }

        [Header("Info Button (wired by setup)")]
        // Shown only in scenes that carry an intro/explainer screen - reopens it on demand.
        public GameObject infoButton;

        public void OnInfoClicked()
        {
            var intro = FindAnyObjectByType<AimIntroScreen>(FindObjectsInactive.Include);
            intro?.Open();
        }

        // Cycles the aim-camera variant (A -> B -> C) from the pause menu. The variant
        // controller itself refuses while an aim window is open.
        public void OnCameraVariantClicked()
        {
            var variants = FindAnyObjectByType<KineticEnergy.Camera.AimCameraVariantController>(FindObjectsInactive.Include);
            if (variants == null) return;
            variants.CycleVariant();
            RefreshCameraVariantLabel();
        }

        void RefreshCameraVariantLabel()
        {
            var variants = FindAnyObjectByType<KineticEnergy.Camera.AimCameraVariantController>(FindObjectsInactive.Include);
            bool switchingAvailable = variants != null && variants.variantSwitchingEnabled;

            // Camera-locked scenes (the economy test) hide the whole selector block.
            if (cameraVariantLabel != null && cameraVariantLabel.transform.parent != null)
            {
                cameraVariantLabel.transform.parent.gameObject.SetActive(switchingAvailable);
            }
            cameraVariantHint?.SetActive(switchingAvailable);
            if (!switchingAvailable)
            {
                if (cameraVariantEnergyNote != null) cameraVariantEnergyNote.text = "";
                return;
            }

            if (cameraVariantLabel != null)
            {
                cameraVariantLabel.text = "Camera: " + variants.CurrentLabel;
            }
            if (cameraVariantEnergyNote != null)
            {
                cameraVariantEnergyNote.text = variants.EnergyControlsNote;
            }
        }

        public void OnScenesClicked()
        {
            pausePanel?.SetActive(false);
            scenesPanel?.SetActive(true);
            Select(firstScenesButton);
        }

        public void OnScenesBackClicked()
        {
            scenesPanel?.SetActive(false);
            pausePanel?.SetActive(true);
            Select(firstPauseButton);
        }

        // Called by each per-scene button in ScenesPanel (see KineticEnergySetup.BuildPauseSystem)
        // with that scene's name baked in as a persistent listener argument - resets timeScale
        // first for the same reason OnRestartClicked does: this component only ever calls
        // LoadScene while paused (Time.timeScale == 0f), and leaving it at 0 would freeze the
        // destination scene's own physics/Update-driven logic the instant it loads.
        public void LoadSceneByName(string sceneName)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(sceneName);
        }

        // The Gauntlet's two variants are the same scene under one flag - these bake the
        // tester's choice into the static selection the scene's run logger consumes on load.
        public void LoadSceneVariantA(string sceneName)
        {
            KineticEnergy.Level.SlowdownVariantSelection.PendingVariantB = false;
            LoadSceneByName(sceneName);
        }

        public void LoadSceneVariantB(string sceneName)
        {
            KineticEnergy.Level.SlowdownVariantSelection.PendingVariantB = true;
            LoadSceneByName(sceneName);
        }

        public void OnQuitClicked()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void Select(GameObject go)
        {
            if (EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(go);
        }
    }
}
