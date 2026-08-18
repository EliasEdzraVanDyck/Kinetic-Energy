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

        [Tooltip("Tier palette the fill is coloured from: 0-19% wears the first tier's colour, 20-39% the second, and so on. Empty = the fill keeps its own colour.")]
        public KineticEnergy.Level.EnergyTierPalette tierPalette;

        // The bar's own colour reports which 20% band the tank is sitting in, so the tier
        // you can currently afford is readable without aiming at anything. Fed the RAW tank
        // fraction - not the display-scaled one the fills use.
        public void SetEnergyTint(float tankFraction)
        {
            if (tierPalette == null || tierPalette.tiers == null || tierPalette.tiers.Length == 0) return;

            int band = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(tankFraction) * 5f), 0, tierPalette.tiers.Length - 1);
            Color banded = tierPalette.tiers[band].baseColor;

            // Kept in step so releasing a launch lock restores the CURRENT band rather than
            // whatever colour happened to be showing when the lock started.
            normalEnergyColor = banded;
            if (!lockActive && energyFillImage != null) energyFillImage.color = banded;
            // On the split meters most of the range is drawn by the tall segment's own fill,
            // so the bar would otherwise only ever show the first two bands.
            if (premiumRect != null)
            {
                Transform premiumFill = premiumRect.Find("PremiumBoostFill");
                Image premiumImage = premiumFill != null ? premiumFill.GetComponent<Image>() : null;
                if (premiumImage != null) premiumImage.color = banded;
            }
        }

        // projectedSpend = what the launch would actually pay if fired right now. The CHARGE
        // bar - the one that grows while you hold - turns the tier's colour the instant that
        // spend covers the requirement, so the answer to "is this enough?" is the colour of
        // the bar you are already watching, not a number to compare. The tick stays as the
        // exact threshold, and the two agree by construction: the blue turns as it passes it.
        public void SetRequirementTick(float fraction, Color color, bool visible, float projectedSpend)
        {
            if (chargeFillImage != null)
            {
                if (!chargeColorCaptured)
                {
                    normalChargeColor = chargeFillImage.color;
                    chargeColorCaptured = true;
                }
                if (!visible)
                {
                    chargeFillImage.color = normalChargeColor;
                }
                else
                {
                    // The bar wears the REQUIREMENT'S colour the whole time it is aimed at,
                    // so the tier is readable before the charge gets there; it just sits
                    // darkened until the spend actually covers the price, then snaps to
                    // full strength. Colour = what this costs, brightness = can I pay it.
                    bool affordable = projectedSpend >= Mathf.Clamp01(fraction) - 0.0001f;
                    chargeFillImage.color = affordable
                        ? color
                        : new Color(color.r * 0.4f, color.g * 0.4f, color.b * 0.4f, color.a);
                }
            }

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
