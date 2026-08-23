using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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

