using UnityEngine;
using KineticEnergy.Player;
using KineticEnergy.UI;

namespace KineticEnergy.Level
{
    // FastPacedLevel's finish trigger - opens the pause screen with the "You Win!" label (see
    // PauseController.ShowWin) instead of reloading the scene the way the other levels'
    // FinishLine does (direct request). One-shot: the trigger can re-fire if the player resumes
    // and drifts back through it, but the win screen only ever opens once per scene load.
    // Identifies the player by component rather than tag, matching FinishLine's own reasoning.
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
