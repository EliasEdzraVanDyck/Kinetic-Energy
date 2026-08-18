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
        [Tooltip("Seconds of warning flash before the burst starts.")]
        public float windUpSeconds = 0.7f;
        [Tooltip("Shots fired per burst.")]
        public int shotsPerBurst = 3;
        [Tooltip("Seconds from the FIRST shot of a burst to the last. The shots are spread evenly across it, so raising the shot count packs them tighter rather than lengthening the burst.")]
        public float burstSeconds = 0.6f;
        [Tooltip("Cooldown after a whole burst, in world-motion seconds.")]
        public float attackCooldown = 2.2f;
        [Tooltip("Colour flashed during the windup - the ground hunter's warning yellow, so an incoming attack reads the same whatever is firing it.")]
        public Color windUpColor = new Color(1f, 0.93f, 0.32f);
        [Tooltip("Pulses per second of the windup flash. Matches the ground hunter's rate: a turret has no vulnerable state to signal, so this flash means one thing only - an attack is coming.")]
        public float pulseSpeed = 6f;

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
        bool bursting;
        int shotsLeftInBurst;
        float nextShotTimer;
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

            // Mid-burst: the shots walk out on their own spacing. The colour is held SOLID
            // rather than flashing - exactly as the hunter goes solid once it commits.
            // Blinking is the warning; once the shots are leaving, the warning is over.
            if (bursting)
            {
                nextShotTimer -= dt;
                if (nextShotTimer <= 0f) FireOneShot();
                return;
            }

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
            FlashWarning();
            if (stateTimer <= 0f) BeginBurst();
        }

        void FlashWarning()
        {
            if (bodyRenderer == null) return;
            float blink = Mathf.PingPong(Time.unscaledTime * pulseSpeed, 1f);
            bodyRenderer.material.color = Color.Lerp(restColor, windUpColor, blink);
        }

        void BeginBurst()
        {
            windingUp = false;
            bursting = true;
            if (bodyRenderer != null) bodyRenderer.material.color = windUpColor; // committed - held solid
            shotsLeftInBurst = Mathf.Max(shotsPerBurst, 1);
            nextShotTimer = 0f; // the first shot leaves the instant the windup ends
        }

        // Spacing is derived so the whole burst spans burstSeconds however many shots it
        // holds - a single-shot burst has nothing to space and simply fires.
        float BurstShotInterval => shotsPerBurst > 1
            ? Mathf.Max(burstSeconds, 0f) / (shotsPerBurst - 1)
            : 0f;

        void FireOneShot()
        {
            Fire();
            shotsLeftInBurst--;
            if (shotsLeftInBurst > 0)
            {
                nextShotTimer = BurstShotInterval;
                return;
            }

            // Burst spent - back to rest, and the cooldown covers the whole volley.
            bursting = false;
            cooldownRemaining = attackCooldown;
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
        }

        bool PlayerInRange()
        {
            if (player == null) return false;
            return (player.transform.position - transform.position).sqrMagnitude <= detectionRadius * detectionRadius;
        }

        // Launches ONE projectile and nothing else - ending the attack and starting the
        // cooldown belong to the burst, which only finishes on its last shot.
        void Fire()
        {
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
            bursting = false;
            shotsLeftInBurst = 0;
            nextShotTimer = 0f;
            stateTimer = 0f;
            cooldownRemaining = 0f;
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
            gameObject.SetActive(true);
        }
    }
}
