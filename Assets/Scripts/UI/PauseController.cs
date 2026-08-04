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
        [TextArea]
        public string controlsText =
            "Left Stick - Aim while charging\n" +
            "Right Stick - Camera\n" +
            "Left Trigger (hold) - Charge Launch\n" +
            "Right Trigger - Fire\n" +
            "Start / Options / Esc - Pause";
        public Text controlsBodyText;

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
            if (controlsBodyText != null) controlsBodyText.text = controlsText;
            pausePanel?.SetActive(false);
            controlsPanel?.SetActive(false);
            scenesPanel?.SetActive(false);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (controlsBodyText != null) controlsBodyText.text = controlsText;
        }
#endif

        void Update()
        {
            if (pauseAction != null && pauseAction.action != null && pauseAction.action.WasPressedThisFrame())
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
            Select(firstPauseButton);
        }

        void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;
            pausePanel?.SetActive(false);
            controlsPanel?.SetActive(false);
            scenesPanel?.SetActive(false);
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
