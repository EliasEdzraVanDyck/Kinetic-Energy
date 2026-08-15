using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum MergedEconomyVariant
    {
        RevocableExtra,    // A - the latest chain extra stays provisional orange; a lapse revokes it
        BankedExtra,       // B - every chain extra banks INSTANTLY as normal energy; a lapse only resets the multiplier
        RevocableExtraMax, // C - variant A, but every launch fires at MAX energy automatically
        BankedExtraMax,    // D - variant B, but every launch fires at MAX energy automatically
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
        [Tooltip("A: revocable chain extra (capped at 20% of the tank). B: extras bank instantly. C/D: the same two with every launch auto-firing at MAX energy. Cycle with V / D-pad Right and C / D-pad Left.")]
        public MergedEconomyVariant currentVariant = MergedEconomyVariant.RevocableExtra;

        bool BankedMode => currentVariant == MergedEconomyVariant.BankedExtra
            || currentVariant == MergedEconomyVariant.BankedExtraMax;
        bool AutoMaxMode => currentVariant == MergedEconomyVariant.RevocableExtraMax
            || currentVariant == MergedEconomyVariant.BankedExtraMax;

        [Header("1 - Combo refunds")]
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
        [Tooltip("Scene objects (found by name at Start) whose surface never counts: no refund, no chain. Only the big flat floor - QuarryTerrain must NOT be listed.")]
        public string[] bigFloorObjectNames = { "QuarryFloor" };

        [Header("2 - Safety recharge")]
        [Tooltip("Dropping below this fraction switches the grounded recharge ON.")]
        [Range(0f, 1f)] public float safetyTriggerFraction = 0.1f;
        [Tooltip("The recharge fills up to here, then switches OFF until the next dip below the trigger.")]
        [Range(0f, 1f)] public float safetyCeilingFraction = 0.3f;
        [Tooltip("Tank fraction regained per REAL second while standing on the ground with the recharge latched on.")]
        public float regenPerSecond = 0.08f;
        [Tooltip("Seconds for freshly-regenerated energy to convert from ORANGE into normal yellow - a steady recharge shows a small orange tip at the fill edge that keeps turning yellow behind it.")]
        public float regenOrangeFadeSeconds = 0.35f;

        [Header("3 - Premium top")]
        // The normal/boost split is POSITIONAL and fixed at 80/20 (direct request): the
        // first 80% of the tank is always normal energy whatever filled it, the last 20%
        // is always boosted - only combo extras and the pound boost can fill it, and it
        // is lost when the chain stops. Matches the meter's 8+2 block geometry exactly.
        const float PremiumBoundary = 0.8f;

        KineticCubeController controller;
        KineticEnergy.UI.PauseController pauseController;

        // Combo state - the same machinery as EconomyVariantController's variant B, minus
        // the multiplier inflation: the base refund flows through the (ceiling-capped)
        // controller pipeline, the chain EXTRA is paid here directly so it may pass the cap.
        int comboCount;
        float comboExtra;        // the latest landing's extra (variant A/C) - revocable orange, never above 20%
        float windowRemaining;
        bool chainInFlight;
        Transform launchSurface;

        // Safety recharge state.
        bool safetyActive;
        float regenPool;         // the FRESH regen slice, drawn orange - decays into yellow

        readonly List<GameObject> floorObjects = new List<GameObject>();

        // Runtime UI (combo circle riding the repurposed slowdown meter, HUD tag).
        GameObject comboCircle;
        Text comboText;
        Text hudLabel;
        Color defaultSlowFillColor = Color.cyan;
        Vector2 defaultSlowMeterPosition;
        RectTransform slowMeterRoot;

        float NextMultiplier => Mathf.Min(comboBaseRefund + comboStepPerLevel * comboCount, comboMaxMultiplier);

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

            floorObjects.Clear();
            foreach (string floorName in bigFloorObjectNames)
            {
                GameObject floor = GameObject.Find(floorName);
                if (floor != null) floorObjects.Add(floor);
            }

            // The one-time economy wiring: base combo refunds through the ordinary
            // pipeline, capped at the premium floor; slow-down stays free.
            controller.slowdownMode = SlowdownMode.Unlimited;
            controller.groundedRefundMultiplier = comboBaseRefund;
            controller.midairRefundBaseMultiplier = comboBaseRefund;
            controller.midairRefundSpendFactor = 0f;
            controller.ordinaryRefundCeiling = PremiumBoundary;
            // The safety recharge IS this scene's stranding failsafe - the grounded
            // reserve (unspendable bottom slice) is disabled, the whole tank fires.
            controller.minEnergyReserve = 0f;

            controller.LaunchFired += OnLaunchFired;
            controller.CrashRegistered += OnCrash;
            DamageWalls.PlayerRespawned += OnPlayerRespawned;

            BuildHudTag();
            SetupComboMeter();
            BuildPremiumZone();
            ApplyVariant();
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
            controller.alwaysMaxCharge = AutoMaxMode;
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

            // Variant cycling, blocked while an aim is open like every harness.
            if (!controller.IsAimingOrCharging)
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
            }

            // A chained flight that ended WITHOUT a landing crash (NonStick bounce, enemy
            // hit, soft touch-down) thaws once the player stands with no launch running.
            if (chainInFlight && controller.IsGrounded && !controller.HasLaunched)
            {
                chainInFlight = false;
            }

            if (windowRemaining > 0f && !chainInFlight)
            {
                windowRemaining -= Time.unscaledDeltaTime;
                if (windowRemaining <= 0f) ResetCombo(revoke: true);
            }

            UpdateSafetyRecharge();
        }

        void UpdateSafetyRecharge()
        {
            float energyNow = controller.EnergyFraction;
            if (!safetyActive && energyNow < safetyTriggerFraction) safetyActive = true;

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
                if (comboIdle && restingOnSurface && energyNow < safetyCeilingFraction)
                {
                    float headroom = safetyCeilingFraction - energyNow;
                    float gained = Mathf.Min(regenPerSecond * Time.unscaledDeltaTime, headroom);
                    controller.AddEnergy(gained);
                    regenPool += gained;
                }
                if (controller.EnergyFraction >= safetyCeilingFraction - 0.0001f) safetyActive = false;
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

            launchSurface = null;
            if (Physics.Raycast(controller.transform.position, Vector3.down, out RaycastHit hit, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                launchSurface = hit.collider.transform;
            }
            if (windowRemaining > 0f) chainInFlight = true;
        }

        void OnCrash(Vector3 position)
        {
            // Landings that never COUNT: the big floor, and the launch's own takeoff
            // object - the refund just granted is taken back and the chain neither grows
            // nor refreshes.
            Collider crashSurface = controller.LastCrashSurface;
            if (crashSurface != null && (IsBigFloor(crashSurface.transform)
                || (launchSurface != null && crashSurface.transform == launchSurface)))
            {
                if (controller.LastCrashRefund > 0f) controller.AddEnergy(-controller.LastCrashRefund);
                chainInFlight = false;
                return;
            }

            // The chain EXTRA for this landing, from the PRE-landing chain level (the base
            // refund was already paid by the ordinary pipeline). Paid directly, so it can
            // pass the premium boundary - this and the pound boost are the only ways up
            // there. Under the positional 80/20 rule, whatever lands below 80% is normal
            // energy immediately; only the part sitting above 80% is at risk.
            float extraRate = NextMultiplier - comboBaseRefund;
            if (extraRate > 0f && comboCount > 0)
            {
                float extra = controller.LastLaunchEnergySpent * extraRate;
                controller.AddEnergy(extra);
                // A: the LATEST extra stays provisional orange (replace, not accumulate) -
                // but the BOOSTED slice is hard-capped at the premium segment's 20%, so a
                // full tank never has more than its top two blocks at risk. B: every
                // extra is normal energy the instant it's paid.
                comboExtra = BankedMode
                    ? 0f
                    : Mathf.Min(extra, Mathf.Min(controller.EnergyFraction, 1f - PremiumBoundary));
            }

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
                // Variant A/C: the provisional extra is revoked (it was capped at 20%,
                // so this can never gut a built-up tank)...
                if (comboExtra > 0f) controller.AddEnergy(-comboExtra);
                // ...and in EVERY variant the premium top above 80% dies with the chain.
                controller.ClampEnergyTo(PremiumBoundary);
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
                        comboText.text = "x" + NextMultiplier.ToString("0.0##");
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
                const float mainSpan = 0.8f; // 8 of the meter's 10 blocks
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

                const float premiumSpan = 1f - mainSpan;
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
            hudLabel.text = currentVariant switch
            {
                MergedEconomyVariant.RevocableExtra => "Variant A - Merged economy (revocable combo extra)",
                MergedEconomyVariant.BankedExtra => "Variant B - Merged economy (extras bank instantly)",
                MergedEconomyVariant.RevocableExtraMax => "Variant C - A + auto max-energy launches",
                MergedEconomyVariant.BankedExtraMax => "Variant D - B + auto max-energy launches",
                _ => "Variant ?",
            };
        }
    }
}
