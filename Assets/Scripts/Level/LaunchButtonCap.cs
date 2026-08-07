using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // Lives on the button cap itself (collision callbacks fire on the collider's own
    // GameObject) and just forwards the player's touch to the parent LaunchButton. Identifies
    // the player by component rather than tag, matching FinishLine's reasoning.
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
