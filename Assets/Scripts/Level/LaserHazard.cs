using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A laser beam that HURTS instead of killing: touching it shoves the player, drains
    // energy and locks launching for a moment - exactly what an enemy body-check or an
    // enemy projectile does. Sits on the gate's beam root in place of DamageWalls, so a
    // mistimed run costs you a chunk of tank and your position instead of the whole run.
    //
    // Works as a solid collider or a trigger volume alike (the beams are kinematic).
    public class LaserHazard : MonoBehaviour
    {
        [Tooltip("Impulse applied to the player on contact - the enemy-projectile value by default.")]
        public float knockbackForce = 22f;
        [Tooltip("Energy fraction the player loses per hit.")]
        [Range(0f, 1f)] public float energyDrain = 0.1f;
        [Tooltip("Seconds the player cannot launch or aim after being hit.")]
        public float launchLockSeconds = 0.5f;
        [Tooltip("Seconds before the same beam can hit again - without it a player pushed ALONG the beam gets re-hit every frame.")]
        public float retriggerDelay = 0.6f;
        [Tooltip("How much of the shove points straight up, so a hit lifts the player clear of the beam rather than only sideways.")]
        [Range(0f, 1f)] public float upwardBias = 0.35f;

        float nextHitTime;

        void OnCollisionEnter(Collision collision)
        {
            TryHit(collision.collider);
        }

        void OnCollisionStay(Collision collision)
        {
            TryHit(collision.collider);
        }

        void OnTriggerEnter(Collider other)
        {
            TryHit(other);
        }

        void OnTriggerStay(Collider other)
        {
            TryHit(other);
        }

        void TryHit(Collider other)
        {
            if (Time.unscaledTime < nextHitTime) return;
            KineticCubeController player = other.GetComponent<KineticCubeController>();
            if (player == null) return;
            nextHitTime = Time.unscaledTime + Mathf.Max(retriggerDelay, 0.05f);

            // Shoved OUT of the beam: away from the beam's own axis, plus a lift so the
            // player clears it instead of being scraped along its length.
            Collider ownCollider = GetComponentInChildren<Collider>();
            Vector3 away = ownCollider != null
                ? other.bounds.center - ownCollider.ClosestPoint(other.bounds.center)
                : other.bounds.center - transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -player.transform.forward;
            Vector3 shove = Vector3.Lerp(away.normalized, Vector3.up, Mathf.Clamp01(upwardBias)).normalized;

            player.ApplyEnemyHit(shove * knockbackForce, energyDrain, launchLockSeconds);
        }
    }
}
