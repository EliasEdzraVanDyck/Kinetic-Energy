using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // Level 8's end pad: touching it hands the run to the stage controller, which either
    // reloads the level on the next challenge or shows the win screen after the last one.
    // One-shot per scene load; identifies the player by component like every trigger here.
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
