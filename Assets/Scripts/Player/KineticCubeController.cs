using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Level;
using System.Collections.Generic;

namespace KineticEnergy.Player
{

    public enum SlowdownMode
    {
        Unlimited,
        AimBudget,
        EnergyTank,
    }

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

        [Tooltip("Rigidbody linear damping applied to a zero-charge launch.")]
        public float minLaunchDamping = 2.8f;
        [Tooltip("Rigidbody linear damping applied to a full-charge launch.")]
        public float maxLaunchDamping = 1.0f;

        [Tooltip("Fixed low damping used by downward (ground pound) launches instead of the charge curve.")]
        public float downLaunchDamping = 0.2f;

        [Tooltip("Damping applied while airborne with no launch in flight, so plain falls accelerate naturally.")]
        public float plainFallDamping = 0.2f;

        [Header("Control Scheme Variants (QuarryAim lab - all default OFF)")]

        [Tooltip("Grounded aim: the camera slowly pans horizontally after the aim swings past the follow threshold to either side.")]
        public bool groundedAimCameraFollow = false;
        [Tooltip("Degrees of horizontal aim-vs-camera deviation before the follow starts.")]
        public float groundedAimFollowThreshold = 60f;
        [Tooltip("The follow band past the threshold: the aim is HARD-CLAMPED at threshold+band, and the pan speed ramps from zero (at the threshold) to full (at the clamp).")]
        public float groundedAimFollowBand = 5f;
        [Tooltip("Full pan speed, reached when the aim sits at the clamp edge.")]
        public float groundedAimFollowSpeed = 45f;
        [Tooltip("Grounded aim: the launch strength is DIALLED (wheel / bumpers) exactly like the midair aim, instead of charging over held time.")]
        public bool groundedDialControls = false;
        [Tooltip("Controller energy dial on the bumpers - RB adds, LB removes - replacing the right-stick dial (grounded and midair alike). LB stops acting as charge-cancel while this is on.")]
        public bool bumperEnergyDial = false;

        [Header("Overcharge Scatter (economy test - 0 = off)")]

        [Tooltip("Cone radius (degrees) at FULL charge. 0 disables scatter entirely.")]
        public float launchScatterMaxAngle = 0f;
        [Tooltip("Charge fraction where the cone starts growing - below this, launches stay exact.")]
        [Range(0f, 1f)] public float launchScatterStartFraction = 0.25f;

        [Header("Zero-Damping Test Mode")]

        [Tooltip("TEST MODE: fire all launches with zero damping, using the matched min/max forces computed at startup. Leave OFF outside the dedicated test scene.")]
        public bool zeroDampingMatchedLaunches = false;
        [Tooltip("Computed at startup (when the test mode is on): the zero-damping launch force whose 45-degree flat-ground distance matches a zero-charge damped launch. Charge lerps between this and the max, exactly like the damped pair.")]
        public float zeroDampingMinLaunchForce;
        [Tooltip("Computed at startup (when the test mode is on): the zero-damping launch force whose 45-degree flat-ground distance matches a full-charge damped launch.")]
        public float zeroDampingMaxLaunchForce;

        [Header("Energy")]
        [Tooltip("Fraction of the tank the player starts the level with.")]
        [Range(0f, 1f)] public float startingEnergyFraction = 0.2f;

        [Tooltip("Fraction of the whole tank a FULL charge costs. Keep at 1 - see the code comment.")]
        public float energyCostPerFullCharge = 1f;
        [Tooltip("GROUNDED launches can never spend the tank below this reserve. Midair launches may commit everything.")]
        [Range(0f, 1f)] public float minEnergyReserve = 0.05f;
        [Tooltip("Ordinary landing refunds never fill the tank past this fraction (1 = rule off). The pound-boost pipeline and direct AddEnergy payments ignore it - the merged economy scene sets 0.8 so the top of the tank stays premium.")]
        [Range(0f, 1f)] public float ordinaryRefundCeiling = 1f;
        [Tooltip("Every charge reads as the MAXIMUM the tank can pay - no manual energy regulation (the merged economy scene's auto-max variants set this).")]
        public bool alwaysMaxCharge = false;
        [Tooltip("Midair launches ADD the velocity the cube carried into the aim (captured at aim open) on top of the launch impulse - Level1Economy's momentum option.")]
        public bool addPreAimVelocityToLaunch = false;
        [Tooltip("Under the momentum option, a launch from a WALL stick synthesizes a carry velocity equal to a launch at AT LEAST this charge fraction (the previous launch's charge wins when higher) - a wall stick holds zero velocity, so without this wall relaunches got no momentum treatment at all. 0 = off; the merged economy harness stamps the recharge baseline here.")]
        [Range(0f, 1f)] public float wallLaunchMomentumFloorFraction = 0f;
        float previousLaunchChargeFraction;

        bool wallCarryArmed;

        Vector3 WallMomentumCarry(Vector3 direction)
        {
            if (!addPreAimVelocityToLaunch || !wallCarryArmed || wallLaunchMomentumFloorFraction <= 0f) return Vector3.zero;
            float carryFraction = Mathf.Max(wallLaunchMomentumFloorFraction, previousLaunchChargeFraction);
            float carrySpeed = Mathf.Lerp(minLaunchForce, maxLaunchForce, carryFraction) / rb.mass;
            return direction.normalized * carrySpeed;
        }
        [Tooltip("Multiplies real seconds of holding into charge-seconds - the main knob for how fast charging feels.")]
        public float chargeAccumulationRate = 0.3f;

        [Tooltip("How quickly a charge input's rate ramps up while sustained (1 = rate doubles after one second). Ramp resets when the dial flips direction.")]
        public float chargeAcceleration = 1f;
        [Tooltip("GROUNDED aim only: overrides chargeAcceleration for the hold-to-charge ramp when >= 0 (the aim-lab scenes set a steeper value - the bullet-time's scaled fill mutes the shared default there). -1 = use chargeAcceleration, identical everywhere.")]
        public float groundedAimChargeAcceleration = -1f;
        [Tooltip("Test-level switch: the tank is pinned at 100% and refunds/costs are ignored.")]
        public bool infiniteEnergy = false;

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

        public float groundPoundBoostMultiplier = 1.5f;
        public float groundPoundHopHeight = 0.2f;
        public float groundPoundSlowDuration = 0.5f;

        public float groundPoundChargeBaseSpeed = 1.5f;
        public float groundPoundChargeSpeedGrowth = 5f;

        [Header("Aim Slowdown Resource")]
        [Tooltip("How the midair-aim slow-down is paid for - see the SlowdownMode enum.")]
        public SlowdownMode slowdownMode = SlowdownMode.Unlimited;
        [Tooltip("AimBudget mode: total real seconds of slow-down available. Refills on every crash.")]
        public float aimBudgetSeconds = 2f;

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

        [Tooltip("Pitch the grounded aim starts at every time it opens. Negative = upward.")]
        public float defaultAimPitch = -30f;
        public Transform cameraTransform;
        public KineticEnergy.Camera.ThirdPersonOrbitCamera cameraOrbit;
        [Tooltip("Yellow direction arrow shown while a charge is being aimed.")]
        public AimArrowIndicator aimArrow;

        [Header("Midair Energy Dial")]
        [Tooltip("Charge added/removed per second while the right stick is pushed up/down during the midair aim.")]
        public float dialStickRate = 0.5f;
        [Tooltip("Charge added/removed per mouse-wheel notch during the midair aim.")]
        public float dialWheelStep = 0.05f;
        [Tooltip("Multiplies the GAMEPAD dial rate (stick and bumpers) during the MIDAIR aim only - the wheel and the grounded dial are untouched. 1.2 = 20% faster charging on a controller.")]
        public float gamepadMidairDialRateMultiplier = 1.2f;
        [Tooltip("How quickly the GAMEPAD midair dial accelerates while held in one direction (1 = rate doubles after one second, 0 = flat rate). Flipping direction drops straight back to the base speed. Replaces the shared Charge Acceleration for this input only.")]
        public float gamepadMidairDialAcceleration = 1f;

        [Header("Landing Preview")]
        public int maxPredictionSteps = 3000;
        public float previewLineHeight = 0.65f;
        public float groundCheckDistance = 0.6f;
        public LandingPreviewController landingPreview;

        [Header("Mouse Aim Option")]

        public bool groundedAimWithMouse = true;
        public float groundedMouseAimSensitivity = 0.15f;
        [Tooltip("WASD-as-camera turns this much faster while Always Mouse is on - keys are all-or-nothing, unlike a stick.")]
        public float wasdCameraTurnMultiplier = 1.5f;

        [Header("Time Scales")]
        [Tooltip("Global time scale while a launch is being aimed/charged midair (bullet time).")]
        public float chargeTimeScale = 0.2f;
        [Tooltip("Base global time scale while a launch is in flight and nothing is charging.")]
        public float launchFlightTimeScale = 2f;

        [Tooltip("Added to the flight time scale per full tank of energy spent on the launch (1 = +1% speed per 1% energy).")]
        public float flightTimeScaleEnergyBonus = 1f;

        [Tooltip("Extra game speed on the first falling frame of a flight (0.01 = +1%).")]
        public float fallSpeedUpStart = 0.01f;
        [Tooltip("Extra game speed at the moment of impact (0.5 = +50%).")]
        public float fallSpeedUpEnd = 0.5f;

        [Header("Launch Limit")]
        [Tooltip("Launches allowed since last standing/crashing (a crash resets the budget). 0 = unlimited.")]
        public int maxLaunchesPerFlight = 2;

        [Tooltip("Launches granted by a NON-grounding crash (walls/sides/floating objects) until truly grounded again. 0 = off.")]
        public int wallCrashLaunchAllowance = 0;

        [Header("Crash Guards")]

        public float launchGraceDuration = 0.15f;

        public float minLaunchClearDistance = 2f;
        [Tooltip("How close a surface normal must be to world-up (dot) to count as walkable flat ground.")]
        [Range(0f, 1f)] public float flatGroundStickThreshold = 0.9f;
        [Tooltip("How steeply downward a launch must aim (dot with down) to count as a slam that bypasses the guards above.")]
        [Range(0f, 1f)] public float slamDownwardThreshold = 0.7f;

        public int stuckOnGroundTickThreshold = 10;
        [Tooltip("How long a NON-sticky wall/ceiling holds a crash before dropping the cube back into gravity.")]
        public float nonStickyWallStickDuration = 0.3f;

        [Header("Fall Reset")]
        public float fallResetY = -30f;

        [Header("Physics")]

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
        [Tooltip("Right Mouse / Left Trigger - holds the midair first-person aim open.")]
        public InputActionReference airAimAction;
        [Tooltip("Left Mouse / Right Trigger - confirms the midair launch.")]
        public InputActionReference airLaunchAction;

        [Header("Controls Text")]

        public Text controlsPanelBody;

        Rigidbody rb;
        BoxCollider boxCollider;
        KineticCubeControllerFreeMove freeMoveController;

        bool isAiming;
        bool waitingForAimRelease;
        float aimYaw;
        float aimPitch;

        enum HoldChargeDirection { None, Up, Down }
        HoldChargeDirection holdChargeDirection = HoldChargeDirection.None;
        float holdChargeHeldSeconds;
        float aimChargeHeldSeconds;

        float dialRampSeconds;
        int dialRampDirection;

        bool airAiming;

        Vector3 preAirAimVelocity;

        float chargeTime;

        bool aimButtonSpent;

        int launchesRemainingOverride = -1;

        bool hasLaunched;
        bool currentFlightIsDownward;
        bool currentFlightIsVertical;
        float currentFlightIntensity;
        bool exactFlightNoNudge;
        float launchGraceTimer;
        Vector3 launchStartPosition;
        int groundedTicksSinceLaunch;
        int launchesSinceGrounded;
        Vector3 velocityBeforePhysicsStep;

        bool isStuck;
        Vector3 stuckSurfaceNormal;
        float nonStickyReleaseTimer;
        bool isGrounded;
        bool groundedLastFrame;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;
        float queuedDamping;
        Vector3 queuedExtraVelocity;

        float energyFraction;
        float lastLaunchEnergySpent;
        bool lastLaunchWasGrounded;

        public enum LaunchKind { GroundedAim, HoldCharge, AirAim }
        public LaunchKind LastLaunchKind { get; private set; }
        bool lastLaunchWasPound;

        float flightEnergySpent;

        float poundWindowTimer;
        float poundPendingRefund;

        bool poundWindowFromPound;
        float poundBoostExtra;
        bool poundAimHoldingGravityOff;

        float activeFlightTimeScale = 1f;

        float flightApexY;
        float flightPredictedLandingY;

        float gradualDrainRemaining;
        float gradualDrainPerSecond;

        float lastPredictedFlightSeconds;

        float lastPredictedFlightRealSeconds;

        float aimBudgetRemaining;
        float slowdownSecondsUsed;
        bool slowdownWasAvailable;

        Vector3[] trajectoryBuffer;
        Vector3 lastPredictedLanding;
        bool hasValidPredictedLanding;
        int lastTrajectoryStepCount;
        Vector3 lastPredictedLandingNormal = Vector3.up;

        Collider lastPredictedLandingSource;
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

        public float EnergyFraction => energyFraction;
        public bool IsStuck => isStuck;
        public bool IsGrounded => isGrounded;

        public Vector3 StuckSurfaceNormal => stuckSurfaceNormal;

        public void CarryStuckRider(Vector3 riderVelocity, Quaternion rotationDelta)
        {
            if (!isStuck) return;
            stuckCarryVelocity = riderVelocity;
            pendingCarryRotation = rotationDelta;
            hasPendingCarry = true;
        }

        Vector3 stuckCarryVelocity;
        Quaternion pendingCarryRotation = Quaternion.identity;
        bool hasPendingCarry;
        public bool IsAimingOrCharging => isAiming || airAiming || holdChargeDirection != HoldChargeDirection.None;
        public bool HasLaunched => hasLaunched;
        public int LaunchesSinceGrounded => launchesSinceGrounded;

        public float SlowdownSecondsUsed => slowdownSecondsUsed;
        public float AimBudgetRemaining => aimBudgetRemaining;

        public bool IsAirAiming => airAiming;

        public MovingPlatform GroundPlatform => freeMoveController != null ? freeMoveController.GroundPlatform : null;

        public float LastLaunchEnergySpent => lastLaunchEnergySpent;

        float crashEnergySpent;
        int crashEnergySpentFrame = -1;

        public float ArrivalEnergySpent => crashEnergySpentFrame == Time.frameCount
            ? Mathf.Max(lastLaunchEnergySpent, crashEnergySpent)
            : lastLaunchEnergySpent;

        public Vector3 LastPredictedLanding => lastPredictedLanding;

        public Collider PredictedLandingSource => lastPredictedLandingSource;

        public float ProjectedLaunchSpend => energyCostPerFullCharge > 0f
            ? Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge)
            : SpendableEnergy();
        public bool HasValidPredictedLanding => hasValidPredictedLanding;

        public Vector3 LastPredictedLandingNormal => lastPredictedLandingNormal;
        public float CurrentChargeFraction => ChargeFraction();

        public void ForceEndAirAimAndFall()
        {
            if (!airAiming) return;
            CancelAirAim();
            rb.linearVelocity = Vector3.zero;
            rb.useGravity = true;

            airAimLockedUntilGrounded = true;
            cameraOrbit?.SnapToThirdPersonOrbit();
        }

        bool airAimLockedUntilGrounded;

        public void AddEnergy(float delta)
        {
            if (infiniteEnergy) return;
            energyFraction = Mathf.Clamp01(energyFraction + delta);
        }

        public bool TryGetFlightPositionAhead(float gameSecondsAhead, out Vector3 position)
        {
            position = transform.position;
            if (!hasLaunched || !hasValidPredictedLanding || lastTrajectoryStepCount < 2) return false;
            float flightTime = flightElapsedSeconds + Mathf.Max(gameSecondsAhead, 0f);
            int index = Mathf.Clamp(Mathf.RoundToInt(flightTime / Time.fixedDeltaTime), 0, lastTrajectoryStepCount - 1);
            position = trajectoryBuffer[index];
            return true;
        }

        float flightElapsedSeconds;

        public bool TryGetPredictedArcPoint(float fraction, out Vector3 point, out Vector3 landing)
        {
            point = Vector3.zero;
            landing = lastPredictedLanding;

            if ((!airAiming && !isAiming) || !hasValidPredictedLanding || lastTrajectoryStepCount < 2) return false;
            int index = Mathf.Clamp(Mathf.RoundToInt(lastTrajectoryStepCount * fraction), 0, lastTrajectoryStepCount - 1);
            point = trajectoryBuffer[index];
            return true;
        }
        public float PredictedFlightSecondsLive => lastPredictedFlightSeconds;
        public float PredictedFlightRealSecondsLive => lastPredictedFlightRealSeconds;

        public event System.Action MidairAimOpened;
        public event System.Action SlowdownDepleted;
        public event System.Action LaunchFired;

        public event System.Action<float, UnityEngine.Vector3> MidairAimFired;
        public event System.Action MidairAimReleased;
        public event System.Action<UnityEngine.Vector3> CrashRegistered;

        public event System.Action EnemyKilled;
        public event System.Action PlayerHurt;
        bool suppressAimReleasedEvent;
        bool justUnpaused;
        KineticEnergy.Camera.AimCameraVariantController aimVariants;

        bool FreeLookAimActive => aimVariants != null && aimVariants.ActivePreset != null
            && aimVariants.ActivePreset.UsesFreeLook;

        public bool AllowGroundedMovement => !IsAimingOrCharging && !hasLaunched && !isStuck

            && knockbackTimer <= 0f;
        public bool AllowAirborneNudge => !IsAimingOrCharging && !isStuck && launchGraceTimer <= 0f

            && !exactFlightNoNudge;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            boxCollider = GetComponent<BoxCollider>();
            freeMoveController = GetComponent<KineticCubeControllerFreeMove>();
            aimVariants = GetComponent<KineticEnergy.Camera.AimCameraVariantController>();
            trajectoryBuffer = new Vector3[Mathf.Max(maxPredictionSteps, 1)];
            ApplyGravity();
            energyFraction = infiniteEnergy ? 1f : startingEnergyFraction;
            aimBudgetRemaining = aimBudgetSeconds;

            rb.useGravity = true;

            GetComponent<Polish>()?.ResetCrashParticles();
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
            if (zeroDampingMatchedLaunches) ComputeZeroDampingForces();
        }

        void OnEnable()
        {
            EnableAction(moveAction);
            EnableAction(groundedAimAction);
            EnableAction(groundedLaunchAction);
            EnableAction(upLaunchAction);
            EnableAction(groundPoundAction);
            EnableAction(cancelChargeAction);
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

        void Update()
        {

            bool paused = Time.timeScale <= 0f;
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;

            if (paused)
            {
                justUnpaused = true;
                return;
            }

            if (justUnpaused)
            {
                justUnpaused = false;
                waitingForAimRelease = true;
                aimButtonSpent = true;
                return;
            }

            if (infiniteEnergy) energyFraction = 1f;

            if (aimButtonSpent && !AimButtonHeld()) aimButtonSpent = false;

            bool poundBoostClaimable = poundWindowTimer > 0f && poundPendingRefund > 0f;
            if (AimButtonPressedThisFrame() && !poundBoostClaimable
                && (launchLockTimer > 0f || energyFraction <= 0f || !CanStartNewLaunch()))
            {
                aimButtonSpent = true;
            }

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

            UpdateSlowdownResource();
            ApplyChargeTimeScale();
            UpdateCameraCoordination();
            UpdateEnergyMeters();

            if (isAiming)
            {

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

                if (isGrounded && !poundAimHoldingGravityOff) CancelAirAim();
                else UpdateAirAim();
            }
            else if (isGrounded && poundWindowTimer <= 0f)
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

                if (energyFraction > 0f && CanStartNewLaunch() && PoundPressedThisFrame())
                {
                    StartHoldCharge(HoldChargeDirection.Down);
                }
                else if (energyFraction > 0f && CanStartNewLaunch() && UpChargePressedThisFrame())
                {
                    StartHoldCharge(HoldChargeDirection.Up);
                }
                else
                {
                    UpdateAirAim();
                }
            }

            if (isGrounded || isStuck) airAimLockedUntilGrounded = false;

            groundedLastFrame = isGrounded;
        }

        bool SlowdownAvailable()
        {
            switch (slowdownMode)
            {
                case SlowdownMode.AimBudget: return aimBudgetRemaining > 0f;
                case SlowdownMode.EnergyTank: return energyFraction > 0f;
                default: return true;
            }
        }

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

        void ApplyChargeTimeScale()
        {

            bool rawAimHeld = AimButtonHeld() && !aimButtonSpent && energyFraction > 0f
                && CanStartNewLaunch() && !airAimLockedUntilGrounded;
            bool airAimSlow = !isGrounded && (airAiming || rawAimHeld) && SlowdownAvailable();

            bool poundWindowSlow = poundWindowTimer > 0f;

            bool holdChargeSlow = holdChargeDirection != HoldChargeDirection.None;

            float flightScale = 1f;
            if (hasLaunched)
            {
                flightScale = activeFlightTimeScale;

                flightApexY = Mathf.Max(flightApexY, transform.position.y);
                if (rb.linearVelocity.y < 0f && !currentFlightIsDownward)
                {
                    float descentSpan = Mathf.Max(flightApexY - flightPredictedLandingY, 0.01f);
                    float descentProgress = Mathf.Clamp01((flightApexY - transform.position.y) / descentSpan);
                    flightScale *= 1f + Mathf.Lerp(fallSpeedUpStart, fallSpeedUpEnd, descentProgress);
                }
            }

            if (isGrounded && knockbackTimer <= 0f && launchLockTimer <= 0f) flightSpeedUpSuppressed = false;

            if (flightSpeedUpSuppressed || knockbackTimer > 0f || launchLockTimer > 0f) flightScale = 1f;

            bool slowRequested = (airAimSlow || holdChargeSlow || poundWindowSlow) && launchLockTimer <= 0f;
            Time.timeScale = slowRequested ? chargeTimeScale : flightScale;
        }

        bool AimButtonHeld()
        {
            if (groundedAimAction != null && groundedAimAction.action != null && groundedAimAction.action.IsPressed()) return true;
            if (airAimAction != null && airAimAction.action != null && airAimAction.action.IsPressed()) return true;
            return false;
        }

        bool AimButtonPressedThisFrame()
        {
            if (groundedAimAction != null && groundedAimAction.action != null && groundedAimAction.action.WasPressedThisFrame()) return true;
            if (airAimAction != null && airAimAction.action != null && airAimAction.action.WasPressedThisFrame()) return true;
            return false;
        }

        void UpdateCameraCoordination()
        {
            if (cameraOrbit == null) return;

            bool moveIsKeyboardDriven = moveAction != null && moveAction.action != null
                && moveAction.action.activeControl != null && moveAction.action.activeControl.device is Keyboard;

            bool freeLookAim = FreeLookAimActive && airAiming;
            bool aimWithMoveStick = (airAiming && !moveIsKeyboardDriven)
                || (groundedAimWithMouse && isAiming && moveIsKeyboardDriven);

            Vector2 aimStick = aimWithMoveStick && moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            Vector2 freeLook = Vector2.zero;
            if (freeLookAim)
            {
                if (moveIsKeyboardDriven && moveAction != null && moveAction.action != null)
                {
                    freeLook += moveAction.action.ReadValue<Vector2>();
                }
                freeLook += GamepadLookValue();
            }
            cameraOrbit.SetFreeLook(freeLookAim, freeLook);
            if (aimStick.sqrMagnitude < aimDeadzone * aimDeadzone) aimStick = Vector2.zero;
            if (groundedAimWithMouse && isAiming && moveIsKeyboardDriven) aimStick *= wasdCameraTurnMultiplier;
            cameraOrbit.SetAimStickOverride(aimWithMoveStick, aimStick,
                groundedAimWithMouse && isAiming && moveIsKeyboardDriven);

            cameraOrbit.SetMouseLookSuppressed(groundedAimWithMouse && isAiming);

            cameraOrbit.SetIgnoreSlowMo(holdChargeDirection == HoldChargeDirection.Up || holdChargeDirection == HoldChargeDirection.Down);

            cameraOrbit.SetLaunchInFlight(hasLaunched, currentFlightIsVertical, currentFlightIntensity);

            cameraOrbit.SetRemainingFlight(hasLaunched && lastPredictedFlightSeconds > 0.05f
                ? Mathf.Max(lastPredictedFlightSeconds - flightElapsedSeconds, 0f)
                : float.PositiveInfinity);
            cameraOrbit.SetPlayerGrounded(isGrounded);

            bool framingAim = !isGrounded && hasValidPredictedLanding && airAiming;
            cameraOrbit.SetTrajectoryFraming(framingAim, lastPredictedLanding);
        }

        void UpdateEnergyMeters()
        {

            if (energyMeter != null)
            {
                energyMeter.SetVisible(!infiniteEnergy);
                energyMeter.SetEnergy(energyFraction);

                energyMeter.SetEnergyTint();
                energyMeter.SetLaunchLocked(launchLockTimer > 0f);
                bool charging = isAiming || holdChargeDirection != HoldChargeDirection.None || airAiming;
                energyMeter.SetCharge(ChargeFraction(), charging);

                energyMeter.SetBonus(
                    energyFraction + poundPendingRefund * (groundPoundBoostMultiplier - 1f),
                    poundPendingRefund > 0f && poundWindowTimer > 0f);

                KineticEnergy.Level.EnergyRequirement aimedRequirement = null;
                if (IsAimingOrCharging && hasValidPredictedLanding && lastPredictedLandingSource != null)
                {
                    aimedRequirement = lastPredictedLandingSource.GetComponentInParent<KineticEnergy.Level.EnergyRequirement>();
                }

                float projectedSpend = energyCostPerFullCharge > 0f
                    ? Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge)
                    : SpendableEnergy();
                energyMeter.SetChargeTint(projectedSpend, charging);
                energyMeter.SetRequirementTick(
                    aimedRequirement != null ? aimedRequirement.RequirementFraction : 0f,
                    aimedRequirement != null ? aimedRequirement.TierColor : Color.white,
                    aimedRequirement != null);
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

        void UpdateGroundedAim()
        {
            bool aimPressed = groundedAimAction != null && groundedAimAction.action != null && groundedAimAction.action.IsPressed();

            bool cancelPressed = !bumperEnergyDial
                && cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame();

            if (isAiming && cancelPressed)
            {
                CloseGroundedAim();
                waitingForAimRelease = true;
                return;
            }

            if (waitingForAimRelease)
            {
                if (!aimPressed) waitingForAimRelease = false;
                return;
            }

            bool canStartNewAim = energyFraction > 0f && CanStartNewLaunch();
            bool aimHeld = isAiming ? aimPressed : (aimPressed && canStartNewAim);

            if (aimHeld)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;
                    aimChargeHeldSeconds = 0f;

                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    SeedAimFromCamera();
                    aimArrow?.SetVisible(true);
                    landingPreview?.SetVisible(true);

                    landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);
                    if (!isGrounded) MidairAimOpened?.Invoke();
                }

                if (groundedDialControls)
                {

                    float groundedDial = 0f;
                    if (Gamepad.current != null)
                    {
                        float bumpers = (Gamepad.current.rightShoulder.isPressed ? 1f : 0f)
                            - (Gamepad.current.leftShoulder.isPressed ? 1f : 0f);
                        if (bumpers != 0f) groundedDial += bumpers * dialStickRate * maxChargeTime * Time.unscaledDeltaTime;
                    }
                    if (Mouse.current != null)
                    {
                        float scroll = Mouse.current.scroll.ReadValue().y;
                        if (Mathf.Abs(scroll) > 0.01f) groundedDial += Mathf.Sign(scroll) * dialWheelStep * maxChargeTime;
                    }
                    chargeTime = Mathf.Clamp(chargeTime + groundedDial, 0f, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));
                }
                else
                {

                    aimChargeHeldSeconds += Time.unscaledDeltaTime;
                    float groundedRamp = groundedAimChargeAcceleration >= 0f
                        ? 1f + aimChargeHeldSeconds * groundedAimChargeAcceleration
                        : ChargeRateRamp(aimChargeHeldSeconds);
                    AccumulateCharge(Time.deltaTime * chargeAccumulationRate * groundedRamp);
                }

                float aimDt = Time.unscaledDeltaTime;
                var refinement = KineticEnergy.Camera.AimRefinementSettings.Active;
                if (groundedAimWithMouse && Mouse.current != null)
                {
                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();

                    if (refinement != null && refinement.groundedFineAimEnabled && mouseDelta.sqrMagnitude > 0.0001f)
                    {
                        float t = Mathf.Clamp01(mouseDelta.magnitude / Mathf.Max(refinement.groundedFineAimMouseReference, 0.01f));
                        mouseDelta *= Mathf.Lerp(refinement.groundedFineAimMinFactor, 1f, t);
                    }
                    aimYaw = Mathf.Repeat(aimYaw + mouseDelta.x * groundedMouseAimSensitivity, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - mouseDelta.y * groundedMouseAimSensitivity, minAimPitch, maxAimPitch);
                }

                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;
                bool moveIsGamepad = moveAction != null && moveAction.action != null
                    && moveAction.action.activeControl != null && moveAction.action.activeControl.device is Gamepad;
                if ((!groundedAimWithMouse || moveIsGamepad) && stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {

                    if (refinement != null) stick = refinement.ConditionStick(stick);
                    aimYaw = Mathf.Repeat(aimYaw + stick.x * aimRotationSpeed * aimDt, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - stick.y * aimRotationSpeed * aimDt, minAimPitch, maxAimPitch);
                }

                if (groundedAimCameraFollow && cameraOrbit != null)
                {
                    float cameraYaw = cameraOrbit.CurrentYaw;
                    float aimDelta = Mathf.DeltaAngle(cameraYaw, aimYaw);
                    float maxDelta = groundedAimFollowThreshold + groundedAimFollowBand;
                    if (Mathf.Abs(aimDelta) > maxDelta)
                    {
                        aimYaw = Mathf.Repeat(cameraYaw + Mathf.Sign(aimDelta) * maxDelta, 360f);
                    }
                    cameraOrbit.ApplyAimEdgeFollow(aimYaw, groundedAimFollowThreshold, groundedAimFollowBand, groundedAimFollowSpeed);
                }

                Vector3 direction = AimDirection();
                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(direction, chargeFraction);

                float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
                float damping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);
                ApplyZeroDampingMatch(chargeFraction, ref force, ref damping);

                Vector3 previewBase = rb.linearVelocity;
                if (freeMoveController != null && isGrounded) previewBase -= freeMoveController.GroundPlatformVelocity;
                ShowLandingPreview(direction * force / rb.mass + previewBase + WallMomentumCarry(direction), damping);

                bool firePressed = groundedLaunchAction != null && groundedLaunchAction.action != null && groundedLaunchAction.action.WasPressedThisFrame();
                if (firePressed)
                {
                    QueueLaunch(direction, force, damping);
                    LastLaunchKind = LaunchKind.GroundedAim;
                    CloseGroundedAim();
                    waitingForAimRelease = true;
                }
            }
            else if (isAiming)
            {

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

            aimYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            aimPitch = Mathf.Clamp(defaultAimPitch, minAimPitch, maxAimPitch);
        }

        Vector3 AimDirection()
        {
            return Quaternion.Euler(aimPitch, aimYaw, 0f) * Vector3.forward;
        }

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

            bool releasedNow = holdChargeDirection switch
            {
                HoldChargeDirection.Up => !((upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.IsPressed())
                    || (keyboardAvailable && Keyboard.current.spaceKey.isPressed)),
                HoldChargeDirection.Down => !((groundPoundAction != null && groundPoundAction.action != null && groundPoundAction.action.IsPressed())
                    || (keyboardAvailable && Keyboard.current.eKey.isPressed)),
                _ => false,
            };

            holdChargeHeldSeconds += Time.unscaledDeltaTime;
            float chargeSpeed = groundPoundChargeBaseSpeed + groundPoundChargeSpeedGrowth * holdChargeHeldSeconds;
            AccumulateCharge(Time.unscaledDeltaTime * chargeAccumulationRate * chargeSpeed);

            Vector3 direction = holdChargeDirection == HoldChargeDirection.Down ? Vector3.down : Vector3.up;

            float chargeFraction = ChargeFraction();
            aimArrow?.SetAim(direction, chargeFraction);

            float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
            float damping = holdChargeDirection == HoldChargeDirection.Down
                ? downLaunchDamping
                : Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);
            if (holdChargeDirection != HoldChargeDirection.Down)
            {
                ApplyZeroDampingMatch(chargeFraction, ref force, ref damping);
            }

            ShowLandingPreview(direction * force / rb.mass, damping);

            if (releasedNow)
            {
                bool firedPound = holdChargeDirection == HoldChargeDirection.Down && !isGrounded;
                QueueLaunch(direction, force, damping);
                LastLaunchKind = LaunchKind.HoldCharge;
                lastLaunchWasPound = firedPound;
                CancelHoldCharge();
            }
        }

        void UpdateAirAim()
        {
            bool aimHeld = airAimAction != null && airAimAction.action != null && airAimAction.action.IsPressed();

            if (!aimHeld)
            {
                if (airAiming)
                {

                    bool resumeFlight = hasLaunched && !isGrounded && !poundAimHoldingGravityOff;
                    CancelAirAim();
                    if (resumeFlight)
                    {
                        rb.linearVelocity = preAirAimVelocity;
                    }

                    cameraOrbit?.SnapToThirdPersonOrbit();
                }
                return;
            }

            if (!airAiming)
            {

                if (airAimLockedUntilGrounded) return;

                bool poundBoostClaimable = poundWindowTimer > 0f && poundPendingRefund > 0f;
                if (!poundBoostClaimable && (energyFraction <= 0f || !CanStartNewLaunch())) return;

                if (!(airAimAction != null && airAimAction.action != null && airAimAction.action.WasPressedThisFrame())) return;

                airAiming = true;
                preAirAimVelocity = rb.linearVelocity;
                chargeTime = 0f;
                dialRampSeconds = 0f;
                dialRampDirection = 0;

                cameraOrbit?.SetFirstPersonMode(true);
                landingPreview?.SetVisible(true);
                landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);
                MidairAimOpened?.Invoke();

                if (poundWindowTimer > 0f)
                {
                    PayPoundBoostedRefund();
                    if (poundWindowFromPound)
                    {

                        poundAimHoldingGravityOff = true;
                        rb.useGravity = false;

                        chargeTime = Mathf.Min(maxChargeTime, EnergyChargeCeiling());
                    }
                }
            }

            float dialDelta = 0f;

            float padDialRate = dialStickRate * gamepadMidairDialRateMultiplier;
            bool dialIsGamepad = false;
            if (FreeLookAimActive || bumperEnergyDial)
            {

                if (Gamepad.current != null)
                {
                    float bumpers = (Gamepad.current.rightShoulder.isPressed ? 1f : 0f)
                        - (Gamepad.current.leftShoulder.isPressed ? 1f : 0f);
                    if (bumpers != 0f)
                    {
                        dialDelta += bumpers * padDialRate * maxChargeTime * Time.unscaledDeltaTime;
                        dialIsGamepad = true;
                    }
                }
            }
            else
            {
                float stickY = GamepadLookValue().y;
                if (Mathf.Abs(stickY) > 0.5f)
                {
                    dialDelta += Mathf.Sign(stickY) * padDialRate * maxChargeTime * Time.unscaledDeltaTime;
                    dialIsGamepad = true;
                }
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

                float dialRamp = dialIsGamepad
                    ? 1f + dialRampSeconds * Mathf.Max(gamepadMidairDialAcceleration, 0f)
                    : ChargeRateRamp(dialRampSeconds);
                chargeTime = Mathf.Clamp(chargeTime + dialDelta * dialRamp,
                    0f, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));
            }

            chargeTime = Mathf.Min(chargeTime, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));

            Vector3 direction = cameraOrbit != null ? cameraOrbit.AimForward : transform.forward;
            float fireFraction = Mathf.Min(ChargeFraction(), energyCostPerFullCharge > 0f ? SpendableEnergy() / energyCostPerFullCharge : 1f);
            float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, fireFraction);
            float damping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, fireFraction);
            ApplyZeroDampingMatch(fireFraction, ref force, ref damping);

            cameraOrbit?.SetAimZoom(fireFraction);

            Vector3 momentumCarry = addPreAimVelocityToLaunch
                ? direction.normalized * preAirAimVelocity.magnitude
                : Vector3.zero;
            momentumCarry += WallMomentumCarry(direction);
            ShowLandingPreview(rb.linearVelocity + momentumCarry + direction * force / rb.mass, damping);

            bool firePressed = airLaunchAction != null && airLaunchAction.action != null && airLaunchAction.action.WasPressedThisFrame();
            if (firePressed && energyFraction > 0f && CanStartNewLaunch())
            {
                chargeTime = fireFraction * maxChargeTime;
                QueueLaunch(direction, force, damping);
                LastLaunchKind = LaunchKind.AirAim;

                if (addPreAimVelocityToLaunch)
                {
                    rb.linearVelocity += direction.normalized * preAirAimVelocity.magnitude;
                }
                exactFlightNoNudge = true;
                MidairAimFired?.Invoke(fireFraction, lastPredictedLanding);
                suppressAimReleasedEvent = true;
                CancelAirAim();

                cameraOrbit?.SnapToThirdPersonOrbit();
            }
        }

        void CancelAirAim()
        {

            if (poundAimHoldingGravityOff)
            {
                poundAimHoldingGravityOff = false;
                poundWindowTimer = 0f;
                rb.useGravity = true;
                RevertPoundBoost();
            }
            airAiming = false;
            chargeTime = 0f;
            aimButtonSpent = true;
            landingPreview?.SetVisible(false);
            cameraOrbit?.SetFirstPersonMode(false);
            cameraOrbit?.SetAimZoom(0f);

            if (suppressAimReleasedEvent) suppressAimReleasedEvent = false;
            else MidairAimReleased?.Invoke();
        }

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

        Vector2 GamepadLookValue()
        {
            InputActionReference look = cameraOrbit != null ? cameraOrbit.lookAction : null;
            if (look == null || look.action == null) return Vector2.zero;
            if (look.action.activeControl == null || !(look.action.activeControl.device is Gamepad)) return Vector2.zero;
            return look.action.ReadValue<Vector2>();
        }

        float ChargeFraction()
        {

            if (alwaysMaxCharge)
            {
                float maxTime = Mathf.Min(maxChargeTime, EnergyChargeCeiling());
                return maxChargeTime > 0f ? Mathf.Clamp01(maxTime / maxChargeTime) : 1f;
            }
            return maxChargeTime > 0f ? Mathf.Clamp01(chargeTime / maxChargeTime) : 1f;
        }

        float ChargeRateRamp(float sustainedSeconds)
        {
            return 1f + sustainedSeconds * Mathf.Max(chargeAcceleration, 0f);
        }

        void AccumulateCharge(float delta)
        {
            chargeTime = Mathf.Min(chargeTime + delta, maxChargeTime, EnergyChargeCeiling());
        }

        float EnergyChargeCeiling()
        {
            return energyCostPerFullCharge > 0f ? (SpendableEnergy() / energyCostPerFullCharge) * maxChargeTime : maxChargeTime;
        }

        float SpendableEnergy()
        {
            bool treatAsGrounded = isGrounded && !poundAimHoldingGravityOff;
            return treatAsGrounded ? Mathf.Max(energyFraction - minEnergyReserve, 0f) : energyFraction;
        }

        void ClampEnergyFloor()
        {
            if (energyFraction < minEnergyReserve) energyFraction = minEnergyReserve;
        }

        public void ClampEnergyTo(float fraction)
        {
            if (infiniteEnergy) return;
            energyFraction = Mathf.Min(energyFraction, Mathf.Clamp01(fraction));
        }

        public void EnsureEnergyAtLeast(float fraction)
        {
            if (infiniteEnergy) return;
            energyFraction = Mathf.Max(energyFraction, Mathf.Clamp01(fraction));
        }

        public void SetEnergyTo(float fraction)
        {
            if (infiniteEnergy) return;
            energyFraction = Mathf.Clamp01(fraction);
        }

        [Tooltip("While clinging to a surface, an aim whose dot with that surface's normal is at or below this counts as fired INTO the surface - the preview marks it as a failed shot. Slightly above 0 so shots that merely graze along the face count too.")]
        public float stuckSurfaceLaunchClearance = 0.12f;

        [Tooltip("Seconds of lost ground control after an enemy hit, so the knockback actually carries (grounded movement overwrites velocity every tick otherwise).")]
        public float enemyHitControlLossSeconds = 0.35f;
        float knockbackTimer;
        float launchLockTimer;

        bool flightSpeedUpSuppressed;
        Vector3 pendingEnemyKnockback;
        bool hasPendingEnemyKnockback;

        public void ApplyEnemyHit(Vector3 impulse, float energyLoss, float launchLockSeconds, bool canEmptyRespawn = true)
        {
            PlayerHurt?.Invoke();
            launchLockTimer = Mathf.Max(launchLockTimer, launchLockSeconds);
            poundWindowTimer = 0f;
            if (airAiming) CancelAirAim();
            CancelHoldCharge();
            CloseGroundedAim();
            waitingForAimRelease = true;
            aimButtonSpent = true;

            isStuck = false;
            nonStickyReleaseTimer = 0f;
            rb.useGravity = true;
            rb.linearDamping = plainFallDamping;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            pendingEnemyKnockback = impulse;
            hasPendingEnemyKnockback = true;
            knockbackTimer = enemyHitControlLossSeconds;
            flightSpeedUpSuppressed = true;

            if (!infiniteEnergy)
            {
                energyFraction = Mathf.Max(energyFraction - energyLoss, 0f);

                if (energyFraction <= 0f && canEmptyRespawn) EnergyEmptiedByHit?.Invoke();
            }
        }

        public event System.Action EnergyEmptiedByHit;

        public void RespawnAtPoint(Vector3 position)
        {
            if (airAiming) CancelAirAim();
            CancelHoldCharge();
            CloseGroundedAim();
            waitingForAimRelease = true;
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
            previousLaunchChargeFraction = 0f;
            wallCarryArmed = false;
            airAimLockedUntilGrounded = false;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = true;
            rb.linearDamping = plainFallDamping;

            hasPendingEnemyKnockback = false;
            knockbackTimer = 0f;
            launchLockTimer = 0f;
            flightSpeedUpSuppressed = false;

            transform.position = position;
            rb.position = position;

            RigidbodyInterpolation previousInterpolation = rb.interpolation;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.interpolation = previousInterpolation;

            energyFraction = infiniteEnergy ? 1f : startingEnergyFraction;
            Time.timeScale = 1f;

            cameraOrbit?.ResetToStartPose();
        }

        bool CanStartNewLaunch()
        {

            if (launchLockTimer > 0f) return false;

            if (launchesRemainingOverride == 0) return false;
            if (launchesRemainingOverride > 0) return true;
            return maxLaunchesPerFlight <= 0 || launchesSinceGrounded < maxLaunchesPerFlight;
        }

        void ApplyZeroDampingMatch(float chargeFraction, ref float force, ref float damping)
        {
            if (!zeroDampingMatchedLaunches || damping <= 0f) return;
            force = Mathf.Lerp(zeroDampingMinLaunchForce, zeroDampingMaxLaunchForce, chargeFraction);
            damping = 0f;
        }

        void ComputeZeroDampingForces()
        {
            Vector3 reference = new Vector3(0f, Mathf.Sin(45f * Mathf.Deg2Rad), Mathf.Cos(45f * Mathf.Deg2Rad));
            zeroDampingMinLaunchForce = SolveMatchedForce(reference, minLaunchForce, minLaunchDamping);
            zeroDampingMaxLaunchForce = SolveMatchedForce(reference, maxLaunchForce, maxLaunchDamping);
            Debug.Log($"[ZeroDampingTest] matched forces solved at startup: min {minLaunchForce} -> {zeroDampingMinLaunchForce:F2}, max {maxLaunchForce} -> {zeroDampingMaxLaunchForce:F2}");
        }

        float SolveMatchedForce(Vector3 direction, float dampedForce, float damping)
        {
            SimulateFlatFlight(direction * (dampedForce / rb.mass), damping, out float targetRange, out _);
            float low = 0.05f, high = 1f;
            for (int i = 0; i < 32; i++)
            {
                float mid = (low + high) * 0.5f;
                SimulateFlatFlight(direction * (mid * dampedForce / rb.mass), 0f, out float range, out _);
                if (range < targetRange) low = mid;
                else high = mid;
            }
            return dampedForce * (low + high) * 0.5f;
        }

        static void SimulateFlatFlight(Vector3 v0, float damping, out float range, out float apex)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 p = Vector3.zero;
            Vector3 v = v0;
            apex = 0f;
            for (int step = 0; step < 3000; step++)
            {
                v += Physics.gravity * dt;
                v /= 1f + damping * dt;
                Vector3 prev = p;
                p += v * dt;
                if (p.y > apex) apex = p.y;
                if (p.y < 0f && v.y < 0f)
                {

                    float t = prev.y / Mathf.Max(prev.y - p.y, 0.0001f);
                    Vector3 landing = Vector3.Lerp(prev, p, t);
                    range = new Vector3(landing.x, 0f, landing.z).magnitude;
                    return;
                }
            }
            range = new Vector3(p.x, 0f, p.z).magnitude;
        }

        public float ScatterConeAngleFor(float chargeFraction)
        {
            if (launchScatterMaxAngle <= 0f) return 0f;
            float span = 1f - launchScatterStartFraction;
            if (span <= 0.0001f) return chargeFraction >= 1f ? launchScatterMaxAngle : 0f;

            float x = Mathf.Clamp01((chargeFraction - launchScatterStartFraction) / span) * 100f;
            float factor = launchScatterMaxAngle / Mathf.Sqrt(100f);
            return Mathf.Sqrt(x) * factor;
        }

        static Vector3 RandomDirectionInCone(Vector3 direction, float coneAngleDegrees)
        {

            float offsetAngle = coneAngleDegrees * Mathf.Sqrt(Random.value);
            float spin = Random.value * 360f;
            Quaternion tilt = Quaternion.AngleAxis(offsetAngle, Vector3.Cross(direction, Random.onUnitSphere).normalized);
            return (Quaternion.AngleAxis(spin, direction) * tilt) * direction;
        }

        void QueueLaunch(Vector3 direction, float force, float damping)
        {

            float scatterCone = ScatterConeAngleFor(ChargeFraction());
            if (scatterCone > 0.01f)
            {
                direction = RandomDirectionInCone(direction.normalized, scatterCone);
            }
            queuedDirection = direction;
            queuedForce = force;
            queuedDamping = damping;

            queuedExtraVelocity = WallMomentumCarry(direction);
            wallCarryArmed = false;
            launchQueued = true;
            hasLaunched = true;
            launchesSinceGrounded++;
            if (launchesRemainingOverride > 0) launchesRemainingOverride--;
            exactFlightNoNudge = false;
            aimButtonSpent = true;

            lastLaunchWasGrounded = isGrounded && !poundAimHoldingGravityOff;
            lastLaunchWasPound = false;
            currentFlightIsDownward = Vector3.Dot(direction.normalized, Vector3.down) >= slamDownwardThreshold;

            currentFlightIsVertical = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) >= slamDownwardThreshold;

            currentFlightIntensity = ChargeFraction();

            lastLaunchEnergySpent = Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge);

            if (poundAimHoldingGravityOff)
            {
                poundAimHoldingGravityOff = false;
                poundWindowTimer = 0f;
                rb.useGravity = true;
                poundBoostExtra = 0f;
            }
            if (!infiniteEnergy)
            {
                if (gradualLaunchDrain)
                {

                    gradualDrainRemaining = lastLaunchEnergySpent;
                    gradualDrainPerSecond = lastLaunchEnergySpent / lastPredictedFlightSeconds;
                }
                else
                {
                    energyFraction = Mathf.Clamp01(energyFraction - lastLaunchEnergySpent);
                }
            }
            flightEnergySpent += lastLaunchEnergySpent;

            activeFlightTimeScale = launchFlightTimeScale + lastLaunchEnergySpent * flightTimeScaleEnergyBonus;

            lastPredictedFlightRealSeconds = lastPredictedFlightSeconds / Mathf.Max(activeFlightTimeScale, 0.01f);
            flightElapsedSeconds = 0f;

            flightApexY = transform.position.y;
            flightPredictedLandingY = hasValidPredictedLanding ? lastPredictedLanding.y : fallResetY;

            launchGraceTimer = launchGraceDuration;

            previousLaunchChargeFraction = ChargeFraction();

            flightSpeedUpSuppressed = false;

            LaunchFired?.Invoke();
        }

        void FixedUpdate()
        {
            UpdateIgnoredCheckpointButton();

            bool airAimFrozen = airAiming && (isGrounded || SlowdownAvailable());

            bool frozenThisTick = isAiming || holdChargeDirection != HoldChargeDirection.None || airAimFrozen || isStuck
                || poundWindowTimer > 0f;
            if (frozenThisTick)
            {

                Vector3 carryVelocity = Vector3.zero;
                if (hasPendingCarry && isStuck) carryVelocity = stuckCarryVelocity;
                else if (isGrounded && freeMoveController != null) carryVelocity = freeMoveController.GroundPlatformVelocity;

                if (isAiming)
                {
                    if (aimRideBody == null && isGrounded && freeMoveController != null
                        && freeMoveController.GroundPlatform != null)
                    {
                        aimRideBody = freeMoveController.GroundPlatform.GetComponent<Rigidbody>();
                        if (aimRideBody != null) aimRideOffset = rb.position - aimRideBody.position;
                    }
                    if (aimRideBody != null)
                    {
                        carryVelocity = (aimRideBody.position + aimRideOffset - rb.position) / Time.fixedDeltaTime;
                    }
                }
                else aimRideBody = null;

                rb.linearVelocity = carryVelocity;
                rb.angularVelocity = Vector3.zero;

                if (hasPendingCarry && isStuck && stuckSurfaceNormal.sqrMagnitude > 0.0001f)
                {
                    stuckSurfaceNormal = (pendingCarryRotation * stuckSurfaceNormal).normalized;
                }
                hasPendingCarry = false;
            }

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
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
                rb.linearDamping = queuedDamping;

                if (freeMoveController != null && isGrounded)
                {
                    Vector3 platformCarry = freeMoveController.GroundPlatformVelocity;
                    if (platformCarry.sqrMagnitude > 0.0001f) rb.linearVelocity -= platformCarry;
                }
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);

                if (queuedExtraVelocity.sqrMagnitude > 0.0001f)
                {
                    rb.linearVelocity += queuedExtraVelocity;
                    queuedExtraVelocity = Vector3.zero;
                }
                launchGraceTimer = launchGraceDuration;
                launchStartPosition = transform.position;
                freeMoveController?.FaceLaunchDirection(queuedDirection);

                slamJustFired = currentFlightIsDownward;
                slamForce = queuedForce;
            }

            if (launchGraceTimer > 0f) launchGraceTimer -= Time.fixedDeltaTime;
            if (knockbackTimer > 0f) knockbackTimer -= Time.fixedDeltaTime;
            if (launchLockTimer > 0f) launchLockTimer -= Time.fixedDeltaTime;
            if (hasLaunched) flightElapsedSeconds += Time.fixedDeltaTime;
            if (hasPendingEnemyKnockback)
            {
                rb.linearVelocity = pendingEnemyKnockback;
                hasPendingEnemyKnockback = false;
            }

            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);

            float descentReach = 0f;
            if (freeMoveController != null && freeMoveController.OnMovingPlatform
                && freeMoveController.GroundPlatformVelocity.y < 0f)
            {
                descentReach = -freeMoveController.GroundPlatformVelocity.y * Time.fixedDeltaTime + 0.05f;
            }
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out RaycastHit groundHit,
                transform.rotation, groundCheckDistance + descentReach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            if (isGrounded && !hasLaunched)
            {
                launchesSinceGrounded = 0;
                launchesRemainingOverride = -1;
                exactFlightNoNudge = false;
                flightEnergySpent = 0f;
                if (!infiniteEnergy) ClampEnergyFloor();

                if (!isStuck) wallCarryArmed = false;
            }

            if (!hasLaunched && !isGrounded && !isStuck && rb.linearDamping > plainFallDamping)
            {
                rb.linearDamping = plainFallDamping;
            }

            if (slamJustFired && isGrounded)
            {
                RegisterCrash(groundHit.normal, slamForce, groundHit.collider);
            }

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

            if (isStuck && isGrounded && Vector3.Dot(stuckSurfaceNormal, Vector3.up) >= flatGroundStickThreshold)
            {
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
            }

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

            velocityBeforePhysicsStep = rb.linearVelocity;
        }

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
            DispatchHazardContact(collision);

            if (collision.collider.GetComponentInParent<RestartWall>() != null)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            Enemy enemy = collision.collider.GetComponentInParent<Enemy>();
            FlyingEnemy flyer = collision.collider.GetComponentInParent<FlyingEnemy>();
            TurretEnemy turret = collision.collider.GetComponentInParent<TurretEnemy>();
            if (enemy != null || flyer != null || turret != null)
            {
                if (hasLaunched && !isStuck)
                {

                    float launchSpend = lastLaunchEnergySpent;
                    RegisterCrash(collision.GetContact(0).normal, velocityBeforePhysicsStep.magnitude, collision.collider);
                    isStuck = false;
                    nonStickyReleaseTimer = 0f;
                    rb.useGravity = true;
                    rb.linearDamping = plainFallDamping;

                    if (enemy != null)
                    {
                        if (enemy.CanBeKilledByLaunch)
                        {
                            if (launchSpend >= enemy.MinKillEnergyFraction) { enemy.OnHitByLaunch(); EnemyKilled?.Invoke(); }
                            else enemy.PunishFailedKill();
                        }
                    }

                    else if (flyer != null)
                    {
                        if (flyer.LaunchKillAllowedFor(collision.collider) && launchSpend >= flyer.minKillEnergyFraction - 0.0001f)
                        {
                            flyer.OnHitByLaunch();
                            EnemyKilled?.Invoke();
                        }
                        else
                        {
                            flyer.OnLaunchSurvived();

                            WeakSpotFlyingEnemy weakSpotFlyer = flyer as WeakSpotFlyingEnemy;
                            Collider flyerBody = weakSpotFlyer != null && weakSpotFlyer.weakSpot != null
                                ? weakSpotFlyer.weakSpot
                                : flyer.GetComponent<Collider>();
                            if (flyerBody == null) flyerBody = flyer.GetComponentInChildren<Collider>();
                            if (flyerBody != null)
                            {
                                float halfHeight = boxCollider != null ? boxCollider.bounds.extents.y : 0.5f;

                                Vector3 spotCentre;
                                float spotHalf;
                                if (weakSpotFlyer != null && weakSpotFlyer.weakSpot != null)
                                {
                                    spotCentre = weakSpotFlyer.StunnedWeakSpotCentre();
                                    spotHalf = weakSpotFlyer.WeakSpotHalfExtent();
                                }
                                else
                                {
                                    spotCentre = flyerBody.bounds.center;
                                    spotHalf = flyerBody.bounds.extents.y;
                                }
                                Vector3 perch = new Vector3(
                                    spotCentre.x,
                                    spotCentre.y + spotHalf + halfHeight + 0.05f,
                                    spotCentre.z);
                                transform.position = perch;
                                rb.position = perch;
                                RigidbodyInterpolation interpolationMode = rb.interpolation;
                                rb.interpolation = RigidbodyInterpolation.None;
                                rb.interpolation = interpolationMode;
                            }
                        }
                    }
                    else if (launchSpend >= turret.minKillEnergyFraction - 0.0001f) { turret.OnHitByLaunch(); EnemyKilled?.Invoke(); }
                }
                return;
            }

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

            if (!hasLaunched || isStuck) return;

            if (!currentFlightIsDownward)
            {
                if (launchGraceTimer > 0f) return;
                if (Vector3.Distance(transform.position, launchStartPosition) < minLaunchClearDistance) return;
            }

            RegisterCrash(collision.GetContact(0).normal, velocityBeforePhysicsStep.magnitude, collision.collider);
        }

        public Collider LastCrashSurface { get; private set; }
        public float LastCrashRefund { get; private set; }

        public bool LastCrashWasPound { get; private set; }

        public Vector3 PreCollisionVelocity => velocityBeforePhysicsStep;

        Collider ignoredCheckpointButton;

        Rigidbody aimRideBody;
        Vector3 aimRideOffset;

        void SetIgnoredCheckpointButton(Collider button)
        {
            if (ignoredCheckpointButton == button) return;
            if (ignoredCheckpointButton != null && boxCollider != null && ignoredCheckpointButton.enabled)
            {
                Physics.IgnoreCollision(boxCollider, ignoredCheckpointButton, false);
            }
            ignoredCheckpointButton = button;
            if (ignoredCheckpointButton != null && boxCollider != null)
            {
                Physics.IgnoreCollision(boxCollider, ignoredCheckpointButton, true);
            }
        }

        void UpdateIgnoredCheckpointButton()
        {
            if (isGrounded && IsAimingOrCharging)
            {

                Vector3 reach = (boxCollider != null ? boxCollider.bounds.extents : Vector3.one * 0.5f)
                    + new Vector3(0.35f, 0.7f, 0.35f);
                foreach (Collider candidate in Physics.OverlapBox(transform.position, reach,
                    transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    Checkpoint checkpoint = candidate.GetComponentInParent<Checkpoint>();
                    if (checkpoint != null && checkpoint.buttonCollider == candidate)
                    {
                        SetIgnoredCheckpointButton(candidate);
                        return;
                    }
                }

            }
            else if (isGrounded && !hasLaunched)
            {
                SetIgnoredCheckpointButton(null);
            }
            else if (ignoredCheckpointButton != null)
            {

                Bounds clearance = ignoredCheckpointButton.bounds;
                clearance.Expand(1.2f);
                if (boxCollider != null && !clearance.Intersects(boxCollider.bounds))
                {
                    SetIgnoredCheckpointButton(null);
                }
            }
        }

        void DispatchHazardContact(Collision collision)
        {
            LaserHazard hazard = collision.collider.GetComponent<LaserHazard>();
            if (hazard != null) hazard.TryHit(boxCollider);
        }

        void OnCollisionStay(Collision collision)
        {
            DispatchHazardContact(collision);

            if (hasLaunched && !isStuck && collision.contactCount > 0
                && Vector3.Dot(velocityBeforePhysicsStep, collision.GetContact(0).normal) < -0.5f
                && (collision.collider.GetComponentInParent<Enemy>() != null
                    || collision.collider.GetComponentInParent<FlyingEnemy>() != null
                    || collision.collider.GetComponentInParent<TurretEnemy>() != null))
            {
                OnCollisionEnter(collision);
            }
        }

        void RegisterCrash(Vector3 contactNormal, float crashSpeed, Collider surface)
        {

            if (surface != null && surface.GetComponentInParent<NonStickSurface>() != null) return;

            SetIgnoredCheckpointButton(null);

            LastCrashSurface = surface;
            LastCrashRefund = 0f;
            LastCrashWasPound = lastLaunchWasPound;

            crashEnergySpent = lastLaunchEnergySpent;
            crashEnergySpentFrame = Time.frameCount;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;

            isStuck = true;
            hasLaunched = false;
            launchesSinceGrounded = 0;

            bool groundingCrash = Vector3.Dot(contactNormal, Vector3.up) >= flatGroundStickThreshold;
            launchesRemainingOverride = wallCrashLaunchAllowance > 0 && !groundingCrash
                ? wallCrashLaunchAllowance
                : -1;
            exactFlightNoNudge = false;

            waitingForAimRelease = true;
            if (airAiming) CancelAirAim();

            stuckSurfaceNormal = contactNormal;

            wallCarryArmed = Vector3.Dot(contactNormal, Vector3.up) < 0.7f;
            freeMoveController?.AlignVisualToSurface(stuckSurfaceNormal);

            nonStickyReleaseTimer = 0f;
            TimedStickyPanel timedPanel = surface != null ? surface.GetComponentInParent<TimedStickyPanel>() : null;
            if (timedPanel != null)
            {

                nonStickyReleaseTimer = timedPanel.holdSeconds;
                timedPanel.OnPlayerStuck();
            }
            else if (Vector3.Dot(contactNormal, Vector3.up) < flatGroundStickThreshold)
            {
                StickySurface stickySurface = surface != null ? surface.GetComponentInParent<StickySurface>() : null;

                bool hazardFace = surface != null && surface.GetComponent<LaserHazard>() != null;
                if (hazardFace || stickySurface == null || !stickySurface.sticky)
                {
                    nonStickyReleaseTimer = nonStickyWallStickDuration;
                }
            }

            if (gradualLaunchDrain && !infiniteEnergy && gradualDrainRemaining > 0f)
            {
                energyFraction = Mathf.Max(energyFraction - gradualDrainRemaining, 0f);
                gradualDrainRemaining = 0f;
            }

            bool refundAllowed = surface == null || surface.GetComponentInParent<NoRefundSurface>() == null;
            if (refundAllowed)
            {
                RefundEnergyForCrash();
            }

            if (lastLaunchWasPound)
            {
                transform.position += Vector3.up * groundPoundHopHeight;
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
                poundWindowTimer = groundPoundSlowDuration;
                poundWindowFromPound = true;
                lastLaunchWasPound = false;
                lastLaunchEnergySpent = 0f;
            }

            else if (!groundingCrash && refundAllowed)
            {
                poundWindowTimer = groundPoundSlowDuration;
                poundPendingRefund = lastLaunchEnergySpent;
                poundWindowFromPound = false;
            }

            flightEnergySpent = 0f;

            TargetSphere sphere = surface != null ? surface.GetComponentInParent<TargetSphere>() : null;
            if (sphere != null)
            {
                nonStickyReleaseTimer = nonStickyWallStickDuration;
                sphere.OnHitByCrash();
            }

            if (slowdownMode == SlowdownMode.AimBudget) aimBudgetRemaining = aimBudgetSeconds;

            CrashRegistered?.Invoke(transform.position);
        }

        void RefundEnergyForCrash()
        {
            if (infiniteEnergy)
            {
                energyFraction = 1f;
                return;
            }

            float energyBeforeRefund = energyFraction;

            if (lastLaunchWasPound)
            {
                float flightSpend = flightEnergySpent > 0.0001f ? flightEnergySpent : lastLaunchEnergySpent;
                energyFraction = Mathf.Clamp01(energyFraction + flightSpend * poundFlightRefundMultiplier);

                poundPendingRefund = lastLaunchEnergySpent;
                ClampEnergyFloor();
                LastCrashRefund = Mathf.Max(energyFraction - energyBeforeRefund, 0f);
                return;
            }

            float gain;
            if (lastLaunchWasGrounded)
            {
                gain = lastLaunchEnergySpent * groundedRefundMultiplier;
            }
            else
            {

                gain = lastLaunchEnergySpent * (midairRefundBaseMultiplier + midairRefundSpendFactor * lastLaunchEnergySpent);
            }
            float refunded = Mathf.Clamp01(energyFraction + gain);

            if (ordinaryRefundCeiling < 1f)
            {
                refunded = Mathf.Min(refunded, Mathf.Max(ordinaryRefundCeiling, energyBeforeRefund));
            }
            energyFraction = refunded;
            ClampEnergyFloor();
            LastCrashRefund = Mathf.Max(energyFraction - energyBeforeRefund, 0f);
        }

        float LaunchFlightScaleForCurrentCharge()
        {
            float spend = energyCostPerFullCharge > 0f
                ? Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge)
                : 0f;
            return launchFlightTimeScale + spend * flightTimeScaleEnergyBonus;
        }

        float PredictedFlightRealSeconds(int steps, bool downwardLaunch)
        {
            if (steps <= 0) return 0f;
            steps = Mathf.Min(steps, trajectoryBuffer.Length);

            float dt = Time.fixedDeltaTime;
            float baseScale = LaunchFlightScaleForCurrentCharge();
            float landingY = trajectoryBuffer[steps - 1].y;
            float apexY = trajectoryBuffer[0].y;
            float realSeconds = 0f;

            for (int i = 0; i < steps; i++)
            {
                float y = trajectoryBuffer[i].y;
                if (y > apexY) apexY = y;
                bool falling = i > 0 && y < trajectoryBuffer[i - 1].y;

                float scale = baseScale;
                if (falling && !downwardLaunch)
                {
                    float descentSpan = Mathf.Max(apexY - landingY, 0.01f);
                    float descentProgress = Mathf.Clamp01((apexY - y) / descentSpan);
                    scale *= 1f + Mathf.Lerp(fallSpeedUpStart, fallSpeedUpEnd, descentProgress);
                }

                realSeconds += Mathf.Min(dt, dt / Mathf.Max(scale, 0.01f));
            }
            return realSeconds;
        }

        void ShowLandingPreview(Vector3 initialVelocity, float damping)
        {
            Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;

            bool downwardLaunch = initialVelocity.sqrMagnitude > 0.0001f
                && Vector3.Dot(initialVelocity.normalized, Vector3.down) >= slamDownwardThreshold;

            predictionLeadSeconds = 0f;
            if (predictionHasMovers)
            {
                PredictLandingPoint(transform.position, initialVelocity, damping, out int probeSteps, out _);
                predictionLeadSeconds = PredictedFlightRealSeconds(probeSteps, downwardLaunch);
                predictionSyncFrame = -1;
            }

            Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, damping, out int stepCount, out bool didLand);
            lastPredictedLanding = landingPoint;
            hasValidPredictedLanding = didLand;
            lastTrajectoryStepCount = stepCount;
            lastPredictedFlightSeconds = Mathf.Max(stepCount * Time.fixedDeltaTime, 0.1f);

            lastPredictedFlightRealSeconds = PredictedFlightRealSeconds(stepCount, downwardLaunch);

            bool blockedByStuckSurface = isStuck
                && stuckSurfaceNormal.sqrMagnitude > 0.0001f
                && initialVelocity.sqrMagnitude > 0.0001f
                && Vector3.Dot(initialVelocity.normalized, stuckSurfaceNormal) <= stuckSurfaceLaunchClearance;

            if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
            {

                landingPreview.SetProjectedSpend(energyCostPerFullCharge > 0f
                    ? Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge)
                    : SpendableEnergy());
                landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand,
                    lastPredictedLandingNormal, lastPredictedLandingSource, blockedByStuckSurface);
            }
        }

        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, float damping, out int stepCount, out bool didLand)
        {
            EnsurePredictionClone();

            if (predictionSyncFrame != Time.frameCount)
            {
                SyncPredictionGeometry();
                predictionSyncFrame = Time.frameCount;
            }

            predictionRb.linearDamping = damping;

            bool spawnCached = spawnCacheFrame == Time.frameCount && spawnCacheStart == startPos;
            Vector3 clearanceDir = isStuck && stuckSurfaceNormal.sqrMagnitude > 0.0001f ? stuckSurfaceNormal : Vector3.up;
            Vector3 spawnPos = spawnCached ? spawnCacheResult : startPos + clearanceDir * 0.15f;

            if (!spawnCached && predictionCloneCollider != null)
            {
                const float depenetrationSkin = 0.12f;
                Vector3 originalCloneSize = predictionCloneCollider.size;
                predictionCloneCollider.size = originalCloneSize + Vector3.one * depenetrationSkin;
                foreach (PredictionGeometryProxy entry in geometryProxies)
                {
                    if (entry.proxy == null || !entry.proxy.activeSelf) continue;
                    Collider proxyCollider = entry.proxyBox != null ? (Collider)entry.proxyBox
                        : entry.proxySphere != null ? (Collider)entry.proxySphere
                        : entry.proxyCapsule != null ? (Collider)entry.proxyCapsule
                        : entry.proxyMesh;
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

                if (i >= 2 && predictionRb.linearVelocity.sqrMagnitude < 0.0001f)
                {
                    didLand = true;
                    break;
                }

                if (pos.y < fallResetY) break;
            }

            lastPredictedLandingNormal = predictionStopper != null && predictionStopper.HasContact
                ? predictionStopper.LastContactNormal
                : Vector3.up;
            lastPredictedLandingSource = predictionStopper != null && predictionStopper.HasContact
                ? ResolvePredictionSource(predictionStopper.LastContactCollider)
                : null;

            return landing;
        }

        Collider ResolvePredictionSource(Collider proxyCollider)
        {
            if (proxyCollider == null) return null;
            foreach (PredictionGeometryProxy entry in geometryProxies)
            {
                if (entry.proxy == proxyCollider.gameObject) return entry.source;
            }
            return null;
        }

        void EnsurePredictionClone()
        {
            if (predictionClone != null) return;

            if (!predictionSceneReady)
            {

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

            predictionStopper = predictionClone.AddComponent<PredictionCloneStopper>();
        }

        void BuildPredictionGeometryProxies()
        {
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include);

            foreach (Collider col in colliders)
            {
                if (col == boxCollider) continue;

                Rigidbody colBody = col.attachedRigidbody;
                if (colBody != null && !colBody.isKinematic) continue;

                if (col.GetComponentInParent<AimPreviewIgnored>() != null) continue;

                if (col.isTrigger) continue;

                GameObject proxy = new GameObject("PredictionGeometryProxy");
                SceneManager.MoveGameObjectToScene(proxy, predictionScene);

                PredictionGeometryProxy entry = new PredictionGeometryProxy
                {
                    source = col,
                    proxy = proxy,
                    mover = col.GetComponentInParent<MovingPlatform>(),
                };
                if (col is BoxCollider)
                {
                    entry.proxyBox = proxy.AddComponent<BoxCollider>();
                }
                else if (col is SphereCollider)
                {
                    entry.proxySphere = proxy.AddComponent<SphereCollider>();
                }
                else if (col is CapsuleCollider)
                {

                    entry.proxyCapsule = proxy.AddComponent<CapsuleCollider>();
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

                if (entry.mover != null) predictionHasMovers = true;
                geometryProxies.Add(entry);
                MirrorGeometryProxy(entry);
            }
        }

        class PredictionGeometryProxy
        {
            public Collider source;
            public GameObject proxy;

            public MovingPlatform mover;
            public BoxCollider proxyBox;
            public SphereCollider proxySphere;
            public CapsuleCollider proxyCapsule;
            public MeshCollider proxyMesh;
        }
        readonly List<PredictionGeometryProxy> geometryProxies = new List<PredictionGeometryProxy>();

        float predictionLeadSeconds;
        bool predictionHasMovers;

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

            Vector3 leadOffset = Vector3.zero;
            if (entry.mover != null && predictionLeadSeconds > 0f && entry.mover != GroundPlatform)
            {
                leadOffset = entry.mover.LeadOffset(predictionLeadSeconds);
            }
            entry.proxy.transform.SetPositionAndRotation(sourceTransform.position + leadOffset, sourceTransform.rotation);
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
            else if (entry.proxyCapsule != null && entry.source is CapsuleCollider sourceCapsule)
            {
                if (entry.proxyCapsule.center != sourceCapsule.center) entry.proxyCapsule.center = sourceCapsule.center;
                if (entry.proxyCapsule.radius != sourceCapsule.radius) entry.proxyCapsule.radius = sourceCapsule.radius;
                if (entry.proxyCapsule.height != sourceCapsule.height) entry.proxyCapsule.height = sourceCapsule.height;
                if (entry.proxyCapsule.direction != sourceCapsule.direction) entry.proxyCapsule.direction = sourceCapsule.direction;
            }
            else if (entry.proxyMesh != null && entry.source is MeshCollider sourceMesh)
            {
                if (entry.proxyMesh.sharedMesh != sourceMesh.sharedMesh) entry.proxyMesh.sharedMesh = sourceMesh.sharedMesh;
            }

            bool sourceSolid = entry.source.enabled && entry.source.gameObject.activeInHierarchy;
            if (entry.proxy.activeSelf != sourceSolid) entry.proxy.SetActive(sourceSolid);
        }

        void WriteControlsText()
        {
            const string crashLine =
                "Crashing refunds energy - green STICKY surfaces hold you until you launch,\n" +
                "anything else drops you after a moment (flat ground you can walk off freely)\n";

            if (controlsPanelBody != null)
            {
                controlsPanelBody.text =
                   "WHILE GROUNDED\n" +
                   "Hold Left Bumper / Right Mouse to aim and charge.\n" +
                   "  Mouse: Aim       WASD: Camera\n" +
                   "  Left Stick: Aim  Right Stick: Camera\n" +
                   "  Right Trigger / Left Mouse: Fire\n" +
                   "  A / Space (hold): Charge straight up\n" +
                   "  Release to launch\n\n" +

                   "AIRBORNE\n" +
                   "Hold Left Bumper / Right Mouse to enter first-person aim.\n" +
                   "  Mouse / Right Stick: Aim\n" +
                   "  Mouse Wheel / Right Stick Up/Down: Add or remove energy\n" +
                   "  Blue bar: Energy cost\n" +
                   "  Left Mouse / Right Trigger: Fire\n" +
                   "  A / Space (hold): Charge straight up\n\n" +

                   "GROUND POUND\n" +
                   "Hold X / E to charge. Release to slam straight down.\n" +
                   "  Landing a pound triggers brief slow motion.\n" +
                   "  Aim during slow motion to gain BONUS ENERGY.\n" +
                   "  Orange bar: Bonus energy\n" +
                   "  The shot fires instantly at full charge.\n\n" +

                   "CANCEL CHARGE\n" +
                   "Release Left Bumper / Right Mouse.\n\n" +

                   crashLine +

                   "Mouse / Right Stick: Camera\n" +
                   "Start / Options / Esc: Pause";
            }
        }
    }
}

