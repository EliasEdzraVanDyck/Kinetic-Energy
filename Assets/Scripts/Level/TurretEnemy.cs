using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A stationary TURRET: a cylinder body mounted on a wall or platform (its placement/
    // rotation is set in the editor - it never moves). Player inside detection range ->
    // warning flash -> fires the same red-capsule EnemyProjectile as the flying enemy,
    // with the same intercept lead. Launching into it kills it; player respawn revives it.
    // Windup/cooldown run on WorldMotionTime, same rule as every non-player actor.
    public class TurretEnemy : MonoBehaviour
    {
        [Header("Attack")]
        [Tooltip("The player inside this range (any direction) triggers a shot.")]
        public float detectionRadius = 26f;
        [Tooltip("Seconds of warning flash before the shot fires.")]
        public float windUpSeconds = 0.7f;
        [Tooltip("Cooldown between shots, in world-motion seconds.")]
        public float attackCooldown = 2.2f;
        public Color windUpColor = new Color(1f, 0.35f, 0.1f); // same flash as the ground enemy

        [Header("Projectile")]
        public float projectileSpeed = 26f;
        public float projectileLifetimeSeconds = 6f;
        public float projectileKnockback = 22f;
        [Range(0f, 1f)] public float projectileEnergyDrain = 0.1f;
        public float projectileLaunchLock = 0.5f;
        [Tooltip("How far in front of the turret's centre the shot spawns - keeps it clear of the mounting wall.")]
        public float muzzleClearance = 1.4f;
        public Vector3 projectileScale = new Vector3(0.35f, 0.6f, 0.35f);
        public Color projectileColor = new Color(0.9f, 0.1f, 0.08f);

        KineticCubeController player;
        Rigidbody playerBody;
        Renderer bodyRenderer;
        Color restColor;

        bool windingUp;
        float stateTimer;
        float cooldownRemaining;

        void Start()
        {
            player = FindAnyObjectByType<KineticCubeController>();
            if (player != null) playerBody = player.GetComponent<Rigidbody>();
            bodyRenderer = GetComponentInChildren<Renderer>();
            if (bodyRenderer != null) restColor = bodyRenderer.material.color;
        }

        void FixedUpdate()
        {
            float dt = WorldMotionTime.FixedDeltaTime;
            if (cooldownRemaining > 0f) cooldownRemaining -= dt;

            if (!windingUp)
            {
                if (cooldownRemaining <= 0f && PlayerInRange())
                {
                    windingUp = true;
                    stateTimer = windUpSeconds;
                }
                return;
            }

            stateTimer -= dt;
            if (bodyRenderer != null)
            {
                float blink = Mathf.PingPong(Time.unscaledTime * 7f, 1f);
                bodyRenderer.material.color = Color.Lerp(restColor, windUpColor, blink);
            }
            if (stateTimer <= 0f) Fire();
        }

        bool PlayerInRange()
        {
            if (player == null) return false;
            return (player.transform.position - transform.position).sqrMagnitude <= detectionRadius * detectionRadius;
        }

        void Fire()
        {
            windingUp = false;
            cooldownRemaining = attackCooldown;
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
            if (player == null) return;

            Vector3 intercept = EnemyProjectile.PredictIntercept(transform.position, player, playerBody, projectileSpeed);
            Vector3 direction = intercept - transform.position;
            if (direction.sqrMagnitude < 0.01f) direction = player.transform.position - transform.position;
            direction.Normalize();

            EnemyProjectile.Spawn(transform.position + direction * muzzleClearance, direction,
                projectileScale, projectileColor, projectileSpeed, projectileLifetimeSeconds,
                projectileKnockback, projectileEnergyDrain, projectileLaunchLock);
        }

        // Same kill/respawn contract as the other enemies.
        public void OnHitByLaunch()
        {
            gameObject.SetActive(false);
        }

        public void ResetToSpawn()
        {
            windingUp = false;
            stateTimer = 0f;
            cooldownRemaining = 0f;
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
            gameObject.SetActive(true);
        }
    }
}
