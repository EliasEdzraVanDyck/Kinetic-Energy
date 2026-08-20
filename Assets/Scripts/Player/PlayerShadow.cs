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

            bool didHit = Physics.Raycast(player.position, Vector3.down, out RaycastHit hit, maxDistance)
                && hit.collider != playerCollider;

            shadowVisual.gameObject.SetActive(didHit);
            if (didHit)
            {
                shadowVisual.position = hit.point + Vector3.up * surfaceOffset;
            }
        }
    }
}
