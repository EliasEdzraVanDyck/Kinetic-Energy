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
        public GameObject cameraSettingsPanel;
        [Tooltip("Level1Challenge's challenge-variant list (a scene-only screen) - left empty everywhere else.")]
        public GameObject variantsPanel;
        [Tooltip("LevelElementsTest's section-jump list (a scene-only screen) - left empty everywhere else.")]
        public GameObject sectionsPanel;
        public GameObject firstPauseButton;
        public GameObject firstControlsButton;
        public GameObject firstScenesButton;
        public GameObject firstCameraSettingsButton;
        public GameObject firstVariantsButton;
        public GameObject firstSectionsButton;

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

        // Read by systems that must distinguish a genuine pause from other timeScale-0
        // freezes (the midair aim's bullet time) - e.g. the economy harness's real-time
        // combo window.
        public bool IsPaused => isPaused;

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
            cameraSettingsPanel?.SetActive(false);
            variantsPanel?.SetActive(false);
            sectionsPanel?.SetActive(false);
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

        // The LOCKED win (the self-contained Level1 test scenes): the pause screen with
        // its title reading "You win!" and the Resume button gone - the run is over.
        // Everything else on the menu (Restart, BuildInfo, Scenes, Quit) works as usual;
        // a Restart reloads the scene, which restores the title and the button.
        public void ShowWinLocked()
        {
            if (pausePanel != null)
            {
                Text titleText = pausePanel.transform.Find("Title")?.GetComponent<Text>();
                if (titleText != null) titleText.text = "You win!";
                Transform resume = pausePanel.transform.Find("ResumeButton");
                if (resume != null) resume.gameObject.SetActive(false);
            }
            if (!isPaused) Pause();
            // Pause() selected the (now hidden) Resume - hand the gamepad focus to Restart.
            if (pausePanel != null)
            {
                Transform restart = pausePanel.transform.Find("RestartButton");
                if (restart != null) Select(restart.gameObject);
            }
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
            cameraSettingsPanel?.SetActive(false);
            variantsPanel?.SetActive(false);
            sectionsPanel?.SetActive(false);
            pausePanel?.SetActive(true);
            SetHintsVisible(false);
            RefreshCameraVariantLabel();
            infoButton?.SetActive(FindAnyObjectByType<AimIntroScreen>(FindObjectsInactive.Include) != null);
            Select(firstPauseButton);
        }

        public void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;
            pausePanel?.SetActive(false);
            controlsPanel?.SetActive(false);
            scenesPanel?.SetActive(false);
            cameraSettingsPanel?.SetActive(false);
            variantsPanel?.SetActive(false);
            sectionsPanel?.SetActive(false);
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

        // The section list (LevelElementsTest only). Picking one teleports rather than
        // reloading, so the menu closes itself and hands play straight back.
        public void OnSectionsClicked()
        {
            pausePanel?.SetActive(false);
            sectionsPanel?.SetActive(true);
            Select(firstSectionsButton);
        }

        public void OnSectionsBackClicked()
        {
            sectionsPanel?.SetActive(false);
            pausePanel?.SetActive(true);
            Select(firstPauseButton);
        }

        // Wired AFTER the section jump on each section button: the teleport already put
        // the player where they asked to be, so staying paused would just be in the way.
        public void ResumeAfterSectionJump()
        {
            sectionsPanel?.SetActive(false);
            Resume();
        }

        // The challenge-variant list (Level1Challenge only) - picking one restarts the
        // scene on that variant, so the panel itself just navigates.
        public void OnVariantsClicked()
        {
            pausePanel?.SetActive(false);
            variantsPanel?.SetActive(true);
            Select(firstVariantsButton);
        }

        public void OnVariantsBackClicked()
        {
            variantsPanel?.SetActive(false);
            pausePanel?.SetActive(true);
            Select(firstPauseButton);
        }

        // The camera-settings sub-screen (the speed sliders), same in/out pattern as the
        // Controls and Scenes panels.
        public void OnCameraSettingsClicked()
        {
            pausePanel?.SetActive(false);
            cameraSettingsPanel?.SetActive(true);
            Select(firstCameraSettingsButton);
        }

        public void OnCameraSettingsBackClicked()
        {
            cameraSettingsPanel?.SetActive(false);
            pausePanel?.SetActive(true);
            Select(firstPauseButton);
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

        // Level 8's challenge-stage buttons - the Gauntlet-variant pattern: bake the
        // choice into the static selection, then (re)load the level, which always means
        // starting the run over on that stage.
        public void LoadChallengeStage1(string sceneName)
        {
            LoadChallengeStage(sceneName, KineticEnergy.Level.ChallengeStage.LimitedSlowdown);
        }

        public void LoadChallengeStage2(string sceneName)
        {
            LoadChallengeStage(sceneName, KineticEnergy.Level.ChallengeStage.OverchargeScatter);
        }

        public void LoadChallengeStage3(string sceneName)
        {
            LoadChallengeStage(sceneName, KineticEnergy.Level.ChallengeStage.ChasingWall);
        }

        public void LoadChallengeStage4(string sceneName)
        {
            LoadChallengeStage(sceneName, KineticEnergy.Level.ChallengeStage.SealingWalls);
        }

        public void LoadChallengeStage5(string sceneName)
        {
            LoadChallengeStage(sceneName, KineticEnergy.Level.ChallengeStage.ShrinkingPlatforms);
        }

        void LoadChallengeStage(string sceneName, KineticEnergy.Level.ChallengeStage stage)
        {
            KineticEnergy.Level.ChallengeStageSelection.PendingStage = stage;
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
