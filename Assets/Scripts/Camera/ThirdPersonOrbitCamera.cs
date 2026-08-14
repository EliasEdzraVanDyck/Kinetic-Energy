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
        // Raised in magnitude from -20 - direct request: "allow the camera to be pointed upwards
        // as well, for the vertical segment it was hard not being able to see what was above me".
        // Negative pitch swings the camera BELOW the target, looking UP (see the position formula
        // below: desiredPosition = focusPoint - rotation*forward*distance, and positive pitch
        // tilts forward toward -Y, so negative pitch tilts it toward +Y, putting the camera
        // underneath) - -20 only allowed a shallow 20-degree glance upward, nowhere near enough to
        // see a platform directly overhead. -75 mirrors maxPitch's own margin from the 90-degree
        // gimbal-adjacent instability described below, applied to the opposite pole.
        public float minPitch = -75f;
        // Pulled back from an earlier 89 - that was close enough to true top-down (90) that
        // Quaternion.LookRotation(lookDir, Vector3.up) started reading as visibly "spinning"
        // while rotating: as lookDir approaches anti-parallel to the Vector3.up hint, tiny
        // positional differences (SmoothDamp lag makes the ACTUAL camera position lag behind the
        // theoretical orbit spot, so this isn't just the exact 90-degree instant) flip which way
        // LookRotation resolves roll, which reads as spin. 75 keeps a real margin from that
        // degenerate zone while still allowing a dramatically steep, near-top-down view.
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

        float followSmoothTime; // the eased, currently-active follow smoothing

        // Set per frame by KineticCubeController - true for the whole launch flight, so the
        // orbit follow uses the lazier launch smoothing while the cube rockets away.
        // Vertical flights (up-charge / pound) use their own slightly tighter value.
        bool launchInFlight;
        bool launchIsVertical;
        float launchIntensity = 1f;

        [Tooltip("How much LONGER the launch-follow smoothing runs for a zero-charge launch (1 = no stretch). Weak launches are slow, so without this their camera lag is over before it reads; full-charge launches always use the base values.")]
        public float shortLaunchLagMultiplier = 2f;

        public void SetLaunchInFlight(bool inFlight, bool vertical, float intensity01)
        {
            launchInFlight = inFlight;
            launchIsVertical = vertical;
            launchIntensity = Mathf.Clamp01(intensity01);
        }

        [Header("Auto Recenter")]
        // Used by forward hold-charge launches to swing the camera back behind the player
        // after firing. Cancels itself the instant the player provides any manual look input,
        // so it never fights the player's own camera control.
        public float recenterSpeed = 240f;

        [Header("Input")]
        public InputActionReference lookAction;

        // While the midair aim is open the LEFT stick steers the camera (the right stick is
        // the energy dial there): any non-mouse look input is substituted by the stick value
        // fed in here each frame. Mouse aiming is deliberately unaffected - the substitution
        // only applies when the look action isn't mouse-driven.
        bool aimStickOverrideActive;
        Vector2 aimStickOverrideValue;
        bool aimStickOverrideKeyboard; // the override is WASD (grounded aim), not a gamepad stick

        [Header("Keyboard & Mouse Speed")]
        // Direct feedback: with mouse the camera is too fast outside aiming and slightly
        // too fast while aiming. These scale MOUSE/WASD-driven look only - gamepad sticks
        // are untouched.
        [Tooltip("Mouse look speed multiplier for the ordinary third-person orbit (not aiming).")]
        [Range(0.1f, 1f)] public float mouseOrbitSpeedMultiplier = 0.6f;
        [Tooltip("Mouse look speed multiplier during the midair first-person aim.")]
        [Range(0.1f, 1f)] public float mouseAimSpeedMultiplier = 0.85f;
        [Tooltip("Speed multiplier for the WASD-driven camera during the grounded aim.")]
        [Range(0.1f, 1f)] public float wasdAimCameraSpeedMultiplier = 0.85f;

        [Header("Gamepad Speed")]
        // Direct feedback: the stick camera should be a bit quicker than baseline - +20%
        // grounded, +10% airborne. Mouse/WASD input never touches these.
        [Tooltip("Gamepad look speed multiplier while the player is GROUNDED.")]
        [Range(0.5f, 2f)] public float gamepadGroundedSpeedMultiplier = 1.2f;
        [Tooltip("Gamepad look speed multiplier while the player is AIRBORNE (flights and midair aim).")]
        [Range(0.5f, 2f)] public float gamepadAirborneSpeedMultiplier = 1.1f;

        // Fed per frame by KineticCubeController - the gamepad multipliers key off it.
        bool playerGrounded;

        public void SetPlayerGrounded(bool grounded)
        {
            playerGrounded = grounded;
        }
        // While the grounded aim's "Aim: Mouse" option is steering the launch direction with
        // mouse delta (KineticCubeController.groundedAimWithMouse), mouse-driven look input is
        // ignored so one hand motion doesn't rotate the camera and the aim arrow together.
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

        // EnergyEconomy1's straight-up/ground-pound charges: the camera keeps FULL speed while
        // the game runs slow ("the camera should not be bound to the gamespeed" - direct
        // request) - set per frame by KineticCubeController.
        bool ignoreSlowMo;

        public void SetIgnoreSlowMo(bool ignore)
        {
            ignoreSlowMo = ignore;
        }

        // While a launch is aimed MIDAIR the orbit frames the TRAJECTORY instead of the player
        // (direct request: "the visual line should be in the middle of the screen") - the focus
        // point becomes the line's midpoint outright, so the line is genuinely centered and the
        // player simply falls out of frame on a long arc, which is the intent. Distance from
        // First person sits at the centre of the player's FRONT FACE, pushed this far further
        // along the view direction so the cube itself is never in shot (direct request,
        // replacing the look-at-the-landing-point framing, which whipped around whenever the
        // predicted landing jumped). The cube is 1 unit across, so 0.5 reaches its front face
        // and the rest is clearance.
        public float firstPersonForwardOffset = 0.75f;
        // Position smoothing used for the frame(s) right after a first-person <-> third-person
        // switch (direct request: the change should be near instant). Ordinary movement keeps
        // positionSmoothTime; only the mode change uses this much snappier value.
        public float modeSwitchSmoothTime = 0.02f;

        bool modeSwitching;

        // First-person aim looks AT the predicted landing point (the cursor at the end of the
        // dotted line) rather than along the raw launch ray - the arc drops under gravity, so
        // the two differ. Snapped into place the instant aiming starts, then eased whenever
        // the landing point MOVES (new target, changed energy), which is what stops the
        // violent whipping when a target jumps (direct request).
        public float framingTurnSpeed = 300f;
        // How close (degrees, per axis) the cursor must be to the AIM for the view to centre
        // on it. Past this the view stays glued to the aim - never pulled partway - so a
        // steeply-up shot, which arcs over and lands far BELOW you, can't drag the view down
        // and read as an upward pitch cap (direct report, twice).
        public float framingMaxDeviation = 45f;

        bool framingActive;
        Vector3 framingPoint;
        bool framingJustStarted;

        // The first-person VIEW's own yaw/pitch, eased toward the framing target. Kept as
        // separate angles (not a quaternion slerp) on purpose: interpolating between two
        // level rotations along the shortest quaternion arc rolls the horizon mid-way, which
        // read as the camera's Z rotation changing while moving between two targets. Building
        // the rotation from yaw/pitch alone keeps roll at exactly zero on every frame.
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
        // "Slow down the speed of aiming if you make fine adjustments with your mouse or stick,
        // if you make wider less fine movements, the speed should be the same as now" (direct
        // request) - rotation speed scales with how hard the input is being pushed, from
        // fineAimMinFactor at a barely-moving input up to full speed at/beyond the reference
        // magnitude. The reference is per-device because the two input types live on completely
        // different scales: a stick deflection maxes out around 1.0, while a mouse delta is
        // pixels-per-frame and routinely reads 10+ during a fast sweep - a single shared
        // threshold would either make the stick never reach full speed or make every mouse
        // movement count as "wide".
        [Range(0f, 1f)] public float fineAimMinFactor = 0.3f;
        public float fineAimStickReference = 0.9f;
        public float fineAimMouseReference = 8f;

        [Header("First Person Aim")]
        // FastPaced scheme only (see KineticCubeController.UpdateFastPacedScheme) -
        // SetFirstPersonMode collapses the orbit to sit exactly at the focus point instead of
        // orbiting at `distance`, and SetAimZoom narrows the field of view as charge builds so a
        // long-charged shot's distant landing spot stays legible instead of shrinking to a speck
        // - direct request: "the longer you charge the more you need to zoom in on the landing
        // spot". Both are no-ops for every other scheme, which never calls them.
        public float normalFov = 60f;
        public float maxZoomFov = 20f;
        // Pitch limits while first person is active - near-vertical is SAFE there (first person
        // applies the raw rotation directly, none of the LookRotation-at-target degeneracy the
        // +/-75 orbit limits guard against), and the midair aim needs to look almost straight
        // down to line up pounds.
        public float firstPersonMinPitch = -89f;
        public float firstPersonMaxPitch = 89f;

        UnityEngine.Camera cam;
        bool firstPerson;

        // ---------- Aim-camera variant support (the depth-perception playtest) ----------
        // Baseline (A) keeps the frozen first-person aim EXACTLY as it always was; the OTS
        // variants (B/C) position the camera over-the-shoulder behind the launch vector
        // with a slow drift orbit for motion parallax, looking at the predicted landing
        // point. Which one is active comes from AimCameraVariantController via the preset.
        AimCameraPreset aimPreset;
        float aimZoomFraction;   // last energy-dial fraction fed to SetAimZoom
        float driftClock;        // unscaled seconds into the drift ellipse
        float driftAmpFactor;    // 0..1 ramp of the drift amplitude

        // Free-look (variants E/F): rotates the VIEW only - the launch vector (yaw/pitch)
        // and the predicted landing stay exactly where they are. Fed per frame by
        // KineticCubeController; reset when a fresh aim opens.
        bool freeLookActive;
        Vector2 freeLookInput;
        float freeLookYaw;
        float freeLookPitch;

        // Aim-refinement lab (AimRefinementSettings.Active - only present in its scene):
        // One-Euro smoothing of the midair aim's yaw/pitch, reset on every aim open.
        readonly OneEuroFilter aimYawFilter = new OneEuroFilter();
        readonly OneEuroFilter aimPitchFilter = new OneEuroFilter();

        public void SetFreeLook(bool active, Vector2 input)
        {
            freeLookActive = active;
            freeLookInput = active ? input : Vector2.zero;
        }

        // Which shoulder the OTS offset sits over: +1 = right (player appears left of
        // centre), -1 = left. AUTO mode picks the clearer side while aiming (obstruction-
        // based, the way cover shooters do it) and glides across rather than snapping;
        // Q / Right Stick Click still swaps manually and holds that choice for the rest of
        // the current aim window (auto resumes on the next aim).
        [Tooltip("Automatically hold the clearer shoulder during OTS aims: when geometry squeezes the current side and the mirrored side is clear, the camera glides across. Manual swaps (Q / Right Stick Click) override it for the rest of that aim.")]
        public bool autoShoulder = true;
        [Tooltip("Extra clearance (fraction of the offset span) the OTHER side must have before an auto-swap triggers - hysteresis so the camera never flip-flops.")]
        [Range(0.05f, 0.6f)] public float autoShoulderMargin = 0.25f;
        [Tooltip("Unscaled seconds the shoulder-swap glide takes.")]
        public float shoulderSwapSmoothTime = 0.22f;

        float shoulderTarget = 1f;
        float shoulderCurrent = 1f;
        float shoulderVelocity;
        bool shoulderManualHold; // Q was pressed during this aim - auto stays out of it

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

        // World-space direction this camera is currently looking - the midair aim fires
        // exactly along this, so the shot always goes exactly where the first-person view points.
        public Vector3 AimForward => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        // Called the instant a MIDAIR launch fires: the camera jumps straight to its third-
        // person orbit slot behind the player and follows from there. Without this the
        // camera exits first person AT the player - the launch simply flies past it and the
        // catch-up reads as instant, no matter the smooth time. Starting from the orbit slot
        // makes the launch trailing develop exactly like a grounded launch's.
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
            if (firstPerson != enabled) modeSwitching = true; // near-instant transition, see modeSwitchSmoothTime
            firstPerson = enabled;
            if (enabled)
            {
                // Fresh aim window: drift starts from rest and ramps in; a manual shoulder
                // hold expires (auto resumes); any free-look offset resets to centred.
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

        public void SetAimZoom(float chargeFraction01)
        {
            if (cam == null) cam = GetComponent<UnityEngine.Camera>();
            if (cam == null) return;
            aimZoomFraction = Mathf.Clamp01(chargeFraction01);
            cam.fieldOfView = Mathf.Lerp(normalFov, maxZoomFov, aimZoomFraction);
        }

        [Header("Wall Occlusion")]
        // "If the camera is looking at a wall from the outside, you should be able to look
        // through the wall" (direct request - commonly called camera occlusion culling). There
        // was no camera-collision handling here at all before this - a wall ending up between
        // the orbit position and the player (e.g. the camera orbits to just outside one of
        // Level2's hallway walls) just blocked the view outright. Hides (doesn't fade) whichever
        // renderers are directly between the camera and the player each frame, restoring them
        // the instant they're no longer in the way.
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
            if (yawInitialized) return; // already set externally (CameraStartFacing) before this ran

            Vector3 offset = transform.position - (target.position + Vector3.up * height);
            if (offset.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
        }

        // Called from CameraStartFacing.Awake() - guaranteed to run before this component's own
        // Start() (Unity runs every Awake() in the scene before any Start()), so it always wins
        // over the offset-based auto-calculation above. Also snaps position immediately rather
        // than letting LateUpdate's SmoothDamp ease into the new orbit spot over a few frames,
        // so the camera is already correctly framed on the very first rendered frame instead of
        // visibly sliding into place right as the level appears.
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

        // Starts a smooth (not instant) swing of the orbit yaw back to directly behind
        // targetYawDegrees (the player's new facing) - "move behind the player again", not
        // "snap" (that's what SetInitialYaw is for, at level load). Actual interpolation happens
        // in LateUpdate so it can be interrupted cleanly by manual look input at any point.
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

            Vector2 look = lookAction != null && lookAction.action != null
                ? lookAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            bool lookIsMouseDriven = lookAction != null && lookAction.action != null
                && lookAction.action.activeControl != null
                && lookAction.action.activeControl.device is Mouse;

            // Left-stick aim substitution (see SetAimStickOverride) - replaces stick-driven
            // look input only; a mouse-driven frame keeps the mouse delta untouched.
            if (aimStickOverrideActive && !lookIsMouseDriven) look = aimStickOverrideValue;

            // Mouse-driven look is dropped while the mouse is busy steering the grounded aim -
            // see SetMouseLookSuppressed.
            if (mouseLookSuppressed && lookIsMouseDriven) look = Vector2.zero;

            // Aim-refinement lab: stick input gets the re-scaled deadzone + response curve
            // (finer control across the lower stick range; gamepad only, mouse untouched).
            AimRefinementSettings refinement = AimRefinementSettings.Active;
            if (refinement != null && !lookIsMouseDriven && look.sqrMagnitude > 0.0001f)
            {
                look = refinement.ConditionStick(look);
            }

            // Unscaled, not Time.deltaTime - Time.deltaTime already shrinks 1:1 with
            // Time.timeScale, which would otherwise make the camera merely ride along with
            // whatever fraction chargeTimeScale happens to be. Instead the camera runs at a
            // flat HALF speed whenever the game is running slow. Strictly < 1, not != 1 - a
            // launch in flight speeds the game UP (KineticCubeController.launchFlightTimeScale)
            // and camera/aiming must be unaffected by that, which unscaled time gives for free.
            //
            // The frame right after a scene reload (Restart, or the new fall-reset) can have an
            // abnormally large deltaTime - loading everything (Player/Camera/PauseSystem, plus
            // Level1's platform generation) takes real time before the next frame renders.
            // Multiplied straight into this accumulator, holding the stick at that exact moment
            // (plausible right after falling or hitting Restart) could snap yaw/pitch to a
            // garbage value in one frame, making the camera look broken/unresponsive afterward -
            // still a risk with unscaled time, so the clamp stays.
            bool gameRunningSlow = Time.timeScale < 1f && !ignoreSlowMo;
            float dt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime) * (gameRunningSlow ? 0.5f : 1f);

            // Fine-aim scaling (see the Fine Aim header comment): a gentle input rotates at
            // fineAimMinFactor of normal speed, ramping linearly up to full speed at the active
            // device's reference magnitude. The device is read from whichever control is
            // actually driving the action THIS frame, so switching between mouse and gamepad
            // mid-session picks the right scale automatically.
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

            // Keyboard & mouse speed scaling (direct feedback: mouse camera too fast outside
            // aiming, slightly too fast while aiming) - gamepad sticks pass through at 1.
            float deviceSpeedScale = 1f;
            if (lookIsMouseDriven) deviceSpeedScale = firstPerson ? mouseAimSpeedMultiplier : mouseOrbitSpeedMultiplier;
            else if (aimStickOverrideActive && aimStickOverrideKeyboard) deviceSpeedScale = wasdAimCameraSpeedMultiplier;
            else deviceSpeedScale = playerGrounded ? gamepadGroundedSpeedMultiplier : gamepadAirborneSpeedMultiplier;

            // Manual input always wins outright, the instant there is any - recentering only
            // ever happens while the player isn't already telling the camera what to do.
            if (look.sqrMagnitude > 0.0001f) recentering = false;

            // FOV/sensitivity compensation: without this the aim zoom silently changed the
            // aim feel - the same mouse motion sweeps the same WORLD angle at 20 degrees FOV
            // as at 60, which covers ~3x the SCREEN, so zoomed-in aiming was ~3x twitchier.
            // Scaling by tan(fov/2) keeps screen-space sensitivity constant across the zoom.
            // Exactly 1 whenever the FOV is at its normal value, so nothing else changes.
            float fovSensitivityScale = cam != null
                ? Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(normalFov * 0.5f * Mathf.Deg2Rad)
                : 1f;
            // Aim lab: deliberately UNDER-compensate at high zoom - a touch slower than
            // geometrically correct, because precision matters most exactly then.
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
                // See highAngleYawFloor: steep orbit pitches damp the yaw rate so near-top-
                // down views don't spin around the player uncontrollably fast.
                float pitchYawScale = firstPerson
                    ? 1f
                    : Mathf.Max(Mathf.Abs(Mathf.Cos(pitch * Mathf.Deg2Rad)), highAngleYawFloor);
                yaw += look.x * rotationSpeed * fineAimScale * fovSensitivityScale * pitchYawScale * deviceSpeedScale * dt;
            }
            float pitchDelta = (invertY ? look.y : -look.y) * rotationSpeed * fineAimScale * fovSensitivityScale * deviceSpeedScale * dt;
            pitch = Mathf.Clamp(pitch + pitchDelta,
                firstPerson ? firstPersonMinPitch : minPitch,
                firstPerson ? firstPersonMaxPitch : maxPitch);

            // Aim lab: One-Euro smoothing of the midair aim's angles - adaptive, so a
            // nearly-still aim is rock-steady on distant landings (angular tremble becomes
            // metres out there) while fast sweeps pass through unlagged. Per-device tuning;
            // filters are seeded fresh at every aim open.
            if (refinement != null && refinement.smoothingEnabled && firstPerson)
            {
                float filterDt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);
                float cutoff = lookIsMouseDriven ? refinement.mouseMinCutoff : refinement.stickMinCutoff;
                float beta = lookIsMouseDriven ? refinement.mouseBeta : refinement.stickBeta;
                yaw = aimYawFilter.Filter(yaw, filterDt, cutoff, beta);
                pitch = aimPitchFilter.Filter(pitch, filterDt, cutoff, beta);
            }

            // Traditional 3rd-person platformer orbit: position swings around the target on
            // both yaw and pitch, always framing it, rather than tilting/panning in place.
            // firstPerson (the midair aim's mode - see SetFirstPersonMode) collapses this to
            // sit exactly at the focus point instead of orbiting at `distance`, using the raw
            // look rotation directly rather than LookRotation-at-target (which degenerates at
            // zero distance, where focusPoint - transform.position is ~zero and has no reliable
            // direction). Reuses the same SmoothDamp position glide either way, so switching in
            // or out of first person eases smoothly rather than snapping.
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;

            // First person (Baseline): the player's own centre pushed forward past its front
            // face - NOT the focus point, which carries the third-person `height` lift.
            // OTS variants: over-the-shoulder behind the launch vector, with drift parallax.
            // Third person keeps its ordinary orbit.
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

            // A mode switch uses the much shorter smooth time until the camera has essentially
            // arrived - so first <-> third person reads as a snap without the hard teleport
            // (and without the leftover SmoothDamp velocity that a teleport would keep).
            // Entering an OTS aim instead BLENDS over the preset's blendInTime (never snaps).
            // Priority: an in-flight launch's lazy trailing beats everything (including the
            // mode-switch snap - firing out of the midair aim IS a mode switch, and the snap
            // was eating the launch lag there); the snap still covers aim open/cancel.
            // Engaging the lag is INSTANT (the launch moment should trail immediately), but
            // releasing it EASES over followLagRecoverySeconds - snapping straight back to
            // the tight follow made the camera lunge at the player the frame a flight ended.
            float activeLaunchFollow = (launchIsVertical ? verticalLaunchFollowSmoothTime : launchFollowSmoothTime)
                * Mathf.Lerp(shortLaunchLagMultiplier, 1f, launchIntensity);
            float targetFollow = launchInFlight && !firstPerson ? activeLaunchFollow : positionSmoothTime;
            if (targetFollow > followSmoothTime)
            {
                followSmoothTime = targetFollow;
            }
            else
            {
                // Rate derives from the REMAINING gap (an exponential-style decay with
                // followLagRecoverySeconds as its time constant). The old fixed rate came
                // from the base values only, so relaxing from a short-launch-stretched
                // smoothing took over a second - the camera felt drugged after landing.
                float relaxRate = Mathf.Max((followSmoothTime - targetFollow) / Mathf.Max(followLagRecoverySeconds, 0.01f), 0.05f);
                followSmoothTime = Mathf.MoveTowards(followSmoothTime, targetFollow, relaxRate * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
            }

            float smoothTime;
            if (launchInFlight && !firstPerson) smoothTime = followSmoothTime;
            else if (modeSwitching) smoothTime = OtsAimActive ? aimPreset.blendInTime : modeSwitchSmoothTime;
            else if (OtsAimActive) smoothTime = 0.02f; // near-rigid: the screen anchor must not slosh
            else smoothTime = followSmoothTime;
            // Explicit UNSCALED delta time: SmoothDamp's default is Time.deltaTime, which the
            // in-flight game-speed-up inflates 2-3x - the camera was catching up that much
            // faster than the smooth time promised, which read as a near-instant snap on
            // midair launches. Real-seconds smoothing keeps the trailing consistent at any
            // game speed (slow-mo included, matching how the rotation input already works).
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime,
                Mathf.Infinity, Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
            if (modeSwitching && (transform.position - desiredPosition).sqrMagnitude < 0.0025f) modeSwitching = false;

            if (firstPerson)
            {
                // The view target in yaw/pitch: the aim itself, or the cursor when it's
                // CLOSE to the aim. Centering only engages while the cursor sits within
                // framingMaxDeviation of the aim on both axes - a cursor further off (an
                // up-aimed shot always lands far BELOW the aim) no longer drags the view at
                // all, so a steep upward aim shows exactly where you're aiming instead of
                // reading as a pitch cap. Never pulled partway: it's the cursor or the aim.
                float targetYaw = yaw;
                float targetPitch = pitch;
                Vector3 framingDir = framingPoint - transform.position;
                if (framingActive && framingDir.sqrMagnitude > 0.0001f)
                {
                    float framingYaw = Mathf.Atan2(framingDir.x, framingDir.z) * Mathf.Rad2Deg;
                    float framingPitch = -Mathf.Asin(Mathf.Clamp(framingDir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float framingYawDelta = Mathf.DeltaAngle(yaw, framingYaw);
                    float framingPitchDelta = framingPitch - pitch;
                    if (Mathf.Abs(framingYawDelta) <= framingMaxDeviation && Mathf.Abs(framingPitchDelta) <= framingMaxDeviation)
                    {
                        targetYaw = yaw + framingYawDelta;
                        targetPitch = pitch + framingPitchDelta;
                    }
                }

                if (framingJustStarted || !viewAnglesSeeded)
                {
                    // Aiming just began - land on the cursor immediately.
                    viewYaw = targetYaw;
                    viewPitch = targetPitch;
                    viewAnglesSeeded = true;
                    framingJustStarted = false;
                }
                else
                {
                    // The landing point moved (retarget, energy change) - ease across at a
                    // capped rate, per axis. Unscaled so it feels the same during the aim's
                    // bullet-time.
                    float turnStep = framingTurnSpeed * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);
                    viewYaw = Mathf.MoveTowardsAngle(viewYaw, targetYaw, turnStep);
                    viewPitch = Mathf.MoveTowards(viewPitch, targetPitch, turnStep);
                }

                // Free-look (E and F): the accumulated offset rotates the VIEW in place, on
                // top of the aim framing - the aim vector, cursor, and camera POSITION never
                // move with it. Clamped to a cone around the default view (direct request:
                // 45 degrees in all directions, on the preset).
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

                // Built from yaw/pitch alone - the roll (Z) component is always exactly
                // zero. The OTS drift lives HERE too, matching the anchored-position math
                // exactly - that identity is what keeps the player pinned while the world
                // sways (the parallax).
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
                // Look directly at the target from wherever the camera ACTUALLY is, rather than
                // reusing the theoretical orbit rotation - position lags behind via SmoothDamp, so
                // during fast stick movement the two used to disagree and the camera briefly didn't
                // point exactly at the player.
                Vector3 lookDir = focusPoint - transform.position;
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
            }

            // Always called, even in first person - UpdateWallOcclusion's own distance check
            // already no-ops the sphere-cast when the camera sits ~on top of the target (exactly
            // the first-person case), but the loop that RESTORES a renderer hidden just before
            // switching into first person still needs to run every frame, or a wall occluded the
            // instant before RMB was pressed would stay disabled for the entire aim.
            UpdateWallOcclusion(focusPoint);
        }

        // Over-the-shoulder aim placement - SCREEN-ANCHORED (direct request: the player
        // must sit stably in the corner no matter the aim angle). The camera's view
        // rotation is decided first (the framing block's angles plus drift/free-look);
        // the position is then solved so the player projects EXACTLY onto the preset's
        // viewport anchor: position = player - rotation * (anchorRay * distance). Stable
        // by construction at any pitch, any zoom (the FOV feeds the ray), any drift.
        float driftYawCurrent;
        float driftPitchCurrent;

        Vector3 OtsDesiredPosition(Vector2 look)
        {
            float udt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime);

            // Drift clock and amplitude ramp - UNSCALED, or the 20% bullet-time would turn
            // the ellipse into a crawl. The drift is applied to the VIEW rotation (and the
            // position follows through the anchor), so the world sways while the player
            // stays pinned - parallax without player wobble.
            bool holdDrift = aimPreset.pauseDriftWhileAiming && look.sqrMagnitude > 0.0001f;
            if (!holdDrift) driftClock += udt;
            driftAmpFactor = Mathf.MoveTowards(driftAmpFactor, 1f, udt / Mathf.Max(aimPreset.driftRampIn, 0.01f));

            float phase = driftClock / Mathf.Max(aimPreset.driftPeriod, 0.01f) * Mathf.PI * 2f;
            driftYawCurrent = Mathf.Sin(phase) * aimPreset.driftYawAmplitude * driftAmpFactor;
            driftPitchCurrent = Mathf.Sin(phase + aimPreset.driftPhaseOffset * Mathf.Deg2Rad)
                * aimPreset.driftPitchAmplitude * driftAmpFactor;

            // The rotation the camera will actually render with this frame (the framing
            // block's eased angles once seeded, else the raw aim), plus free-look + drift.
            float viewY = viewAnglesSeeded ? viewYaw : yaw;
            float viewP = viewAnglesSeeded ? viewPitch : pitch;
            if (freeLookActive)
            {
                viewY += freeLookYaw;
                viewP += freeLookPitch;
            }
            Quaternion viewRotation = Quaternion.Euler(viewP + driftPitchCurrent, viewY + driftYawCurrent, 0f);

            // Auto shoulder on the anchored frame: the swap mirrors the anchor's X around
            // screen centre. Clearance-compare both mirrored positions, with hysteresis.
            if (autoShoulder && !shoulderManualHold)
            {
                float currentClear = ShoulderClearance(AnchoredPosition(viewRotation, shoulderTarget));
                float otherClear = ShoulderClearance(AnchoredPosition(viewRotation, -shoulderTarget));
                if (otherClear > currentClear + autoShoulderMargin) shoulderTarget = -shoulderTarget;
            }
            shoulderCurrent = Mathf.SmoothDamp(shoulderCurrent, shoulderTarget, ref shoulderVelocity,
                shoulderSwapSmoothTime, Mathf.Infinity, udt);

            Vector3 desired = AnchoredPosition(viewRotation, shoulderCurrent);

            // Clearance: pulling in along the player-camera axis keeps the player ON the
            // anchor ray - they just render slightly larger, never displaced or hidden.
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

        // The camera position that puts the player exactly on the preset's viewport anchor
        // for the given view rotation. shoulderSign mirrors the anchor X around centre
        // (+1 = the preset's own side, -1 = the mirrored shoulder); the smoothed swap
        // glides the anchor across the screen.
        Vector3 AnchoredPosition(Quaternion viewRotation, float shoulderSign)
        {
            float anchorX = 0.5f + (aimPreset.playerViewportAnchor.x - 0.5f) * shoulderSign;
            float anchorY = aimPreset.playerViewportAnchor.y;

            float fov = cam != null ? cam.fieldOfView : normalFov;
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            float tanHalfY = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float tanHalfX = tanHalfY * aspect;

            Vector3 anchorRay = new Vector3(
                (anchorX * 2f - 1f) * tanHalfX,
                (anchorY * 2f - 1f) * tanHalfY,
                1f).normalized;
            return target.position - viewRotation * (anchorRay * Mathf.Max(aimPreset.otsBack, 0.5f));
        }

        // Fraction (0..1) of the player-to-position span that is unobstructed - the auto
        // shoulder compares both sides with this.
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

        // Hides any renderer whose collider sits directly between the camera and the player,
        // restoring it the instant it no longer does - see occlusionMask's own comment.
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
                    // Only interested in geometry actually BETWEEN the camera and the player -
                    // never hide the player's own collider/visual.
                    if (target != null && (hit.collider.transform == target || hit.collider.transform.IsChildOf(target))) continue;

                    Renderer hitRenderer = hit.collider.GetComponent<Renderer>();
                    if (hitRenderer == null) continue;

                    stillOccludedThisFrame.Add(hitRenderer);
                    if (!occludedRenderers.Contains(hitRenderer))
                    {
                        hitRenderer.enabled = false;
                        occludedRenderers.Add(hitRenderer);
                    }
                }
            }

            for (int i = occludedRenderers.Count - 1; i >= 0; i--)
            {
                if (stillOccludedThisFrame.Contains(occludedRenderers[i])) continue;

                if (occludedRenderers[i] != null) occludedRenderers[i].enabled = true;
                occludedRenderers.RemoveAt(i);
            }
        }
    }
}
