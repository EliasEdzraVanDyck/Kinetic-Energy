using UnityEngine;
using UnityEngine.SceneManagement;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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
