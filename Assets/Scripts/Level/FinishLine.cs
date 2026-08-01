using UnityEngine;
using UnityEngine.SceneManagement;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // Lives on an invisible trigger volume over the finish platform (see
    // LevelGenerator.BuildFinishPad). Identifies the player by component rather than tag/name -
    // matches this codebase's existing pattern (DestroyIfExists by name, GetComponent checks)
    // instead of relying on Unity's separate tag system, which nothing else here uses.
    public class FinishLine : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<KineticCubeController>() == null) return;

            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
