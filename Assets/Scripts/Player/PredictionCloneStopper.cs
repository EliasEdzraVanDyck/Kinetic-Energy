using UnityEngine;

namespace KineticEnergy.Player
{
    // Lives on KineticCubeController's hidden real-physics prediction clone. The clone has no
    // other script, so it needs its own copy of the same crash-stop rule the real cube uses
    // (KineticCubeController.OnCollisionEnter) - otherwise it would slide/settle via ordinary
    // friction instead of stopping instantly, and the preview wouldn't match what actually
    // happens when the real cube crashes. Any surface stops it, walls included (direct request:
    // "when you touch a wall after launching you should stick to it, no matter the control
    // scheme") - matches the real cube's any-surface sticking exactly.
    public class PredictionCloneStopper : MonoBehaviour
    {
        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void OnCollisionEnter(Collision collision)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
