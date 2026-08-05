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
        StickAim,        // RB cycles to/from this one - hold South/LT/RT to charge a launch in
                          // that direction (up/down/forward), release to fire - see
                          // UpdateStickAimChargeScheme.
        Mixed,           // RB cycles to/from this one - grounded behaves exactly like
                         // LaunchInstantly, airborne behaves like StickAim but with a single
                         // shared once-per-flight limit across all three directions instead of
                         // one each - see UpdateMixedScheme.
        DefyGravity      // RB cycles to/from this one - hold Right Trigger/Left Trigger/South to
                         // charge a straight-line Forward/Up/Down flight (duration AND speed both
                         // grow with charge). Gravity is suspended for the whole charge AND the
                         // charged flight duration, resuming only once that timer runs out - see
                         // UpdateDefyGravityScheme.
    }

    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeController : MonoBehaviour
    {
        [Header("Launch Force")]
        // Raised again (16->45 / 70->110) per direct request: "the strength of your launches
        // should be much higher from the start" - minLaunchForce especially, since with charge
        // accumulation now much slower (see AccumulateCharge/chargeAccumulationRate) even a brief
        // tap needs to feel powerful rather than barely moving the cube. Damping (below)
        // deliberately left untouched - a faster exit speed at the same damping also flies
        // further, which is consistent with "much higher", not just "much faster".
        public float minLaunchForce = 45f;
        public float maxLaunchForce = 110f;
        public float maxChargeTime = 1.5f;
        // Total launches allowed per flight (the first grounded/starting one plus however many
        // more this allows), shared across every scheme - direct request: "2 launches since
        // launching, no matter what sort of launching and no matter the control scheme". Resets
        // alongside hasLaunched the moment the cube genuinely lands again.
        public int maxLaunchesPerFlight = 2;

        [Header("Energy")]
        // "you start at say 20%" (direct request) - a fraction of a full energy tank, spent on
        // charging (see AccumulateCharge) and refunded (with interest) on crashing (see
        // GainEnergyFromCrash). Shared across every scheme, not just Defy Gravity.
        [Range(0f, 1f)] public float startingEnergyFraction = 0.2f;
        // Exchange rate: a FULL charge (chargeFraction 1.0) costs this fraction of the entire
        // energy tank. 1 means a full charge always empties the tank outright (unless you don't
        // have that much stored, in which case AccumulateCharge caps the charge itself before it
        // gets that far - see its own comment). Exposed as its own knob rather than hardcoded so
        // the exchange rate can be tuned without touching the charge-accumulation code itself.
        public float energyCostPerFullCharge = 1f;
        // Energy gained on crashing scales with impact speed, and the RATE itself increases with
        // speed too (not just a flat multiple) - direct request: "the faster your speed at crash
        // that factor at which you gain more energy should also increase". gainedFraction =
        // crashSpeed * energyGainPerSpeed * (1 + crashSpeed * energyGainSpeedBonus).
        public float energyGainPerSpeed = 0.03f;
        public float energyGainSpeedBonus = 0.01f;
        // Yellow energy / blue charge-preview meter, top-right corner - wired by KineticEnergySetup.
        public EnergyMeterController energyMeter;
        // Multiplies Time.deltaTime in AccumulateCharge, so a second of real holding doesn't turn
        // straight into a second of chargeTime - direct request: "even at the starting energy it
        // should take say 1 second to charge up to 20%". At startingEnergyFraction (0.2) and
        // energyCostPerFullCharge (1), the energy-imposed charge ceiling (see EnergyChargeCeiling)
        // is 0.2 * maxChargeTime chargeTime-seconds; reaching that ceiling in 1 real second needs
        // a rate of (0.2 * maxChargeTime) / 1s, which at the current maxChargeTime (1.5) is 0.3.
        public float chargeAccumulationRate = 0.3f;

        [Header("Defy Gravity Scheme")]
        // Charging determines BOTH how long the straight-line flight lasts AND how fast it moves
        // (direct request: "you charge the amount of time and speed you will launch"), the same
        // chargeFraction interpolating both ranges together.
        public float minDefyGravityDuration = 0.4f;
        public float maxDefyGravityDuration = 1.5f;
        public float minDefyGravitySpeed = 10f;
        public float maxDefyGravitySpeed = 35f;
        // Applied only AFTER the forced-flight timer runs out and gravity resumes ("you start
        // falling again since gravity starts affecting you again") - low, like downLaunchDamping,
        // so gravity is clearly the thing doing the falling rather than drag fighting it.
        public float defyGravityFallDamping = 0.2f;

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

        [Header("Wall Crash")]
        // A wall crash (contact normal mostly horizontal, |normal.y| below this) doesn't stick
        // like a floor/ceiling crash does - direct request: "when you launch against a wall,
        // instead of not being affected at all, you just fall slower instead of not at all". See
        // OnCollisionEnter for the floor/ceiling-vs-wall split itself.
        [Range(0f, 1f)] public float wallNormalThreshold = 0.5f;
        // What fraction of the velocity LEFT after removing the into-wall component (see
        // Vector3.ProjectOnPlane in OnCollisionEnter) survives the hit - the rest of this section
        // is what actually makes it keep falling afterward rather than sticking.
        [Range(0f, 1f)] public float wallCrashVelocityRetention = 0.4f;
        // Raises drag for the remainder of the fall after a wall hit, same lever
        // minLaunchDamping/maxLaunchDamping already use to shape a whole trajectory rather than
        // just its first instant - this is what makes "fall slower" describe the ENTIRE rest of
        // the fall, not just a one-off speed chop at the moment of impact. Reset automatically the
        // next time a launch actually fires (see FixedUpdate's launchQueued handling), so it never
        // needs restoring by hand.
        public float wallCrashFallDamping = 3f;

        [Header("Aiming")]
        [Range(0f, 1f)] public float aimDeadzone = 0.15f;
        public float aimRotationSpeed = 90f;
        public float minAimPitch = -80f;
        public float maxAimPitch = 80f;
        // Negative tilts UP in this project's Quaternion.Euler(pitch, yaw, 0) convention -
        // empirically confirmed (pitch=20 => world Y -0.34, pitch=-20 => +0.34) rather than
        // assumed, since the sign is easy to get backwards. -30 starts the old scheme's (and
        // Mixed-grounded's) very first aim frame noticeably higher than the previous +20 -
        // direct request: "should start much higher, say at 30 degrees".
        public float defaultAimPitch = -30f;
        public Transform cameraTransform;
        // Only used by StickAim/Mixed-air (see RecenterCameraForStickAimLaunch) - the
        // charge-based schemes never touch this, since yanking the camera right after firing
        // would fight the "watch the trail all the way to the landing point" experience those
        // schemes are built around.
        public KineticEnergy.Camera.ThirdPersonOrbitCamera cameraOrbit;
        public AimArrowIndicator aimArrow;

        [Header("Landing")]
        public int maxPredictionSteps = 3000;
        public float previewLineHeight = 0.65f;
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
        // StickAim is now the only reachable scheme by default - this blocks BOTH the Dpad radial
        // menu (SetControlSchemeFromMenu) and West's unconditional "back to Launch Instantly" (
        // HandlePreviewModeSwitch) from ever leaving StickAim, without deleting either scheme's
        // logic. Flip true to get the old switching behavior back for testing.
        public bool schemeSwitchingEnabled = false;

        [Header("Testing")]
        // Applied to the GLOBAL Physics.gravity every time this changes (Awake + OnValidate, so
        // it also takes effect live if tweaked in the Inspector during Play mode) - exposed here,
        // not just in Project Settings, specifically so it's a quick, obvious knob to test
        // against. Matches ProjectSettings/DynamicsManager.asset's own value.
        public float gravity = -30f;
        // Global Time.timeScale while charging ANY launch (old-style isAiming or the new
        // hold-to-charge StickAim/Mixed-air system) - "bullet time" so a precise shot is easier
        // to line up. Restored to 1 the instant charging ends (fire or cancel). Deliberately does
        // NOT slow down aim/turn responsiveness - see UpdateChargeBasedScheme's use of
        // Time.unscaledDeltaTime for aimYaw/aimPitch specifically.
        public float chargeTimeScale = 0.75f;

        [Header("Stick Aim Scheme")]
        // Right Bumper - shows/hides the landing-preview trail, for every scheme, and does NOT
        // switch the control scheme anymore (direct request - see HandleTrailToggle and
        // SetControlSchemeFromMenu/the Dpad radial menu, now the only way to switch schemes).
        // Renamed from switchSchemeAction now that its role has changed.
        public InputActionReference trailToggleAction;
        // South: the up ("jump") charge/launch. LT: down ("slam"). RT: forward. All three hold-
        // to-charge, release-to-fire - see UpdateStickAimChargeScheme.
        public InputActionReference upLaunchAction;
        // Aborts whichever charge (old-style isAiming, or the new hold-to-charge system) is
        // currently in progress, without firing - needed here specifically because "release
        // fires" for the new system, unlike the charge-based schemes where release-without-RT
        // already doubled as a cancel.
        public InputActionReference cancelChargeAction;
        // South ("jump"): launches straight up if the stick is centered (within aimDeadzone), or
        // tilted this many degrees above horizontal toward wherever the stick is pointing
        // otherwise. Usable any time (not just grounded) - see UpdateStickAimChargeScheme.
        public float stickAimUpAngle = 80f;
        // Left Trigger ("slam"): shallower than the jump by request, kept as its own separate
        // field rather than reusing stickAimUpAngle so the two can be tuned independently.
        public float stickAimDownAngle = 60f;
        // A downward launch uses this fixed, low damping instead of the minLaunchDamping/
        // maxLaunchDamping charge curve the other two directions share. That curve is tuned to
        // shape a horizontal ARC (see the Launch Force header comment) and, at a purely vertical
        // velocity, is strong enough to counteract gravity's pull faster than gravity can add to
        // it - the fall settled toward a near-constant speed instead of accelerating, reading as
        // the launch's own strength overriding gravity rather than gravity still visibly
        // affecting it (direct request). Flat rather than charge-interpolated like the other two
        // - charging still controls exit speed via minLaunchForce/maxLaunchForce as before, only
        // the drag fighting gravity on the way down changes.
        public float downLaunchDamping = 0.2f;
        // How far the stick has to be pushed (as a fraction of full deflection) before
        // UpdateStickAimChargeScheme treats it as "held" and uses the tilted angle instead of
        // the neutral case - much stricter than the general aimDeadzone above (direct request:
        // "atleast 90% all the way through"), kept as its own field rather than reusing
        // aimDeadzone since the two are tuned for different purposes (old scheme's continuous
        // aim adjustment vs. this system's binary tilted/neutral decision).
        [Range(0f, 1f)] public float stickAimDeadzone = 0.9f;
        // Right Trigger, stick held: tilted this many degrees toward wherever the stick is
        // pointing.
        public float stickAimForwardAngle = 30f;
        // RT, stick centered: a small tilt toward the player's current facing direction (see
        // FacingFlatDirection) rather than perfectly flat - kept separate from
        // stickAimForwardAngle so the "aimed" and "un-aimed" cases can be tuned independently.
        public float stickAimForwardNeutralAngle = 5f;
        // Shown flat on top of the player, always facing the same direction FacingFlatDirection
        // resolves to, whenever StickAim or Mixed is the active scheme - wired by KineticEnergySetup.
        public FacingArrowIndicator facingArrow;

        Rigidbody rb;
        BoxCollider boxCollider;
        bool isAiming;
        bool waitingForLtRelease;
        bool hasLaunched;
        bool isGrounded;
        // True from the instant a genuine in-flight crash is detected (see OnCollisionEnter)
        // until the next launch actually fires (see FixedUpdate's launchQueued handling) - "you
        // stop all movement and stick to that location until you launch again" (direct request).
        // Any surface counts, not just ground - confirmed directly.
        bool isStuck;
        float chargeTime;
        float aimYaw;
        float aimPitch;
        ControlScheme controlScheme = ControlScheme.StickAim;
        // 0-1 fraction of a full tank, shared by every scheme - see startingEnergyFraction's own
        // comment for how it's spent/gained.
        float energyFraction;

        // Read by KineticCubeControllerFreeMove to know whether it should instantly face
        // movement direction while walking (StickAim only - see its FixedUpdate).
        public ControlScheme CurrentScheme => controlScheme;

        // Editor-script-only setter (controlScheme itself stays private/encapsulated, only ever
        // changed internally by SwitchToScheme/HandlePreviewModeSwitch at runtime) - needed
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
        // ground). Gated on the FULL flight (hasLaunched) AND isStuck, not just the grace window -
        // directly overwriting velocity is exactly what must never happen while a real launch is
        // still in progress, no matter how close to some surface the cube's own ground check
        // thinks it is, and the whole point of isStuck is that free walking never resumes once
        // the cube has crashed at least once - only another launch breaks it free (direct
        // request). Also blocked while charging any of the three hold-to-charge systems, same
        // reasoning as isAiming - the cube needs to stay put while charging, ground or air.
        public bool AllowGroundedMovement => !isAiming && !hasLaunched && !isStuck
            && stickAimChargeType == StickAimChargeType.None && defyGravityChargeType == DefyGravityFlightType.None;

        // AllowAirborneNudge: safe for FreeMove to apply a small, ADDITIVE force (air control,
        // leaning) while genuinely airborne. Only needs to wait out the brief post-launch grace
        // window, not the whole flight - an additive nudge can't stomp the launch the way
        // directly setting velocity can, so there's no reason to also suppress this for the
        // entire duration of every shot (which is what silently killed air-nudging for launched
        // shots the last time this was "fixed"). Also blocked while isStuck (frozen, not falling)
        // and during Defy Gravity's forced-velocity flight window, where even a small additive
        // nudge would spoil the straight line the charge promised.
        public bool AllowAirborneNudge => !isAiming && !isStuck && defyGravityFlightTimer <= 0f && launchGraceTimer <= 0f
            && stickAimChargeType == StickAimChargeType.None && defyGravityChargeType == DefyGravityFlightType.None;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;
        float queuedDamping;
        // >0 only for a Defy Gravity launch - see QueueLaunch's own comment.
        float queuedDefyGravityDuration;
        float launchGraceTimer;
        Vector3 launchStartPosition;
        // Every scheme's actual fire moment goes through QueueLaunch (see its own comment), which
        // increments this - one single shared counter instead of five separate per-scheme/per-
        // direction flags, so "2 launches since launching, no matter what sort of launching and no
        // matter the control scheme" (direct request) is enforced identically everywhere rather
        // than each scheme needing its own matching limit. Reset to 0 the instant a genuine crash
        // is detected (see OnCollisionEnter) - still meaningfully caps chaining multiple launches
        // together before ever touching anything, even though energy is now the OTHER (and
        // usually tighter) limit on how much charging is possible at all.
        int launchesUsedThisFlight;
        // Which of South/LT/RT the new hold-to-charge system (UpdateStickAimChargeScheme) is
        // currently charging, if any. None means "not currently charging" - checked instead of a
        // separate bool since exactly one of four states applies at a time.
        enum StickAimChargeType { None, Up, Down, Forward }
        StickAimChargeType stickAimChargeType = StickAimChargeType.None;
        // Same idea as StickAimChargeType, for the Defy Gravity scheme's own hold-to-charge
        // system (UpdateDefyGravityScheme) - kept as a separate enum/field rather than reusing
        // StickAimChargeType since the two schemes' charge systems are otherwise independent and
        // can be active at different times depending on controlScheme.
        enum DefyGravityFlightType { None, Forward, Up, Down }
        DefyGravityFlightType defyGravityChargeType = DefyGravityFlightType.None;
        // Counts down while a Defy Gravity flight is actively forcing a constant velocity (see
        // FixedUpdate) - >0 means "still defying gravity", 0 means gravity has resumed.
        float defyGravityFlightTimer;
        Vector3 defyGravityFlightVelocity;
        // Forward-only: the last FLAT (horizontal) stick direction the stick was genuinely held
        // past stickAimDeadzone, remembered so a release can keep launching that same horizontal
        // direction while dropping to the shallow neutral angle (see the Forward case in
        // UpdateStickAimChargeScheme) - direct request: "the direction should be frozen when
        // launching forward but the lower angle should be used". Up/Down deliberately do NOT
        // freeze at all - they always go straight up/down the instant the stick isn't held past
        // the deadzone, same direct request. Only meaningful once the stick has actually been
        // held past the deadzone at least once THIS charge (stickAimHasAimed) - before that, the
        // neutral fallback (facing direction) is still correct. Charge strength (chargeTime/
        // force) is untouched by any of this - it keeps accumulating every frame regardless of
        // stick state, same as before.
        Vector3 stickAimLastFlatDirection;
        bool stickAimHasAimed;

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
            energyFraction = startingEnergyFraction;
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
            trailToggleAction?.action?.Enable();
            upLaunchAction?.action?.Enable();
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

            // South is StickAim/Mixed's up-launch button (see upLaunchAction) - skipping the old
            // preview-toggle handler here while either is active avoids the same physical press
            // doing two unrelated things (it's also pointless there, since neither ever shows a
            // trail/ghost/crosshair preview to toggle via this specific mechanism).
            if (controlScheme != ControlScheme.StickAim && controlScheme != ControlScheme.Mixed) HandlePreviewModeSwitch();
            HandleTrailToggle();

            // Kept outside any scheme-specific branch below (and updated unconditionally every
            // frame) so it also correctly hides itself the instant the player switches to a
            // charge-based scheme.
            if (facingArrow != null)
            {
                // Mixed's grounded phase behaves like the old scheme (no arrow there either) and
                // its airborne phase reuses StickAim's charge system but isn't StickAim itself -
                // direct request: the arrow "shouldn't appear for the 3rd control scheme".
                bool showFacingArrow = controlScheme == ControlScheme.StickAim;
                facingArrow.SetVisible(showFacingArrow);
                facingArrow.SetFacingYaw(freeMoveController != null ? freeMoveController.FacingYaw : 0f);
            }

            // "Game speed 75% while charging, aiming speed unaffected" - applied every frame from
            // whatever isAiming/stickAimChargeType/defyGravityChargeType currently are. A
            // one-frame lag on the exact instant charging starts/stops (this runs before this
            // frame's scheme update sets them) is imperceptible, well under 17ms.
            ApplyChargeTimeScale();

            // Yellow energy / blue charge-preview meter - updated unconditionally every frame
            // (not just inside whichever scheme branch is active) so it stays correct through
            // scheme switches too.
            if (energyMeter != null)
            {
                energyMeter.SetEnergy(energyFraction);
                bool charging = isAiming || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None;
                energyMeter.SetCharge(ChargeFraction(), charging);
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
            }

            UpdateChargeBasedScheme();
        }

        void ApplyChargeTimeScale()
        {
            bool charging = isAiming || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None;
            Time.timeScale = charging ? chargeTimeScale : 1f;
        }

        // Shared by LaunchInstantly/HoldRelease/AnalogPressure (standalone) and Mixed's grounded
        // phase (the combined "case LaunchInstantly / case Mixed" below gives Mixed the exact
        // same charge/fire rule) - hold LT to aim (stick adjusts yaw/pitch over time, charge
        // accumulates per-scheme), RT (or release, depending on scheme) fires.
        void UpdateChargeBasedScheme()
        {
            bool ltIsPressed = launchAction != null && launchAction.action != null && launchAction.action.IsPressed();
            bool cancelPressed = cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame();

            // Universal LB-cancel: aborts the current aim/charge without firing, the instant
            // it's pressed. Release-without-RT already worked as a cancel too (see the isAiming
            // branch below) - this is just a faster, always-available way to do the same thing,
            // needed consistently here since Mixed's grounded phase is otherwise indistinguishable
            // from standalone LaunchInstantly from the player's point of view.
            if (isAiming && cancelPressed)
            {
                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
                waitingForLtRelease = true;
                return;
            }

            // One-shot-per-hold: once a launch fires, LT must be GENUINELY released (raw button
            // state) before it can gate another aim session.
            if (waitingForLtRelease)
            {
                if (!ltIsPressed) waitingForLtRelease = false;
                return;
            }

            // Energy and the per-flight launch cap are now what gate starting a new aim - not
            // isGrounded/hasLaunched. Both the original grounded start AND the mid-air
            // "air-relaunch" (or, now, a charge started from a freshly-crashed isStuck position)
            // go through this exact same check, "no matter what sort of launching and no matter
            // the control scheme" (direct request, extended to cover energy). Only used to gate a
            // BRAND NEW aim session starting, never to decide whether an ALREADY-ACTIVE one should
            // keep going (see ltHeld just below) - re-deriving a live/proximity-based condition
            // every frame of an already-active charge could spuriously flip it and read as "LT let
            // go", firing prematurely. This exact class of bug has bitten this project before.
            bool canStartNewAim = launchesUsedThisFlight < maxLaunchesPerFlight && energyFraction > 0f;
            bool ltHeld = isAiming ? ltIsPressed : (ltIsPressed && canStartNewAim);

            if (ltHeld)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;
                    // Instantly stop dead, even if the complementary free-move system (or an
                    // existing flight, for the air-relaunch case) had the cube moving right up
                    // until this frame - aiming has to start from a perfectly stationary cube.
                    // FixedUpdate keeps re-applying this for the whole duration of the aim, not
                    // just this one frame, so an airborne aim session doesn't slowly start
                    // falling again from gravity while the player is still lining it up.
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
                        // The original system: LT alone both aims and charges over time for as
                        // long as it's held. RT is a single instant-fire press using whatever
                        // charge has built up so far. Mixed's grounded phase behaves identically.
                        AccumulateCharge();
                        launchNow = rtPressed;
                        break;

                    case ControlScheme.AnalogPressure:
                        // LT only aims. Charge directly tracks how hard RT is CURRENTLY pressed
                        // (no ramp-up time, drops back toward 0 the moment RT is let go) rather
                        // than building up over time, so the arrow/power preview responds live to
                        // trigger pressure. The actual launch trigger for this scheme isn't RT at
                        // all anymore - it's releasing LT while RT is still held, handled below in
                        // the isAiming/LT-released branch, since that's the frame ltHeld itself
                        // goes false and control never reaches this switch. Still capped by
                        // available energy, same as every other scheme's charge.
                        chargeTime = Mathf.Min(Mathf.Clamp01(rtAnalogValue) * maxChargeTime, EnergyChargeCeiling());
                        launchNow = false;
                        break;

                    default: // HoldRelease
                        // LT only aims. RT is a separate hold-to-charge/release-to-launch
                        // trigger: charge builds over time only while RT is held, and firing
                        // happens on release, using whatever charge had accumulated by then.
                        if (rtHeld) AccumulateCharge();
                        launchNow = rtReleased;
                        break;
                }

                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;

                // Unscaled - aim/turn responsiveness must NOT slow down just because
                // chargeTimeScale is slowing everything else while charging (direct request:
                // "the speed of aiming shouldn't be affected").
                float aimDt = Time.unscaledDeltaTime;
                if (stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {
                    aimYaw = Mathf.Repeat(aimYaw + stick.x * aimRotationSpeed * aimDt, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - stick.y * aimRotationSpeed * aimDt, minAimPitch, maxAimPitch);
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

        // Grounded: exactly LaunchInstantly's aim/charge/fire flow (see UpdateChargeBasedScheme's
        // combined switch case). Airborne: StickAim's hold-to-charge system. Both draw from the
        // same shared launchesUsedThisFlight/maxLaunchesPerFlight cap, so switching between the
        // two mid-flight (grounded launch, then an airborne StickAim-style follow-up) still adds
        // up to the same total every scheme gets. Once either system has an active charge in
        // progress, stick with it regardless of isGrounded's exact value that frame - re-deciding
        // by isGrounded alone every frame could otherwise switch systems mid-charge right at a
        // ledge edge.
        void UpdateMixedScheme()
        {
            if (isAiming)
            {
                UpdateChargeBasedScheme();
            }
            else if (stickAimChargeType != StickAimChargeType.None)
            {
                UpdateStickAimChargeScheme();
            }
            else if (isGrounded)
            {
                UpdateChargeBasedScheme();
            }
            else
            {
                UpdateStickAimChargeScheme();
            }
        }

        void FixedUpdate()
        {
            // Continuously (not just once at the instant aiming/charging starts) - gravity would
            // otherwise keep re-accelerating an airborne aim/charge session downward every tick,
            // which the old scheme never had to account for since it only ever aimed from solid
            // ground. This is what actually keeps the air-relaunch and airborne StickAim/Mixed/
            // Defy Gravity charges frozen in place for their whole duration, not just their
            // opening frame - and, now, also what keeps a crashed/isStuck cube pinned exactly
            // where it crashed (direct request: "stop all movement and stick to that location").
            if (isAiming || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None || isStuck)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (launchQueued)
            {
                launchQueued = false;
                isStuck = false; // breaking free of a crashed/stuck position, if it was set
                rb.linearDamping = queuedDamping;
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);
                launchGraceTimer = launchGraceDuration;
                launchStartPosition = transform.position;
                freeMoveController?.FaceLaunchDirection(queuedDirection);

                // Defy Gravity only (queuedDefyGravityDuration is 0 for every other scheme) -
                // arms the forced-velocity flight below, taking effect this same tick.
                if (queuedDefyGravityDuration > 0f)
                {
                    defyGravityFlightTimer = queuedDefyGravityDuration;
                    defyGravityFlightVelocity = queuedDirection * (queuedForce / rb.mass);
                }
            }

            // "Gravity shouldn't affect you [during the charge], after that time is up you start
            // falling again since gravity starts affecting you again" (direct request) - a
            // continuous per-tick velocity override, same technique as the charge-freeze above
            // (just a non-zero constant instead of zero), so gravity's single-tick contribution
            // gets cancelled again next tick before it can compound into a curve. The instant the
            // timer expires this block simply stops running and gravity/damping (set to
            // defyGravityFallDamping when this was queued) take over normally.
            if (defyGravityFlightTimer > 0f)
            {
                rb.linearVelocity = defyGravityFlightVelocity;
                rb.angularVelocity = Vector3.zero;
                defyGravityFlightTimer -= Time.fixedDeltaTime;
            }

            if (launchGraceTimer > 0f) launchGraceTimer -= Time.fixedDeltaTime;

            // Grounded state comes from a direct downward check each step, not accumulated
            // OnCollisionEnter/Stay/Exit state - Continuous collision detection (needed so a fast
            // launch can't tunnel through the floor) can keep reporting contact slightly after the
            // cube has genuinely left the ground, which was letting hasLaunched clear near a lob
            // shot's apex (low velocity, stale "grounded") and allowing a mid-air relaunch. A fresh
            // check each step has no such lag: it's simply true or false for exactly this instant.
            // Still needed for UpdateMixedScheme's grounded-vs-airborne branch selection even
            // though it's no longer used to gate launching itself (see canStartNewAim/canLaunch,
            // now purely energy/launches-cap based) or to re-arm after a landing (see
            // OnCollisionEnter below, now event-driven instead of debounced-velocity-driven).
            //
            // A single ray from the exact center used to do this, but a landing right at a
            // platform's edge can leave the cube's CENTER hanging just past the edge while a
            // corner of its collider is still genuinely resting on the surface - the center ray
            // then misses the platform entirely. A BoxCast across the cube's own footprint
            // (slightly inset to avoid catching geometry the cube isn't actually resting on)
            // reports grounded if ANY part of that footprint has support below, not just the
            // exact center point.
            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out _, transform.rotation, groundCheckDistance);
        }

        void OnCollisionEnter(Collision collision)
        {
            // Only a genuine in-flight crash counts - not pre-launch walking (hasLaunched false),
            // and not an already-stuck body (frozen, shouldn't be generating fresh contacts at
            // all, but defensive regardless).
            if (!hasLaunched || isStuck) return;
            if (launchGraceTimer > 0f) return;
            // See minLaunchClearDistance's own comment - a second, independent guard alongside
            // the time-based one above, for a shallow shot that can still genuinely re-touch its
            // own launch platform after the grace window has already expired.
            if (Vector3.Distance(transform.position, launchStartPosition) < minLaunchClearDistance) return;

            // Floor/ceiling (contact normal mostly vertical) sticks you in place, same as always.
            // A wall (contact normal mostly horizontal) doesn't - direct request: "instead of not
            // being affected at all, you just fall slower instead of not at all" - see
            // wallNormalThreshold's own comment for why the split lives there instead of here.
            Vector3 contactNormal = collision.GetContact(0).normal;
            if (Mathf.Abs(contactNormal.y) < wallNormalThreshold)
            {
                // Removes only the into-wall component (ProjectOnPlane against the wall's own
                // normal) so a shallow, glancing hit doesn't kill sideways/downward motion it
                // never actually had, then scales whatever's left down and raises drag so the
                // rest of the fall genuinely reads as "slower", not just a single speed chop.
                // Deliberately does NOT touch hasLaunched/isStuck/launchesUsedThisFlight/energy -
                // this is still the same flight, just a slower one, not a stick.
                Vector3 remainingVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, contactNormal);
                rb.linearVelocity = remainingVelocity * wallCrashVelocityRetention;
                rb.linearDamping = wallCrashFallDamping;
                return;
            }

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

            // "Whenever you crash onto an object, you stop all movement and stick to that
            // location until you launch again" (direct request) - stop dead, freeze in place
            // (see the FixedUpdate block above), and reset the per-flight launch count so the
            // next charge (from wherever this crash left the cube) starts fresh.
            float crashSpeed = rb.linearVelocity.magnitude;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            isStuck = true;
            hasLaunched = false;
            launchesUsedThisFlight = 0;
            defyGravityFlightTimer = 0f; // interrupt an in-progress forced flight if the crash happens mid-flight

            GainEnergyFromCrash(crashSpeed);
        }

        // West/North/East pick the control scheme (see ControlScheme) rather than the visual
        // preview mode - Ghost/Crosshair preview modes are disabled anyway (see
        // LandingPreviewController.ghostAndCrosshairEnabled), so those buttons were free to
        // repurpose. The trail on/off toggle used to live on South here too, but South is also
        // StickAim's Up-charge AND DefyGravity's Down-charge button - see HandleTrailToggle's own
        // comment for why it moved to Right Bumper instead, which no scheme's charge buttons use.
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

        // Keeps the always-on corner hint AND the pause menu's detailed Controls panel accurate
        // to whichever scheme is CURRENTLY active - both used to be fixed strings written once at
        // setup time, which meant either one could silently describe controls that no longer
        // matched reality the next time a scheme's bindings changed. Called from every place
        // UpdateSchemeLabel already is (Start, scheme switches), so both stay in sync with no
        // separate call sites to remember.
        void UpdateControlsText()
        {
            // Right Bumper toggles the landing-preview trail for every scheme, regardless of
            // schemeSwitchingEnabled - it no longer switches schemes at all (see HandleTrailToggle).
            const string trailToggleLine = "Show/Hide Trail: Right Bumper\n";
            const string trailToggleLinePanel = "Right Bumper - Show/hide the landing-preview trail\n";
            // Only mention scheme-switching when it can actually do something - with
            // schemeSwitchingEnabled false, telling the player about a button that does nothing
            // would just be inaccurate.
            string switchLine = schemeSwitchingEnabled ? "Switch Scheme: Dpad (hold)\n" : "";
            string switchLinePanel = schemeSwitchingEnabled
                ? "Dpad (hold) - Open a radial menu to pick a scheme directly\n"
                : "";
            // Same crash-and-stick behavior on every scheme now, so this line is shared verbatim
            // rather than repeated with small variations per case.
            const string stuckLine = "Crashing into floors/ceilings sticks you in place and refunds energy - walls just slow your fall\n";
            const string stuckLinePanel =
                "Crashing into a floor or ceiling stops you dead and sticks you there until\n" +
                "  you launch again - it also refunds energy, more than the charge cost,\n" +
                "  scaling up with how fast you were going. A wall doesn't stick you - it\n" +
                "  just sheds some speed and lets you keep falling, slower than before.\n";

            if (controlsHintLabel != null)
            {
                controlsHintLabel.text = controlScheme switch
                {
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
                        "Grounded: Left Trigger to aim+charge (as the original scheme), Right\n" +
                        "  Trigger to launch\n" +
                        "Airborne: hold South / Left Trigger / Right Trigger to charge Up / Down /\n" +
                        "  Forward\n" +
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
                        "Airborne - hold South / Left Trigger / Right Trigger to charge an Up /\n" +
                        "  Down / Forward launch (exactly like the Stick Aim scheme).\n" +
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

        // Right Bumper universally shows/hides the landing-preview trail, for EVERY scheme, and
        // no longer switches the control scheme at all (direct request: "right bumper shouldn't
        // change the current control scheme" - see SetControlSchemeFromMenu/the Dpad radial menu
        // for the only remaining way to switch). Moved here (was on South, selectNoneAction)
        // specifically because South is StickAim's Up-charge AND DefyGravity's Down-charge
        // button - a single South press used to fire both the toggle AND a charge-start on the
        // same frame. Right Bumper isn't used as a charge button by any scheme, so it's free.
        // Unconditional, not gated by schemeSwitchingEnabled - that flag is specifically about
        // whether OTHER schemes are reachable, unrelated to a visual on/off toggle.
        void HandleTrailToggle()
        {
            if (trailToggleAction == null || trailToggleAction.action == null || !trailToggleAction.action.WasPressedThisFrame()) return;
            if (landingPreview == null) return;

            landingPreview.SetMode(landingPreview.CurrentMode == PredictionMode.None ? PredictionMode.Trail : PredictionMode.None);
        }

        // The Dpad radial menu's entry point (see RadialMenuController) - now the ONLY way to
        // switch control schemes (direct request - Right Bumper no longer does this, see
        // HandleTrailToggle). Respects schemeSwitchingEnabled - RadialMenuController checks this
        // before ever calling in.
        public void SetControlSchemeFromMenu(ControlScheme scheme)
        {
            SwitchToScheme(scheme);
        }

        // Cancels whichever of the three charge systems was active, cleanly, regardless of which
        // one it was, since once the scheme switches, none of the three Update() branches that
        // would otherwise notice a release/cancel is guaranteed to run for it again.
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

            UpdateSchemeLabel();
        }

        // Hold South/LT/RT to charge a launch in that direction (same charge curve as the
        // charge-based schemes: minLaunchForce/maxLaunchForce interpolated by how long it's
        // held, over maxChargeTime), release to fire, Left Bumper cancels without firing. Shows
        // the same aim arrow + landing trail the charge-based schemes do while charging - "the
        // same sort of visual... that shows you your exact launch path" (direct request). All
        // three directions share the single launchesUsedThisFlight/maxLaunchesPerFlight cap -
        // same limit, same counter, for both standalone StickAim and Mixed's airborne phase.
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
                if (cancelPressed)
                {
                    CancelStickAimCharge();
                    return;
                }

                bool releasedNow = stickAimChargeType switch
                {
                    StickAimChargeType.Up => upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasReleasedThisFrame(),
                    StickAimChargeType.Down => launchAction != null && launchAction.action != null && launchAction.action.WasReleasedThisFrame(),
                    _ => fireAction != null && fireAction.action != null && fireAction.action.WasReleasedThisFrame(),
                };

                AccumulateCharge();
                Vector3 dir;
                if (stickHeld)
                {
                    dir = ComputeStickAimDirection(stickAimChargeType, true, stickDirection);
                    stickAimLastFlatDirection = stickDirection;
                    stickAimHasAimed = true;
                }
                else if (stickAimChargeType == StickAimChargeType.Forward)
                {
                    // Forward keeps launching the last direction the stick actually pointed (if
                    // any), but drops to the shallow neutral angle the instant the stick isn't
                    // held past stickAimDeadzone anymore - direct request: "the direction should
                    // be frozen when launching forward but the lower angle should be used".
                    Vector3 flat = stickAimHasAimed ? stickAimLastFlatDirection : FacingFlatDirection();
                    dir = TiltedDirection(flat, stickAimForwardNeutralAngle);
                }
                else
                {
                    // Up/Down: always straight up/down the instant the stick isn't held past
                    // stickAimDeadzone - unlike Forward, these never freeze at an angle (same
                    // direct request).
                    dir = ComputeStickAimDirection(stickAimChargeType, false, stickDirection);
                }

                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(dir, chargeFraction);

                float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
                // Down uses its own fixed, low damping so gravity stays visibly in control of the
                // fall - see downLaunchDamping's own comment.
                float previewDamping = stickAimChargeType == StickAimChargeType.Down
                    ? downLaunchDamping
                    : Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);

                // rb.linearVelocity is held at zero for the whole charge (see FixedUpdate), so
                // unlike the charge-based schemes' initialVelocity this doesn't need to add it in.
                Vector3 initialVelocity = dir * previewForce / rb.mass;
                Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, previewDamping, out int stepCount, out bool didLand);
                lastPredictedLanding = landingPoint;
                hasPredictedLanding = true;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand);
                }

                if (releasedNow)
                {
                    QueueLaunch(dir, previewForce, previewDamping);
                    RecenterCameraForStickAimLaunch(dir);

                    stickAimChargeType = StickAimChargeType.None;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);
                }
            }
            else
            {
                bool canLaunch = launchesUsedThisFlight < maxLaunchesPerFlight && energyFraction > 0f;

                bool upPressed = canLaunch && upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame();
                bool ltPressed = canLaunch && launchAction != null && launchAction.action != null && launchAction.action.WasPressedThisFrame();
                bool rtPressed = canLaunch && fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();

                if (upPressed) StartStickAimCharge(StickAimChargeType.Up);
                else if (ltPressed) StartStickAimCharge(StickAimChargeType.Down);
                else if (rtPressed) StartStickAimCharge(StickAimChargeType.Forward);
            }
        }

        void StartStickAimCharge(StickAimChargeType type)
        {
            stickAimChargeType = type;
            chargeTime = 0f;
            stickAimHasAimed = false;
            // Instant stop, same reasoning as the charge-based schemes' aim-start - FixedUpdate
            // keeps re-applying this for the whole charge, not just this one frame, so an
            // airborne charge doesn't slowly start falling again from gravity.
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
                    return stickHeld ? TiltedDirection(stickDirection, stickAimUpAngle) : Vector3.up;
                case StickAimChargeType.Down:
                    // Negative angle reuses TiltedDirection unchanged - cos is even (same
                    // magnitude either sign) and sin flips sign, so this mirrors the tilt
                    // downward through horizontal instead of duplicating the method for one sign
                    // flip.
                    return stickHeld ? TiltedDirection(stickDirection, -stickAimDownAngle) : Vector3.down;
                default: // Forward
                    // A shallower, separate angle when the stick is centered (toward facing)
                    // than when it's actually held (toward the stick) - see
                    // stickAimForwardNeutralAngle's own comment for why these are independent.
                    return stickHeld
                        ? TiltedDirection(stickDirection, stickAimForwardAngle)
                        : TiltedDirection(FacingFlatDirection(), stickAimForwardNeutralAngle);
            }
        }

        // Hold Right Trigger/Left Trigger/South to charge a straight-line Forward/Up/Down flight
        // - direct request's button mapping ("right trigger, left trigger and button south
        // respectively" -> "forward, upward, downwards respectively"), deliberately DIFFERENT
        // from StickAim's South=Up/LT=Down (only RT=Forward matches between the two schemes).
        // Charging grows BOTH the flight's duration and its speed together (same chargeFraction
        // interpolating both ranges) - release to fire, Left Bumper cancels. Forward can be
        // steered with the left stick, same "held past stickAimDeadzone or fall back to current
        // facing" convention as StickAim's own Forward - but always perfectly flat (no tilt
        // angle), matching "straight forward" literally. Up/Down never take stick input at all.
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

                float flightSpeed = Mathf.Lerp(minDefyGravitySpeed, maxDefyGravitySpeed, chargeFraction);
                float flightDuration = Mathf.Lerp(minDefyGravityDuration, maxDefyGravityDuration, chargeFraction);

                // rb.linearVelocity is held at zero for the whole charge (see FixedUpdate), so
                // the preview's initial velocity is just the flight speed itself.
                Vector3 initialVelocity = dir * flightSpeed;
                Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, defyGravityFallDamping, out int stepCount, out bool didLand, flightDuration);
                lastPredictedLanding = landingPoint;
                hasPredictedLanding = true;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand);
                }

                if (releasedNow)
                {
                    // Force = speed here (mass is 1, and QueueLaunch's impulse divides by mass) -
                    // matches how the forced-flight velocity gets rebuilt in FixedUpdate
                    // (queuedDirection * queuedForce / rb.mass).
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
                bool canLaunch = launchesUsedThisFlight < maxLaunchesPerFlight && energyFraction > 0f;

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
            // Instant stop, same reasoning as every other charge-start - FixedUpdate keeps
            // re-applying this for the whole charge, not just this one frame, so an airborne
            // charge doesn't slowly start falling again from gravity.
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

        // Shared by every scheme's actual firing moment - just the physics-impulse queuing
        // (FixedUpdate applies it next tick) plus arming hasLaunched/launchGraceTimer. Doesn't
        // zero velocity itself: the charge-based flows already hold it at zero continuously for
        // the whole charge (see FixedUpdate), so by the time this runs it's already correct.
        // defyGravityDuration is >0 only for a Defy Gravity launch - see its own field comment.
        void QueueLaunch(Vector3 direction, float force, float damping, float defyGravityDuration = 0f)
        {
            queuedDirection = direction;
            queuedForce = force;
            queuedDamping = damping;
            queuedDefyGravityDuration = defyGravityDuration;
            launchQueued = true;
            hasLaunched = true;
            // Every launch spends the charge fraction it took to build, straight out of the
            // shared energy tank - "no more time/energy/speed can be added... when you reach the
            // limit" (direct request) is what AccumulateCharge already enforces on the way up;
            // this is the other half, actually deducting it once the charge is spent for real.
            energyFraction = Mathf.Clamp01(energyFraction - ChargeFraction() * energyCostPerFullCharge);
            // Every scheme's actual fire moment goes through here, so counting it centrally
            // (rather than at each call site) is what makes the 2-launch cap apply identically
            // "no matter what sort of launching and no matter the control scheme" - see
            // launchesUsedThisFlight's own comment.
            launchesUsedThisFlight++;
            // Armed here already (not just when FixedUpdate actually applies the impulse) so
            // AllowGroundedMovement/AllowAirborneNudge are already correct the instant firing is
            // decided - closes a script-execution-order edge case where
            // KineticCubeControllerFreeMove's FixedUpdate could otherwise run before this
            // component's on the very first physics tick after firing, see it as still
            // "allowed", and set velocity directly moments before the impulse itself applies.
            launchGraceTimer = launchGraceDuration;
        }

        // StickAim/Mixed-air only - the charge-based schemes deliberately never touch the camera
        // (see cameraOrbit's own field comment), so this stays separate from QueueLaunch.
        void RecenterCameraForStickAimLaunch(Vector3 direction)
        {
            // "Camera moves behind the player again after launching" - swings back to directly
            // behind the new launch direction, smoothly, cancelling instantly on manual look
            // input (see ThirdPersonOrbitCamera.RecenterBehindTarget). A straight-up/down launch
            // (stick centered) has direction with NO horizontal component - Atan2(0, 0) returns 0
            // (world +Z) in that case, which would silently ignore whatever direction the player
            // actually happened to be facing and snap the camera to an arbitrary world-relative
            // spot instead. Falling back to the player's current facing whenever the launch
            // itself doesn't establish a new horizontal direction fixes that.
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
        // gravityFreeDuration: Defy Gravity only (every other caller passes 0, unaffected) - the
        // real cube forces its velocity to stay constant for this many SIMULATED seconds after
        // firing (see FixedUpdate's defyGravityFlightTimer block), so the preview has to do the
        // exact same thing or the trail would show a normal gravity-curved arc for a launch that
        // actually flies dead straight until the timer runs out. This project already found and
        // fixed one real trail-accuracy bug from the prediction clone drifting from what the real
        // cube does (see the 0.15f start-offset comment below) - matching the real per-tick
        // override technique exactly, not approximating it, is what keeps this one accurate too.
        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, float damping, out int stepCount, out bool didLand, float gravityFreeDuration = 0f)
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
            // it has moved at all. 0.02 wasn't actually enough margin - PhysX's own default
            // contact offset is 0.01 (see ProjectSettings/DynamicsManager.asset), so a 0.02 gap
            // left only ~0.01 of genuine clearance beyond PhysX's own "already touching" zone,
            // easily eaten by solver/continuous-detection approximation. Confirmed empirically
            // (temporary batch-mode diagnostic comparing predicted vs actual real-physics
            // outcomes): a moderate-strength, moderate-angle shot could register an instant false
            // "landed" contact with the launch surface at 0.02, get stopped dead by
            // PredictionCloneStopper within the first step or two, and then just slowly re-settle
            // under gravity from a standstill - producing a predicted landing point barely more
            // than a meter away when the real shot (unaffected, since the real cube was never
            // teleported this close to its own surface to begin with) actually travelled over
            // 15m further. A stronger shot cleared the margin fast enough within one step to
            // avoid the false contact, which is why this wasn't uniformly wrong - "isn't accurate
            // in all cases" is exactly what that looks like from the player's side.
            predictionRb.position = startPos + Vector3.up * 0.15f;
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
                // Same continuous per-tick override the real cube uses while a Defy Gravity
                // flight is still forcing its velocity (see FixedUpdate) - applied BEFORE this
                // step's Simulate() so it actually governs this step's motion, exactly mirroring
                // the real cube's script-then-physics execution order.
                if (i * dt < gravityFreeDuration)
                {
                    predictionRb.linearVelocity = initialVelocity;
                    predictionRb.angularVelocity = Vector3.zero;
                }

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

            // Mirrors OnCollisionEnter's floor/ceiling-vs-wall split exactly, so the trail stays
            // accurate now that a wall crash no longer stops the real cube outright (see
            // PredictionCloneStopper's own comment) - copied once here rather than kept in sync
            // every call, same as the collider size just above; these three rarely change at
            // runtime and PredictLandingPoint already re-syncs the one that does (damping).
            PredictionCloneStopper stopper = predictionClone.AddComponent<PredictionCloneStopper>();
            stopper.wallNormalThreshold = wallNormalThreshold;
            stopper.wallCrashVelocityRetention = wallCrashVelocityRetention;
            stopper.wallCrashFallDamping = wallCrashFallDamping;

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

        // Shared by every scheme's charge-accumulation step (see each Update*Scheme method) -
        // chargeTime is capped by BOTH maxChargeTime (the existing per-shot limit) AND however
        // much energy is actually left, expressed as an equivalent charge-time ceiling. This one
        // change is what makes the blue charge-preview bar automatically "never bigger than the
        // amount of energy in the bar" and makes charging stop growing "when you reach the limit
        // of the energy you have stored" (direct request) - both fall out of chargeFraction being
        // bounded by construction, no separate clamping needed anywhere else.
        void AccumulateCharge()
        {
            chargeTime = Mathf.Min(chargeTime + Time.deltaTime * chargeAccumulationRate, maxChargeTime, EnergyChargeCeiling());
        }

        float EnergyChargeCeiling()
        {
            return energyCostPerFullCharge > 0f ? (energyFraction / energyCostPerFullCharge) * maxChargeTime : maxChargeTime;
        }

        // "You gain energy depending on the speed you used to crash onto it, it should be more
        // than what you put in it, with the faster your speed at crash that factor at which you
        // gain more energy should also increase" (direct request) - the energyGainSpeedBonus term
        // is what makes the RATE increase with speed too, not just the raw amount: a crash at
        // twice the speed doesn't just gain twice the base energy, it gains MORE than twice, since
        // the multiplier itself grows with speed.
        void GainEnergyFromCrash(float crashSpeed)
        {
            float gained = crashSpeed * energyGainPerSpeed * (1f + crashSpeed * energyGainSpeedBonus);
            energyFraction = Mathf.Clamp01(energyFraction + gained);
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
