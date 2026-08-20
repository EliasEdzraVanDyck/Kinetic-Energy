using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Level;
using KineticEnergy.UI;

namespace KineticEnergy.Player
{

    public enum EnergyControlMode
    {
        Standard,
        Automatic,
        CircleCrank,
        DedicatedButtons,
        ReverseDirection,
    }

    public enum ControlScheme
    {
        LaunchInstantly,
        HoldRelease,
        AnalogPressure,
        StickAim,

        Mixed,

        DefyGravity,

        FastPaced

    }

    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeController : MonoBehaviour
    {
        [Header("Launch Force")]

        public float minLaunchForce = 45f;
        public float maxLaunchForce = 110f;
        public float maxChargeTime = 1.5f;

        [Header("Energy")]

        [Range(0f, 1f)] public float startingEnergyFraction = 0.2f;

        public float energyCostPerFullCharge = 1f;

        public float energyGainPerSpeed = 0.03f;
        public float energyGainSpeedBonus = 0.01f;

        public bool refundSpentEnergyOnly = false;

        [Range(0f, 1f)] public float minEnergyGainPerCrash = 0.05f;

        [Range(0f, 1f)] public float minEnergyReserve = 0.05f;

        public EnergyMeterController energyMeter;

        public float chargeAccumulationRate = 0.3f;

        [Header("Defy Gravity Scheme")]

        public float minDefyGravityDuration = 0.4f;
        public float maxDefyGravityDuration = 1.5f;

        public float maxDefyGravitySpeed = 70f;

        public float defyGravityFallDamping = 0.2f;

        [Header("Fast Paced Scheme")]

        public float fastPacedMinDamping = 2.8f;
        public float fastPacedMaxDamping = 1.0f;

        public float fastPacedRefundMultiplier = 1.2f;

        public float fastPacedFlightTimeScale = 1.5f;
        public InputActionReference fastPacedAimAction;
        public InputActionReference fastPacedLaunchAction;

        [Header("Energy Control (EnergyRegulation scenes)")]

        public EnergyControlMode energyControlMode = EnergyControlMode.Standard;

        public float crankChargePerRevolution = 0.5f;
        [Range(0f, 1f)] public float crankDeadzone = 0.9f;

        public float buttonChargeRate = 0.5f;
        public float wheelChargeStep = 0.05f;

        public float autoAimMaxDistance = 400f;
        public int autoSearchIterations = 6;

        [Range(0f, 0.5f)] public float autoChargeFailsafe = 0.05f;

        [Header("Energy Economy (EnergyEconomy1)")]

        public bool lastLaunchRefundEconomy = false;

        public bool westAirDownLaunch = false;

        public float groundPoundRefundMultiplier = 1.2f;

        public float groundPoundMinRefund = 0.1f;

        public float upDownChargeSpeedMultiplier = 1.5f;

        public float midairRefundSpendFactor = 3f;

        public bool chainLaunchAccumulation = false;

        [Header("Energy Economy 4")]

        public bool groundPoundBoostEconomy = false;
        public float groundPoundBoostMultiplier = 1.5f;
        public float groundPoundHopHeight = 0.1f;
        public float groundPoundSlowDuration = 0.3f;

        public float groundPoundChargeBaseSpeed = 1.5f;
        public float groundPoundChargeSpeedGrowth = 1f;

        public float minLaunchDamping = 2.8f;
        public float maxLaunchDamping = 1.0f;

        [Header("Aiming")]
        [Range(0f, 1f)] public float aimDeadzone = 0.15f;
        public float aimRotationSpeed = 90f;
        public float minAimPitch = -80f;
        public float maxAimPitch = 80f;

        public float defaultAimPitch = -30f;
        public Transform cameraTransform;

        public KineticEnergy.Camera.ThirdPersonOrbitCamera cameraOrbit;
        public AimArrowIndicator aimArrow;

        [Header("Landing")]
        public int maxPredictionSteps = 3000;
        public float previewLineHeight = 0.65f;
        public float groundCheckDistance = 0.6f;
        public LandingPreviewController landingPreview;

        [Header("Controls Text")]

        public Text controlsHintLabel;
        public Text controlsPanelBody;

        [Header("Fall Reset")]
        public float fallResetY = -30f;

        [Header("Launch Grace")]

        public float launchGraceDuration = 0.15f;

        public float minLaunchClearDistance = 2f;

        [Header("Crash Stick")]

        [Range(0f, 1f)] public float flatGroundStickThreshold = 0.9f;

        [Range(0f, 1f)] public float slamDownwardThreshold = 0.7f;

        public int stuckOnGroundTickThreshold = 10;

        [Header("Sticky Walls")]

        public bool stickyWallsOnly = false;
        public float nonStickyWallStickDuration = 0.3f;

        [Header("Launch Limit")]

        public int maxLaunchesPerFlight = 0;

        [Header("Input")]
        public InputActionReference moveAction;
        public InputActionReference launchAction;
        public InputActionReference fireAction;

        public InputActionReference selectClassicSchemeAction;
        public InputActionReference selectHoldReleaseSchemeAction;
        public InputActionReference selectAnalogSchemeAction;
        public InputActionReference selectNoneAction;

        [Header("Scheme Restriction")]

        public bool alternateSchemesEnabled = false;

        public bool schemeSwitchingEnabled = false;

        public bool mixedFastPacedAir = false;

        public bool mouseAirControls = false;

        public bool groundedAimWithMouse = false;
        public float groundedMouseAimSensitivity = 0.15f;

        public float wasdCameraTurnMultiplier = 1.5f;

        public bool airUsesGroundedAim = false;

        [Header("Testing")]

        public float gravity = -30f;

        public float chargeTimeScale = 0.75f;

        public float launchFlightTimeScale = 2f;

        [Header("FastPacedLevel Tweaks")]

        public bool disableAirNudge = false;

        public bool aimWithEitherStick = false;

        [Header("Stick Aim Scheme")]

        public InputActionReference trailToggleAction;

        public InputActionReference upLaunchAction;

        public InputActionReference cancelChargeAction;

        public float stickAimUpAngle = 80f;

        public float stickAimDownAngle = 60f;

        public float downLaunchDamping = 0.2f;

        [Range(0f, 1f)] public float stickAimDeadzone = 0.9f;

        public float stickAimForwardAngle = 30f;

        public float stickAimForwardNeutralAngle = 5f;

        public FacingArrowIndicator facingArrow;

        Rigidbody rb;
        BoxCollider boxCollider;
        bool isAiming;
        bool waitingForLtRelease;
        bool hasLaunched;

        bool currentFlightIsDownward;

        Vector3 velocityBeforePhysicsStep;

        int groundedTicksSinceLaunch;
        bool isGrounded;

        bool isStuck;

        Vector3 stuckSurfaceNormal;

        float nonStickyReleaseTimer;

        int launchesSinceGrounded;

        bool mixedAirAiming;

        bool fastPacedFlightExact;

        float crankPreviousAngle;
        bool crankHasPreviousAngle;
        bool reverseChargingDown;

        bool chargeDisplayInsufficient;

        bool autoAimForced;
        bool positioningAimUsedThisFlight;

        bool aimButtonSpent;
        EnergyCrankUI energyCrankUI;
        float chargeTime;
        float aimYaw;
        float aimPitch;

        [SerializeField] ControlScheme controlScheme = ControlScheme.StickAim;

        float energyFraction;

        float lastLaunchEnergySpent;

        bool lastLaunchWasGrounded;
        bool lastLaunchWasAirDown;

        Vector3 carriedLaunchVelocity;
        float flightEnergySpent;
        bool wasFrozenLastTick;

        float groundPoundWindowTimer;

        bool groundPoundAimNoGravity;

        float groundPoundPendingRefund;

        float groundPoundBoostExtra;

        float groundPoundChargeHoldTime;

        public ControlScheme CurrentScheme => controlScheme;

        public float EnergyFraction => energyFraction;
        public bool IsStuck => isStuck;
        public bool IsGrounded => isGrounded;

        public bool IsAimingOrCharging => isAiming || fastPacedAiming || fastPacedCharging || mixedAirAiming
            || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None;

        public void SetControlScheme(ControlScheme scheme)
        {
            controlScheme = scheme;
        }

        public bool AllowGroundedMovement => !isAiming && !hasLaunched && !isStuck && !mixedAirAiming
            && stickAimChargeType == StickAimChargeType.None && defyGravityChargeType == DefyGravityFlightType.None && !fastPacedCharging

            && !fastPacedAiming;

        public bool AllowAirborneNudge => !isAiming && !isStuck && !mixedAirAiming && defyGravityFlightTimer <= 0f && launchGraceTimer <= 0f
            && stickAimChargeType == StickAimChargeType.None && defyGravityChargeType == DefyGravityFlightType.None && !fastPacedCharging

            && !(mixedFastPacedAir && fastPacedAiming)

            && !fastPacedFlightExact

            && !disableAirNudge;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;
        float queuedDamping;

        float queuedDefyGravityDuration;
        float launchGraceTimer;
        Vector3 launchStartPosition;

        enum StickAimChargeType { None, Up, Down, Forward }
        StickAimChargeType stickAimChargeType = StickAimChargeType.None;

        enum DefyGravityFlightType { None, Forward, Up, Down }
        DefyGravityFlightType defyGravityChargeType = DefyGravityFlightType.None;

        float defyGravityFlightTimer;
        Vector3 defyGravityFlightVelocity;

        bool fastPacedAiming;

        bool fastPacedCharging;

        Vector3 stickAimLastFlatDirection;
        bool stickAimHasAimed;

        Vector3[] trajectoryBuffer;

        Vector3 lastPredictedLanding;
        bool hasPredictedLanding;

        bool hasValidPredictedLanding;

        GameObject predictionClone;
        Rigidbody predictionRb;
        BoxCollider predictionCloneCollider;
        PredictionCloneStopper predictionStopper;

        int predictionSyncFrame = -1;
        int spawnCacheFrame = -1;
        Vector3 spawnCacheStart;
        Vector3 spawnCacheResult;

        Vector3 lastAutoTarget;
        float lastAutoSolvedCharge = -1f;
        int lastAutoSolveFrame = -1000;

        Vector3 lastPredictedLandingNormal = Vector3.up;
        Scene predictionScene;
        PhysicsScene predictionPhysicsScene;
        bool predictionSceneReady;
        static int predictionSceneCounter;

        KineticCubeControllerFreeMove freeMoveController;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            boxCollider = GetComponent<BoxCollider>();
            trajectoryBuffer = new Vector3[Mathf.Max(maxPredictionSteps, 1)];

            freeMoveController = GetComponent<KineticCubeControllerFreeMove>();
            energyCrankUI = GetComponent<EnergyCrankUI>();
            ApplyGravity();
            energyFraction = startingEnergyFraction;

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
            UpdateSchemeLabel();
            ApplyGamepadBlock();
        }

        public void ApplyGamepadBlock()
        {
            InputActionAsset actionAsset = moveAction != null && moveAction.action != null && moveAction.action.actionMap != null
                ? moveAction.action.actionMap.asset
                : null;
            if (actionAsset == null) return;

            if (groundedAimWithMouse)
            {
                InputControlScheme? keyboardScheme = actionAsset.FindControlScheme("Keyboard&Mouse");
                actionAsset.bindingMask = keyboardScheme.HasValue
                    ? InputBinding.MaskByGroup(keyboardScheme.Value.bindingGroup)
                    : (InputBinding?)null;
            }
            else
            {
                actionAsset.bindingMask = null;
            }
        }

        void OnDestroy()
        {
            if (predictionClone != null) Destroy(predictionClone);
            if (predictionSceneReady && predictionScene.IsValid()) SceneManager.UnloadSceneAsync(predictionScene);
        }

        void OnEnable()
        {
            moveAction?.action?.Enable();
            launchAction?.action?.Enable();
            fireAction?.action?.Enable();
            selectClassicSchemeAction?.action?.Enable();
            selectHoldReleaseSchemeAction?.action?.Enable();
            selectAnalogSchemeAction?.action?.Enable();
            selectNoneAction?.action?.Enable();
            trailToggleAction?.action?.Enable();
            upLaunchAction?.action?.Enable();
            fastPacedAimAction?.action?.Enable();
            fastPacedLaunchAction?.action?.Enable();
        }

        void OnDisable()
        {
            moveAction?.action?.Disable();
            launchAction?.action?.Disable();
            fireAction?.action?.Disable();
            selectClassicSchemeAction?.action?.Disable();
            selectHoldReleaseSchemeAction?.action?.Disable();
            selectAnalogSchemeAction?.action?.Disable();
            selectNoneAction?.action?.Disable();
            trailToggleAction?.action?.Disable();
            upLaunchAction?.action?.Disable();
            fastPacedAimAction?.action?.Disable();
            fastPacedLaunchAction?.action?.Disable();
        }

        void Update()
        {

            {
                bool paused = Time.timeScale <= 0f;
                Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = paused;
            }

            if (Time.timeScale <= 0f) return;

            if (aimButtonSpent && !AimButtonHeld()) aimButtonSpent = false;

            if (groundPoundWindowTimer > 0f)
            {
                groundPoundWindowTimer -= Time.unscaledDeltaTime;

                if (groundPoundWindowTimer <= 0f) groundPoundPendingRefund = 0f;
            }

            if (transform.position.y < fallResetY)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            if (controlScheme != ControlScheme.StickAim && controlScheme != ControlScheme.Mixed) HandlePreviewModeSwitch();
            HandleTrailToggle();

            if (facingArrow != null)
            {

                bool showFacingArrow = controlScheme == ControlScheme.StickAim;
                facingArrow.SetVisible(showFacingArrow);
                facingArrow.SetFacingYaw(freeMoveController != null ? freeMoveController.FacingYaw : 0f);
            }

            ApplyChargeTimeScale();

            if (cameraOrbit != null)
            {

                bool energyModeOwnsSticks =
                    (energyControlMode == EnergyControlMode.CircleCrank && (fastPacedAiming || fastPacedCharging))
                    || (energyControlMode == EnergyControlMode.DedicatedButtons && fastPacedCharging);

                bool moveIsGamepadDriven = moveAction != null && moveAction.action != null
                    && moveAction.action.activeControl != null && moveAction.action.activeControl.device is Gamepad;
                bool aimWithMoveStick = energyModeOwnsSticks
                    || (mixedFastPacedAir && controlScheme == ControlScheme.Mixed && (fastPacedAiming || fastPacedCharging))

                    || (groundedAimWithMouse && isAiming && !moveIsGamepadDriven);
                Vector2 aimStick = aimWithMoveStick && moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;
                if (aimStick.sqrMagnitude < aimDeadzone * aimDeadzone) aimStick = Vector2.zero;

                if (groundedAimWithMouse && isAiming && !moveIsGamepadDriven) aimStick *= wasdCameraTurnMultiplier;

                if (energyControlMode == EnergyControlMode.CircleCrank && (fastPacedAiming || fastPacedCharging) && !moveIsGamepadDriven)
                {
                    aimStick = Vector2.zero;
                }

                if (aimWithEitherStick && !aimWithMoveStick && moveIsGamepadDriven
                    && GamepadLookValue().sqrMagnitude < aimDeadzone * aimDeadzone)
                {
                    Vector2 leftStick = moveAction != null && moveAction.action != null
                        ? moveAction.action.ReadValue<Vector2>()
                        : Vector2.zero;
                    if (leftStick.sqrMagnitude > aimDeadzone * aimDeadzone)
                    {
                        aimWithMoveStick = true;
                        aimStick = leftStick;
                    }
                }

                cameraOrbit.SetAimStickOverride(aimWithMoveStick, aimStick);

                cameraOrbit.SetMouseLookSuppressed(groundedAimWithMouse && isAiming);

                cameraOrbit.SetIgnoreSlowMo(westAirDownLaunch
                    && (stickAimChargeType == StickAimChargeType.Up || stickAimChargeType == StickAimChargeType.Down));

                bool framingAim = !isGrounded && hasValidPredictedLanding && (fastPacedAiming || fastPacedCharging);
                cameraOrbit.SetTrajectoryFraming(framingAim, lastPredictedLanding);
            }

            if (energyMeter != null)
            {
                energyMeter.SetEnergy(energyFraction);
                bool charging = isAiming || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None || fastPacedCharging

                    || (fastPacedAiming && energyControlMode != EnergyControlMode.Standard);
                energyMeter.SetCharge(ChargeFraction(), charging);
                energyMeter.SetChargeWarning(chargeDisplayInsufficient);

                energyMeter.SetBonus(
                    energyFraction + groundPoundPendingRefund * (groundPoundBoostMultiplier - 1f),
                    groundPoundBoostEconomy && groundPoundPendingRefund > 0f && groundPoundWindowTimer > 0f);
            }

            switch (controlScheme)
            {
                case ControlScheme.StickAim:
                    UpdateStickAimChargeScheme();
                    return;
                case ControlScheme.Mixed:
                    UpdateMixedScheme();
                    return;
                case ControlScheme.DefyGravity:
                    UpdateDefyGravityScheme();
                    return;
                case ControlScheme.FastPaced:
                    UpdateFastPacedScheme();
                    return;
            }

            UpdateChargeBasedScheme();
        }

        void ApplyChargeTimeScale()
        {

            bool airborneAimSlow = !isGrounded && (isAiming || fastPacedAiming || mixedAirAiming || (AimButtonHeld() && !aimButtonSpent));
            bool charging = airborneAimSlow
                || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None
                || fastPacedCharging

                || groundPoundWindowTimer > 0f;

            bool fastPacedInFlight = controlScheme == ControlScheme.FastPaced && hasLaunched;

            float flightScale = fastPacedInFlight ? fastPacedFlightTimeScale : (hasLaunched ? launchFlightTimeScale : 1f);
            Time.timeScale = charging ? chargeTimeScale : flightScale;
        }

        bool AimButtonHeld()
        {
            if (launchAction != null && launchAction.action != null && launchAction.action.IsPressed()) return true;
            if (fastPacedAimAction != null && fastPacedAimAction.action != null && fastPacedAimAction.action.IsPressed()) return true;
            return false;
        }

        void UpdateChargeBasedScheme()
        {
            bool ltIsPressed = launchAction != null && launchAction.action != null && launchAction.action.IsPressed();
            bool cancelPressed = cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame();

            if (isAiming && cancelPressed)
            {
                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
                waitingForLtRelease = true;
                return;
            }

            if (waitingForLtRelease)
            {
                if (!ltIsPressed) waitingForLtRelease = false;
                return;
            }

            bool canStartNewAim = energyFraction > 0f && CanStartNewLaunch();
            bool ltHeld = isAiming ? ltIsPressed : (ltIsPressed && canStartNewAim);

            if (ltHeld)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;

                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    SeedAimFromCamera();
                    aimArrow?.SetVisible(true);
                    landingPreview?.SetVisible(true);
                }

                bool rtHeld = fireAction != null && fireAction.action != null && fireAction.action.IsPressed();
                bool rtPressed = fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();
                bool rtReleased = fireAction != null && fireAction.action != null && fireAction.action.WasReleasedThisFrame();
                float rtAnalogValue = fireAction != null && fireAction.action != null ? fireAction.action.ReadValue<float>() : 0f;

                bool launchNow;

                switch (controlScheme)
                {
                    case ControlScheme.LaunchInstantly:
                    case ControlScheme.Mixed:

                        AccumulateCharge();
                        launchNow = rtPressed;
                        break;

                    case ControlScheme.AnalogPressure:

                        chargeTime = Mathf.Min(Mathf.Clamp01(rtAnalogValue) * maxChargeTime, EnergyChargeCeiling());
                        launchNow = false;
                        break;

                    default:

                        if (rtHeld) AccumulateCharge();
                        launchNow = rtReleased;
                        break;
                }

                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;

                float aimDt = Time.unscaledDeltaTime;
                if (groundedAimWithMouse && Mouse.current != null)
                {

                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                    aimYaw = Mathf.Repeat(aimYaw + mouseDelta.x * groundedMouseAimSensitivity, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - mouseDelta.y * groundedMouseAimSensitivity, minAimPitch, maxAimPitch);
                }

                bool moveIsGamepad = moveAction != null && moveAction.action != null
                    && moveAction.action.activeControl != null && moveAction.action.activeControl.device is Gamepad;
                if ((!groundedAimWithMouse || moveIsGamepad) && stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {
                    aimYaw = Mathf.Repeat(aimYaw + stick.x * aimRotationSpeed * aimDt, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - stick.y * aimRotationSpeed * aimDt, minAimPitch, maxAimPitch);
                }

                Vector3 dir = AimDirection();
                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(dir, chargeFraction);

                float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);

                float previewDamping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);

                Vector3 initialVelocity = rb.linearVelocity + dir * previewForce / rb.mass;
                Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, previewDamping, out int stepCount, out bool didLand);
                lastPredictedLanding = landingPoint;
                hasPredictedLanding = true;
                hasValidPredictedLanding = didLand;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand, lastPredictedLandingNormal);
                }

                if (launchNow)
                {
                    QueueLaunch(dir, previewForce, previewDamping);

                    isAiming = false;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);
                    waitingForLtRelease = true;
                }
            }
            else if (isAiming)
            {

                bool analogLaunch = controlScheme == ControlScheme.AnalogPressure
                    && fireAction != null && fireAction.action != null && fireAction.action.IsPressed();

                if (analogLaunch)
                {
                    float rtAnalogValue = fireAction.action.ReadValue<float>();
                    chargeTime = Mathf.Min(Mathf.Clamp01(rtAnalogValue) * maxChargeTime, EnergyChargeCeiling());
                    float chargeFraction = ChargeFraction();

                    QueueLaunch(AimDirection(), Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction), Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction));
                    waitingForLtRelease = true;
                }

                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }
        }

        void UpdateMixedScheme()
        {
            if (isAiming)
            {

                bool upConvertPressed = (upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame())
                    || (mouseAirControls && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);
                if (upConvertPressed)
                {
                    float carriedCharge = chargeTime;
                    isAiming = false;
                    waitingForLtRelease = true;
                    StartStickAimCharge(StickAimChargeType.Up);
                    chargeTime = carriedCharge;
                    UpdateStickAimChargeScheme();
                    return;
                }
                UpdateChargeBasedScheme();
            }
            else if (stickAimChargeType != StickAimChargeType.None)
            {
                UpdateStickAimChargeScheme();
            }
            else if (mixedFastPacedAir && (fastPacedAiming || fastPacedCharging))
            {

                if (isGrounded && energyControlMode != EnergyControlMode.Automatic) CancelFastPacedAim();
                else UpdateFastPacedScheme();
            }
            else if (isGrounded || airUsesGroundedAim)
            {

                if (mixedFastPacedAir && energyControlMode == EnergyControlMode.Automatic)
                {
                    UpdateFastPacedScheme();
                    return;
                }

                bool upPressed = energyFraction > 0f && ((upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame())
                    || (mouseAirControls && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame));
                if (upPressed) UpdateStickAimChargeScheme();
                else UpdateChargeBasedScheme();
            }
            else if (mixedFastPacedAir)
            {

                bool groundPoundPressed = (selectClassicSchemeAction != null && selectClassicSchemeAction.action != null
                        && selectClassicSchemeAction.action.WasPressedThisFrame())

                    || (mouseAirControls && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);
                if (westAirDownLaunch && energyFraction > 0f && CanStartNewLaunch() && groundPoundPressed)
                {
                    StartStickAimCharge(StickAimChargeType.Down);
                    return;
                }

                UpdateFastPacedScheme();
            }
            else
            {
                UpdateMixedAirScheme();
            }
        }

        void UpdateMixedAirScheme()
        {
            bool ltHeld = launchAction != null && launchAction.action != null && launchAction.action.IsPressed();

            if (waitingForLtRelease)
            {
                if (!ltHeld) waitingForLtRelease = false;
                return;
            }

            if (ltHeld && energyFraction > 0f && CanStartNewLaunch())
            {
                if (!mixedAirAiming)
                {
                    mixedAirAiming = true;

                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                UpdateStickAimChargeScheme();
            }
            else
            {
                mixedAirAiming = false;
            }
        }

        InputActionReference DownChargeActionForCurrentScheme()
        {
            return controlScheme == ControlScheme.Mixed ? selectClassicSchemeAction : launchAction;
        }

        bool CanStartNewLaunch()
        {
            return maxLaunchesPerFlight <= 0 || launchesSinceGrounded < maxLaunchesPerFlight;
        }

        void FixedUpdate()
        {

            bool frozenNow = isAiming || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None
                || isStuck || fastPacedCharging || mixedAirAiming || fastPacedAiming

                || (groundPoundBoostEconomy && groundPoundWindowTimer > 0f);
            if (frozenNow)
            {

                if (!wasFrozenLastTick && !isStuck) carriedLaunchVelocity = rb.linearVelocity;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            wasFrozenLastTick = frozenNow;

            bool slamJustFired = false;
            float slamForce = 0f;

            if (launchQueued)
            {
                launchQueued = false;
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
                rb.linearDamping = queuedDamping;

                if (chainLaunchAccumulation) rb.linearVelocity = carriedLaunchVelocity;
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);
                launchGraceTimer = launchGraceDuration;
                launchStartPosition = transform.position;
                freeMoveController?.FaceLaunchDirection(queuedDirection);

                if (queuedDefyGravityDuration > 0f)
                {
                    defyGravityFlightTimer = queuedDefyGravityDuration;
                    defyGravityFlightVelocity = queuedDirection * (queuedForce / rb.mass);
                }

                slamJustFired = currentFlightIsDownward;
                slamForce = queuedForce;
            }

            if (defyGravityFlightTimer > 0f)
            {
                rb.linearVelocity = defyGravityFlightVelocity;
                rb.angularVelocity = Vector3.zero;
                defyGravityFlightTimer -= Time.fixedDeltaTime;
            }

            if (launchGraceTimer > 0f) launchGraceTimer -= Time.fixedDeltaTime;

            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out RaycastHit groundHit, transform.rotation, groundCheckDistance);

            if (isGrounded && !hasLaunched)
            {
                launchesSinceGrounded = 0;
                fastPacedFlightExact = false;
                flightEnergySpent = 0f;
                carriedLaunchVelocity = Vector3.zero;
                ClampEnergyFloor();
            }

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

            if (hasLaunched && isGrounded && defyGravityFlightTimer <= 0f)
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
                return;
            }

            if (mixedFastPacedAir && energyControlMode == EnergyControlMode.Automatic
                && hasLaunched && !positioningAimUsedThisFlight
                && other.GetComponentInParent<PositioningTarget>() != null)
            {
                positioningAimUsedThisFlight = true;
                autoAimForced = true;
                aimButtonSpent = false;
                if (!fastPacedAiming)
                {
                    fastPacedAiming = true;
                    cameraOrbit?.SetFirstPersonMode(!isGrounded);
                }
            }
        }

        void OnCollisionEnter(Collision collision)
        {

            if (collision.collider.GetComponentInParent<RestartWall>() != null)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            BreakableCrackWall breakable = collision.collider.GetComponentInParent<BreakableCrackWall>();
            if (breakable != null && hasLaunched && currentFlightIsDownward)
            {
                breakable.Smash();
                rb.linearVelocity = velocityBeforePhysicsStep;
                return;
            }

            if (!hasLaunched || isStuck) return;

            if (!currentFlightIsDownward)
            {
                if (launchGraceTimer > 0f) return;

                if (Vector3.Distance(transform.position, launchStartPosition) < minLaunchClearDistance) return;
            }

            if (hasPredictedLanding)
            {
                Vector3 error = transform.position - lastPredictedLanding;
                Debug.Log($"LandingCheck: predicted={lastPredictedLanding}, actual={transform.position}, error=(x:{error.x:F2}, y:{error.y:F2}, z:{error.z:F2}), distance={error.magnitude:F2}m");
                hasPredictedLanding = false;
            }

            RegisterCrash(collision.GetContact(0).normal, velocityBeforePhysicsStep.magnitude, collision.collider);
        }

        void RegisterCrash(Vector3 contactNormal, float crashSpeed, Collider surface)
        {

            if (surface != null && surface.GetComponentInParent<NonStickSurface>() != null) return;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;

            isStuck = true;
            hasLaunched = false;
            defyGravityFlightTimer = 0f;
            launchesSinceGrounded = 0;
            mixedAirAiming = false;
            fastPacedFlightExact = false;
            carriedLaunchVelocity = Vector3.zero;

            waitingForLtRelease = true;
            if (mixedFastPacedAir && (fastPacedAiming || fastPacedCharging)) CancelFastPacedAim();

            stuckSurfaceNormal = contactNormal;
            freeMoveController?.AlignVisualToSurface(stuckSurfaceNormal);

            nonStickyReleaseTimer = 0f;
            if (stickyWallsOnly && Vector3.Dot(contactNormal, Vector3.up) < flatGroundStickThreshold)
            {
                StickySurface stickySurface = surface != null ? surface.GetComponentInParent<StickySurface>() : null;
                if (stickySurface == null || !stickySurface.sticky)
                {
                    nonStickyReleaseTimer = nonStickyWallStickDuration;
                }
            }

            if (controlScheme == ControlScheme.FastPaced)
            {
                cameraOrbit?.SetUpVector(stuckSurfaceNormal);
            }

            if (surface == null || surface.GetComponentInParent<BreakableCrackWall>() == null)
            {
                GainEnergyFromCrash(crashSpeed);
            }

            if (groundPoundBoostEconomy && lastLaunchWasAirDown)
            {
                transform.position += Vector3.up * groundPoundHopHeight;
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
                groundPoundWindowTimer = groundPoundSlowDuration;

                lastLaunchWasAirDown = false;
                lastLaunchEnergySpent = 0f;
            }

            flightEnergySpent = 0f;

            LaunchTarget target = surface != null ? surface.GetComponentInParent<LaunchTarget>() : null;
            if (target != null)
            {
                nonStickyReleaseTimer = nonStickyWallStickDuration;
                target.Hit();
            }
        }

        void HandlePreviewModeSwitch()
        {
            if (schemeSwitchingEnabled && selectClassicSchemeAction != null && selectClassicSchemeAction.action != null && selectClassicSchemeAction.action.WasPressedThisFrame())
            {
                controlScheme = ControlScheme.LaunchInstantly;
                UpdateSchemeLabel();
            }
            else if (alternateSchemesEnabled && selectHoldReleaseSchemeAction != null && selectHoldReleaseSchemeAction.action != null && selectHoldReleaseSchemeAction.action.WasPressedThisFrame())
            {
                controlScheme = ControlScheme.HoldRelease;
                UpdateSchemeLabel();
            }
            else if (alternateSchemesEnabled && selectAnalogSchemeAction != null && selectAnalogSchemeAction.action != null && selectAnalogSchemeAction.action.WasPressedThisFrame())
            {
                controlScheme = ControlScheme.AnalogPressure;
                UpdateSchemeLabel();
            }
        }

        void UpdateSchemeLabel()
        {
            if (landingPreview != null && landingPreview.modeLabel != null)
            {
                landingPreview.modeLabel.text = "";
            }

            UpdateControlsText();
        }

        void UpdateControlsText()
        {

            const string trailToggleLine = "Show/Hide Trail: Right Bumper\n";
            const string trailToggleLinePanel = "Right Bumper - Show/hide the landing-preview trail\n";

            string switchLine = schemeSwitchingEnabled ? "Switch Scheme: Dpad (hold)\n" : "";
            string switchLinePanel = schemeSwitchingEnabled
                ? "Dpad (hold) - Open a radial menu to pick a scheme directly\n"
                : "";

            string stuckLine = stickyWallsOnly
                ? "Crashing refunds energy - green STICKY walls hold you until you launch, anything else drops you after a moment\n"
                : "Crashing into anything sticks you in place and refunds energy - walk away freely on solid ground, or launch again to break free of a wall\n";
            string stuckLinePanel = stickyWallsOnly
                ? "Crashing into anything stops you dead and refunds energy, more than the\n" +
                  "  charge cost, scaling up with how fast you were going. Only the green\n" +
                  "  STICKY walls hold you there until you launch again - any other wall or\n" +
                  "  ceiling lets go after a brief moment and drops you back into gravity.\n" +
                  "  On flat ground you can always just walk away.\n"
                : "Crashing into anything (any surface) stops you dead and sticks you there -\n" +
                  "  it also refunds energy, more than the charge cost, scaling up with how\n" +
                  "  fast you were going. On a floor or ceiling you can just walk away\n" +
                  "  whenever, energy or not; against a wall you stay stuck until you launch.\n";

            if (controlsHintLabel != null)
            {
                controlsHintLabel.text = controlScheme switch
                {
                    ControlScheme.Mixed when mixedFastPacedAir =>
                        "Move (on the ground): Left Stick / WASD\n" +
                        "Grounded: Left Trigger to aim+charge, Right Trigger to launch - or hold\n" +
                        "  South to charge an Up launch (release to fire)\n" +
                        "Airborne: hold Right Mouse / Left Trigger to aim (first person), then\n" +
                        "  hold Left Mouse / Right Trigger to charge - release to fire where\n" +
                        "  you're looking. Release Right Mouse to cancel and return to 3rd person\n" +
                        stuckLine +
                        "Camera: Mouse / Right Stick\n" +
                        trailToggleLine +
                        switchLine +
                        "Pause: Start / Options / Esc",
                    ControlScheme.StickAim =>
                        "Move (on the ground): Left Stick\n" +
                        "Nudge (in the air): Left Stick\n" +
                        "Hold South / Left Trigger / Right Trigger: charge Up / Down / Forward\n" +
                        "Longer hold launches further and costs more energy, release to fire\n" +
                        "Left Bumper cancels\n" +
                        stuckLine +
                        "Camera: Right Stick\n" +
                        trailToggleLine +
                        switchLine +
                        "Pause: Start / Options / Esc",
                    ControlScheme.Mixed =>
                        "Move (on the ground): Left Stick\n" +
                        "Grounded: Left Trigger to aim+charge, Right Trigger to launch - or hold\n" +
                        "  South to charge an Up launch (release to fire)\n" +
                        "Airborne: hold Left Trigger to aim (you freeze in place), then hold\n" +
                        "  Right Trigger / South / West to charge Forward / Up / Down - release\n" +
                        "  to fire, tilt the Left Stick to angle it. Letting go of Left Trigger\n" +
                        "  stops aiming without firing\n" +
                        "Left Bumper cancels either\n" +
                        stuckLine +
                        "Camera: Right Stick\n" +
                        trailToggleLine +
                        switchLine +
                        "Pause: Start / Options / Esc",
                    ControlScheme.DefyGravity =>
                        "Move (on the ground): Left Stick\n" +
                        "Hold Right Trigger / Left Trigger / South: charge a Forward / Up / Down\n" +
                        "  flight - aim Forward with the Left Stick\n" +
                        "Gravity is off while charging and during the flight, back on once it ends\n" +
                        "Left Bumper cancels\n" +
                        stuckLine +
                        "Camera: Right Stick\n" +
                        trailToggleLine +
                        switchLine +
                        "Pause: Start / Options / Esc",
                    ControlScheme.FastPaced =>
                        "Move/Nudge: WASD (or equivalent) / Left Stick\n" +
                        "Look / Camera: Mouse / Right Stick\n" +
                        "Aim (first person): Right Mouse / Left Trigger (hold)\n" +
                        "Hold Left Mouse / Right Trigger to charge, release to launch where you're looking\n" +
                        "Longer charge = further shot, and the camera zooms in to help you judge it\n" +
                        "Release Right Mouse to return to 3rd person\n" +
                        "Gravity is off in this level\n" +
                        stuckLine +
                        "Pause: Start / Options / Esc",
                    _ =>
                        "Move (on the ground): Left Stick\n" +
                        "Nudge (in the air): Left Stick\n" +
                        "Aim: Left Trigger (hold)\n" +
                        "Adjust Aim: Left Stick (while aiming)\n" +
                        "Launch: Right Trigger, Left Bumper cancels\n" +
                        stuckLine +
                        "Camera: Right Stick\n" +
                        trailToggleLine +
                        switchLine +
                        "Pause: Start / Options / Esc",
                };
            }

            if (controlsPanelBody != null)
            {
                controlsPanelBody.text = controlScheme switch
                {
                    ControlScheme.Mixed when mixedFastPacedAir =>
                        "Left Stick / WASD - Move (on the ground, while not aiming)\n" +
                        "Grounded - Left Trigger (hold) to aim and charge, Left Stick to adjust\n" +
                        "  aim, Right Trigger to launch. South (hold) charges an Up launch,\n" +
                        "  release to fire - tilted toward the Left Stick, straight up centered.\n" +
                        "Airborne - hold Right Mouse / Left Trigger to aim: first-person view\n" +
                        "  with the trail-and-reticle preview. Hold Left Mouse / Right Trigger\n" +
                        "  to charge, release to fire straight along where you're looking.\n" +
                        "  Release Right Mouse at any time to cancel and return to 3rd person.\n" +
                        stuckLinePanel +
                        "Mouse / Right Stick - Camera\n" +
                        trailToggleLinePanel +
                        switchLinePanel +
                        "Start / Options / Esc - Pause",
                    ControlScheme.StickAim =>
                        "Left Stick - Move (on the ground)\n" +
                        "Left Stick (in the air) - Nudge distance / drift sideways\n" +
                        "South (hold) - Charge an Up launch: straight up if the stick is centered,\n" +
                        "  or tilted 80 degrees toward the stick otherwise. Release to fire.\n" +
                        "Left Trigger (hold) - Charge a Down launch: straight down if centered, or\n" +
                        "  tilted 60 degrees toward the stick. Release to fire.\n" +
                        "Right Trigger (hold) - Charge a Forward launch: tilted 5 degrees ahead if\n" +
                        "  centered, or 30 degrees toward the stick. Release to fire.\n" +
                        "The longer any of the three is held, the further it launches - and the\n" +
                        "  more energy it costs (see the yellow meter, top right). Left Bumper\n" +
                        "  cancels whichever is charging.\n" +
                        stuckLinePanel +
                        "Right Stick - Camera\n" +
                        trailToggleLinePanel +
                        switchLinePanel +
                        "Start / Options / Esc - Pause",
                    ControlScheme.Mixed =>
                        "Left Stick - Move (on the ground, while not aiming)\n" +
                        "Left Stick (in the air) - Nudge distance / drift sideways\n" +
                        "Grounded - Left Trigger (hold) to aim and charge, Left Stick to adjust\n" +
                        "  aim, Right Trigger to launch (exactly like the original scheme).\n" +
                        "  South (hold) also works from the ground: charge an Up launch, release\n" +
                        "  to fire - tilted toward the Left Stick, straight up when centered.\n" +
                        "Airborne - hold Left Trigger to aim: you freeze in place while it's\n" +
                        "  held. Then hold Right Trigger / South / West to charge a Forward /\n" +
                        "  Up / Down launch - release to fire, tilted toward the Left Stick when\n" +
                        "  it's held past the deadzone, straight when centered. Letting go of\n" +
                        "  Left Trigger stops aiming (or a charge) without firing.\n" +
                        "Left Bumper - Cancel whichever is currently charging, grounded or air.\n" +
                        stuckLinePanel +
                        "Right Stick - Camera\n" +
                        trailToggleLinePanel +
                        switchLinePanel +
                        "Start / Options / Esc - Pause",
                    ControlScheme.DefyGravity =>
                        "Left Stick - Move (on the ground)\n" +
                        "Right Trigger (hold) - Charge a Forward flight - aim it with the Left\n" +
                        "  Stick, or leave the stick centered to fly the way you're facing.\n" +
                        "Left Trigger (hold) - Charge an Up flight, always straight up.\n" +
                        "South (hold) - Charge a Down flight, always straight down.\n" +
                        "Charging grows both the flight's speed AND how long it lasts, up to the\n" +
                        "  energy you have stored. Gravity is suspended for the whole charge and\n" +
                        "  the flight itself - release to fire, then gravity resumes once the\n" +
                        "  flight's time runs out. Left Bumper cancels the current charge.\n" +
                        stuckLinePanel +
                        "Right Stick - Camera\n" +
                        trailToggleLinePanel +
                        switchLinePanel +
                        "Start / Options / Esc - Pause",
                    ControlScheme.FastPaced =>
                        "WASD (or equivalent) / Left Stick - Move / Nudge in the air\n" +
                        "Mouse / Right Stick - Look around (full 3rd person camera by default)\n" +
                        "Right Mouse / Left Trigger (hold) - Aim: switches to a first-person view\n" +
                        "  with a trail-and-reticle landing preview\n" +
                        "Left Mouse / Right Trigger (hold, while aiming) - Charge a launch straight\n" +
                        "  along the camera's current look direction. Release to fire.\n" +
                        "The longer the charge, the further the launch travels and the more the\n" +
                        "  camera zooms in on the predicted landing spot, so it stays readable.\n" +
                        "Release Right Mouse at any time to cancel and return to 3rd person.\n" +
                        "Gravity is off throughout this level - you fly in a straight line until\n" +
                        "  you crash into something.\n" +
                        stuckLinePanel +
                        "Start / Options / Esc - Pause",
                    _ =>
                        "Left Stick - Move (on the ground, while not aiming)\n" +
                        "Left Stick (in the air) - Nudge distance / drift sideways\n" +
                        "Left Trigger - Aim (hold; the cube stays put)\n" +
                        "Left Stick (while aiming) - Adjust aim direction\n" +
                        "Right Trigger - Launch\n" +
                        "Left Bumper - Cancel the current aim/charge without firing\n" +
                        stuckLinePanel +
                        "Right Stick - Camera\n" +
                        trailToggleLinePanel +
                        switchLinePanel +
                        "Start / Options / Esc - Pause" +
                        (alternateSchemesEnabled ? "" : "\n\n(Hold-Release and Analog launch schemes are still in the project, just disabled)"),
                };
            }
        }

        void HandleTrailToggle()
        {

            if (controlScheme == ControlScheme.FastPaced) return;

            if (energyControlMode == EnergyControlMode.ReverseDirection && (fastPacedAiming || fastPacedCharging)) return;
            if (trailToggleAction == null || trailToggleAction.action == null || !trailToggleAction.action.WasPressedThisFrame()) return;
            if (landingPreview == null) return;

            PredictionMode shownMode = landingPreview.ghostAndCrosshairEnabled ? PredictionMode.TrailAndCrosshair : PredictionMode.Trail;
            landingPreview.SetMode(landingPreview.CurrentMode == PredictionMode.None ? shownMode : PredictionMode.None);
        }

        public void SetControlSchemeFromMenu(ControlScheme scheme)
        {
            SwitchToScheme(scheme);
        }

        void SwitchToScheme(ControlScheme scheme)
        {
            controlScheme = scheme;

            if (isAiming)
            {
                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }
            if (stickAimChargeType != StickAimChargeType.None)
            {
                stickAimChargeType = StickAimChargeType.None;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }
            if (defyGravityChargeType != DefyGravityFlightType.None)
            {
                defyGravityChargeType = DefyGravityFlightType.None;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }
            if (fastPacedAiming)
            {
                CancelFastPacedAim();
            }
            mixedAirAiming = false;

            UpdateSchemeLabel();
        }

        void PayGroundPoundBoostedRefund()
        {
            if (groundPoundPendingRefund <= 0f) return;
            float before = energyFraction;
            energyFraction = Mathf.Clamp01(energyFraction + groundPoundPendingRefund * (groundPoundBoostMultiplier - 1f));
            groundPoundBoostExtra = Mathf.Max(0f, energyFraction - before);
            groundPoundPendingRefund = 0f;
        }

        void RevertGroundPoundBoost()
        {
            if (groundPoundBoostExtra <= 0f) return;
            energyFraction = Mathf.Clamp01(energyFraction - groundPoundBoostExtra);
            groundPoundBoostExtra = 0f;
        }

        void CancelFastPacedAim()
        {
            if (groundPoundAimNoGravity)
            {
                groundPoundAimNoGravity = false;
                groundPoundWindowTimer = 0f;
                rb.useGravity = true;
                RevertGroundPoundBoost();
            }
            fastPacedAiming = false;
            fastPacedCharging = false;
            chargeTime = 0f;
            reverseChargingDown = false;
            crankHasPreviousAngle = false;
            chargeDisplayInsufficient = false;
            autoAimForced = false;
            lastAutoSolvedCharge = -1f;
            aimButtonSpent = true;
            energyCrankUI?.SetVisible(false);
            landingPreview?.SetVisible(false);
            cameraOrbit?.SetFirstPersonMode(false);
            cameraOrbit?.SetAimZoom(0f);
        }

        Vector2 GamepadLookValue()
        {
            InputActionReference look = cameraOrbit != null ? cameraOrbit.lookAction : null;
            if (look == null || look.action == null) return Vector2.zero;
            if (look.action.activeControl == null || !(look.action.activeControl.device is Gamepad)) return Vector2.zero;
            return look.action.ReadValue<Vector2>();
        }

        void UpdateCrankCharge()
        {
            energyCrankUI?.SetVisible(true);

            Vector2 input = GamepadLookValue();
            if (input.magnitude < crankDeadzone && Keyboard.current != null)
            {
                Vector2 keys = Vector2.zero;
                if (Keyboard.current.wKey.isPressed) keys.y += 1f;
                if (Keyboard.current.sKey.isPressed) keys.y -= 1f;
                if (Keyboard.current.dKey.isPressed) keys.x += 1f;
                if (Keyboard.current.aKey.isPressed) keys.x -= 1f;
                if (keys != Vector2.zero) input = keys.normalized;
            }

            if (input.magnitude >= crankDeadzone)
            {
                float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
                energyCrankUI?.SetDotAngle(angle);
                if (crankHasPreviousAngle)
                {
                    float delta = Mathf.DeltaAngle(crankPreviousAngle, angle);

                    chargeTime = Mathf.Clamp(chargeTime + (-delta / 360f) * crankChargePerRevolution * maxChargeTime,
                        0f, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));
                }
                crankPreviousAngle = angle;
                crankHasPreviousAngle = true;
            }
            else
            {
                crankHasPreviousAngle = false;
            }
        }

        void UpdateDedicatedButtonsCharge()
        {
            float delta = 0f;
            float stickY = GamepadLookValue().y;
            if (Mathf.Abs(stickY) > 0.5f)
            {
                delta += Mathf.Sign(stickY) * buttonChargeRate * maxChargeTime * Time.unscaledDeltaTime;
            }
            if (Mouse.current != null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) delta += Mathf.Sign(scroll) * wheelChargeStep * maxChargeTime;
            }
            if (delta != 0f)
            {
                chargeTime = Mathf.Clamp(chargeTime + delta, 0f, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));
            }
        }

        void UpdateReverseCharge()
        {
            bool flipPressed = (trailToggleAction != null && trailToggleAction.action != null && trailToggleAction.action.WasPressedThisFrame())
                || (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame);
            if (flipPressed)
            {
                reverseChargingDown = !reverseChargingDown;
                if (reverseChargingDown) chargeTime = Mathf.Min(maxChargeTime, EnergyChargeCeiling());
            }

            if (reverseChargingDown)
            {
                chargeTime = Mathf.Max(chargeTime - Time.unscaledDeltaTime * chargeAccumulationRate, 0f);
            }
            else
            {
                chargeTime = Mathf.Min(chargeTime + Time.unscaledDeltaTime * chargeAccumulationRate, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));
            }
        }

        void UpdateEnergyModeAim(bool firePressed)
        {
            Vector3 dir = cameraOrbit != null ? cameraOrbit.AimForward : transform.forward;

            landingPreview?.SetVisible(true);
            landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);

            if (energyControlMode == EnergyControlMode.Automatic)
            {
                cameraOrbit?.SetFirstPersonMode(!isGrounded);
            }

            switch (energyControlMode)
            {
                case EnergyControlMode.Automatic:

                    if (!TryGetAutoAimTarget(dir, out Vector3 target))
                    {
                        target = (cameraTransform != null ? cameraTransform.position : transform.position) + dir * autoAimMaxDistance;
                    }

                    Vector3 toTarget = target - transform.position;
                    if (toTarget.sqrMagnitude > 0.01f) dir = toTarget.normalized;

                    bool solveDue = lastAutoSolvedCharge < 0f
                        || (Time.frameCount - lastAutoSolveFrame >= 5
                            && ((target - lastAutoTarget).sqrMagnitude > 0.25f || Time.frameCount - lastAutoSolveFrame >= 20));
                    if (solveDue)
                    {
                        lastAutoSolvedCharge = SolveChargeForTarget(dir, target);
                        lastAutoTarget = target;
                        lastAutoSolveFrame = Time.frameCount;
                    }

                    float required = Mathf.Clamp01(lastAutoSolvedCharge + autoChargeFailsafe);

                    float affordable = energyCostPerFullCharge > 0f ? Mathf.Clamp01(SpendableEnergy() / energyCostPerFullCharge) : 1f;
                    chargeDisplayInsufficient = required > affordable + 0.0001f;
                    chargeTime = Mathf.Min(required, affordable) * maxChargeTime;
                    break;
                case EnergyControlMode.CircleCrank:
                    UpdateCrankCharge();
                    break;
                case EnergyControlMode.DedicatedButtons:
                    UpdateDedicatedButtonsCharge();
                    break;
                case EnergyControlMode.ReverseDirection:
                    UpdateReverseCharge();
                    break;
            }

            float fireFraction = Mathf.Min(ChargeFraction(), energyCostPerFullCharge > 0f ? SpendableEnergy() / energyCostPerFullCharge : 1f);
            float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, fireFraction);
            float damping = Mathf.Lerp(fastPacedMinDamping, fastPacedMaxDamping, fireFraction);

            Vector3 initialVelocity = rb.linearVelocity + dir * force / rb.mass;
            Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
            Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, damping, out int stepCount, out bool didLand);
            lastPredictedLanding = landingPoint;
            hasPredictedLanding = true;
            hasValidPredictedLanding = didLand;
            if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
            {
                landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand, lastPredictedLandingNormal);
            }

            if (firePressed && energyFraction > 0f && CanStartNewLaunch())
            {
                chargeTime = fireFraction * maxChargeTime;
                QueueLaunch(dir, force, damping);
                if (mixedFastPacedAir && controlScheme == ControlScheme.Mixed) fastPacedFlightExact = true;
                CancelFastPacedAim();
            }
        }

        bool TryGetAutoAimTarget(Vector3 dir, out Vector3 target)
        {
            Vector3 origin = cameraTransform != null ? cameraTransform.position : transform.position;

            float minDistance = Vector3.Distance(origin, transform.position) - 1f;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, autoAimMaxDistance, ~0, QueryTriggerInteraction.Collide);
            float bestDistance = float.MaxValue;
            target = default;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.distance < minDistance) continue;
                if (hit.collider == boxCollider || hit.collider.transform.IsChildOf(transform)) continue;

                if (hit.collider.isTrigger && hit.collider.GetComponentInParent<PositioningTarget>() == null) continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    target = hit.point;
                    found = true;
                }
            }
            return found;
        }

        float SolveChargeForTarget(Vector3 dir, Vector3 target)
        {
            const int coarseSamples = 8;
            float bestCharge = 0f;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < coarseSamples; i++)
            {
                float candidate = i / (float)(coarseSamples - 1);
                float distance = LandingDistanceToPoint(dir, candidate, target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestCharge = candidate;
                }
            }

            float cell = 1f / (coarseSamples - 1);
            float lo = Mathf.Clamp01(bestCharge - cell);
            float hi = Mathf.Clamp01(bestCharge + cell);
            int fineSamples = Mathf.Max(autoSearchIterations, 2);
            for (int i = 0; i <= fineSamples; i++)
            {
                float candidate = Mathf.Lerp(lo, hi, i / (float)fineSamples);
                float distance = LandingDistanceToPoint(dir, candidate, target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestCharge = candidate;
                }
            }
            return bestCharge;
        }

        const int AutoSolveStepLimit = 150;

        float LandingDistanceToPoint(Vector3 dir, float chargeFraction, Vector3 target)
        {
            float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
            float damping = Mathf.Lerp(fastPacedMinDamping, fastPacedMaxDamping, chargeFraction);

            Vector3 landing = PredictLandingPoint(transform.position, rb.linearVelocity + dir * force / rb.mass, damping, out int _, out bool _, 0f, AutoSolveStepLimit);
            return (landing - target).sqrMagnitude;
        }

        void UpdateStickAimChargeScheme()
        {
            Vector2 stick = moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            bool stickHeld = stick.sqrMagnitude > stickAimDeadzone * stickAimDeadzone;
            Vector3 stickDirection = stickHeld ? StickWorldDirection(stick) : Vector3.zero;

            bool cancelPressed = cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame();

            if (stickAimChargeType != StickAimChargeType.None)
            {

                if (mixedAirAiming && !(launchAction != null && launchAction.action != null && launchAction.action.IsPressed()))
                {
                    CancelStickAimCharge();
                    mixedAirAiming = false;
                    return;
                }

                if (cancelPressed)
                {
                    CancelStickAimCharge();
                    return;
                }

                InputActionReference downAction = DownChargeActionForCurrentScheme();
                bool keyboardAvailable = mouseAirControls && Keyboard.current != null;

                if (controlScheme == ControlScheme.Mixed)
                {
                    bool upSwitch = (upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame())
                        || (keyboardAvailable && Keyboard.current.spaceKey.wasPressedThisFrame);
                    bool downSwitch = (downAction != null && downAction.action != null && downAction.action.WasPressedThisFrame())
                        || (keyboardAvailable && Keyboard.current.eKey.wasPressedThisFrame);
                    bool forwardSwitch = fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();
                    if (upSwitch && stickAimChargeType != StickAimChargeType.Up) stickAimChargeType = StickAimChargeType.Up;
                    else if (downSwitch && stickAimChargeType != StickAimChargeType.Down) stickAimChargeType = StickAimChargeType.Down;
                    else if (forwardSwitch && stickAimChargeType != StickAimChargeType.Forward) stickAimChargeType = StickAimChargeType.Forward;
                }

                bool releasedNow = stickAimChargeType switch
                {
                    StickAimChargeType.Up => (upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasReleasedThisFrame())
                        || (keyboardAvailable && Keyboard.current.spaceKey.wasReleasedThisFrame),
                    StickAimChargeType.Down => (downAction != null && downAction.action != null && downAction.action.WasReleasedThisFrame())
                        || (keyboardAvailable && Keyboard.current.eKey.wasReleasedThisFrame),
                    _ => fireAction != null && fireAction.action != null && fireAction.action.WasReleasedThisFrame(),
                };

                if (westAirDownLaunch && (stickAimChargeType == StickAimChargeType.Up || stickAimChargeType == StickAimChargeType.Down))
                {
                    float chargeSpeed = upDownChargeSpeedMultiplier;

                    if (groundPoundBoostEconomy)
                    {
                        chargeSpeed = groundPoundChargeBaseSpeed + groundPoundChargeSpeedGrowth * groundPoundChargeHoldTime;
                        groundPoundChargeHoldTime += Time.unscaledDeltaTime;
                    }
                    chargeTime = Mathf.Min(
                        chargeTime + Time.unscaledDeltaTime * chargeAccumulationRate * chargeSpeed,
                        maxChargeTime, EnergyChargeCeiling());
                }
                else
                {
                    AccumulateCharge();
                }
                Vector3 dir;
                if (stickHeld)
                {
                    dir = ComputeStickAimDirection(stickAimChargeType, true, stickDirection);
                    stickAimLastFlatDirection = stickDirection;
                    stickAimHasAimed = true;
                }
                else if (stickAimChargeType == StickAimChargeType.Forward)
                {

                    Vector3 flat = stickAimHasAimed
                        ? stickAimLastFlatDirection
                        : (mouseAirControls && groundedAimWithMouse ? CameraForwardFlat() : FacingFlatDirection());
                    dir = TiltedDirection(flat, stickAimForwardNeutralAngle);
                }
                else
                {

                    dir = ComputeStickAimDirection(stickAimChargeType, false, stickDirection);
                }

                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(dir, chargeFraction);

                float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);

                float previewDamping = stickAimChargeType == StickAimChargeType.Down
                    ? downLaunchDamping
                    : Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);

                Vector3 initialVelocity = dir * previewForce / rb.mass;
                Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, previewDamping, out int stepCount, out bool didLand);
                lastPredictedLanding = landingPoint;
                hasPredictedLanding = true;
                hasValidPredictedLanding = didLand;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand, lastPredictedLandingNormal);
                }

                if (releasedNow)
                {
                    QueueLaunch(dir, previewForce, previewDamping);

                    if (stickAimChargeType == StickAimChargeType.Down && !isGrounded) lastLaunchWasAirDown = true;

                    if (stickAimChargeType == StickAimChargeType.Forward) RecenterCameraForStickAimLaunch(dir);

                    stickAimChargeType = StickAimChargeType.None;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);

                    if (mixedAirAiming)
                    {
                        mixedAirAiming = false;
                        waitingForLtRelease = true;
                    }
                }
            }
            else
            {
                bool canLaunch = energyFraction > 0f && CanStartNewLaunch();

                InputActionReference downAction = DownChargeActionForCurrentScheme();
                bool keyboardAvailable = mouseAirControls && Keyboard.current != null;
                bool upPressed = canLaunch && ((upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame())
                    || (keyboardAvailable && Keyboard.current.spaceKey.wasPressedThisFrame));
                bool downPressed = canLaunch && ((downAction != null && downAction.action != null && downAction.action.WasPressedThisFrame())
                    || (keyboardAvailable && Keyboard.current.eKey.wasPressedThisFrame));
                bool rtPressed = canLaunch && fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();

                if (upPressed) StartStickAimCharge(StickAimChargeType.Up);
                else if (downPressed) StartStickAimCharge(StickAimChargeType.Down);
                else if (rtPressed) StartStickAimCharge(StickAimChargeType.Forward);
            }
        }

        void StartStickAimCharge(StickAimChargeType type)
        {
            stickAimChargeType = type;
            chargeTime = 0f;
            stickAimHasAimed = false;
            groundPoundChargeHoldTime = 0f;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            aimArrow?.SetVisible(true);
            landingPreview?.SetVisible(true);
        }

        void CancelStickAimCharge()
        {
            stickAimChargeType = StickAimChargeType.None;
            chargeTime = 0f;
            aimArrow?.SetVisible(false);
            landingPreview?.SetVisible(false);
        }

        Vector3 ComputeStickAimDirection(StickAimChargeType type, bool stickHeld, Vector3 stickDirection)
        {
            switch (type)
            {
                case StickAimChargeType.Up:

                    return Vector3.up;
                case StickAimChargeType.Down:

                    return Vector3.down;
                default:

                    return stickHeld
                        ? TiltedDirection(stickDirection, stickAimForwardAngle)
                        : TiltedDirection(FacingFlatDirection(), stickAimForwardNeutralAngle);
            }
        }

        void UpdateDefyGravityScheme()
        {
            Vector2 stick = moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            bool stickHeld = stick.sqrMagnitude > stickAimDeadzone * stickAimDeadzone;
            Vector3 stickDirection = stickHeld ? StickWorldDirection(stick) : Vector3.zero;

            bool cancelPressed = cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame();

            if (defyGravityChargeType != DefyGravityFlightType.None)
            {
                if (cancelPressed)
                {
                    CancelDefyGravityCharge();
                    return;
                }

                bool releasedNow = defyGravityChargeType switch
                {
                    DefyGravityFlightType.Up => launchAction != null && launchAction.action != null && launchAction.action.WasReleasedThisFrame(),
                    DefyGravityFlightType.Down => upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasReleasedThisFrame(),
                    _ => fireAction != null && fireAction.action != null && fireAction.action.WasReleasedThisFrame(),
                };

                AccumulateCharge();

                Vector3 dir = defyGravityChargeType switch
                {
                    DefyGravityFlightType.Up => Vector3.up,
                    DefyGravityFlightType.Down => Vector3.down,
                    _ => stickHeld ? stickDirection : FacingFlatDirection(),
                };

                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(dir, chargeFraction);

                float flightSpeed = Mathf.Lerp(minLaunchForce, maxDefyGravitySpeed, chargeFraction);
                float flightDuration = Mathf.Lerp(minDefyGravityDuration, maxDefyGravityDuration, chargeFraction);

                Vector3 initialVelocity = dir * flightSpeed;
                Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, defyGravityFallDamping, out int stepCount, out bool didLand, flightDuration);
                lastPredictedLanding = landingPoint;
                hasPredictedLanding = true;
                hasValidPredictedLanding = didLand;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand, lastPredictedLandingNormal);
                }

                if (releasedNow)
                {

                    QueueLaunch(dir, flightSpeed * rb.mass, defyGravityFallDamping, flightDuration);
                    RecenterCameraForStickAimLaunch(dir);

                    defyGravityChargeType = DefyGravityFlightType.None;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);
                }
            }
            else
            {
                bool canLaunch = energyFraction > 0f && CanStartNewLaunch();

                bool ltPressed = canLaunch && launchAction != null && launchAction.action != null && launchAction.action.WasPressedThisFrame();
                bool southPressed = canLaunch && upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame();
                bool rtPressed = canLaunch && fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();

                if (ltPressed) StartDefyGravityCharge(DefyGravityFlightType.Up);
                else if (southPressed) StartDefyGravityCharge(DefyGravityFlightType.Down);
                else if (rtPressed) StartDefyGravityCharge(DefyGravityFlightType.Forward);
            }
        }

        void StartDefyGravityCharge(DefyGravityFlightType type)
        {
            defyGravityChargeType = type;
            chargeTime = 0f;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            aimArrow?.SetVisible(true);
            landingPreview?.SetVisible(true);
        }

        void CancelDefyGravityCharge()
        {
            defyGravityChargeType = DefyGravityFlightType.None;
            chargeTime = 0f;
            aimArrow?.SetVisible(false);
            landingPreview?.SetVisible(false);
        }

        void UpdateFastPacedScheme()
        {

            if (autoAimForced && fastPacedAimAction != null && fastPacedAimAction.action != null && fastPacedAimAction.action.WasPressedThisFrame())
            {
                autoAimForced = false;
            }
            bool rmbHeld = autoAimForced
                || (fastPacedAimAction != null && fastPacedAimAction.action != null && fastPacedAimAction.action.IsPressed());

            if (!rmbHeld)
            {
                if (fastPacedAiming)
                {

                    if (groundPoundAimNoGravity)
                    {
                        groundPoundAimNoGravity = false;
                        groundPoundWindowTimer = 0f;
                        rb.useGravity = true;
                        RevertGroundPoundBoost();
                    }
                    fastPacedAiming = false;
                    fastPacedCharging = false;
                    chargeTime = 0f;
                    landingPreview?.SetVisible(false);
                    cameraOrbit?.SetFirstPersonMode(false);
                    cameraOrbit?.SetAimZoom(0f);
                }
                return;
            }

            if (!fastPacedAiming)
            {

                if (energyFraction <= 0f) return;

                if (!(fastPacedAimAction != null && fastPacedAimAction.action != null && fastPacedAimAction.action.WasPressedThisFrame()))
                {
                    return;
                }
                fastPacedAiming = true;
                cameraOrbit?.SetFirstPersonMode(true);

                if (energyControlMode != EnergyControlMode.Standard)
                {
                    chargeTime = 0f;
                    reverseChargingDown = false;
                    chargeDisplayInsufficient = false;
                }

                if (groundPoundBoostEconomy && groundPoundWindowTimer > 0f)
                {
                    PayGroundPoundBoostedRefund();
                    chargeTime = Mathf.Min(maxChargeTime, EnergyChargeCeiling());
                    groundPoundAimNoGravity = true;
                    rb.useGravity = false;
                }
            }

            bool lmbPressed = fastPacedLaunchAction != null && fastPacedLaunchAction.action != null && fastPacedLaunchAction.action.WasPressedThisFrame();
            bool lmbReleased = fastPacedLaunchAction != null && fastPacedLaunchAction.action != null && fastPacedLaunchAction.action.WasReleasedThisFrame();

            if (energyControlMode != EnergyControlMode.Standard)
            {
                UpdateEnergyModeAim(lmbPressed);
                return;
            }

            if (!fastPacedCharging)
            {

                if (!lmbPressed || energyFraction <= 0f || !CanStartNewLaunch()) return;

                fastPacedCharging = true;

                if (groundPoundBoostEconomy && groundPoundWindowTimer > 0f)
                {
                    PayGroundPoundBoostedRefund();
                    chargeTime = Mathf.Min(maxChargeTime, EnergyChargeCeiling());
                    groundPoundAimNoGravity = true;
                    rb.useGravity = false;
                }
                else
                {
                    chargeTime = 0f;
                }

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                landingPreview?.SetVisible(true);
                landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);
            }

            AccumulateCharge();

            Vector3 dir = cameraOrbit != null ? cameraOrbit.AimForward : transform.forward;
            float chargeFraction = ChargeFraction();

            cameraOrbit?.SetAimZoom(chargeFraction);

            float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
            float previewDamping = Mathf.Lerp(fastPacedMinDamping, fastPacedMaxDamping, chargeFraction);

            Vector3 initialVelocity = dir * previewForce / rb.mass;
            Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
            Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, previewDamping, out int stepCount, out bool didLand);
            lastPredictedLanding = landingPoint;
            hasPredictedLanding = true;
            hasValidPredictedLanding = didLand;

            if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
            {
                landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand, lastPredictedLandingNormal);
            }

            if (lmbReleased)
            {
                QueueLaunch(dir, previewForce, previewDamping);

                if (mixedFastPacedAir && controlScheme == ControlScheme.Mixed) fastPacedFlightExact = true;

                CancelFastPacedAim();
            }
        }

        void QueueLaunch(Vector3 direction, float force, float damping, float defyGravityDuration = 0f)
        {
            if (groundPoundAimNoGravity)
            {
                groundPoundAimNoGravity = false;
                groundPoundWindowTimer = 0f;
                rb.useGravity = true;
                groundPoundBoostExtra = 0f;
            }
            queuedDirection = direction;
            queuedForce = force;
            queuedDamping = damping;
            queuedDefyGravityDuration = defyGravityDuration;
            launchQueued = true;
            hasLaunched = true;
            launchesSinceGrounded++;
            fastPacedFlightExact = false;
            aimButtonSpent = true;
            positioningAimUsedThisFlight = false;
            lastLaunchWasGrounded = isGrounded;
            lastLaunchWasAirDown = false;
            currentFlightIsDownward = Vector3.Dot(direction.normalized, Vector3.down) >= slamDownwardThreshold;

            lastLaunchEnergySpent = Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge);
            energyFraction = Mathf.Clamp01(energyFraction - lastLaunchEnergySpent);
            flightEnergySpent += lastLaunchEnergySpent;

            launchGraceTimer = launchGraceDuration;
        }

        void RecenterCameraForStickAimLaunch(Vector3 direction)
        {

            Vector3 flatLaunchDir = new Vector3(direction.x, 0f, direction.z);
            float launchYaw = flatLaunchDir.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg
                : (freeMoveController != null ? freeMoveController.FacingYaw : 0f);
            cameraOrbit?.RecenterBehindTarget(launchYaw);
        }

        static Vector3 TiltedDirection(Vector3 flatDirection, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 flat = flatDirection.sqrMagnitude > 0.0001f ? flatDirection.normalized : Vector3.forward;
            return flat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
        }

        Vector3 StickWorldDirection(Vector2 stick)
        {
            Vector3 dir = CameraForwardFlat() * stick.y + CameraRightFlat() * stick.x;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        Vector3 FacingFlatDirection()
        {
            float yaw = freeMoveController != null ? freeMoveController.FacingYaw : 0f;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        Vector3 CameraForwardFlat()
        {
            if (cameraTransform == null) return Vector3.forward;
            Vector3 f = cameraTransform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        Vector3 CameraRightFlat()
        {
            if (cameraTransform == null) return Vector3.right;
            Vector3 r = cameraTransform.right;
            r.y = 0f;
            return r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
        }

        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, float damping, out int stepCount, out bool didLand, float gravityFreeDuration = 0f, int stepLimit = 0)
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
                        : entry.proxySphere != null ? entry.proxySphere
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
            predictionRb.WakeUp(); predictionRb.linearVelocity = initialVelocity;
            predictionRb.angularVelocity = Vector3.zero;

            float dt = Time.fixedDeltaTime;
            Vector3 landing = startPos;
            stepCount = 0;
            didLand = false;

            int stepBudget = stepLimit > 0 ? Mathf.Min(stepLimit, maxPredictionSteps) : maxPredictionSteps;
            for (int i = 0; i < stepBudget; i++)
            {

                if (i * dt < gravityFreeDuration)
                {
                    predictionRb.linearVelocity = initialVelocity;
                    predictionRb.angularVelocity = Vector3.zero;
                }

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

            return landing;
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
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Collider col in colliders)
            {
                if (col == boxCollider) continue;
                if (col.GetComponent<Rigidbody>() != null) continue;

                if (col.isTrigger) continue;

                GameObject proxy = new GameObject("PredictionGeometryProxy");
                SceneManager.MoveGameObjectToScene(proxy, predictionScene);

                PredictionGeometryProxy entry = new PredictionGeometryProxy { source = col, proxy = proxy };
                if (col is BoxCollider)
                {
                    entry.proxyBox = proxy.AddComponent<BoxCollider>();
                }
                else if (col is MeshCollider meshCol)
                {
                    entry.proxyMesh = proxy.AddComponent<MeshCollider>();
                    entry.proxyMesh.convex = meshCol.convex;
                }
                else if (col is SphereCollider)
                {

                    entry.proxySphere = proxy.AddComponent<SphereCollider>();
                }
                else if (col is CapsuleCollider sourceCapsuleCollider)
                {
                    entry.proxyCapsule = proxy.AddComponent<CapsuleCollider>();
                    entry.proxyCapsule.direction = sourceCapsuleCollider.direction;
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
            public MeshCollider proxyMesh;
            public SphereCollider proxySphere;
            public CapsuleCollider proxyCapsule;
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
            else if (entry.proxyMesh != null && entry.source is MeshCollider sourceMesh)
            {
                if (entry.proxyMesh.sharedMesh != sourceMesh.sharedMesh) entry.proxyMesh.sharedMesh = sourceMesh.sharedMesh;
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

            bool sourceSolid = entry.source.enabled && entry.source.gameObject.activeInHierarchy;
            if (entry.proxy.activeSelf != sourceSolid) entry.proxy.SetActive(sourceSolid);
        }

        float ChargeFraction()
        {
            return maxChargeTime > 0f ? Mathf.Clamp01(chargeTime / maxChargeTime) : 1f;
        }

        void AccumulateCharge()
        {
            chargeTime = Mathf.Min(chargeTime + Time.deltaTime * chargeAccumulationRate, maxChargeTime, EnergyChargeCeiling());
        }

        float EnergyChargeCeiling()
        {
            return energyCostPerFullCharge > 0f ? (SpendableEnergy() / energyCostPerFullCharge) * maxChargeTime : maxChargeTime;
        }

        float SpendableEnergy()
        {
            return isGrounded ? Mathf.Max(energyFraction - minEnergyReserve, 0f) : energyFraction;
        }

        void ClampEnergyFloor()
        {
            if (energyFraction < minEnergyReserve) energyFraction = minEnergyReserve;
        }

        void GainEnergyFromCrash(float crashSpeed)
        {

            if (groundPoundBoostEconomy)
            {
                float flightSpend = flightEnergySpent > 0.0001f ? flightEnergySpent : lastLaunchEnergySpent;

                energyFraction = Mathf.Clamp01(energyFraction + flightSpend);
                if (lastLaunchWasAirDown) groundPoundPendingRefund = lastLaunchEnergySpent;
                ClampEnergyFloor();
                return;
            }

            if (lastLaunchRefundEconomy)
            {

                float spend = chainLaunchAccumulation ? flightEnergySpent : lastLaunchEnergySpent;
                float economyGain;
                if (lastLaunchWasAirDown)
                {
                    economyGain = Mathf.Max(spend * groundPoundRefundMultiplier, groundPoundMinRefund);
                }
                else if (lastLaunchWasGrounded)
                {
                    economyGain = spend;
                }
                else
                {

                    float x = spend;
                    economyGain = spend * (x * midairRefundSpendFactor + 1f);
                }
                energyFraction = Mathf.Clamp01(energyFraction + economyGain);
                ClampEnergyFloor();
                return;
            }

            float gained = controlScheme == ControlScheme.FastPaced || refundSpentEnergyOnly
                ? lastLaunchEnergySpent * fastPacedRefundMultiplier
                : crashSpeed * energyGainPerSpeed * (1f + crashSpeed * energyGainSpeedBonus);
            gained = Mathf.Max(gained, minEnergyGainPerCrash);
            energyFraction = Mathf.Clamp01(energyFraction + gained);
            ClampEnergyFloor();
        }

        Vector3 AimDirection()
        {
            return Quaternion.Euler(aimPitch, aimYaw, 0f) * Vector3.forward;
        }

        void SeedAimFromCamera()
        {

            aimYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            aimPitch = Mathf.Clamp(defaultAimPitch, minAimPitch, maxAimPitch);
        }
    }
}
