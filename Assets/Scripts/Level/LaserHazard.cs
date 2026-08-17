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
        [Tooltip("Impulse applied to the player on contact. Softer than an enemy projectile's 22 - a beam nudges you off course rather than throwing you.")]
        public float knockbackForce = 16.5f;
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

            // Thrown BACK the way you came. Pushing "away from the beam" looked right on
            // paper but a fast launch is already past the beam's centre by the time the
            // hit registers, so that vector pointed forwards and the laser flung the
            // player THROUGH the gate. Reversing their travel always reads as being
            // stopped. Read before ApplyEnemyHit, which wipes the velocity.
            Rigidbody body = other.attachedRigidbody;
            Vector3 travel = body != null ? body.linearVelocity : Vector3.zero;
            travel.y = 0f;

            Vector3 back;
            if (travel.sqrMagnitude > 0.01f)
            {
                back = -travel.normalized;
            }
            else
            {
                // Standing in the beam: no travel to reverse, so fall back to the geometry.
                Collider ownCollider = GetComponentInChildren<Collider>();
                Vector3 away = ownCollider != null
                    ? other.bounds.center - ownCollider.ClosestPoint(other.bounds.center)
                    : other.bounds.center - transform.position;
                away.y = 0f;
                back = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
            }

            Vector3 shove = Vector3.Lerp(back, Vector3.up, Mathf.Clamp01(upwardBias)).normalized;

            player.ApplyEnemyHit(shove * knockbackForce, energyDrain, launchLockSeconds);
        }
    }
}
