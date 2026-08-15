using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum EconomyVariant
    {
        AimDrain,     // A - aiming MIDAIR drains the tank (the earlier drain design)
        ComboRefund,  // B - flat 70% refunds, +10% per chained landing, revocable orange extra
        ChargeDrain,  // C - EVERY charge type drains: grounded aim, hold charges, midair aim
        Tuned,        // D - two readable rules: flat refunds + grounded recharge
        TargetHunter, // E - ONLY targets pay energy; launches refund nothing
    }

    // The economy playtest harness (QuarryEconomy scene only - this component lives on a
    // scene object, never on the Player prefab). Cycles four energy-economy designs with
    // V / D-pad Right (forward) and C / D-pad Left (back) - free in this scene because
    // camera-variant switching is locked off. Applies each design purely through the
    // controller's PUBLIC tuning fields plus the event hooks, and restores the scene's own
    // values when switching away.
    //
    // Variant D ("Tuned") - REDESIGNED after playtest feedback (the old four-rule blend
    // was unreadable and starved the tank). Now exactly two rules a player can hold:
    //   1. Every landed launch refunds a flat 75% of what it spent - launching always
    //     costs a little, so energy can never be farmed to infinity by launching.
    //   2. Standing on the GROUND slowly recharges the tank (8%/s) up to a 60% ceiling -
    //     a soft-lock is impossible (wait and you can always fly again), but the top 40%
    //     of the tank can only be held through efficient play, never by waiting.
    //
    // Variant E ("TargetHunter") - launches refund NOTHING; collecting a TARGET pays the
    // whole launch's spend back times a bonus multiplier (default 1.4x, with a floor so
    // cheap pokes still pay). Hit targets = net positive; miss = the full spend is gone.
    public class EconomyVariantController : MonoBehaviour
    {
        [Tooltip("The design active at scene start.")]
        public EconomyVariant currentVariant = EconomyVariant.AimDrain;

        [Tooltip("First-boot explainer text (also shown by the pause menu's Info button). Edit freely.")]
        [TextArea(10, 30)]
        public string introText =
            "ENERGY ECONOMY VARIANTS\n\n" +
            "This scene tests five energy economies. Switch with V / D-pad Right (back: C / D-pad Left).\n\n" +
            "A - Midair aim drain: holding the midair aim open bleeds your tank.\n" +
            "B - Combo refunds: every landing refunds 70% of its cost; relaunch within 2 seconds\n" +
            "     and each chained landing pays 10% more (the orange circle shows the multiplier).\n" +
            "     Bonus energy above 70% is provisional (orange) - lost if the chain breaks.\n" +
            "C - Every charge drains: ALL charging costs energy per second - grounded aiming,\n" +
            "     hold charges, and the midair aim alike.\n" +
            "D - Launch tax + ground recharge: every launch keeps 25% of its cost, and standing\n" +
            "     on the ground slowly recharges you up to 60%. You can never be stranded,\n" +
            "     but the top of the tank has to be earned.\n" +
            "E - Only targets pay: launches refund NOTHING - collecting a target pays back\n" +
            "     1.4x what the flight cost. Hit and profit, miss and pay in full.\n\n" +
            "The feedback form in the pause menu asks which economy felt best.\n\n" +
            "Press any button to start.";

        [Header("1 - Aim drain")]
        [Tooltip("Tank fraction lost per real second while the midair aim is open.")]
        public float aimDrainPerSecond = 0.08f;
        [Tooltip("GROUNDED-aim launch refunds in this variant are the scene's own tuning times this - slightly above 1, so landings pay back a bit more.")]
        public float aimDrainGroundedRefundBoost = 1.15f;
        [Tooltip("MIDAIR-aim launch refunds in this variant are the scene's own tuning times this - tuned separately from the grounded boost.")]
        public float aimDrainMidairRefundBoost = 1.15f;

        [Header("2 - Combo refund")]
        [Tooltip("Refund fraction of the spent energy for an unchained landing, any aim method.")]
        public float comboBaseRefund = 0.7f;
        [Tooltip("Extra refund fraction added per chained landing (x1 = +this, x2 = +2x this...).")]
        public float comboStepPerLevel = 0.1f;
        [Tooltip("Seconds after a landing to fire the next launch before the combo (and its orange extra) is lost.")]
        public float comboWindowSeconds = 2f;
        [Tooltip("The chain multiplier can never exceed this refund fraction (2 = 200%) - the count stops growing once a landing reaches it.")]
        public float comboMaxMultiplier = 2f;
        [Tooltip("How far down the combo meter moves so its circle clears the energy meter.")]
        public float comboMeterDropPixels = 44f;
        public Color comboMeterColor = new Color(1f, 0.62f, 0.1f); // matches the pound-boost orange

        [Header("C - Charge drain (every charge type)")]
        [Tooltip("Tank fraction lost per REAL second while ANY charge is open: grounded aim, up/pound hold charges, and the midair aim alike.")]
        public float chargeDrainPerSecond = 0.06f;
        [Tooltip("Launch refunds in THIS variant are the scene's own refund tuning times this - slightly above 1, so landings pay back a bit more.")]
        public float chargeDrainRefundBoost = 1.15f;

        [Header("B - Landings that pay nothing")]
        [Tooltip("Scene objects (found by name at Start) whose surface never counts in the COMBO variant - only the big flat floor. Landing there (or back on the launch object) pays no refund and never chains. (QuarryTerrain must NOT be listed: it is most of the level.)")]
        public string[] bigFloorObjectNames = { "QuarryFloor" };

        [Header("D - Tuned (flat refunds + grounded recharge)")]
        [Tooltip("Every landed launch refunds this fraction of its spend - below 1, so launching can never be energy-positive.")]
        [Range(0f, 1f)] public float tunedFlatRefund = 0.75f;
        [Tooltip("Tank fraction regained per REAL second while standing on the ground.")]
        public float tunedRegenPerSecond = 0.08f;
        [Tooltip("The grounded recharge stops at this fraction - the top of the tank must be EARNED, waiting can't fill it.")]
        [Range(0f, 1f)] public float tunedRegenCeiling = 0.6f;

        [Header("E - Target hunter (only targets pay)")]
        [Tooltip("A collected target pays back the flight's spend times this - above 1, so hitting targets is a net GAIN.")]
        public float targetRewardMultiplier = 1.4f;
        [Tooltip("Minimum energy a collected target always pays, so cheap launches still profit from a hit.")]
        [Range(0f, 1f)] public float targetMinReward = 0.15f;

        KineticCubeController controller;
        KineticEnergy.UI.PauseController pauseController;

        // Scene defaults, captured once so switching variants never permanently mutates
        // the Player's tuned values.
        SlowdownMode defaultSlowdownMode;
        float defaultTankDrain;
        float defaultGroundedRefund;
        float defaultMidairBase;
        float defaultMidairSpendFactor;
        Color defaultSlowFillColor = Color.cyan;
        Vector2 defaultSlowMeterPosition;
        RectTransform slowMeterRoot;

        // Combo state (variants 2 and 4).
        int comboCount;
        float comboExtra;        // revocable orange energy (variant 2 only)
        float windowRemaining;
        bool chainInFlight;      // fired inside the window - frozen until that launch lands
        Transform launchSurface; // what the current launch took off from - landing back on it never chains

        // Variant D: how much of the CURRENT tank came from standing-still regen - shown
        // in orange on the meter. Spending eats this pool first, so the orange vanishes
        // with use and re-grows while standing.
        float regenPool;
        float lastEnergySeen;

        // The big-floor objects that pay no refunds in the boosted variants.
        readonly System.Collections.Generic.List<GameObject> floorObjects = new System.Collections.Generic.List<GameObject>();

        // Runtime UI.
        GameObject comboCircle;
        Text comboText;

        bool ComboLike => currentVariant == EconomyVariant.ComboRefund;
        float ActiveWindow => comboWindowSeconds;

        void Start()
        {
            controller = FindAnyObjectByType<KineticCubeController>();
            if (controller == null)
            {
                Debug.LogError("EconomyVariantController: no KineticCubeController in the scene.");
                enabled = false;
                return;
            }

            defaultSlowdownMode = controller.slowdownMode;
            defaultTankDrain = controller.tankDrainPerSecond;
            defaultGroundedRefund = controller.groundedRefundMultiplier;
            defaultMidairBase = controller.midairRefundBaseMultiplier;
            defaultMidairSpendFactor = controller.midairRefundSpendFactor;
            if (controller.slowdownMeter != null && controller.slowdownMeter.energyFillImage != null)
            {
                defaultSlowFillColor = controller.slowdownMeter.energyFillImage.color;
                slowMeterRoot = controller.slowdownMeter.GetComponent<RectTransform>();
                if (slowMeterRoot != null) defaultSlowMeterPosition = slowMeterRoot.anchoredPosition;
            }

            controller.LaunchFired += OnLaunchFired;
            controller.CrashRegistered += OnCrash;
            TargetSphere.Collected += OnTargetCollected;
            DamageWalls.PlayerRespawned += OnPlayerRespawned;

            pauseController = FindAnyObjectByType<KineticEnergy.UI.PauseController>(FindObjectsInactive.Include);

            floorObjects.Clear();
            foreach (string floorName in bigFloorObjectNames)
            {
                GameObject floor = GameObject.Find(floorName);
                if (floor != null) floorObjects.Add(floor);
            }

            BuildHudTag();
            ApplyVariant();

            // The first-boot explainer (and the pause menu's Info target) - its text lives
            // HERE on the harness so it's editable alongside the variant tuning.
            GameObject introGo = new GameObject("EconomyIntro");
            var intro = introGo.AddComponent<KineticEnergy.UI.AimIntroScreen>();
            intro.introKey = "economy";
            intro.bodyText = introText;
        }

        void OnDestroy()
        {
            TargetSphere.Collected -= OnTargetCollected;
            DamageWalls.PlayerRespawned -= OnPlayerRespawned;
            if (controller == null) return;
            controller.LaunchFired -= OnLaunchFired;
            controller.CrashRegistered -= OnCrash;
        }

        // A hazard respawn wipes the run state: the energy tank was just reset by the
        // controller, so the chain clears WITHOUT the revoke penalty, and the regen-pool
        // display starts over from the fresh tank.
        void OnPlayerRespawned()
        {
            ResetCombo(revoke: false);
            regenPool = 0f;
            if (controller != null) lastEnergySeen = controller.EnergyFraction;
        }

        // Variant E: a collected target pays the flight's spend times the bonus multiplier
        // (with a floor for cheap launches) - the ONLY energy income in that variant.
        void OnTargetCollected()
        {
            if (currentVariant != EconomyVariant.TargetHunter || controller == null) return;
            float reward = Mathf.Max(controller.LastLaunchEnergySpent * targetRewardMultiplier, targetMinReward);
            controller.AddEnergy(reward);
        }

        void Update()
        {
            if (controller == null) return;

            // timeScale 0 is BOTH the pause/intro freeze AND the midair aim's bullet-time
            // freeze. The harness must keep running through the AIM freeze - the combo
            // window and the charge rents run on REAL seconds precisely so aiming can't
            // hide from them (this gate was why the window sat at full through every
            // midair aim) - while a genuine pause or the intro overlay halts everything.
            if (Time.timeScale <= 0f)
            {
                bool trulyPaused = (pauseController != null && pauseController.IsPaused)
                    || KineticEnergy.UI.AimIntroScreen.InputBlocked;
                if (trulyPaused || !controller.IsAimingOrCharging) return;
            }

            bool forward = (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame);
            bool back = (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);
            if ((forward || back) && !controller.IsAimingOrCharging)
            {
                int count = System.Enum.GetValues(typeof(EconomyVariant)).Length;
                currentVariant = (EconomyVariant)(((int)currentVariant + (forward ? 1 : count - 1)) % count);
                ApplyVariant();
            }

            // A chained flight normally thaws via its landing crash - but flights can end
            // WITHOUT one (a NonStick bounce, an enemy hit, a soft touch-down). Standing
            // on the ground with no launch in progress means the flight is over however it
            // ended, so the frozen window must resume; without this it stuck at full.
            if (ComboLike && chainInFlight && controller.IsGrounded && !controller.HasLaunched)
            {
                chainInFlight = false;
            }

            // Combo window: ticks only BETWEEN launches (frozen while a chained launch is
            // in the air - the landing is what matters, not the flight time).
            if (ComboLike && windowRemaining > 0f && !chainInFlight)
            {
                windowRemaining -= Time.unscaledDeltaTime;
                if (windowRemaining <= 0f) ResetCombo(revoke: true);
            }

            // Live refund multipliers - the chain level feeds straight into the public
            // tuning fields the refund code already uses.
            if (currentVariant == EconomyVariant.ComboRefund)
            {
                float refund = Mathf.Min(comboBaseRefund + comboStepPerLevel * comboCount, comboMaxMultiplier);
                controller.groundedRefundMultiplier = refund;
                controller.midairRefundBaseMultiplier = refund;
                controller.midairRefundSpendFactor = 0f;
            }

            // Variant D rule 2: the ground slowly recharges the tank, up to its ceiling -
            // a soft-lock is impossible, but the top of the tank stays earned-only. The
            // regen pool tracks how much of the tank the ground gave (drawn in orange);
            // any spend since last frame comes out of the pool first.
            if (currentVariant == EconomyVariant.Tuned)
            {
                float energyNow = controller.EnergyFraction;
                if (energyNow < lastEnergySeen) regenPool = Mathf.Max(0f, regenPool - (lastEnergySeen - energyNow));

                if (controller.IsGrounded && energyNow < tunedRegenCeiling)
                {
                    float headroom = tunedRegenCeiling - energyNow;
                    float gained = Mathf.Min(tunedRegenPerSecond * Time.unscaledDeltaTime, headroom);
                    controller.AddEnergy(gained);
                    regenPool += gained;
                }
                regenPool = Mathf.Min(regenPool, controller.EnergyFraction);
                lastEnergySeen = controller.EnergyFraction;
            }

            // Variant C: rent on EVERY charge - grounded aim, hold charges, midair aim.
            // Real seconds, so the bullet-time doesn't discount the cost.
            if (currentVariant == EconomyVariant.ChargeDrain && controller.IsAimingOrCharging)
            {
                controller.AddEnergy(-chargeDrainPerSecond * Time.unscaledDeltaTime);
            }
        }

        void LateUpdate()
        {
            if (controller == null) return;
            UpdateComboUi();
            UpdateRegenUi();
        }

        // ---------- Variant application ----------

        void ApplyVariant()
        {
            // Baseline restore first, so each variant starts from the scene's own values.
            controller.slowdownMode = defaultSlowdownMode;
            controller.tankDrainPerSecond = defaultTankDrain;
            controller.groundedRefundMultiplier = defaultGroundedRefund;
            controller.midairRefundBaseMultiplier = defaultMidairBase;
            controller.midairRefundSpendFactor = defaultMidairSpendFactor;
            controller.launchScatterMaxAngle = 0f;
            ResetCombo(revoke: true);

            // The regen-pool display state starts fresh with every variant.
            regenPool = 0f;
            lastEnergySeen = controller.EnergyFraction;

            switch (currentVariant)
            {
                case EconomyVariant.AimDrain:
                    controller.slowdownMode = SlowdownMode.EnergyTank;
                    controller.tankDrainPerSecond = aimDrainPerSecond;
                    // Landings pay back slightly more than the scene's own tuning here -
                    // the grounded and midair aims carry their own separate boosts.
                    controller.groundedRefundMultiplier = defaultGroundedRefund * aimDrainGroundedRefundBoost;
                    controller.midairRefundBaseMultiplier = defaultMidairBase * aimDrainMidairRefundBoost;
                    controller.midairRefundSpendFactor = defaultMidairSpendFactor * aimDrainMidairRefundBoost;
                    break;

                case EconomyVariant.ComboRefund:
                    controller.slowdownMode = SlowdownMode.Unlimited;
                    break;

                case EconomyVariant.ChargeDrain:
                    // The drain is applied manually in Update (EnergyTank mode would only
                    // meter the midair aim - C charges rent on EVERY charge type).
                    controller.slowdownMode = SlowdownMode.Unlimited;
                    controller.groundedRefundMultiplier = defaultGroundedRefund * chargeDrainRefundBoost;
                    controller.midairRefundBaseMultiplier = defaultMidairBase * chargeDrainRefundBoost;
                    controller.midairRefundSpendFactor = defaultMidairSpendFactor * chargeDrainRefundBoost;
                    break;

                case EconomyVariant.Tuned:
                    // Rule 1: flat refunds below 100% - launching is never energy-positive.
                    // Rule 2 (the grounded recharge) runs per frame in Update.
                    controller.slowdownMode = SlowdownMode.Unlimited;
                    controller.groundedRefundMultiplier = tunedFlatRefund;
                    controller.midairRefundBaseMultiplier = tunedFlatRefund;
                    controller.midairRefundSpendFactor = 0f;
                    break;

                case EconomyVariant.TargetHunter:
                    // Launches refund NOTHING - only collected targets pay (see OnTargetCollected).
                    controller.slowdownMode = SlowdownMode.Unlimited;
                    controller.groundedRefundMultiplier = 0f;
                    controller.midairRefundBaseMultiplier = 0f;
                    controller.midairRefundSpendFactor = 0f;
                    break;
            }

            if (hudLabel != null) hudLabel.text = CurrentLabel;
            RefreshMeterLayout();
        }

        string CurrentLabel => currentVariant switch
        {
            EconomyVariant.AimDrain => "Variant A - Midair aim drain",
            EconomyVariant.ComboRefund => "Variant B - Combo refunds",
            EconomyVariant.ChargeDrain => "Variant C - Every charge drains",
            EconomyVariant.Tuned => "Variant D - Launch tax + ground recharge",
            EconomyVariant.TargetHunter => "Variant E - Only targets pay energy",
            _ => "Variant ?",
        };

        // ---------- Combo machinery (variants 2 and 4) ----------

        void OnLaunchFired()
        {
            if (!ComboLike) return;
            // Where this launch left FROM (grounded launches; a midair relaunch has no
            // surface below and matches nothing) - landing back on the same object must
            // not build the chain.
            launchSurface = null;
            if (Physics.Raycast(controller.transform.position, Vector3.down, out RaycastHit hit, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                launchSurface = hit.collider.transform;
            }
            if (windowRemaining > 0f) chainInFlight = true; // chain stays alive through the flight
        }

        void OnCrash(Vector3 position)
        {
            if (!ComboLike) return;

            // Landings that never COUNT in the combo variant: the big quarry floor, and
            // the very object this launch took off from. They pay nothing - the refund
            // just granted is taken straight back - and the chain neither grows nor
            // refreshes (an in-flight freeze just thaws).
            Collider crashSurface = controller.LastCrashSurface;
            if (crashSurface != null && (IsBigFloor(crashSurface.transform)
                || (launchSurface != null && crashSurface.transform == launchSurface)))
            {
                if (controller.LastCrashRefund > 0f) controller.AddEnergy(-controller.LastCrashRefund);
                chainInFlight = false;
                return;
            }

            // The refund for THIS landing was just paid with the multipliers derived from
            // the pre-landing combo level. Only the LATEST landing's extra stays provisional
            // orange (variant 2) - a successful chain SOLIDIFIES the previous extra onto the
            // energy meter (direct request), so replacing rather than accumulating here is
            // exactly the banking step.
            if (currentVariant == EconomyVariant.ComboRefund)
            {
                comboExtra = Mathf.Min(
                    controller.LastLaunchEnergySpent * comboStepPerLevel * comboCount,
                    controller.EnergyFraction);
            }

            comboCount++;
            // Capped chain: the count stops at the level whose multiplier reaches the cap.
            if (comboStepPerLevel > 0f)
            {
                int maxLevels = Mathf.Max(Mathf.FloorToInt((comboMaxMultiplier - comboBaseRefund) / comboStepPerLevel + 0.0001f), 0);
                comboCount = Mathf.Min(comboCount, maxLevels);
            }
            chainInFlight = false;
            windowRemaining = ActiveWindow;
        }

        bool IsBigFloor(Transform surfaceTransform)
        {
            foreach (GameObject floor in floorObjects)
            {
                if (floor != null && (surfaceTransform == floor.transform || surfaceTransform.IsChildOf(floor.transform)))
                {
                    return true;
                }
            }
            return false;
        }

        void ResetCombo(bool revoke)
        {
            if (revoke && currentVariant == EconomyVariant.ComboRefund && comboExtra > 0f)
            {
                controller.AddEnergy(-comboExtra); // the orange extra is lost, pound-boost style
            }
            comboExtra = 0f;
            comboCount = 0;
            windowRemaining = 0f;
            chainInFlight = false;
        }

        // ---------- HUD: variant tag, combo meter, scatter ring ----------

        Text hudLabel;

        void BuildHudTag()
        {
            GameObject root = new GameObject("EconomyVariantTag");
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
            rt.sizeDelta = new Vector2(560f, 34f);

            hudLabel = textGo.AddComponent<Text>();
            hudLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudLabel.fontSize = 22;
            hudLabel.alignment = TextAnchor.LowerRight;
            hudLabel.color = new Color(1f, 1f, 1f, 0.55f);
        }

        // The combo meter is the repurposed slowdown meter: dropped a bit lower (so the
        // circle clears the energy meter), fill re-coloured orange, showing the remaining
        // chain window. The circle in front shows "xN".
        void RefreshMeterLayout()
        {
            var meter = controller.slowdownMeter;
            if (meter == null) return;

            if (slowMeterRoot != null)
            {
                slowMeterRoot.anchoredPosition = ComboLike
                    ? defaultSlowMeterPosition + new Vector2(0f, -comboMeterDropPixels)
                    : defaultSlowMeterPosition;
            }
            if (meter.energyFillImage != null)
            {
                meter.energyFillImage.color = currentVariant == EconomyVariant.ComboRefund
                    ? comboMeterColor
                    : defaultSlowFillColor;
            }
            if (comboCircle == null && meter.energyFillImage != null) BuildComboCircle(meter);
            if (comboCircle != null) comboCircle.SetActive(false);
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
            comboText.resizeTextForBestFit = true; // "x0.8" has to fit the circle too
            comboText.resizeTextMinSize = 10;
            comboText.resizeTextMaxSize = 22;
            comboText.fontStyle = FontStyle.Bold;
            comboText.alignment = TextAnchor.MiddleCenter;
            comboText.color = new Color(0.1f, 0.08f, 0.02f);
        }

        void UpdateComboUi()
        {
            var meter = controller.slowdownMeter;
            var energyMeter = controller.energyMeter;
            if (!ComboLike)
            {
                if (comboCircle != null) comboCircle.SetActive(false);
                return;
            }

            // Runs after the controller's own meter update, deliberately overriding it:
            // in these variants the slow meter IS the chain-window meter.
            if (meter != null)
            {
                bool windowLive = windowRemaining > 0f || chainInFlight;
                meter.SetVisible(true);
                meter.SetCharge(0f, false);
                meter.SetEnergy(chainInFlight ? 1f : (ActiveWindow > 0f ? windowRemaining / ActiveWindow : 0f));
                if (comboCircle != null)
                {
                    bool showCircle = comboCount > 0 && windowLive;
                    comboCircle.SetActive(showCircle);
                    if (showCircle && comboText != null)
                    {
                        // The MULTIPLIER the next landing pays, not the chain length:
                        // x0.7, x0.8, x0.9, x1, x1.1 ... capped at comboMaxMultiplier.
                        float multiplier = Mathf.Min(comboBaseRefund + comboStepPerLevel * comboCount, comboMaxMultiplier);
                        comboText.text = "x" + multiplier.ToString("0.0##");
                    }
                }
            }

            // Variant 2's revocable extra rides the energy meter in ORANGE, pound-style:
            // the yellow understates by the extra, the orange behind pokes out by it.
            if (currentVariant == EconomyVariant.ComboRefund && energyMeter != null && comboExtra > 0f)
            {
                float energy = controller.EnergyFraction;
                energyMeter.SetEnergy(energy - comboExtra);
                energyMeter.SetBonus(energy, true);
            }
        }

        // Variant D: the ground-regen energy rides the meter in the same ORANGE bonus
        // segment - what standing still gave you stays visibly distinct from earned
        // energy until it's spent. Runs after the controller's own meter update.
        void UpdateRegenUi()
        {
            if (currentVariant != EconomyVariant.Tuned) return;
            var energyMeter = controller.energyMeter;
            if (energyMeter == null || regenPool <= 0.001f) return;

            float energy = controller.EnergyFraction;
            energyMeter.SetEnergy(energy - regenPool);
            energyMeter.SetBonus(energy, true);
        }

    }
}
