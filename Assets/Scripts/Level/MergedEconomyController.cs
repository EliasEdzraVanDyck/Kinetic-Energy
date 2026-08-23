using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum MergedEconomyVariant
    {
        VariantA,
        VariantB,
        VariantC,
        VariantD,
        VariantE,
    }

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
        [Tooltip("The xN circle's colour while NO combo is running - the circle and its value stay visible at all times, grey until a chain starts.")]
        public Color comboIdleColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
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
        [Tooltip("Open the intro overlay automatically at boot. OFF keeps it reachable through the pause menu's BuildInfo button only - the bottom-left section HUD carries the teaching now.")]
        public bool showIntroOnBoot = true;
        [Tooltip("The first-boot key: each key shows once per game session. Scenes with their own intro text get their own key.")]
        public string introKey = "economy2";
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

        [Header("Wall / midair launch stake (all variants)")]

        [Tooltip("Minimum stake for launches opened while NOT grounded (wall sticks, midair): the momentum carry floor and the payout stake floor. Independent of the recharge ceiling.")]
        [Range(0f, 1f)] public float wallLaunchStakeFraction = 0.4f;

        [Header("Premium top (all variants)")]

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
        [Tooltip("A combo window that runs dry while you are AIRBORNE cuts the midair aim and drops you - the held flight is forfeited and you fall. Off where the window is purely an economy timer.")]
        public bool dropPlayerWhenWindowExpires = false;
        [Tooltip("Standing still refills you below the baseline even while a combo window is still running. Off, the recharge waits for the chain to lapse so the chain stays the only income.")]
        public bool regenWhileComboRunning = false;
        [Tooltip("Show the bottom-right variant tag (off in the locked test scenes).")]
        public bool showHudTag = true;

        KineticCubeController controller;
        KineticEnergy.UI.PauseController pauseController;

        int comboCount;
        float comboExtra;
        float windowRemaining;
        bool chainInFlight;
        Transform launchSurface;

        Transform lastTouchedSurface;

        bool flightOpen;
        float flightFirstSpend;
        float flightMidairSpend;
        float flightStartEnergy;
        float groundedSettleTimer;

        [Tooltip("A flight resting on the ground this long WITHOUT a registered crash closes its ledger unpaid - shallow swallowed landings can then never defer their spends onto a later (wall) crash, so walls pay exactly like platforms.")]
        public float flightSettleSeconds = 0.25f;

        bool safetyActive;
        float regenPool;

        [Tooltip("Explicit combo-window meter. When set, the combo display lives HERE and controller.slowdownMeter is left alone - so a scene can show the aim-budget bar separately (Level1Challenge stage 1). Empty = repurpose controller.slowdownMeter, as before.")]
        public KineticEnergy.Player.EnergyMeterController comboMeter;

        KineticEnergy.Player.EnergyMeterController ComboDisplayMeter
            => comboMeter != null ? comboMeter : (controller != null ? controller.slowdownMeter : null);

        GameObject comboCircle;
        Image comboCircleImage;
        Text comboText;
        Text hudLabel;
        Color defaultSlowFillColor = Color.cyan;
        Vector2 defaultSlowMeterPosition;
        RectTransform slowMeterRoot;

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

            if (FindAnyObjectByType<ChallengeStageController>(FindObjectsInactive.Include) == null)
            {
                controller.slowdownMode = SlowdownMode.Unlimited;
            }
            controller.ordinaryRefundCeiling = PremiumBoundary;

            controller.minEnergyReserve = 0f;

            controller.LaunchFired += OnLaunchFired;
            controller.CrashRegistered += OnCrash;
            DamageWalls.PlayerRespawned += OnPlayerRespawned;

            controller.addPreAimVelocityToLaunch = momentumLaunches;

            BuildHudTag();
            SetupComboMeter();
            BuildPremiumZone();
            ApplyVariant();

            GameObject introGo = new GameObject("MergedEconomyIntro");
            var intro = introGo.AddComponent<KineticEnergy.UI.AimIntroScreen>();
            intro.introKey = introKey;
            intro.bodyText = introText;
            intro.showOnBoot = showIntroOnBoot;
        }

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

            if (BankedMode) comboExtra = 0f;
            controller.alwaysMaxCharge = autoMaxEnergy;

            controller.wallLaunchMomentumFloorFraction = Mathf.Clamp01(wallLaunchStakeFraction);

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

            if (Time.timeScale <= 0f)
            {
                bool trulyPaused = (pauseController != null && pauseController.IsPaused)
                    || KineticEnergy.UI.AimIntroScreen.InputBlocked;
                if (trulyPaused || !controller.IsAimingOrCharging) return;
            }

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

                bool toggleAutoMax = (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.dpad.down.wasPressedThisFrame);
                if (toggleAutoMax)
                {
                    autoMaxEnergy = !autoMaxEnergy;
                    controller.alwaysMaxCharge = autoMaxEnergy;
                    RefreshHudLabel();
                }
            }

            if (controller.IsGrounded && !controller.HasLaunched)
            {

                chainInFlight = false;
                flightOpen = false;
            }

            if ((flightOpen || chainInFlight) && controller.IsGrounded) groundedSettleTimer += Time.unscaledDeltaTime;
            else groundedSettleTimer = 0f;
            if (groundedSettleTimer >= Mathf.Max(flightSettleSeconds, 0.05f))
            {
                flightOpen = false;

                chainInFlight = false;
                groundedSettleTimer = 0f;
            }

            if (windowRemaining > 0f)
            {
                windowRemaining -= Time.unscaledDeltaTime;
                if (windowRemaining <= 0f)
                {
                    ResetCombo(revoke: true);

                    if (dropPlayerWhenWindowExpires && !controller.IsGrounded && !controller.IsStuck)
                    {
                        controller.ForceEndAirAimAndFall();
                    }
                }
            }

            if (comboExtra > 1f - PremiumBoundary && controller.EnergyFraction >= 0.999f)
            {
                comboExtra = 1f - PremiumBoundary;
            }

            UpdateSafetyRecharge();
        }

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

                bool comboIdle = regenWhileComboRunning || windowRemaining <= 0f;
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

            if (regenPool > 0f)
            {
                float fade = Mathf.Min(Time.unscaledDeltaTime / Mathf.Max(regenOrangeFadeSeconds, 0.01f), 1f);
                regenPool -= regenPool * fade;
                if (regenPool < 0.0005f) regenPool = 0f;
            }
            regenPool = Mathf.Min(regenPool, controller.EnergyFraction);
        }

        void OnLaunchFired()
        {

            regenPool = 0f;

            if (!flightOpen)
            {
                flightOpen = true;
                flightFirstSpend = controller.LastLaunchEnergySpent;
                flightMidairSpend = 0f;

                flightStartEnergy = Mathf.Min(controller.EnergyFraction + controller.LastLaunchEnergySpent, 1f);

                if (!controller.IsGrounded)
                {
                    flightStartEnergy = Mathf.Max(flightStartEnergy, Mathf.Clamp01(wallLaunchStakeFraction));
                }

                launchSurface = null;
                if ((controller.IsGrounded || controller.IsStuck)
                    && Physics.Raycast(controller.transform.position, Vector3.down, out RaycastHit hit, 4f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    launchSurface = hit.collider.transform;

                    lastTouchedSurface = launchSurface;
                }
            }
            else
            {
                flightMidairSpend += controller.LastLaunchEnergySpent;
            }

            if (windowRemaining > 0f)
            {

                chainInFlight = true;
            }
            else
            {

                windowRemaining = comboWindowSeconds;
            }
        }

        void OnCrash(Vector3 position)
        {

            if (controller.LastCrashWasPound)
            {
                Collider poundSurface = controller.LastCrashSurface;
                bool sameGroundAsLastTime = poundSurface != null && poundSurface.transform == lastTouchedSurface;
                if (poundSurface != null) lastTouchedSurface = poundSurface.transform;

                flightOpen = false;
                if (!sameGroundAsLastTime)
                {
                    comboCount++;
                    if (comboStepPerLevel > 0f)
                    {
                        int poundMaxLevels = Mathf.Max(Mathf.FloorToInt((comboMaxMultiplier - comboBaseRefund) / comboStepPerLevel + 0.0001f), 0);
                        comboCount = Mathf.Min(comboCount, poundMaxLevels);
                    }
                }
                chainInFlight = false;

                windowRemaining = comboWindowSeconds;
                return;
            }

            Collider crashSurface = controller.LastCrashSurface;
            if (crashSurface != null && crashSurface.transform == lastTouchedSurface)
            {
                if (controller.LastCrashRefund > 0f) controller.AddEnergy(-controller.LastCrashRefund);

                controller.AddEnergy((DualRefundMode || TotalLossMode) && flightOpen
                    ? Mathf.Min(flightFirstSpend + flightMidairSpend, flightStartEnergy)
                    : controller.ArrivalEnergySpent);
                chainInFlight = false;
                flightOpen = false;
                windowRemaining = comboWindowSeconds;
                return;
            }

            if (crashSurface != null) lastTouchedSurface = crashSurface.transform;

            if ((DualRefundMode || TotalLossMode) && controller.LastCrashRefund > 0f)
            {
                controller.AddEnergy(-controller.LastCrashRefund);
            }

            if (DualRefundMode && flightOpen)
            {
                float m1 = Mathf.Min(firstLaunchBaseRefund + firstLaunchStepPerLevel * comboCount, comboMaxMultiplier);
                float m2 = Mathf.Min(midairLaunchBaseRefund + midairLaunchStepPerLevel * comboCount, comboMaxMultiplier);

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

                float m = NextMultiplier;

                float totalSpend = Mathf.Min(flightFirstSpend + flightMidairSpend, flightStartEnergy);
                float baseGain = totalSpend * totalLossBaseRefund;
                float baseHeadroom = Mathf.Max(PremiumBoundary - controller.EnergyFraction, 0f);
                controller.AddEnergy(Mathf.Min(baseGain, baseHeadroom));
                float extraGain = totalSpend * (m - totalLossBaseRefund);
                if (extraGain > 0f) controller.AddEnergy(extraGain);
            }

            if (!DualRefundMode && !TotalLossMode)
            {
                float extraRate = NextMultiplier - ActiveComboBase;
                if (extraRate > 0f && comboCount > 0)
                {
                    float extra = controller.LastLaunchEnergySpent * extraRate;
                    controller.AddEnergy(extra);

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

                    controller.ClampEnergyTo(totalLossKeepFraction);
                }
                else
                {

                    if (comboExtra > 0f) controller.AddEnergy(-comboExtra);

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

            ResetCombo(revoke: false);
            lastTouchedSurface = null;
            safetyActive = false;
            regenPool = 0f;
            flightOpen = false;
        }

        void LateUpdate()
        {
            if (controller == null) return;

            var meter = ComboDisplayMeter;
            if (meter != null)
            {
                bool windowLive = windowRemaining > 0f;
                meter.SetVisible(true);
                meter.SetCharge(0f, false);
                meter.SetEnergy(comboWindowSeconds > 0f ? windowRemaining / comboWindowSeconds : 0f);
                if (comboCircle != null)
                {

                    comboCircle.SetActive(true);
                    bool comboLive = comboCount > 0 && windowLive;
                    if (comboCircleImage != null)
                    {
                        comboCircleImage.color = comboLive ? comboMeterColor : comboIdleColor;
                    }
                    if (comboText != null)
                    {

                        float circleMultiplier = DualRefundMode
                            ? Mathf.Min(midairLaunchBaseRefund + midairLaunchStepPerLevel * comboCount, comboMaxMultiplier)
                            : NextMultiplier;
                        comboText.text = "x" + circleMultiplier.ToString("0.0##");
                    }
                }
            }

            var energyMeter = controller.energyMeter;
            if (energyMeter != null)
            {
                float mainSpan = PremiumBoundary;

                energyMeter.displaySpan = mainSpan;
                float energy = controller.EnergyFraction;
                bool charging = controller.IsAimingOrCharging;
                float charge = controller.CurrentChargeFraction;

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
            var meter = ComboDisplayMeter;
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
            comboCircleImage = circle;

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
            if (!showHudTag) return;
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

