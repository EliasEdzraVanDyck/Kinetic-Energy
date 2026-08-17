using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A hovering blue pad over a section's platform: touching it makes that section the
    // place you respawn. Jumping to a section from the pause menu claims its checkpoint
    // too, so the menu and the pads always agree on where "back" is.
    public class Checkpoint : MonoBehaviour
    {
        [Tooltip("Where a death sends the player once this checkpoint is claimed. Empty = this object's own position.")]
        public Transform respawnPoint;

        LevelSectionController sections;

        public Transform RespawnTarget => respawnPoint != null ? respawnPoint : transform;
        public bool Claimed { get; private set; }

        void Start()
        {
            sections = FindAnyObjectByType<LevelSectionController>();
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<KineticCubeController>() == null) return;
            if (sections == null) sections = FindAnyObjectByType<LevelSectionController>();
            sections?.SetActiveRespawn(RespawnTarget);
            SetClaimed(true);
        }

        // Claiming HIDES the pad - it has done its job, and a vanished pad is the clearest
        // signal that this section is now where you come back to. The trigger stays live so
        // a reset can hand the pad back.
        public void SetClaimed(bool claimed)
        {
            Claimed = claimed;
            foreach (Renderer padRenderer in GetComponentsInChildren<Renderer>(true))
            {
                padRenderer.enabled = !claimed;
            }
        }
    }
}
