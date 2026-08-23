using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using KineticEnergy.Level;

namespace KineticEnergy.UI
{

    public class MainMenuController : MonoBehaviour
    {
        [Header("Panels (wired by setup)")]
        public GameObject menuPanel;

        [Header("Controller Navigation (wired by setup)")]

        public GameObject firstMenuButton;

        void Start()
        {

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            if (menuPanel != null) menuPanel.SetActive(true);
            Select(firstMenuButton);
        }

        static void Select(GameObject button)
        {
            if (EventSystem.current == null || button == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(button);
        }

        public void LoadSceneByName(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        public void LoadSceneVariantA(string sceneName)
        {
            SlowdownVariantSelection.PendingVariantB = false;
            SceneManager.LoadScene(sceneName);
        }

        public void LoadSceneVariantB(string sceneName)
        {
            SlowdownVariantSelection.PendingVariantB = true;
            SceneManager.LoadScene(sceneName);
        }

        [Header("Feedback")]
        [Tooltip("Opened in the system browser by the menu's Feedback button.")]
        public string feedbackFormUrl = "https://forms.gle/c7TVCoLzkktTWJFc7";

        public void OnFeedbackClicked()
        {
            Application.OpenURL(feedbackFormUrl);
        }

        public void OnQuitClicked()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}

