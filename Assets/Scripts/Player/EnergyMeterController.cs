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
        public Image chargeFillImage;
        // Orange preview bar sitting BEHIND the yellow fill, showing current energy PLUS the
        // ground pound's still-unclaimed boost extra - so only the extra portion pokes out
        // past the yellow, in orange, until it's claimed or forfeited.
        public Image bonusFillImage;

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
