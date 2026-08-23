using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

    public class DamageWalls : MonoBehaviour
    {
        [Tooltip("Where the player reappears - wired per scene. Falls back to the world origin if left empty.")]
        public Transform respawnPoint;

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

