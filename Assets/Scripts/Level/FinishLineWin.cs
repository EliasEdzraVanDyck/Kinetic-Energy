using UnityEngine;
using KineticEnergy.Player;
using KineticEnergy.UI;

namespace KineticEnergy.Level
{

    public class FinishLineWin : MonoBehaviour
    {
        public PauseController pauseController;

        bool triggered;

        void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            if (other.GetComponent<KineticCubeController>() == null) return;

            triggered = true;
            pauseController?.ShowWin();
        }
    }
}
