using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // When a player launch can actually KILL this enemy. Outside its window the crash
    // still registers normally (refund, flight ends) - the enemy just survives it.
    public enum EnemyKillWindow
    {
        Always,           // regular enemies - any launch kills
        WhileCoolingDown, // hunter A - killable only during the post-attack cooldown
        WhileWindingUp,   // hunter B ("stalker") - killable only during the telegraph
    }

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
        // The body colour answers ONE question at a glance: can a launch kill this RIGHT
        // NOW? Red says no, purple says yes. Which moments count as "yes" depends on the
        // kill window, so the same two colours read correctly for every variant.
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
        protected KineticCubeController player; // subclasses shove the player on failed kills
        Collider bodyCollider;
        Collider playerCollider;
        Renderer bodyRenderer;

        // Is a player launch lethal to this enemy at this instant? Each kill window makes
        // a different moment the punishable one, and the colour follows whichever it is.
        // An aim pointed at this enemy changes NOTHING here: the kill window is earned by
        // the enemy's own behaviour (a missed attack, a telegraph), never by being aimed
        // at - the preview's green judges energy only, not this window.
        bool KillableNow => killWindow switch
        {
            EnemyKillWindow.WhileCoolingDown => vulnerableTimer > 0f,
            EnemyKillWindow.WhileWindingUp => state == EnemyState.WindingUp,
            _ => true, // Always - a plain enemy is punishable whenever you can reach it
        };

        // What the body sits at when it is not mid-flash.
        Color IdleColor => KillableNow ? vulnerableColor : baseColor;

        Vector3 spawnPoint;    // wander centre - re-centres wherever an attack lands
        Vector3 originalSpawn; // the scene position, immutable - respawns always come back here
        // The platform standing under the enemy at BOOT. Unlike platformBelow (which follows
        // it to wherever it lands) this never moves, so it stays the answer to "where do I
        // belong". Null when the enemy booted over open air.
        Collider homePlatform;
        float homeLaunchCooldown;
        Collider platformBelow;
        Vector3 currentTarget;
        float pauseRemaining;

        EnemyState state = EnemyState.Wandering;
        float stateTimer;
        float cooldownRemaining;
        Vector3 attackTarget;    // the player's position when the windup began
        Vector3 flightVelocity;  // the ballistic arc, integrated manually
        float flightDuration;    // solved flight time of the current attack arc
        float flightElapsed;
        float fallVelocity;      // vertical speed while unsupported outside a launch

        // Virtual for the sized variants: their override applies the size multipliers
        // FIRST, then runs this base wiring unchanged.
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
                    // The hunter KNOWS the moment a launch is fired at it: the fired
                    // trajectory's landing point is public knowledge, and a landing on
                    // this enemy books a dodge timed to escape just before impact.
                    player.LaunchFired += OnPlayerLaunchFired;
                    player.CrashRegistered += OnPlayerCrashedSomewhere;
                }
            }
            bodyCollider = GetComponent<Collider>();
            if (player != null) playerCollider = player.GetComponent<Collider>();
            bodyRenderer = GetComponentInChildren<Renderer>();
            // Opens on its state colour instead of whatever the shared material happens to
            // be. Assigning through .material instances a per-renderer copy, so the
            // material ASSET is never written to.
            if (bodyRenderer != null) bodyRenderer.material.color = IdleColor;

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 5f))
            {
                platformBelow = hit.collider;
                homePlatform = hit.collider; // where it belongs, fixed for the whole run
            }

            PickNewTarget();
        }

        void OnDestroy()
        {
            if (player == null) return;
            player.LaunchFired -= OnPlayerLaunchFired;
            player.CrashRegistered -= OnPlayerCrashedSomewhere;
        }

        // Fired the instant ANY player launch leaves: if its predicted landing sits on
        // this enemy, book the dodge for (flight time - lead) real seconds from now -
        // not immediately, just in time.
        void OnPlayerLaunchFired()
        {
            if (!dodgePlayerLaunches || vulnerableTimer > 0f || dodgeCooldownRemaining > 0f) return;
            if (player == null || !player.HasValidPredictedLanding) return;

            float hitRadius = transform.localScale.x * 0.5f + dodgePredictedHitRadius;
            if ((player.LastPredictedLanding - body.position).sqrMagnitude > hitRadius * hitRadius) return;

            scheduledDodgeTimer = Mathf.Max(player.PredictedFlightRealSecondsLive - dodgeLeadSeconds, 0.02f);
            dodgeScheduled = true;
        }

        // The flight ended somewhere (a wall, another enemy...) - the booked dodge is moot.
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

            // The cooldown kill-window has a TELL: a soft white pulse for as long as the
            // enemy is punishable (the windup window's tell is the existing orange flash).
            // Held every tick (the windup and launch states paint themselves): red while
            // untouchable, the old purple the moment a launch would land the kill. A
            // punishable enemy also pulses brighter, so the opening reads instantly even
            // against a busy background.
            if (bodyRenderer != null && state != EnemyState.WindingUp && state != EnemyState.Launching)
            {
                if (KillableNow && killWindow != EnemyKillWindow.Always)
                {
                    // A SHALLOW breath, not a wash to white: the punish window has to stay
                    // recognisably the enemy's own tier colour, because that colour is what
                    // the charge bar is matched against to know the launch will kill. At the
                    // old 0.4 the body spent the whole window visibly lighter than its tier.
                    float pulse = Mathf.PingPong(Time.unscaledTime * 2.5f, 0.15f);
                    bodyRenderer.material.color = Color.Lerp(vulnerableColor, Color.white, pulse);
                }
                else
                {
                    bodyRenderer.material.color = IdleColor;
                }
            }

            // A booked dodge counts down in real seconds and fires from any ground state.
            if (dodgeScheduled)
            {
                if (player == null || !player.HasLaunched) dodgeScheduled = false;
                else
                {
                    scheduledDodgeTimer -= dt;
                    // vulnerableTimer re-checked HERE too: if its own attack landed after
                    // the booking, the punish window wins and the dodge is forfeited. A
                    // windup-killable enemy mid-telegraph is likewise committed.
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
                    // The punish window is a genuine opening: while vulnerable the enemy
                    // STANDS STILL - no wandering, no attacking (dodges are already blocked
                    // elsewhere). Recovery (1s) is shorter than the window (up to the full
                    // attack cooldown), so without this it strolled off mid-punish-window.
                    if (vulnerableTimer > 0f)
                    {
                        MoveGrounded(body.position, dt); // gravity settle only, no travel
                        break;
                    }
                    // Standing on the wrong platform outranks patrolling it: go home first.
                    if (TryHomePlatformLaunch()) break;
                    // A DETECTED player ends the wandering: the enemy holds its ground,
                    // squared on the target, until the attack cooldown lets it strike.
                    // Wandering only resumes once the player is genuinely gone - off this
                    // platform and outside the detection radius.
                    if (PlayerDetected())
                    {
                        if (MoveGrounded(body.position, dt)) break; // gravity settle only
                    }
                    else if (MoveGrounded(WanderStep(dt), dt)) break;
                    if (cooldownRemaining <= 0f && PlayerIsAttackable()) BeginWindUp();
                    break;

                case EnemyState.WindingUp:
                    // Dodging out of the telegraph cancels the attack - slippery BEFORE it
                    // strikes, but never during the post-attack punish window (ShouldDodge).
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

        // Grounded movement with the void catch: returns true if the enemy left this state
        // (return-launched or self-reset - the caller must stop touching state this tick).
        bool MoveGrounded(Vector3 horizontalTarget, float dt)
        {
            Vector3 next = WithGroundedY(horizontalTarget, dt);

            // Hunter: genuinely falling (not a step-down) - launch back to a platform
            // instead of dropping into the void.
            // The return launch is for being stranded MID-RUN, never for the drop onto the
            // ground at spawn: a stalker placed even slightly above its platform used to
            // read that first fall as the void and launch itself into the air on boot and
            // after every respawn. It has to have stood somewhere first.
            if (returnLaunchToPlatform && groundedSinceSpawn && lastMoveUnsupported
                && fallVelocity < -4f && OverGenuineVoid())
            {
                return TryReturnLaunch();
            }
            if (next.y < originalSpawn.y - 40f)
            {
                // The deep backstop: a hunter that somehow slipped past the void trigger
                // still LAUNCHES home if it has not already tried - the teleport is only
                // for a return arc that itself failed.
                if (returnLaunchToPlatform && !returnLaunching) return TryReturnLaunch();
                ResetToSpawn();
                return true;
            }
            body.MovePosition(next);
            return false;
        }

        bool lastMoveUnsupported; // set by WithGroundedY - drives the hunter's return launch
        bool groundedSinceSpawn;  // gates that launch until the enemy has actually landed
        bool attackConnected;     // this attack flight body-checked the player - no punish window
        bool returnLaunching;     // a return hop is in flight - no re-trigger until it lands
        bool lastFlightWasAttack; // Land() opens the punish window only after real attacks
        float dodgeCooldownRemaining;
        float vulnerableTimer;    // > 0: freshly attacked - dodging disabled, punish freely
        Rigidbody playerBody;
        bool dodgeScheduled;      // the player fired AT this enemy - hop is booked
        float scheduledDodgeTimer; // real seconds until that hop (impact time minus lead)

        [Tooltip("How far below itself the enemy looks for ground. Must comfortably exceed the biggest drop in the level: anything further down is invisible to it, and a drop it cannot see reads as empty void.")]
        public float groundProbeDistance = 60f;

        // Is there genuinely NOTHING to land on below? The return launch is for being
        // stranded over emptiness, not for stepping off a ledge onto lower ground - and the
        // hazard floor does not count as somewhere to land.
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
                return false; // real ground somewhere below - it is only falling, not stranded
            }
            return true;
        }

        // Kinematic bodies ignore gravity, so ground contact is enforced by hand: the enemy
        // rests exactly on the surface below and falls under real gravity when unsupported.
        Vector3 WithGroundedY(Vector3 next, float dt)
        {
            lastMoveUnsupported = false;
            float bodyRadius = transform.localScale.x * 0.5f;
            // TRIGGERS ARE NOT GROUND. Raycasts hit trigger volumes by default, so a
            // checkpoint pad (or any trigger hovering over a platform) was being treated as
            // the surface below - the enemy came to rest on top of THAT and hung in the air
            // above the platform instead of falling to it.
            //
            // The ray starts ABOVE the body, not just inside it: a ray beginning inside a
            // collider does not report it, so an enemy that ended up level with (or inside)
            // a platform saw no ground at all and sank straight through it before catching
            // itself. Starting clear of its own body finds the surface and lifts it out.
            // The reach was a fixed 8 units, which is SHORTER than the drops in these
            // levels: an enemy on a raised ledge saw nothing beneath it, called itself
            // unsupported and hurled itself at a platform - with the arena sitting quietly
            // 15 units below. A reach that spans the level's real drops also stops a fast
            // fall from tunnelling through a thin platform, since the clamp below can only
            // apply while the surface is actually visible to the probe.
            // ...which puts the ray ABOVE THE ENEMY'S OWN HEAD, so pointing down its first
            // hit is its own sphere - and "resting on" your own top means teleporting up by
            // your own diameter, every tick: the self-powered ascent on boot and respawn.
            // The original inside-the-collider origin skipped self implicitly; an origin
            // above the body must skip it EXPLICITLY. RaycastAll, nearest hit that is not
            // itself, the player, or the hazard floor.
            float probeLift = bodyRadius + 0.5f;
            RaycastHit hit = default;
            bool foundGround = false;
            foreach (RaycastHit candidate in Physics.RaycastAll(next + Vector3.up * probeLift, Vector3.down,
                         probeLift + groundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (candidate.collider == null) continue;
                if (candidate.collider.transform == transform || candidate.collider.transform.IsChildOf(transform)) continue;
                if (candidate.collider.GetComponent<KineticCubeController>() != null) continue;
                // The hazard floor is not a place to stand - falling onto it must read as
                // falling into the void, which resets the enemy.
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
                    groundedSinceSpawn = true; // it has stood on something now
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

        // Standing on the enemy's OWN platform counts as detected no matter the distance -
        // its patrol ground is its territory, and a shared floor leaves nowhere to hide on
        // a platform bigger than the radius. Geometry, not identity: the player's grounded
        // position inside the platform's footprint, near its top face.
        bool PlayerOnMyPlatform()
        {
            if (player == null || platformBelow == null || !player.IsGrounded) return false;
            Bounds bounds = platformBelow.bounds;
            Vector3 p = player.transform.position;
            if (p.x < bounds.min.x || p.x > bounds.max.x) return false;
            if (p.z < bounds.min.z || p.z > bounds.max.z) return false;
            // Near the TOP face only - grounded on a platform below a bridge must not
            // read as sharing the bridge.
            return p.y >= bounds.max.y - 1f && p.y <= bounds.max.y + 4f;
        }

        // Detection: the radius, OR the shared platform. Once a player is detected the
        // enemy stays ON them (no more wandering - see the Wandering state) until they are
        // genuinely gone: off the platform AND outside the radius.
        bool PlayerDetected()
        {
            if (player == null) return false;
            if (PlayerOnMyPlatform()) return true;
            Vector3 toPlayer = player.transform.position - body.position;
            return toPlayer.sqrMagnitude <= detectionRadius * detectionRadius;
        }

        // Grounded prey within range, at roughly this height - airborne players are safe,
        // which is the whole rhythm of the game. The HUNTER variant drops both caveats:
        // anyone inside the radius is fair game, airborne included.
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
            attackTarget = player.transform.position; // the OLD position - no homing
        }

        // A launching player bearing down on this enemy - and the estimated ARRIVAL is
        // dodgeLeadSeconds away: the hop happens just in time, not the moment the player
        // enters some radius. Blocked entirely while the enemy is cooling down from its
        // own attack (vulnerableTimer, sized to the attack cooldown) - that is the
        // player's guaranteed opening.
        bool ShouldDodge()
        {
            if (player == null || playerBody == null) return false;
            if (!player.HasLaunched) return false;
            if (dodgeCooldownRemaining > 0f || vulnerableTimer > 0f) return false;
            // A windup-killable enemy is COMMITTED during its telegraph - dodging out of
            // its only kill window would make it effectively immortal.
            if (killWindow == EnemyKillWindow.WhileWindingUp && state == EnemyState.WindingUp) return false;

            Vector3 toEnemy = body.position - player.transform.position;
            float distance = toEnemy.magnitude;
            if (distance > dodgeTriggerRadius) return false;

            Vector3 velocity = playerBody.linearVelocity;
            if (velocity.magnitude < 8f) return false;
            if (Vector3.Dot(velocity.normalized, toEnemy.normalized) < 0.65f) return false;

            // Closing speed along the approach line -> time to impact, converted to REAL
            // seconds (the player's flight runs on sped-up game time; the enemy's reactions
            // run on world-motion time).
            float closingSpeed = Vector3.Dot(velocity, toEnemy / distance);
            if (closingSpeed < 6f) return false;
            float timeToImpact = distance / closingSpeed / Mathf.Max(Time.timeScale, 1f);
            return timeToImpact <= dodgeLeadSeconds;
        }

        // A short sideways ballistic hop, perpendicular to the player's approach, kept on
        // the walkable area when one is known. Reuses the flight machinery - Land() puts
        // the enemy back to wandering (and its attack cooldown restarts, so a dodge is
        // never immediately followed by a counter-attack).
        void BeginDodge()
        {
            Vector3 approach = playerBody != null ? playerBody.linearVelocity : Vector3.forward;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f) approach = player.transform.position - body.position;
            approach.y = 0f;
            approach.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, approach).normalized;
            // Hop toward whichever side the enemy already sits on relative to the approach
            // line - away from the incoming path, never across it.
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
            // Blinks between the flash and the CURRENT state colour, so the windup carries
            // both messages at once: an attack is coming, and (for a stalker, whose only
            // kill window IS the windup) this is your moment to hit it.
            float blink = Mathf.PingPong(Time.unscaledTime * 6f, 1f);
            bodyRenderer.material.color = Color.Lerp(IdleColor, windUpColor, blink);
        }

        void BeginLaunch()
        {
            // HUNTER targeting at the moment of firing: a grounded player is struck at
            // their EXACT current position; an airborne player is INTERCEPTED - the enemy
            // reads the player's own fired trajectory (the same data the aim showed) and
            // solves where they'll be when this launch arrives, iteratively.
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
                            target = onPath; // the authoritative path the player will fly
                        }
                        else if (playerBody != null)
                        {
                            // Plain fall (no fired trajectory): simple ballistic lead.
                            target = player.transform.position
                                + playerBody.linearVelocity * gameAhead
                                + 0.5f * gameAhead * gameAhead * Physics.gravity;
                        }
                    }
                    attackTarget = target;
                }
            }

            state = EnemyState.Launching;
            lastFlightWasAttack = true; // landing this flight opens the punish window
            if (bodyRenderer != null) bodyRenderer.material.color = windUpColor;

            // A player-style ballistic launch, solved for TIME instead of angle. The landing
            // point sits attackOvershoot BEHIND the player's old position so the descending
            // tail of the arc sweeps through the player rather than ending at their feet.
            // Flight time comes from attackLaunchSpeed (clamped), then stretches if needed
            // so the arc genuinely peaks at attackArcHeight - a real rise-and-descend arc,
            // never a ground-skimming line, and never lifted off its exact landing point.
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

        // The attack arc's flight time for a given target - the same maths BeginLaunch
        // uses, extracted so the intercept iteration can converge on it.
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

            // Land on whatever is under the descending arc - but not before most of the
            // solved flight time has passed. The fast attack arc is nearly flat and skims
            // the ground the whole way; without this guard the graze registered as a
            // landing and the enemy stopped short of the player.
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

            // Overshot into the void (the player baited it off the edge, or the attack
            // missed everything). Hunters launch back to the nearest platform; ordinary
            // enemies self-reset. Depth threshold well above the reset one, so the return
            // fires while there is still room to arc back up.
            if (returnLaunchToPlatform && !returnLaunching && next.y < originalSpawn.y - 12f)
            {
                TryReturnLaunch();
                return;
            }
            if (next.y < originalSpawn.y - 40f)
            {
                // Same rule as the grounded backstop: launch home first, teleport only if
                // a return arc already failed.
                if (returnLaunchToPlatform && !returnLaunching) TryReturnLaunch();
                else ResetToSpawn();
            }
        }

        // Hunter: pick the nearest standable platform top and ballistically hop onto it,
        // reusing the attack-flight machinery (UpdateFlight lands it, Land() re-centres
        // the wander there). Returns true - the caller's state handling is done.
        bool TryReturnLaunch()
        {
            // Two-tier choice: platforms whose top is BELOW the hunter's current height are
            // strongly preferred (launching down/level while still above the target is a
            // reliable arc); higher platforms are the fallback only.
            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            bool found = false;
            Vector3 bestBelow = Vector3.zero;
            float bestBelowDistance = float.MaxValue;
            bool foundBelow = false;
            float bodyRadius = transform.localScale.x * 0.5f;

            foreach (Collider col in Physics.OverlapSphere(body.position, platformSearchRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                // Standable geometry only: no dynamic bodies, no actors, no hazards.
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                if (col.GetComponentInParent<KineticCubeController>() != null) continue;
                if (col.GetComponentInParent<Enemy>() != null) continue;
                if (col.GetComponentInParent<FlyingEnemy>() != null) continue;
                if (col.GetComponentInParent<TurretEnemy>() != null) continue;
                if (col.GetComponentInParent<DamageWalls>() != null) continue;

                Bounds bounds = col.bounds;
                if (bounds.size.x < 4f || bounds.size.z < 4f) continue;        // too thin to stand on
                if (bounds.max.y > body.position.y + 20f) continue;            // unreachably high

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

            if (foundBelow) best = bestBelow; // launch while still ABOVE the target if possible
            if (!found)
            {
                // NO platform in range: hop home to the boot spawn instead of teleporting -
                // the arc solver handles any offset, so the flight back is always flyable.
                // A return-capable enemy visibly LAUNCHES back; it never blinks to spawn
                // (that read as "they just spawn back", direct report). The -40 reset stays
                // as the final backstop should even this arc somehow fail.
                best = originalSpawn;
            }

            returnLaunching = true; // one attempt per fall - the -40 reset is the backstop
            BeginBallisticHop(best);
            return true;
        }

        // The shared arc: a time-solved ballistic hop onto a point, with enough apex to
        // clear the ledge in between. Used by the void recovery and the walk-home launch.
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

        // Knocked onto SOMEBODY ELSE'S ground - by a dodge, a missed attack, a shove - the
        // enemy hops back to the platform it booted on. Its patrol belongs to that platform,
        // so a stray landing elsewhere would otherwise leave it wandering the wrong ledge
        // for the rest of the run. Only from a settled wander: never mid-attack, and never
        // while vulnerable, since the punish window must stay a standing target.
        bool TryHomePlatformLaunch()
        {
            if (!returnToHomePlatform || homePlatform == null) return false;
            if (!groundedSinceSpawn || homeLaunchCooldown > 0f) return false;
            if (platformBelow == homePlatform) return false;

            homeLaunchCooldown = homeLaunchRetrySeconds;
            lastFlightWasAttack = false; // a commute earns the player no punish window
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
            // Landed after a real ATTACK: dodging switches off while it cools down from
            // attacking (the FULL attack cooldown, or the configured minimum if that is
            // longer) - the player's guaranteed opening starts the moment it touches down.
            //
            // UNLESS the attack CONNECTED: a landed hit is a won exchange, so there is no
            // punish window and no long recovery - the enemy straightens up almost at once.
            // The brief beat that remains is collision grace, not recovery: contact with
            // the player is ignored until wandering resumes (the enemy lands right where
            // the player stood, and re-enabling collision in the same instant let its
            // kinematic collider eat the very knockback it just dealt).
            if (lastFlightWasAttack)
            {
                if (attackConnected) stateTimer = 0.25f;
                else vulnerableTimer = Mathf.Max(vulnerableAfterAttackSeconds, attackCooldown);
            }
            attackConnected = false;
            lastFlightWasAttack = false;
            platformBelow = landedOn; // the walkable area follows the enemy to its new platform
            if (bodyRenderer != null) bodyRenderer.material.color = IdleColor;
        }

        void BeginWander()
        {
            state = EnemyState.Wandering;
            spawnPoint = body.position; // radius wandering re-centres on wherever it landed
            pauseRemaining = 0f;
            SetPlayerCollisionIgnored(false);
            PickNewTarget();
        }

        void SetPlayerCollisionIgnored(bool ignored)
        {
            if (bodyCollider != null && playerCollider != null)
                Physics.IgnoreCollision(bodyCollider, playerCollider, ignored);
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
            player.ApplyEnemyHit(shoveDirection * knockbackForce, attackEnergyDrain, postHitLaunchLockSeconds);
            attackConnected = true; // a LANDED hit - this attack earns no punish window

            // No further contacts until the enemy is back to wandering: the landed enemy
            // sits right where the player stood, and its infinite-mass kinematic collider
            // pinning the player was eating the knockback.
            SetPlayerCollisionIgnored(true);

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

        // Whether a launch hitting RIGHT NOW would kill - read by the crash pipeline.
        // The windows map onto the enemy's own tells: the windup flash and the post-
        // attack vulnerability pulse.
        // Mirrors KillableNow exactly (colour and gameplay must never disagree), the
        // lethal-aim window included.
        public bool CanBeKilledByLaunch => KillableNow;

        // The minimum launch-energy fraction a kill requires (on top of the kill window).
        // The base enemy asks nothing; the sized variants raise it per class, and the
        // crash pipeline routes a cheaper launch to PunishFailedKill instead.
        public virtual float MinKillEnergyFraction => 0f;

        // A vulnerable enemy hit by an UNDER-charged launch: base enemies never get here
        // (their minimum is 0), the sized variants hurt the player back.
        public virtual void PunishFailedKill() { }

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
            spawnPoint = originalSpawn; // undo any wander re-centring from past attacks
            transform.position = spawnPoint;
            if (body != null) body.position = spawnPoint;
            state = EnemyState.Wandering;
            pauseRemaining = 0f;
            cooldownRemaining = 0f;
            fallVelocity = 0f;
            returnLaunching = false;
            attackConnected = false;
            // A fresh spawn has not touched down yet, so it must not read its opening
            // descent as "stranded over the void" and hurl itself at a platform.
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
