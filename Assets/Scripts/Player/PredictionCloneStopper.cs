using UnityEngine;

namespace KineticEnergy.Player
{
    // Lives on KineticCubeController's hidden real-physics prediction clone. The clone has no
    // other script, so it needs its own copy of the same crash rule the real cube uses
    // (KineticCubeController.OnCollisionEnter) - otherwise the preview wouldn't match what
    // actually happens when the real cube crashes. Mirrors that method's floor/ceiling-vs-wall
    // split exactly: a wall hit (contact normal mostly horizontal) sheds velocity and raises drag
    // instead of stopping outright, matching the real cube's "you just fall slower instead of not
    // at all" wall behavior (direct request) - only a floor/ceiling hit stops the clone dead.
    public class PredictionCloneStopper : MonoBehaviour
    {
        // Copied from the real controller's own tunables by EnsurePredictionClone every time the
        // clone is (re)created, so the two never drift apart - see
        // KineticCubeController.wallNormalThreshold's own comment for what each one means.
        [HideInInspector] public float wallNormalThreshold = 0.5f;
        [HideInInspector] public float wallCrashVelocityRetention = 0.4f;
        [HideInInspector] public float wallCrashFallDamping = 3f;

        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void OnCollisionEnter(Collision collision)
        {
            Vector3 contactNormal = collision.GetContact(0).normal;
            if (Mathf.Abs(contactNormal.y) < wallNormalThreshold)
            {
                Vector3 remainingVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, contactNormal);
                rb.linearVelocity = remainingVelocity * wallCrashVelocityRetention;
                rb.linearDamping = wallCrashFallDamping;
                return;
            }

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
