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
        // The stopping contact's surface normal - read by KineticCubeController after each
        // prediction so the cross-and-ring marker can lie flat against whatever face the shot
        // actually lands on (wall, floor, ceiling alike). Cleared via ClearContact before every
        // prediction run.
        public Vector3 LastContactNormal { get; private set; } = Vector3.up;
        // The PROXY collider the clone stopped on - the controller maps it back to the real
        // scene object so the preview can judge the landing (deadly? side of grounded geometry?).
        public Collider LastContactCollider { get; private set; }
        public bool HasContact { get; private set; }

        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        public void ClearContact()
        {
            HasContact = false;
            LastContactNormal = Vector3.up;
            LastContactCollider = null;
        }

        void OnCollisionEnter(Collision collision)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            if (collision.contactCount > 0)
            {
                LastContactNormal = collision.GetContact(0).normal;
                LastContactCollider = collision.collider;
                HasContact = true;
            }
        }
    }
}
