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

        // Raised after any hazard respawn completes - Level 8's challenge controller
        // listens, resetting its walls so a retry never faces mid-run hazard state.
        public static event System.Action PlayerRespawned;

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
            // position - ground and flying alike - and clears live projectiles, so a
            // retry faces the level as it started.
            //
            // EXCEPT where a section index owns the level: it revives only the enemies from
            // the active section onward, so ground you have already cleared stays cleared.
            // Doing it here as well would blanket-revive them and undo exactly that, so the
            // decision is handed over (it listens for PlayerRespawned below).
            bool sectionsOwnRespawns = Object.FindAnyObjectByType<LevelSectionController>() != null;
            if (!sectionsOwnRespawns)
            {
                foreach (Enemy enemy in Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include))
                {
                    enemy.ResetToSpawn();
                }
                foreach (FlyingEnemy flyer in Object.FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include))
                {
                    flyer.ResetToSpawn();
                }
                foreach (TurretEnemy turret in Object.FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include))
                {
                    turret.ResetToSpawn();
                }
            }
            foreach (EnemyProjectile projectile in Object.FindObjectsByType<EnemyProjectile>(FindObjectsInactive.Exclude))
            {
                Destroy(projectile.gameObject);
            }

            PlayerRespawned?.Invoke();
        }
    }
}
