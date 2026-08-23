using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Player
{

    public class EnergyMeterController : MonoBehaviour
    {
        public Image energyFillImage;
        [Tooltip("Fill colour while the player is launch-locked after an enemy hit.")]
        public Color lockedColor = new Color(0.9f, 0.15f, 0.1f);
        [Tooltip("How much the yellow fill grows at the peak of each lock pulse (1 = no growth).")]
        public float lockPulseScale = 1.08f;
        [Tooltip("Grow-and-shrink pulses per second while locked.")]
        public float lockPulsesPerSecond = 4f;
        public Image chargeFillImage;

        public Image bonusFillImage;

        bool lockActive;
        float lockStartTime;
        Color normalEnergyColor;
        Vector3 normalScale;
        Vector2 normalPosition;

        public void SetLaunchLocked(bool locked)
        {
            if (energyFillImage == null) return;

            RectTransform rect = energyFillImage.rectTransform;
            if (locked)
            {
                if (!lockActive)
                {
                    lockActive = true;
                    lockStartTime = Time.unscaledTime;
                    normalEnergyColor = energyFillImage.color;
                    normalScale = rect.localScale;
                    normalPosition = rect.anchoredPosition;
                }
                energyFillImage.color = lockedColor;
                float wave = Mathf.Abs(Mathf.Sin((Time.unscaledTime - lockStartTime) * Mathf.PI * lockPulsesPerSecond));
                float scale = Mathf.Lerp(1f, lockPulseScale, wave);
                rect.localScale = normalScale * scale;

                Vector2 filledCentre = new Vector2(
                    (energyFillImage.fillAmount * 0.5f - rect.pivot.x) * rect.rect.width,
                    (0.5f - rect.pivot.y) * rect.rect.height);
                rect.anchoredPosition = normalPosition + filledCentre * (1f - scale);
            }
            else if (lockActive)
            {
                lockActive = false;
                energyFillImage.color = normalEnergyColor;
                rect.localScale = normalScale;
                rect.anchoredPosition = normalPosition;
            }
        }

        public void SetVisible(bool visible)
        {
            GameObject container = energyFillImage != null && energyFillImage.transform.parent != null
                ? energyFillImage.transform.parent.gameObject
                : gameObject;
            if (container.activeSelf != visible) container.SetActive(visible);
        }

        public void SetBonus(float totalFraction, bool visible)
        {
            if (bonusFillImage == null) return;
            bonusFillImage.gameObject.SetActive(visible);
            bonusFillImage.fillAmount = visible ? Mathf.Clamp01(totalFraction) : 0f;
        }

        public void SetEnergy(float fraction)
        {
            if (energyFillImage != null) energyFillImage.fillAmount = Mathf.Clamp01(fraction);
        }

        UnityEngine.UI.Image requirementTick;

        [System.NonSerialized] public float displaySpan = 1f;
        RectTransform premiumRect;

        Image premiumEnergyFill;
        bool premiumEnergyResolved;
        Color normalChargeColor;
        bool chargeColorCaptured;
        Image premiumChargeFill;
        Color normalPremiumChargeColor;
        bool premiumChargeResolved;

        Image ResolvePremiumChargeFill()
        {
            if (premiumChargeResolved) return premiumChargeFill;
            premiumChargeResolved = true;
            Transform body = chargeFillImage != null ? chargeFillImage.transform.parent : null;
            if (body == null) return null;
            if (premiumRect == null) premiumRect = body.Find("PremiumZone") as RectTransform;
            Transform fill = premiumRect != null ? premiumRect.Find("PremiumChargeFill") : null;
            if (fill != null)
            {
                premiumChargeFill = fill.GetComponent<Image>();
                if (premiumChargeFill != null) normalPremiumChargeColor = premiumChargeFill.color;
            }
            return premiumChargeFill;
        }

        Image ResolvePremiumEnergyFill()
        {
            if (premiumEnergyResolved) return premiumEnergyFill;
            premiumEnergyResolved = true;
            Transform body = energyFillImage != null ? energyFillImage.transform.parent : null;
            if (body == null) return null;
            if (premiumRect == null) premiumRect = body.Find("PremiumZone") as RectTransform;
            Transform fill = premiumRect != null ? premiumRect.Find("PremiumBoostFill") : null;
            if (fill != null) premiumEnergyFill = fill.GetComponent<Image>();
            return premiumEnergyFill;
        }

        [Tooltip("The shared blackbody band palette, used for the CHARGE bar's heat ramp.")]
        public KineticEnergy.Level.EnergyBandPalette bandPalette;

        [Tooltip("The tank's own colour, in every state. Deliberately OFF the heat ramp: the energy bar answers 'how much have I got', which is the ceiling on what a charge can reach - it is the backdrop the hot charge bar is read against, not a competitor for the same language.")]
        public Color energyColor = new Color(0.3f, 0.65f, 1f);

        [Tooltip("The meter's frame - outline and block dividers. A mid grey rather than white: structure has to separate from BOTH the empty meter behind it and the scene's flat clear colour without reading as another lit-up value. Every meter uses this, so the energy and combo bars match by construction.")]
        public Color frameColor = new Color(0.62f, 0.62f, 0.66f, 0.9f);

        void Awake()
        {
            PaintFrame();
        }

        public void PaintFrame()
        {
            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                string n = image.name;
                if (n == "Outline" || n == "PremiumOutline"
                    || n.StartsWith("Divider") || n.StartsWith("PremiumDivider"))
                {
                    image.color = frameColor;
                }
            }
        }

        public void SetEnergyTint()
        {

            normalEnergyColor = energyColor;
            if (!lockActive && energyFillImage != null) energyFillImage.color = energyColor;

            Image premiumEnergy = ResolvePremiumEnergyFill();
            if (premiumEnergy != null) premiumEnergy.color = energyColor;
        }

        public void SetChargeTint(float chargeTankFraction, bool charging)
        {
            if (chargeFillImage == null) return;
            if (!chargeColorCaptured)
            {
                normalChargeColor = chargeFillImage.color;
                chargeColorCaptured = true;
            }

            Image premiumCharge = ResolvePremiumChargeFill();
            KineticEnergy.Level.EnergyBandPalette.Band band = charging && bandPalette != null
                ? bandPalette.GetBand(chargeTankFraction)
                : null;

            Color paint = band != null ? band.baseColor : normalChargeColor;
            chargeFillImage.color = paint;

            if (premiumCharge != null) premiumCharge.color = band != null ? paint : normalPremiumChargeColor;
        }

        public void SetRequirementTick(float fraction, Color color, bool visible)
        {
            if (requirementTick == null)
            {
                if (energyFillImage == null) return;
                Transform container = energyFillImage.transform.parent != null
                    ? energyFillImage.transform.parent
                    : energyFillImage.transform;
                GameObject go = new GameObject("RequirementTick", typeof(RectTransform));
                go.transform.SetParent(container, false);
                requirementTick = go.AddComponent<UnityEngine.UI.Image>();
                RectTransform rect = go.GetComponent<RectTransform>();

                rect.anchorMax = new Vector2(0f, 1.15f);
                rect.anchorMin = new Vector2(0f, -0.15f);
                rect.sizeDelta = new Vector2(3f, 0f);
                premiumRect = container.Find("PremiumZone") as RectTransform;
            }

            requirementTick.gameObject.SetActive(visible);
            if (!visible) return;

            requirementTick.color = color;
            RectTransform tickRect = requirementTick.rectTransform;
            RectTransform barRect = tickRect.parent as RectTransform;

            const float inset = 3f;
            float span = Mathf.Clamp(displaySpan, 0.0001f, 1f);
            float barWidth = barRect != null ? barRect.rect.width : 0f;
            float x;
            if (fraction <= span || premiumRect == null)
            {
                x = inset + Mathf.Clamp01(fraction / span) * (barWidth - inset * 2f);
            }
            else
            {
                float intoPremium = Mathf.Clamp01((fraction - span) / Mathf.Max(1f - span, 0.0001f));
                x = barWidth + inset + intoPremium * (premiumRect.rect.width - inset * 2f);
            }
            tickRect.anchorMin = new Vector2(0f, tickRect.anchorMin.y);
            tickRect.anchorMax = new Vector2(0f, tickRect.anchorMax.y);
            tickRect.anchoredPosition = new Vector2(x, 0f);
        }

        public void SetCharge(float fraction, bool visible)
        {
            if (chargeFillImage == null) return;
            chargeFillImage.gameObject.SetActive(visible);

            float displayFloor = Mathf.Min(0.05f, energyFillImage != null ? energyFillImage.fillAmount : 1f);
            chargeFillImage.fillAmount = visible ? Mathf.Max(Mathf.Clamp01(fraction), displayFloor) : 0f;
        }
    }
}

