using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

    public class ChallengeFinishTrigger : MonoBehaviour
    {
        bool triggered;

        void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            if (other.GetComponent<KineticCubeController>() == null) return;
            ChallengeStageController stages = FindAnyObjectByType<ChallengeStageController>();
            if (stages == null) return;
            triggered = true;
            stages.OnFinishReached();
        }
    }
}

