using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Player
{

    public class EnergyMeterController : MonoBehaviour
    {
        public Image energyFillImage;
        public Image chargeFillImage;

        public Image bonusFillImage;

        public Color chargeColor = new Color(0.3f, 0.65f, 1f);
        public Color insufficientChargeColor = new Color(0.9f, 0.2f, 0.2f);

        public void SetChargeWarning(bool insufficient)
        {
            if (chargeFillImage != null) chargeFillImage.color = insufficient ? insufficientChargeColor : chargeColor;
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

        public void SetCharge(float fraction, bool visible)
        {
            if (chargeFillImage == null) return;
            chargeFillImage.gameObject.SetActive(visible);
            chargeFillImage.fillAmount = visible ? Mathf.Max(Mathf.Clamp01(fraction), 0.05f) : 0f;
        }
    }
}
