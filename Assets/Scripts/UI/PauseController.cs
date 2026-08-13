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
            RefreshCameraVariantLabel();
            Select(firstPauseButton);
        }

        void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;
            pausePanel?.SetActive(false);
            controlsPanel?.SetActive(false);
            scenesPanel?.SetActive(false);
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
            if (cameraVariantLabel == null) return;
            var variants = FindAnyObjectByType<KineticEnergy.Camera.AimCameraVariantController>(FindObjectsInactive.Include);
            cameraVariantLabel.text = variants != null ? "Camera: " + variants.CurrentLabel : "Camera: -";
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
