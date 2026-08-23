using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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

