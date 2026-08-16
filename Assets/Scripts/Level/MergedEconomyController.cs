using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum MergedEconomyVariant
    {
        VariantA, // revocable chain extra: the latest bonus stays provisional orange; a lapse revokes it
        VariantB, // banked chain extra: every bonus is normal energy instantly; a lapse only resets the multiplier
        VariantC, // dual-launch refunds (revocable): the landing pays from BOTH the first launch and the midair relaunches
        VariantD, // dual-launch refunds (banked)
        VariantE, // total loss: missing the combo window costs ALL energy; the recharge rebuilds to its own threshold
    }

    // QuarryEconomy2's single MERGED economy (a scene object, never a prefab) - the combo
    // refunds and the recharge-over-time joined into one design:
    //
    //   1. COMBO landings: every landed launch refunds the base fraction; relaunching
    //      inside the window chains, and each chained landing pays a growing EXTRA on top
    //      (orange, revocable until the chain banks it) up to the multiplier cap. The big
    //      floor and the object you launched from never count, exactly like variant B.
    //   2. SAFETY RECHARGE: dropping below the trigger fraction (10%) latches a grounded
    //      recharge that refills up to its ceiling (30%), then switches off until you dip
    //      below the trigger again. Regen-gained energy shows in orange while it lasts.
    //   3. PREMIUM TOP: ordinary refunds stop at the premium floor (80%, enforced by the
    //      controller's ordinaryRefundCeiling). Only the combo EXTRAS (paid directly by
    //      this harness) and the ground-pound boost pipeline fill the last 20%.
    public class MergedEconomyController : MonoBehaviour
    {
        [Tooltip("A: revocable chain extra (at a FULL tank the boosted part caps at the top 20%). B: extras bank instantly. C/D: dual-launch refunds, revocable/banked. E: a missed window costs ALL energy. Cycle with V / D-pad Right and C / D-pad Left. AUTO MAX energy is a separate toggle: X / D-pad Down, works in every variant.")]
        public MergedEconomyVariant currentVariant = MergedEconomyVariant.VariantA;

        [Header("Auto max energy (X / D-pad Down)")]
        [Tooltip("When on, every launch fires with the MAXIMUM the tank can pay - no manual regulation. Orthogonal to the variants: toggled in game with X or D-pad Down.")]
        public bool autoMaxEnergy = false;

        bool BankedMode => currentVariant == MergedEconomyVariant.VariantB
            || currentVariant == MergedEconomyVariant.VariantD
            || currentVariant == MergedEconomyVariant.VariantE;
        bool DualRefundMode => currentVariant == MergedEconomyVariant.VariantC
            || currentVariant == MergedEconomyVariant.VariantD;
        bool TotalLossMode => currentVariant == MergedEconomyVariant.VariantE;

        [Header("A/B - Combo refunds")]
        [Tooltip("Refund fraction of the spent energy for an unchained landing, any aim method.")]
        public float comboBaseRefund = 0.7f;
        [Tooltip("Extra refund fraction added per chained landing (x1 = +this, x2 = +2x this...).")]
        public float comboStepPerLevel = 0.1f;
        [Tooltip("Seconds after a landing to fire the next launch before the combo (and its orange extra) is lost.")]
        public float comboWindowSeconds = 2f;
        [Tooltip("The chain multiplier can never exceed this refund fraction (2 = 200%).")]
        public float comboMaxMultiplier = 2f;
        [Tooltip("How far down the combo meter moves so its circle clears the energy meter.")]
        public float comboMeterDropPixels = 44f;
        public Color comboMeterColor = new Color(1f, 0.62f, 0.1f);
        [Header("C/D - Dual-launch refunds")]
        [Tooltip("C/D: refund multiplier on the FLIGHT-OPENING launch's spend, at combo level 0.")]
        public float firstLaunchBaseRefund = 0.6f;
        [Tooltip("C/D: added to the first-launch multiplier per chained landing.")]
        public float firstLaunchStepPerLevel = 0.1f;
        [Tooltip("C/D: refund multiplier on the MIDAIR relaunch spend (all relaunches of the flight together), at combo level 0.")]
        public float midairLaunchBaseRefund = 0.85f;
        [Tooltip("C/D: added to the midair-relaunch multiplier per chained landing.")]
        public float midairLaunchStepPerLevel = 0.15f;

        [Header("C/D - Safety recharge")]
        [Tooltip("C/D's OWN safety recharge trigger - dropping below this switches the grounded recharge on in the dual variants.")]
        [Range(0f, 1f)] public float dualSafetyTriggerFraction = 0.1f;
        [Tooltip("C/D's OWN safety recharge ceiling - the recharge fills to here, then switches off until the next dip below the trigger.")]
        [Range(0f, 1f)] public float dualSafetyCeilingFraction = 0.4f;

        [Header("E - Total loss on a missed window")]
        [Tooltip("E: its OWN base refund for an unchained landing (A/B use Combo Base Refund).")]
        public float totalLossBaseRefund = 0.7f;
        [Tooltip("E: its OWN extra refund fraction added per chained landing.")]
        public float totalLossStepPerLevel = 0.1f;
        [Tooltip("E: its OWN recharge trigger - after the total loss the tank sits at 0, well below this, so the recharge starts immediately.")]
        [Range(0f, 1f)] public float totalLossSafetyTriggerFraction = 0.1f;
        [Tooltip("E: its OWN recharge ceiling - the rebuild after a missed window stops here.")]
        [Range(0f, 1f)] public float totalLossSafetyCeilingFraction = 0.4f;

        [Header("Intro")]
        [Tooltip("First-boot explainer (also opened by the pause menu's BuildInfo button). Edit freely.")]
        [TextArea(10, 30)]
        public string introText =
            "MERGED ECONOMY VARIANTS\n\n" +
            "Combo refunds + an under-threshold recharge + a premium top: the first 80% of the tank is\n" +
            "normal energy, the last 20% (the two big meter blocks) only fills from combo extras or the\n" +
            "pound boost - and dies when your chain stops.\n\n" +
            "Switch variants with V / D-pad Right (back: C / D-pad Left):\n" +
            "A - Revocable extra: the latest chain bonus stays orange, lost if the window lapses.\n" +
            "B - Banked extra: every chain bonus becomes normal energy instantly.\n" +
            "C - Dual refunds: landings pay from BOTH the flight's first launch and its midair\n" +
            "     relaunches, each with its own rate; the bonus part stays revocable orange.\n" +
            "D - Dual refunds (banked): the same, with bonuses banking instantly.\n" +
            "E - Total loss: miss a combo window and ALL energy is gone - the ground recharge\n" +
            "     then rebuilds you to its threshold.\n\n" +
            "X / D-pad Down toggles AUTO MAX ENERGY in ANY variant: every launch fires with the\n" +
            "maximum the tank can pay - no manual regulation.\n\n" +
            "Standing still below the recharge threshold slowly refills the tank (fresh energy shows\n" +
            "orange, then turns yellow as it banks). If you are completely empty you cannot launch.\n\n" +
            "Press any button to start.";

        [Header("A/B - Safety recharge (rate + fade shared by all variants)")]
        [Tooltip("Dropping below this fraction switches the grounded recharge ON.")]
        [Range(0f, 1f)] public float safetyTriggerFraction = 0.1f;
        [Tooltip("The recharge fills up to here, then switches OFF until the next dip below the trigger.")]
        [Range(0f, 1f)] public float safetyCeilingFraction = 0.3f;
        [Tooltip("Tank fraction regained per REAL second while standing on the ground with the recharge latched on.")]
        public float regenPerSecond = 0.08f;
        [Tooltip("Seconds for freshly-regenerated energy to convert from ORANGE into normal yellow - a steady recharge shows a small orange tip at the fill edge that keeps turning yellow behind it.")]
        public float regenOrangeFadeSeconds = 0.35f;

        [Header("Premium top (all variants)")]
        // The normal/boost split is POSITIONAL: everything below the boundary is normal
        // energy whatever filled it, everything above is boosted - only combo extras and
        // the pound boost can fill it, and it dies when the chain stops. MUST match the
        // scene's meter prefab (0.8 = the 8+2 meter, 0.4 = Level1Economy's 4+6 meter).
        [Tooltip("The normal/boost boundary. 0.8 pairs with the 8+2 meter, 0.4 with Level1Economy's 4+6 meter.")]
        [Range(0f, 1f)] public float premiumBoundaryFraction = 0.8f;
        float PremiumBoundary => Mathf.Clamp01(premiumBoundaryFraction);

        [Header("Scene lockdown (Level1Economy)")]
        [Tooltip("Freezes the setup: no variant cycling (V/C), no auto-max toggle (X/D-pad Down) - the scene tests exactly what the inspector says.")]
        public bool lockSettings = false;
        [Tooltip("Momentum launches (midair launches carry the velocity you aimed with) forced from Start - Level1Economy locks this ON.")]
        public bool momentumLaunches = false;
        [Tooltip("E: a missed combo window clamps energy TO this fraction (0 = lose everything; Level1Economy uses 0.4).")]
        [Range(0f, 1f)] public float totalLossKeepFraction = 0f;

        KineticCubeController controller;
        KineticEnergy.UI.PauseController pauseController;

        // Combo state - the same machinery as EconomyVariantController's variant B, minus
        // the multiplier inflation: the base refund flows through the (ceiling-capped)
        // controller pipeline, the chain EXTRA is paid here directly so it may pass the cap.
        int comboCount;
        float comboExtra;        // the latest landing's extra (revocable variants) - orange; capped at 20% only at a FULL tank
        float windowRemaining;
        bool chainInFlight;
        Transform launchSurface;

        // C/D/E flight bookkeeping: the opening launch's spend and the summed midair
        // relaunch spends of the CURRENT flight - the landing pays from both.
        bool flightOpen;
        float flightFirstSpend;
        float flightMidairSpend;
        float flightStartEnergy;   // the tank BEFORE the first launch - the payout base's hard cap
        float groundedSettleTimer; // grounded time with an open ledger - closes it unpaid

        [Tooltip("A flight resting on the ground this long WITHOUT a registered crash closes its ledger unpaid - shallow swallowed landings can then never defer their spends onto a later (wall) crash, so walls pay exactly like platforms.")]
        public float flightSettleSeconds = 0.25f;

        // Safety recharge state.
        bool safetyActive;
        float regenPool;         // the FRESH regen slice, drawn orange - decays into yellow


        // Runtime UI (combo circle riding the repurposed slowdown meter, HUD tag).
        GameObject comboCircle;
        Text comboText;
        Text hudLabel;
        Color defaultSlowFillColor = Color.cyan;
        Vector2 defaultSlowMeterPosition;
        RectTransform slowMeterRoot;

        // E carries its own combo base/step; A/B share the base pair (C/D have their
        // dual-launch pairs and never read these).
        float ActiveComboBase => TotalLossMode ? totalLossBaseRefund : comboBaseRefund;
        float ActiveComboStep => TotalLossMode ? totalLossStepPerLevel : comboStepPerLevel;
        float NextMultiplier => Mathf.Min(ActiveComboBase + ActiveComboStep * comboCount, comboMaxMultiplier);

        void Start()
        {
            controller = FindAnyObjectByType<KineticCubeController>();
            if (controller == null)
            {
                Debug.LogError("MergedEconomyController: no KineticCubeController in the scene.");
                enabled = false;
                return;
            }
            pauseController = FindAnyObjectByType<KineticEnergy.UI.PauseController>(FindObjectsInactive.Include);

            // The one-time economy wiring (per-variant refund routing lives in
            // ApplyVariant); slow-down stays free.
            controller.slowdownMode = SlowdownMode.Unlimited;
            controller.ordinaryRefundCeiling = PremiumBoundary;
            // The safety recharge IS this scene's stranding failsafe - the grounded
            // reserve (unspendable bottom slice) is disabled, the whole tank fires.
            controller.minEnergyReserve = 0f;

            controller.LaunchFired += OnLaunchFired;
            controller.CrashRegistered += OnCrash;
            DamageWalls.PlayerRespawned += OnPlayerRespawned;

            controller.addPreAimVelocityToLaunch = momentumLaunches;

            BuildHudTag();
            SetupComboMeter();
            BuildPremiumZone();
            ApplyVariant();

            // The first-boot explainer (and the pause menu's BuildInfo target) - the text
            // lives on this harness so it is editable alongside the variant tuning.
            GameObject introGo = new GameObject("MergedEconomyIntro");
            var intro = introGo.AddComponent<KineticEnergy.UI.AimIntroScreen>();
            intro.introKey = "economy2";
            intro.bodyText = introText;
        }

        // ---------- The tall premium zone (this scene only) ----------

        // The 8+2 meter geometry lives in the PremiumEnergyMeter PREFAB (built by the
        // setup script and wired into this scene) - the harness only locates the premium
        // segment's fill images by name and drives them.
        Image premiumOrangeFill;
        Image premiumChargeFill;

        void BuildPremiumZone()
        {
            var meter = controller.energyMeter;
            if (meter == null) return;
            foreach (Image image in meter.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "PremiumBoostFill") premiumOrangeFill = image;
                else if (image.name == "PremiumChargeFill") premiumChargeFill = image;
            }
            if (premiumOrangeFill == null)
            {
                Debug.LogWarning("MergedEconomyController: the scene's meter has no premium segment - run Setup Quarry Economy 2 to wire the PremiumEnergyMeter prefab.");
            }
        }

        void ApplyVariant()
        {
            // Entering a banked variant solidifies any provisional extra into normal energy.
            if (BankedMode) comboExtra = 0f;
            controller.alwaysMaxCharge = autoMaxEnergy;

            // The wall-launch momentum floor: launches from a wall stick synthesize a
            // carry velocity worth at least the variant's recharge baseline (the
            // VELOCITY reading of the wall stake - see KineticCubeController).
            controller.wallLaunchMomentumFloorFraction = ActiveSafetyCeiling;

            // Refund routing: A/B pay the base through the ordinary (ceiling-capped)
            // pipeline; C/D and E silence the pipeline entirely - their whole payout is
            // computed by the harness on landing from the flight's spends.
            bool harnessPaysRefund = DualRefundMode || TotalLossMode;
            controller.groundedRefundMultiplier = harnessPaysRefund ? 0f : ActiveComboBase;
            controller.midairRefundBaseMultiplier = harnessPaysRefund ? 0f : ActiveComboBase;
            controller.midairRefundSpendFactor = 0f;

            RefreshHudLabel();
        }

        void OnDestroy()
        {
            DamageWalls.PlayerRespawned -= OnPlayerRespawned;
            if (controller == null) return;
            controller.LaunchFired -= OnLaunchFired;
            controller.CrashRegistered -= OnCrash;
            controller.ordinaryRefundCeiling = 1f;
            controller.alwaysMaxCharge = false;
        }

        void Update()
        {
            if (controller == null) return;

            // Keep running through the midair aim's bullet-time freeze (the combo window
            // runs on REAL seconds so aiming can't hide from it); a genuine pause or the
            // intro overlay still halts everything.
            if (Time.timeScale <= 0f)
            {
                bool trulyPaused = (pauseController != null && pauseController.IsPaused)
                    || KineticEnergy.UI.AimIntroScreen.InputBlocked;
                if (trulyPaused || !controller.IsAimingOrCharging) return;
            }

            // Variant cycling and the auto-max toggle, blocked while an aim is open -
            // and entirely disabled in locked scenes (Level1Economy).
            if (!lockSettings && !controller.IsAimingOrCharging)
            {
                bool forward = (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame);
                bool back = (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);
                if (forward || back)
                {
                    int count = System.Enum.GetValues(typeof(MergedEconomyVariant)).Length;
                    currentVariant = (MergedEconomyVariant)(((int)currentVariant + (forward ? 1 : count - 1)) % count);
                    ApplyVariant();
                }

                // AUTO MAX is orthogonal to the variants: X / D-pad Down flips between
                // manual regulation and every-launch-at-maximum, wherever you are.
                bool toggleAutoMax = (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.dpad.down.wasPressedThisFrame);
                if (toggleAutoMax)
                {
                    autoMaxEnergy = !autoMaxEnergy;
                    controller.alwaysMaxCharge = autoMaxEnergy;
                    RefreshHudLabel();
                }
            }

            // A chained flight that ended WITHOUT a landing crash (NonStick bounce, enemy
            // hit, soft touch-down) thaws once the player stands with no launch running.
            if (controller.IsGrounded && !controller.HasLaunched)
            {
                // However the flight ended, standing still means it is over: the chain
                // freeze thaws and the flight bookkeeping closes unpaid.
                chainInFlight = false;
                flightOpen = false;
            }

            // Shallow landings the crash pipeline SWALLOWS leave hasLaunched set, dodging
            // the thaw above - their ledgers used to stay open, deferring several
            // launches' spends onto the next wall crash (which then paid a huge backlog
            // where a platform paid nothing). Resting on the ground with an open ledger
            // for a settle moment closes it unpaid: every registered crash - wall or
            // platform alike - can only ever pay its OWN flight.
            if (flightOpen && controller.IsGrounded) groundedSettleTimer += Time.unscaledDeltaTime;
            else groundedSettleTimer = 0f;
            if (groundedSettleTimer >= Mathf.Max(flightSettleSeconds, 0.05f))
            {
                flightOpen = false;
                groundedSettleTimer = 0f;
            }

            if (windowRemaining > 0f && !chainInFlight)
            {
                windowRemaining -= Time.unscaledDeltaTime;
                if (windowRemaining <= 0f) ResetCombo(revoke: true);
            }

            // The boost cap bites ONLY at a FULL tank: reaching 100% converts any
            // provisional extra beyond the premium segment's 20% into normal energy.
            // Below full, the boost may grow past 20% freely.
            if (comboExtra > 1f - PremiumBoundary && controller.EnergyFraction >= 0.999f)
            {
                comboExtra = 1f - PremiumBoundary;
            }

            UpdateSafetyRecharge();
        }

        // C/D and E carry their own recharge trigger/ceiling; A/B share the base pair.
        float ActiveSafetyTrigger => TotalLossMode ? totalLossSafetyTriggerFraction
            : DualRefundMode ? dualSafetyTriggerFraction : safetyTriggerFraction;
        float ActiveSafetyCeiling => TotalLossMode ? totalLossSafetyCeilingFraction
            : DualRefundMode ? dualSafetyCeilingFraction : safetyCeilingFraction;

        void UpdateSafetyRecharge()
        {
            float energyNow = controller.EnergyFraction;
            if (!safetyActive && energyNow < ActiveSafetyTrigger) safetyActive = true;

            if (safetyActive)
            {
                // "Standing on a platform" is EITHER state the player rests in: the
                // ordinary grounded check, or the crash-stick (floating walls and other
                // surfaces the ground probe misses register as stuck, not grounded - the
                // recharge must run on every platform type alike). It also WAITS for the
                // combo meter to go idle: while a chain window is live (or a chained
                // launch is in the air), the chain is the income - no double-dipping.
                bool comboIdle = windowRemaining <= 0f && !chainInFlight;
                bool restingOnSurface = controller.IsGrounded || controller.IsStuck;
                if (comboIdle && restingOnSurface && energyNow < ActiveSafetyCeiling)
                {
                    float headroom = ActiveSafetyCeiling - energyNow;
                    float gained = Mathf.Min(regenPerSecond * Time.unscaledDeltaTime, headroom);
                    controller.AddEnergy(gained);
                    regenPool += gained;
                }
                if (controller.EnergyFraction >= ActiveSafetyCeiling - 0.0001f) safetyActive = false;
            }

            // Fresh regen is orange only BRIEFLY, then converts to yellow: the pool
            // decays exponentially, so a steady recharge carries a small constant orange
            // tip at the fill edge that keeps turning yellow behind it, and once the
            // recharge stops the last of the orange fades out completely.
            if (regenPool > 0f)
            {
                float fade = Mathf.Min(Time.unscaledDeltaTime / Mathf.Max(regenOrangeFadeSeconds, 0.01f), 1f);
                regenPool -= regenPool * fade;
                if (regenPool < 0.0005f) regenPool = 0f;
            }
            regenPool = Mathf.Min(regenPool, controller.EnergyFraction);
        }

        // ---------- Combo machinery ----------

        void OnLaunchFired()
        {
            // A launch ends the recharge moment outright - any not-yet-converted orange
            // tip clears with it.
            regenPool = 0f;

            // C/D flight bookkeeping: the first launch OPENS the flight, every further
            // launch before the landing is a midair relaunch - both spends pay out at
            // the landing with their own multipliers.
            if (!flightOpen)
            {
                flightOpen = true;
                flightFirstSpend = controller.LastLaunchEnergySpent;
                flightMidairSpend = 0f;
                // The tank as it stood BEFORE this launch's spend came out - mid-flight
                // income (pound boost, refunds re-spent on relaunches) can push the
                // summed spends past it, and the payout must never reward more than the
                // energy the flight actually started with (direct diagnosis).
                flightStartEnergy = Mathf.Min(controller.EnergyFraction + controller.LastLaunchEnergySpent, 1f);

                // A flight opened while NOT grounded (stuck on a wall or another object,
                // or genuinely midair) treats the recharge baseline as its minimum stake:
                // the payout cap is floored at the variant's regen ceiling, while a
                // grounded launch with a fuller tank keeps its real (higher) value.
                if (!controller.IsGrounded)
                {
                    flightStartEnergy = Mathf.Max(flightStartEnergy, ActiveSafetyCeiling);
                }

                // The no-self-hop rule keys on the FLIGHT'S takeoff object, captured only
                // when the flight opens FROM a surface. Midair relaunches must never
                // overwrite it: a relaunch fired while diving low over the destination
                // platform used to stamp THAT platform as the "takeoff", falsely voiding
                // the whole landing's refund (the missing grounded+midair dual payout).
                launchSurface = null;
                if ((controller.IsGrounded || controller.IsStuck)
                    && Physics.Raycast(controller.transform.position, Vector3.down, out RaycastHit hit, 4f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    launchSurface = hit.collider.transform;
                }
            }
            else
            {
                flightMidairSpend += controller.LastLaunchEnergySpent;
            }

            if (windowRemaining > 0f) chainInFlight = true;
        }

        void OnCrash(Vector3 position)
        {
            // The one landing that never COUNTS: back on the very object this launch
            // took off from (no self-hops - and the big floor is just another object
            // under this rule now, not a blanket exclusion). The refund just granted is
            // taken back and the chain neither grows nor refreshes.
            Collider crashSurface = controller.LastCrashSurface;
            if (crashSurface != null && launchSurface != null && crashSurface.transform == launchSurface)
            {
                if (controller.LastCrashRefund > 0f) controller.AddEnergy(-controller.LastCrashRefund);
                chainInFlight = false;
                flightOpen = false;
                return;
            }

            // C/D and E are the SOLE payers of landing refunds. The zeroed multipliers
            // silence the ordinary pipeline, but the POUND WASH pays through its own
            // branch regardless - so whatever the pipeline actually paid for this crash
            // is taken back first, or pound-ending flights double-paid (wash + flight
            // sum) on every surface.
            if ((DualRefundMode || TotalLossMode) && controller.LastCrashRefund > 0f)
            {
                controller.AddEnergy(-controller.LastCrashRefund);
            }

            // C/D: the landing's WHOLE payout is computed here, from both of the
            // flight's launch types - only for a genuinely OPEN flight ledger (a crash
            // arriving after the ledger settled closed pays nothing). The base parts
            // respect the 80% ordinary ceiling exactly like the pipeline would; the
            // combo-driven parts on top are boost and follow the variant's extra rules.
            if (DualRefundMode && flightOpen)
            {
                float m1 = Mathf.Min(firstLaunchBaseRefund + firstLaunchStepPerLevel * comboCount, comboMaxMultiplier);
                float m2 = Mathf.Min(midairLaunchBaseRefund + midairLaunchStepPerLevel * comboCount, comboMaxMultiplier);

                // The payout base caps at the tank the flight STARTED with - spends
                // funded by mid-flight income scale both parts down proportionally.
                float rawTotal = flightFirstSpend + flightMidairSpend;
                float spendScale = rawTotal > flightStartEnergy && rawTotal > 0.0001f ? flightStartEnergy / rawTotal : 1f;
                float firstSpend = flightFirstSpend * spendScale;
                float midairSpend = flightMidairSpend * spendScale;

                float baseGain = firstSpend * firstLaunchBaseRefund + midairSpend * midairLaunchBaseRefund;
                float baseHeadroom = Mathf.Max(PremiumBoundary - controller.EnergyFraction, 0f);
                controller.AddEnergy(Mathf.Min(baseGain, baseHeadroom));

                float extraGain = firstSpend * (m1 - firstLaunchBaseRefund)
                    + midairSpend * (m2 - midairLaunchBaseRefund);
                if (extraGain > 0f)
                {
                    controller.AddEnergy(extraGain);
                    comboExtra = BankedMode ? 0f : Mathf.Min(extraGain, controller.EnergyFraction);
                }
            }
            else if (TotalLossMode && flightOpen)
            {
                // E: the WHOLE flight pays as one sum - (first launch + midair
                // relaunches) times E's combo multiplier (direct request). Base part
                // ceiling-capped exactly like the pipeline; the combo-driven part on
                // top is boost and may pass the ceiling.
                float m = NextMultiplier;
                // Capped at the tank the flight started with, same rule as C/D.
                float totalSpend = Mathf.Min(flightFirstSpend + flightMidairSpend, flightStartEnergy);
                float baseGain = totalSpend * totalLossBaseRefund;
                float baseHeadroom = Mathf.Max(PremiumBoundary - controller.EnergyFraction, 0f);
                controller.AddEnergy(Mathf.Min(baseGain, baseHeadroom));
                float extraGain = totalSpend * (m - totalLossBaseRefund);
                if (extraGain > 0f) controller.AddEnergy(extraGain);
            }

            // The chain EXTRA for this landing, from the PRE-landing chain level (the base
            // refund was already paid by the ordinary pipeline). Paid directly, so it can
            // pass the premium boundary - this and the pound boost are the only ways up
            // there. Under the positional 80/20 rule, whatever lands below 80% is normal
            // energy immediately; only the part sitting above 80% is at risk.
            // A/B: the chain EXTRA on top of the pipeline-paid base refund.
            if (!DualRefundMode && !TotalLossMode)
            {
                float extraRate = NextMultiplier - ActiveComboBase;
                if (extraRate > 0f && comboCount > 0)
                {
                    float extra = controller.LastLaunchEnergySpent * extraRate;
                    controller.AddEnergy(extra);
                    // Revocable variants: the LATEST extra stays provisional orange
                    // (replace, not accumulate), free to exceed 20% below a full tank -
                    // the cap bites only at 100% (see the full-tank conversion in
                    // Update). Banked: normal energy the instant it's paid.
                    comboExtra = BankedMode ? 0f : Mathf.Min(extra, controller.EnergyFraction);
                }
            }

            flightOpen = false;

            comboCount++;
            if (comboStepPerLevel > 0f)
            {
                int maxLevels = Mathf.Max(Mathf.FloorToInt((comboMaxMultiplier - comboBaseRefund) / comboStepPerLevel + 0.0001f), 0);
                comboCount = Mathf.Min(comboCount, maxLevels);
            }
            chainInFlight = false;
            windowRemaining = comboWindowSeconds;
        }

        void ResetCombo(bool revoke)
        {
            if (revoke && controller != null)
            {
                if (TotalLossMode)
                {
                    // E: a missed window costs everything down to the KEEP fraction -
                    // 0 by default (total loss), 0.4 in Level1Economy ("revert to 40%").
                    controller.ClampEnergyTo(totalLossKeepFraction);
                }
                else
                {
                    // Revocable variants lose the provisional extra...
                    if (comboExtra > 0f) controller.AddEnergy(-comboExtra);
                    // ...and in every variant the premium top above 80% dies with the chain.
                    controller.ClampEnergyTo(PremiumBoundary);
                }
            }
            comboExtra = 0f;
            comboCount = 0;
            windowRemaining = 0f;
            chainInFlight = false;
        }

        void OnPlayerRespawned()
        {
            // The tank was just reset by the controller - clear the run state without the
            // revoke penalty, and restart the regen display from the fresh tank.
            ResetCombo(revoke: false);
            safetyActive = false;
            regenPool = 0f;
            flightOpen = false;
        }

        // ---------- UI ----------

        void LateUpdate()
        {
            if (controller == null) return;

            // The slow meter is the chain-window meter, exactly like variant B.
            var meter = controller.slowdownMeter;
            if (meter != null)
            {
                bool windowLive = windowRemaining > 0f || chainInFlight;
                meter.SetVisible(true);
                meter.SetCharge(0f, false);
                meter.SetEnergy(chainInFlight ? 1f : (comboWindowSeconds > 0f ? windowRemaining / comboWindowSeconds : 0f));
                if (comboCircle != null)
                {
                    bool showCircle = comboCount > 0 && windowLive;
                    comboCircle.SetActive(showCircle);
                    if (showCircle && comboText != null)
                    {
                        // C/D show their midair multiplier (the headline number); the
                        // others the ordinary chain multiplier.
                        float circleMultiplier = DualRefundMode
                            ? Mathf.Min(midairLaunchBaseRefund + midairLaunchStepPerLevel * comboCount, comboMaxMultiplier)
                            : NextMultiplier;
                        comboText.text = "x" + circleMultiplier.ToString("0.0##");
                    }
                }
            }

            // The energy meter is repainted every frame (overriding the controller's own
            // write). The DISPLAY mapping is fixed by the meter's geometry alone: the 8
            // normal blocks are 0..80%, the two big blocks 80..100% - every block is
            // exactly 10% of the tank, no matter what the ECONOMY's premium floor is
            // tuned to (the floor governs refund caps and chain-loss, never block size).
            var energyMeter = controller.energyMeter;
            if (energyMeter != null)
            {
                float mainSpan = PremiumBoundary; // the meter variant's normal-block span
                float energy = controller.EnergyFraction;
                bool charging = controller.IsAimingOrCharging;
                float charge = controller.CurrentChargeFraction;

                // Orange = the volatile slices: the provisional extra and everything
                // above the 80% boundary (they overlap, so take the larger, never both),
                // plus the freshly-regenerated tip still converting. Boost orange is
                // capped at 20% of the tank by construction.
                float overflow = Mathf.Max(energy - PremiumBoundary, 0f);
                float orange = Mathf.Clamp(Mathf.Max(comboExtra, overflow) + regenPool, 0f, energy);
                float yellow = energy - orange;

                float yellowInMain = Mathf.Min(yellow, mainSpan);
                float energyInMain = Mathf.Min(energy, mainSpan);
                energyMeter.SetEnergy(yellowInMain / mainSpan);
                energyMeter.SetBonus(energyInMain / mainSpan, energyInMain - yellowInMain > 0.001f);
                energyMeter.SetCharge(Mathf.Min(charge, mainSpan) / mainSpan, charging);

                float premiumSpan = Mathf.Max(1f - mainSpan, 0.0001f);
                if (premiumOrangeFill != null)
                {
                    premiumOrangeFill.fillAmount = Mathf.Clamp01((energy - mainSpan) / premiumSpan);
                }
                if (premiumChargeFill != null)
                {
                    premiumChargeFill.enabled = charging;
                    premiumChargeFill.fillAmount = charging ? Mathf.Clamp01((charge - mainSpan) / premiumSpan) : 0f;
                }
            }
        }

        void SetupComboMeter()
        {
            var meter = controller.slowdownMeter;
            if (meter == null) return;

            if (meter.energyFillImage != null)
            {
                defaultSlowFillColor = meter.energyFillImage.color;
                meter.energyFillImage.color = comboMeterColor;
                slowMeterRoot = meter.GetComponent<RectTransform>();
                if (slowMeterRoot != null)
                {
                    defaultSlowMeterPosition = slowMeterRoot.anchoredPosition;
                    slowMeterRoot.anchoredPosition = defaultSlowMeterPosition + new Vector2(0f, -comboMeterDropPixels);
                }
                BuildComboCircle(meter);
            }
        }

        void BuildComboCircle(KineticEnergy.Player.EnergyMeterController meter)
        {
            Transform container = meter.energyFillImage.transform.parent != null
                ? meter.energyFillImage.transform.parent
                : meter.transform;

            comboCircle = new GameObject("ComboCircle", typeof(RectTransform));
            comboCircle.transform.SetParent(container, false);
            RectTransform rt = comboCircle.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-10f, 0f);
            rt.sizeDelta = new Vector2(46f, 46f);

            Image circle = comboCircle.AddComponent<Image>();
            Sprite knob = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            if (knob != null) circle.sprite = knob;
            circle.color = comboMeterColor;

            GameObject textGo = new GameObject("Count", typeof(RectTransform));
            textGo.transform.SetParent(comboCircle.transform, false);
            RectTransform trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            comboText = textGo.AddComponent<Text>();
            comboText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            comboText.fontSize = 22;
            comboText.resizeTextForBestFit = true;
            comboText.resizeTextMinSize = 10;
            comboText.resizeTextMaxSize = 22;
            comboText.fontStyle = FontStyle.Bold;
            comboText.alignment = TextAnchor.MiddleCenter;
            comboText.color = new Color(0.1f, 0.08f, 0.02f);

            comboCircle.SetActive(false);
        }

        void BuildHudTag()
        {
            GameObject root = new GameObject("MergedEconomyTag");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-24f, 16f);
            rt.sizeDelta = new Vector2(620f, 34f);

            hudLabel = textGo.AddComponent<Text>();
            hudLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudLabel.fontSize = 22;
            hudLabel.alignment = TextAnchor.LowerRight;
            hudLabel.color = new Color(1f, 1f, 1f, 0.55f);
            RefreshHudLabel();
        }

        void RefreshHudLabel()
        {
            if (hudLabel == null) return;
            string label = currentVariant switch
            {
                MergedEconomyVariant.VariantA => "Variant A - Merged economy (revocable combo extra)",
                MergedEconomyVariant.VariantB => "Variant B - Merged economy (extras bank instantly)",
                MergedEconomyVariant.VariantC => "Variant C - Dual-launch refunds (revocable extra)",
                MergedEconomyVariant.VariantD => "Variant D - Dual-launch refunds (banked)",
                MergedEconomyVariant.VariantE => "Variant E - Total loss on a missed window",
                _ => "Variant ?",
            };
            if (autoMaxEnergy) label += " [AUTO MAX]";
            hudLabel.text = label;
        }
    }
}
