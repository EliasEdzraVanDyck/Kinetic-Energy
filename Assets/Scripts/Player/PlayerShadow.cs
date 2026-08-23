using UnityEngine;

namespace KineticEnergy.Player
{

    public class PlayerShadow : MonoBehaviour
    {
        public Transform player;
        public Transform shadowVisual;
        public float maxDistance = 500f;
        public float surfaceOffset = 0.02f;

        Collider playerCollider;

        void Awake()
        {
            if (player != null) playerCollider = player.GetComponent<Collider>();
        }

        void LateUpdate()
        {
            if (player == null || shadowVisual == null) return;

            bool didHit = Physics.Raycast(player.position, Vector3.down, out RaycastHit hit, maxDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider != playerCollider;

            shadowVisual.gameObject.SetActive(didHit);
            if (!didHit) return;

            Rigidbody hitBody = hit.collider.attachedRigidbody;
            Vector3 renderOffset = hitBody != null && hitBody.interpolation != RigidbodyInterpolation.None
                ? hitBody.transform.position - hitBody.position
                : Vector3.zero;

            Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
            shadowVisual.position = hit.point + renderOffset + normal * surfaceOffset;
            shadowVisual.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        }
    }
}

