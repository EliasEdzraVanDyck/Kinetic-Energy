using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace KineticEnergy.UI
{

    public class MainMenuController : MonoBehaviour
    {
        [Header("Panels (wired by setup)")]
        public GameObject menuPanel;
        public GameObject scenesPanel;

        [Header("Controller Navigation (wired by setup)")]

        public GameObject firstMenuButton;
        public GameObject firstScenesButton;

        [Header("Flow")]
        [Tooltip("Scene the Start button loads - the first stop of the playtest chain.")]
        public string startSceneName = "Tutorial";
        [Tooltip("Your feedback form's URL - the Feedback button opens it in the default browser. Paste the link here once you have it.")]
        public string feedbackUrl = "";

        void Start()
        {

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            if (menuPanel != null) menuPanel.SetActive(true);
            if (scenesPanel != null) scenesPanel.SetActive(false);
            Select(firstMenuButton);
        }

        static void Select(GameObject button)
        {
            if (EventSystem.current == null || button == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(button);
        }

        public void OnStartClicked()
        {
            SceneManager.LoadScene(startSceneName);
        }

        public void OnFeedbackClicked()
        {
            if (string.IsNullOrEmpty(feedbackUrl))
            {
                Debug.LogWarning("MainMenuController: no feedbackUrl assigned yet - set it on the MainMenuUI object.");
                return;
            }
            Application.OpenURL(feedbackUrl);
        }

        public void OnScenesClicked()
        {
            if (menuPanel != null) menuPanel.SetActive(false);
            if (scenesPanel != null) scenesPanel.SetActive(true);
            Select(firstScenesButton);
        }

        public void OnScenesBackClicked()
        {
            if (scenesPanel != null) scenesPanel.SetActive(false);
            if (menuPanel != null) menuPanel.SetActive(true);
            Select(firstMenuButton);
        }

        public void OnQuitClicked()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        public void LoadSceneByName(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
