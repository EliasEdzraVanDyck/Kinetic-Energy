using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // The Gauntlet's beat-5 gate: an invisible trigger that clamps the energy tank down to
    // clampFraction the moment the player passes through. Blunt, but reliable - it
    // guarantees the final beat is always played on a low tank, which is the condition the
    // two slowdown variants only diverge under. Clamping is a min(), so a player arriving
    // even lower keeps their lower value, and re-entries are harmless.
    public class EnergyClampTrigger : MonoBehaviour
    {
        [Tooltip("The tank is clamped down to at most this fraction on entry.")]
        [Range(0f, 1f)] public float clampFraction = 0.25f;

        void OnTriggerEnter(Collider other)
        {
            KineticCubeController controller = other.GetComponent<KineticCubeController>();
            if (controller == null) return;
            controller.ClampEnergyTo(clampFraction);
        }
    }
}
