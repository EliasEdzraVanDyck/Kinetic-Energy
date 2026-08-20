using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

    public class LaunchButtonCap : MonoBehaviour
    {
        public LaunchButton button;

        void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.GetComponent<KineticCubeController>() == null) return;
            button?.Press();
        }
    }
}
