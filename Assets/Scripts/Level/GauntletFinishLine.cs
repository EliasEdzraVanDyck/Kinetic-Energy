using UnityEngine;
using KineticEnergy.Player;
using KineticEnergy.UI;

namespace KineticEnergy.Level
{

    public class GauntletFinishLine : MonoBehaviour
    {
        public GauntletRunLogger logger;
        public PauseController pauseController;

        bool triggered;

        void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            if (other.GetComponent<KineticCubeController>() == null) return;

            triggered = true;
            logger?.CompleteRun();
            pauseController?.ShowWin();
        }
    }
}

