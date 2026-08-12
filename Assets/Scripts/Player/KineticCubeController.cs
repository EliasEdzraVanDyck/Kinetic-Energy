using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Level;
using System.Collections.Generic;

namespace KineticEnergy.Player
{
    // How the midair-aim slow-down is paid for. Levels pick one:
    //  - Unlimited:  slowing down while aiming is free (The Quarry - the toy test).
    //  - AimBudget:  a separate resource of aimBudgetSeconds is drained while slowed;
    //                it refills on every crash. (The Gauntlet, Variant A.)
    //  - EnergyTank: slowing down drains the regular energy tank at tankDrainPerSecond.
    //                Thinking and moving compete for the same fuel. (Variant B.)
    // While the resource is empty the midair aim STAYS USABLE - the cube just no longer
    // freezes and time no longer slows, so aiming happens in real time mid-fall.
    public enum SlowdownMode
    {
        Unlimited,
        AimBudget,
        EnergyTank,
    }

    // The launch-cube: charge a launch, fly, crash-stick, launch again.
    //
    // CONTROLS (the one scheme this project kept - grounded and midair are separate):
    //  Grounded - hold Left Trigger / Right Mouse to aim (arrow + dotted trail, charge grows
    //             over time), Right Trigger / Left Mouse fires. Holding South / Space instead
    //             charges a straight-UP launch, released to fire.
    //  Midair   - hold Left Trigger / Right Mouse to aim in first person: the energy dial is
    //             adjusted with the right stick (up/down) or the mouse wheel, and Right
    //             Trigger / Left Mouse CONFIRMS the launch along the camera's look direction.
    //             West / E charges the GROUND POUND: a straight-down launch that smashes
    //             through breakable crack panes.
    //  Left Bumper cancels any charge. Right Bumper shows/hides the trajectory trail.
    //
    // CRASH RULES: any surface stops the cube dead and sticks it there. Near-flat ground can
    // always be walked away from. Walls/ceilings hold permanently only when they carry a
    // StickySurface component - anything else clings for nonStickyWallStickDuration seconds
    // and then drops the cube back into gravity. A NonStickSurface never registers a crash.
    //
    // ENERGY: every launch spends the charge it was fired with. Crashing refunds by the rule
    // that survived playtesting: a grounded launch pays back exactly what it cost, a midair
    // launch pays spend * (1 + midairRefundSpendFactor * spend), and the ground pound pays
    // spend * groundPoundRefundMultiplier (at least groundPoundMinRefund of the tank).
    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeController : MonoBehaviour
    {
        [Header("Launch")]
        [Tooltip("Exit speed of a zero-charge launch (the cube's mass is 1, so force = speed).")]
        public float minLaunchForce = 60f;
        [Tooltip("Exit speed of a full-charge launch.")]
        public float maxLaunchForce = 130f;
        [Tooltip("Seconds of charging that count as a full charge.")]
        public float maxChargeTime = 1.5f;
        // Damping is interpolated by charge alongside force: linear drag eats proportionally
        // more of a slow shot's range than a fast one's, so a single constant can't keep both
        // ends of the charge range landing at sensible distances. Verified empirically with
        // real-physics batch simulations back when this pair was tuned.
        [Tooltip("Rigidbody linear damping applied to a zero-charge launch.")]
        public float minLaunchDamping = 2.8f;
        [Tooltip("Rigidbody linear damping applied to a full-charge launch.")]
        public float maxLaunchDamping = 1.0f;
        // The arc-shaping damping curve above would fight gravity to a near-constant fall
        // speed on a purely vertical shot - a fixed low drag keeps gravity visibly in charge
        // of a downward launch instead.
        [Tooltip("Fixed low damping used by downward (ground pound) launches instead of the charge curve.")]
        public float downLaunchDamping = 0.2f;
        // A PLAIN fall (walked off a ledge, dropped from a cling - no launch in flight) must
        // not inherit the last launch's arc-shaping drag: at damping 2.8 terminal velocity is
        // only ~11 m/s, which reads as a parachute.
        [Tooltip("Damping applied while airborne with no launch in flight, so plain falls accelerate naturally.")]
        public float plainFallDamping = 0.2f;

        [Header("Energy")]
        [Tooltip("Fraction of the tank the player starts the level with.")]
        [Range(0f, 1f)] public float startingEnergyFraction = 0.2f;
        // MUST stay 1: at exactly 1, the charge bar can never show more than the stored
        // energy, and the amount charged IS the amount deducted, by construction. Use
        // chargeAccumulationRate to make charging feel cheaper/slower instead.
        [Tooltip("Fraction of the whole tank a FULL charge costs. Keep at 1 - see the code comment.")]
        public float energyCostPerFullCharge = 1f;
        [Tooltip("GROUNDED launches can never spend the tank below this reserve. Midair launches may commit everything.")]
        [Range(0f, 1f)] public float minEnergyReserve = 0.05f;
        [Tooltip("Multiplies real seconds of holding into charge-seconds - the main knob for how fast charging feels.")]
        public float chargeAccumulationRate = 0.3f;
        // The grounded aim charge, the forward hold-charge, and the midair energy dial all
        // ACCELERATE: rate multiplier = 1 + sustainedSeconds * this. On the dial, flipping
        // between adding and removing resets the ramp, so lowering energy also speeds up the
        // longer you keep lowering. (Up/down charges use their own base+growth ramp - see
        // groundPoundChargeBaseSpeed/groundPoundChargeSpeedGrowth.)
        [Tooltip("How quickly a charge input's rate ramps up while sustained (1 = rate doubles after one second). Ramp resets when the dial flips direction.")]
        public float chargeAcceleration = 1f;
        [Tooltip("Test-level switch: the tank is pinned at 100% and refunds/costs are ignored.")]
        public bool infiniteEnergy = false;
        // Test-level switch (Level 1): a launch's cost drains from the meter OVER the
        // flight instead of instantly - 0% drained at fire, 100% by landing, paced by the
        // flight time the aim predicted. Firing a new launch midair stops the old drain, so
        // the not-yet-spent remainder stays in the meter and funds the next launch (halfway
        // through the path = half the energy still available).
        [Tooltip("Drain a launch's cost over the flight instead of instantly (see comment). Off = classic instant deduction.")]
        public bool gradualLaunchDrain = false;
        [Tooltip("Yellow energy / blue charge meter, top right - wired per scene by the setup script.")]
        public EnergyMeterController energyMeter;

        [Header("Crash Refunds")]
        [Tooltip("A grounded (first) launch's crash refunds spend * this (1 = an exact wash).")]
        public float groundedRefundMultiplier = 1f;
        [Tooltip("A midair launch's refund: spend * (base + factor * spend).")]
        public float midairRefundBaseMultiplier = 1f;
        [Tooltip("The 'factor' above - the midair multiplier RISES with how much was committed.")]
        public float midairRefundSpendFactor = 0.3f;
        [Tooltip("A pound crash immediately refunds the whole flight's spend times this (1 = an exact wash); the boost extra comes on top.")]
        public float poundFlightRefundMultiplier = 1f;

        [Header("Ground Pound (the EnergyEconomy4 mechanic)")]
        // The pound doesn't stick - it BOUNCES: a free hop of groundPoundHopHeight, then a
        // slow-mo window of groundPoundSlowDuration real seconds during which the cube hangs
        // frozen. The crash refunds the flight's WHOLE spend as a wash immediately; the
        // pound's boost EXTRA (poundSpend * (boostMultiplier - 1)) stays on offer - claimed
        // by opening a midair aim inside the window (which also starts that aim FULLY
        // charged and holds gravity off for its duration), forfeited if the window lapses.
        // An aim that closes without firing gives the extra back.
        public float groundPoundBoostMultiplier = 1.5f;
        public float groundPoundHopHeight = 0.2f;
        public float groundPoundSlowDuration = 0.5f;
        // The pound/up charge speed ramp: rate multiplier = base + growth * secondsHeld
        // (real seconds, matching the unscaled charge).
        public float groundPoundChargeBaseSpeed = 1.5f;
        public float groundPoundChargeSpeedGrowth = 5f;

        [Header("Aim Slowdown Resource")]
        [Tooltip("How the midair-aim slow-down is paid for - see the SlowdownMode enum.")]
        public SlowdownMode slowdownMode = SlowdownMode.Unlimited;
        [Tooltip("AimBudget mode: total real seconds of slow-down available. Refills on every crash.")]
        public float aimBudgetSeconds = 2f;
        // Default drains a FULL tank in aimBudgetSeconds (1 / 2s = 0.5/s), so a full tank buys
        // approximately the same total slow-time as Variant A's budget - the tuning-parity rule
        // this comparison test depends on. Log the actual values used with every test run.
        [Tooltip("EnergyTank mode: tank fraction drained per real second of slow-down.")]
        public float tankDrainPerSecond = 0.5f;
        [Tooltip("Optional bar showing the remaining aim budget (AimBudget mode only) - wired by the setup script.")]
        public EnergyMeterController slowdownMeter;

        [Header("Aiming")]
        [Range(0f, 1f)] public float aimDeadzone = 0.15f;
        [Tooltip("Degrees per second the stick moves the grounded aim.")]
        public float aimRotationSpeed = 90f;
        public float minAimPitch = -80f;
        public float maxAimPitch = 80f;
        // Negative pitch tilts UP in this project's Quaternion.Euler convention (verified
        // empirically, the sign is easy to get backwards) - -30 starts the aim 30 degrees
        // above horizontal.
        [Tooltip("Pitch the grounded aim starts at every time it opens. Negative = upward.")]
        public float defaultAimPitch = -30f;
        public Transform cameraTransform;
        public KineticEnergy.Camera.ThirdPersonOrbitCamera cameraOrbit;
        [Tooltip("Yellow direction arrow shown while a charge is being aimed.")]
        public AimArrowIndicator aimArrow;

        [Header("Hold-To-Charge Directions")]
        [Tooltip("Degrees above horizontal a direction-switched Forward charge fires at while the stick is held.")]
        public float stickAimForwardAngle = 30f;
        [Tooltip("Degrees above horizontal a Forward charge fires at with the stick centered (toward current facing).")]
        public float stickAimForwardNeutralAngle = 5f;
        [Tooltip("Stick deflection needed before a Forward charge counts the stick as 'held'.")]
        [Range(0f, 1f)] public float stickAimDeadzone = 0.9f;

        [Header("Midair Energy Dial")]
        [Tooltip("Charge added/removed per second while the right stick is pushed up/down during the midair aim.")]
        public float dialStickRate = 0.5f;
        [Tooltip("Charge added/removed per mouse-wheel notch during the midair aim.")]
        public float dialWheelStep = 0.05f;

        [Header("Landing Preview")]
        public int maxPredictionSteps = 3000;
        public float previewLineHeight = 0.65f;
        public float groundCheckDistance = 0.6f;
        public LandingPreviewController landingPreview;

        [Header("Mouse Aim Option")]
        // DEFAULT ON: the grounded aim follows raw mouse delta, and WASD drives the camera
        // while aiming. Gamepad input is untouched either way - a stick that is actively
        // aiming keeps its normal role (checked per frame via the move action's active
        // device), so controller players get the exact same controls as always.
        public bool groundedAimWithMouse = true;
        public float groundedMouseAimSensitivity = 0.15f;
        [Tooltip("WASD-as-camera turns this much faster while Always Mouse is on - keys are all-or-nothing, unlike a stick.")]
        public float wasdCameraTurnMultiplier = 1.5f;

        [Header("Time Scales")]
        [Tooltip("Global time scale while a launch is being aimed/charged midair (bullet time).")]
        public float chargeTimeScale = 0.2f;
        [Tooltip("Base global time scale while a launch is in flight and nothing is charging.")]
        public float launchFlightTimeScale = 2f;
        // The flight speed-up scales with commitment: base 200%, plus 1% of game speed for
        // every 1% of the tank the launch spent (a full-tank launch flies at 300%).
        [Tooltip("Added to the flight time scale per full tank of energy spent on the launch (1 = +1% speed per 1% energy).")]
        public float flightTimeScaleEnergyBonus = 1f;
        // Falling adds ANOTHER ramp on top: from the first descending frame the game speeds
        // up by fallSpeedUpStart, growing in even steps to fallSpeedUpEnd at the moment of
        // impact - measured as descent progress from the flight's apex down to the landing
        // height the aim predicted at fire time.
        [Tooltip("Extra game speed on the first falling frame of a flight (0.01 = +1%).")]
        public float fallSpeedUpStart = 0.01f;
        [Tooltip("Extra game speed at the moment of impact (0.5 = +50%).")]
        public float fallSpeedUpEnd = 0.5f;

        [Header("Launch Limit")]
        [Tooltip("Launches allowed since last standing/crashing (a crash resets the budget). 0 = unlimited.")]
        public int maxLaunchesPerFlight = 2;
        // Level-1 test rule: a crash on a surface that does NOT ground you (a wall, a
        // platform's side, a floating object, a target - anything whose face is too steep
        // to stand on) grants only this many launches until you're genuinely grounded
        // again. Between two floating walls that means exactly one midair launch per hop.
        // 0 = off: every crash restores the full launch budget, as always.
        [Tooltip("Launches granted by a NON-grounding crash (walls/sides/floating objects) until truly grounded again. 0 = off.")]
        public int wallCrashLaunchAllowance = 0;

        [Header("Crash Guards")]
        // A large impulse can make PhysX re-report the launch platform's own continuous
        // contact as a fresh OnCollisionEnter - any contact this soon after firing is
        // necessarily spurious (no real landing is possible this fast at this game's speeds).
        public float launchGraceDuration = 0.15f;
        // Second, independent guard: a shallow shot can genuinely re-touch its own platform
        // after the grace window - also require this much distance from the launch point.
        public float minLaunchClearDistance = 2f;
        [Tooltip("How close a surface normal must be to world-up (dot) to count as walkable flat ground.")]
        [Range(0f, 1f)] public float flatGroundStickThreshold = 0.9f;
        [Tooltip("How steeply downward a launch must aim (dot with down) to count as a slam that bypasses the guards above.")]
        [Range(0f, 1f)] public float slamDownwardThreshold = 0.7f;
        // Backstop for a launch that never separates from the ground at all (slides to a stop
        // under friction) - after this many consecutive grounded physics ticks mid-"flight",
        // register the crash that OnCollisionEnter never got an event for.
        public int stuckOnGroundTickThreshold = 10;
        [Tooltip("How long a NON-sticky wall/ceiling holds a crash before dropping the cube back into gravity.")]
        public float nonStickyWallStickDuration = 0.3f;

        [Header("Fall Reset")]
        public float fallResetY = -30f;

        [Header("Physics")]
        // Applied to the global Physics.gravity on Awake and OnValidate, so it doubles as a
        // live testing knob. Keep in sync with ProjectSettings/DynamicsManager.asset.
        public float gravity = -30f;

        [Header("Input")]
        public InputActionReference moveAction;
        [Tooltip("Left Trigger / Right Mouse - opens the grounded aim (and the midair aim, via airAimAction's shared bindings).")]
        public InputActionReference groundedAimAction;
        [Tooltip("Right Trigger / Left Mouse - fires the grounded aim's launch.")]
        public InputActionReference groundedLaunchAction;
        [Tooltip("South / Space - hold to charge a straight-up launch.")]
        public InputActionReference upLaunchAction;
        [Tooltip("West (E is read from the keyboard directly) - hold midair to charge the ground pound.")]
        public InputActionReference groundPoundAction;
        [Tooltip("Left Bumper - cancels the current charge without firing.")]
        public InputActionReference cancelChargeAction;
        [Tooltip("Right Bumper - shows/hides the trajectory trail.")]
        public InputActionReference trailToggleAction;
        [Tooltip("Right Mouse / Left Trigger - holds the midair first-person aim open.")]
        public InputActionReference airAimAction;
        [Tooltip("Left Mouse / Right Trigger - confirms the midair launch.")]
        public InputActionReference airLaunchAction;

        [Header("Controls Text")]
        // The top-left corner hint is NOT script-written (direct request) - author its text
        // directly on the ControlsHintLabel object in the scene. Only the pause menu's
        // detailed Controls panel body is still filled in at runtime.
        public Text controlsPanelBody;

        // ---------- Runtime state ----------

        Rigidbody rb;
        BoxCollider boxCollider;
        KineticCubeControllerFreeMove freeMoveController;

        // Grounded aim state.
        bool isAiming;
        bool waitingForAimRelease; // one-shot-per-hold: the aim button must be genuinely released after a fire/cancel
        float aimYaw;
        float aimPitch;

        // Hold-to-charge state (straight up, ground pound, and the direction-switched forward).
        enum HoldChargeDirection { None, Up, Down, Forward }
        HoldChargeDirection holdChargeDirection = HoldChargeDirection.None;
        Vector3 holdChargeLastStickDirection;
        bool holdChargeStickWasHeld;
        float holdChargeHeldSeconds; // real seconds this charge has been held - drives the rate ramp
        float aimChargeHeldSeconds;  // ditto for the grounded aim's charge

        // Midair dial ramp state: how long the dial has been moving in one direction, and
        // which direction that is (+1 adding, -1 removing, 0 idle). A flip resets the ramp.
        float dialRampSeconds;
        int dialRampDirection;

        // Midair first-person aim state. Aiming (the camera/reticle) and charging are separate
        // on purpose: the aim can stay open across the energy dial's whole adjustment.
        bool airAiming;
        // The flight velocity captured the instant the midair aim opened (the freeze zeroes
        // it) - restored when the aim is released WITHOUT firing, so the original arc
        // resumes instead of dropping straight down.
        Vector3 preAirAimVelocity;

        // Shared charge amount for whichever charge system is active, in seconds of charging.
        float chargeTime;

        // After a launch or cancel, a still-held aim button counts for nothing until genuinely
        // released once - prevents a held trigger from instantly reopening the aim.
        bool aimButtonSpent;

        // Wall-crash launch limit state: -1 = inactive (the normal maxLaunchesPerFlight
        // budget applies); otherwise the exact launches left until genuinely grounded.
        int launchesRemainingOverride = -1;

        // Flight state.
        bool hasLaunched;
        bool currentFlightIsDownward; // slams are EXPECTED to instantly re-strike their own surface - bypasses the crash guards
        bool exactFlightNoNudge;      // a midair-aimed launch flies the predicted line exactly - the stick must not bend it
        float launchGraceTimer;
        Vector3 launchStartPosition;
        int groundedTicksSinceLaunch;
        int launchesSinceGrounded;
        Vector3 velocityBeforePhysicsStep; // clean pre-collision velocity for the crash refund

        // Crash-stick state.
        bool isStuck;
        Vector3 stuckSurfaceNormal;
        float nonStickyReleaseTimer;
        bool isGrounded;

        // Queued launch, applied on the next physics tick.
        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;
        float queuedDamping;

        // Energy.
        float energyFraction;
        float lastLaunchEnergySpent;
        bool lastLaunchWasGrounded;
        bool lastLaunchWasPound;
        // Running total spent across every launch since the last landing - the pound's wash
        // refund pays back the WHOLE flight, not just the pound itself.
        float flightEnergySpent;

        // Ground-pound bounce state: the post-pound slow-mo window, the boost extra still on
        // offer, the provisional extra paid at aim-open (backed out if the aim closes without
        // firing), and whether an aim opened inside the window is holding gravity off.
        float poundWindowTimer;
        float poundPendingRefund;
        float poundBoostExtra;
        bool poundAimHoldingGravityOff;

        // This flight's time scale - base plus the energy-spend bonus, fixed at fire time.
        float activeFlightTimeScale = 1f;
        // The descent ramp's endpoints: the highest point this flight has reached, and the
        // landing height the aim predicted when it fired.
        float flightApexY;
        float flightPredictedLandingY;

        // Gradual-drain state: how much of the current launch's cost is still undrained,
        // and how fast it drains (cost / predicted flight seconds). A new launch overwrites
        // both - the old remainder is simply never taken.
        float gradualDrainRemaining;
        float gradualDrainPerSecond;
        // Predicted flight duration of the last previewed shot, captured for the drain pace.
        float lastPredictedFlightSeconds;
        // The same duration as estimated REAL seconds - game seconds divided by the flight
        // speed-up the current dial would produce (plus the average descent ramp).
        float lastPredictedFlightRealSeconds;

        // Slowdown resource.
        float aimBudgetRemaining;
        float slowdownSecondsUsed;
        bool slowdownWasAvailable;

        // Landing prediction.
        Vector3[] trajectoryBuffer;
        Vector3 lastPredictedLanding;
        bool hasValidPredictedLanding;
        Vector3 lastPredictedLandingNormal = Vector3.up;
        GameObject predictionClone;
        Rigidbody predictionRb;
        BoxCollider predictionCloneCollider;
        PredictionCloneStopper predictionStopper;
        Scene predictionScene;
        PhysicsScene predictionPhysicsScene;
        bool predictionSceneReady;
        static int predictionSceneCounter;
        int predictionSyncFrame = -1;
        int spawnCacheFrame = -1;
        Vector3 spawnCacheStart;
        Vector3 spawnCacheResult;

        // ---------- Read-only state and events for companion components ----------

        public float EnergyFraction => energyFraction;
        public bool IsStuck => isStuck;
        public bool IsGrounded => isGrounded;
        public bool IsAimingOrCharging => isAiming || airAiming || holdChargeDirection != HoldChargeDirection.None;
        public bool HasLaunched => hasLaunched;
        public int LaunchesSinceGrounded => launchesSinceGrounded;

        // Total real seconds of midair slow-down consumed this scene - read by the run logger.
        public float SlowdownSecondsUsed => slowdownSecondsUsed;
        public float AimBudgetRemaining => aimBudgetRemaining;

        // Read by MovingPlatform's lead arrow: whether the midair aim is open, and the
        // currently previewed shot's predicted flight duration - in game seconds, and
        // converted to estimated REAL seconds (flights run sped-up, platforms run on real
        // time, so the lead must be in the platform's clock).
        public bool IsAirAiming => airAiming;
        public float PredictedFlightSecondsLive => lastPredictedFlightSeconds;
        public float PredictedFlightRealSecondsLive => lastPredictedFlightRealSeconds;

        // Fired the frame a midair aim opens / the slowdown resource runs dry / a launch
        // fires - the Gauntlet's run logger subscribes to these.
        public event System.Action MidairAimOpened;
        public event System.Action SlowdownDepleted;
        public event System.Action LaunchFired;

        // Split in two because the two things the free-move component can do carry very
        // different risk. Directly SETTING velocity (walking) must be blocked for a launch's
        // whole flight - a shallow shot can read "grounded" mid-flight and be silently
        // overwritten. An ADDITIVE nudge only needs to wait out the brief post-launch grace.
        public bool AllowGroundedMovement => !IsAimingOrCharging && !hasLaunched && !isStuck
            // An enemy hit's knockback window - the walk code must not stomp the shove.
            && knockbackTimer <= 0f;
        public bool AllowAirborneNudge => !IsAimingOrCharging && !isStuck && launchGraceTimer <= 0f
            // A midair-aimed launch promises the predicted line exactly - see exactFlightNoNudge.
            && !exactFlightNoNudge;

        // ---------- Lifecycle ----------

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            boxCollider = GetComponent<BoxCollider>();
            freeMoveController = GetComponent<KineticCubeControllerFreeMove>();
            trajectoryBuffer = new Vector3[Mathf.Max(maxPredictionSteps, 1)];
            ApplyGravity();
            energyFraction = infiniteEnergy ? 1f : startingEnergyFraction;
            aimBudgetRemaining = aimBudgetSeconds;
            // Defensive: a scene saved mid-stuck must not start the game with gravity off.
            rb.useGravity = true;
        }

        void OnValidate()
        {
            ApplyGravity();
        }

        void ApplyGravity()
        {
            Physics.gravity = new Vector3(0f, gravity, 0f);
        }

        void Start()
        {
            WriteControlsText();
        }

        void OnEnable()
        {
            EnableAction(moveAction);
            EnableAction(groundedAimAction);
            EnableAction(groundedLaunchAction);
            EnableAction(upLaunchAction);
            EnableAction(groundPoundAction);
            EnableAction(cancelChargeAction);
            EnableAction(trailToggleAction);
            EnableAction(airAimAction);
            EnableAction(airLaunchAction);
        }

        void OnDisable()
        {
            DisableAction(moveAction);
            DisableAction(groundedAimAction);
            DisableAction(groundedLaunchAction);
            DisableAction(upLaunchAction);
            DisableAction(groundPoundAction);
            DisableAction(cancelChargeAction);
            DisableAction(trailToggleAction);
            DisableAction(airAimAction);
            DisableAction(airLaunchAction);
        }

        static void EnableAction(InputActionReference reference) => reference?.action?.Enable();
        static void DisableAction(InputActionReference reference) => reference?.action?.Disable();

        void OnDestroy()
        {
            if (predictionClone != null) Destroy(predictionClone);
            if (predictionSceneReady && predictionScene.IsValid()) SceneManager.UnloadSceneAsync(predictionScene);
        }

        // ---------- Per-frame flow ----------

        void Update()
        {
            // The cursor is locked during play (the midair aim is mouse-driven) and released
            // only while paused, when the menus need a visible, free cursor.
            bool paused = Time.timeScale <= 0f;
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;

            // timeScale 0 freezes deltaTime-scaled logic for free, but not raw edge-detected
            // input - nothing below may run while the pause menu is up.
            if (paused) return;

            if (infiniteEnergy) energyFraction = 1f;

            // Re-arm the spent-aim-button latch only once both aim buttons are genuinely up.
            if (aimButtonSpent && !AimButtonHeld()) aimButtonSpent = false;

            // The post-ground-pound slow-mo window - real seconds, so it isn't stretched by
            // the slow-mo it is itself causing. Lapsing with no aim opened forfeits the
            // boost extra for good (the plain wash refund was already paid at the crash).
            if (poundWindowTimer > 0f)
            {
                poundWindowTimer -= Time.unscaledDeltaTime;
                if (poundWindowTimer <= 0f) poundPendingRefund = 0f;
            }

            if (transform.position.y < fallResetY)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            HandleTrailToggle();
            UpdateSlowdownResource();
            ApplyChargeTimeScale();
            UpdateCameraCoordination();
            UpdateEnergyMeters();

            // The one control scheme: grounded aim / hold-charges / midair first-person aim.
            if (isAiming)
            {
                // Pressing the up-launch button mid-aim converts the aim into the straight-up
                // hold-charge, carrying the accumulated charge over.
                if (UpChargePressedThisFrame())
                {
                    float carriedCharge = chargeTime;
                    isAiming = false;
                    waitingForAimRelease = true;
                    StartHoldCharge(HoldChargeDirection.Up);
                    chargeTime = carriedCharge;
                    UpdateHoldCharge();
                    return;
                }
                UpdateGroundedAim();
            }
            else if (holdChargeDirection != HoldChargeDirection.None)
            {
                UpdateHoldCharge();
            }
            else if (airAiming)
            {
                // The first-person aim exists only midair - the moment the cube is grounded
                // again the ordinary grounded controls take over, even mid-hold.
                if (isGrounded) CancelAirAim();
                else UpdateAirAim();
            }
            else if (isGrounded)
            {
                if (energyFraction > 0f && CanStartNewLaunch() && UpChargePressedThisFrame())
                {
                    StartHoldCharge(HoldChargeDirection.Up);
                }
                else
                {
                    UpdateGroundedAim();
                }
            }
            else
            {
                // Midair, nothing active: West/E starts the ground pound, the aim button opens
                // the first-person aim.
                if (energyFraction > 0f && CanStartNewLaunch() && PoundPressedThisFrame())
                {
                    StartHoldCharge(HoldChargeDirection.Down);
                }
                else
                {
                    UpdateAirAim();
                }
            }
        }

        // ---------- Slowdown resource ----------

        // Whether the slow-down (and the midair freeze that goes with it) is currently paid
        // for. Grounded aiming is always free - the resource only meters MIDAIR deliberation.
        bool SlowdownAvailable()
        {
            switch (slowdownMode)
            {
                case SlowdownMode.AimBudget: return aimBudgetRemaining > 0f;
                case SlowdownMode.EnergyTank: return energyFraction > 0f;
                default: return true;
            }
        }

        // Only the midair first-person AIM counts as metered deliberation. The ground pound
        // and the up-charge are committed actions, not thinking - they always freeze and
        // charge exactly as they always have, in every slowdown mode.
        bool MidairDeliberationActive()
        {
            return !isGrounded && airAiming;
        }

        void UpdateSlowdownResource()
        {
            bool deliberating = MidairDeliberationActive() && SlowdownAvailable();
            if (deliberating)
            {
                float dt = Time.unscaledDeltaTime;
                slowdownSecondsUsed += dt;

                if (slowdownMode == SlowdownMode.AimBudget)
                {
                    aimBudgetRemaining = Mathf.Max(aimBudgetRemaining - dt, 0f);
                }
                else if (slowdownMode == SlowdownMode.EnergyTank && !infiniteEnergy)
                {
                    energyFraction = Mathf.Max(energyFraction - tankDrainPerSecond * dt, 0f);
                }
            }

            bool availableNow = SlowdownAvailable();
            if (slowdownWasAvailable && !availableNow && MidairDeliberationActive())
            {
                SlowdownDepleted?.Invoke();
            }
            slowdownWasAvailable = availableNow;
        }

        // ---------- Time scale ----------

        void ApplyChargeTimeScale()
        {
            // The midair first-person aim slows time only while the slowdown resource can
            // pay for it (the raw aim-button hold is included so there's no gap between the
            // press and the state change). Grounded aiming runs at full speed on purpose.
            // The raw aim-button hold only bridges press-to-open - with no launch available
            // the aim can't open, so the hold must not slow time either.
            bool rawAimHeld = AimButtonHeld() && !aimButtonSpent && CanStartNewLaunch();
            bool airAimSlow = !isGrounded && (airAiming || rawAimHeld) && SlowdownAvailable();
            // The post-ground-pound window holds the slow-mo for its duration - part of the
            // pound itself, so never metered by the slowdown resource.
            bool poundWindowSlow = poundWindowTimer > 0f;
            // Hold-charges (up-launch, ground pound, forward) ALWAYS slow time, grounded or
            // midair, in every slowdown mode - their meters run on unscaled time, so the
            // bullet-time is what makes them read as fast.
            bool holdChargeSlow = holdChargeDirection != HoldChargeDirection.None;

            float flightScale = 1f;
            if (hasLaunched)
            {
                flightScale = activeFlightTimeScale;
                // The descent ramp: track the apex, and while falling scale the speed-up by
                // how far down the descent has come relative to the predicted landing.
                // Deliberately NOT applied to downward (ground pound) launches - those are
                // one continuous dive and feel right at the plain flight speed.
                flightApexY = Mathf.Max(flightApexY, transform.position.y);
                if (rb.linearVelocity.y < 0f && !currentFlightIsDownward)
                {
                    float descentSpan = Mathf.Max(flightApexY - flightPredictedLandingY, 0.01f);
                    float descentProgress = Mathf.Clamp01((flightApexY - transform.position.y) / descentSpan);
                    flightScale *= 1f + Mathf.Lerp(fallSpeedUpStart, fallSpeedUpEnd, descentProgress);
                }
            }
            Time.timeScale = airAimSlow || holdChargeSlow || poundWindowSlow ? chargeTimeScale : flightScale;
        }

        bool AimButtonHeld()
        {
            if (groundedAimAction != null && groundedAimAction.action != null && groundedAimAction.action.IsPressed()) return true;
            if (airAimAction != null && airAimAction.action != null && airAimAction.action.IsPressed()) return true;
            return false;
        }

        // ---------- Camera coordination ----------

        void UpdateCameraCoordination()
        {
            if (cameraOrbit == null) return;

            // While the midair aim is open the LEFT stick steers the camera (the right stick
            // is the energy dial there); while the mouse steers the grounded aim, WASD
            // drives the camera instead. The WASD capture engages ONLY when the keyboard is
            // genuinely the device driving movement - checking "not gamepad-driven" was
            // wrong, because a CENTERED left stick actuates nothing (activeControl is null),
            // which silently swallowed the right stick's camera look on controllers until
            // the left stick moved.
            bool moveIsKeyboardDriven = moveAction != null && moveAction.action != null
                && moveAction.action.activeControl != null && moveAction.action.activeControl.device is Keyboard;
            bool aimWithMoveStick = airAiming
                || (groundedAimWithMouse && isAiming && moveIsKeyboardDriven);

            Vector2 aimStick = aimWithMoveStick && moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            if (aimStick.sqrMagnitude < aimDeadzone * aimDeadzone) aimStick = Vector2.zero;
            if (groundedAimWithMouse && isAiming && moveIsKeyboardDriven) aimStick *= wasdCameraTurnMultiplier;
            cameraOrbit.SetAimStickOverride(aimWithMoveStick, aimStick);

            // While the mouse steers the grounded aim it must not also orbit the camera.
            cameraOrbit.SetMouseLookSuppressed(groundedAimWithMouse && isAiming);

            // The straight-up and ground-pound charges keep the camera at full speed through
            // the bullet-time.
            cameraOrbit.SetIgnoreSlowMo(holdChargeDirection == HoldChargeDirection.Up || holdChargeDirection == HoldChargeDirection.Down);

            // First-person midair aim looks at the cursor at the end of the dotted line.
            bool framingAim = !isGrounded && hasValidPredictedLanding && airAiming;
            cameraOrbit.SetTrajectoryFraming(framingAim, lastPredictedLanding);
        }

        void UpdateEnergyMeters()
        {
            // Each meter disables itself while its corresponding mode is off: the energy
            // meter under infinite energy, the slowdown bar outside AimBudget mode.
            if (energyMeter != null)
            {
                energyMeter.SetVisible(!infiniteEnergy);
                energyMeter.SetEnergy(energyFraction);
                bool charging = isAiming || holdChargeDirection != HoldChargeDirection.None || airAiming;
                energyMeter.SetCharge(ChargeFraction(), charging);
                // While the pound's boost extra is still on offer, preview it in orange
                // poking out past the end of the yellow fill.
                energyMeter.SetBonus(
                    energyFraction + poundPendingRefund * (groundPoundBoostMultiplier - 1f),
                    poundPendingRefund > 0f && poundWindowTimer > 0f);
            }

            if (slowdownMeter != null)
            {
                bool showBudget = slowdownMode == SlowdownMode.AimBudget;
                slowdownMeter.SetVisible(showBudget);
                if (showBudget)
                {
                    slowdownMeter.SetEnergy(aimBudgetSeconds > 0f ? aimBudgetRemaining / aimBudgetSeconds : 0f);
                    slowdownMeter.SetCharge(0f, false);
                }
            }
        }

        // ---------- Grounded aim (hold to aim and charge, fire button launches) ----------

        void UpdateGroundedAim()
        {
            bool aimPressed = groundedAimAction != null && groundedAimAction.action != null && groundedAimAction.action.IsPressed();
            bool cancelPressed = cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame();

            if (isAiming && cancelPressed)
            {
                CloseGroundedAim();
                waitingForAimRelease = true;
                return;
            }

            // One-shot-per-hold: after a fire/cancel the aim button must be genuinely
            // released before it can open a new aim session.
            if (waitingForAimRelease)
            {
                if (!aimPressed) waitingForAimRelease = false;
                return;
            }

            // Energy alone gates STARTING a new aim - never an already-active one, which
            // could otherwise spuriously cancel mid-session.
            bool canStartNewAim = energyFraction > 0f && CanStartNewLaunch();
            bool aimHeld = isAiming ? aimPressed : (aimPressed && canStartNewAim);

            if (aimHeld)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;
                    aimChargeHeldSeconds = 0f;
                    // Stop dead instantly - FixedUpdate keeps re-applying this for the whole
                    // aim, so an airborne aim session doesn't sag under gravity.
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    SeedAimFromCamera();
                    aimArrow?.SetVisible(true);
                    landingPreview?.SetVisible(true);
                    // The cursor at the end of the line shows for grounded aims too.
                    landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);
                    if (!isGrounded) MidairAimOpened?.Invoke();
                }

                // The charge rate ramps up the longer the aim is held - same acceleration
                // principle as the up/down hold-charges.
                aimChargeHeldSeconds += Time.unscaledDeltaTime;
                AccumulateCharge(Time.deltaTime * chargeAccumulationRate * ChargeRateRamp(aimChargeHeldSeconds));

                // Aim adjustment runs on unscaled time - responsiveness must not slow down
                // with the bullet-time.
                float aimDt = Time.unscaledDeltaTime;
                if (groundedAimWithMouse && Mouse.current != null)
                {
                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                    aimYaw = Mathf.Repeat(aimYaw + mouseDelta.x * groundedMouseAimSensitivity, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - mouseDelta.y * groundedMouseAimSensitivity, minAimPitch, maxAimPitch);
                }
                // Under Always Mouse only KEYBOARD movement is repurposed for the camera - a
                // gamepad stick still aims.
                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;
                bool moveIsGamepad = moveAction != null && moveAction.action != null
                    && moveAction.action.activeControl != null && moveAction.action.activeControl.device is Gamepad;
                if ((!groundedAimWithMouse || moveIsGamepad) && stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {
                    aimYaw = Mathf.Repeat(aimYaw + stick.x * aimRotationSpeed * aimDt, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - stick.y * aimRotationSpeed * aimDt, minAimPitch, maxAimPitch);
                }

                Vector3 direction = AimDirection();
                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(direction, chargeFraction);

                float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
                float damping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);
                ShowLandingPreview(direction * force / rb.mass + rb.linearVelocity, damping);

                bool firePressed = groundedLaunchAction != null && groundedLaunchAction.action != null && groundedLaunchAction.action.WasPressedThisFrame();
                if (firePressed)
                {
                    QueueLaunch(direction, force, damping);
                    CloseGroundedAim();
                    waitingForAimRelease = true;
                }
            }
            else if (isAiming)
            {
                // Aim button released without firing - a plain cancel.
                CloseGroundedAim();
            }
        }

        void CloseGroundedAim()
        {
            isAiming = false;
            chargeTime = 0f;
            aimArrow?.SetVisible(false);
            landingPreview?.SetVisible(false);
        }

        void SeedAimFromCamera()
        {
            // Yaw starts wherever the camera currently faces; pitch starts at a fixed,
            // predictable default rather than the camera's arbitrary vertical angle.
            aimYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            aimPitch = Mathf.Clamp(defaultAimPitch, minAimPitch, maxAimPitch);
        }

        Vector3 AimDirection()
        {
            return Quaternion.Euler(aimPitch, aimYaw, 0f) * Vector3.forward;
        }

        // ---------- Hold-to-charge launches (straight up / ground pound / forward) ----------

        bool UpChargePressedThisFrame()
        {
            if (upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame()) return true;
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        bool PoundPressedThisFrame()
        {
            if (groundPoundAction != null && groundPoundAction.action != null && groundPoundAction.action.WasPressedThisFrame()) return true;
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        }

        void StartHoldCharge(HoldChargeDirection direction)
        {
            holdChargeDirection = direction;
            chargeTime = 0f;
            holdChargeHeldSeconds = 0f;
            holdChargeStickWasHeld = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            aimArrow?.SetVisible(true);
            landingPreview?.SetVisible(true);
        }

        void CancelHoldCharge()
        {
            holdChargeDirection = HoldChargeDirection.None;
            chargeTime = 0f;
            aimArrow?.SetVisible(false);
            landingPreview?.SetVisible(false);
        }

        void UpdateHoldCharge()
        {
            if (cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame())
            {
                CancelHoldCharge();
                return;
            }

            bool keyboardAvailable = Keyboard.current != null;

            // Pressing a DIFFERENT direction button mid-charge switches the charge to that
            // direction - the accumulated charge carries over, and firing then happens on the
            // new button's release. Deliberately NO switch INTO the ground pound: a pound
            // must be committed to from the start (a fresh midair West/E press), never
            // smuggled in through a cheaper up/forward charge.
            bool upSwitch = (upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame())
                || (keyboardAvailable && Keyboard.current.spaceKey.wasPressedThisFrame);
            bool forwardSwitch = groundedLaunchAction != null && groundedLaunchAction.action != null && groundedLaunchAction.action.WasPressedThisFrame();
            if (upSwitch && holdChargeDirection != HoldChargeDirection.Up && holdChargeDirection != HoldChargeDirection.Down) holdChargeDirection = HoldChargeDirection.Up;
            else if (forwardSwitch && holdChargeDirection == HoldChargeDirection.Up) holdChargeDirection = HoldChargeDirection.Forward;

            bool releasedNow = holdChargeDirection switch
            {
                HoldChargeDirection.Up => (upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasReleasedThisFrame())
                    || (keyboardAvailable && Keyboard.current.spaceKey.wasReleasedThisFrame),
                HoldChargeDirection.Down => (groundPoundAction != null && groundPoundAction.action != null && groundPoundAction.action.WasReleasedThisFrame())
                    || (keyboardAvailable && Keyboard.current.eKey.wasReleasedThisFrame),
                _ => groundedLaunchAction != null && groundedLaunchAction.action != null && groundedLaunchAction.action.WasReleasedThisFrame(),
            };

            // Every hold-charge ACCELERATES - the rate grows the longer the button is held.
            // Up/down fill in REAL time (the bullet-time must not slow their meter) with the
            // pound's own base+growth ramp; forward uses the shared acceleration curve.
            holdChargeHeldSeconds += Time.unscaledDeltaTime;
            if (holdChargeDirection == HoldChargeDirection.Up || holdChargeDirection == HoldChargeDirection.Down)
            {
                float chargeSpeed = groundPoundChargeBaseSpeed + groundPoundChargeSpeedGrowth * holdChargeHeldSeconds;
                AccumulateCharge(Time.unscaledDeltaTime * chargeAccumulationRate * chargeSpeed);
            }
            else
            {
                AccumulateCharge(Time.deltaTime * chargeAccumulationRate * ChargeRateRamp(holdChargeHeldSeconds));
            }

            // Direction: up and down always fire dead vertical. Forward fires toward the
            // stick when held past the deadzone (frozen to the last held direction when let
            // go), or toward the cube's current facing before any stick input.
            Vector2 stick = moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            bool stickHeld = stick.sqrMagnitude > stickAimDeadzone * stickAimDeadzone;
            if (stickHeld)
            {
                holdChargeLastStickDirection = StickWorldDirection(stick);
                holdChargeStickWasHeld = true;
            }

            Vector3 direction = holdChargeDirection switch
            {
                HoldChargeDirection.Up => Vector3.up,
                HoldChargeDirection.Down => Vector3.down,
                _ => stickHeld
                    ? TiltedDirection(holdChargeLastStickDirection, stickAimForwardAngle)
                    : TiltedDirection(holdChargeStickWasHeld ? holdChargeLastStickDirection : FacingFlatDirection(), stickAimForwardNeutralAngle),
            };

            float chargeFraction = ChargeFraction();
            aimArrow?.SetAim(direction, chargeFraction);

            float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
            float damping = holdChargeDirection == HoldChargeDirection.Down
                ? downLaunchDamping
                : Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);

            // Velocity is held at zero for the whole charge, so the preview starts from rest.
            ShowLandingPreview(direction * force / rb.mass, damping);

            if (releasedNow)
            {
                bool firedPound = holdChargeDirection == HoldChargeDirection.Down && !isGrounded;
                QueueLaunch(direction, force, damping);
                lastLaunchWasPound = firedPound;
                // Only a FORWARD launch swings the camera back behind the player - vertical
                // launches leave the view exactly where it was.
                if (holdChargeDirection == HoldChargeDirection.Forward) RecenterCameraBehindLaunch(direction);
                CancelHoldCharge();
            }
        }

        // ---------- Midair first-person aim (dial the energy, confirm to fire) ----------

        void UpdateAirAim()
        {
            bool aimHeld = airAimAction != null && airAimAction.action != null && airAimAction.action.IsPressed();

            if (!aimHeld)
            {
                if (airAiming)
                {
                    // Released without firing: the suspended flight RESUMES on its original
                    // path - the velocity captured at aim-open is handed back. (Not after a
                    // pound bounce or on the ground, where there is no flight to resume.)
                    bool resumeFlight = hasLaunched && !isGrounded && !poundAimHoldingGravityOff;
                    CancelAirAim();
                    if (resumeFlight)
                    {
                        rb.linearVelocity = preAirAimVelocity;
                    }
                }
                return;
            }

            if (!airAiming)
            {
                // No energy, or no launch available (the wall-crash limit / launch budget
                // spent) - then there is nothing to aim WITH, so aim mode must not open.
                if (energyFraction <= 0f || !CanStartNewLaunch()) return;
                // Opening the aim always needs a FRESH press - a button still held from
                // before a launch/crash does nothing until released and re-pressed.
                if (!(airAimAction != null && airAimAction.action != null && airAimAction.action.WasPressedThisFrame())) return;

                airAiming = true;
                preAirAimVelocity = rb.linearVelocity; // captured before the freeze zeroes it
                chargeTime = 0f; // each fresh aim starts from a clean dial
                dialRampSeconds = 0f;
                dialRampDirection = 0;
                cameraOrbit?.SetFirstPersonMode(true);
                landingPreview?.SetVisible(true);
                landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);
                MidairAimOpened?.Invoke();

                // An aim opened inside the post-ground-pound window claims the boost extra
                // and starts FULLY charged - everything the tank can pay, instantly - with
                // gravity held off for the whole aim.
                if (poundWindowTimer > 0f)
                {
                    PayPoundBoostedRefund();
                    chargeTime = Mathf.Min(maxChargeTime, EnergyChargeCeiling());
                    poundAimHoldingGravityOff = true;
                    rb.useGravity = false;
                }
            }

            // The energy dial: right stick up/down adds/removes charge continuously, mouse
            // wheel steps it per notch. Live for the whole aim - the launch button is purely
            // a confirm. The dial ACCELERATES like every other charge input: the rate grows
            // the longer it keeps moving in one direction, and FLIPPING between adding and
            // removing resets the ramp - so lowering energy also lowers faster over time.
            float dialDelta = 0f;
            float stickY = GamepadLookValue().y;
            if (Mathf.Abs(stickY) > 0.5f)
            {
                dialDelta += Mathf.Sign(stickY) * dialStickRate * maxChargeTime * Time.unscaledDeltaTime;
            }
            if (Mouse.current != null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) dialDelta += Mathf.Sign(scroll) * dialWheelStep * maxChargeTime;
            }
            if (dialDelta != 0f)
            {
                int dialDirection = dialDelta > 0f ? 1 : -1;
                if (dialDirection != dialRampDirection)
                {
                    dialRampSeconds = 0f;
                    dialRampDirection = dialDirection;
                }
                dialRampSeconds += Time.unscaledDeltaTime;
                chargeTime = Mathf.Clamp(chargeTime + dialDelta * ChargeRateRamp(dialRampSeconds),
                    0f, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));
            }

            // What would actually fire: the dialed charge, capped by what the tank can pay.
            Vector3 direction = cameraOrbit != null ? cameraOrbit.AimForward : transform.forward;
            float fireFraction = Mathf.Min(ChargeFraction(), energyCostPerFullCharge > 0f ? SpendableEnergy() / energyCostPerFullCharge : 1f);
            float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, fireFraction);
            float damping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, fireFraction);

            // The camera zooms in with the dialed charge, so a long shot's distant landing
            // spot stays legible.
            cameraOrbit?.SetAimZoom(fireFraction);

            // The launch impulse ADDS to the current motion. While the slowdown resource
            // holds, the cube is frozen (velocity zero) - once it runs dry the cube keeps
            // falling through the aim and the preview accounts for that live velocity.
            ShowLandingPreview(rb.linearVelocity + direction * force / rb.mass, damping);

            // Real-time estimate of that flight for the moving platforms' lead arrows: the
            // flight runs sped up (base + energy bonus, plus the descent ramp on average).
            float estimatedFlightScale = (launchFlightTimeScale + fireFraction * flightTimeScaleEnergyBonus)
                * (1f + (fallSpeedUpStart + fallSpeedUpEnd) * 0.25f);
            lastPredictedFlightRealSeconds = lastPredictedFlightSeconds / Mathf.Max(estimatedFlightScale, 0.01f);

            bool firePressed = airLaunchAction != null && airLaunchAction.action != null && airLaunchAction.action.WasPressedThisFrame();
            if (firePressed && energyFraction > 0f && CanStartNewLaunch())
            {
                chargeTime = fireFraction * maxChargeTime; // pay exactly for what fires
                QueueLaunch(direction, force, damping);
                exactFlightNoNudge = true; // the shot follows the predicted line exactly
                CancelAirAim();
            }
        }

        void CancelAirAim()
        {
            // A post-pound aim that closes WITHOUT firing gives the boost extra back (the
            // plain wash refund underneath stays), releases the gravity hold, and ends the
            // window - no lingering freeze.
            if (poundAimHoldingGravityOff)
            {
                poundAimHoldingGravityOff = false;
                poundWindowTimer = 0f;
                rb.useGravity = true;
                RevertPoundBoost();
            }
            airAiming = false;
            chargeTime = 0f;
            aimButtonSpent = true; // closing the aim spends the hold - release before re-aiming
            landingPreview?.SetVisible(false);
            cameraOrbit?.SetFirstPersonMode(false);
            cameraOrbit?.SetAimZoom(0f);
        }

        // The pound's boost EXTRA (the plain wash was already paid at the crash) lands the
        // moment a midair aim opens inside the window. Remembered as provisional - measured
        // against what was actually banked, so a full tank can't be over-debited on revert.
        void PayPoundBoostedRefund()
        {
            if (poundPendingRefund <= 0f) return;
            float before = energyFraction;
            energyFraction = Mathf.Clamp01(energyFraction + poundPendingRefund * (groundPoundBoostMultiplier - 1f));
            poundBoostExtra = Mathf.Max(0f, energyFraction - before);
            poundPendingRefund = 0f;
        }

        void RevertPoundBoost()
        {
            if (poundBoostExtra <= 0f) return;
            energyFraction = Mathf.Clamp01(energyFraction - poundBoostExtra);
            poundBoostExtra = 0f;
        }

        // The right stick's raw value, gamepad-only - the look action carries mouse deltas
        // too, and those must never leak into the energy dial.
        Vector2 GamepadLookValue()
        {
            InputActionReference look = cameraOrbit != null ? cameraOrbit.lookAction : null;
            if (look == null || look.action == null) return Vector2.zero;
            if (look.action.activeControl == null || !(look.action.activeControl.device is Gamepad)) return Vector2.zero;
            return look.action.ReadValue<Vector2>();
        }

        // ---------- Charge bookkeeping ----------

        float ChargeFraction()
        {
            return maxChargeTime > 0f ? Mathf.Clamp01(chargeTime / maxChargeTime) : 1f;
        }

        // The shared acceleration curve for every charge input - see chargeAcceleration.
        float ChargeRateRamp(float sustainedSeconds)
        {
            return 1f + sustainedSeconds * Mathf.Max(chargeAcceleration, 0f);
        }

        // chargeTime is capped by BOTH the per-shot maximum and however much energy is left,
        // expressed as an equivalent charge-time ceiling - the blue charge bar can never show
        // more than the stored energy, by construction.
        void AccumulateCharge(float delta)
        {
            chargeTime = Mathf.Min(chargeTime + delta, maxChargeTime, EnergyChargeCeiling());
        }

        float EnergyChargeCeiling()
        {
            return energyCostPerFullCharge > 0f ? (SpendableEnergy() / energyCostPerFullCharge) * maxChargeTime : maxChargeTime;
        }

        // GROUNDED launches keep a small reserve so you can never strand yourself standing
        // still; a MIDAIR launch may commit the whole tank as a save-throw.
        float SpendableEnergy()
        {
            return isGrounded ? Mathf.Max(energyFraction - minEnergyReserve, 0f) : energyFraction;
        }

        // On landing only: never end a flight with less than the reserve.
        void ClampEnergyFloor()
        {
            if (energyFraction < minEnergyReserve) energyFraction = minEnergyReserve;
        }

        // Test-level hook (EnergyClampTrigger): force the tank down to at most this fraction.
        public void ClampEnergyTo(float fraction)
        {
            if (infiniteEnergy) return;
            energyFraction = Mathf.Min(energyFraction, Mathf.Clamp01(fraction));
        }

        [Tooltip("Seconds of lost ground control after an enemy hit, so the knockback actually carries (grounded movement overwrites velocity every tick otherwise).")]
        public float enemyHitControlLossSeconds = 0.35f;
        float knockbackTimer;

        // Enemy attack hook: a launching enemy that body-checks the player SHOVES them and
        // drains some energy. The hit interrupts any aim/charge, breaks a crash-stick, and
        // suppresses grounded movement briefly - without that window the walk code would
        // erase the shove on the very next physics tick.
        public void ApplyEnemyHit(Vector3 impulse, float energyLoss)
        {
            if (airAiming) CancelAirAim();
            CancelHoldCharge();
            CloseGroundedAim();
            waitingForAimRelease = true;
            aimButtonSpent = true;

            isStuck = false;
            nonStickyReleaseTimer = 0f;
            rb.useGravity = true;
            rb.linearDamping = plainFallDamping;
            rb.linearVelocity = impulse; // replaced outright - a clean, readable shove
            knockbackTimer = enemyHitControlLossSeconds;

            if (!infiniteEnergy) energyFraction = Mathf.Max(energyFraction - energyLoss, 0f);
        }

        // Hazard hook (DamageWalls): a full instant respawn - every aim, charge, flight and
        // stick state is wound down, the tank returns to its starting level, and the player
        // reappears at the given point standing still under normal gravity.
        public void RespawnAtPoint(Vector3 position)
        {
            if (airAiming) CancelAirAim();
            CancelHoldCharge();
            CloseGroundedAim();
            waitingForAimRelease = true; // held buttons must be re-pressed after a respawn
            aimButtonSpent = true;

            hasLaunched = false;
            launchQueued = false;
            isStuck = false;
            nonStickyReleaseTimer = 0f;
            launchesSinceGrounded = 0;
            launchesRemainingOverride = -1;
            flightEnergySpent = 0f;
            gradualDrainRemaining = 0f;
            poundWindowTimer = 0f;
            poundPendingRefund = 0f;
            poundBoostExtra = 0f;
            poundAimHoldingGravityOff = false;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = true;
            rb.linearDamping = plainFallDamping;
            transform.position = position;

            energyFraction = infiniteEnergy ? 1f : startingEnergyFraction;
            Time.timeScale = 1f;
        }

        bool CanStartNewLaunch()
        {
            // The wall-crash limit, when armed, replaces the normal budget outright.
            if (launchesRemainingOverride == 0) return false;
            if (launchesRemainingOverride > 0) return true;
            return maxLaunchesPerFlight <= 0 || launchesSinceGrounded < maxLaunchesPerFlight;
        }

        // ---------- Firing ----------

        void QueueLaunch(Vector3 direction, float force, float damping)
        {
            // Firing out of a post-pound aim: the shot is taken, so the boost is earned and
            // kept; the gravity hold and the window end with the launch.
            if (poundAimHoldingGravityOff)
            {
                poundAimHoldingGravityOff = false;
                poundWindowTimer = 0f;
                rb.useGravity = true;
                poundBoostExtra = 0f;
            }
            queuedDirection = direction;
            queuedForce = force;
            queuedDamping = damping;
            launchQueued = true;
            hasLaunched = true;
            launchesSinceGrounded++;
            if (launchesRemainingOverride > 0) launchesRemainingOverride--;
            exactFlightNoNudge = false;   // re-armed by the midair fire path right after this call
            aimButtonSpent = true;        // a held aim button does nothing further until released
            lastLaunchWasGrounded = isGrounded;
            lastLaunchWasPound = false;   // re-set by the pound's own fire path
            currentFlightIsDownward = Vector3.Dot(direction.normalized, Vector3.down) >= slamDownwardThreshold;

            // Deduct what was ACTUALLY spendable, not the theoretical charge cost - the
            // refund can then never fabricate energy a nearly-empty tank didn't really spend.
            lastLaunchEnergySpent = Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge);
            if (!infiniteEnergy)
            {
                if (gradualLaunchDrain)
                {
                    // The cost leaves the meter over the flight instead of now. Starting a
                    // new launch overwrites any old drain - the undrained remainder of the
                    // previous launch is still in the meter, funding this one.
                    gradualDrainRemaining = lastLaunchEnergySpent;
                    gradualDrainPerSecond = lastLaunchEnergySpent / lastPredictedFlightSeconds;
                }
                else
                {
                    energyFraction = Mathf.Clamp01(energyFraction - lastLaunchEnergySpent);
                }
            }
            flightEnergySpent += lastLaunchEnergySpent; // running total for the pound's whole-flight wash

            // The flight speed-up grows with commitment: +1% game speed per 1% of the tank
            // this launch spent (see flightTimeScaleEnergyBonus).
            activeFlightTimeScale = launchFlightTimeScale + lastLaunchEnergySpent * flightTimeScaleEnergyBonus;

            // Arm the descent ramp: apex starts here, and the landing height is whatever the
            // aim just predicted (a shot into the void ramps toward the fall-reset instead).
            flightApexY = transform.position.y;
            flightPredictedLandingY = hasValidPredictedLanding ? lastPredictedLanding.y : fallResetY;

            // Armed here already (not just when FixedUpdate applies the impulse) so
            // AllowGroundedMovement/AllowAirborneNudge are correct the instant firing is
            // decided - the free-move component's FixedUpdate can run before ours.
            launchGraceTimer = launchGraceDuration;

            LaunchFired?.Invoke();
        }

        void RecenterCameraBehindLaunch(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            float launchYaw = flat.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg
                : (freeMoveController != null ? freeMoveController.FacingYaw : 0f);
            cameraOrbit?.RecenterBehindTarget(launchYaw);
        }

        // ---------- Physics step ----------

        void FixedUpdate()
        {
            // Freeze the cube for the whole duration of any aim/charge (and while crash-
            // stuck) - continuously, not just on the opening frame, so gravity can't sag an
            // airborne aim downward tick by tick. Only the midair first-person AIM's freeze
            // is conditional on the slowdown resource; hold-charges (up-launch, ground
            // pound) and the grounded aim always freeze, exactly as they always have.
            bool airAimFrozen = airAiming && (isGrounded || SlowdownAvailable());
            // The post-ground-pound window also freezes the cube in place - the free hop
            // just hangs there until the window lapses or an aim opens.
            bool frozenThisTick = isAiming || holdChargeDirection != HoldChargeDirection.None || airAimFrozen || isStuck
                || poundWindowTimer > 0f;
            if (frozenThisTick)
            {
                // On a moving platform, "frozen" means frozen RELATIVE TO THE PLATFORM -
                // the ride continues through a grounded aim instead of the platform
                // sliding out from under it.
                rb.linearVelocity = isGrounded && freeMoveController != null
                    ? freeMoveController.GroundPlatformVelocity
                    : Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Gradual drain: the launch's cost trickles out of the meter as the flight
            // progresses (paused while frozen mid-aim - the flight isn't progressing then).
            if (gradualLaunchDrain && !infiniteEnergy && hasLaunched && !frozenThisTick && gradualDrainRemaining > 0f)
            {
                float drainStep = Mathf.Min(gradualDrainPerSecond * Time.fixedDeltaTime, gradualDrainRemaining);
                gradualDrainRemaining -= drainStep;
                energyFraction = Mathf.Max(energyFraction - drainStep, 0f);
            }

            bool slamJustFired = false;
            float slamForce = 0f;

            if (launchQueued)
            {
                launchQueued = false;
                isStuck = false;             // breaking free of a crashed/stuck position
                nonStickyReleaseTimer = 0f;  // launching supersedes a pending timed release
                rb.useGravity = true;        // back on, undoing the crash-stick
                rb.linearDamping = queuedDamping;
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);
                launchGraceTimer = launchGraceDuration;
                launchStartPosition = transform.position;
                freeMoveController?.FaceLaunchDirection(queuedDirection);

                slamJustFired = currentFlightIsDownward;
                slamForce = queuedForce;
            }

            if (launchGraceTimer > 0f) launchGraceTimer -= Time.fixedDeltaTime;
            if (knockbackTimer > 0f) knockbackTimer -= Time.fixedDeltaTime;

            // Grounded state from a fresh BoxCast across the cube's own footprint each step -
            // continuous collision detection can report contact slightly after a real
            // departure, and a single center ray misses edge landings.
            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out RaycastHit groundHit, transform.rotation, groundCheckDistance);

            // Standing on the ground with no flight in progress restores the per-flight
            // launch budget and the energy floor.
            if (isGrounded && !hasLaunched)
            {
                launchesSinceGrounded = 0;
                launchesRemainingOverride = -1; // genuinely grounded - the wall-crash limit lifts
                exactFlightNoNudge = false;
                flightEnergySpent = 0f; // the flight (and its refund basis) ends on the ground
                if (!infiniteEnergy) ClampEnergyFloor();
            }

            // Plain falls (no launch in flight) shed the last launch's arc-shaping drag -
            // see plainFallDamping.
            if (!hasLaunched && !isGrounded && !isStuck && rb.linearDamping > plainFallDamping)
            {
                rb.linearDamping = plainFallDamping;
            }

            // A slam fired from ZERO clearance never actually leaves its surface - PhysX
            // absorbs the downward impulse into the already-supporting contact and no
            // OnCollisionEnter ever fires. Handle that crash directly. Standing on a
            // breakable pane, the same slam smashes it instead so gravity carries the cube
            // through the fresh hole.
            if (slamJustFired && isGrounded)
            {
                BreakableCrackWall groundBreakable = groundHit.collider != null ? groundHit.collider.GetComponentInParent<BreakableCrackWall>() : null;
                if (groundBreakable != null)
                {
                    groundBreakable.Smash();
                }
                else
                {
                    RegisterCrash(groundHit.normal, slamForce, groundHit.collider);
                }
            }

            // Backstop for any other launch that never separates from the ground (slides to a
            // stop under friction) - the crash OnCollisionEnter never got an event for.
            if (hasLaunched && isGrounded)
            {
                groundedTicksSinceLaunch++;
                if (groundedTicksSinceLaunch >= stuckOnGroundTickThreshold && !isStuck)
                {
                    RegisterCrash(groundHit.normal, rb.linearVelocity.magnitude, groundHit.collider);
                }
            }
            else
            {
                groundedTicksSinceLaunch = 0;
            }

            // A crash-stuck cube resting on genuinely FLAT ground breaks free automatically -
            // walking away must never require energy. Only near-horizontal surfaces qualify;
            // walls and ramps hold until the next launch.
            if (isStuck && isGrounded && Vector3.Dot(stuckSurfaceNormal, Vector3.up) >= flatGroundStickThreshold)
            {
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
            }

            // Timed release from a non-sticky wall: the cling holds like a normal stick for
            // its brief duration, then lets go and gravity takes over with low drag.
            if (isStuck && nonStickyReleaseTimer > 0f)
            {
                nonStickyReleaseTimer -= Time.fixedDeltaTime;
                if (nonStickyReleaseTimer <= 0f)
                {
                    isStuck = false;
                    rb.useGravity = true;
                    rb.linearDamping = downLaunchDamping;
                }
            }

            // Captured LAST, after a same-tick launch impulse, and strictly before the
            // physics step resolves the upcoming collision - OnCollisionEnter reading
            // rb.linearVelocity directly gets inconsistently post-collision values.
            velocityBeforePhysicsStep = rb.linearVelocity;
        }

        // ---------- Collisions ----------

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<RestartWall>() != null)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            // RestartWall reloads the level on any touch - checked before every guard so a
            // grounded walk-in restarts as reliably as a mid-flight crash.
            if (collision.collider.GetComponentInParent<RestartWall>() != null)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            // Breakable crack panes are solid to everything EXCEPT a downward launch - a slam
            // smashes through, restoring the pre-impact velocity PhysX already absorbed.
            BreakableCrackWall breakable = collision.collider.GetComponentInParent<BreakableCrackWall>();
            if (breakable != null && hasLaunched && currentFlightIsDownward)
            {
                breakable.Smash();
                rb.linearVelocity = velocityBeforePhysicsStep;
                return;
            }

            // Enemies: a launch KILLS the enemy and registers a full crash (refund and the
            // wall-crash launch limit apply, the flight ends) - but with NO cling window
            // for now: the enemy is gone, so you drop into a normal fall immediately.
            // Relaunching happens midair, within whatever launches the crash granted.
            // Walking into one is harmless; it just shoves you.
            Enemy enemy = collision.collider.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                if (hasLaunched && !isStuck)
                {
                    RegisterCrash(collision.GetContact(0).normal, velocityBeforePhysicsStep.magnitude, collision.collider);
                    isStuck = false;
                    nonStickyReleaseTimer = 0f;
                    rb.useGravity = true;
                    rb.linearDamping = plainFallDamping;
                    enemy.OnHitByLaunch();
                }
                return;
            }

            // Target spheres always register, BEFORE every guard below - a floating target
            // can never be the launch platform spuriously re-reporting contact, so the
            // grace/clear-distance guards must not swallow the hit (they did: spheres close
            // to the launch point were phased through). A launch onto one is a full crash;
            // touching one with no flight in progress still collects it, without the stick.
            TargetSphere touchedSphere = collision.collider.GetComponentInParent<TargetSphere>();
            if (touchedSphere != null)
            {
                if (hasLaunched && !isStuck)
                {
                    RegisterCrash(collision.GetContact(0).normal, velocityBeforePhysicsStep.magnitude, collision.collider);
                }
                else
                {
                    touchedSphere.OnHitByCrash();
                }
                return;
            }

            // Only a genuine in-flight crash counts - not pre-launch walking, not an
            // already-stuck body.
            if (!hasLaunched || isStuck) return;

            // Slams bypass the spurious-recontact guards - immediately re-striking the launch
            // surface is their whole point. Every other direction keeps both guards.
            if (!currentFlightIsDownward)
            {
                if (launchGraceTimer > 0f) return;
                if (Vector3.Distance(transform.position, launchStartPosition) < minLaunchClearDistance) return;
            }

            RegisterCrash(collision.GetContact(0).normal, velocityBeforePhysicsStep.magnitude, collision.collider);
        }

        // Crash-stick: stop dead, freeze in place, gravity off, refund energy. Shared by
        // OnCollisionEnter and FixedUpdate's zero-clearance slam check.
        void RegisterCrash(Vector3 contactNormal, float crashSpeed, Collider surface)
        {
            // A NonStickSurface never registers as a crash at all - no freeze, no refund;
            // physics carries the cube onward.
            if (surface != null && surface.GetComponentInParent<NonStickSurface>() != null) return;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;

            isStuck = true;
            hasLaunched = false;
            launchesSinceGrounded = 0; // a crash is a landing - the launch budget resets
            // ...EXCEPT under the wall-crash rule: a surface too steep to stand on grants
            // only the small allowance until the player is genuinely grounded again.
            bool groundingCrash = Vector3.Dot(contactNormal, Vector3.up) >= flatGroundStickThreshold;
            launchesRemainingOverride = wallCrashLaunchAllowance > 0 && !groundingCrash
                ? wallCrashLaunchAllowance
                : -1;
            exactFlightNoNudge = false;

            // A crash closes any aim outright and demands a genuine release-and-repress.
            waitingForAimRelease = true;
            if (airAiming) CancelAirAim();

            stuckSurfaceNormal = contactNormal;
            freeMoveController?.AlignVisualToSurface(stuckSurfaceNormal);

            // Sticky grammar: near-flat ground is always walkable. A wall/ceiling holds
            // permanently only when it carries StickySurface (with sticky on) - anything else
            // clings briefly, then drops. A TimedStickyPanel holds for its own duration.
            nonStickyReleaseTimer = 0f;
            TimedStickyPanel timedPanel = surface != null ? surface.GetComponentInParent<TimedStickyPanel>() : null;
            if (timedPanel != null)
            {
                // Holds like a sticky surface, but only for the panel's own duration - the
                // panel drops its collider at the same moment this timer releases the stick,
                // so the player falls even off a flat panel top.
                nonStickyReleaseTimer = timedPanel.holdSeconds;
                timedPanel.OnPlayerStuck();
            }
            else if (Vector3.Dot(contactNormal, Vector3.up) < flatGroundStickThreshold)
            {
                StickySurface stickySurface = surface != null ? surface.GetComponentInParent<StickySurface>() : null;
                if (stickySurface == null || !stickySurface.sticky)
                {
                    nonStickyReleaseTimer = nonStickyWallStickDuration;
                }
            }

            // Gradual drain: landing means the launch is 100% spent - whatever hadn't
            // trickled out yet is taken now, BEFORE the refund is paid.
            if (gradualLaunchDrain && !infiniteEnergy && gradualDrainRemaining > 0f)
            {
                energyFraction = Mathf.Max(energyFraction - gradualDrainRemaining, 0f);
                gradualDrainRemaining = 0f;
            }

            // Breakable crack panes never refund energy - they exist to be smashed through,
            // not farmed.
            if (surface == null || surface.GetComponentInParent<BreakableCrackWall>() == null)
            {
                RefundEnergyForCrash();
            }

            // The ground pound doesn't stick - it BOUNCES: a free hop (no energy cost),
            // gravity back on, and the slow-mo window during which an aim starts fully
            // charged. Consumed here so the hop's own landing is not treated as a pound.
            if (lastLaunchWasPound)
            {
                transform.position += Vector3.up * groundPoundHopHeight;
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
                poundWindowTimer = groundPoundSlowDuration;
                lastLaunchWasPound = false;
                lastLaunchEnergySpent = 0f;
            }

            flightEnergySpent = 0f; // consumed by the refund above - the next flight starts fresh

            // Solid target spheres: the crash counts exactly like any other surface (energy
            // included, handled above), then the sphere vanishes - so the cling release is
            // armed UNCONDITIONALLY (there is no surface left to rest against; hanging there
            // forever would be a soft-lock) and the hit is reported for the counter/respawn.
            TargetSphere sphere = surface != null ? surface.GetComponentInParent<TargetSphere>() : null;
            if (sphere != null)
            {
                nonStickyReleaseTimer = nonStickyWallStickDuration;
                sphere.OnHitByCrash();
            }

            // Variant A: the aim budget refills on every crash.
            if (slowdownMode == SlowdownMode.AimBudget) aimBudgetRemaining = aimBudgetSeconds;
        }

        // The refund rules: EnergyEconomy1's per-launch economy for ordinary crashes, and
        // the EnergyEconomy4 pound rule - the WHOLE flight's spend comes back as a wash
        // immediately, with the boost extra deferred to the slow-mo window (see the Ground
        // Pound header fields).
        void RefundEnergyForCrash()
        {
            if (infiniteEnergy)
            {
                energyFraction = 1f;
                return;
            }

            if (lastLaunchWasPound)
            {
                float flightSpend = flightEnergySpent > 0.0001f ? flightEnergySpent : lastLaunchEnergySpent;
                energyFraction = Mathf.Clamp01(energyFraction + flightSpend * poundFlightRefundMultiplier);
                // The boost extra keys off the POUND launch alone, not the whole flight.
                poundPendingRefund = lastLaunchEnergySpent;
                ClampEnergyFloor();
                return;
            }

            float gain;
            if (lastLaunchWasGrounded)
            {
                gain = lastLaunchEnergySpent * groundedRefundMultiplier;
            }
            else
            {
                // spend * (base + factor * spend): the multiplier rises with how much was committed.
                gain = lastLaunchEnergySpent * (midairRefundBaseMultiplier + midairRefundSpendFactor * lastLaunchEnergySpent);
            }
            energyFraction = Mathf.Clamp01(energyFraction + gain);
            ClampEnergyFloor();
        }

        // ---------- Trail toggle ----------

        void HandleTrailToggle()
        {
            if (trailToggleAction == null || trailToggleAction.action == null || !trailToggleAction.action.WasPressedThisFrame()) return;
            if (landingPreview == null) return;
            landingPreview.SetMode(landingPreview.CurrentMode == PredictionMode.None ? PredictionMode.TrailAndCrosshair : PredictionMode.None);
        }

        // ---------- Direction helpers ----------

        // Tilts a flat (Y=0) direction angleDeg above horizontal into a unit launch direction.
        static Vector3 TiltedDirection(Vector3 flatDirection, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 flat = flatDirection.sqrMagnitude > 0.0001f ? flatDirection.normalized : Vector3.forward;
            return flat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
        }

        Vector3 StickWorldDirection(Vector2 stick)
        {
            Vector3 direction = CameraForwardFlat() * stick.y + CameraRightFlat() * stick.x;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        // The cube's own current facing, not the camera's - "launch forward" means the way
        // the cube is pointing.
        Vector3 FacingFlatDirection()
        {
            float yaw = freeMoveController != null ? freeMoveController.FacingYaw : 0f;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        Vector3 CameraForwardFlat()
        {
            if (cameraTransform == null) return Vector3.forward;
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        Vector3 CameraRightFlat()
        {
            if (cameraTransform == null) return Vector3.right;
            Vector3 right = cameraTransform.right;
            right.y = 0f;
            return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        }

        // ---------- Landing prediction ----------

        void ShowLandingPreview(Vector3 initialVelocity, float damping)
        {
            Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
            Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, damping, out int stepCount, out bool didLand);
            lastPredictedLanding = landingPoint;
            hasValidPredictedLanding = didLand;
            lastPredictedFlightSeconds = Mathf.Max(stepCount * Time.fixedDeltaTime, 0.1f);

            if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
            {
                landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand, lastPredictedLandingNormal);
            }
        }

        // Runs the ACTUAL Unity physics engine on a hidden stand-in Rigidbody inside an
        // isolated PhysicsScene, fast-forwarded via manual Simulate() calls - accurate by
        // construction, since it's the same code path that will move the real cube. Several
        // formula-based approximations were tried and never quite matched.
        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, float damping, out int stepCount, out bool didLand)
        {
            EnsurePredictionClone();
            // Mirror the live scene's geometry once per frame - every prediction within one
            // frame sees identical geometry anyway.
            if (predictionSyncFrame != Time.frameCount)
            {
                SyncPredictionGeometry();
                predictionSyncFrame = Time.frameCount;
            }

            predictionRb.linearDamping = damping;

            // Spawn slightly off the resting surface, along its normal - teleporting the
            // clone exactly onto (or into) the surface registers an instant false "landed".
            // The offset direction matters: while stuck to a wall or ceiling, world-up points
            // along or INTO the surface; the stuck normal is correct in every orientation.
            bool spawnCached = spawnCacheFrame == Time.frameCount && spawnCacheStart == startPos;
            Vector3 clearanceDir = isStuck && stuckSurfaceNormal.sqrMagnitude > 0.0001f ? stuckSurfaceNormal : Vector3.up;
            Vector3 spawnPos = spawnCached ? spawnCacheResult : startPos + clearanceDir * 0.15f;

            // Depenetrate the spawn from static geometry (aiming while pressed against a wall
            // would otherwise start the clone overlapping it and collapse the trail). The
            // clone is inflated by a small skin for the pass so "merely touching" also counts.
            if (!spawnCached && predictionCloneCollider != null)
            {
                const float depenetrationSkin = 0.12f;
                Vector3 originalCloneSize = predictionCloneCollider.size;
                predictionCloneCollider.size = originalCloneSize + Vector3.one * depenetrationSkin;
                foreach (PredictionGeometryProxy entry in geometryProxies)
                {
                    if (entry.proxy == null || !entry.proxy.activeSelf) continue;
                    Collider proxyCollider = entry.proxyBox != null ? (Collider)entry.proxyBox : entry.proxyMesh;
                    if (proxyCollider == null) continue;
                    if (proxyCollider.bounds.SqrDistance(spawnPos) > 2.25f) continue;
                    if (Physics.ComputePenetration(predictionCloneCollider, spawnPos, transform.rotation,
                        proxyCollider, entry.proxy.transform.position, entry.proxy.transform.rotation,
                        out Vector3 pushDirection, out float pushDistance))
                    {
                        spawnPos += pushDirection * (pushDistance + 0.02f);
                    }
                }
                predictionCloneCollider.size = originalCloneSize;
            }

            if (!spawnCached)
            {
                spawnCacheFrame = Time.frameCount;
                spawnCacheStart = startPos;
                spawnCacheResult = spawnPos;
            }

            predictionStopper?.ClearContact();
            predictionRb.position = spawnPos;
            predictionRb.rotation = transform.rotation;
            predictionRb.linearVelocity = initialVelocity;
            predictionRb.angularVelocity = Vector3.zero;
            predictionRb.Sleep();
            predictionRb.WakeUp();
            predictionRb.linearVelocity = initialVelocity;
            predictionRb.angularVelocity = Vector3.zero;

            float dt = Time.fixedDeltaTime;
            Vector3 landing = startPos;
            stepCount = 0;
            didLand = false;

            for (int i = 0; i < maxPredictionSteps; i++)
            {
                predictionPhysicsScene.Simulate(dt);

                Vector3 pos = predictionClone.transform.position;
                landing = pos;
                if (stepCount < trajectoryBuffer.Length) trajectoryBuffer[stepCount++] = pos;

                // Only trusted after a couple of real steps - reading velocity before the
                // first step has genuinely applied could misreport "already at rest".
                if (i >= 2 && predictionRb.linearVelocity.sqrMagnitude < 0.0001f)
                {
                    didLand = true;
                    break;
                }

                // A shot into a bottomless gap never comes to rest - bail once it's fallen
                // past the fall-reset threshold instead of burning the whole step budget.
                if (pos.y < fallResetY) break;
            }

            lastPredictedLandingNormal = predictionStopper != null && predictionStopper.HasContact
                ? predictionStopper.LastContactNormal
                : Vector3.up;

            return landing;
        }

        void EnsurePredictionClone()
        {
            if (predictionClone != null) return;

            if (!predictionSceneReady)
            {
                // A genuinely separate PhysicsScene - manual Simulate() calls on it cannot
                // possibly touch the real player or camera, no matter how long a prediction runs.
                predictionScene = SceneManager.CreateScene(
                    "KineticEnergyPredictionPhysics_" + (predictionSceneCounter++),
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                predictionPhysicsScene = predictionScene.GetPhysicsScene();
                BuildPredictionGeometryProxies();
                predictionSceneReady = true;
            }

            predictionClone = new GameObject("PredictionClone (hidden)");
            SceneManager.MoveGameObjectToScene(predictionClone, predictionScene);

            predictionRb = predictionClone.AddComponent<Rigidbody>();
            predictionRb.mass = rb.mass;
            predictionRb.linearDamping = rb.linearDamping;
            predictionRb.angularDamping = rb.angularDamping;
            predictionRb.constraints = RigidbodyConstraints.FreezeRotation;
            predictionRb.interpolation = RigidbodyInterpolation.None;
            predictionRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            predictionCloneCollider = predictionClone.AddComponent<BoxCollider>();
            if (boxCollider != null) predictionCloneCollider.size = boxCollider.size;

            // Stops dead on first contact, mirroring the real cube's crash-stick.
            predictionStopper = predictionClone.AddComponent<PredictionCloneStopper>();
        }

        // Colliders can't be shared across PhysicsScenes - build static-geometry stand-ins in
        // the isolated scene, paired with their sources so SyncPredictionGeometry can mirror
        // moves/resizes/active-state flips every prediction frame. Inactive objects are
        // included on purpose, ready for anything that gets enabled later.
        void BuildPredictionGeometryProxies()
        {
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include);

            foreach (Collider col in colliders)
            {
                if (col == boxCollider) continue;
                // Dynamic bodies are excluded, but KINEMATIC ones (moving platforms) are
                // genuine landable geometry - their proxies mirror position every frame.
                Rigidbody colBody = col.attachedRigidbody;
                if (colBody != null && !colBody.isKinematic) continue;
                // Marked colliders (the boundary cage) are invisible to the aim - the trail
                // passes through and the reticle never focuses them.
                if (col.GetComponentInParent<AimPreviewIgnored>() != null) continue;
                // Trigger volumes (finish lines, beat regions) aren't solid ground. Target
                // spheres are SOLID colliders, so they're included as ordinary geometry -
                // the trail terminates on them and the reticle/camera focus them.
                if (col.isTrigger) continue;

                GameObject proxy = new GameObject("PredictionGeometryProxy");
                SceneManager.MoveGameObjectToScene(proxy, predictionScene);

                PredictionGeometryProxy entry = new PredictionGeometryProxy { source = col, proxy = proxy };
                if (col is BoxCollider)
                {
                    entry.proxyBox = proxy.AddComponent<BoxCollider>();
                }
                else if (col is SphereCollider)
                {
                    entry.proxySphere = proxy.AddComponent<SphereCollider>();
                }
                else if (col is MeshCollider meshCol)
                {
                    entry.proxyMesh = proxy.AddComponent<MeshCollider>();
                    entry.proxyMesh.convex = meshCol.convex;
                }
                else
                {
                    Debug.LogWarning($"KineticCubeController: unhandled collider type {col.GetType().Name} on {col.name} - not included in landing prediction geometry.");
                    Destroy(proxy);
                    continue;
                }

                geometryProxies.Add(entry);
                MirrorGeometryProxy(entry);
            }
        }

        class PredictionGeometryProxy
        {
            public Collider source;
            public GameObject proxy;
            public BoxCollider proxyBox;
            public SphereCollider proxySphere;
            public MeshCollider proxyMesh;
        }
        readonly List<PredictionGeometryProxy> geometryProxies = new List<PredictionGeometryProxy>();

        void SyncPredictionGeometry()
        {
            for (int i = geometryProxies.Count - 1; i >= 0; i--)
            {
                PredictionGeometryProxy entry = geometryProxies[i];
                if (entry.source == null)
                {
                    if (entry.proxy != null) Destroy(entry.proxy);
                    geometryProxies.RemoveAt(i);
                    continue;
                }
                MirrorGeometryProxy(entry);
            }
        }

        void MirrorGeometryProxy(PredictionGeometryProxy entry)
        {
            Transform sourceTransform = entry.source.transform;
            entry.proxy.transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            entry.proxy.transform.localScale = sourceTransform.lossyScale;

            if (entry.proxyBox != null && entry.source is BoxCollider sourceBox)
            {
                if (entry.proxyBox.center != sourceBox.center) entry.proxyBox.center = sourceBox.center;
                if (entry.proxyBox.size != sourceBox.size) entry.proxyBox.size = sourceBox.size;
            }
            else if (entry.proxySphere != null && entry.source is SphereCollider sourceSphere)
            {
                if (entry.proxySphere.center != sourceSphere.center) entry.proxySphere.center = sourceSphere.center;
                if (entry.proxySphere.radius != sourceSphere.radius) entry.proxySphere.radius = sourceSphere.radius;
            }
            else if (entry.proxyMesh != null && entry.source is MeshCollider sourceMesh)
            {
                if (entry.proxyMesh.sharedMesh != sourceMesh.sharedMesh) entry.proxyMesh.sharedMesh = sourceMesh.sharedMesh;
            }

            bool sourceSolid = entry.source.enabled && entry.source.gameObject.activeInHierarchy;
            if (entry.proxy.activeSelf != sourceSolid) entry.proxy.SetActive(sourceSolid);
        }

        // ---------- Controls text ----------

        void WriteControlsText()
        {
            const string crashLine =
                "Crashing refunds energy - green STICKY surfaces hold you until you launch,\n" +
                "anything else drops you after a moment (flat ground you can walk off freely)\n";

            if (controlsPanelBody != null)
            {
                controlsPanelBody.text =
                    "Left Stick / WASD - Move (on the ground); nudge the flight (in the air)\n" +
                    "Grounded - Left Trigger / Right Mouse (hold) aims and charges over time.\n" +
                    "  The MOUSE steers the aim (WASD drives the camera meanwhile); on\n" +
                    "  controller the Left Stick steers it. Right Trigger / Left Mouse fires.\n" +
                    "  South / Space (hold) charges a straight-UP launch - release to fire.\n" +
                    "Airborne - Right Mouse / Left Trigger (hold) opens a first-person aim\n" +
                    "  with the dotted trail and reticle. Add or remove launch energy with\n" +
                    "  the Mouse Wheel or Right Stick up/down - the blue bar previews the\n" +
                    "  cost. Left Mouse / Right Trigger fires along where you're looking.\n" +
                    "  West / E (hold) charges the GROUND POUND - release to slam straight\n" +
                    "  down. Landing a pound BOUNCES you into a brief slow-mo window: open\n" +
                    "  an aim inside it to claim bonus energy (the orange bar) and start\n" +
                    "  that shot instantly at full charge.\n" +
                    "Left Bumper - Cancel the current charge without firing.\n" +
                    crashLine +
                    "Right Bumper - Show/hide the trajectory trail\n" +
                    "Mouse / Right Stick - Camera\n" +
                    "Start / Options / Esc - Pause";
            }
        }
    }
}
