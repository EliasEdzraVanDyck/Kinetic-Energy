using UnityEngine;
using UnityEngine.SceneManagement;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A trigger that loads another scene when the player touches it - The Quarry's
    // return-to-menu pad. One-shot per scene load; identifies the player by component
    // rather than tag, the pattern this codebase uses everywhere.
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
