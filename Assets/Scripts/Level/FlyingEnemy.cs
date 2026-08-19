using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A FLYING enemy: drifts around a settable radius of its spawn point, and when the
    // player comes inside its detection range it telegraphs (warning flash, like the
    // ground enemy) and fires a red capsule projectile (EnemyProjectile) from its centre.
    //
    // The shot LEADS a moving player: at the moment of firing it solves the intercept
    // point from the player's current velocity (ballistic - gravity included while the
    // player is airborne) against the projectile's own travel speed, iteratively - so a
    // player who keeps flying their current path gets hit exactly, and relaunching in
    // time is the dodge. A standing player is simply shot where they stand.
    //
    // All movement, windup, and cooldown run on WorldMotionTime (min of scaled/unscaled):
    // slowed by the aim's bullet-time, unaffected by the launch game-speed-up - the same
    // rule as every other non-player mover. Launching INTO the flyer still kills it.
    public class FlyingEnemy : MonoBehaviour
    {
        [Header("Flight")]
        [Tooltip("How far from the spawn point the enemy may drift.")]
        public float flyRadius = 9f;
        [Tooltip("Vertical extent of the drift, as a fraction of flyRadius.")]
        [Range(0f, 1f)] public float verticalRadiusFactor = 0.4f;
        public float flySpeed = 5f;
        [Tooltip("Random pause between drift hops, min..max seconds.")]
        public Vector2 pauseRange = new Vector2(0.2f, 1f);

        [Header("Attack")]
        [Tooltip("The player inside this range triggers the attack (any height - it flies).")]
        public float detectionRadius = 22f;
        [Tooltip("Seconds of warning flash before the shot fires.")]
        public float windUpSeconds = 0.6f;
        [Tooltip("Cooldown between shots, in world-motion seconds.")]
        public float attackCooldown = 2.5f;
        public Color windUpColor = new Color(1f, 0.25f, 0.15f);

        [Header("Posture and turning")]
        [Tooltip("Degrees the body is pitched NOSE-DOWN while flying. A hunched flyer carries anything mounted on its back tilted upward - which is what makes a back weak spot reachable from above.")]
        public float hunchPitchDegrees = 0f;
        [Tooltip("Degrees per second the body turns (as a slerp rate). Lower = heavier, slower to bring its aim round.")]
        public float turnSpeed = 6f;
        [Tooltip("Seconds after firing during which it neither moves nor turns - a committed, readable pause the player can punish.")]
        public float postFireHoldSeconds = 0f;

        [Header("Obstacle avoidance")]
        [Tooltip("ON: wander targets are only accepted where there is clear air, and a drift that would carry it into something turns away instead. Needed wherever the flyer patrols among walls rather than open sky.")]
        public bool avoidObstacles = false;
        [Tooltip("Clear space a wander target needs around it, in world units.")]
        public float obstacleClearance = 3.5f;
        [Tooltip("Candidate points tried before it gives up and simply holds station this pause.")]
        public int targetAttempts = 10;

        [Header("Survived-hit stagger")]
        [Tooltip("Degrees the flyer pitches NOSE-DOWN while staggered - slumping forward swings its back (and the weak spot on it) up to face the player.")]
        public float stunLeanDegrees = 80f;
        [Tooltip("Seconds it hangs motionless after surviving a launch: no drifting, turning, winding up or firing. This is the opening to line the weak spot up.")]
        public float stunSeconds = 1f;
        [Tooltip("Body colour while stunned - blue, clearly apart from the active red.")]
        public Color stunColor = new Color(0.2f, 0.45f, 1f);
        [Tooltip("Seconds before the stun ends during which the body BLINKS between the stun blue and the active red - the warning that it is about to wake up.")]
        public float stunBlinkSeconds = 0.4f;

        [Header("Projectile")]
        public float projectileSpeed = 26f;
        public float projectileLifetimeSeconds = 6f;
        [Tooltip("Impulse applied to the player on a hit.")]
        public float projectileKnockback = 22f;
        [Range(0f, 1f)] public float projectileEnergyDrain = 0.1f;
        [Tooltip("Seconds the player cannot launch/aim after being hit.")]
        public float projectileLaunchLock = 0.5f;
        [Tooltip("Capsule scale - Y is the long half-axis (pointed at the player).")]
        public Vector3 projectileScale = new Vector3(0.35f, 0.6f, 0.35f);
        public Color projectileColor = new Color(0.9f, 0.1f, 0.08f);

        enum FlyerState { Patrol, WindingUp }

        Rigidbody body;
        KineticCubeController player;
        Rigidbody playerBody;
        Renderer bodyRenderer;
        Color restColor;

        Vector3 spawnPoint;
        Vector3 currentTarget;
        float postFireHoldRemaining;
        float stunRemaining;
        // Read by WeakSpotFlyingEnemy, which only widens its kill hitbox while staggered.
        public bool IsStunned => stunRemaining > 0f;
        Quaternion stunRotation = Quaternion.identity;
        float pauseRemaining;
        FlyerState state = FlyerState.Patrol;
        float stateTimer;
        float cooldownRemaining;

        void Start()
        {
            spawnPoint = transform.position;
            player = FindAnyObjectByType<KineticCubeController>();
            if (player != null) playerBody = player.GetComponent<Rigidbody>();
            bodyRenderer = GetComponentInChildren<Renderer>();
            if (bodyRenderer != null) restColor = bodyRenderer.material.color;

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            PickNewTarget();
        }

        void FixedUpdate()
        {
            float dt = WorldMotionTime.FixedDeltaTime;
            if (cooldownRemaining > 0f) cooldownRemaining -= dt;

            // Staggered by a hit it survived: hangs exactly where it was, slumped forward,
            // doing nothing at all until it shakes it off.
            if (stunRemaining > 0f)
            {
                stunRemaining -= dt;
                body.MoveRotation(stunRotation);
                if (bodyRenderer != null)
                {
                    // Blue for the whole stun; in the final stretch it BLINKS blue<->red
                    // (unscaled time, so the tell keeps flashing through bullet-time),
                    // then hands back the rest colour the moment the stun expires.
                    if (stunRemaining <= 0f) bodyRenderer.material.color = restColor;
                    else if (stunRemaining <= stunBlinkSeconds)
                    {
                        bool showRed = Mathf.FloorToInt(Time.unscaledTime * 8f) % 2 == 0;
                        bodyRenderer.material.color = showRed ? restColor : stunColor;
                    }
                    else bodyRenderer.material.color = stunColor;
                }
                return;
            }

            switch (state)
            {
                case FlyerState.Patrol:
                    // The post-shot hold: frozen outright - no drift, no turning, no fresh
                    // windup. It has committed to the shot and has to sit in it.
                    if (postFireHoldRemaining > 0f)
                    {
                        postFireHoldRemaining -= dt;
                        break;
                    }
                    UpdatePatrol(dt);
                    if (cooldownRemaining <= 0f && PlayerInRange()) BeginWindUp();
                    break;

                case FlyerState.WindingUp:
                    stateTimer -= dt;
                    FlashWarning();
                    FaceTowards(player != null ? player.transform.position : transform.position + transform.forward, dt);
                    if (stateTimer <= 0f) Fire();
                    break;
            }
        }

        // ---------- Patrol ----------

        void UpdatePatrol(float dt)
        {
            if (pauseRemaining > 0f)
            {
                pauseRemaining -= dt;
                if (pauseRemaining <= 0f) PickNewTarget();
                return;
            }

            Vector3 position = body.position;
            Vector3 toTarget = currentTarget - position;
            float distance = toTarget.magnitude;
            float step = flySpeed * dt;

            if (distance <= step)
            {
                body.MovePosition(currentTarget);
                pauseRemaining = Random.Range(pauseRange.x, pauseRange.y);
            }
            else
            {
                Vector3 nextPosition = position + toTarget / distance * step;
                // Even a clear destination can have a wall across the way to it - the drift
                // is checked step by step, and it re-picks rather than sliding into one.
                if (avoidObstacles && !HasRoomAt(nextPosition, BodyRadius + 0.5f))
                {
                    PickNewTarget();
                    return;
                }
                body.MovePosition(nextPosition);
                FaceTowards(currentTarget, dt);
            }
        }

        void PickNewTarget()
        {
            int attempts = avoidObstacles ? Mathf.Max(targetAttempts, 1) : 1;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector3 offset = Random.insideUnitSphere;
                Vector3 candidate = spawnPoint + new Vector3(
                    offset.x * flyRadius,
                    offset.y * flyRadius * verticalRadiusFactor,
                    offset.z * flyRadius);

                if (!avoidObstacles || HasRoomAt(candidate, obstacleClearance))
                {
                    currentTarget = candidate;
                    return;
                }
            }
            // Boxed in on every try - hold station rather than pick a spot inside a wall.
            currentTarget = body != null ? body.position : transform.position;
        }

        float BodyRadius => transform.localScale.x * 0.5f;

        // Clear air at a point? Its own body, the player and other flyers do not count as
        // obstacles - only the level's geometry, which is what it must not drift into.
        bool HasRoomAt(Vector3 point, float radius)
        {
            foreach (Collider hit in Physics.OverlapSphere(point, radius,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit == null) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.GetComponentInParent<KineticCubeController>() != null) continue;
                if (hit.GetComponentInParent<FlyingEnemy>() != null) continue;
                return false;
            }
            return true;
        }

        // Turning is only ever toward where it is GOING (while patrolling) or toward the
        // player (while winding up to shoot) - it never swings round to track an incoming
        // player on its own. The hunch rides on top of whichever it is, so the body keeps
        // its nose-down posture through every turn.
        void FaceTowards(Vector3 point, float dt)
        {
            Vector3 look = point - body.position;
            // FLATTENED before the look rotation: the hunch is the body's ONE pitch. With
            // the vertical component left in, flying down toward a waypoint stacked the
            // descent's own tilt on top of the hunch (and climbing subtracted from it), so
            // the lean visibly deepened and shallowed with the travel direction.
            look.y = 0f;
            if (look.sqrMagnitude < 0.001f) return;
            Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up)
                * Quaternion.Euler(hunchPitchDegrees, 0f, 0f);
            body.MoveRotation(Quaternion.Slerp(body.rotation, target, turnSpeed * dt));
        }

        // ---------- Attack ----------

        bool PlayerInRange()
        {
            if (player == null) return false;
            return (player.transform.position - body.position).sqrMagnitude <= detectionRadius * detectionRadius;
        }

        void BeginWindUp()
        {
            state = FlyerState.WindingUp;
            stateTimer = windUpSeconds;
        }

        void FlashWarning()
        {
            if (bodyRenderer == null) return;
            float blink = Mathf.PingPong(Time.unscaledTime * 7f, 1f);
            bodyRenderer.material.color = Color.Lerp(restColor, windUpColor, blink);
        }

        void Fire()
        {
            state = FlyerState.Patrol;
            cooldownRemaining = attackCooldown;
            postFireHoldRemaining = postFireHoldSeconds;
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
            if (player == null) return;

            Vector3 intercept = PredictIntercept();
            Vector3 direction = intercept - body.position;
            if (direction.sqrMagnitude < 0.01f) direction = player.transform.position - body.position;

            EnemyProjectile.Spawn(body.position, direction.normalized, projectileScale, projectileColor,
                projectileSpeed, projectileLifetimeSeconds, projectileKnockback,
                projectileEnergyDrain, projectileLaunchLock);
        }

        // Iterative ballistic intercept: where will the player be when a shot travelling
        // at projectileSpeed gets there? Converges in a handful of iterations. Uses the
        // player's REAL velocity (zero while standing/frozen, ballistic while airborne).
        // The projectile moves on WorldMotionTime while the player moves on scaled time,
        // so during the launch speed-up its effective speed in GAME seconds shrinks by
        // the timescale - divided out here so the lead stays exact.
        Vector3 PredictIntercept()
        {
            Vector3 basePosition = player.transform.position;
            Vector3 velocity = playerBody != null ? playerBody.linearVelocity : Vector3.zero;
            bool airborne = !player.IsGrounded && velocity.sqrMagnitude > 0.01f;

            float effectiveSpeed = projectileSpeed / Mathf.Max(Time.timeScale, 1f);
            effectiveSpeed = Mathf.Max(effectiveSpeed, 0.1f);

            float time = 0f;
            Vector3 predicted = basePosition;
            for (int i = 0; i < 8; i++)
            {
                predicted = basePosition + velocity * time;
                if (airborne) predicted += 0.5f * time * time * Physics.gravity;
                time = Vector3.Distance(body.position, predicted) / effectiveSpeed;
            }
            return predicted;
        }

        // ---------- Kill / respawn (same contract as the ground enemy) ----------

        [Tooltip("Minimum launch-energy fraction a kill needs - a cheaper hit staggers instead. 0 = any launch kills.")]
        [Range(0f, 1f)] public float minKillEnergyFraction = 0f;

        // Whether a launch crash on the given collider may kill this flyer. The base
        // flyer dies to any hit; the weak-spot variant only through its back cube.
        public virtual bool LaunchKillAllowedFor(Collider hitCollider) => true;

        public void OnHitByLaunch()
        {
            gameObject.SetActive(false);
        }

        // A launch that connected but NOT on a killing spot. The flyer is knocked out of
        // whatever it was doing and left hanging, slumped forward - which is the whole
        // point: the slump rolls its back uppermost, presenting the weak spot for the
        // follow-up shot instead of leaving the player to chase a moving target.
        public void OnLaunchSurvived()
        {
            stunRemaining = stunSeconds;
            state = FlyerState.Patrol;
            stateTimer = 0f;
            pauseRemaining = 0f;
            postFireHoldRemaining = 0f;

            // Slumps from wherever it was already pointing, keeping its heading.
            Vector3 heading = body.rotation * Vector3.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;
            stunRotation = Quaternion.LookRotation(heading.normalized, Vector3.up)
                * Quaternion.Euler(stunLeanDegrees, 0f, 0f);

            if (bodyRenderer != null) bodyRenderer.material.color = stunColor; // stunned reads BLUE from the first frame
        }

        public void ResetToSpawn()
        {
            transform.position = spawnPoint;
            if (body != null) body.position = spawnPoint;
            state = FlyerState.Patrol;
            pauseRemaining = 0f;
            cooldownRemaining = 0f;
            postFireHoldRemaining = 0f;
            stunRemaining = 0f;
            if (bodyRenderer != null) bodyRenderer.material.color = restColor;
            PickNewTarget();
            gameObject.SetActive(true);
        }
    }
}
