using UnityEngine;

namespace KineticEnergy.Player
{
    // Lives on KineticCubeController's hidden real-physics prediction clone. The clone has no
    // other script, so it needs its own copy of the same landing-stop rule the real cube uses
    // (KineticCubeController.OnCollisionEnter) - otherwise it would slide/settle via ordinary
    // friction instead of stopping instantly, and the preview wouldn't match what actually
    // happens when the real cube lands.
    public class PredictionCloneStopper : MonoBehaviour
    {
        public float groundNormalDot = 0.5f;

        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void OnCollisionEnter(Collision collision)
        {
            foreach (ContactPoint contact in collision.contacts)
            {
                if (Vector3.Dot(contact.normal, Vector3.up) > groundNormalDot)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    return;
                }
            }
        }
    }
}
