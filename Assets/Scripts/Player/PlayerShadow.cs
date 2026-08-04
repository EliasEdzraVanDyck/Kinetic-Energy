using UnityEngine;

namespace KineticEnergy.Player
{
    // A simple ground-contact shadow: casts straight down from the player every frame and
    // positions a flat disc at whatever it hits, so there's always a visual sense of "directly
    // below me" - most useful mid-flight, when the cube can be well above anything it's about to
    // land on and would otherwise give no sense of how far below the ground actually is.
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

            // A raycast originating INSIDE a collider does not register a hit against that same
            // collider (unlike a shape cast, which can - see KineticCubeController's own ground
            // check for where that distinction mattered) - starting exactly at the player's own
            // center already skips its own collider without needing an explicit layer mask. The
            // collider comparison below is just a defensive backstop against that assumption.
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
