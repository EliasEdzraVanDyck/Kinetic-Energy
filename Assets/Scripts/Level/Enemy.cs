using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum EnemyWanderMode
    {
        // Random points within wanderRadius of the spawn position.
        WithinRadius,
        // Random points across the full top surface of the platform underneath, kept
        // edgeMargin inside its edges. Falls back to WithinRadius if no platform is found.
        PlatformSurface,
    }

    // A ground enemy that wanders randomly, and ATTACKS the way the player moves: when the
    // player stands within detection range, it stops, winds up (flashing its warning
    // colour), then LAUNCHES itself in a ballistic arc - same gravity as everything else -
    // toward the position the player held when the windup began. No homing: the telegraph
    // plus the committed arc is the counterplay (sidestep, launch over it, or pound it
    // mid-flight). Landing a hit shoves the player and drains some energy.
    //
    // Movement and flight follow the project-wide WorldMotionTime rule and run on a
    // kinematic interpolated rigidbody. Launching INTO the enemy still kills it (the
    // player's crash pipeline wins any clash).
    public class Enemy : MonoBehaviour
    {
        [Header("Wandering")]
        [Tooltip("How wander targets are picked - within a radius of the spawn, or across the platform underneath.")]
        public EnemyWanderMode wanderMode = EnemyWanderMode.WithinRadius;
        [Tooltip("WithinRadius mode: how far from the spawn point the enemy may roam.")]
        public float wanderRadius = 8f;
        [Tooltip("How far inside the platform's edges the enemy stays - applies to BOTH wander modes.")]
        public float edgeMargin = 1.5f;
        [Tooltip("Walking speed in units per (world-motion) second.")]
        public float moveSpeed = 4.5f;
        [Tooltip("Random pause between wander hops, min..max seconds.")]
        public Vector2 pauseRange = new Vector2(0.4f, 1.5f);

        [Header("Attack (player-style launch)")]
        [Tooltip("The player must be grounded within this range (and near this height) to trigger an attack.")]
        public float detectionRadius = 12f;
        [Tooltip("Seconds of stationary windup (warning flash) before the launch.")]
        public float windUpSeconds = 0.5f;
        [Tooltip("Seconds of recovery after landing before wandering resumes.")]
        public float recoverSeconds = 1f;
        [Tooltip("Cooldown between attacks, in world-motion seconds.")]
        public float attackCooldown = 3f;
        [Tooltip("Horizontal speed of the attack launch - the arc is solved to still land exactly on the target.")]
        public float attackLaunchSpeed = 48f;
        [Tooltip("Shortest and longest the attack flight may take, in seconds - keeps close-range attacks dodgeable and long-range ones snappy.")]
        public Vector2 attackFlightTimeRange = new Vector2(0.15f, 0.5f);
        [Tooltip("Minimum apex height of the attack arc - keeps the flat, fast arc from grazing the ground and landing short of the player.")]
        public float attackArcHeight = 3f;
        [Tooltip("Impulse applied to the player on a successful hit.")]
        public float knockbackForce = 24f;
        [Tooltip("Energy fraction the player loses on a successful hit.")]
        [Range(0f, 1f)] public float attackEnergyDrain = 0.15f;
        [Tooltip("Colour flashed during the windup.")]
        public Color windUpColor = new Color(1f, 0.35f, 0.1f);

        enum EnemyState { Wandering, WindingUp, Launching, Recovering }

        Rigidbody body;
        KineticCubeController player;
        Renderer bodyRenderer;
        Color restColor;

        Vector3 spawnPoint;
        Collider platformBelow;
        Vector3 currentTarget;
        float pauseRemaining;

        EnemyState state = EnemyState.Wandering;
        float stateTimer;
        float cooldownRemaining;
        Vector3 attackTarget;    // the player's position when the windup began
        Vector3 flightVelocity;  // the ballistic arc, integrated manually
        float fallVelocity;      // vertical speed while unsupported outside a launch

        void Start()
        {
            spawnPoint = transform.position;
            player = FindAnyObjectByType<KineticCubeController>();
            bodyRenderer = GetComponentInChildren<Renderer>();
            if (bodyRenderer != null) restColor = bodyRenderer.material.color;

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 5f))
            {
                platformBelow = hit.collider;
            }

            PickNewTarget();
        }

        void FixedUpdate()
        {
            float dt = WorldMotionTime.FixedDeltaTime;
            if (cooldownRemaining > 0f) cooldownRemaining -= dt;

            switch (state)
            {
                case EnemyState.Wandering:
                    body.MovePosition(WithGroundedY(WanderStep(dt), dt));
                    if (cooldownRemaining <= 0f && PlayerIsAttackable()) BeginWindUp();
                    break;

                case EnemyState.WindingUp:
                    stateTimer -= dt;
                    FlashWarning();
                    body.MovePosition(WithGroundedY(body.position, dt));
                    if (stateTimer <= 0f) BeginLaunch();
                    break;

                case EnemyState.Launching:
                    UpdateFlight(dt);
                    break;

                case EnemyState.Recovering:
                    stateTimer -= dt;
                    body.MovePosition(WithGroundedY(body.position, dt));
                    if (stateTimer <= 0f) BeginWander();
                    break;
            }
        }

        // Kinematic bodies ignore gravity, so ground contact is enforced by hand: the enemy
        // rests exactly on the surface below and falls under real gravity when unsupported.
        Vector3 WithGroundedY(Vector3 next, float dt)
        {
            float bodyRadius = transform.localScale.x * 0.5f;
            if (Physics.Raycast(next + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, bodyRadius + 8f)
                && hit.collider.GetComponent<KineticCubeController>() == null)
            {
                float restY = hit.point.y + bodyRadius;
                if (next.y > restY + 0.02f)
                {
                    fallVelocity += Physics.gravity.y * dt;
                    next.y = Mathf.Max(restY, next.y + fallVelocity * dt);
                }
                else
                {
                    next.y = restY;
                }
                if (next.y <= restY + 0.001f) fallVelocity = 0f;
            }
            else
            {
                fallVelocity += Physics.gravity.y * dt;
                next.y += fallVelocity * dt;
            }
            return next;
        }

        // ---------- Wandering ----------

        // Horizontal wander step only - the caller resolves the height via WithGroundedY.
        Vector3 WanderStep(float dt)
        {
            Vector3 position = body.position;

            if (pauseRemaining > 0f)
            {
                pauseRemaining -= dt;
                if (pauseRemaining <= 0f) PickNewTarget();
                return position;
            }

            Vector3 toTarget = currentTarget - position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            float step = moveSpeed * dt;

            if (distance <= step)
            {
                pauseRemaining = Random.Range(pauseRange.x, pauseRange.y);
                return new Vector3(currentTarget.x, position.y, currentTarget.z);
            }
            return position + toTarget / distance * step;
        }

        void PickNewTarget()
        {
            bool haveWalkableArea = TryGetWalkableArea(out float minX, out float maxX, out float minZ, out float maxZ);

            if (wanderMode == EnemyWanderMode.PlatformSurface && haveWalkableArea)
            {
                currentTarget = new Vector3(Random.Range(minX, maxX), body != null ? body.position.y : transform.position.y, Random.Range(minZ, maxZ));
                return;
            }

            // WithinRadius - but the edge rule applies here too: the same margin keeps
            // radius-wanderers from ever straying near the platform's edge.
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            float y = body != null ? body.position.y : transform.position.y;
            Vector3 target = new Vector3(spawnPoint.x + offset.x, y, spawnPoint.z + offset.y);
            if (haveWalkableArea)
            {
                target.x = Mathf.Clamp(target.x, minX, maxX);
                target.z = Mathf.Clamp(target.z, minZ, maxZ);
            }
            currentTarget = target;
        }

        // The platform's top surface shrunk by edgeMargin (plus the enemy's own radius) -
        // the walkable rectangle both wander modes are confined to.
        bool TryGetWalkableArea(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            if (platformBelow == null) return false;

            Bounds bounds = platformBelow.bounds;
            float inset = edgeMargin + transform.localScale.x * 0.5f;
            minX = bounds.min.x + inset;
            maxX = bounds.max.x - inset;
            minZ = bounds.min.z + inset;
            maxZ = bounds.max.z - inset;
            return minX < maxX && minZ < maxZ;
        }

        // ---------- Attacking ----------

        // Grounded prey within range, at roughly this height - airborne players are safe,
        // which is the whole rhythm of the game.
        bool PlayerIsAttackable()
        {
            if (player == null || !player.IsGrounded) return false;
            Vector3 toPlayer = player.transform.position - body.position;
            if (Mathf.Abs(toPlayer.y) > 4f) return false;
            toPlayer.y = 0f;
            return toPlayer.sqrMagnitude <= detectionRadius * detectionRadius;
        }

        void BeginWindUp()
        {
            state = EnemyState.WindingUp;
            stateTimer = windUpSeconds;
            attackTarget = player.transform.position; // the OLD position - no homing
        }

        void FlashWarning()
        {
            if (bodyRenderer == null) return;
            float blink = Mathf.PingPong(Time.unscaledTime * 6f, 1f);
            bodyRenderer.material.color = Color.Lerp(restColor, windUpColor, blink);
        }

        void BeginLaunch()
        {
            state = EnemyState.Launching;
            if (bodyRenderer != null) bodyRenderer.material.color = windUpColor;

            // A player-style ballistic launch, solved for TIME instead of angle: the flight
            // takes range/attackLaunchSpeed seconds (clamped), and the exit velocity is
            // whatever ballistic arc lands exactly on the target in that time - height
            // difference included. Raising attackLaunchSpeed makes the whole attack faster
            // and flatter without ever overshooting.
            Vector3 toTarget = attackTarget - body.position;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float range = Mathf.Max(flat.magnitude, 0.5f);
            float flightTime = Mathf.Clamp(range / Mathf.Max(attackLaunchSpeed, 0.1f), attackFlightTimeRange.x, attackFlightTimeRange.y);

            // The time-solved vertical speed lands exactly on the target, but at high
            // horizontal speeds the arc is so flat it grazes the ground and stops short.
            // Enforce a minimum apex height instead - landing slightly PAST the old
            // position beats stopping in front of the player.
            float verticalSpeed = toTarget.y / flightTime - 0.5f * Physics.gravity.y * flightTime;
            float apexSpeed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * Mathf.Max(attackArcHeight, 0f));
            flightVelocity = flat / flightTime + Vector3.up * Mathf.Max(verticalSpeed, apexSpeed);
        }

        void UpdateFlight(float dt)
        {
            flightVelocity += Physics.gravity * dt;
            Vector3 next = body.position + flightVelocity * dt;

            // Land on whatever is under the descending arc.
            float bodyRadius = transform.localScale.x * 0.5f;
            if (flightVelocity.y < 0f
                && Physics.Raycast(body.position, Vector3.down, out RaycastHit hit, bodyRadius + Mathf.Abs(flightVelocity.y * dt) + 0.1f)
                && hit.collider.GetComponent<KineticCubeController>() == null)
            {
                body.MovePosition(hit.point + Vector3.up * bodyRadius);
                Land(hit.collider);
                return;
            }

            body.MovePosition(next);

            // Overshot into the void (the player baited it off the edge) - self-reset.
            if (next.y < spawnPoint.y - 40f) ResetToSpawn();
        }

        void Land(Collider landedOn)
        {
            state = EnemyState.Recovering;
            stateTimer = recoverSeconds;
            cooldownRemaining = attackCooldown;
            fallVelocity = 0f;
            platformBelow = landedOn; // the walkable area follows the enemy to its new platform
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
        }

        void BeginWander()
        {
            state = EnemyState.Wandering;
            spawnPoint = body.position; // radius wandering re-centres on wherever it landed
            pauseRemaining = 0f;
            PickNewTarget();
        }

        // A mid-flight body-check: hitting the player during the attack launch shoves them
        // and drains energy. The player's own launch always wins the clash (his crash
        // pipeline kills this enemy before this can fire).
        void OnCollisionEnter(Collision collision)
        {
            if (state != EnemyState.Launching || player == null) return;
            if (collision.collider.GetComponent<KineticCubeController>() == null) return;
            if (player.HasLaunched) return; // the clash goes to the player

            // Shove the player horizontally AWAY from the enemy plus an upward pop - never
            // along the flight velocity, which points downward on descent and would just
            // pin the player into the platform.
            Vector3 away = player.transform.position - body.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = new Vector3(flightVelocity.x, 0f, flightVelocity.z);
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
            }
            Vector3 shoveDirection = (away.normalized + Vector3.up * 0.6f).normalized;
            player.ApplyEnemyHit(shoveDirection * knockbackForce, attackEnergyDrain);

            // Land on the GROUND below, never on the player - treating the player's
            // collider as the home platform gave the enemy a moving, bogus walkable area
            // and sent it wandering off the edge.
            Collider ground = platformBelow;
            if (Physics.Raycast(body.position + Vector3.up * 0.05f, Vector3.down, out RaycastHit groundHit, 12f)
                && groundHit.collider.GetComponent<KineticCubeController>() == null)
            {
                ground = groundHit.collider;
                body.MovePosition(new Vector3(body.position.x, groundHit.point.y + transform.localScale.x * 0.5f, body.position.z));
            }
            Land(ground);
        }

        // ---------- Kill / respawn ----------

        // Called by KineticCubeController when a launch hits this enemy. Deactivated, not
        // destroyed - a player respawn brings every enemy back (see ResetToSpawn).
        public void OnHitByLaunch()
        {
            gameObject.SetActive(false);
        }

        // Called on player respawn (DamageWalls): back to the original spot, alive again,
        // wander state reset - as if the level had just started for this enemy.
        public void ResetToSpawn()
        {
            transform.position = spawnPoint;
            if (body != null) body.position = spawnPoint;
            state = EnemyState.Wandering;
            pauseRemaining = 0f;
            cooldownRemaining = 0f;
            fallVelocity = 0f;
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
            if (Physics.Raycast(spawnPoint, Vector3.down, out RaycastHit hit, 5f)) platformBelow = hit.collider;
            PickNewTarget();
            gameObject.SetActive(true);
        }
    }
}
