using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using KineticEnergy.Level;

namespace KineticEnergy.UI
{
    // The boot menu (MainMenu.unity): one button per test level - The Quarry, and The
    // Gauntlet once per slowdown variant - plus Quit. Buttons are wired by the setup script
    // with persistent listeners, same as PauseController's.
    public class MainMenuController : MonoBehaviour
    {
        [Header("Panels (wired by setup)")]
        public GameObject menuPanel;

        [Header("Controller Navigation (wired by setup)")]
        // Gamepad support: the Dpad/stick navigates between Buttons via Unity's automatic
        // navigation, but only once something is SELECTED - this is the button focused when
        // the menu opens.
        public GameObject firstMenuButton;

        void Start()
        {
            // Arriving here from a level can leave the OS cursor locked and a stale
            // timeScale - a menu needs both back to normal.
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

        // The Gauntlet's two variants are the same scene under one flag - these bake the
        // tester's choice into the static selection the scene's run logger consumes on load.
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

        public void OnQuitClicked()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
