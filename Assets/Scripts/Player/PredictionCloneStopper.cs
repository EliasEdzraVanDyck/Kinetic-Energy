using UnityEngine;

namespace KineticEnergy.Player
{

    public class PredictionCloneStopper : MonoBehaviour
    {

        public Vector3 LastContactNormal { get; private set; } = Vector3.up;
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
        }

        void OnCollisionEnter(Collision collision)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            if (collision.contactCount > 0)
            {
                LastContactNormal = collision.GetContact(0).normal;
                HasContact = true;
            }
        }
    }
}
