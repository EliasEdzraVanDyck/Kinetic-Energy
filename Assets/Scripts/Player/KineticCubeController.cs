using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Level;
using KineticEnergy.UI;

namespace KineticEnergy.Player
{
    // How the FastPaced-style charge's ENERGY amount is controlled (the EnergyRegulation
    // playtest scenes - each scene's Player instance picks one; Standard everywhere else, which
    // is the untouched hold-to-charge-over-time behavior).
    public enum EnergyControlMode
    {
        Standard,          // hold the launch button, charge grows with time (unchanged)
        Automatic,         // charge solved automatically to reach whatever the aim points at
        CircleCrank,       // crank the stick/WASD in circles: clockwise adds, ccw subtracts
        DedicatedButtons,  // stick up/down (and mouse wheel) add/subtract
        ReverseDirection,  // standard, but RB/MMB jumps to max and drains at the same rate
    }

    public enum ControlScheme
    {
        LaunchInstantly, // West: LT aims+charges over time together, RT press = instant launch (the original system)
        HoldRelease,     // North: LT aims only, RT held charges over time, RT release = launch
        AnalogPressure,  // East: LT aims only, charge directly tracks RT's analog pressure, RT release = launch
        StickAim,        // RB cycles to/from this one - hold South/LT/RT to charge a launch in
                          // that direction (up/down/forward), release to fire - see
                          // UpdateStickAimChargeScheme.
        Mixed,           // Grounded behaves exactly like LaunchInstantly (LT aims+charges, RT
                         // fires). Airborne: LT opens that SAME aim-and-confirm flow mid-air,
                         // while RT/South/West are StickAim-style hold-to-charge Forward/Up/Down
                         // (release fires, stick tilts the angle) - Down deliberately sits on
                         // West here, not LT - see UpdateMixedScheme.
        DefyGravity,     // RB cycles to/from this one - hold Right Trigger/Left Trigger/South to
                         // charge a straight-line Forward/Up/Down flight (duration AND speed both
                         // grow with charge). Gravity is suspended for the whole charge AND the
                         // charged flight duration, resuming only once that timer runs out - see
                         // UpdateDefyGravityScheme.
        FastPaced        // FastPacedLevel's only reachable scheme (schemeSwitchingEnabled false
                         // there) - full free-look 3rd-person camera by default; hold Right Mouse
                         // to aim (camera swings to first person, reticle shows), hold Left Mouse
                         // to charge a shot straight along the camera's current look direction,
                         // release to fire. Releasing Right Mouse cancels any charge and reverts
                         // the camera. See UpdateFastPacedScheme.
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

        [Header("Energy")]
        // "you start at say 20%" (direct request) - a fraction of a full energy tank, spent on
        // charging (see AccumulateCharge) and refunded (with interest) on crashing (see
        // GainEnergyFromCrash). Shared across every scheme, not just Defy Gravity.
        [Range(0f, 1f)] public float startingEnergyFraction = 0.2f;
        // Exchange rate: a FULL charge (chargeFraction 1.0) costs this fraction of the entire
        // energy tank. MUST stay at 1 - direct request: "you shouldn't be able to charge more
        // energy than the amount you have stored currently, the amount you charged should be
        // exactly the same as the energy you subtract from the current stored". At 1,
        // EnergyChargeCeiling() (below) reduces to energyFraction * maxChargeTime, which makes
        // ChargeFraction() <= energyFraction ALWAYS hold (charging literally cannot exceed what's
        // stored) and the QueueLaunch deduction (ChargeFraction() * this field) reduce to exactly
        // ChargeFraction() - i.e., "charged" and "subtracted" are the same number by construction,
        // not just approximately. This was briefly lowered (0.1) to make a full-power charge
        // affordable more than once per tank, but that broke both those guarantees (a full charge
        // could cost far less than 100% of the meter while still SHOWING as a full-width charge
        // bar, i.e. visually/mechanically "charging more than currently stored") - use
        // chargeAccumulationRate instead to make charging feel cheaper/slower without breaking
        // the 1:1 relationship this field's value of 1 guarantees.
        public float energyCostPerFullCharge = 1f;
        // Energy gained on crashing scales with impact speed, and the RATE itself increases with
        // speed too (not just a flat multiple) - direct request: "the faster your speed at crash
        // that factor at which you gain more energy should also increase". gainedFraction =
        // crashSpeed * energyGainPerSpeed * (1 + crashSpeed * energyGainSpeedBonus).
        public float energyGainPerSpeed = 0.03f;
        public float energyGainSpeedBonus = 0.01f;
        // SlowPacedLevel's economy (direct request: "you just gain 1.2 times the energy you put
        // in a launch, no matter the speed and all"): when true, every crash refunds exactly
        // lastLaunchEnergySpent * fastPacedRefundMultiplier, replacing the speed-based formula
        // above - the same rule FastPaced already uses, now available to any scheme per scene.
        public bool refundSpentEnergyOnly = false;
        // Floors GainEnergyFromCrash's result - direct request: "you should at least get 5% of
        // the meter back no matter your speed when you land". Without this, a crash at very low
        // speed (a gentle short hop, or the tail end of a soft landing) could compute a gain close
        // to zero from the speed-based formula above, reading as "the meter doesn't always update
        // /you don't always gain energy properly" even though a genuine stick-and-refund DID just
        // happen - this guarantees every crash that reaches GainEnergyFromCrash refunds at least
        // this much regardless of how gentle it was.
        [Range(0f, 1f)] public float minEnergyGainPerCrash = 0.05f;
        // Reserve floor (direct request): GROUNDED launches may never spend below this, and a
        // crash refund never leaves you under it. MIDAIR is exempt - see SpendableEnergy - so
        // an airborne launch can commit the entire tank. Enforced via EnergyChargeCeiling (a
        // charge can't grow past what's spendable), QueueLaunch's deduction, and
        // ClampEnergyFloor (the post-crash failsafe).
        [Range(0f, 1f)] public float minEnergyReserve = 0.05f;
        // Yellow energy / blue charge-preview meter, top-right corner - wired by KineticEnergySetup.
        public EnergyMeterController energyMeter;
        // Multiplies Time.deltaTime in AccumulateCharge, so a second of real holding doesn't turn
        // straight into a second of chargeTime - direct request: "even at the starting energy it
        // should take say 1 second to charge up to 20%". At startingEnergyFraction (0.2) and
        // energyCostPerFullCharge (must stay 1 - see its own comment), the energy-imposed charge
        // ceiling is exactly 0.2 * maxChargeTime (1.5) = 0.3 chargeTime-seconds; reaching that in
        // 1 real second needs a rate of 0.3/1.0 = 0.3. This is the knob to use for making charging
        // feel cheaper/slower/faster overall - unlike energyCostPerFullCharge, changing this
        // doesn't break the "can't charge more than you have stored" guarantee.
        public float chargeAccumulationRate = 0.3f;

        [Header("Defy Gravity Scheme")]
        // Charging determines BOTH how long the straight-line flight lasts AND how fast it moves
        // (direct request: "you charge the amount of time and speed you will launch"), the same
        // chargeFraction interpolating both ranges together.
        public float minDefyGravityDuration = 0.4f;
        public float maxDefyGravityDuration = 1.5f;
        // Minimum deliberately reuses minLaunchForce (the SAME field every other scheme's charge
        // curve starts from - see UpdateChargeBasedScheme/UpdateStickAimChargeScheme) rather than
        // its own separate value, so the schemes can never drift apart again - direct request:
        // "make sure all control schemes have the same minimum launch force". Only the ceiling
        // stays scheme-specific (maxDefyGravitySpeed), since a sustained forced-velocity flight
        // and a ballistic arc's exit speed aren't really the same kind of "maximum" to begin with.
        // Raised from 35 to stay ABOVE minLaunchForce once that became the shared floor (45) -
        // left at the old value this would have made charging LONGER produce a SLOWER flight
        // (Mathf.Lerp(45, 35, t) decreases as t grows), a real bug introduced by the unification
        // above, not something anyone asked for. 70 keeps a comparable min-to-max ratio to the
        // original 10/35 pair while staying below maxLaunchForce (110) - Defy Gravity's charge
        // also scales flight DURATION at the same time (see minDefyGravityDuration/
        // maxDefyGravityDuration), so its top end doesn't need to match a ballistic shot's exit
        // speed to still cover serious distance (70 * 1.5s = up to 105m in a straight line).
        public float maxDefyGravitySpeed = 70f;
        // Applied only AFTER the forced-flight timer runs out and gravity resumes ("you start
        // falling again since gravity starts affecting you again") - low, like downLaunchDamping,
        // so gravity is clearly the thing doing the falling rather than drag fighting it.
        public float defyGravityFallDamping = 0.2f;

        [Header("Fast Paced Scheme")]
        // Zero gravity in FastPacedLevel (see the scene's own gravity=0 override) turns a launch
        // into a straight line with no arc to shape, so damping alone decides how far it carries
        // before drag brings it to a stop - a fundamentally different physics regime than the
        // gravity-arc curve minLaunchDamping/maxLaunchDamping is tuned for, so this gets its own
        // dedicated pair rather than reusing that one. Same charge-fraction interpolation
        // (Mathf.Lerp by ChargeFraction()) as every other scheme's damping curve.
        public float fastPacedMinDamping = 2.8f;
        public float fastPacedMaxDamping = 1.0f;
        // FastPaced-only energy economy (direct request): the crash refund is EXACTLY the energy
        // the launch itself spent, times this - "take the exact amount of energy you put in to
        // launch, remember it and multiply by 1.2" - replacing the speed-based
        // energyGainPerSpeed/energyGainSpeedBonus formula the other schemes keep using. See
        // lastLaunchEnergySpent/GainEnergyFromCrash.
        public float fastPacedRefundMultiplier = 1.2f;
        // Global Time.timeScale while a FastPaced launch is in flight (direct request: "when
        // launching increase gamespeed to 150%, reset after") - the counterpart to
        // chargeTimeScale's slow-down, applied by the same ApplyChargeTimeScale. Deliberately
        // does NOT speed up camera/aiming (ThirdPersonOrbitCamera runs on unscaled time and only
        // halves itself when the game runs SLOW) or airborne nudging
        // (KineticCubeControllerFreeMove divides its nudge acceleration back out by timeScale
        // when >1) - both direct requests.
        public float fastPacedFlightTimeScale = 1.5f;
        public InputActionReference fastPacedAimAction;
        public InputActionReference fastPacedLaunchAction;

        [Header("Energy Control (EnergyRegulation scenes)")]
        // See the EnergyControlMode enum - Standard everywhere except the four EnergyRegulation
        // scenes, whose Player instances each carry their own mode.
        public EnergyControlMode energyControlMode = EnergyControlMode.Standard;
        // CircleCrank: one full crank revolution changes the charge by this fraction of a full
        // charge; input only counts while deflected at least crankDeadzone.
        public float crankChargePerRevolution = 0.5f;
        [Range(0f, 1f)] public float crankDeadzone = 0.9f;
        // DedicatedButtons: stick-held rate (full-charge fractions per second) and per-wheel-
        // notch step.
        public float buttonChargeRate = 0.5f;
        public float wheelChargeStep = 0.05f;
        // Automatic: how far the aim ray looks for a target, and how many ternary-search
        // iterations refine the solved charge each frame (each iteration costs two trajectory
        // simulations - lower this if aiming ever feels heavy).
        public float autoAimMaxDistance = 400f;
        public int autoSearchIterations = 6;
        // Added on top of the solved minimum charge (direct request: "the minimum energy
        // needed to reach it + a small failsafe like 5%") - a slight overshoot beats clipping
        // the near edge.
        [Range(0f, 0.5f)] public float autoChargeFailsafe = 0.05f;

        [Header("Energy Economy (EnergyEconomy1)")]
        // Scene-scoped (EnergyEconomy1, direct request): crash refunds derive ONLY from the
        // last launch's own spend - ground launch pays back exactly its cost, a midair launch
        // pays spend * (1.01 + 0.01 * X) with X = full tank / spend (smaller launches earn a
        // proportionally bigger bonus), and the West straight-down air launch pays a flat 1.2x.
        public bool lastLaunchRefundEconomy = false;
        // Scene-scoped: West starts a straight-down hold-to-charge MIDAIR ONLY - the South
        // up-launch mirrored downward (hold to charge, release to fire, stick tilts it);
        // grounded West stays inert.
        public bool westAirDownLaunch = false;
        // The ground pound's crash refund: spend * this (direct request - editor-tunable).
        public float groundPoundRefundMultiplier = 1.2f;
        // ... but never less than this fraction of the full tank (direct request: "at least
        // 10% of the max energy").
        public float groundPoundMinRefund = 0.1f;
        // Straight-up / ground-pound charges fill this much faster than a regular grounded
        // launch charge (direct request: 1.5x). Those charges also run on UNSCALED time, so
        // this is a clean multiple of the grounded reference rate in both cases.
        public float upDownChargeSpeedMultiplier = 1.5f;
        // Midair-launch refund curve (direct request): refund = spend * (factor * X + 1), with
        // X = spend / full tank - so the multiplier RISES with how much was committed (a 10%
        // launch pays 1.3x at factor 3, a 50% launch 2.5x). Exposed so the slope is tunable
        // without touching code; the reserve floor and the 100% cap bound it either way.
        public float midairRefundSpendFactor = 3f;

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

        [Header("Crash Stick")]
        // How close a crash surface's normal has to be to world-up (Vector3.Dot with Vector3.up)
        // to count as "flat ground" for the walk-away-without-launching exception (see isStuck's
        // own comment) - direct request, fixing a bug the exception itself introduced: "when you
        // crash into a surface that is at least not a flat plane like solid ground, you still
        // don't stick, you fall immediately". The old check reused isGrounded (a plain downward
        // BoxCast, true for anything with support below regardless of its angle), which wrongly
        // treated ramps/near-walls as walkable ground and auto-cleared isStuck for them too. 0.9
        // only accepts genuinely near-horizontal surfaces (~25 degrees off flat or less).
        [Range(0f, 1f)] public float flatGroundStickThreshold = 0.9f;
        // How steeply DOWN a launch has to be aimed (Vector3.Dot(direction, Vector3.down)) to
        // count as a "slam" for OnCollisionEnter's guard-bypass (see currentFlightIsDownward's own
        // comment) - direct request fixing a real bug: launching straight down from standing on
        // the ground never registered as a crash at all, since it always fell inside
        // launchGraceDuration/minLaunchClearDistance, guards built for a DIFFERENT problem
        // (a shallow horizontal shot spuriously re-touching its own launch platform). 0.7 covers
        // StickAim's down-charge (60 degrees off horizontal by default = dot ~0.87) and Defy
        // Gravity's Down direction (straight down, dot 1.0) while still excluding mostly-forward
        // or mostly-up shots that legitimately need the spurious-recontact protection.
        [Range(0f, 1f)] public float slamDownwardThreshold = 0.7f;
        // Backstop for the general case slamDownwardThreshold's INSTANT check doesn't cover: a
        // launch that never gets any real separation from the ground at all, in ANY direction -
        // found empirically (a trajectory-distance diagnostic while designing a level, not a
        // direct report) firing a dead-level Defy Gravity Forward burst from standing on flat
        // ground: with zero vertical component it stays exactly at ground height the whole
        // flight, slides to a stop under ordinary friction, and never leaves contact long enough
        // to re-trigger OnCollisionEnter - hasLaunched then never clears, exactly the same
        // permanent-stuck symptom currentFlightIsDownward was built to fix, just via sliding
        // contact instead of resting contact. Counted in FixedUpdate ticks rather than seconds to
        // match launchGraceDuration's own scale - high enough that a real upward/angled shot
        // (which clears groundCheckDistance within 1-2 ticks, see FixedUpdate's own isGrounded
        // comment) is never mistaken for one of these, low enough that the stuck case is caught
        // quickly (0.2s at the default 50Hz fixed timestep).
        public int stuckOnGroundTickThreshold = 10;

        [Header("Sticky Walls")]
        // SlowPacedLevel only (every other scene's instance leaves this false, keeping the
        // universal any-surface stick): when true, only a wall carrying a StickySurface
        // component (with sticky enabled) holds the cube until the next launch. Crashing into
        // any OTHER non-flat surface still stops the cube dead and refunds energy exactly like
        // a normal crash, but only for nonStickyWallStickDuration seconds - then it lets go and
        // falls under gravity (with downLaunchDamping's low drag, so gravity is visibly the
        // thing doing the falling). Near-flat ground (flatGroundStickThreshold) is exempt
        // either way - floors keep the walk-away-freely behavior in every scene.
        public bool stickyWallsOnly = false;
        public float nonStickyWallStickDuration = 0.3f;

        [Header("Launch Limit")]
        // Tutorial only (direct request: "the player should only be allowed to perform 2
        // launches before landing") - 0 means unlimited, the default everywhere else. Counts
        // every launch since the cube last stood on the ground; a crash (any surface) is a
        // "landing" and resets it, so a wall crash mid-chain gives fresh launches, exactly like
        // the energy refund treats it as a fresh start.
        public int maxLaunchesPerFlight = 0;

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
        // Tutorial2 only (direct request: "replace the tutorial's midair controls with the air
        // controls of the FastPaced Level"): while Mixed is active and this is on, the AIRBORNE
        // phase runs the full FastPaced flow instead of the LT-gated directional charges -
        // hold Right Mouse / Left Trigger to aim (first-person camera + reticle), hold Left
        // Mouse / Right Trigger to charge, release to fire along the camera's look direction.
        // Grounded controls stay Mixed's own (LT aim / RT confirm, South's up-charge). Needs
        // fastPacedAimAction/fastPacedLaunchAction wired, same as FastPacedLevel. Also locks
        // the OS cursor for the whole scene, since the air aim is mouse-driven.
        public bool mixedFastPacedAir = false;
        // Tutorial only (direct request): mouse+keyboard support for the midair charges -
        // Space also starts/releases the Up charge, E the Down charge (read directly off the
        // keyboard; those keys' action-asset bindings, Jump/Interact, are unused by this
        // controller so nothing double-triggers), and a Forward charge with the stick centered
        // fires along the CAMERA's exact look direction, so the mouse aims it by orbiting the
        // camera. The air-aim gate (RMB) and forward trigger (LMB) already exist as bindings
        // on launchAction/fireAction. Off everywhere else.
        public bool mouseAirControls = false;
        // Fast-paced scenes' pause-menu option (GroundedAimToggle): when on, the grounded
        // LT-aim's direction is adjusted with raw MOUSE DELTA instead of the stick/WASD - the
        // two input styles are being playtested against each other, so the switch is exclusive
        // rather than both-at-once. While aiming this way, the camera ignores mouse look so the
        // same hand motion doesn't spin the view and the aim arrow together.
        public bool groundedAimWithMouse = false;
        public float groundedMouseAimSensitivity = 0.15f;
        // WASD-as-camera turns this much faster while Always Mouse is on (direct request:
        // "say 50% bigger") - key input is all-or-nothing, unlike an analog stick, so it needs
        // the extra rate to cover the same arcs comfortably.
        public float wasdCameraTurnMultiplier = 1.5f;
        // Tutorial3/TestLevel3 (direct request): the AIR uses the exact same aiming flow as the
        // GROUND - LT aim + RT confirm, Space/South up-charge, everything - instead of the
        // LT-gated directional air charges. Routed by sending airborne frames through the
        // grounded branch in UpdateMixedScheme.
        public bool airUsesGroundedAim = false;

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
        // "While launching and not aiming, make the game speed 200%" (direct request) - global
        // Time.timeScale while a launch is in flight (hasLaunched) and nothing is charging or
        // aiming (those keep chargeTimeScale's slow-down, which always wins). Purely the
        // player's PERCEPTION of speed, not the real values: the trajectory is identical (same
        // physics, just more of it per real second), the camera reads unscaled time so turning
        // is unaffected, and the airborne nudge divides its acceleration back out by any
        // timeScale above 1 (see KineticCubeControllerFreeMove.FixedUpdate), so steering
        // strength per real second is unchanged too. Resets to 1x the moment the flight ends
        // (crash or landing). FastPaced ignores this and keeps its own
        // fastPacedFlightTimeScale.
        public float launchFlightTimeScale = 2f;

        [Header("FastPacedLevel Tweaks")]
        // Scene-scoped (FastPacedLevel only, direct request): no midair nudging at all - a
        // launch flies exactly as fired.
        public bool disableAirNudge = false;
        // Scene-scoped: EITHER stick aims. The left stick is free once nudging is off, so
        // whenever the right stick is idle the left one drives the camera (= the aim) instead.
        public bool aimWithEitherStick = false;

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
        // Set in QueueLaunch, read by OnCollisionEnter - a launch aimed steeply enough downward
        // (a "slam") is EXPECTED to immediately re-strike whatever surface it fired from (that's
        // the whole point of aiming down while standing on the ground), so launchGraceTimer/
        // minLaunchClearDistance's spurious-recontact protection doesn't apply to it the way it
        // does to a forward/upward shot - see OnCollisionEnter's own comment on why this bypasses
        // both guards. Direct request, fixing a real soft-lock: "when you touch a wall after
        // launching you should stick to it" type crashes that should register right away (a close
        // slam) instead never did, leaving hasLaunched stuck true and movement blocked, which
        // was worst when energy was also 0 (no way to fire a NEW, farther-travelling shot either).
        bool currentFlightIsDownward;
        // See FixedUpdate's own comment - the clean, pre-collision-resolution velocity used by
        // GainEnergyFromCrash instead of reading rb.linearVelocity directly inside OnCollisionEnter.
        Vector3 velocityBeforePhysicsStep;
        // Counts consecutive FixedUpdate ticks the cube has read isGrounded while still
        // hasLaunched - see stuckOnGroundTickThreshold's own comment for what this catches that
        // currentFlightIsDownward's instant check doesn't.
        int groundedTicksSinceLaunch;
        bool isGrounded;
        // True from the instant a genuine in-flight crash is detected (see OnCollisionEnter)
        // until the next launch actually fires (see FixedUpdate's launchQueued handling) - "you
        // stop all movement and stick to that location until you launch again" (direct request).
        // Any surface counts, not just ground - confirmed directly.
        bool isStuck;
        // The contact normal from whichever crash set isStuck - checked against
        // flatGroundStickThreshold in FixedUpdate (only a near-horizontal surface auto-clears
        // isStuck without a fresh launch) and fed to freeMoveController.AlignVisualToSurface so
        // the cube visually rests flush against whatever it stuck to, floor or wall alike.
        Vector3 stuckSurfaceNormal;
        // >0 only while stuck to a NON-sticky wall in a stickyWallsOnly scene - counts down in
        // FixedUpdate and releases the stick (gravity back on) when it runs out. See
        // nonStickyWallStickDuration's own comment.
        float nonStickyReleaseTimer;
        // Launches fired since the cube last rested on the ground (or crashed) - gates new
        // charges against maxLaunchesPerFlight. See that field's own comment.
        int launchesSinceGrounded;
        // Mixed's airborne aim gate (direct request: "when you want to launch midair, you need
        // to press left trigger to aim again... letting go of the left trigger stops aiming in
        // midair too"): true while LT is held in the air with no charge fired yet. Freezes the
        // cube (FixedUpdate) exactly like every other aim/charge state, and gates the three
        // directional charges - RT/South/West only start a charge while this is on. Cleared by
        // releasing LT (cancels any charge in progress without firing), by firing, or by a
        // scheme switch.
        bool mixedAirAiming;
        // True for the whole flight after a hybrid (mixedFastPacedAir) air launch: WASD is the
        // CAMERA control during that aim, so it's routinely still held on the release frame -
        // and a held stick instantly became the airborne nudge force, bending the real flight
        // away from the predicted dotted line (direct bug report: "the line should show the
        // exact path"). Locking the nudge out for these flights makes the trajectory match the
        // prediction exactly; cleared on any crash/landing. FastPacedLevel's own scheme keeps
        // its documented WASD-nudge behavior.
        bool fastPacedFlightExact;
        // Energy-control state: the crank's last input angle (degrees, math convention),
        // whether it was valid last frame, and ReverseDirection's current draining flag.
        float crankPreviousAngle;
        bool crankHasPreviousAngle;
        bool reverseChargingDown;
        // Automatic mode: the aimed shot needs more energy than is stored - drives the meter's
        // red charge bar.
        bool chargeDisplayInsufficient;
        // Automatic mode: a PositioningObject touched mid-flight force-opens the aim without
        // any button held (a mid-air re-aim checkpoint) - stays open until the player fires,
        // takes over by pressing the aim button, or the aim cancels. Once per launch.
        bool autoAimForced;
        bool positioningAimUsedThisFlight;
        // After a launch (or any aim cancel), a still-HELD aim button counts for nothing -
        // no aim, and no raw-button slow-mo either (direct request: "aim shouldn't be
        // registered still if you held on to it... only reactivated if you let go and then
        // repress") - until genuinely released once. Cleared in Update the frame both aim
        // buttons are up.
        bool aimButtonSpent;
        EnergyCrankUI energyCrankUI;
        float chargeTime;
        float aimYaw;
        float aimPitch;
        // [SerializeField] is load-bearing, not decorative - a bare private field is never
        // written to the saved scene/prefab at all, so SetControlScheme's assignment (called by
        // every scene's Editor setup script) would only ever hold for the remainder of that
        // Editor session and silently revert to this field's C# default (StickAim) the next time
        // the scene is loaded, including at real Play/build time. Every scene before
        // FastPacedLevel happened to want StickAim anyway, which is exactly why this was never
        // noticed - StickAim IS the default, so the bug was invisible. FastPacedLevel needs a
        // genuinely different starting scheme, which is what surfaced this.
        [SerializeField] ControlScheme controlScheme = ControlScheme.StickAim;
        // 0-1 fraction of a full tank, shared by every scheme - see startingEnergyFraction's own
        // comment for how it's spent/gained.
        float energyFraction;
        // What the most recent launch actually deducted from energyFraction - the ACTUAL amount
        // (clamped by what was available), not the theoretical ChargeFraction cost, so the
        // FastPaced refund can never fabricate energy a nearly-empty tank didn't really spend.
        // See fastPacedRefundMultiplier.
        float lastLaunchEnergySpent;
        // Where/how the last launch fired - drives lastLaunchRefundEconomy's per-launch rules.
        bool lastLaunchWasGrounded;
        bool lastLaunchWasAirDown;

        // Read by KineticCubeControllerFreeMove to know whether it should instantly face
        // movement direction while walking (StickAim only - see its FixedUpdate).
        public ControlScheme CurrentScheme => controlScheme;

        // Read-only state for companion components (OutOfEnergyRestart) - no behavior of their
        // own, just visibility into what the controller already tracks.
        public float EnergyFraction => energyFraction;
        public bool IsStuck => isStuck;
        public bool IsGrounded => isGrounded;
        // Any aim/charge state - all of them deliberately hold velocity at zero, so a
        // stranded-check based on "not moving" has to exclude them (see OutOfEnergyRestart).
        public bool IsAimingOrCharging => isAiming || fastPacedAiming || fastPacedCharging || mixedAirAiming
            || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None;

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
        // thinks it is. isStuck itself now auto-clears the instant the cube is genuinely grounded
        // again (see FixedUpdate's isGrounded check) - direct request: "you should still be able
        // to move on the ground no matter how much energy you have got left" - so this only stays
        // blocking for a wall-stuck cube (never satisfies isGrounded) until it launches free.
        // Also blocked while charging any of the three hold-to-charge systems, same reasoning as
        // isAiming - the cube needs to stay put while charging, ground or air.
        public bool AllowGroundedMovement => !isAiming && !hasLaunched && !isStuck && !mixedAirAiming
            && stickAimChargeType == StickAimChargeType.None && defyGravityChargeType == DefyGravityFlightType.None && !fastPacedCharging
            // "While aiming you shouldn't be able to move" (direct request) - a grounded
            // FastPaced-style aim (Automatic's grounded auto-aim especially) locks walking too.
            && !fastPacedAiming;

        // AllowAirborneNudge: safe for FreeMove to apply a small, ADDITIVE force (air control,
        // leaning) while genuinely airborne. Only needs to wait out the brief post-launch grace
        // window, not the whole flight - an additive nudge can't stomp the launch the way
        // directly setting velocity can, so there's no reason to also suppress this for the
        // entire duration of every shot (which is what silently killed air-nudging for launched
        // shots the last time this was "fixed"). Also blocked while isStuck (frozen, not falling)
        // and during Defy Gravity's forced-velocity flight window, where even a small additive
        // nudge would spoil the straight line the charge promised.
        public bool AllowAirborneNudge => !isAiming && !isStuck && !mixedAirAiming && defyGravityFlightTimer <= 0f && launchGraceTimer <= 0f
            && stickAimChargeType == StickAimChargeType.None && defyGravityChargeType == DefyGravityFlightType.None && !fastPacedCharging
            // Tutorial2's midair aim repurposes the left stick for AIMING (see the camera's
            // SetAimStickOverride) - the same stick must not simultaneously nudge the flight.
            && !(mixedFastPacedAir && fastPacedAiming)
            // A hybrid fast-paced launch flies the predicted line EXACTLY - see the flag's own comment.
            && !fastPacedFlightExact
            // FastPacedLevel: nudging disabled outright - see the field's own comment.
            && !disableAirNudge;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;
        float queuedDamping;
        // >0 only for a Defy Gravity launch - see QueueLaunch's own comment.
        float queuedDefyGravityDuration;
        float launchGraceTimer;
        Vector3 launchStartPosition;
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
        // True from the moment Right Mouse is pressed (with energy available) until it's
        // released - see UpdateFastPacedScheme. Governs ONLY the camera's first-person mode and
        // reticle visibility, not movement - unlike every other scheme, "aiming" here can span
        // several shots in a row (direct request: only releasing Right Mouse reverts the camera),
        // so it must NOT freeze velocity itself, or the very shot that just fired would be
        // re-frozen the instant this flag re-triggers next frame while Right Mouse is still held.
        bool fastPacedAiming;
        // True only between a fresh Left Mouse press and its release/fire - THIS is what freezes
        // velocity and accumulates charge (see FixedUpdate/AllowGroundedMovement/
        // AllowAirborneNudge), matching every other scheme's "frozen only while actively
        // charging" behavior. Requires a genuinely fresh press to (re)start, same as StickAim/
        // DefyGravity's WasPressedThisFrame gate - holding Left Mouse through a fire does not
        // auto-restart a new charge.
        bool fastPacedCharging;
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
        // Whether that prediction actually LANDED on something (as opposed to trailing off
        // into open air) - the camera only frames a real landing spot, see framingAim.
        bool hasValidPredictedLanding;

        GameObject predictionClone;
        Rigidbody predictionRb;
        BoxCollider predictionCloneCollider;
        PredictionCloneStopper predictionStopper;
        // Per-frame caches (performance): geometry sync and spawn depenetration are identical
        // for every prediction within one frame, so they run once - the Automatic solver fires
        // ~19 predictions per frame and was paying both costs every single time.
        int predictionSyncFrame = -1;
        int spawnCacheFrame = -1;
        Vector3 spawnCacheStart;
        Vector3 spawnCacheResult;
        // Automatic solve cache: re-solving 19 simulations every frame was the main perf sink -
        // the result only changes when the TARGET moves, so it's reused until it does (or a
        // periodic refresh).
        Vector3 lastAutoTarget;
        float lastAutoSolvedCharge = -1f;
        int lastAutoSolveFrame = -1000;
        // Normal of the face the last prediction landed on (world up when it didn't land) -
        // orients the cross-and-ring marker flush against that face.
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
            // Same Player object as KineticCubeControllerFreeMove - used to snap the visual to
            // instantly face the launch direction the moment a launch fires (see FixedUpdate).
            freeMoveController = GetComponent<KineticCubeControllerFreeMove>();
            energyCrankUI = GetComponent<EnergyCrankUI>(); // present only in the CircleCrank scene
            ApplyGravity();
            energyFraction = startingEnergyFraction;
            // Defensive - OnCollisionEnter turns this off while stuck; a scene saved mid-stuck (or
            // any other stale serialized state) shouldn't start the game with gravity off.
            rb.useGravity = true;
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
            ApplyGamepadBlock();
        }

        // Always Mouse blocks ALL gamepad gameplay input (direct request) by masking the shared
        // action asset down to the Keyboard&Mouse bindings - every action this controller, the
        // free-move controller, and the camera read comes from that asset, so one mask covers
        // them all. The MENUS stay controller-usable: the UI input module runs on its own
        // default actions asset (unmasked), and PauseController reads the Start button directly
        // as the escape hatch for OPENING the menu. Called from Start (each scene load resets
        // the runtime mask to that scene's setting) and from GroundedAimToggle on every flip.
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
            // FastPaced is mouse-driven (camera on raw mouse delta, RMB/LMB as its two triggers),
            // so the OS cursor has to be locked+hidden during play or it drifts across (and
            // eventually out of) the window while looking around - direct request: "the cursor
            // should lock when playing that scene". Released whenever the game is paused, since
            // the pause menu's buttons are unusable without a visible, free cursor - which is why
            // this sits ABOVE the timeScale early-return below (that return firing is exactly the
            // paused case this must react to). Every other scheme is gamepad-driven and never
            // locks, preserving their existing behavior untouched.
            // The cursor is locked in EVERY scene now (direct request), releasing only while
            // paused (the pause/win menus need a visible, free cursor - and MainMenu has no
            // player, so its own controller unlocks there).
            {
                bool paused = Time.timeScale <= 0f;
                Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = paused;
            }

            // Time.timeScale freezes deltaTime-scaled logic (like charge accumulation) for free,
            // but not this raw edge-detected input - without this guard, aiming/firing could
            // still start or complete while the pause menu is up.
            if (Time.timeScale <= 0f) return;

            // The spent-aim-button latch re-arms only once the buttons are genuinely up - see
            // aimButtonSpent's own comment.
            if (aimButtonSpent && !AimButtonHeld()) aimButtonSpent = false;

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

            // Tutorial2's midair aim steers with the LEFT stick (direct request) - fed to the
            // camera every frame while that aim is active and cleared otherwise, so the right
            // stick hands control back cleanly the moment the aim ends. Mouse aiming is
            // unaffected (the camera only substitutes non-mouse look input).
            if (cameraOrbit != null)
            {
                // Two users of the left-stick/WASD -> camera substitution: Tutorial2's midair
                // aim, and the "Aim: Mouse" grounded option - there the mouse steers the aim
                // arrow, so WASD takes over the CAMERA (direct request), while mouse look is
                // suppressed below.
                // The energy-control modes that repurpose the sticks: CircleCrank owns BOTH
                // sticks/WASD for the whole aim (the crank), DedicatedButtons owns the right
                // stick while the charge is live - the camera must ignore stick look then
                // (mouse look still works; SetAimStickOverride only substitutes non-mouse input).
                bool energyModeOwnsSticks =
                    (energyControlMode == EnergyControlMode.CircleCrank && (fastPacedAiming || fastPacedCharging))
                    || (energyControlMode == EnergyControlMode.DedicatedButtons && fastPacedCharging);

                bool moveIsGamepadDriven = moveAction != null && moveAction.action != null
                    && moveAction.action.activeControl != null && moveAction.action.activeControl.device is Gamepad;
                bool aimWithMoveStick = energyModeOwnsSticks
                    || (mixedFastPacedAir && controlScheme == ControlScheme.Mixed && (fastPacedAiming || fastPacedCharging))
                    // Grounded mouse-aim hands WASD to the camera - but a GAMEPAD stick keeps
                    // aiming instead (see UpdateChargeBasedScheme), so it must not be captured.
                    || (groundedAimWithMouse && isAiming && !moveIsGamepadDriven);
                Vector2 aimStick = aimWithMoveStick && moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;
                if (aimStick.sqrMagnitude < aimDeadzone * aimDeadzone) aimStick = Vector2.zero;
                // WASD camera control runs faster under Always Mouse - see the field's comment.
                if (groundedAimWithMouse && isAiming && !moveIsGamepadDriven) aimStick *= wasdCameraTurnMultiplier;
                // The LEFT stick keeps adjusting the midair aim in every energy mode (direct
                // request) - the modes only claim what they actually use: CircleCrank takes the
                // right stick and KEYBOARD WASD (so keyboard input must not also steer the
                // aim), DedicatedButtons just the right stick. The override being active is
                // what blocks the right stick's normal camera look either way.
                if (energyControlMode == EnergyControlMode.CircleCrank && (fastPacedAiming || fastPacedCharging) && !moveIsGamepadDriven)
                {
                    aimStick = Vector2.zero;
                }
                // FastPacedLevel: either stick aims (direct request) - with the right stick
                // idle, the left one (free, since nudging is off there) drives the camera,
                // which IS the aim in that scheme. Gamepad only; the mouse keeps its own path.
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

                // While the mouse is steering the grounded aim, it must not ALSO orbit the
                // camera - see groundedAimWithMouse's own comment.
                cameraOrbit.SetMouseLookSuppressed(groundedAimWithMouse && isAiming);

                // EnergyEconomy1's up/ground-pound charges: the camera keeps full speed
                // through the bullet-time (direct request).
                cameraOrbit.SetIgnoreSlowMo(westAirDownLaunch
                    && (stickAimChargeType == StickAimChargeType.Up || stickAimChargeType == StickAimChargeType.Down));

                // First-person midair aim looks at the cursor at the end of the dotted line -
                // instantly on aim start, eased as the landing point moves (direct request).
                // Rotation only; the launch direction still comes from pitch/yaw, so there is
                // no feedback loop between where the view points and where the shot lands.
                bool framingAim = !isGrounded && hasValidPredictedLanding && (fastPacedAiming || fastPacedCharging);
                cameraOrbit.SetTrajectoryFraming(framingAim, lastPredictedLanding);
            }

            // Yellow energy / blue charge-preview meter - updated unconditionally every frame
            // (not just inside whichever scheme branch is active) so it stays correct through
            // scheme switches too.
            if (energyMeter != null)
            {
                energyMeter.SetEnergy(energyFraction);
                bool charging = isAiming || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None || fastPacedCharging
                    // The energy modes' charge is live for the whole aim - the bar shows it
                    // from the first aim frame (direct request).
                    || (fastPacedAiming && energyControlMode != EnergyControlMode.Standard);
                energyMeter.SetCharge(ChargeFraction(), charging);
                energyMeter.SetChargeWarning(chargeDisplayInsufficient);
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
            // Slow-mo starts the moment AIMING starts, in every scheme (direct request: "time
            // goes slower the moment you aim" - pressing LT/Right Mouse, not the launch
            // button): isAiming covers the grounded aim, mixedAirAiming the slow-paced air aim,
            // fastPacedAiming EVERY FastPaced-style aim (FastPacedLevel and the hybrids alike -
            // this supersedes the old FastPacedLevel full-speed-while-aiming behavior), and the
            // hold-to-charge states double as their own aim.
            // Aim-driven slow-mo is MIDAIR-ONLY (direct request: "time shouldn't be slowed
            // when aiming while grounded") - the raw-button read stays (slow from the physical
            // LT/RMB press onward, covering carried-over holds, energy-gated frames, and the
            // instant after firing), but only while airborne. Grounded aiming runs at full
            // speed; the hold-to-charge states below still slow whenever active.
            bool airborneAimSlow = !isGrounded && (isAiming || fastPacedAiming || mixedAirAiming || (AimButtonHeld() && !aimButtonSpent));
            bool charging = airborneAimSlow
                || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None
                || fastPacedCharging;
            // Charging's bullet-time wins over the flight speed-up - starting a mid-air charge
            // freezes the cube anyway (the "flight" is suspended), so the slow-down is the state
            // that matches what's on screen. hasLaunched clears on crash (RegisterCrash), which
            // is the "reset after" the request asks for; the fall-reset and pause system already
            // manage timeScale themselves on their own paths.
            bool fastPacedInFlight = controlScheme == ControlScheme.FastPaced && hasLaunched;
            // Every other scheme's in-flight speed-up - see launchFlightTimeScale's own comment.
            float flightScale = fastPacedInFlight ? fastPacedFlightTimeScale : (hasLaunched ? launchFlightTimeScale : 1f);
            Time.timeScale = charging ? chargeTimeScale : flightScale;
        }

        // The physical aim buttons, read raw: launchAction is Left Trigger (with the Right
        // Mouse binding), fastPacedAimAction the FastPaced aim where wired. IsPressed, not any
        // aim-state flag - see ApplyChargeTimeScale's use.
        bool AimButtonHeld()
        {
            if (launchAction != null && launchAction.action != null && launchAction.action.IsPressed()) return true;
            if (fastPacedAimAction != null && fastPacedAimAction.action != null && fastPacedAimAction.action.IsPressed()) return true;
            return false;
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

            // Energy alone gates starting a new aim now - "you should be able to launch unlimited
            // times, as long as you have energy left" (direct request, replacing the old per-
            // flight launch-count cap entirely). Both the original grounded start AND the mid-air
            // "air-relaunch" (or a charge started from a freshly-crashed isStuck position) go
            // through this exact same check, no matter the control scheme. Only used to gate a
            // BRAND NEW aim session starting, never to decide whether an ALREADY-ACTIVE one should
            // keep going (see ltHeld just below) - re-deriving a live/proximity-based condition
            // every frame of an already-active charge could spuriously flip it and read as "LT let
            // go", firing prematurely. This exact class of bug has bitten this project before.
            bool canStartNewAim = energyFraction > 0f && CanStartNewLaunch();
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
                if (groundedAimWithMouse && Mouse.current != null)
                {
                    // The pause-menu "Aim: Always Mouse" option - raw mouse delta adjusts the
                    // aim (already per-frame, no dt).
                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                    aimYaw = Mathf.Repeat(aimYaw + mouseDelta.x * groundedMouseAimSensitivity, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - mouseDelta.y * groundedMouseAimSensitivity, minAimPitch, maxAimPitch);
                }
                // In mouse mode only KEYBOARD (WASD) input is repurposed for the camera - a
                // GAMEPAD stick still aims like always (direct request: "you should still be
                // able to aim with your joystick if you are on controller").
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
        // same shared energyFraction gate, so switching between the two mid-flight (grounded
        // launch, then an airborne StickAim-style follow-up) is limited only by energy, the same
        // as every other scheme. Once either system has an active charge in
        // progress, stick with it regardless of isGrounded's exact value that frame - re-deciding
        // by isGrounded alone every frame could otherwise switch systems mid-charge right at a
        // ledge edge.
        void UpdateMixedScheme()
        {
            if (isAiming)
            {
                // The grounded LT-aim also converts into the Up hold-to-charge when the up
                // button is pressed mid-aim (direct request) - the accumulated charge carries
                // over, and the launch then fires on the up button's release. LT is still
                // physically held, so the one-shot latch stops it from reopening the old aim
                // the moment this charge ends.
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
                // The first-person aim (and its charge zoom) exists ONLY midair in this hybrid
                // (direct request: "should only happen if you aim when midair, not when you are
                // grounded") - the moment the cube is grounded again, the aim cancels cleanly
                // and the ordinary grounded Mixed controls take over, even if the aim button is
                // still held. EXCEPTION: Automatic Energy, whose grounded aim is the same
                // auto-solve (direct request), so its aim survives being grounded.
                if (isGrounded && energyControlMode != EnergyControlMode.Automatic) CancelFastPacedAim();
                else UpdateFastPacedScheme();
            }
            else if (isGrounded || airUsesGroundedAim)
            {
                // Automatic Energy: grounded aiming uses the SAME auto-solve flow as midair
                // (direct request: "aiming while grounded follows the same principle of
                // calculating how much energy you need"), replacing the build-up-over-time
                // grounded aim entirely in that scene.
                if (mixedFastPacedAir && energyControlMode == EnergyControlMode.Automatic)
                {
                    UpdateFastPacedScheme();
                    return;
                }
                // airUsesGroundedAim (Tutorial3/TestLevel3) sends AIRBORNE frames through this
                // same branch - the air-relaunch path inside UpdateChargeBasedScheme already
                // handles the freeze-and-aim mid-air, so "same controls as on the ground" is
                // literal here.
                // South's Up hold-to-charge also works straight from the ground (direct
                // request) - routing this frame's press into the stick-aim system starts the
                // charge; every following frame reaches it through the stickAimChargeType
                // branch above. Everything else grounded stays the LT-aim/RT-confirm flow.
                // Space joins South here in the mouse-controls scenes (direct request) - the
                // release path already listens for Space via the same mouseAirControls flag.
                bool upPressed = energyFraction > 0f && ((upLaunchAction != null && upLaunchAction.action != null && upLaunchAction.action.WasPressedThisFrame())
                    || (mouseAirControls && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame));
                if (upPressed) UpdateStickAimChargeScheme();
                else UpdateChargeBasedScheme();
            }
            else if (mixedFastPacedAir)
            {
                // EnergyEconomy1: West starts the straight-down hold-to-charge, MIDAIR ONLY
                // (direct request - South's up-launch mirrored downward; the charge then runs
                // through the ordinary stick-aim branch above until release fires it).
                bool groundPoundPressed = (selectClassicSchemeAction != null && selectClassicSchemeAction.action != null
                        && selectClassicSchemeAction.action.WasPressedThisFrame())
                    // E on keyboard (direct request) - the release path already listens for E
                    // via the same mouseAirControls flag.
                    || (mouseAirControls && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);
                if (westAirDownLaunch && energyFraction > 0f && CanStartNewLaunch() && groundPoundPressed)
                {
                    StartStickAimCharge(StickAimChargeType.Down);
                    return;
                }

                // Tutorial2's air phase: the full FastPaced flow (RMB/LT aim in first person,
                // LMB/RT charge, fire along the look direction) - see mixedFastPacedAir's own
                // comment.
                UpdateFastPacedScheme();
            }
            else
            {
                UpdateMixedAirScheme();
            }
        }

        // Mixed's airborne flow (direct request rework): Left Trigger is the aim GATE - holding
        // it freezes the cube in place, and only then do RT/South/West start their
        // Forward/Up/Down hold-to-charges (release fires, stick tilts the angle, exactly as
        // before). Releasing LT stops aiming - including cancelling a charge in progress
        // without firing (see UpdateStickAimChargeScheme's mixedAirAiming check). After a fire
        // LT must be genuinely released before it can open a new aim (waitingForLtRelease, the
        // same one-shot-per-hold rule the grounded flow uses), or the shot that just fired
        // would be re-frozen the very next frame while LT is still down.
        void UpdateMixedAirScheme()
        {
            bool ltHeld = launchAction != null && launchAction.action != null && launchAction.action.IsPressed();

            if (waitingForLtRelease)
            {
                if (!ltHeld) waitingForLtRelease = false;
                return;
            }

            // Same gates as starting any new launch - without energy (or launches) left, LT
            // does nothing, so the freeze can't be used to hover indefinitely.
            if (ltHeld && energyFraction > 0f && CanStartNewLaunch())
            {
                if (!mixedAirAiming)
                {
                    mixedAirAiming = true;
                    // Instant stop, same as every other aim/charge start - FixedUpdate keeps
                    // re-applying this for as long as the aim is held.
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

        // Mixed-air remap (direct request): the Down charge sits on West there, since Left
        // Trigger in Mixed's air phase opens the full aim-and-confirm flow instead. Standalone
        // StickAim keeps its original LT = Down. West is free in Mixed - HandlePreviewModeSwitch
        // (its only other reader) is skipped for StickAim/Mixed in Update().
        InputActionReference DownChargeActionForCurrentScheme()
        {
            return controlScheme == ControlScheme.Mixed ? selectClassicSchemeAction : launchAction;
        }

        // Gates STARTING a new charge only, never an already-active one - same reasoning as the
        // energy gate (canStartNewAim): re-deriving mid-charge could spuriously cancel it.
        bool CanStartNewLaunch()
        {
            return maxLaunchesPerFlight <= 0 || launchesSinceGrounded < maxLaunchesPerFlight;
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
            // EVERY aim/charge state holds the cube still, the forward first-person aim
            // included (direct request: aiming forward midair should be as slow as aiming
            // downwards - the difference was never the timescale, both run at 0.75; the
            // vertical charges froze velocity while this aim let you keep falling at full
            // speed). Safe now that firing closes the aim and a held button can't reopen it -
            // the old "don't freeze here" rule existed for when one hold spanned several shots.
            if (isAiming || stickAimChargeType != StickAimChargeType.None || defyGravityChargeType != DefyGravityFlightType.None
                || isStuck || fastPacedCharging || mixedAirAiming || fastPacedAiming)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            bool slamJustFired = false;
            float slamForce = 0f;

            if (launchQueued)
            {
                launchQueued = false;
                isStuck = false; // breaking free of a crashed/stuck position, if it was set
                nonStickyReleaseTimer = 0f; // launching supersedes a pending timed release
                rb.useGravity = true; // back on, undoing OnCollisionEnter's stick - direct request
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

                slamJustFired = currentFlightIsDownward;
                slamForce = queuedForce;
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
            // now purely energy-based) or to re-arm after a landing (see OnCollisionEnter below,
            // now event-driven instead of debounced-velocity-driven).
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
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out RaycastHit groundHit, transform.rotation, groundCheckDistance);

            // Standing (or walking) on the ground with no flight in progress restores the full
            // per-flight launch budget - hasLaunched must be false so the brief grounded window
            // right after firing (launch grace) can't refund the launch that just spent it.
            if (isGrounded && !hasLaunched)
            {
                launchesSinceGrounded = 0;
                fastPacedFlightExact = false;
                ClampEnergyFloor(); // landed with 5% or less -> topped back up to 5%
            }

            // A slam fired from ZERO clearance (already resting on the exact surface it's aimed
            // at) never actually leaves that surface - PhysX's contact solver absorbs a downward
            // impulse into an already-supporting surface instantly, same as pushing down on
            // something already resting on a table, so rb.linearVelocity never shows any change
            // and OnCollisionEnter never fires again (the contact never broke to begin with; at
            // most Unity reports OnCollisionStay, which this script doesn't listen for at all).
            // Confirmed directly with a Play-mode diagnostic - a Vector3.down slam from standing
            // on flat ground left velocity/position completely unchanged for 35+ physics ticks
            // despite currentFlightIsDownward's OnCollisionEnter guard-bypass being in place and
            // working correctly; the crash simply never had an event to attach to. This handles
            // that case directly instead of waiting for a collision callback that isn't coming -
            // same direct request as currentFlightIsDownward's own comment, and same crash
            // handling (RegisterCrash) either way. Uses queuedForce as the crash speed proxy,
            // since the real velocity got absorbed before it could be measured - the cube
            // committed to that speed the instant it fired, even though the ground meant it never
            // got to carry it anywhere.
            if (slamJustFired && isGrounded)
            {
                // Slamming while STANDING on a breakable pane: the zero-clearance slam's
                // contact never breaks (see the comment above), so OnCollisionEnter's own
                // breakable check can't fire - smash it from here instead, and skip the crash
                // so gravity carries the cube down through the fresh hole.
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

            // Backstop for every OTHER direction that can end up with the exact same "never
            // actually leaves the ground" symptom slamJustFired already handles for steep downward
            // launches - see stuckOnGroundTickThreshold's own comment. Reset to 0 the instant
            // isGrounded reads false (a real departure), hasLaunched is false (nothing to catch -
            // walking counts as "grounded" too and must never trip this), or
            // defyGravityFlightTimer is still running - a flat Defy Gravity charge deliberately
            // holds a fixed height for its whole flight (that's the mechanic), which reads exactly
            // like "grounded" to the BoxCast the entire time; only counts from the moment that
            // forced-flight window actually ends.
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

            // Breaks a crashed/isStuck cube free automatically once it's genuinely resting on
            // FLAT ground again, no fresh launch required - direct request: "you should still be
            // able to move on the ground no matter how much energy you have got left". Without
            // this, running out of energy right after a floor crash would leave the cube frozen
            // forever (charging a new launch, the only other way isStuck clears, needs energy it
            // doesn't have). Requires isGrounded AND a near-horizontal stuckSurfaceNormal
            // (flatGroundStickThreshold) - isGrounded alone (just "something below me") isn't
            // enough, since it doesn't know the surface's angle: a wall or ramp can easily have
            // support within groundCheckDistance too, and wrongly auto-clearing THOSE was exactly
            // the bug direct feedback caught: "when you crash into a surface that is at least not
            // a flat plane like solid ground, you still don't stick, you fall immediately".
            if (isStuck && isGrounded && Vector3.Dot(stuckSurfaceNormal, Vector3.up) >= flatGroundStickThreshold)
            {
                isStuck = false;
                nonStickyReleaseTimer = 0f;
                rb.useGravity = true;
            }

            // Timed release from a NON-sticky wall (stickyWallsOnly scenes only - see
            // nonStickyWallStickDuration): the cling holds exactly like a normal stick for its
            // brief duration, then lets go and gravity takes over. downLaunchDamping (the low,
            // gravity-stays-in-charge drag the down-launch already uses) replaces whatever
            // damping the crashed flight left behind, so the drop reads as a clean fall rather
            // than sinking through high launch drag. Deliberately keeps ticking while a charge
            // is in progress - the charge freeze owns the cube then anyway, and firing clears
            // this timer via the launchQueued block above.
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

            // Captured LAST, after everything above (including a fresh launchQueued impulse this
            // exact tick) has had its say, and still strictly before Unity's physics simulation
            // (which runs only after every script's FixedUpdate this frame) resolves whatever
            // collision is about to happen - direct request, fixing "the energy you get from
            // crashes doesn't always seem to be consistent". OnCollisionEnter used to read
            // rb.linearVelocity directly, but by the time that callback fires PhysX may already
            // have partially applied its own collision response (a documented Unity gotcha), and
            // how much gets resolved before vs after the callback isn't consistent across impact
            // angles/geometry. Capturing at the TOP of FixedUpdate instead (an earlier version of
            // this fix) missed a same-tick "slam" launch's own impulse entirely, reading the
            // stale pre-launch velocity instead - capturing here avoids both problems at once.
            velocityBeforePhysicsStep = rb.linearVelocity;
        }

        // Trigger volumes: RestartWall frames (the FloatingWallBorder prefab's strips are
        // triggers on purpose - solid borders registered contacts through the wall face), and
        // Automatic Energy's PositioningObject checkpoints.
        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<RestartWall>() != null)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            // Automatic Energy: the FIRST PositioningObject touched after a launch freezes the
            // flight right there and opens the aim automatically (direct request) - a mid-air
            // re-aim checkpoint. Once per launch; the next launch re-arms it.
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
            // RestartWall: any touch reloads the level - checked before every guard below, so a
            // grounded walk-in restarts just as reliably as a mid-flight crash. Same reload the
            // fall-reset uses. (The TRIGGER variant below serves the FloatingWallBorder frames.)
            if (collision.collider.GetComponentInParent<RestartWall>() != null)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            // BreakableCrackWall: solid to everything EXCEPT a downward launch (direct request:
            // "you can only smash through if you launch downwards") - smashing destroys the
            // pane and restores the pre-impact velocity, since PhysX has already absorbed the
            // slam into the contact by the time this callback runs; without the restore the
            // cube would stop dead on a surface that no longer exists. A non-downward hit falls
            // through to the normal crash handling below, treating the pane as ordinary solid.
            BreakableCrackWall breakable = collision.collider.GetComponentInParent<BreakableCrackWall>();
            if (breakable != null && hasLaunched && currentFlightIsDownward)
            {
                breakable.Smash();
                rb.linearVelocity = velocityBeforePhysicsStep;
                return;
            }

            // Only a genuine in-flight crash counts - not pre-launch walking (hasLaunched false),
            // and not an already-stuck body (frozen, shouldn't be generating fresh contacts at
            // all, but defensive regardless). Any surface counts, walls included - direct
            // request: "when you touch a wall after launching you should stick to it, no matter
            // the control scheme" (this project briefly had walls fall instead of stick; that's
            // reverted here - see isStuck's own comment for how walking away afterward still
            // works without needing a fresh launch, on a floor/ceiling at least).
            if (!hasLaunched || isStuck) return;
            // A steeply-downward "slam" skips both guards below entirely - see
            // currentFlightIsDownward's own comment. For every OTHER launch direction they still
            // apply unchanged: launchGraceTimer guards against PhysX re-reporting a large impulse's
            // own launch-platform contact as a fresh collision, and minLaunchClearDistance guards
            // against a shallow, low-angle shot genuinely re-touching that same platform shortly
            // after. Direct request, fixing a real soft-lock: launching straight down from
            // standing on the ground fell inside BOTH of those windows every time, so the crash
            // never registered at all - no stick, no energy, and hasLaunched stayed true forever,
            // permanently blocking ground movement (worst at 0 energy, since firing a fresh,
            // farther-travelling shot to escape wasn't an option either).
            if (!currentFlightIsDownward)
            {
                if (launchGraceTimer > 0f) return;
                // See minLaunchClearDistance's own comment - a second, independent guard alongside
                // the time-based one above, for a shallow shot that can still genuinely re-touch its
                // own launch platform after the grace window has already expired.
                if (Vector3.Distance(transform.position, launchStartPosition) < minLaunchClearDistance) return;
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

            RegisterCrash(collision.GetContact(0).normal, velocityBeforePhysicsStep.magnitude, collision.collider);
        }

        // "Whenever you crash onto an object, you stop all movement and stick to that location
        // until you launch again" (direct request) - stop dead, freeze in place, and turn gravity
        // off outright (direct request, replacing reliance on the FixedUpdate freeze block's
        // continuous velocity-zeroing alone) so there's no way for even a single tick's worth of
        // gravity to sneak in a visible sag before the next zero. isStuck itself clears two ways:
        // a fresh launch (as before, see FixedUpdate's launchQueued handling), or automatically
        // once resting on genuinely FLAT ground - see flatGroundStickThreshold's own comment for
        // why isGrounded alone wasn't enough. Shared by OnCollisionEnter (the normal path - a shot
        // that travelled away and later hit something) AND FixedUpdate's own immediate-slam check
        // (see slamAbsorbedByGround's own comment) - a slam fired from ZERO clearance never gets a
        // fresh OnCollisionEnter to react to at all, since the cube never actually leaves the
        // surface it's already touching.
        void RegisterCrash(Vector3 contactNormal, float crashSpeed, Collider surface)
        {
            // A NonStickSurface (the launch button's cap) never registers as a crash at all -
            // no freeze, no energy refund; physics carries the cube onward, so it just falls
            // away again (direct request: "touching the button shouldn't be sticky, meaning
            // the player falls down again").
            if (surface != null && surface.GetComponentInParent<NonStickSurface>() != null) return;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;

            isStuck = true;
            hasLaunched = false;
            defyGravityFlightTimer = 0f; // interrupt an in-progress forced flight if the crash happens mid-flight
            launchesSinceGrounded = 0;   // a crash is a landing - the per-flight launch budget resets
            mixedAirAiming = false;      // defensive - the freeze should prevent crashing mid-aim at all
            fastPacedFlightExact = false; // the exact-line flight ended in this crash

            // "If you crash onto an object, aiming should be disabled until you pressed it
            // again" (direct request, all scenes/schemes): a crash closes any aim outright and
            // demands a genuine release-and-repress of the aim button - a trigger still held
            // from before the impact must not reopen aiming by itself. waitingForLtRelease is
            // exactly that one-shot-per-hold latch (honored by the grounded flow AND Mixed's
            // air-aim gate); the hybrid's FastPaced-style aim gets cancelled here and its own
            // entry already requires a fresh press.
            waitingForLtRelease = true;
            if (mixedFastPacedAir && (fastPacedAiming || fastPacedCharging)) CancelFastPacedAim();

            // Fed to flatGroundStickThreshold's check above and to AlignVisualToSurface below -
            // direct request: "the cubes surface should align with the surface it just hit, so
            // they are parallel".
            stuckSurfaceNormal = contactNormal;
            freeMoveController?.AlignVisualToSurface(stuckSurfaceNormal);

            // Sticky-walls-only mode (see the field's own comment): a wall crash only holds
            // permanently if the surface actually carries the sticky property - otherwise arm
            // the brief cling timer that FixedUpdate releases into a gravity fall. Near-flat
            // ground stays exempt (dot >= flatGroundStickThreshold), matching the auto-clear
            // that already lets the cube walk away from floors.
            nonStickyReleaseTimer = 0f;
            if (stickyWallsOnly && Vector3.Dot(contactNormal, Vector3.up) < flatGroundStickThreshold)
            {
                StickySurface stickySurface = surface != null ? surface.GetComponentInParent<StickySurface>() : null;
                if (stickySurface == null || !stickySurface.sticky)
                {
                    nonStickyReleaseTimer = nonStickyWallStickDuration;
                }
            }

            // FastPaced only - re-bases the whole camera orbit (and first-person aim) around the
            // crashed platform's surface normal, so "up" on screen matches the platform's own up
            // (direct request). The spiral's platforms face every direction around a full circle;
            // keeping world-up would leave the camera sideways or upside-down relative to
            // whatever you just stuck to. The other schemes' levels are world-up-oriented and
            // keep the camera's existing behavior untouched.
            if (controlScheme == ControlScheme.FastPaced)
            {
                cameraOrbit?.SetUpVector(stuckSurfaceNormal);
            }

            // Breakable crack panes never refund energy (direct request) - they exist to be
            // smashed through, not farmed. A non-downward crash still stops/clings exactly as
            // before; it just pays nothing.
            if (surface == null || surface.GetComponentInParent<BreakableCrackWall>() == null)
            {
                GainEnergyFromCrash(crashSpeed);
            }
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
            // rather than repeated with small variations per case. stickyWallsOnly scenes
            // (SlowPacedLevel) get their own wording, since "stuck until you launch" is only
            // true of the marked sticky walls there.
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
            // FastPaced's reticle is a dedicated TrailAndCrosshair mode, not the plain Trail this
            // toggle switches between - a stray Right Bumper press (a gamepad plugged in
            // alongside mouse/keyboard) would otherwise silently stomp it to Trail or None mid-aim.
            if (controlScheme == ControlScheme.FastPaced) return;
            // Reverse Direction repurposes RB as the charge-direction flip while its charge is
            // live - which is now the whole AIM (direct request: the flip is available while
            // aiming) - a flip must not also toggle the trail.
            if (energyControlMode == EnergyControlMode.ReverseDirection && (fastPacedAiming || fastPacedCharging)) return;
            if (trailToggleAction == null || trailToggleAction.action == null || !trailToggleAction.action.WasPressedThisFrame()) return;
            if (landingPreview == null) return;

            // Where crosshair visuals are unlocked for this scene (SlowPacedLevel), the toggle
            // restores the full trail+reticle mode rather than downgrading to plain Trail.
            PredictionMode shownMode = landingPreview.ghostAndCrosshairEnabled ? PredictionMode.TrailAndCrosshair : PredictionMode.Trail;
            landingPreview.SetMode(landingPreview.CurrentMode == PredictionMode.None ? shownMode : PredictionMode.None);
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
            if (fastPacedAiming)
            {
                CancelFastPacedAim();
            }
            mixedAirAiming = false;

            UpdateSchemeLabel();
        }

        // Cleanly winds down the FastPaced-style aim state: camera back to third person, zoom
        // reset, any charge discarded without firing. Shared by scheme switches and Tutorial2's
        // grounded-cancels-the-air-aim rule (see UpdateMixedScheme).
        void CancelFastPacedAim()
        {
            fastPacedAiming = false;
            fastPacedCharging = false;
            chargeTime = 0f;
            reverseChargingDown = false;
            crankHasPreviousAngle = false;
            chargeDisplayInsufficient = false;
            autoAimForced = false;
            lastAutoSolvedCharge = -1f; // next aim starts with a fresh solve
            aimButtonSpent = true; // closing the aim spends the hold - release before re-aiming
            energyCrankUI?.SetVisible(false);
            landingPreview?.SetVisible(false);
            cameraOrbit?.SetFirstPersonMode(false);
            cameraOrbit?.SetAimZoom(0f);
        }

        // ==================== Energy control modes (EnergyRegulation scenes) ====================

        // The right stick's raw value, gamepad-only - the look action carries mouse deltas too,
        // and those must never leak into the crank/buttons charge input.
        Vector2 GamepadLookValue()
        {
            InputActionReference look = cameraOrbit != null ? cameraOrbit.lookAction : null;
            if (look == null || look.action == null) return Vector2.zero;
            if (look.action.activeControl == null || !(look.action.activeControl.device is Gamepad)) return Vector2.zero;
            return look.action.ReadValue<Vector2>();
        }

        // Circle Crank: the popup dot follows the input direction along the ring; cranking
        // CLOCKWISE (angle decreasing, at >= crankDeadzone deflection) adds charge, counter-
        // clockwise subtracts - right stick and WASD alike (direct spec).
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
                    // Negative delta = clockwise in this convention = ADD energy.
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

        // Dedicated Buttons: right stick up adds / down subtracts at buttonChargeRate; mouse
        // wheel notches step by wheelChargeStep (direct spec).
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

        // Reverse Direction: standard time-charging, but RB / middle mouse flips it - the meter
        // jumps to the maximum the CURRENT energy allows and drains at the same rate charging
        // adds; pressing again flips back to adding from wherever it is (direct spec).
        void UpdateReverseCharge()
        {
            bool flipPressed = (trailToggleAction != null && trailToggleAction.action != null && trailToggleAction.action.WasPressedThisFrame())
                || (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame);
            if (flipPressed)
            {
                reverseChargingDown = !reverseChargingDown;
                if (reverseChargingDown) chargeTime = Mathf.Min(maxChargeTime, EnergyChargeCeiling());
            }

            // UNSCALED time both ways (direct request): the midair aim's slow-mo scales
            // Time.deltaTime, which made the meter fill/drain 25% slower in the air than
            // grounded - real-time rates keep the speed identical wherever you aim, and the
            // add and subtract directions symmetric by construction.
            if (reverseChargingDown)
            {
                chargeTime = Mathf.Max(chargeTime - Time.unscaledDeltaTime * chargeAccumulationRate, 0f);
            }
            else
            {
                chargeTime = Mathf.Min(chargeTime + Time.unscaledDeltaTime * chargeAccumulationRate, Mathf.Min(maxChargeTime, EnergyChargeCeiling()));
            }
        }

        // The non-Standard energy modes' whole aim phase (direct request): the trail+reticle
        // preview is live from the first aim frame, the mode's energy input is adjustable the
        // entire time, and the launch button is purely a CONFIRM - one fresh press fires the
        // previewed shot; both aim and launch need genuine re-presses afterwards. No freeze:
        // the shot is computed from the CURRENT motion (the launch impulse adds to it) - which
        // is also what fixed Automatic's short mid-air shots: the old solver assumed a
        // standing start, so any launch fired while already flying got the wrong charge.
        void UpdateEnergyModeAim(bool firePressed)
        {
            Vector3 dir = cameraOrbit != null ? cameraOrbit.AimForward : transform.forward;

            landingPreview?.SetVisible(true);
            landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);

            // "While grounded you shouldn't zoom in" (direct request): Automatic's grounded
            // aim stays third-person - first person only midair, switching live if the aim
            // spans both.
            if (energyControlMode == EnergyControlMode.Automatic)
            {
                cameraOrbit?.SetFirstPersonMode(!isGrounded);
            }

            switch (energyControlMode)
            {
                case EnergyControlMode.Automatic:
                    // Wherever the aim points - surface or PositioningObject sphere - the
                    // EXACT required charge is solved over the FULL range (not capped by
                    // stored energy): the meter shows the true requirement, red when it
                    // exceeds what's stored (direct request).
                    if (!TryGetAutoAimTarget(dir, out Vector3 target))
                    {
                        target = (cameraTransform != null ? cameraTransform.position : transform.position) + dir * autoAimMaxDistance;
                    }
                    // FIRE DIRECTION: from the PLAYER toward the target - NOT the camera's
                    // forward. In third person (the grounded aim) the camera looks DOWN past
                    // the player, and firing along that tilted direction needed absurd charge
                    // to reach anything - the routinely-way-too-much-energy bug. Player-to-
                    // target is the direction whose arc family reaches the spot with the
                    // minimum charge; in first person (midair) the two nearly coincide.
                    Vector3 toTarget = target - transform.position;
                    if (toTarget.sqrMagnitude > 0.01f) dir = toTarget.normalized;
                    // Amortized solve (performance): at most one search per
                    // autoSolveIntervalFrames, re-run only when the target has actually moved
                    // (with a slower periodic refresh) - a moving aim no longer pays the full
                    // search cost every single frame. The meter lags the aim by at most ~0.1s.
                    bool solveDue = lastAutoSolvedCharge < 0f
                        || (Time.frameCount - lastAutoSolveFrame >= 5
                            && ((target - lastAutoTarget).sqrMagnitude > 0.25f || Time.frameCount - lastAutoSolveFrame >= 20));
                    if (solveDue)
                    {
                        lastAutoSolvedCharge = SolveChargeForTarget(dir, target);
                        lastAutoTarget = target;
                        lastAutoSolveFrame = Time.frameCount;
                    }
                    // The solved minimum plus the failsafe margin - see autoChargeFailsafe.
                    float required = Mathf.Clamp01(lastAutoSolvedCharge + autoChargeFailsafe);
                    // Meter AND launch intake CAP at what's stored (direct request) - the red
                    // bar sits at the current level and flags that the true need is higher.
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

            // What actually fires: the dialed/required charge capped by what the tank can pay.
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
                chargeTime = fireFraction * maxChargeTime; // pay exactly for what fires
                QueueLaunch(dir, force, damping);
                if (mixedFastPacedAir && controlScheme == ControlScheme.Mixed) fastPacedFlightExact = true;
                CancelFastPacedAim();
            }
        }

        bool TryGetAutoAimTarget(Vector3 dir, out Vector3 target)
        {
            Vector3 origin = cameraTransform != null ? cameraTransform.position : transform.position;
            // In third person the camera sits behind the player - anything the ray crosses
            // BEFORE reaching the player's depth is behind/beside the cube, not aimable.
            float minDistance = Vector3.Distance(origin, transform.position) - 1f;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, autoAimMaxDistance, ~0, QueryTriggerInteraction.Collide);
            float bestDistance = float.MaxValue;
            target = default;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.distance < minDistance) continue;
                if (hit.collider == boxCollider || hit.collider.transform.IsChildOf(transform)) continue;
                // Triggers only count when they're genuine aim targets (PositioningObject) -
                // finish/restart volumes and the like stay invisible to the aim.
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

        // Finds the charge whose LANDING point is nearest the target ("the minimum energy
        // needed to get there"). Deliberately a coarse GRID SCAN plus a fine local scan, not a
        // ternary search: on this game's platform courses the objective has a NARROW valley -
        // an undershoot and an overshoot both fall into the void and score nearly identically
        // far - and a ternary search's probes usually both land on that plateau, converging
        // essentially anywhere (the routinely-overspending bug, direct report). A grid cannot
        // miss a valley wider than one cell; the fine pass then pins the minimum down to a few
        // percent.
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

        // Solver probes run on a short step budget (150 steps = 3 simulated seconds) - a
        // landing decides itself well within that at this game's speeds, and the full
        // 3000-step budget made each of the search's probes vastly more expensive than it
        // needed to be.
        const int AutoSolveStepLimit = 150;

        float LandingDistanceToPoint(Vector3 dir, float chargeFraction, Vector3 target)
        {
            float force = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
            float damping = Mathf.Lerp(fastPacedMinDamping, fastPacedMaxDamping, chargeFraction);
            // rb velocity is ~zero here (the Automatic aim freezes the cube), included anyway
            // so a grounded aim with residual motion still solves correctly.
            Vector3 landing = PredictLandingPoint(transform.position, rb.linearVelocity + dir * force / rb.mass, damping, out int _, out bool _, 0f, AutoSolveStepLimit);
            return (landing - target).sqrMagnitude;
        }

        // Hold South/LT/RT to charge a launch in that direction (same charge curve as the
        // charge-based schemes: minLaunchForce/maxLaunchForce interpolated by how long it's
        // held, over maxChargeTime), release to fire, Left Bumper cancels without firing. Shows
        // the same aim arrow + landing trail the charge-based schemes do while charging - "the
        // same sort of visual... that shows you your exact launch path" (direct request). All
        // three directions share the single energyFraction gate - same limit, for both
        // standalone StickAim and Mixed's airborne phase.
        void UpdateStickAimChargeScheme()
        {
            Vector2 stick = moveAction != null && moveAction.action != null
                ? moveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
            bool stickHeld = stick.sqrMagnitude > stickAimDeadzone * stickAimDeadzone;
            Vector3 stickDirection = stickHeld ? StickWorldDirection(stick) : Vector3.zero;

            // Always Mouse, midair (direct spec): W is the "angled" modifier. No extra code is
            // needed for the held case - W feeds the move action, and a pure-W stick push
            // resolves to the camera's flat facing (StickWorldDirection), which the mouse
            // rotates: Space/E/RT alone = straight up / straight down / straight forward, and
            // adding W = the tilted 80/60/30-degree variants toward wherever the mouse points.
            // The one Always-Mouse special case is the forward NEUTRAL heading - see the
            // Forward branch below.

            bool cancelPressed = cancelChargeAction != null && cancelChargeAction.action != null && cancelChargeAction.action.WasPressedThisFrame();

            if (stickAimChargeType != StickAimChargeType.None)
            {
                // Mixed-air only: letting go of Left Trigger stops aiming, cancelling whatever
                // charge is in progress WITHOUT firing (direct request) - grounded charges
                // (South's up-launch, standalone StickAim) never set mixedAirAiming, so this
                // can't touch them.
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

                // Mixed: pressing a DIFFERENT direction button mid-charge switches the charge
                // to that direction (direct request) - the accumulated charge carries over, and
                // firing then happens on the NEW button's release. Checked before releasedNow
                // below so the release test always tracks whichever direction is now active.
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

                // EnergyEconomy1 (westAirDownLaunch is that scene's marker): the straight-up
                // and ground-pound charges fill in REAL time - "the energy meter should not be
                // bound to the gamespeed" while charging them (direct request). Every other
                // charge keeps breathing with the bullet-time as before.
                if (westAirDownLaunch && (stickAimChargeType == StickAimChargeType.Up || stickAimChargeType == StickAimChargeType.Down))
                {
                    chargeTime = Mathf.Min(
                        chargeTime + Time.unscaledDeltaTime * chargeAccumulationRate * upDownChargeSpeedMultiplier,
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
                    // Forward keeps launching the last direction the stick actually pointed (if
                    // any), but drops to the shallow neutral angle the instant the stick isn't
                    // held past stickAimDeadzone anymore - direct request: "the direction should
                    // be frozen when launching forward but the lower angle should be used".
                    // Under Always Mouse, "straight forward" follows the camera's (mouse's)
                    // current flat heading rather than the cube's facing.
                    Vector3 flat = stickAimHasAimed
                        ? stickAimLastFlatDirection
                        : (mouseAirControls && groundedAimWithMouse ? CameraForwardFlat() : FacingFlatDirection());
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
                hasValidPredictedLanding = didLand;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand, lastPredictedLandingNormal);
                }

                if (releasedNow)
                {
                    QueueLaunch(dir, previewForce, previewDamping);
                    // The West air-down launch's flat 1.2x refund keys off this (economy
                    // scenes) - checked AFTER QueueLaunch, which resets it.
                    if (stickAimChargeType == StickAimChargeType.Down && !isGrounded) lastLaunchWasAirDown = true;
                    // Only a FORWARD launch swings the camera back behind the player - an
                    // up/down launch leaves it exactly where you left it (direct request).
                    // Those fire dead vertical now anyway, so there is no new horizontal
                    // heading to swing to; recentering just yanked the view for nothing.
                    if (stickAimChargeType == StickAimChargeType.Forward) RecenterCameraForStickAimLaunch(dir);

                    stickAimChargeType = StickAimChargeType.None;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);

                    // Mixed-air fire: close the aim and demand a fresh LT press for the next
                    // one - see UpdateMixedAirScheme's own comment.
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
                    // ALWAYS straight up (direct request - the stick-tilted 80-degree variant
                    // is retired; stickAimUpAngle stays as a field for the serialized scenes
                    // but is no longer read here).
                    return Vector3.up;
                case StickAimChargeType.Down:
                    // ALWAYS straight down, same request - the ground pound and every other
                    // down-launch fire dead vertical regardless of the stick.
                    return Vector3.down;
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

                float flightSpeed = Mathf.Lerp(minLaunchForce, maxDefyGravitySpeed, chargeFraction);
                float flightDuration = Mathf.Lerp(minDefyGravityDuration, maxDefyGravityDuration, chargeFraction);

                // rb.linearVelocity is held at zero for the whole charge (see FixedUpdate), so
                // the preview's initial velocity is just the flight speed itself.
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

        // FastPacedLevel's only scheme. Right Mouse (held) is a pure aim/camera toggle - swings
        // to first person and shows the reticle, but does NOT by itself freeze the player, since
        // (direct request) only releasing Right Mouse should return to 3rd person, which means a
        // single hold has to be able to cover several shots in a row. Left Mouse (held, only
        // while aiming) is the actual charge trigger, mirroring StickAim/DefyGravity's own
        // hold-to-charge/release-to-fire shape exactly, just fired along the camera's current
        // look direction instead of a stick-picked one - see fastPacedAiming/fastPacedCharging's
        // own field comments for why the freeze lives on the charge flag, not the aim flag.
        void UpdateFastPacedScheme()
        {
            // autoAimForced keeps the aim alive with no button held (PositioningObject
            // checkpoint); a genuine fresh press of the aim button hands control back to the
            // ordinary hold-to-maintain rule.
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
                // Same energy gate every other scheme uses to allow STARTING a new aim - see
                // canStartNewAim/canLaunch's own comments.
                if (energyFraction <= 0f) return;
                // Opening the aim ALWAYS needs a FRESH press now (direct request: "needing you
                // to repress it to aim again" after each launch - and the same gate already
                // fixed Tutorial2's grounded-launch carry-over hold). A held button never
                // opens the aim, in FastPacedLevel and the Tutorial2 hybrid alike; holding it
                // still MAINTAINS an open aim as before.
                if (!(fastPacedAimAction != null && fastPacedAimAction.action != null && fastPacedAimAction.action.WasPressedThisFrame()))
                {
                    return;
                }
                fastPacedAiming = true;
                cameraOrbit?.SetFirstPersonMode(true);
                // Energy modes: each fresh aim starts from a clean dial.
                if (energyControlMode != EnergyControlMode.Standard)
                {
                    chargeTime = 0f;
                    reverseChargingDown = false;
                    chargeDisplayInsufficient = false;
                }
            }

            bool lmbPressed = fastPacedLaunchAction != null && fastPacedLaunchAction.action != null && fastPacedLaunchAction.action.WasPressedThisFrame();
            bool lmbReleased = fastPacedLaunchAction != null && fastPacedLaunchAction.action != null && fastPacedLaunchAction.action.WasReleasedThisFrame();

            // Every non-Standard energy mode: the AIM PHASE is the energy phase (direct
            // request: "while aiming the energy input for your launches should start changing
            // and be able to be interacted with, so that the firing button is basically only
            // to confirm the launch").
            if (energyControlMode != EnergyControlMode.Standard)
            {
                UpdateEnergyModeAim(lmbPressed);
                return;
            }

            if (!fastPacedCharging)
            {
                // A fresh press only - holding Left Mouse through a fire does not auto-restart a
                // new charge, matching StickAim/DefyGravity's own WasPressedThisFrame gate.
                if (!lmbPressed || energyFraction <= 0f || !CanStartNewLaunch()) return;

                fastPacedCharging = true;
                chargeTime = 0f;
                // Instant stop, same reasoning as every other scheme's charge-start - FixedUpdate
                // keeps re-applying this for the whole charge (see fastPacedCharging's own use
                // there), so the shot always fires from a dead stop with no drift to account for.
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                landingPreview?.SetVisible(true);
                landingPreview?.SetMode(PredictionMode.TrailAndCrosshair);
            }

            AccumulateCharge();

            Vector3 dir = cameraOrbit != null ? cameraOrbit.AimForward : transform.forward;
            float chargeFraction = ChargeFraction();
            // Narrows the camera's field of view as charge builds - direct request: "the longer
            // you charge the more you need to zoom in on the landing spot".
            cameraOrbit?.SetAimZoom(chargeFraction);

            float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
            float previewDamping = Mathf.Lerp(fastPacedMinDamping, fastPacedMaxDamping, chargeFraction);

            // rb.linearVelocity is held at zero for the whole charge (see FixedUpdate), so no
            // drift term to add in - same reasoning as StickAim/DefyGravity's own preview.
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
                // Hybrid flights follow the predicted line exactly - see fastPacedFlightExact.
                if (mixedFastPacedAir && controlScheme == ControlScheme.Mixed) fastPacedFlightExact = true;

                // "Disable aim the moment you launch" (direct request, superseding the old
                // aim-spans-several-shots behavior): the whole aim - first person, zoom,
                // preview - closes with the shot, and re-aiming needs a genuinely fresh press
                // of the aim button (see the entry gate above).
                CancelFastPacedAim();
            }
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
            launchesSinceGrounded++;
            fastPacedFlightExact = false; // re-armed by the hybrid fire path right after this call
            aimButtonSpent = true;        // a held aim button does nothing further until released
            positioningAimUsedThisFlight = false; // the new flight gets its checkpoint again
            lastLaunchWasGrounded = isGrounded;   // ground vs midair origin, for the refund economy
            lastLaunchWasAirDown = false;         // re-set by the West down-launch's own fire path
            currentFlightIsDownward = Vector3.Dot(direction.normalized, Vector3.down) >= slamDownwardThreshold;
            // Every launch spends the charge fraction it took to build, straight out of the
            // shared energy tank - "no more time/energy/speed can be added... when you reach the
            // limit" (direct request) is what AccumulateCharge already enforces on the way up;
            // this is the other half, actually deducting it once the charge is spent for real.
            // Remembered as the amount ACTUALLY deducted (the Min with what's available), not the
            // raw ChargeFraction cost - feeds the FastPaced crash refund, see
            // fastPacedRefundMultiplier's own comment.
            lastLaunchEnergySpent = Mathf.Min(SpendableEnergy(), ChargeFraction() * energyCostPerFullCharge);
            energyFraction = Mathf.Clamp01(energyFraction - lastLaunchEnergySpent);
            // No failsafe at launch time, in either state (direct request: the 5% failsafe
            // applies only ONCE YOU LAND) - a launch may leave the tank as empty as the
            // player committed it to be.
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
        // stepLimit > 0 caps the simulation budget (the Automatic solver's probes use a short
        // budget - a landing decides itself within a few seconds of simulated flight); 0 means
        // the full maxPredictionSteps as always.
        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, float damping, out int stepCount, out bool didLand, float gravityFreeDuration = 0f, int stepLimit = 0)
        {
            EnsurePredictionClone();
            // Keeps the isolated physics scene's geometry matching the live scene before this
            // frame's simulation - active-state flips (launch buttons), moves, resizes,
            // rotations all land here. Once per FRAME (see the perf caches) - every prediction
            // in the same frame sees identical geometry anyway.
            if (predictionSyncFrame != Time.frameCount)
            {
                SyncPredictionGeometry();
                predictionSyncFrame = Time.frameCount;
            }

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
            // Offset direction matters as much as the margin itself: world-up is only "away from
            // the surface I'm resting on" while standing on a floor. Stuck to a wall or hanging
            // from a platform's underside (FastPacedLevel's tilted spiral platforms especially -
            // direct bug report: "the visual doesn't appear if you are hanging onto a platform at
            // certain degrees, mostly 90 degrees or up"), up is parallel to - or straight INTO -
            // the stuck surface, embedding the clone in it, which registers an instant false
            // "landed" at the player's own position and collapses the trail/reticle to nothing.
            // The stuck surface's normal always points away from whatever the cube is stuck to,
            // so it's the correct clearance direction in every orientation; world-up remains the
            // un-stuck fallback (grounded standing, mid-air) where it was already correct.
            // Same start point within one frame -> the depenetrated spawn is identical, and
            // recomputing it per prediction was a real cost with the Automatic solver's many
            // predictions per frame.
            bool spawnCached = spawnCacheFrame == Time.frameCount && spawnCacheStart == startPos;
            Vector3 clearanceDir = isStuck && stuckSurfaceNormal.sqrMagnitude > 0.0001f ? stuckSurfaceNormal : Vector3.up;
            Vector3 spawnPos = spawnCached ? spawnCacheResult : startPos + clearanceDir * 0.15f;

            // Depenetrate the spawn point from the static geometry: aiming while pressed up
            // against a wall - e.g. falling down its face right after a non-sticky cling
            // released, TestLevel1's core loop - would otherwise start the clone OVERLAPPING
            // that wall, register an instant false "landing", and collapse the dotted line to
            // nothing (direct bug report). ComputePenetration is purely geometric, so it works
            // across the physics-scene boundary; the proxies are almost all BoxColliders, which
            // it fully supports (a non-convex MeshCollider just returns false and is skipped).
            if (!spawnCached && predictionCloneCollider != null)
            {
                // Inflated by a skin for the pass: merely TOUCHING a wall (the ~1cm of gap left
                // while sliding down a face after a cling released) is not penetration, so the
                // un-inflated check let the clone spawn inside PhysX's contact offset - the
                // stopper killed its velocity on the first step and the "trail" was just the
                // clone falling straight down (direct bug report: "it rather points down...
                // the moment you don't touch the wall anymore it appears correctly"). With the
                // skin, touching counts as overlapping and gets pushed out to a genuinely
                // contact-free gap.
                const float depenetrationSkin = 0.12f;
                Vector3 originalCloneSize = predictionCloneCollider.size;
                predictionCloneCollider.size = originalCloneSize + Vector3.one * depenetrationSkin;
                foreach (PredictionGeometryProxy entry in geometryProxies)
                {
                    if (entry.proxy == null || !entry.proxy.activeSelf) continue;
                    Collider proxyCollider = entry.proxyBox != null ? (Collider)entry.proxyBox : entry.proxyMesh;
                    if (proxyCollider == null) continue;
                    // Broad-phase: anything whose bounds sit clearly away from the spawn can't
                    // be overlapping it.
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

            predictionCloneCollider = predictionClone.AddComponent<BoxCollider>();
            if (boxCollider != null) predictionCloneCollider.size = boxCollider.size;
            // No Physics.IgnoreCollision needed anymore - a separate PhysicsScene means the
            // clone cannot physically collide with the real player's collider at all.

            // Any-surface sticking (direct request) - PredictionCloneStopper just stops on the
            // first contact, period, matching the real cube's OnCollisionEnter.
            predictionStopper = predictionClone.AddComponent<PredictionCloneStopper>();

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
        // lifetime - but no longer a frozen snapshot: each proxy stays PAIRED with its source
        // collider (geometryProxies) and SyncPredictionGeometry re-mirrors transform, collider
        // dimensions, and active state on every prediction frame, so the dotted line stays
        // accurate when a launch button flips a platform on/off, or anything gets moved,
        // resized, or rotated at runtime (direct request). INACTIVE objects are included in
        // this initial scan on purpose - a button-revealed platform needs its proxy already
        // waiting (disabled) before it first turns on. Only colliders created brand-new at
        // runtime after this scan remain invisible to the prediction.
        void BuildPredictionGeometryProxies()
        {
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Collider col in colliders)
            {
                if (col == boxCollider) continue;
                if (col.GetComponent<Rigidbody>() != null) continue;
                // Trigger volumes (e.g. Level1's finish line) aren't solid ground - including one
                // here would make the prediction clone incorrectly "land" on thin air.
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
        }
        readonly List<PredictionGeometryProxy> geometryProxies = new List<PredictionGeometryProxy>();

        // Re-mirrors every proxy from its live source - called once per prediction (i.e. every
        // frame while aiming). ~10-30 proxies of trivially cheap copies; only SetActive is
        // guarded, since toggling an object is the one genuinely non-free operation here.
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

            bool sourceSolid = entry.source.enabled && entry.source.gameObject.activeInHierarchy;
            if (entry.proxy.activeSelf != sourceSolid) entry.proxy.SetActive(sourceSolid);
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
            return energyCostPerFullCharge > 0f ? (SpendableEnergy() / energyCostPerFullCharge) * maxChargeTime : maxChargeTime;
        }

        // What may actually be spent. MIDAIR: the whole tank - no reserve at all (direct
        // request), so a save-throw launch can dump everything. GROUNDED: the reserve holds,
        // so you can never strand yourself while standing still.
        float SpendableEnergy()
        {
            return isGrounded ? Mathf.Max(energyFraction - minEnergyReserve, 0f) : energyFraction;
        }

        // The failsafe: ON LANDING ONLY (direct request) - land with 5% or less and the tank
        // is topped back up to 5%, so you can never end a flight stranded. Called from the
        // crash path and from the grounded check in FixedUpdate (which covers landings that
        // never register as a crash, e.g. simply walking back onto solid ground).
        void ClampEnergyFloor()
        {
            if (energyFraction < minEnergyReserve) energyFraction = minEnergyReserve;
        }

        // "You gain energy depending on the speed you used to crash onto it, it should be more
        // than what you put in it, with the faster your speed at crash that factor at which you
        // gain more energy should also increase" (direct request) - the energyGainSpeedBonus term
        // is what makes the RATE increase with speed too, not just the raw amount: a crash at
        // twice the speed doesn't just gain twice the base energy, it gains MORE than twice, since
        // the multiplier itself grows with speed.
        void GainEnergyFromCrash(float crashSpeed)
        {
            // EnergyEconomy1 (direct request): the refund derives ONLY from the last launch's
            // own spend, by its type - no speed term, no minimum floor:
            //   ground launch          -> exactly what it cost (a wash);
            //   midair launch          -> spend * (1.01 + 0.01 * X), X = full tank / spend, so
            //                             smaller launches earn a bigger multiplier (X capped
            //                             at 100 so a near-zero tap can't mint energy);
            //   West air-down launch   -> flat 1.2x.
            if (lastLaunchRefundEconomy)
            {
                float economyGain;
                if (lastLaunchWasAirDown)
                {
                    economyGain = Mathf.Max(lastLaunchEnergySpent * groundPoundRefundMultiplier, groundPoundMinRefund);
                }
                else if (lastLaunchWasGrounded)
                {
                    economyGain = lastLaunchEnergySpent;
                }
                else
                {
                    // X = energy used / max energy (the tank is the 0-1 fraction itself, so X
                    // IS the spend), refund = spend * (X * factor + 1) - see the field.
                    float x = lastLaunchEnergySpent;
                    economyGain = lastLaunchEnergySpent * (x * midairRefundSpendFactor + 1f);
                }
                energyFraction = Mathf.Clamp01(energyFraction + economyGain);
                ClampEnergyFloor();
                return;
            }

            // FastPaced replaces the speed-based formula outright: refund exactly what the launch
            // spent, times fastPacedRefundMultiplier (direct request - see the field's own
            // comment). The minEnergyGainPerCrash floor (itself a direct request from earlier)
            // still applies to both paths - without it, a tap-charge FastPaced shot could refund
            // near zero and soft-lock a nearly-empty tank mid-air.
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
