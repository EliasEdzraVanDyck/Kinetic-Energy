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

        public GameObject controlsHintLabel;
        [Tooltip("Scene-local hint objects (found by name at Start) that also hide while the menu is open - e.g. QuarryNew's QuarryIntroHud.")]
        public string[] sceneHintObjectNames = { "QuarryIntroHud" };

        readonly List<GameObject> hintObjects = new List<GameObject>();

        [Header("Controls Text")]

        public Text controlsBodyText;

        [Header("Feedback")]
        [Tooltip("Opened in the system browser by the pause menu's Feedback button.")]
        public string feedbackFormUrl = "https://forms.gle/c7TVCoLzkktTWJFc7";

        [Header("Aim Camera Variant (wired by setup)")]

        public Text cameraVariantLabel;

        public Text cameraVariantEnergyNote;

        public GameObject cameraVariantHint;

        [Header("Win")]

        public Text winLabel;

        bool isPaused;

        public bool IsPaused => isPaused;

        void OnEnable()
        {
            pauseAction?.action?.Enable();

#if UNITY_WEBGL && !UNITY_EDITOR

            if (pauseAction != null && pauseAction.action != null)
            {
                for (int i = 0; i < pauseAction.action.bindings.Count; i++)
                {
                    if (pauseAction.action.bindings[i].path.ToLowerInvariant().Contains("escape"))
                    {
                        pauseAction.action.ApplyBindingOverride(i, "<Keyboard>/backquote");
                    }
                }
            }
#endif
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
            if (variantsPanel != null) variantsPanel.SetActive(false);
            if (sectionsPanel != null) sectionsPanel.SetActive(false);
            winLabel?.gameObject.SetActive(false);

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

        public void ShowWin()
        {
            winLabel?.gameObject.SetActive(true);
            if (!isPaused) Pause();
        }

        public void ShowWinLocked()
        {
            if (pausePanel != null)
            {
                Text titleText = pausePanel.transform.Find("Title")?.GetComponent<Text>();
                if (titleText != null)
                {
                    titleText.text = "You Win!";
                    ShowWinSubtitle(titleText);
                }
                Transform resume = pausePanel.transform.Find("ResumeButton");
                if (resume != null) resume.gameObject.SetActive(false);
            }
            if (!isPaused) Pause();

            if (pausePanel != null)
            {
                Transform restart = pausePanel.transform.Find("RestartButton");
                if (restart != null) Select(restart.gameObject);
            }
        }

        [Tooltip("The line under the win title. Slightly smaller than the title, in the same font and colour.")]
        public string winSubtitle = "Thanks for playing!";

        void ShowWinSubtitle(Text titleText)
        {
            RectTransform titleRect = titleText.rectTransform;
            Transform existing = titleRect.parent.Find("WinSubtitle");
            Text subtitle;
            if (existing != null) subtitle = existing.GetComponent<Text>();
            else
            {
                GameObject go = new GameObject("WinSubtitle", typeof(RectTransform));
                go.transform.SetParent(titleRect.parent, false);
                subtitle = go.AddComponent<Text>();
                subtitle.font = titleText.font;
                subtitle.color = titleText.color;
                subtitle.alignment = titleText.alignment;
                subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
                subtitle.verticalOverflow = VerticalWrapMode.Overflow;
                subtitle.raycastTarget = false;

                RectTransform rect = go.GetComponent<RectTransform>();
                rect.anchorMin = titleRect.anchorMin;
                rect.anchorMax = titleRect.anchorMax;
                rect.pivot = titleRect.pivot;
                rect.sizeDelta = titleRect.sizeDelta;

                rect.anchoredPosition = titleRect.anchoredPosition
                    - new Vector2(0f, titleRect.sizeDelta.y * 0.85f);
                subtitle.fontSize = Mathf.Max(Mathf.RoundToInt(titleText.fontSize * 0.55f), 10);
            }
            subtitle.text = winSubtitle;
            subtitle.gameObject.SetActive(true);
        }

        void Update()
        {

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
            if (variantsPanel != null) variantsPanel.SetActive(false);
            if (sectionsPanel != null) sectionsPanel.SetActive(false);
            pausePanel?.SetActive(true);
            SetHintsVisible(false);
            RefreshCameraVariantLabel();

            infoButton?.SetActive(buildInfoButtonEnabled
                && FindAnyObjectByType<AimIntroScreen>(FindObjectsInactive.Include) != null);
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
            if (variantsPanel != null) variantsPanel.SetActive(false);
            if (sectionsPanel != null) sectionsPanel.SetActive(false);
            SetHintsVisible(true);

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

        public void OnFeedbackClicked()
        {
            Application.OpenURL(feedbackFormUrl);
        }

        [Header("Info Button (wired by setup)")]

        public GameObject infoButton;
        [Tooltip("Master switch for the BuildInfo button. OFF keeps it hidden even though the intro overlay exists in the scene - nothing re-activates it while this is unticked.")]
        public bool buildInfoButtonEnabled = false;

        public void OnInfoClicked()
        {
            var intro = FindAnyObjectByType<AimIntroScreen>(FindObjectsInactive.Include);
            intro?.Open();
        }

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

        public void OnSectionsClicked()
        {
            pausePanel?.SetActive(false);
            if (sectionsPanel != null) sectionsPanel.SetActive(true);
            Select(firstSectionsButton);
        }

        public void OnSectionsBackClicked()
        {
            if (sectionsPanel != null) sectionsPanel.SetActive(false);
            pausePanel?.SetActive(true);
            Select(firstPauseButton);
        }

        public void ResumeAfterSectionJump()
        {
            if (sectionsPanel != null) sectionsPanel.SetActive(false);
            Resume();
        }

        public void OnVariantsClicked()
        {
            pausePanel?.SetActive(false);
            if (variantsPanel != null) variantsPanel.SetActive(true);
            Select(firstVariantsButton);
        }

        public void OnVariantsBackClicked()
        {
            if (variantsPanel != null) variantsPanel.SetActive(false);
            pausePanel?.SetActive(true);
            Select(firstPauseButton);
        }

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

        public void LoadSceneByName(string sceneName)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(sceneName);
        }

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

#if UNITY_WEBGL && !UNITY_EDITOR

        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void CloseGameTab();
#endif

        public void OnQuitClicked()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
            CloseGameTab();
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

