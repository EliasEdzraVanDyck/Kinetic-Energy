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
            // Triggers are not floor: checkpoint pads and finish volumes sit right where the
            // player walks, and the shadow used to climb onto them instead of resting on the
            // ground underneath.
            bool didHit = Physics.Raycast(player.position, Vector3.down, out RaycastHit hit, maxDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider != playerCollider;

            shadowVisual.gameObject.SetActive(didHit);
            if (!didHit) return;

            // A MOVING platform is rendered at its interpolated pose, but the raycast reports
            // the PHYSICS pose, which only steps once per fixed tick - so the shadow sat at a
            // slightly different place than the platform being drawn under it and vibrated as
            // the two diverged and re-synced. Shifting by that same difference puts the
            // shadow on the platform as DRAWN.
            Rigidbody hitBody = hit.collider.attachedRigidbody;
            Vector3 renderOffset = hitBody != null && hitBody.interpolation != RigidbodyInterpolation.None
                ? hitBody.transform.position - hitBody.position
                : Vector3.zero;

            // Laid flat ON the surface it found, not merely above it - a tilted ledge gets a
            // tilted shadow, and the lift is along that surface rather than world up.
            Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
            shadowVisual.position = hit.point + renderOffset + normal * surfaceOffset;
            shadowVisual.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        }
    }
}
