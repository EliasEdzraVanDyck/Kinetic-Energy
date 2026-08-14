using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // The flying enemy's shot: a red capsule flying in a straight line, long axis pointing
    // along its flight. NOT destroyable by the player - it is a TRIGGER, so launches pass
    // straight through it (no crash, no kill): dodging is the only counterplay. Touching
    // the player hurts (knockback + energy drain + launch lock, like a ground enemy's
    // body-check); touching level geometry, or running out of lifetime, despawns it.
    //
    // Motion runs on WorldMotionTime (min of scaled/unscaled per tick): the shot slows
    // down with the aim's bullet-time but is NOT sped up by the in-flight game speed-up -
    // the project-wide rule for every non-player mover.
    public class EnemyProjectile : MonoBehaviour
    {
        public float speed = 26f;
        public float lifetimeSeconds = 6f;
        public float knockbackForce = 22f;
        [Range(0f, 1f)] public float energyDrain = 0.1f;
        public float launchLockSeconds = 0.5f;

        Vector3 direction = Vector3.forward;
        Vector3 spawnOrigin;
        Rigidbody body;
        float lived;

        static Material sharedMaterial; // one material for every shot, created lazily

        // Builds the whole projectile from a primitive: red capsule, trigger collider,
        // kinematic interpolated rigidbody, long axis rotated onto the flight direction,
        // spawned exactly at the given origin (the enemy's centre). Ordered so that
        // motion, lifetime, and rotation are ALL in place before the cosmetic material -
        // the old order could leave a naked, frozen, up-facing capsule behind if the
        // material step failed.
        public static EnemyProjectile Spawn(Vector3 origin, Vector3 flightDirection, Vector3 bodyScale, Color color,
            float speed, float lifetimeSeconds, float knockbackForce, float energyDrain, float launchLockSeconds)
        {
            Vector3 dir = flightDirection.sqrMagnitude > 0.0001f ? flightDirection.normalized : Vector3.forward;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "EnemyProjectile";
            go.transform.localScale = bodyScale;
            go.transform.position = origin;
            // The capsule primitive's long axis is local Y - point it along the flight.
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            go.GetComponent<Collider>().isTrigger = true;

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            EnemyProjectile projectile = go.AddComponent<EnemyProjectile>();
            projectile.direction = dir;
            projectile.spawnOrigin = origin;
            projectile.speed = speed;
            projectile.lifetimeSeconds = lifetimeSeconds;
            projectile.knockbackForce = knockbackForce;
            projectile.energyDrain = energyDrain;
            projectile.launchLockSeconds = launchLockSeconds;
            projectile.body = body;

            // Cosmetics last, and shared: a failed shader lookup can no longer produce a
            // broken projectile, and shots stop leaking one material each.
            if (sharedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    sharedMaterial = new Material(shader);
                    sharedMaterial.color = color;
                }
            }
            if (sharedMaterial != null) go.GetComponent<Renderer>().sharedMaterial = sharedMaterial;
            return projectile;
        }

        // Shared iterative ballistic intercept, used by every shooter (flyer, turret):
        // where will the player be when a shot travelling at projectileSpeed arrives?
        // Gravity applies while the player is airborne; the timescale division accounts
        // for the projectile living on WorldMotionTime while the player lives on scaled
        // time (during the launch speed-up the shot is effectively slower in game-time).
        public static Vector3 PredictIntercept(Vector3 shooterPosition, KineticCubeController player,
            Rigidbody playerBody, float projectileSpeed)
        {
            Vector3 basePosition = player.transform.position;
            Vector3 velocity = playerBody != null ? playerBody.linearVelocity : Vector3.zero;
            bool airborne = !player.IsGrounded && velocity.sqrMagnitude > 0.01f;

            float effectiveSpeed = Mathf.Max(projectileSpeed / Mathf.Max(Time.timeScale, 1f), 0.1f);
            float time = 0f;
            Vector3 predicted = basePosition;
            for (int i = 0; i < 8; i++)
            {
                predicted = basePosition + velocity * time;
                if (airborne) predicted += 0.5f * time * time * Physics.gravity;
                time = Vector3.Distance(shooterPosition, predicted) / effectiveSpeed;
            }
            return predicted;
        }

        void FixedUpdate()
        {
            float dt = WorldMotionTime.FixedDeltaTime;
            lived += dt;
            if (lived >= lifetimeSeconds)
            {
                Destroy(gameObject);
                return;
            }
            body.MovePosition(body.position + direction * (speed * dt));
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.isTrigger) return; // finish pads, clamp zones, other projectiles...

            KineticCubeController player = other.GetComponentInParent<KineticCubeController>();
            if (player != null)
            {
                // A hit is a hit, launched or not - the dodge is SPATIAL (relaunch to be
                // somewhere else), unlike the ground enemy's body-check clash rule.
                //
                // GROUNDED hits knock back in 2D: a shot arriving from above would
                // otherwise shove the player straight into the floor (direct report). The
                // horizontal component of the flight carries the push; a near-vertical
                // shot falls back to its horizontal approach line from the shooter.
                Vector3 pushDirection = direction;
                if (player.IsGrounded)
                {
                    Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
                    if (flat.sqrMagnitude < 0.01f)
                    {
                        flat = Vector3.ProjectOnPlane(player.transform.position - spawnOrigin, Vector3.up);
                    }
                    if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward; // dead-vertical corner case
                    pushDirection = flat.normalized;
                }
                Vector3 shove = (pushDirection + Vector3.up * 0.5f).normalized;
                player.ApplyEnemyHit(shove * knockbackForce, energyDrain, launchLockSeconds);
                Destroy(gameObject);
                return;
            }

            // Passing through its own shooter (or any other enemy) must not pop the shot.
            if (other.GetComponentInParent<FlyingEnemy>() != null) return;
            if (other.GetComponentInParent<TurretEnemy>() != null) return;
            if (other.GetComponentInParent<Enemy>() != null) return;

            Destroy(gameObject); // level geometry
        }
    }
}
