using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

    public enum EnemyKillWindow
    {
        Always,
        WhileCoolingDown,
        WhileWindingUp,
    }

    public enum EnemyWanderMode
    {

        WithinRadius,

        PlatformSurface,
    }

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
        [Tooltip("Apex height of the attack arc above the launch point - the flight time stretches as needed so the arc genuinely rises and then descends onto the target.")]
        public float attackArcHeight = 1.5f;
        [Tooltip("The arc lands this far BEHIND the player's old position (along the attack direction), so the descending tail sweeps through the player instead of stopping at their feet.")]
        public float attackOvershoot = 1.5f;
        [Tooltip("Impulse applied to the player on a successful hit.")]
        public float knockbackForce = 24f;
        [Tooltip("Energy fraction the player loses on a successful hit.")]
        [Range(0f, 1f)] public float attackEnergyDrain = 0.15f;
        [Tooltip("Seconds the player is blocked from launching after being hit.")]
        public float postHitLaunchLockSeconds = 0.5f;
        [Header("Telegraphing")]

        [Tooltip("Body colour while the enemy CANNOT be killed - red reads as 'not now'.")]
        public Color baseColor = new Color(0.78f, 0.11f, 0.09f);
        [Tooltip("Body colour while a launch WOULD kill it - the original purple.")]
        public Color vulnerableColor = new Color(0.72f, 0.15f, 0.6f);
        [Tooltip("Colour flashed during the windup. Bright and warm so it separates clearly from BOTH the red and the purple.")]
        public Color windUpColor = new Color(1f, 0.93f, 0.32f);

        [Header("Hunter variant")]
        [Tooltip("ON: the attack also triggers on AIRBORNE players (no grounded/height requirement) - the ballistic solve leads to wherever they were at windup.")]
        public bool attackAirbornePlayers = false;
        [Tooltip("ON: walking or overshooting off a platform LAUNCHES the enemy back to the nearest platform instead of falling/resetting.")]
        public bool returnLaunchToPlatform = false;
        [Tooltip("How far around itself the enemy searches for a platform to return to.")]
        public float platformSearchRadius = 60f;
        [Tooltip("Hop back to the platform this enemy booted on whenever it ends up standing on a different one. Its patrol is anchored to that platform, so a stray landing elsewhere would otherwise strand it there for the rest of the run.")]
        public bool returnToHomePlatform = true;
        [Tooltip("Seconds before another walk-home hop may be attempted, so a launch that falls short cannot spam.")]
        public float homeLaunchRetrySeconds = 1.5f;
        [Tooltip("HUNTER: hop-dodge sideways when a launching player bears down on it.")]
        public bool dodgePlayerLaunches = false;
        [Tooltip("Outer awareness range - beyond this an incoming player is not considered at all.")]
        public float dodgeTriggerRadius = 18f;
        [Tooltip("The dodge fires when the player's estimated ARRIVAL is this many real seconds away - the just-in-time hop. Bigger = earlier, safer dodges.")]
        public float dodgeLeadSeconds = 0.35f;
        [Tooltip("Length of the sideways dodge hop.")]
        public float dodgeDistance = 6f;
        [Tooltip("Minimum seconds between dodges - it cannot evade forever.")]
        public float dodgeCooldownSeconds = 1.2f;
        [Tooltip("After its OWN attack lands, dodging stays OFF this long - the player's guaranteed punish window.")]
        public float vulnerableAfterAttackSeconds = 2f;
        [Tooltip("When a launch can KILL this enemy - outside the window the crash registers but the enemy survives.")]
        public EnemyKillWindow killWindow = EnemyKillWindow.Always;
        [Tooltip("A fired launch whose predicted landing is within this distance of the enemy books the just-in-time dodge - NO range limit on where the player fires from.")]
        public float dodgePredictedHitRadius = 2.5f;

        enum EnemyState { Wandering, WindingUp, Launching, Recovering }

        Rigidbody body;
        protected KineticCubeController player;
        Collider bodyCollider;
        Collider playerCollider;
        Renderer bodyRenderer;

        bool KillableNow => killWindow switch
        {
            EnemyKillWindow.WhileCoolingDown => vulnerableTimer > 0f,
            EnemyKillWindow.WhileWindingUp => state == EnemyState.WindingUp,
            _ => true,
        };

        Color IdleColor => KillableNow ? vulnerableColor : baseColor;

        Vector3 spawnPoint;
        Vector3 originalSpawn;

        Collider homePlatform;
        float homeLaunchCooldown;
        Collider platformBelow;
        Vector3 currentTarget;
        float pauseRemaining;

        EnemyState state = EnemyState.Wandering;
        float stateTimer;
        float cooldownRemaining;
        Vector3 attackTarget;
        Vector3 flightVelocity;
        float flightDuration;
        float flightElapsed;
        float fallVelocity;

        protected virtual void Start()
        {
            spawnPoint = transform.position;
            originalSpawn = spawnPoint;
            player = FindAnyObjectByType<KineticCubeController>();
            if (player != null)
            {
                playerBody = player.GetComponent<Rigidbody>();
                if (dodgePlayerLaunches)
                {

                    player.LaunchFired += OnPlayerLaunchFired;
                    player.CrashRegistered += OnPlayerCrashedSomewhere;
                }
            }
            bodyCollider = GetComponent<Collider>();
            if (player != null) playerCollider = player.GetComponent<Collider>();
            bodyRenderer = GetComponentInChildren<Renderer>();

            if (bodyRenderer != null) bodyRenderer.material.color = IdleColor;

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 5f))
            {
                platformBelow = hit.collider;
                homePlatform = hit.collider;
            }

            PickNewTarget();
        }

        void OnDestroy()
        {
            if (player == null) return;
            player.LaunchFired -= OnPlayerLaunchFired;
            player.CrashRegistered -= OnPlayerCrashedSomewhere;
        }

        void OnPlayerLaunchFired()
        {
            if (!dodgePlayerLaunches || vulnerableTimer > 0f || dodgeCooldownRemaining > 0f) return;
            if (player == null || !player.HasValidPredictedLanding) return;

            float hitRadius = transform.localScale.x * 0.5f + dodgePredictedHitRadius;
            if ((player.LastPredictedLanding - body.position).sqrMagnitude > hitRadius * hitRadius) return;

            scheduledDodgeTimer = Mathf.Max(player.PredictedFlightRealSecondsLive - dodgeLeadSeconds, 0.02f);
            dodgeScheduled = true;
        }

        void OnPlayerCrashedSomewhere(Vector3 position)
        {
            dodgeScheduled = false;
        }

        void FixedUpdate()
        {
            float dt = WorldMotionTime.FixedDeltaTime;
            if (cooldownRemaining > 0f) cooldownRemaining -= dt;
            if (dodgeCooldownRemaining > 0f) dodgeCooldownRemaining -= dt;
            if (vulnerableTimer > 0f) vulnerableTimer -= dt;
            if (homeLaunchCooldown > 0f) homeLaunchCooldown -= dt;

            if (bodyRenderer != null && state != EnemyState.WindingUp && state != EnemyState.Launching)
            {
                if (KillableNow && killWindow != EnemyKillWindow.Always)
                {

                    float pulse = Mathf.PingPong(Time.unscaledTime * 2.5f, 0.15f);
                    bodyRenderer.material.color = Color.Lerp(vulnerableColor, Color.white, pulse);
                }
                else
                {
                    bodyRenderer.material.color = IdleColor;
                }
            }

            if (dodgeScheduled)
            {
                if (player == null || !player.HasLaunched) dodgeScheduled = false;
                else
                {
                    scheduledDodgeTimer -= dt;

                    bool committedToWindup = killWindow == EnemyKillWindow.WhileWindingUp && state == EnemyState.WindingUp;
                    if (scheduledDodgeTimer <= 0f && vulnerableTimer <= 0f && !committedToWindup
                        && (state == EnemyState.Wandering || state == EnemyState.WindingUp || state == EnemyState.Recovering))
                    {
                        dodgeScheduled = false;
                        BeginDodge();
                        return;
                    }
                }
            }

            switch (state)
            {
                case EnemyState.Wandering:
                    if (dodgePlayerLaunches && ShouldDodge()) { BeginDodge(); break; }

                    if (vulnerableTimer > 0f)
                    {
                        MoveGrounded(body.position, dt);
                        break;
                    }

                    if (TryHomePlatformLaunch()) break;

                    if (PlayerDetected())
                    {
                        if (MoveGrounded(body.position, dt)) break;
                    }
                    else if (MoveGrounded(WanderStep(dt), dt)) break;
                    if (cooldownRemaining <= 0f && PlayerIsAttackable()) BeginWindUp();
                    break;

                case EnemyState.WindingUp:

                    if (dodgePlayerLaunches && ShouldDodge()) { BeginDodge(); break; }
                    stateTimer -= dt;
                    FlashWarning();
                    if (MoveGrounded(body.position, dt)) break;
                    if (stateTimer <= 0f) BeginLaunch();
                    break;

                case EnemyState.Launching:
                    UpdateFlight(dt);
                    break;

                case EnemyState.Recovering:
                    stateTimer -= dt;
                    if (MoveGrounded(body.position, dt)) break;
                    if (stateTimer <= 0f) BeginWander();
                    break;
            }
        }

        bool MoveGrounded(Vector3 horizontalTarget, float dt)
        {
            Vector3 next = WithGroundedY(horizontalTarget, dt);

            if (returnLaunchToPlatform && groundedSinceSpawn && lastMoveUnsupported
                && fallVelocity < -4f && OverGenuineVoid())
            {
                return TryReturnLaunch();
            }
            if (next.y < originalSpawn.y - 40f)
            {

                if (returnLaunchToPlatform && !returnLaunching) return TryReturnLaunch();
                ResetToSpawn();
                return true;
            }
            body.MovePosition(next);
            return false;
        }

        bool lastMoveUnsupported;
        bool groundedSinceSpawn;
        bool attackConnected;
        bool returnLaunching;
        bool lastFlightWasAttack;
        float dodgeCooldownRemaining;
        float vulnerableTimer;
        Rigidbody playerBody;
        bool dodgeScheduled;
        float scheduledDodgeTimer;

        [Tooltip("How far below itself the enemy looks for ground. Must comfortably exceed the biggest drop in the level: anything further down is invisible to it, and a drop it cannot see reads as empty void.")]
        public float groundProbeDistance = 60f;

        bool OverGenuineVoid()
        {
            foreach (RaycastHit hit in Physics.RaycastAll(body.position + Vector3.up * 0.05f, Vector3.down,
                         groundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<KineticCubeController>() != null) continue;
                if (hit.collider.GetComponentInParent<Enemy>() != null) continue;
                if (hit.collider.GetComponentInParent<FlyingEnemy>() != null) continue;
                if (hit.collider.GetComponentInParent<DamageWalls>() != null) continue;
                return false;
            }
            return true;
        }

        Vector3 WithGroundedY(Vector3 next, float dt)
        {
            lastMoveUnsupported = false;
            float bodyRadius = transform.localScale.x * 0.5f;

            float probeLift = bodyRadius + 0.5f;
            RaycastHit hit = default;
            bool foundGround = false;
            foreach (RaycastHit candidate in Physics.RaycastAll(next + Vector3.up * probeLift, Vector3.down,
                         probeLift + groundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (candidate.collider == null) continue;
                if (candidate.collider.transform == transform || candidate.collider.transform.IsChildOf(transform)) continue;
                if (candidate.collider.GetComponent<KineticCubeController>() != null) continue;

                if (candidate.collider.GetComponentInParent<DamageWalls>() != null) continue;
                if (!foundGround || candidate.distance < hit.distance)
                {
                    hit = candidate;
                    foundGround = true;
                }
            }
            if (foundGround)
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
                if (next.y <= restY + 0.001f)
                {
                    fallVelocity = 0f;
                    groundedSinceSpawn = true;
                }
            }
            else
            {
                lastMoveUnsupported = true;
                fallVelocity += Physics.gravity.y * dt;
                next.y += fallVelocity * dt;
            }
            return next;
        }

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

        bool PlayerOnMyPlatform()
        {
            if (player == null || platformBelow == null || !player.IsGrounded) return false;
            Bounds bounds = platformBelow.bounds;
            Vector3 p = player.transform.position;
            if (p.x < bounds.min.x || p.x > bounds.max.x) return false;
            if (p.z < bounds.min.z || p.z > bounds.max.z) return false;

            return p.y >= bounds.max.y - 1f && p.y <= bounds.max.y + 4f;
        }

        bool PlayerDetected()
        {
            if (player == null) return false;
            if (PlayerOnMyPlatform()) return true;
            Vector3 toPlayer = player.transform.position - body.position;
            return toPlayer.sqrMagnitude <= detectionRadius * detectionRadius;
        }

        bool PlayerIsAttackable()
        {
            if (player == null) return false;
            Vector3 toPlayer = player.transform.position - body.position;
            if (attackAirbornePlayers)
            {
                return PlayerOnMyPlatform() || toPlayer.sqrMagnitude <= detectionRadius * detectionRadius;
            }
            if (!player.IsGrounded) return false;
            if (Mathf.Abs(toPlayer.y) > 4f) return false;
            if (PlayerOnMyPlatform()) return true;
            toPlayer.y = 0f;
            return toPlayer.sqrMagnitude <= detectionRadius * detectionRadius;
        }

        void BeginWindUp()
        {
            state = EnemyState.WindingUp;
            stateTimer = windUpSeconds;
            attackTarget = player.transform.position;
        }

        bool ShouldDodge()
        {
            if (player == null || playerBody == null) return false;
            if (!player.HasLaunched) return false;
            if (dodgeCooldownRemaining > 0f || vulnerableTimer > 0f) return false;

            if (killWindow == EnemyKillWindow.WhileWindingUp && state == EnemyState.WindingUp) return false;

            Vector3 toEnemy = body.position - player.transform.position;
            float distance = toEnemy.magnitude;
            if (distance > dodgeTriggerRadius) return false;

            Vector3 velocity = playerBody.linearVelocity;
            if (velocity.magnitude < 8f) return false;
            if (Vector3.Dot(velocity.normalized, toEnemy.normalized) < 0.65f) return false;

            float closingSpeed = Vector3.Dot(velocity, toEnemy / distance);
            if (closingSpeed < 6f) return false;
            float timeToImpact = distance / closingSpeed / Mathf.Max(Time.timeScale, 1f);
            return timeToImpact <= dodgeLeadSeconds;
        }

        void BeginDodge()
        {
            Vector3 approach = playerBody != null ? playerBody.linearVelocity : Vector3.forward;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f) approach = player.transform.position - body.position;
            approach.y = 0f;
            approach.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, approach).normalized;

            Vector3 lateralOffset = body.position - player.transform.position;
            float sign = Vector3.Dot(side, lateralOffset) >= 0f ? 1f : -1f;

            Vector3 target = body.position + side * (sign * dodgeDistance);
            if (TryGetWalkableArea(out float minX, out float maxX, out float minZ, out float maxZ))
            {
                target.x = Mathf.Clamp(target.x, minX, maxX);
                target.z = Mathf.Clamp(target.z, minZ, maxZ);
            }

            state = EnemyState.Launching;
            lastFlightWasAttack = false;
            dodgeCooldownRemaining = dodgeCooldownSeconds;
            if (bodyRenderer != null) bodyRenderer.material.color = IdleColor;
            attackTarget = target;
            fallVelocity = 0f;

            Vector3 toTarget = target - body.position;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float range = Mathf.Max(flat.magnitude, 0.5f);
            float gravityStrength = Mathf.Abs(Physics.gravity.y);
            float apexSpeed = Mathf.Sqrt(2f * gravityStrength * 1.5f);
            float flightTime = (apexSpeed + Mathf.Sqrt(Mathf.Max(apexSpeed * apexSpeed - 2f * gravityStrength * toTarget.y, 0f))) / gravityStrength;

            float verticalSpeed = toTarget.y / flightTime - 0.5f * Physics.gravity.y * flightTime;
            flightVelocity = flat / flightTime + Vector3.up * verticalSpeed;
            flightDuration = flightTime;
            flightElapsed = 0f;
        }

        void FlashWarning()
        {
            if (bodyRenderer == null) return;

            float blink = Mathf.PingPong(Time.unscaledTime * 6f, 1f);
            bodyRenderer.material.color = Color.Lerp(IdleColor, windUpColor, blink);
        }

        void BeginLaunch()
        {

            if (attackAirbornePlayers && player != null)
            {
                if (player.IsGrounded)
                {
                    attackTarget = player.transform.position;
                }
                else
                {
                    Vector3 target = player.transform.position;
                    float timeScaleFactor = Mathf.Max(Time.timeScale, 1f);
                    for (int i = 0; i < 6; i++)
                    {
                        float flightGuess = EstimateAttackFlightTime(target);
                        float gameAhead = flightGuess * timeScaleFactor;
                        if (player.TryGetFlightPositionAhead(gameAhead, out Vector3 onPath))
                        {
                            target = onPath;
                        }
                        else if (playerBody != null)
                        {

                            target = player.transform.position
                                + playerBody.linearVelocity * gameAhead
                                + 0.5f * gameAhead * gameAhead * Physics.gravity;
                        }
                    }
                    attackTarget = target;
                }
            }

            state = EnemyState.Launching;
            lastFlightWasAttack = true;
            if (bodyRenderer != null) bodyRenderer.material.color = windUpColor;

            Vector3 toTarget = attackTarget - body.position;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float range = Mathf.Max(flat.magnitude, 0.5f);
            Vector3 flatDirection = flat / range;
            range += Mathf.Max(attackOvershoot, 0f);

            float gravityStrength = Mathf.Abs(Physics.gravity.y);
            float speedTime = Mathf.Clamp(range / Mathf.Max(attackLaunchSpeed, 0.1f), attackFlightTimeRange.x, attackFlightTimeRange.y);
            float apexSpeed = Mathf.Sqrt(2f * gravityStrength * Mathf.Max(attackArcHeight, 0.1f));
            float apexTime = (apexSpeed + Mathf.Sqrt(Mathf.Max(apexSpeed * apexSpeed - 2f * gravityStrength * toTarget.y, 0f))) / gravityStrength;
            float flightTime = Mathf.Max(speedTime, apexTime);

            float verticalSpeed = toTarget.y / flightTime - 0.5f * Physics.gravity.y * flightTime;
            flightVelocity = flatDirection * (range / flightTime) + Vector3.up * verticalSpeed;
            flightDuration = flightTime;
            flightElapsed = 0f;
        }

        float EstimateAttackFlightTime(Vector3 target)
        {
            Vector3 toTarget = target - body.position;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float range = Mathf.Max(flat.magnitude, 0.5f) + Mathf.Max(attackOvershoot, 0f);
            float gravityStrength = Mathf.Abs(Physics.gravity.y);
            float speedTime = Mathf.Clamp(range / Mathf.Max(attackLaunchSpeed, 0.1f), attackFlightTimeRange.x, attackFlightTimeRange.y);
            float apexSpeed = Mathf.Sqrt(2f * gravityStrength * Mathf.Max(attackArcHeight, 0.1f));
            float apexTime = (apexSpeed + Mathf.Sqrt(Mathf.Max(apexSpeed * apexSpeed - 2f * gravityStrength * toTarget.y, 0f))) / gravityStrength;
            return Mathf.Max(speedTime, apexTime);
        }

        void UpdateFlight(float dt)
        {
            flightElapsed += dt;
            flightVelocity += Physics.gravity * dt;
            Vector3 next = body.position + flightVelocity * dt;

            float bodyRadius = transform.localScale.x * 0.5f;
            if (flightVelocity.y < 0f
                && flightElapsed >= flightDuration * 0.6f
                && Physics.Raycast(body.position, Vector3.down, out RaycastHit hit, bodyRadius + Mathf.Abs(flightVelocity.y * dt) + 0.1f)
                && hit.collider.GetComponent<KineticCubeController>() == null)
            {
                body.MovePosition(hit.point + Vector3.up * bodyRadius);
                Land(hit.collider);
                return;
            }

            body.MovePosition(next);

            if (returnLaunchToPlatform && !returnLaunching && next.y < originalSpawn.y - 12f)
            {
                TryReturnLaunch();
                return;
            }
            if (next.y < originalSpawn.y - 40f)
            {

                if (returnLaunchToPlatform && !returnLaunching) TryReturnLaunch();
                else ResetToSpawn();
            }
        }

        bool TryReturnLaunch()
        {

            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            bool found = false;
            Vector3 bestBelow = Vector3.zero;
            float bestBelowDistance = float.MaxValue;
            bool foundBelow = false;
            float bodyRadius = transform.localScale.x * 0.5f;

            foreach (Collider col in Physics.OverlapSphere(body.position, platformSearchRadius, ~0, QueryTriggerInteraction.Ignore))
            {

                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                if (col.GetComponentInParent<KineticCubeController>() != null) continue;
                if (col.GetComponentInParent<Enemy>() != null) continue;
                if (col.GetComponentInParent<FlyingEnemy>() != null) continue;
                if (col.GetComponentInParent<TurretEnemy>() != null) continue;
                if (col.GetComponentInParent<DamageWalls>() != null) continue;

                Bounds bounds = col.bounds;
                if (bounds.size.x < 4f || bounds.size.z < 4f) continue;
                if (bounds.max.y > body.position.y + 20f) continue;

                const float inset = 1.5f;
                Vector3 point = new Vector3(
                    Mathf.Clamp(body.position.x, bounds.min.x + inset, bounds.max.x - inset),
                    bounds.max.y + bodyRadius,
                    Mathf.Clamp(body.position.z, bounds.min.z + inset, bounds.max.z - inset));
                float distance = (point - body.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = point;
                    found = true;
                }
                if (point.y <= body.position.y && distance < bestBelowDistance)
                {
                    bestBelowDistance = distance;
                    bestBelow = point;
                    foundBelow = true;
                }
            }

            if (foundBelow) best = bestBelow;
            if (!found)
            {

                best = originalSpawn;
            }

            returnLaunching = true;
            BeginBallisticHop(best);
            return true;
        }

        void BeginBallisticHop(Vector3 target)
        {
            state = EnemyState.Launching;
            if (bodyRenderer != null) bodyRenderer.material.color = IdleColor;
            attackTarget = target;
            fallVelocity = 0f;

            Vector3 toTarget = target - body.position;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float range = Mathf.Max(flat.magnitude, 0.5f);
            float gravityStrength = Mathf.Abs(Physics.gravity.y);
            float apexHeight = Mathf.Max(attackArcHeight, 1.5f) + Mathf.Max(toTarget.y, 0f);
            float apexSpeed = Mathf.Sqrt(2f * gravityStrength * apexHeight);
            float flightTime = (apexSpeed + Mathf.Sqrt(Mathf.Max(apexSpeed * apexSpeed - 2f * gravityStrength * toTarget.y, 0f))) / gravityStrength;

            float verticalSpeed = toTarget.y / flightTime - 0.5f * Physics.gravity.y * flightTime;
            flightVelocity = flat / flightTime + Vector3.up * verticalSpeed;
            flightDuration = flightTime;
            flightElapsed = 0f;
        }

        bool TryHomePlatformLaunch()
        {
            if (!returnToHomePlatform || homePlatform == null) return false;
            if (!groundedSinceSpawn || homeLaunchCooldown > 0f) return false;
            if (platformBelow == homePlatform) return false;

            homeLaunchCooldown = homeLaunchRetrySeconds;
            lastFlightWasAttack = false;
            BeginBallisticHop(originalSpawn);
            return true;
        }

        void Land(Collider landedOn)
        {
            state = EnemyState.Recovering;
            stateTimer = recoverSeconds;
            cooldownRemaining = attackCooldown;
            fallVelocity = 0f;
            returnLaunching = false;

            if (lastFlightWasAttack)
            {
                if (attackConnected) stateTimer = 0.25f;
                else vulnerableTimer = Mathf.Max(vulnerableAfterAttackSeconds, attackCooldown);
            }
            attackConnected = false;
            lastFlightWasAttack = false;
            platformBelow = landedOn;
            if (bodyRenderer != null) bodyRenderer.material.color = IdleColor;
        }

        void BeginWander()
        {
            state = EnemyState.Wandering;
            spawnPoint = body.position;
            pauseRemaining = 0f;
            SetPlayerCollisionIgnored(false);
            PickNewTarget();
        }

        void SetPlayerCollisionIgnored(bool ignored)
        {
            if (bodyCollider != null && playerCollider != null)
                Physics.IgnoreCollision(bodyCollider, playerCollider, ignored);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (state != EnemyState.Launching || player == null) return;
            if (collision.collider.GetComponent<KineticCubeController>() == null) return;
            if (player.HasLaunched) return;

            Vector3 away = player.transform.position - body.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = new Vector3(flightVelocity.x, 0f, flightVelocity.z);
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
            }
            Vector3 shoveDirection = (away.normalized + Vector3.up * 0.6f).normalized;
            player.ApplyEnemyHit(shoveDirection * knockbackForce, attackEnergyDrain, postHitLaunchLockSeconds);
            attackConnected = true;

            SetPlayerCollisionIgnored(true);

            Collider ground = platformBelow;
            if (Physics.Raycast(body.position + Vector3.up * 0.05f, Vector3.down, out RaycastHit groundHit, 12f)
                && groundHit.collider.GetComponent<KineticCubeController>() == null)
            {
                ground = groundHit.collider;
                body.MovePosition(new Vector3(body.position.x, groundHit.point.y + transform.localScale.x * 0.5f, body.position.z));
            }
            Land(ground);
        }

        public bool CanBeKilledByLaunch => KillableNow;

        public virtual float MinKillEnergyFraction => 0f;

        public virtual void PunishFailedKill() { }

        public void OnHitByLaunch()
        {
            gameObject.SetActive(false);
        }

        public void ResetToSpawn()
        {
            spawnPoint = originalSpawn;
            transform.position = spawnPoint;
            if (body != null) body.position = spawnPoint;
            state = EnemyState.Wandering;
            pauseRemaining = 0f;
            cooldownRemaining = 0f;
            fallVelocity = 0f;
            returnLaunching = false;
            attackConnected = false;

            groundedSinceSpawn = false;
            lastFlightWasAttack = false;
            dodgeCooldownRemaining = 0f;
            vulnerableTimer = 0f;
            dodgeScheduled = false;
            SetPlayerCollisionIgnored(false);
            if (bodyRenderer != null) bodyRenderer.material.color = IdleColor;
            if (Physics.Raycast(spawnPoint, Vector3.down, out RaycastHit hit, 5f)) platformBelow = hit.collider;
            PickNewTarget();
            gameObject.SetActive(true);
        }
    }
}

