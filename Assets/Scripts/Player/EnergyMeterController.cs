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

        public void SetEnergy(float fraction)
        {
            if (energyFillImage != null) energyFillImage.fillAmount = Mathf.Clamp01(fraction);
        }

        public void SetCharge(float fraction, bool visible)
        {
            if (chargeFillImage == null) return;
            chargeFillImage.gameObject.SetActive(visible);
            chargeFillImage.fillAmount = Mathf.Clamp01(fraction);
        }
    }
}
