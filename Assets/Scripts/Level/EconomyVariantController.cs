using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum EconomyVariant
    {
        AimDrain,          // 1 - aiming midair drains the tank (the earlier drain design)
        ComboRefund,       // 2 - flat 70% refunds, +10% per chained landing, revocable orange extra
        OverchargeScatter, // 3 - big launches scatter: charge buys distance but costs precision
        Tuned,             // 4 - the balanced blend (see the header comment for the reasoning)
    }

    // The economy playtest harness (QuarryEconomy scene only - this component lives on a
    // scene object, never on the Player prefab). Cycles four energy-economy designs with
    // V / D-pad Right (forward) and C / D-pad Left (back) - free in this scene because
    // camera-variant switching is locked off. Applies each design purely through the
    // controller's PUBLIC tuning fields plus the event hooks, and restores the scene's own
    // values when switching away.
    //
    // Variant 4 ("Tuned") - the reasoning:
    //   - Grounded launches refund 50%: launching from safety stays cheap but never free,
    //     so the tank genuinely depletes when you play timidly.
    //   - Midair launches refund 60% + 30% x spend: the EE1 idea kept - committing MORE
    //     energy midair pays back proportionally more, so bravery is the efficient play.
    //   - A gentle 4%/s aim drain: hesitating in bullet-time costs something, but slowly
    //     enough that deliberate planning is still viable (the harsh drain is variant 1's
    //     experiment).
    //   - A SOLID chain bonus (+5% refund per quick relaunch, capped at +20%): rewards
    //     flow like variant 2's combo, but earned energy is never revoked - revocation is
    //     variant 2's experiment; the "best" blend should stay readable and non-punishing.
    public class EconomyVariantController : MonoBehaviour
    {
        [Tooltip("The design active at scene start.")]
        public EconomyVariant currentVariant = EconomyVariant.AimDrain;

        [Header("1 - Aim drain")]
        [Tooltip("Tank fraction lost per real second while the midair aim is open.")]
        public float aimDrainPerSecond = 0.08f;

        [Header("2 - Combo refund")]
        [Tooltip("Refund fraction of the spent energy for an unchained landing, any aim method.")]
        public float comboBaseRefund = 0.7f;
        [Tooltip("Extra refund fraction added per chained landing (x1 = +this, x2 = +2x this...).")]
        public float comboStepPerLevel = 0.1f;
        [Tooltip("Seconds after a landing to fire the next launch before the combo (and its orange extra) is lost.")]
        public float comboWindowSeconds = 2f;
        [Tooltip("How far down the combo meter moves so its circle clears the energy meter.")]
        public float comboMeterDropPixels = 44f;
        public Color comboMeterColor = new Color(1f, 0.62f, 0.1f); // matches the pound-boost orange

        [Header("3 - Overcharge scatter")]
        [Tooltip("Scatter cone radius (degrees) at full charge.")]
        public float scatterMaxAngle = 14f;
        [Tooltip("Charge fraction where the cone starts opening.")]
        [Range(0f, 1f)] public float scatterStartFraction = 0.25f;
        [Tooltip("Dots drawn around the predicted landing to visualise the scatter radius.")]
        public int scatterRingDots = 24;
        public Color scatterRingColor = new Color(1f, 0.45f, 0.15f, 0.9f);

        [Header("4 - Tuned blend")]
        public float tunedGroundedRefund = 0.5f;
        public float tunedMidairBaseRefund = 0.6f;
        public float tunedMidairSpendFactor = 0.3f;
        public float tunedAimDrainPerSecond = 0.04f;
        [Tooltip("SOLID chain bonus per quick relaunch (never revoked).")]
        public float tunedChainStep = 0.05f;
        public float tunedChainCap = 0.2f;
        public float tunedChainWindowSeconds = 3f;

        KineticCubeController controller;

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

        // Runtime UI.
        GameObject comboCircle;
        Text comboText;
        Transform scatterRingRoot;
        Transform[] scatterDots;

        bool ComboLike => currentVariant == EconomyVariant.ComboRefund || currentVariant == EconomyVariant.Tuned;
        float ActiveChainStep => currentVariant == EconomyVariant.Tuned ? tunedChainStep : comboStepPerLevel;
        float ActiveWindow => currentVariant == EconomyVariant.Tuned ? tunedChainWindowSeconds : comboWindowSeconds;

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

            BuildHudTag();
            ApplyVariant();
        }

        void OnDestroy()
        {
            if (controller == null) return;
            controller.LaunchFired -= OnLaunchFired;
            controller.CrashRegistered -= OnCrash;
        }

        void Update()
        {
            if (Time.timeScale <= 0f || controller == null) return;

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
                float refund = comboBaseRefund + comboStepPerLevel * comboCount;
                controller.groundedRefundMultiplier = refund;
                controller.midairRefundBaseMultiplier = refund;
                controller.midairRefundSpendFactor = 0f;
            }
            else if (currentVariant == EconomyVariant.Tuned)
            {
                float bonus = Mathf.Min(tunedChainStep * comboCount, tunedChainCap);
                controller.groundedRefundMultiplier = tunedGroundedRefund + bonus;
                controller.midairRefundBaseMultiplier = tunedMidairBaseRefund + bonus;
                controller.midairRefundSpendFactor = tunedMidairSpendFactor;
            }

            UpdateScatterRing();
        }

        void LateUpdate()
        {
            if (controller == null) return;
            UpdateComboUi();
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

            switch (currentVariant)
            {
                case EconomyVariant.AimDrain:
                    controller.slowdownMode = SlowdownMode.EnergyTank;
                    controller.tankDrainPerSecond = aimDrainPerSecond;
                    break;

                case EconomyVariant.ComboRefund:
                    controller.slowdownMode = SlowdownMode.Unlimited;
                    break;

                case EconomyVariant.OverchargeScatter:
                    controller.slowdownMode = SlowdownMode.Unlimited;
                    controller.launchScatterMaxAngle = scatterMaxAngle;
                    controller.launchScatterStartFraction = scatterStartFraction;
                    break;

                case EconomyVariant.Tuned:
                    controller.slowdownMode = SlowdownMode.EnergyTank;
                    controller.tankDrainPerSecond = tunedAimDrainPerSecond;
                    break;
            }

            if (hudLabel != null) hudLabel.text = CurrentLabel;
            RefreshMeterLayout();
        }

        string CurrentLabel => currentVariant switch
        {
            EconomyVariant.AimDrain => "Economy 1 - Aim drain",
            EconomyVariant.ComboRefund => "Economy 2 - Combo refunds",
            EconomyVariant.OverchargeScatter => "Economy 3 - Overcharge scatter",
            EconomyVariant.Tuned => "Economy 4 - Tuned blend",
            _ => "Economy ?",
        };

        // ---------- Combo machinery (variants 2 and 4) ----------

        void OnLaunchFired()
        {
            if (!ComboLike) return;
            if (windowRemaining > 0f) chainInFlight = true; // chain stays alive through the flight
        }

        void OnCrash(Vector3 position)
        {
            if (!ComboLike) return;

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
            chainInFlight = false;
            windowRemaining = ActiveWindow;
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
                        // x0.7, x0.8, x0.9, x1, x1.1 ...
                        float multiplier = currentVariant == EconomyVariant.Tuned
                            ? tunedMidairBaseRefund + Mathf.Min(tunedChainStep * comboCount, tunedChainCap)
                            : comboBaseRefund + comboStepPerLevel * comboCount;
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

        // ---------- Scatter ring (variant 3) ----------

        void UpdateScatterRing()
        {
            bool show = currentVariant == EconomyVariant.OverchargeScatter
                && controller.IsAimingOrCharging
                && controller.HasValidPredictedLanding;

            float cone = show ? controller.ScatterConeAngleFor(controller.CurrentChargeFraction) : 0f;
            show = show && cone > 0.05f;

            if (!show)
            {
                if (scatterRingRoot != null) scatterRingRoot.gameObject.SetActive(false);
                return;
            }

            if (scatterRingRoot == null) BuildScatterRing();
            scatterRingRoot.gameObject.SetActive(true);

            // Lateral scatter at the landing grows with distance and cone angle.
            Vector3 landing = controller.LastPredictedLanding;
            float distance = Vector3.Distance(controller.transform.position, landing);
            float radius = Mathf.Tan(cone * Mathf.Deg2Rad) * distance;

            for (int i = 0; i < scatterDots.Length; i++)
            {
                float angle = i / (float)scatterDots.Length * Mathf.PI * 2f;
                scatterDots[i].position = landing + new Vector3(Mathf.Cos(angle) * radius, 0.08f, Mathf.Sin(angle) * radius);
                scatterDots[i].localScale = Vector3.one * Mathf.Clamp(radius * 0.06f, 0.12f, 0.5f);
            }
        }

        void BuildScatterRing()
        {
            scatterRingRoot = new GameObject("ScatterRing").transform;
            scatterDots = new Transform[Mathf.Max(scatterRingDots, 8)];

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material dotMaterial = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            dotMaterial.color = scatterRingColor;

            for (int i = 0; i < scatterDots.Length; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "ScatterDot" + i;
                Destroy(dot.GetComponent<Collider>());
                dot.GetComponent<Renderer>().sharedMaterial = dotMaterial;
                dot.transform.SetParent(scatterRingRoot, false);
                scatterDots[i] = dot.transform;
            }
        }
    }
}
