using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // The self-contained finish (Level1Economy / Level1Challenge): touching it opens the
    // pause screen as a locked "You win!" - no scene change, no Resume. One-shot per
    // scene load; identifies the player by component like every trigger here.
    public class WinOnFinish : MonoBehaviour
    {
        bool triggered;

        void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            if (other.GetComponent<KineticCubeController>() == null) return;
            var pause = FindAnyObjectByType<KineticEnergy.UI.PauseController>(FindObjectsInactive.Include);
            if (pause == null) return;
            triggered = true;
            pause.ShowWinLocked();
        }
    }
}
