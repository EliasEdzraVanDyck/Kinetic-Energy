using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A hazard surface: any player contact instantly respawns them at respawnPoint (for
    // now - a damage/health system can hook in here later). Works as a solid collider or a
    // trigger volume alike, and on the object itself or a parent container covering many
    // walls.
    public class DamageWalls : MonoBehaviour
    {
        [Tooltip("Where the player reappears - wired per scene. Falls back to the world origin if left empty.")]
        public Transform respawnPoint;

        void OnCollisionEnter(Collision collision)
        {
            TryRespawn(collision.collider);
        }

        void OnTriggerEnter(Collider other)
        {
            TryRespawn(other);
        }

        void TryRespawn(Collider other)
        {
            KineticCubeController controller = other.GetComponent<KineticCubeController>();
            if (controller == null) return;
            controller.RespawnAtPoint(respawnPoint != null ? respawnPoint.position : Vector3.zero);

            // The player's respawn also resets every (surviving) enemy to its original
            // position, so a retry faces the level as it started.
            foreach (Enemy enemy in Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include))
            {
                enemy.ResetToSpawn();
            }
        }
    }
}
