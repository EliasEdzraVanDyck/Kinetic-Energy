using UnityEngine;
using UnityEngine.SceneManagement;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // Playtest-flow finish trigger: instead of opening the win screen (FinishLineWin), touching
    // it loads the next scene in the chain - MainMenu -> Tutorial -> TestLevel1 -> Tutorial2 ->
    // TestLevel2 -> back to MainMenu. One-shot per scene load; identifies the player by
    // component, matching FinishLine's reasoning.
    public class FinishLineNextScene : MonoBehaviour
    {
        [Tooltip("Scene name loaded when the player reaches this finish - must be in Build Settings.")]
        public string nextSceneName;

        bool triggered;

        void OnTriggerEnter(Collider other)
        {
            if (triggered || string.IsNullOrEmpty(nextSceneName)) return;
            if (other.GetComponent<KineticCubeController>() == null) return;

            triggered = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
