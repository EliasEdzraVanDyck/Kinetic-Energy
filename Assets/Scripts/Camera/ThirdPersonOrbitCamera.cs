using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KineticEnergy.Camera
{
    public class ThirdPersonOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        public float height = 2.5f;

        [Header("Orbit")]
        public float distance = 6f;
        public float rotationSpeed = 120f;

        public float minPitch = -75f;

        public float maxPitch = 75f;
        public bool invertY = false;
        [Tooltip("Yaw speed floor at the steepest pitch (fraction of normal). At high angles the orbit circle shrinks toward the pole, so an unscaled yaw rate visually WHIRLS the world - yaw speed scales down by cos(pitch), never below this floor. Third person only; the first-person aim keeps raw yaw.")]
        [Range(0.1f, 1f)] public float highAngleYawFloor = 0.35f;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.08f;
        [Tooltip("Position smoothing while a launch is in flight - larger than positionSmoothTime, so the camera briefly trails the launch and then catches up.")]
        public float launchFollowSmoothTime = 0.25f;
        [Tooltip("Same trailing effect for VERTICAL launches (up-charge and ground pound) - slightly tighter, so the camera hangs back a touch less on straight up/down flights.")]
        public float verticalLaunchFollowSmoothTime = 0.18f;
        [Tooltip("Time constant of the follow relaxing back to normal tightness after a flight ends - long enough to avoid a lunge, short enough that the camera doesn't feel drugged after landing.")]
        public float followLagRecoverySeconds = 0.3f;
        public float maxDeltaTime = 0.05f;

        float followSmoothTime;

        bool launchInFlight;
        bool launchIsVertical;
        float launchIntensity = 1f;

        [Tooltip("How much LONGER the launch-follow smoothing runs for a zero-charge launch (1 = no stretch). Weak launches are slow, so without this their camera lag is over before it reads; full-charge launches always use the base values.")]
        public float shortLaunchLagMultiplier = 2f;

        [Tooltip("Seconds before the predicted landing at which the launch follow starts tightening, so the camera has mostly caught up by the moment of impact - a crash shake on a camera still swooping in reads as disconnected from it.")]
        public float landingTightenWindowSeconds = 0.45f;
        [Tooltip("What fraction of the launch follow smoothing remains AT the landing (1 = no tightening, 0.4 = a much tighter chase in the final approach).")]
        [Range(0.05f, 1f)] public float landingTightenMultiplier = 0.4f;
        float remainingFlightSeconds = float.PositiveInfinity;

        public void SetRemainingFlight(float seconds) => remainingFlightSeconds = seconds;

        public void SetLaunchInFlight(bool inFlight, bool vertical, float intensity01)
        {
            launchInFlight = inFlight;
            launchIsVertical = vertical;
            launchIntensity = Mathf.Clamp01(intensity01);
        }

        [Header("Auto Recenter")]

        public float recenterSpeed = 240f;

        [Header("Input")]
        public InputActionReference lookAction;

        bool aimStickOverrideActive;
        Vector2 aimStickOverrideValue;
        bool aimStickOverrideKeyboard;

        [Header("Keyboard & Mouse Speed")]

        [Tooltip("Mouse look speed multiplier for the ordinary third-person orbit (not aiming).")]
        [Range(0.1f, 1f)] public float mouseOrbitSpeedMultiplier = 0.6f;
        [Tooltip("Mouse look speed multiplier during the midair first-person aim.")]
        [Range(0.1f, 1f)] public float mouseAimSpeedMultiplier = 0.85f;
        [Tooltip("Speed multiplier for the WASD-driven camera during the grounded aim.")]
        [Range(0.1f, 1f)] public float wasdAimCameraSpeedMultiplier = 0.85f;

        [Header("Gamepad Speed")]

        [Tooltip("Gamepad look speed multiplier while the player is GROUNDED.")]
        [Range(0.5f, 2f)] public float gamepadGroundedSpeedMultiplier = 1.2f;
        [Tooltip("Gamepad look speed multiplier while the player is AIRBORNE (flights and midair aim).")]
        [Range(0.5f, 2f)] public float gamepadAirborneSpeedMultiplier = 1.265f;

        bool playerGrounded;

        public void SetPlayerGrounded(bool grounded)
        {
            playerGrounded = grounded;
        }

        bool mouseLookSuppressed;

        public void SetAimStickOverride(bool active, Vector2 stick, bool keyboardDriven = false)
        {
            aimStickOverrideActive = active;
            aimStickOverrideValue = active ? stick : Vector2.zero;
            aimStickOverrideKeyboard = active && keyboardDriven;
        }

        public void SetMouseLookSuppressed(bool suppressed)
        {
            mouseLookSuppressed = suppressed;
        }

        bool ignoreSlowMo;

        public void SetIgnoreSlowMo(bool ignore)
        {
            ignoreSlowMo = ignore;
        }

        public float firstPersonForwardOffset = 0.75f;

        public float modeSwitchSmoothTime = 0.02f;

        bool modeSwitching;

        public float framingTurnSpeed = 300f;

        public float framingMaxDeviation = 45f;
        [Tooltip("Cursor framing during midair aims: the view centres the landing cursor when it's near the aim. OFF = the view follows the raw aim 1:1, fully free (the aim-lab scenes) - the aim can then never outrun the view.")]
        public bool trajectoryFramingEnabled = true;

        [Tooltip("The player hides OUTRIGHT when the camera is squeezed closer than this (wall crashes collapse the OTS pull-in into the model): a giant near-plane-clipped player reads broken - fully invisible is better. Shown again as soon as there is room.")]
        public float playerHideDistance = 1.1f;
        [Tooltip("Fully cursor-framed while the cursor is within this many degrees of the aim; from here to Framing Max Deviation the view BLENDS gradually back to the raw aim instead of switching at the edge (the hard edge made steep low-energy up-aims rotate abruptly - direct report).")]
        public float framingBlendStartDegrees = 25f;
        [Tooltip("How fast the framing blend weight may change per second (1 = a full handover takes a second). The TIME smoothing is what keeps the blend jitter-free: landing-prediction wobble can no longer whip the blend within a frame.")]
        public float framingBlendSpeed = 2.5f;
        [Tooltip("Seconds of world-space smoothing on the landing point before the view frames it. Close landings turn tiny prediction wobble into big view rotation - this absorbs the wobble at its source while deliberate cursor moves still track promptly.")]
        public float framingPointSmoothTime = 0.12f;

        bool framingActive;
        Vector3 framingPoint;
        bool framingJustStarted;
        float framedWeightCurrent;

        Vector3 framingPointSmoothed;
        Vector3 framingPointVelocity;
        bool framingWasActive;

        float viewYaw;
        float viewPitch;
        bool viewAnglesSeeded;

        public void SetTrajectoryFraming(bool active, Vector3 worldPoint)
        {
            if (active && !framingActive) framingJustStarted = true;
            framingActive = active;
            framingPoint = worldPoint;
        }

        [Header("Fine Aim")]

        [Range(0f, 1f)] public float fineAimMinFactor = 0.3f;
        public float fineAimStickReference = 0.9f;
        public float fineAimMouseReference = 8f;

        [Header("First Person Aim")]

        public float normalFov = 60f;
        public float maxZoomFov = 20f;

        public float firstPersonMinPitch = -89f;
        public float firstPersonMaxPitch = 89f;

        UnityEngine.Camera cam;
        bool firstPerson;

        AimCameraPreset aimPreset;
        float aimZoomFraction;
        float driftClock;
        float driftAmpFactor;

        bool freeLookActive;
        Vector2 freeLookInput;
        float freeLookYaw;
        float freeLookPitch;

        readonly OneEuroFilter aimYawFilter = new OneEuroFilter();
        readonly OneEuroFilter aimPitchFilter = new OneEuroFilter();

        public void SetFreeLook(bool active, Vector2 input)
        {
            freeLookActive = active;
            freeLookInput = active ? input : Vector2.zero;
        }

        [Tooltip("Automatically hold the clearer shoulder during OTS aims: when geometry squeezes the current side and the mirrored side is clear, the camera glides across. Manual swaps (Q / Right Stick Click) override it for the rest of that aim.")]
        public bool autoShoulder = true;
        [Tooltip("Extra clearance (fraction of the offset span) the OTHER side must have before an auto-swap triggers - hysteresis so the camera never flip-flops.")]
        [Range(0.05f, 0.6f)] public float autoShoulderMargin = 0.25f;
        [Tooltip("Unscaled seconds the shoulder-swap glide takes.")]
        public float shoulderSwapSmoothTime = 0.22f;

        float shoulderTarget = 1f;
        float shoulderCurrent = 1f;
        float shoulderVelocity;
        bool shoulderManualHold;

        public void ToggleAimShoulder()
        {
            shoulderTarget = -shoulderTarget;
            shoulderManualHold = true;
        }

        public void SetAimCameraPreset(AimCameraPreset preset)
        {
            aimPreset = preset;
        }

        bool OtsAimActive => firstPerson && aimPreset != null && aimPreset.UsesOverShoulder;

        public Vector3 AimForward => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        System.Collections.Generic.List<Renderer> proximityHiddenRenderers;

        void UpdatePlayerProximityHiding()
        {
            if (target == null) return;
            float distance = Vector3.Distance(transform.position, target.position);

            bool tooClose = proximityHiddenRenderers != null
                ? distance < playerHideDistance * 1.15f
                : distance < playerHideDistance;

            if (tooClose && proximityHiddenRenderers == null)
            {
                proximityHiddenRenderers = new System.Collections.Generic.List<Renderer>();
                foreach (Renderer rend in target.GetComponentsInChildren<Renderer>(false))
                {
                    if (rend == null || !rend.enabled) continue;

                    if (rend.GetComponentInParent<KineticEnergy.Player.LandingPreviewController>() != null) continue;
                    if (rend.GetComponentInParent<KineticEnergy.Player.AimArrowIndicator>() != null) continue;
                    rend.enabled = false;
                    proximityHiddenRenderers.Add(rend);
                }
            }
            else if (!tooClose && proximityHiddenRenderers != null)
            {
                foreach (Renderer rend in proximityHiddenRenderers)
                {
                    if (rend != null) rend.enabled = true;
                }
                proximityHiddenRenderers = null;
            }
        }

        float startYaw;
        float startPitch;
        bool startPoseCaptured;

        bool snapPositionNextUpdate;

        public void ResetToStartPose()
        {
            if (startPoseCaptured)
            {
                yaw = startYaw;
                pitch = startPitch;
            }
            recentering = false;
            snapPositionNextUpdate = true;
            SnapToThirdPersonOrbit();
        }

        public void SetAimYaw(float yawDegrees)
        {
            yaw = yawDegrees;
            yawInitialized = true;
            recentering = false;
            freeLookYaw = 0f;

            viewAnglesSeeded = false;
        }

        public void SnapToThirdPersonOrbit()
        {
            firstPerson = false;
            modeSwitching = false;
            if (target == null) return;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;
            transform.position = focusPoint - rotation * Vector3.forward * distance;
            velocity = Vector3.zero;

            Vector3 lookDir = focusPoint - transform.position;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }

        public void SetFirstPersonMode(bool enabled)
        {
            if (firstPerson != enabled) modeSwitching = true;
            firstPerson = enabled;
            if (enabled)
            {

                driftClock = 0f;
                driftAmpFactor = 0f;
                shoulderManualHold = false;
                freeLookYaw = 0f;
                freeLookPitch = 0f;
                aimYawFilter.Reset();
                aimPitchFilter.Reset();
            }
            else
            {
                SetAimZoom(0f);
                viewAnglesSeeded = false;
            }
        }

        [Tooltip("Seconds of smoothing on the aim zoom - wheel notches and dial steps GLIDE the FOV (and everything derived from it: OTS anchor, pull-back, sensitivity) instead of stepping it.")]
        public float aimZoomSmoothTime = 0.09f;
        float aimZoomTarget;
        float aimZoomVelocity;

        public void SetAimZoom(float chargeFraction01)
        {
            aimZoomTarget = Mathf.Clamp01(chargeFraction01);
            if (!firstPerson)
            {

                aimZoomFraction = aimZoomTarget;
                aimZoomVelocity = 0f;
                ApplyAimZoomFov();
            }
        }

        void ApplyAimZoomFov()
        {
            if (cam == null) cam = GetComponent<UnityEngine.Camera>();
            if (cam == null) return;

            float opticalFraction = aimPreset != null && aimPreset.UsesOverShoulder && firstPerson
                ? Mathf.Pow(aimZoomFraction, aimPreset.zoomCurveExponent) * aimPreset.zoomFovFraction
                : aimZoomFraction;
            cam.fieldOfView = Mathf.Lerp(normalFov, maxZoomFov, opticalFraction);
        }

        [Header("Wall Occlusion")]

        public LayerMask occlusionMask = ~0;
        public float occlusionCheckRadius = 0.25f;

        float yaw;
        float pitch = 15f;
        Vector3 velocity;
        bool yawInitialized;
        bool recentering;
        float recenterTargetYaw;

        readonly List<Renderer> occludedRenderers = new List<Renderer>();
        readonly List<Renderer> stillOccludedThisFrame = new List<Renderer>();

        void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
            if (cam != null) cam.fieldOfView = normalFov;
            followSmoothTime = positionSmoothTime;
        }

        void Start()
        {
            if (target == null) return;
            if (yawInitialized) return;

            Vector3 offset = transform.position - (target.position + Vector3.up * height);
            if (offset.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
        }

        public void SetInitialYaw(float yawDegrees)
        {
            yaw = yawDegrees;
            yawInitialized = true;

            if (target == null) return;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;
            transform.position = focusPoint - rotation * Vector3.forward * distance;

            Vector3 lookDir = focusPoint - transform.position;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }

        public float CurrentYaw => yaw;

        public void ApplyAimEdgeFollow(float aimYawDegrees, float thresholdDegrees, float bandDegrees, float degreesPerSecond)
        {
            float delta = Mathf.DeltaAngle(yaw, aimYawDegrees);
            float excess = Mathf.Abs(delta) - thresholdDegrees;
            if (excess <= 0f) return;
            float intensity = Mathf.Clamp01(excess / Mathf.Max(bandDegrees, 0.1f));
            float step = degreesPerSecond * intensity * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);
            yaw += Mathf.Sign(delta) * Mathf.Min(step, excess);
        }

        public void RecenterBehindTarget(float targetYawDegrees)
        {
            recenterTargetYaw = targetYawDegrees;
            recentering = true;
        }

        void OnEnable()
        {
            lookAction?.action?.Enable();
        }

        void OnDisable()
        {
            lookAction?.action?.Disable();
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (Time.timeScale <= 0f) return;

            if (!startPoseCaptured)
            {
                startYaw = yaw;
                startPitch = pitch;
                startPoseCaptured = true;
            }

            if (firstPerson && Mathf.Abs(aimZoomFraction - aimZoomTarget) > 0.0001f)
            {
                aimZoomFraction = Mathf.SmoothDamp(aimZoomFraction, aimZoomTarget,
                    ref aimZoomVelocity, aimZoomSmoothTime, Mathf.Infinity,
                    Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
                ApplyAimZoomFov();
            }

            Vector2 look = lookAction != null && lookAction.action != null
                ? lookAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            bool lookIsMouseDriven = lookAction != null && lookAction.action != null
                && lookAction.action.activeControl != null
                && lookAction.action.activeControl.device is Mouse;

            if (aimStickOverrideActive && !lookIsMouseDriven) look = aimStickOverrideValue;

            if (mouseLookSuppressed && lookIsMouseDriven) look = Vector2.zero;

            AimRefinementSettings refinement = AimRefinementSettings.Active;
            if (refinement != null && firstPerson && !lookIsMouseDriven && look.sqrMagnitude > 0.0001f)
            {
                look = refinement.ConditionStick(look);
            }

            bool gameRunningSlow = Time.timeScale < 1f && !ignoreSlowMo;
            float dt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime) * (gameRunningSlow ? 0.5f : 1f);

            float fineAimScale = 1f;
            if (look.sqrMagnitude > 0.0001f)
            {
                bool mouseDriven = lookAction != null && lookAction.action != null
                    && lookAction.action.activeControl != null
                    && lookAction.action.activeControl.device is Mouse;
                float reference = mouseDriven ? fineAimMouseReference : fineAimStickReference;
                float t = reference > 0.0001f ? Mathf.Clamp01(look.magnitude / reference) : 1f;
                fineAimScale = Mathf.Lerp(fineAimMinFactor, 1f, t);
            }

            float deviceSpeedScale = 1f;
            if (lookIsMouseDriven)
            {
                deviceSpeedScale = (firstPerson ? mouseAimSpeedMultiplier : mouseOrbitSpeedMultiplier)
                    * KineticEnergy.UI.CameraSpeedSettings.MouseScale;
            }
            else if (aimStickOverrideActive && aimStickOverrideKeyboard)
            {

                deviceSpeedScale = wasdAimCameraSpeedMultiplier * KineticEnergy.UI.CameraSpeedSettings.MouseScale;
            }
            else
            {
                deviceSpeedScale = (playerGrounded ? gamepadGroundedSpeedMultiplier : gamepadAirborneSpeedMultiplier)
                    * KineticEnergy.UI.CameraSpeedSettings.GamepadScale;
            }

            if (look.sqrMagnitude > 0.0001f) recentering = false;

            float fovSensitivityScale = cam != null
                ? Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(normalFov * 0.5f * Mathf.Deg2Rad)
                : 1f;

            if (refinement != null && firstPerson)
            {
                fovSensitivityScale *= Mathf.Lerp(1f, 1f - refinement.zoomExtraPrecision, aimZoomFraction);
            }

            if (recentering)
            {
                yaw = Mathf.MoveTowardsAngle(yaw, recenterTargetYaw, recenterSpeed * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, recenterTargetYaw)) < 0.5f) recentering = false;
            }
            else
            {

                float pitchYawScale = firstPerson
                    ? 1f
                    : Mathf.Max(Mathf.Abs(Mathf.Cos(pitch * Mathf.Deg2Rad)), highAngleYawFloor);
                yaw += look.x * rotationSpeed * fineAimScale * fovSensitivityScale * pitchYawScale * deviceSpeedScale * dt;
            }
            float pitchDelta = (invertY ? look.y : -look.y) * rotationSpeed * fineAimScale * fovSensitivityScale * deviceSpeedScale * dt;
            pitch = Mathf.Clamp(pitch + pitchDelta,
                firstPerson ? firstPersonMinPitch : minPitch,
                firstPerson ? firstPersonMaxPitch : maxPitch);

            if (refinement != null && refinement.smoothingEnabled && firstPerson)
            {
                float filterDt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);
                float cutoff = lookIsMouseDriven ? refinement.mouseMinCutoff : refinement.stickMinCutoff;
                float beta = lookIsMouseDriven ? refinement.mouseBeta : refinement.stickBeta;
                yaw = aimYawFilter.Filter(yaw, filterDt, cutoff, beta);
                pitch = aimPitchFilter.Filter(pitch, filterDt, cutoff, beta);
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;

            if (firstPerson) UpdateFirstPersonViewAngles();

            Vector3 desiredPosition;
            if (OtsAimActive)
            {
                desiredPosition = OtsDesiredPosition(look);
            }
            else if (firstPerson)
            {
                desiredPosition = target.position + rotation * Vector3.forward * firstPersonForwardOffset;
            }
            else
            {
                desiredPosition = focusPoint - rotation * Vector3.forward * distance;
            }

            float activeLaunchFollow = (launchIsVertical ? verticalLaunchFollowSmoothTime : launchFollowSmoothTime)
                * Mathf.Lerp(shortLaunchLagMultiplier, 1f, launchIntensity);

            if (launchInFlight && !firstPerson && remainingFlightSeconds < landingTightenWindowSeconds)
            {
                float approach = Mathf.Clamp01(1f - remainingFlightSeconds / Mathf.Max(landingTightenWindowSeconds, 0.01f));
                activeLaunchFollow *= Mathf.Lerp(1f, landingTightenMultiplier, approach);
            }
            float targetFollow = launchInFlight && !firstPerson ? activeLaunchFollow : positionSmoothTime;
            if (targetFollow > followSmoothTime)
            {
                followSmoothTime = targetFollow;
            }
            else
            {

                float relaxRate = Mathf.Max((followSmoothTime - targetFollow) / Mathf.Max(followLagRecoverySeconds, 0.01f), 0.05f);
                followSmoothTime = Mathf.MoveTowards(followSmoothTime, targetFollow, relaxRate * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
            }

            float smoothTime;
            if (launchInFlight && !firstPerson) smoothTime = followSmoothTime;
            else if (modeSwitching) smoothTime = OtsAimActive ? aimPreset.blendInTime : modeSwitchSmoothTime;
            else if (OtsAimActive) smoothTime = 0.02f;
            else smoothTime = followSmoothTime;

            if (OtsAimActive && !modeSwitching)
            {

                transform.position = desiredPosition;
                velocity = Vector3.zero;
            }
            else if (snapPositionNextUpdate)
            {

                transform.position = desiredPosition;
                velocity = Vector3.zero;
                snapPositionNextUpdate = false;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime,
                    Mathf.Infinity, Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
            }
            if (modeSwitching && (transform.position - desiredPosition).sqrMagnitude < 0.0025f) modeSwitching = false;

            if (firstPerson)
            {

                if (freeLookActive)
                {
                    float fdt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);
                    freeLookYaw += freeLookInput.x * rotationSpeed * fdt;
                    freeLookPitch -= freeLookInput.y * rotationSpeed * fdt;

                    float cone = aimPreset != null ? Mathf.Max(aimPreset.freeLookConeAngle, 1f) : 45f;
                    Vector2 offset = new Vector2(freeLookYaw, freeLookPitch);
                    if (offset.magnitude > cone)
                    {
                        offset = offset.normalized * cone;
                        freeLookYaw = offset.x;
                        freeLookPitch = offset.y;
                    }
                }

                float appliedYaw = viewYaw
                    + (freeLookActive ? freeLookYaw : 0f)
                    + (OtsAimActive ? driftYawCurrent : 0f);
                float appliedPitch = viewPitch
                    + (freeLookActive ? freeLookPitch : 0f)
                    + (OtsAimActive ? driftPitchCurrent : 0f);
                transform.rotation = Quaternion.Euler(appliedPitch, appliedYaw, 0f);
            }
            else
            {

                Vector3 lookDir = focusPoint - transform.position;
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
            }

            UpdateWallOcclusion(focusPoint);

            UpdatePlayerProximityHiding();
        }

        float driftYawCurrent;
        float driftPitchCurrent;

        Vector3 OtsDesiredPosition(Vector2 look)
        {
            float udt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);

            bool holdDrift = aimPreset.pauseDriftWhileAiming && look.sqrMagnitude > 0.0001f;
            if (!holdDrift) driftClock += udt;
            driftAmpFactor = Mathf.MoveTowards(driftAmpFactor, 1f, udt / Mathf.Max(aimPreset.driftRampIn, 0.01f));

            float phase = driftClock / Mathf.Max(aimPreset.driftPeriod, 0.01f) * Mathf.PI * 2f;
            driftYawCurrent = Mathf.Sin(phase) * aimPreset.driftYawAmplitude * driftAmpFactor;
            driftPitchCurrent = Mathf.Sin(phase + aimPreset.driftPhaseOffset * Mathf.Deg2Rad)
                * aimPreset.driftPitchAmplitude * driftAmpFactor;

            float viewY = viewAnglesSeeded ? viewYaw : yaw;
            float viewP = viewAnglesSeeded ? viewPitch : pitch;
            if (freeLookActive)
            {
                viewY += freeLookYaw;
                viewP += freeLookPitch;
            }
            Quaternion viewRotation = Quaternion.Euler(viewP + driftPitchCurrent, viewY + driftYawCurrent, 0f);

            if (autoShoulder && !shoulderManualHold)
            {
                float currentClear = ShoulderClearance(AnchoredPosition(viewRotation, shoulderTarget));
                float otherClear = ShoulderClearance(AnchoredPosition(viewRotation, -shoulderTarget));
                if (otherClear > currentClear + autoShoulderMargin) shoulderTarget = -shoulderTarget;
            }
            shoulderCurrent = Mathf.SmoothDamp(shoulderCurrent, shoulderTarget, ref shoulderVelocity,
                shoulderSwapSmoothTime, Mathf.Infinity, udt);

            Vector3 desired = AnchoredPosition(viewRotation, shoulderCurrent);

            Vector3 toCamera = desired - target.position;
            float span = toCamera.magnitude;
            if (span > 0.001f && Physics.SphereCast(target.position, aimPreset.camCollisionRadius,
                toCamera / span, out RaycastHit hit, span, occlusionMask, QueryTriggerInteraction.Ignore))
            {
                bool isPlayer = hit.collider.transform == target || hit.collider.transform.IsChildOf(target);
                if (!isPlayer)
                {
                    desired = target.position + toCamera / span * Mathf.Max(hit.distance - 0.05f, 0.3f);
                }
            }
            return desired;
        }

        void UpdateFirstPersonViewAngles()
        {

            if (!trajectoryFramingEnabled)
            {
                viewYaw = yaw;
                viewPitch = pitch;
                viewAnglesSeeded = true;
                framingJustStarted = false;
                framedWeightCurrent = 0f;
                return;
            }

            float targetYaw = yaw;
            float targetPitch = pitch;

            Vector3 framingOrigin = target != null ? target.position : transform.position;

            if (framingActive)
            {
                if (!framingWasActive || framingJustStarted)
                {
                    framingPointSmoothed = framingPoint;
                    framingPointVelocity = Vector3.zero;
                }
                else
                {
                    framingPointSmoothed = Vector3.SmoothDamp(framingPointSmoothed, framingPoint,
                        ref framingPointVelocity, framingPointSmoothTime, Mathf.Infinity,
                        Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
                }
            }
            framingWasActive = framingActive;

            Vector3 framingDir = framingPointSmoothed - framingOrigin;
            float framingYawDelta = 0f;
            float framingPitchDelta = 0f;
            float targetWeight = 0f;
            bool framingValid = framingActive && framingDir.sqrMagnitude > 0.0001f;
            if (framingValid)
            {
                float framingYaw = Mathf.Atan2(framingDir.x, framingDir.z) * Mathf.Rad2Deg;
                float framingPitch = -Mathf.Asin(Mathf.Clamp(framingDir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                framingYawDelta = Mathf.DeltaAngle(yaw, framingYaw);
                framingPitchDelta = framingPitch - pitch;

                float deviation = Mathf.Max(Mathf.Abs(framingYawDelta), Mathf.Abs(framingPitchDelta));
                if (deviation <= framingMaxDeviation)
                {

                    float blendSpan = Mathf.Max(framingMaxDeviation - framingBlendStartDegrees, 0.01f);
                    float t = Mathf.Clamp01((deviation - framingBlendStartDegrees) / blendSpan);
                    targetWeight = 1f - t * t;
                }
            }

            framedWeightCurrent = Mathf.MoveTowards(framedWeightCurrent, targetWeight,
                framingBlendSpeed * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
            if (framingValid && framedWeightCurrent > 0.0001f)
            {
                targetYaw = yaw + framingYawDelta * framedWeightCurrent;
                targetPitch = pitch + framingPitchDelta * framedWeightCurrent;
            }

            if (framingJustStarted || !viewAnglesSeeded)
            {
                viewYaw = targetYaw;
                viewPitch = targetPitch;
                viewAnglesSeeded = true;
                framingJustStarted = false;
                framedWeightCurrent = targetWeight;
            }
            else
            {
                float turnStep = framingTurnSpeed * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);
                viewYaw = Mathf.MoveTowardsAngle(viewYaw, targetYaw, turnStep);
                viewPitch = Mathf.MoveTowards(viewPitch, targetPitch, turnStep);
            }
        }

        Vector3 AnchoredPosition(Quaternion viewRotation, float shoulderSign)
        {

            Vector2 anchor = Vector2.Lerp(aimPreset.playerViewportAnchor, aimPreset.playerViewportAnchorZoomed, aimZoomFraction);
            float anchorX = 0.5f + (anchor.x - 0.5f) * shoulderSign;
            float anchorY = anchor.y;

            float fov = cam != null ? cam.fieldOfView : normalFov;
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            float tanHalfY = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float tanHalfX = tanHalfY * aspect;

            Vector3 anchorRay = new Vector3(
                (anchorX * 2f - 1f) * tanHalfX,
                (anchorY * 2f - 1f) * tanHalfY,
                1f).normalized;

            float baseTanHalfY = Mathf.Tan(normalFov * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Min(aimPreset.otsBack * (baseTanHalfY / Mathf.Max(tanHalfY, 0.01f)),
                Mathf.Max(aimPreset.otsBackZoomed, aimPreset.otsBack));
            return target.position - viewRotation * (anchorRay * Mathf.Max(distance, 0.5f));
        }

        float ShoulderClearance(Vector3 position)
        {
            Vector3 toCamera = position - target.position;
            float span = toCamera.magnitude;
            if (span < 0.001f || aimPreset == null) return 1f;
            if (Physics.SphereCast(target.position, aimPreset.camCollisionRadius, toCamera / span,
                out RaycastHit hit, span, occlusionMask, QueryTriggerInteraction.Ignore))
            {
                bool isPlayer = hit.collider.transform == target || hit.collider.transform.IsChildOf(target);
                if (!isPlayer) return hit.distance / span;
            }
            return 1f;
        }

        void HideOccluder(Renderer occluder)
        {
            if (occluder == null) return;
            stillOccludedThisFrame.Add(occluder);
            if (occludedRenderers.Contains(occluder)) return;
            occluder.enabled = false;
            occludedRenderers.Add(occluder);
        }

        void UpdateWallOcclusion(Vector3 focusPoint)
        {
            stillOccludedThisFrame.Clear();

            Vector3 origin = transform.position;
            Vector3 toTarget = focusPoint - origin;
            float distance = toTarget.magnitude;

            if (distance > 0.0001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(origin, occlusionCheckRadius, toTarget / distance, distance, occlusionMask, QueryTriggerInteraction.Ignore);
                foreach (RaycastHit hit in hits)
                {

                    if (target != null && (hit.collider.transform == target || hit.collider.transform.IsChildOf(target))) continue;

                    Transform groupRoot = hit.collider.transform;
                    if (groupRoot.parent != null
                        && groupRoot.GetComponent<KineticEnergy.Level.DamageWalls>() != null
                        && groupRoot.parent.GetComponent<Renderer>() != null)
                    {
                        groupRoot = groupRoot.parent;
                    }

                    HideOccluder(hit.collider.GetComponent<Renderer>());
                    HideOccluder(groupRoot.GetComponent<Renderer>());
                    foreach (KineticEnergy.Level.DamageWalls shell in
                        groupRoot.GetComponentsInChildren<KineticEnergy.Level.DamageWalls>(true))
                    {
                        if (shell.transform != groupRoot) HideOccluder(shell.GetComponent<Renderer>());
                    }
                }
            }

            for (int i = occludedRenderers.Count - 1; i >= 0; i--)
            {
                if (stillOccludedThisFrame.Contains(occludedRenderers[i])) continue;

                if (occludedRenderers[i] != null)
                {

                    KineticEnergy.Level.TargetSphere sphere =
                        occludedRenderers[i].GetComponentInParent<KineticEnergy.Level.TargetSphere>();
                    if (sphere == null || sphere.IsActive)
                    {
                        occludedRenderers[i].enabled = true;
                    }
                }
                occludedRenderers.RemoveAt(i);
            }
        }
    }
}

