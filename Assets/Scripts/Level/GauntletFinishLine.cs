using UnityEngine;
using KineticEnergy.Player;
using KineticEnergy.UI;

namespace KineticEnergy.Level
{
    // The Gauntlet's finish trigger: completes the instrumented run (dumping the stats) and
    // opens the pause screen with the "You Win!" label. One-shot per scene load.
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
