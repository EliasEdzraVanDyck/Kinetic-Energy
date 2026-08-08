using UnityEngine;
using UnityEngine.SceneManagement;

namespace KineticEnergy.UI
{
    // The playtest build's boot menu (MainMenu.unity): explains the two-control-scheme test and
    // offers Start (into the Tutorial), Feedback (opens feedbackUrl in the browser), Scenes
    // (direct access to the four playtest scenes), and Quit - the pause menu's layout with
    // Restart/Resume replaced by Start. Buttons are wired by KineticEnergySetup with persistent
    // listeners, same as PauseController's.
    public class MainMenuController : MonoBehaviour
    {
        [Header("Panels (wired by setup)")]
        public GameObject menuPanel;
        public GameObject scenesPanel;

        [Header("Flow")]
        [Tooltip("Scene the Start button loads - the first stop of the playtest chain.")]
        public string startSceneName = "Tutorial";
        [Tooltip("Your feedback form's URL - the Feedback button opens it in the default browser. Paste the link here once you have it.")]
        public string feedbackUrl = "";

        void Start()
        {
            // Arriving here from Tutorial2/TestLevel2 can leave the OS cursor locked and, in
            // principle, a stale timeScale - a menu needs both back to normal.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            if (menuPanel != null) menuPanel.SetActive(true);
            if (scenesPanel != null) scenesPanel.SetActive(false);
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
        }

        public void OnScenesBackClicked()
        {
            if (scenesPanel != null) scenesPanel.SetActive(false);
            if (menuPanel != null) menuPanel.SetActive(true);
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
