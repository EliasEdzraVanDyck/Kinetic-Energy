using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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

        static Material sharedMaterial;

        public static EnemyProjectile Spawn(Vector3 origin, Vector3 flightDirection, Vector3 bodyScale, Color color,
            float speed, float lifetimeSeconds, float knockbackForce, float energyDrain, float launchLockSeconds)
        {
            Vector3 dir = flightDirection.sqrMagnitude > 0.0001f ? flightDirection.normalized : Vector3.forward;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "EnemyProjectile";

            int noOutline = LayerMask.NameToLayer("NoOutline");
            if (noOutline >= 0) go.layer = noOutline;
            go.transform.localScale = bodyScale;
            go.transform.position = origin;

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
            if (other.isTrigger) return;

            KineticCubeController player = other.GetComponentInParent<KineticCubeController>();
            if (player != null)
            {

                Vector3 pushDirection = direction;
                if (player.IsGrounded)
                {
                    Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
                    if (flat.sqrMagnitude < 0.01f)
                    {
                        flat = Vector3.ProjectOnPlane(player.transform.position - spawnOrigin, Vector3.up);
                    }
                    if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
                    pushDirection = flat.normalized;
                }
                Vector3 shove = (pushDirection + Vector3.up * 0.5f).normalized;
                player.ApplyEnemyHit(shove * knockbackForce, energyDrain, launchLockSeconds);
                Destroy(gameObject);
                return;
            }

            if (other.GetComponentInParent<FlyingEnemy>() != null) return;
            if (other.GetComponentInParent<TurretEnemy>() != null) return;
            if (other.GetComponentInParent<Enemy>() != null) return;

            Destroy(gameObject);
        }
    }
}

