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

        // Floors the DISPLAYED fill at 5% the instant charging starts, rather than growing
        // invisibly from literal zero - direct request: "it should start at a say 5 percent and
        // the longer you press it should move further right". Purely cosmetic - only this display
        // value is floored, not chargeFraction/energy math anywhere else, so the actual charge (and
        // what gets spent on launch) is unaffected.
        public void SetCharge(float fraction, bool visible)
        {
            if (chargeFillImage == null) return;
            chargeFillImage.gameObject.SetActive(visible);
            chargeFillImage.fillAmount = visible ? Mathf.Max(Mathf.Clamp01(fraction), 0.05f) : 0f;
        }
    }
}
