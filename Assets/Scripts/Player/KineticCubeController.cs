using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KineticEnergy.Player
{
    public enum ControlScheme
    {
        LaunchInstantly, // West: LT aims+charges over time together, RT press = instant launch (the original system)
        HoldRelease,     // North: LT aims only, RT held charges over time, RT release = launch
        AnalogPressure,  // East: LT aims only, charge directly tracks RT's analog pressure, RT release = launch
        StickAim         // RB toggles directly to/from this one - no charging or holding at all,
                          // LT/RT each instantly fire in whatever direction the left stick is
                          // currently pointing (see UpdateStickAimScheme).
    }

    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeController : MonoBehaviour
    {
        [Header("Launch Force")]
        public float minLaunchForce = 16f;
        public float maxLaunchForce = 70f;
        public float maxChargeTime = 1.5f;

        // Exit speed went up (minLaunchForce/maxLaunchForce raised from the previous 6/28) for a
        // punchier-feeling launch, but a faster exit speed alone would also fly further - linear
        // drag isn't a fixed fraction of distance, it eats proportionally MORE of a slow shot's
        // range than a fast one's, so a single constant damping value can't keep both ends of the
        // charge range landing where they used to. Verified empirically (not guessed) with a
        // temporary real-physics batch simulation at a representative 30-degree launch angle:
        // matching the OLD min-force(6)/damping(0.25) baseline distance (~2.84) at the NEW,
        // scaled-up min force (~8.6) needed damping ~1.9, while matching the OLD max-force(28)
        // baseline (~46.0) at the new max force (40) needed only ~0.65. Interpolated by charge
        // fraction at launch time (same curve minLaunchForce/maxLaunchForce already use) so both
        // ends of the charge range land close to their old distances despite the higher exit speed.
        // Re-tuned alongside the project's gravity increase (see ProjectSettings/
        // DynamicsManager.asset, -9.81 -> -18) and the force increase above - stronger gravity
        // alone would have shortened every trajectory despite the higher exit speed, and "much
        // faster, launch further" needed both force AND damping to move together, not just force.
        // Then raised again on their own (force UNCHANGED) per direct feedback: "distance ~50%
        // lower but preserve speed" - damping is the only lever that cuts distance without
        // touching exit speed (that's purely force/mass). Verified with a real-physics batch
        // simulation at the 30-degree reference angle: (2.8, 1.0) lands at 18.06m/55.70m
        // (mid/max charge) against a 34.64m/109.08m baseline - within a few percent of exactly
        // half, at the identical exit speed either way.
        public float minLaunchDamping = 2.8f;
        public float maxLaunchDamping = 1.0f;

        [Header("Aiming")]
        [Range(0f, 1f)] public float aimDeadzone = 0.15f;
        public float aimRotationSpeed = 90f;
        public float minAimPitch = -80f;
        public float maxAimPitch = 80f;
        public float defaultAimPitch = 20f;
        public Transform cameraTransform;
        // Only used by StickAim (see QueueStickAimLaunch's RecenterBehindTarget call) - the
        // charge-based schemes never touch this, since yanking the camera right after firing
        // would fight the "watch the trail all the way to the landing point" experience those
        // schemes are built around.
        public KineticEnergy.Camera.ThirdPersonOrbitCamera cameraOrbit;
        public AimArrowIndicator aimArrow;

        [Header("Landing")]
        [Range(0f, 1f)] public float groundNormalDot = 0.5f;
        public int maxPredictionSteps = 3000;
        public float previewLineHeight = 0.65f;
        public float restVelocityThreshold = 0.05f;
        // Grounded+slow has to hold for this many consecutive seconds before a launch is
        // considered actually landed, not just one single FixedUpdate tick. A vertical or
        // steep shot's velocity crosses zero at its apex for barely more than a tick (gravity
        // alone moves it back past restVelocityThreshold almost immediately), and the
        // BoxCast-based isGrounded below is a PROXIMITY check (true within groundCheckDistance
        // of a surface, not only while actually touching one) - a low apex can sit inside that
        // proximity band too. Together a single-tick check could misread "still airborne, at
        // the top of its arc" as "landed", which silently re-armed hasLaunched mid-flight and
        // let the very next Update() tick's ltHeld (still requiring only !hasLaunched &&
        // isGrounded) treat a still-held LT as starting a brand new aim - instantly zeroing
        // velocity mid-air. That's what "cut off after an incredibly short time" actually was;
        // it never touched OnCollisionEnter, which is why launchGraceDuration and
        // minLaunchClearDistance had no effect no matter how they were tuned. A genuine landing
        // stays slow and grounded continuously (friction/damping keep it there), so requiring
        // the condition to hold this long costs no real responsiveness while completely
        // filtering out the momentary apex dip.
        public float restConfirmDuration = 0.1f;
        public float groundCheckDistance = 0.6f;
        public LandingPreviewController landingPreview;

        [Header("Controls Text")]
        // Always-on top-left corner hint (see KineticEnergySetup.BuildPauseSystem's
        // ControlsHintLabel) and the pause menu's detailed Controls panel body - both wired here
        // (cross-hierarchy, PauseSystem is a separate GameObject) so UpdateControlsText can keep
        // BOTH accurate to whichever scheme is actually active, instead of either one being a
        // fixed string that silently goes stale the next time a scheme's controls change.
        public Text controlsHintLabel;
        public Text controlsPanelBody;

        [Header("Fall Reset")]
        public float fallResetY = -30f;

        [Header("Launch Grace")]
        // A large impulse applied while still technically touching the launch platform can make
        // PhysX re-report that same, continuous contact as a fresh OnCollisionEnter the instant
        // velocity changes - without this, that would immediately zero the launch it just fired,
        // reading as "moves a tiny distance, then falls". No real landing is physically possible
        // this soon after firing at any of this game's launch speeds, so any ground contact
        // inside this window is necessarily spurious and safe to ignore outright.
        public float launchGraceDuration = 0.15f;

        // A fixed time window alone wasn't enough - a shallow, low-angle shot travels mostly
        // horizontally and can stay close to (or dip back down near) its own launch platform for
        // noticeably longer than a lofted one, so it could still genuinely re-touch that same
        // platform right after the grace window expired, getting treated as a real landing and
        // cutting the launch short - "cut off from launching if the angle is too low". This is a
        // second, independent guard: also require the cube to have actually travelled at least
        // this far from where it launched, regardless of how much time has passed, before a
        // ground contact is allowed to count.
        public float minLaunchClearDistance = 2f;

        [Header("Input")]
        public InputActionReference moveAction;
        public InputActionReference launchAction;
        public InputActionReference fireAction;
        // Bound to the same West/North/East gamepad buttons as the old SelectGhostPreview/
        // SelectTrailPreview/SelectCrosshairPreview actions (unrenamed in the .inputactions asset
        // itself - purely a labeling mismatch, not a functional one) - repurposed here to select
        // the control scheme instead of the visual preview mode, since Ghost/Crosshair preview
        // modes are currently disabled anyway (see LandingPreviewController.ghostAndCrosshairEnabled).
        public InputActionReference selectClassicSchemeAction;
        public InputActionReference selectHoldReleaseSchemeAction;
        public InputActionReference selectAnalogSchemeAction;
        public InputActionReference selectNoneAction;

        [Header("Scheme Restriction")]
        // Hold-Release and Analog kept, not removed, but not selectable for now - only Launch
        // Instantly is reachable while this is false. Matches the same disable-without-deleting
        // pattern as LandingPreviewController.ghostAndCrosshairEnabled.
        public bool alternateSchemesEnabled = false;
        // StickAim is now the only reachable scheme by default - this blocks BOTH the RB toggle
        // (HandleSchemeSwitch) and West's unconditional "back to Launch Instantly" (
        // HandlePreviewModeSwitch) from ever leaving StickAim, without deleting either scheme's
        // logic. Flip true to get the old switching behavior back for testing.
        public bool schemeSwitchingEnabled = false;

        [Header("Testing")]
        // Applied to the GLOBAL Physics.gravity every time this changes (Awake + OnValidate, so
        // it also takes effect live if tweaked in the Inspector during Play mode) - exposed here,
        // not just in Project Settings, specifically so it's a quick, obvious knob to test
        // against while StickAim is the only scheme in play. Matches ProjectSettings/
        // DynamicsManager.asset's own value.
        public float gravity = -30f;

        [Header("Stick Aim Scheme")]
        // RB toggles directly between LaunchInstantly and StickAim - always reachable regardless
        // of alternateSchemesEnabled above, since both of these two specifically need to stay
        // switchable during play, unlike Hold-Release/Analog.
        public InputActionReference switchSchemeAction;
        // Live-tuned via the Inspector during testing and carried forward as the new default -
        // this is now THE launch speed knob in practice, StickAim being the only reachable
        // scheme (see schemeSwitchingEnabled) - kept public/exposed for exactly that reason,
        // same as gravity above.
        public float stickAimForce = 50f;
        public float stickAimDamping = 0.7f;
        // LT, grounded ("jump"): launches straight up if the stick is centered (within
        // aimDeadzone), or tilted this many degrees above horizontal toward wherever the stick
        // is pointing otherwise.
        public float stickAimUpAngle = 80f;
        // LT, airborne ("slam" - see stickAimDownAngle below): shallower than the jump by
        // request, kept as its own separate field rather than reusing stickAimUpAngle so the two
        // can be tuned independently.
        public float stickAimDownAngle = 60f;
        // RT, stick held: tilted this many degrees toward wherever the stick is pointing.
        // Usable whether grounded or airborne - a follow-up boost/redirect off an existing LT
        // launch, or a standalone launch on its own.
        public float stickAimForwardAngle = 30f;
        // RT, stick centered: a small tilt toward the player's current facing direction (see
        // FacingFlatDirection) rather than perfectly flat - kept separate from
        // stickAimForwardAngle so the "aimed" and "un-aimed" cases can be tuned independently.
        public float stickAimForwardNeutralAngle = 5f;
        // Shown flat on top of the player, always facing the same direction FacingFlatDirection
        // resolves to, whenever StickAim is the active scheme - wired by KineticEnergySetup.
        public FacingArrowIndicator facingArrow;

        Rigidbody rb;
        BoxCollider boxCollider;
        bool isAiming;
        bool waitingForLtRelease;
        bool hasLaunched;
        bool isGrounded;
        float chargeTime;
        float aimYaw;
        float aimPitch;
        ControlScheme controlScheme = ControlScheme.StickAim;

        // Read by KineticCubeControllerFreeMove to know whether it should instantly face
        // movement direction while walking (StickAim only - see its FixedUpdate).
        public ControlScheme CurrentScheme => controlScheme;

        // Editor-script-only setter (controlScheme itself stays private/encapsulated, only ever
        // changed internally by HandleSchemeSwitch/HandlePreviewModeSwitch at runtime) - needed
        // so KineticEnergySetup can explicitly (re)assign the default scheme on every run, the
        // same anti-staleness reasoning as every other tunable it sets: a serialized value from
        // before this default changed would otherwise keep loading as Launch Instantly forever.
        public void SetControlScheme(ControlScheme scheme)
        {
            controlScheme = scheme;
        }

        // Split in two, not one flag - the two things a complementary movement system
        // (KineticCubeControllerFreeMove) might do while a launch is in flight carry very
        // different risk, and gating them the same way is what broke real launches: a fixed,
        // short post-launch window (launchGraceTimer) becoming false again says nothing about
        // whether the cube has actually LANDED. A shallow, low-angle shot stays close to the
        // ground for far longer than the grace window lasts, so FreeMove's own, independent
        // isGrounded check could easily read true again while the cube is still genuinely
        // mid-flight - and its GROUNDED branch sets rb.linearVelocity directly every tick,
        // silently overwriting the launch's actual velocity the instant that happened. That's
        // the real "cut off from launching" bug: it had nothing to do with OnCollisionEnter,
        // launchGraceDuration, or minLaunchClearDistance, which is exactly why tuning either had
        // no effect, and why it was worse at low angles (shallow shots stay near the ground
        // longer) and "random" at others (any trajectory that happens to pass close to any
        // surface, launch platform or not).
        //
        // AllowGroundedMovement: safe for FreeMove to directly SET velocity (walking on the
        // ground). Gated on the FULL flight (hasLaunched), not just the grace window - directly
        // overwriting velocity is exactly what must never happen while a real launch is still in
        // progress, no matter how close to some surface the cube's own ground check thinks it is.
        public bool AllowGroundedMovement => !isAiming && !hasLaunched;

        // AllowAirborneNudge: safe for FreeMove to apply a small, ADDITIVE force (air control,
        // leaning) while genuinely airborne. Only needs to wait out the brief post-launch grace
        // window, not the whole flight - an additive nudge can't stomp the launch the way
        // directly setting velocity can, so there's no reason to also suppress this for the
        // entire duration of every shot (which is what silently killed air-nudging for launched
        // shots the last time this was "fixed").
        public bool AllowAirborneNudge => !isAiming && launchGraceTimer <= 0f;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;
        float queuedDamping;
        float launchGraceTimer;
        Vector3 launchStartPosition;
        float restTimer;
        // StickAim-only: RT is limited to one use per flight, reset alongside hasLaunched at the
        // exact same "genuinely landed" moment (see FixedUpdate's re-arm block) - reuses the same
        // debounced grounded-check rather than a separate, possibly-inconsistent one.
        bool hasUsedForwardLaunch;
        // Same one-per-flight limit as hasUsedForwardLaunch, for LT's grounded "jump" - see
        // UpdateStickAimScheme's ltUpAllowed.
        bool hasUsedUpLaunch;
        // Same idea, for LT's airborne "slam" (the mirror-image downward launch) - independent
        // of hasUsedUpLaunch, since grounded-up and airborne-down are two different actions that
        // happen to share a button. See UpdateStickAimScheme's ltDownAllowed.
        bool hasUsedDownLaunch;

        Vector3[] trajectoryBuffer;

        Vector3 lastPredictedLanding;
        bool hasPredictedLanding;

        GameObject predictionClone;
        Rigidbody predictionRb;
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
            // Same Player object as KineticCubeControllerFreeMove - used to snap the visual to
            // instantly face the launch direction the moment a launch fires (see FixedUpdate).
            freeMoveController = GetComponent<KineticCubeControllerFreeMove>();
            ApplyGravity();
        }

        // OnValidate re-applies this the instant the Inspector value changes, including while
        // already in Play mode - the whole point of exposing gravity as a public field here
        // instead of only via Project Settings is to make it a fast, live testing knob.
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
            switchSchemeAction?.action?.Enable();
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
            switchSchemeAction?.action?.Disable();
        }

        void Update()
        {
            // Time.timeScale freezes deltaTime-scaled logic (like charge accumulation) for free,
            // but not this raw edge-detected input - without this guard, aiming/firing could
            // still start or complete while the pause menu is up.
            if (Time.timeScale <= 0f) return;

            if (transform.position.y < fallResetY)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            HandlePreviewModeSwitch();
            HandleSchemeSwitch();

            // Kept outside the StickAim-only branch below (and updated unconditionally every
            // frame) so it also correctly hides itself the instant the player switches back to a
            // charge-based scheme.
            if (facingArrow != null)
            {
                facingArrow.SetVisible(controlScheme == ControlScheme.StickAim);
                facingArrow.SetFacingYaw(freeMoveController != null ? freeMoveController.FacingYaw : 0f);
            }

            if (controlScheme == ControlScheme.StickAim)
            {
                UpdateStickAimScheme();
                return;
            }

            // Only one launch allowed per landing (hasLaunched), AND launching only ever starts
            // from a currently-grounded state (isGrounded, the same real-time raycast check
            // FixedUpdate uses) - checking both directly here, rather than trusting hasLaunched
            // alone to have been reset at the right moment, is what actually guarantees you can
            // never begin aiming/firing while airborne.
            bool ltHeld = !hasLaunched && isGrounded && launchAction != null && launchAction.action != null && launchAction.action.IsPressed();

            // One-shot-per-hold: once a launch fires, LT must be fully released before it can gate another.
            if (waitingForLtRelease)
            {
                if (!ltHeld) waitingForLtRelease = false;
                return;
            }

            if (ltHeld)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;
                    // Instantly stop dead, even if the complementary free-move system had the
                    // cube moving right up until this frame - aiming has to start from a
                    // perfectly stationary cube (AllowFreeMovement going false only stops NEW
                    // movement input from being applied; it doesn't touch whatever velocity was
                    // already there).
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
                        // The original system: LT alone both aims and charges over time for as
                        // long as it's held. RT is a single instant-fire press using whatever
                        // charge has built up so far.
                        chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);
                        launchNow = rtPressed;
                        break;

                    case ControlScheme.AnalogPressure:
                        // LT only aims. Charge directly tracks how hard RT is CURRENTLY pressed
                        // (no ramp-up time, drops back toward 0 the moment RT is let go) rather
                        // than building up over time, so the arrow/power preview responds live to
                        // trigger pressure. The actual launch trigger for this scheme isn't RT at
                        // all anymore - it's releasing LT while RT is still held, handled below in
                        // the isAiming/LT-released branch, since that's the frame ltHeld itself
                        // goes false and control never reaches this switch.
                        chargeTime = Mathf.Clamp01(rtAnalogValue) * maxChargeTime;
                        launchNow = false;
                        break;

                    default: // HoldRelease
                        // LT only aims. RT is a separate hold-to-charge/release-to-launch
                        // trigger: charge builds over time only while RT is held, and firing
                        // happens on release, using whatever charge had accumulated by then.
                        if (rtHeld) chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);
                        launchNow = rtReleased;
                        break;
                }

                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;

                if (stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {
                    aimYaw = Mathf.Repeat(aimYaw + stick.x * aimRotationSpeed * Time.deltaTime, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - stick.y * aimRotationSpeed * Time.deltaTime, minAimPitch, maxAimPitch);
                }

                Vector3 dir = AimDirection();
                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(dir, chargeFraction);

                float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
                // Interpolated the same way as force - see the Launch Force header comment for
                // why a single constant damping can't keep both ends of the charge range landing
                // where they used to once exit speed went up.
                float previewDamping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);

                // Computed unconditionally (not just when a visual is active) so it can always
                // be cached below and compared against where the cube actually lands - see
                // OnCollisionEnter's LandingCheck log.
                Vector3 initialVelocity = rb.linearVelocity + dir * previewForce / rb.mass;
                Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, previewDamping, out int stepCount, out bool didLand);
                lastPredictedLanding = landingPoint;
                hasPredictedLanding = true;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand);
                }

                if (launchNow)
                {
                    queuedDirection = dir;
                    queuedForce = previewForce;
                    queuedDamping = previewDamping;
                    launchQueued = true;
                    hasLaunched = true;
                    // Armed here already (not just when FixedUpdate actually applies the
                    // impulse) so AllowFreeMovement is already false the instant firing is
                    // decided - closes a script-execution-order edge case where
                    // KineticCubeControllerFreeMove's FixedUpdate could otherwise run before
                    // this component's on the very first physics tick after firing, see it as
                    // still "allowed", and set velocity directly moments before the impulse
                    // itself gets applied.
                    launchGraceTimer = launchGraceDuration;

                    isAiming = false;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);
                    waitingForLtRelease = true;
                }
            }
            else if (isAiming)
            {
                // The Analog scheme's actual launch trigger: releasing LT while RT is still
                // held fires, using RT's pressure at that exact instant as the charge level.
                // Read fresh here rather than trusting chargeTime's value from the last
                // ltHeld-active frame (one frame stale) - aimYaw/aimPitch don't need the same
                // treatment since nothing else touches them once ltHeld goes false, so
                // AimDirection() below still reflects exactly where the player last aimed.
                // If RT was already let go before LT (not held on this exact frame), this
                // falls through to the same cancel behavior every other scheme already has for
                // a plain LT release.
                bool analogLaunch = controlScheme == ControlScheme.AnalogPressure
                    && fireAction != null && fireAction.action != null && fireAction.action.IsPressed();

                if (analogLaunch)
                {
                    float rtAnalogValue = fireAction.action.ReadValue<float>();
                    chargeTime = Mathf.Clamp01(rtAnalogValue) * maxChargeTime;
                    float chargeFraction = ChargeFraction();

                    queuedDirection = AimDirection();
                    queuedForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
                    queuedDamping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);
                    launchQueued = true;
                    hasLaunched = true;
                    launchGraceTimer = launchGraceDuration; // see the same comment in the LaunchInstantly/HoldRelease launch branch above
                    waitingForLtRelease = true;
                }

                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }
        }

        void FixedUpdate()
        {
            if (launchQueued)
            {
                launchQueued = false;
                rb.linearDamping = queuedDamping;
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);
                launchGraceTimer = launchGraceDuration;
                launchStartPosition = transform.position;
                freeMoveController?.FaceLaunchDirection(queuedDirection);
            }

            if (launchGraceTimer > 0f) launchGraceTimer -= Time.fixedDeltaTime;

            // Grounded state comes from a direct downward check each step, not accumulated
            // OnCollisionEnter/Stay/Exit state - Continuous collision detection (needed so a fast
            // launch can't tunnel through the floor) can keep reporting contact slightly after the
            // cube has genuinely left the ground, which was letting hasLaunched clear near a lob
            // shot's apex (low velocity, stale "grounded") and allowing a mid-air relaunch. A fresh
            // check each step has no such lag: it's simply true or false for exactly this instant.
            //
            // A single ray from the exact center used to do this, but a landing right at a
            // platform's edge can leave the cube's CENTER hanging just past the edge while a
            // corner of its collider is still genuinely resting on the surface - the center ray
            // then misses the platform entirely, isGrounded gets stuck false forever, and since
            // both the aim-start gate and the hasLaunched re-arm above require isGrounded, this
            // permanently locked out launching, matching "land on the border and can't launch
            // anymore". A BoxCast across the cube's own footprint (slightly inset to avoid
            // catching geometry the cube isn't actually resting on) reports grounded if ANY part
            // of that footprint has support below, not just the exact center point.
            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out _, transform.rotation, groundCheckDistance);

            // Re-arm the single launch once the cube has actually come to rest on the ground.
            // Grounded alone isn't enough (a launch fired while already touching the floor never
            // triggers a fresh OnCollisionEnter, since contact was never broken - it just slides
            // to a stop via drag/friction), so this also waits for velocity to settle rather than
            // relying only on the hard OnCollisionEnter stop below. Debounced over
            // restConfirmDuration (see its own comment) rather than firing on a single qualifying
            // tick - required to tell a genuine landing apart from a shot's apex momentarily
            // reading the same as one.
            if (hasLaunched && isGrounded && rb.linearVelocity.sqrMagnitude < restVelocityThreshold * restVelocityThreshold)
            {
                restTimer += Time.fixedDeltaTime;
                if (restTimer >= restConfirmDuration)
                {
                    hasLaunched = false;
                    hasUsedForwardLaunch = false;
                    hasUsedUpLaunch = false;
                    hasUsedDownLaunch = false;
                }
            }
            else
            {
                restTimer = 0f;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsGroundContact(collision)) return;
            if (launchGraceTimer > 0f) return;
            // See minLaunchClearDistance's own comment - a second, independent guard alongside
            // the time-based one above, for a shallow shot that can still genuinely re-touch its
            // own launch platform after the grace window has already expired.
            if (hasLaunched && Vector3.Distance(transform.position, launchStartPosition) < minLaunchClearDistance) return;

            // TEMPORARY diagnostic: logs exactly how far off the prediction was the moment it's
            // possible to measure (right as the real landing is detected), including which axis
            // the error is on - needed real numbers rather than another guess at the cause,
            // since the last two fixes (BoxCast sizing, excluding the player's own collider from
            // the sweep) were each individually correct but reportedly didn't fully close the gap.
            if (hasPredictedLanding)
            {
                Vector3 error = transform.position - lastPredictedLanding;
                Debug.Log($"LandingCheck: predicted={lastPredictedLanding}, actual={transform.position}, error=(x:{error.x:F2}, y:{error.y:F2}, z:{error.z:F2}), distance={error.magnitude:F2}m");
                hasPredictedLanding = false;
            }

            // Stop dead the instant it lands - only on a roughly-upward contact normal, so this
            // reads as "touched the ground" and not "bumped into a wall".
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        bool IsGroundContact(Collision collision)
        {
            foreach (ContactPoint contact in collision.contacts)
            {
                if (Vector3.Dot(contact.normal, Vector3.up) > groundNormalDot) return true;
            }
            return false;
        }

        // West/North/East now pick the control scheme (see ControlScheme) rather than the
        // visual preview mode - Ghost/Crosshair preview modes are disabled anyway (see
        // LandingPreviewController.ghostAndCrosshairEnabled), so those buttons were free to
        // repurpose. South still works the way it always did: toggle the trail preview on/off,
        // just switching between Trail and None now instead of being a one-way hide, since
        // nothing else is left to bring it back otherwise.
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
            else if (selectNoneAction != null && selectNoneAction.action != null && selectNoneAction.action.WasPressedThisFrame() && landingPreview != null)
            {
                landingPreview.SetMode(landingPreview.CurrentMode == PredictionMode.None ? PredictionMode.Trail : PredictionMode.None);
            }
        }

        void UpdateSchemeLabel()
        {
            if (landingPreview != null && landingPreview.modeLabel != null)
            {
                landingPreview.modeLabel.text = controlScheme == ControlScheme.StickAim
                    ? /*"LT: Jump (80 deg, grounded only)   RT: Launch (30 deg, ground or air)   RB: Switch Scheme"*/ ""
                    : alternateSchemesEnabled
                        ? /*$"West: Launch Instantly   North: Hold-Release   East: Analog   South: Show/Hide   RB: Switch Scheme   (scheme: {controlScheme})"*/ ""
                        : /*$"South: Show/Hide   RB: Switch Scheme   (scheme: {controlScheme})"*/ "";
            }

            UpdateControlsText();
        }

        // Keeps the always-on corner hint AND the pause menu's detailed Controls panel accurate
        // to whichever scheme is CURRENTLY active - both used to be fixed strings written once at
        // setup time, which meant either one could silently describe controls that no longer
        // matched reality the next time a scheme's bindings changed. Called from every place
        // UpdateSchemeLabel already is (Start, scheme switches), so both stay in sync with no
        // separate call sites to remember.
        void UpdateControlsText()
        {
            bool stickAim = controlScheme == ControlScheme.StickAim;
            // Only mention the Right Bumper toggle when it can actually do something - with
            // schemeSwitchingEnabled false (the default now that StickAim is locked in), telling
            // the player about a button that does nothing would just be inaccurate.
            string switchLine = schemeSwitchingEnabled
                ? (stickAim ? "Switch Scheme: Right Bumper\n" : "Right Bumper - Switch to the Stick Aim scheme\n")
                : "";
            string switchLinePanel = schemeSwitchingEnabled
                ? (stickAim ? "Right Bumper - Switch back to the original launch scheme\n" : "Right Bumper - Switch to the Stick Aim scheme\n")
                : "";

            if (controlsHintLabel != null)
            {
                controlsHintLabel.text = stickAim
                    ? "Move (on the ground): Left Stick\n" +
                      "Nudge (in the air): Left Stick\n" +
                      "Launch Up (grounded): Left Trigger (80 deg toward stick or straight up)\n" +
                      "Launch Down (airborne): Left Trigger (80 deg toward stick or straight down)\n" +
                      "Launch Forward: Right Trigger (30 deg toward stick or dead ahead if holding still)\n" +
                      "Camera: Right Stick\n" +
                      switchLine +
                      "Pause: Start / Options / Esc"
                    : "Move (on the ground): Left Stick\n" +
                      "Nudge (in the air): Left Stick\n" +
                      "Aim: Left Trigger (hold)\n" +
                      "Adjust Aim: Left Stick (while aiming)\n" +
                      "Launch: Right Trigger\n" +
                      "Camera: Right Stick\n" +
                      switchLine +
                      "Pause: Start / Options / Esc";
            }

            if (controlsPanelBody != null)
            {
                controlsPanelBody.text = stickAim
                    ? "Left Stick - Move (on the ground)\n" +
                      "Left Stick (in the air) - Nudge distance / drift sideways\n" +
                      "Left Trigger (grounded) - Jump: straight up if the stick is centered, or\n" +
                      "  tilted 80 degrees toward the stick otherwise. Once per flight.\n" +
                      "Left Trigger (airborne) - Slam: straight down if the stick is centered, or\n" +
                      "  tilted 80 degrees toward the stick otherwise. Once per flight, independent\n" +
                      "  of the grounded jump above - always available in the air either way.\n" +
                      "Right Trigger - Launch: dead ahead if the stick is centered, or tilted 30\n" +
                      "  degrees toward the stick otherwise. Ground or air, once per flight.\n" +
                      "Right Stick - Camera\n" +
                      switchLinePanel +
                      "Start / Options / Esc - Pause"
                    : "Left Stick - Move (on the ground, while not aiming)\n" +
                      "Left Stick (in the air) - Nudge distance / drift sideways\n" +
                      "Left Trigger - Aim (hold; the cube stays put)\n" +
                      "Left Stick (while aiming) - Adjust aim direction\n" +
                      "Right Trigger - Launch\n" +
                      "South - Show/hide the landing preview\n" +
                      "Right Stick - Camera\n" +
                      switchLinePanel +
                      "Start / Options / Esc - Pause" +
                      (alternateSchemesEnabled ? "" : "\n\n(Hold-Release and Analog launch schemes are still in the project, just disabled)");
            }
        }

        // RB always toggles between exactly these two schemes, regardless of which one is
        // currently active or of alternateSchemesEnabled (that flag only governs whether
        // West/North/East can reach Hold-Release/Analog - it has no bearing on this toggle).
        void HandleSchemeSwitch()
        {
            if (!schemeSwitchingEnabled) return;
            if (switchSchemeAction == null || switchSchemeAction.action == null || !switchSchemeAction.action.WasPressedThisFrame()) return;

            controlScheme = controlScheme == ControlScheme.StickAim ? ControlScheme.LaunchInstantly : ControlScheme.StickAim;

            // Switching away from a charge-based scheme mid-aim needs the same clean cancel a
            // normal LT release would do - once StickAim's own Update() branch takes over
            // (see the early return above), the ltHeld/isAiming branch below is never reached
            // again to do this itself, so isAiming would otherwise stay stuck true forever.
            if (isAiming)
            {
                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }

            UpdateSchemeLabel();
        }

        // No charging, no held-aim phase - LT/RT each instantly queue a launch the moment
        // they're pressed, using wherever the left stick happens to be pointing (or a sensible
        // default direction if it's centered) at that exact instant. Mid-air adjustment and
        // ground movement both come from KineticCubeControllerFreeMove exactly as they do for
        // every other scheme (see AllowGroundedMovement/AllowAirborneNudge) - this scheme never
        // sets isAiming, so those properties behave the same as if nothing were being aimed.
        void UpdateStickAimScheme()
        {
            Vector2 stick = moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            bool stickHeld = stick.sqrMagnitude > aimDeadzone * aimDeadzone;
            Vector3 stickDirection = stickHeld ? StickWorldDirection(stick) : Vector3.zero;

            bool ltPressed = launchAction != null && launchAction.action != null && launchAction.action.WasPressedThisFrame();
            bool rtPressed = fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();

            // LT is now context-sensitive: grounded fires the "jump" (up), airborne fires a
            // mirror-image "slam" (down) - same direction logic either way (tilt stickAimUpAngle
            // toward the held stick, or straight up/down if the stick is centered), just flipped.
            // Each half has its own independent once-per-flight limiter (hasUsedUpLaunch /
            // hasUsedDownLaunch, both reset alongside hasLaunched on landing) - grounded-LT no
            // longer needs a separate isGrounded-mid-air-exception for a forward-launch follow-up
            // the way it briefly did; the airborne case is its own thing now, always available in
            // the air regardless of how the cube got there or whether RT was already used.
            bool ltUpAllowed = !hasUsedUpLaunch && isGrounded && !hasLaunched;
            bool ltDownAllowed = !hasUsedDownLaunch && !isGrounded;

            if (ltPressed && ltUpAllowed)
            {
                Vector3 dir = stickHeld ? TiltedDirection(stickDirection, stickAimUpAngle) : Vector3.up;
                // TEMPORARY diagnostic - pins down whether a reported "stick direction doesn't
                // work" is a real direction-reading bug or one of the once-per-flight limiters
                // silently blocking a later attempt.
                Debug.Log($"StickAim LT UP: stick={stick} stickHeld={stickHeld} dir={dir}");
                QueueStickAimLaunch(dir);
                hasUsedUpLaunch = true;
            }
            else if (ltPressed && ltDownAllowed)
            {
                // Negative angle reuses TiltedDirection unchanged - cos is even (same magnitude
                // either sign) and sin flips sign, so passing -stickAimDownAngle mirrors the tilt
                // downward through horizontal instead of duplicating the method for one sign flip.
                Vector3 dir = stickHeld ? TiltedDirection(stickDirection, -stickAimDownAngle) : Vector3.down;
                Debug.Log($"StickAim LT DOWN: stick={stick} stickHeld={stickHeld} dir={dir}");
                QueueStickAimLaunch(dir);
                hasUsedDownLaunch = true;
            }
            else if (ltPressed)
            {
                Debug.Log($"StickAim LT BLOCKED: hasUsedUpLaunch={hasUsedUpLaunch} hasUsedDownLaunch={hasUsedDownLaunch} isGrounded={isGrounded} hasLaunched={hasLaunched}");
            }
            else if (rtPressed && !hasUsedForwardLaunch)
            {
                // Deliberately NOT gated on isGrounded/hasLaunched - this works as a mid-air
                // follow-up (re-queuing a fresh launch while hasLaunched is already true from an
                // earlier one) just as much as a standalone grounded launch. hasUsedForwardLaunch
                // is the actual limiter: only one of these per flight, reset the moment the cube
                // genuinely lands again. A shallower, separate angle when the stick is centered
                // (toward facing) than when it's actually held (toward the stick) - see
                // stickAimForwardNeutralAngle's own comment for why these are independent knobs.
                Vector3 dir = stickHeld
                    ? TiltedDirection(stickDirection, stickAimForwardAngle)
                    : TiltedDirection(FacingFlatDirection(), stickAimForwardNeutralAngle);
                Debug.Log($"StickAim RT: stick={stick} stickHeld={stickHeld} dir={dir}");
                QueueStickAimLaunch(dir);
                hasUsedForwardLaunch = true;
            }
            else if (rtPressed && hasUsedForwardLaunch)
            {
                Debug.Log($"StickAim RT BLOCKED: hasUsedForwardLaunch is still true (already used this flight) - stick={stick} stickHeld={stickHeld}");
            }
        }

        void QueueStickAimLaunch(Vector3 direction)
        {
            // Instant, predictable launches every time - same reasoning as the aim-start instant
            // stop in the charge-based schemes: zeroing first means a mid-air RT re-launch always
            // produces exactly stickAimForce in the new direction, never additively stacked on
            // top of whatever velocity the cube already had.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            queuedDirection = direction;
            queuedForce = stickAimForce;
            queuedDamping = stickAimDamping;
            launchQueued = true;
            hasLaunched = true;
            launchGraceTimer = launchGraceDuration;

            // "Camera moves behind the player again after launching" - swings back to directly
            // behind the new launch direction, smoothly, cancelling instantly on manual look
            // input (see ThirdPersonOrbitCamera.RecenterBehindTarget). A straight-up jump (LT,
            // stick centered) has direction == Vector3.up, with NO horizontal component -
            // Atan2(0, 0) returns 0 (world +Z) in that case, which silently ignored whatever
            // direction the player actually happened to be facing and snapped the camera to an
            // arbitrary world-relative spot instead. Falling back to the player's current facing
            // whenever the launch itself doesn't establish a new horizontal direction fixes that
            // - "behind the player" for a vertical jump means behind whichever way they were
            // already facing, not behind some fixed world direction.
            Vector3 flatLaunchDir = new Vector3(direction.x, 0f, direction.z);
            float launchYaw = flatLaunchDir.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg
                : (freeMoveController != null ? freeMoveController.FacingYaw : 0f);
            cameraOrbit?.RecenterBehindTarget(launchYaw);
        }

        // Takes a flat (Y=0) direction and tilts it by angleDeg above horizontal (negative tilts
        // below - used by the airborne "slam" launch to mirror the grounded "jump" one),
        // producing a unit 3D launch direction.
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

        // The player's own current facing (same yaw the red FacingArrowIndicator shows), not the
        // camera's - "launch forward" should mean the direction the cube is actually pointing,
        // which is also what the arrow promises it means.
        Vector3 FacingFlatDirection()
        {
            float yaw = freeMoveController != null ? freeMoveController.FacingYaw : 0f;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        // Same camera-relative-forward/right convention as KineticCubeControllerFreeMove's own
        // CameraRelativeForward/Right (duplicated rather than shared across the two components -
        // it's two short lines each, not worth an abstraction for).
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

        // Runs the ACTUAL Unity physics engine on a hidden stand-in Rigidbody, fast-forwarded
        // via manual Physics.Simulate() calls, instead of approximating gravity/drag/collision
        // with hand-written math. Three attempts at a formula-based simulation (flat groundLevel,
        // then BoxCast sizing, then excluding the player's own collider) each fixed a real bug
        // but the predicted point still didn't consistently match reality - guessing at Unity's
        // exact internal drag/integration formula wasn't converging. Using the real engine for
        // both is accurate by construction: there's no formula to get subtly wrong, since it's
        // the same code path that will actually move the cube.
        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, float damping, out int stepCount, out bool didLand)
        {
            EnsurePredictionClone();

            // Damping now varies by charge level (see the Launch Force header comment) - set
            // fresh every call to match whatever shot is currently being aimed, rather than the
            // one-time copy EnsurePredictionClone used to take at clone-creation time, which
            // would otherwise leave every prediction using whatever damping happened to be
            // current the first time this level's clone was built.
            predictionRb.linearDamping = damping;

            // Started slightly above startPos, not exactly on it - teleporting straight onto
            // the platform's surface can register as a fresh contact the moment simulation
            // resumes, and the real cube never has this problem (it's been continuously resting
            // on its platform since it landed), but the clone, repositioned from scratch every
            // call, would otherwise immediately "land" on the platform it launches from before
            // it has moved at all.
            predictionRb.position = startPos + Vector3.up * 0.02f;
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

            for (int i = 0; i < maxPredictionSteps; i++)
            {
                predictionPhysicsScene.Simulate(dt);

                Vector3 pos = predictionClone.transform.position;
                landing = pos;
                if (stepCount < trajectoryBuffer.Length) trajectoryBuffer[stepCount++] = pos;

                // Only trusted after a couple of real steps - checking from i==0 risked reading
                // linearVelocity before this same frame's assignment had actually been picked up
                // by a just-touched physics step, which could misreport "already at rest" before
                // the clone had genuinely moved (positioned right on top of the player/arrow).
                // PredictionCloneStopper zeroes this the instant the clone lands, exactly
                // mirroring KineticCubeController's own OnCollisionEnter.
                if (i >= 2 && predictionRb.linearVelocity.sqrMagnitude < 0.0001f)
                {
                    didLand = true;
                    break;
                }

                // A shot aimed at a gap in Level1 (no floor - Sandbox Scene's flat floor always
                // catches it, which is why this only showed up in Level1) never comes to rest at
                // all, so it would otherwise run the full maxPredictionSteps of REAL physics
                // steps every single frame while aiming - this is what actually made aiming
                // unusable there. Once it's fallen past the same threshold that triggers the
                // real fall-reset, there's nothing more useful to simulate; bail out immediately
                // instead of burning the whole step budget on an already-decided miss. didLand
                // stays false - there's no real landing spot to report for Ghost/Crosshair here.
                if (pos.y < fallResetY) break;
            }

            return landing;
        }

        void EnsurePredictionClone()
        {
            if (predictionClone != null) return;

            if (!predictionSceneReady)
            {
                // A genuinely separate PhysicsScene, not just a toggled SimulationMode - manual
                // Simulate() calls on THIS scene step only its own bodies, so it is physically
                // impossible for prediction to touch the real player, the real camera, or
                // anything else in the main scene, no matter how many steps a long prediction
                // needs. Two earlier attempts at cleaning up after the fact - temporarily
                // kinematic, then snapshotting/restoring position+rotation+velocity - both still
                // let a "teleport" and a knock-on camera jump through occasionally. Isolating the
                // simulation removes the possibility outright instead of trying to undo it.
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

            BoxCollider cloneCollider = predictionClone.AddComponent<BoxCollider>();
            if (boxCollider != null) cloneCollider.size = boxCollider.size;
            // No Physics.IgnoreCollision needed anymore - a separate PhysicsScene means the
            // clone cannot physically collide with the real player's collider at all.

            PredictionCloneStopper stopper = predictionClone.AddComponent<PredictionCloneStopper>();
            stopper.groundNormalDot = groundNormalDot;

            // Left permanently active rather than toggled per prediction call - reactivating a
            // GameObject and immediately reading its Rigidbody's state in the same call is
            // exactly the kind of race that likely caused the "lands on the arrow" symptom above.
            // Between predictions it just sits wherever the last one left it (invisible, no
            // renderer) until the next call repositions and re-launches it - harmless, since
            // nothing else ever reads its state.
        }

        // Colliders can't be shared across PhysicsScenes, only duplicated - this builds
        // static-geometry stand-ins inside the prediction's own isolated scene, matching every
        // collider in the main scene that isn't a Rigidbody (platforms, floor) so the clone has
        // something to land on. Built once, lazily, on the first prediction of the level's
        // lifetime - aiming can't start before at least one real Update() frame has passed, by
        // which point every Awake()/Start() in the scene (including LevelGenerator's) has
        // already run, so the geometry being copied is guaranteed final.
        void BuildPredictionGeometryProxies()
        {
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude);

            foreach (Collider col in colliders)
            {
                if (col == boxCollider) continue;
                if (col.GetComponent<Rigidbody>() != null) continue;
                // Trigger volumes (e.g. Level1's finish line) aren't solid ground - including one
                // here would make the prediction clone incorrectly "land" on thin air.
                if (col.isTrigger) continue;

                GameObject proxy = new GameObject("PredictionGeometryProxy");
                SceneManager.MoveGameObjectToScene(proxy, predictionScene);
                proxy.transform.SetPositionAndRotation(col.transform.position, col.transform.rotation);
                proxy.transform.localScale = col.transform.lossyScale;

                if (col is BoxCollider box)
                {
                    BoxCollider proxyBox = proxy.AddComponent<BoxCollider>();
                    proxyBox.center = box.center;
                    proxyBox.size = box.size;
                }
                else if (col is MeshCollider meshCol)
                {
                    MeshCollider proxyMesh = proxy.AddComponent<MeshCollider>();
                    proxyMesh.sharedMesh = meshCol.sharedMesh;
                    proxyMesh.convex = meshCol.convex;
                }
                else
                {
                    Debug.LogWarning($"KineticCubeController: unhandled collider type {col.GetType().Name} on {col.name} - not included in landing prediction geometry.");
                    Destroy(proxy);
                }
            }
        }

        float ChargeFraction()
        {
            return maxChargeTime > 0f ? Mathf.Clamp01(chargeTime / maxChargeTime) : 1f;
        }

        Vector3 AimDirection()
        {
            return Quaternion.Euler(aimPitch, aimYaw, 0f) * Vector3.forward;
        }

        void SeedAimFromCamera()
        {
            // Only yaw comes from the camera (start aiming whichever way you're currently
            // facing) - pitch starts at a fixed, predictable default rather than whatever the
            // camera's current vertical angle happens to be, which could be anywhere from
            // looking flat to looking well upward and made the very first aim direction feel
            // random rather than a sensible, adjustable-from-there starting point.
            aimYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            aimPitch = Mathf.Clamp(defaultAimPitch, minAimPitch, maxAimPitch);
        }
    }
}
