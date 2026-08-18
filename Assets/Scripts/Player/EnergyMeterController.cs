using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Player
{
    // Yellow energy / blue charge-preview meter, top-right corner - wired by KineticEnergySetup,
    // driven every frame from KineticCubeController.Update(). Both Images use Image.Type.Filled
    // (Horizontal, origin Left) so SetEnergy/SetCharge just move a single fillAmount each; the
    // blue bar sits on the same rect as the yellow one so it always reads as "this much of my
    // current energy is what my charge is about to spend" (direct request: it should never look
    // bigger than the yellow bar behind it - true by construction since chargeFraction is itself
    // capped by available energy, see KineticCubeController.AccumulateCharge).
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
        // Orange preview bar sitting BEHIND the yellow fill, showing current energy PLUS the
        // ground pound's still-unclaimed boost extra - so only the extra portion pokes out
        // past the yellow, in orange, until it's claimed or forfeited.
        public Image bonusFillImage;

        bool lockActive;
        float lockStartTime;
        Color normalEnergyColor;
        Vector3 normalScale;
        Vector2 normalPosition;

        // Launch-lock feedback (enemy hit): the YELLOW fill alone turns red and gently
        // pulses - grows and shrinks back - until launching is available again. Driven
        // every frame from KineticCubeController alongside SetEnergy.
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
                // Scaling happens around the rect's pivot; shift the rect so the fixed point
                // of the pulse is the CENTRE of the currently filled portion of the bar
                // (fill is Horizontal/Left, so that centre sits at fillAmount/2 of the width).
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

        // Shows/hides the whole meter (the UI container the fill bars live in) - used to
        // disable a meter whose corresponding mode is off (e.g. energy meter under infinite
        // energy, slowdown bar outside AimBudget mode).
        public void SetVisible(bool visible)
        {
            GameObject container = energyFillImage != null && energyFillImage.transform.parent != null
                ? energyFillImage.transform.parent.gameObject
                : gameObject;
            if (container.activeSelf != visible) container.SetActive(visible);
        }

        // totalFraction = energy + pending boost extra; drawn behind the yellow fill (see
        // the field), so the visible orange sliver is exactly the extra on offer.
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

        // The requirement tick: a thin vertical marker at the fraction an aimed-at
        // interactable demands, in that tier's colour - "do I have enough?" becomes a
        // spatial comparison against the fill edge instead of a colour-to-number lookup,
        // which is what the half-second bullet-time aim window allows for. Built lazily,
        // hidden whenever nothing relevant is aimed at.
        UnityEngine.UI.Image requirementTick;
        Color normalChargeColor;
        bool chargeColorCaptured;

        // What TANK fraction the MAIN bar's width stands for. The premium meter variants
        // hand everything above this to a separate row of taller blocks - the 4+6 meter
        // draws 0..40% normally and 40..100% tall - so anything positioned by tank fraction
        // has to be routed to the right region first. Plain meters leave it at 1.
        [System.NonSerialized] public float displaySpan = 1f;
        RectTransform premiumRect; // the tall segment, when this meter variant has one

        // The tall segment draws the charge ABOVE the main bar's span - on the 4+6 meter
        // that is everything past 40%, so most of the charge's travel. It has to carry the
        // same colour as the main charge bar or the tier read dies at the segment seam.
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

        [Tooltip("Tier palette the CHARGE bar is coloured from while charging: a charge of 0-19% of the tank wears the first tier's colour, 20-39% the second, and so on. Empty = the charge bar keeps its own colour.")]
        public KineticEnergy.Level.EnergyTierPalette tierPalette;

        // Only the CHARGE - the slice this launch is about to spend - carries the tier
        // language, and only while it is being built. The tank's own fills keep their
        // standing meaning at all times: yellow is energy you have, orange is the boost
        // riding on top. Fed the charge in TANK units, so the band it reports is directly
        // comparable to what an interactable demands.
        public void SetChargeTint(float chargeTankFraction, bool charging)
        {
            if (chargeFillImage == null) return;
            if (!chargeColorCaptured)
            {
                normalChargeColor = chargeFillImage.color;
                chargeColorCaptured = true;
            }

            Image premiumCharge = ResolvePremiumChargeFill();

            if (!charging || tierPalette == null || tierPalette.tiers == null || tierPalette.tiers.Length == 0)
            {
                chargeFillImage.color = normalChargeColor;
                if (premiumCharge != null) premiumCharge.color = normalPremiumChargeColor;
                return;
            }

            // The tier this charge can AFFORD, not the band it sits in - so charging up to
            // an enemy's price turns the bar that enemy's own colour, and "the bar matches
            // the target" is the tell that the launch will kill.
            KineticEnergy.Level.EnergyTierPalette.Tier afforded = tierPalette.TierAfforded(chargeTankFraction);
            if (afforded == null)
            {
                chargeFillImage.color = normalChargeColor;
                if (premiumCharge != null) premiumCharge.color = normalPremiumChargeColor;
                return;
            }
            Color banded = afforded.baseColor;
            chargeFillImage.color = banded;
            // Both halves of the same bar: the charge reads as one continuous colour across
            // the seam, so passing 40% is not mistaken for a change of meaning.
            if (premiumCharge != null) premiumCharge.color = banded;
        }

        // The threshold marker itself. The charge bar's own colour is handled by
        // SetChargeTint - the two meet when the growing charge reaches this tick and turns
        // the tier's colour, which is the whole read: length says how far, colour says which
        // tier, and the tick says where the line is.
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
                // Anchored to the bar's LEFT edge and placed by pixel offset, so the marker
                // can travel past the main blocks and into the tall segment beyond them.
                // Slightly taller than the bar, so it reads as a marker ON it, not a slice OF it.
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

            // Tank fraction -> a pixel offset from the bar's left edge, routed to whichever
            // region actually draws that fraction. On the 4+6 meter a 60% mark belongs two
            // tall blocks into the segment, not pinned to the end of the fourth small one.
            const float inset = 3f; // the fills sit 3px inside their frames
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

        // Floors the DISPLAYED fill at 5% the instant charging starts, rather than growing
        // invisibly from literal zero - direct request: "it should start at a say 5 percent and
        // the longer you press it should move further right". Purely cosmetic - only this display
        // value is floored, not chargeFraction/energy math anywhere else, so the actual charge (and
        // what gets spent on launch) is unaffected.
        public void SetCharge(float fraction, bool visible)
        {
            if (chargeFillImage == null) return;
            chargeFillImage.gameObject.SetActive(visible);
            // The visibility floor must never OVERSTATE: with the tank drained below 5%
            // (the economy variants' drains can do that), the floored blue poked past the
            // yellow, promising energy that isn't there - so the floor is capped by the
            // actual energy fill behind it.
            float displayFloor = Mathf.Min(0.05f, energyFillImage != null ? energyFillImage.fillAmount : 1f);
            chargeFillImage.fillAmount = visible ? Mathf.Max(Mathf.Clamp01(fraction), displayFloor) : 0f;
        }
    }
}
