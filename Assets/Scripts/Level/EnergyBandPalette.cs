using UnityEngine;

namespace KineticEnergy.Level
{
    // The energy-requirement colour language: BLACKBODY HEAT. Cooler = less energy,
    // white-hot = maximum. Exactly five BANDS (ranges, not thresholds), shared by the
    // player's energy meter (colour = the band the CURRENT energy falls in) and every
    // interactable (colour = the band its REQUIREMENT falls in) - both resolve through
    // GetBand, so affordability is read by comparing temperature: "is my meter at least
    // as hot as that object?"
    //
    // Base colour and emission intensity are separate on purpose: the emission ramp
    // reinforces the lightness ramp under bloom (brighter = more energy), and the
    // ordering depends on those intensities - do not flatten them.
    //
    // The known collision with the meter's yellow/orange and the DamageWalls' red is
    // resolved by TREATMENT, not hue: interactables are always emissive and bloomed in
    // world space; the meter is always flat and unlit on the HUD; DamageWalls are a deep
    // unlit red so the emissive/non-emissive split separates them from band 1.
    [CreateAssetMenu(menuName = "Kinetic Energy/Energy Band Palette")]
    public class EnergyBandPalette : ScriptableObject
    {
        [System.Serializable]
        public class Band
        {
            public string name = "band";
            public int minPercent;
            public int maxPercent;
            public Color baseColor = Color.white;
            [Tooltip("UNUSED while the language is flat-coloured. Kept as the authored ramp in case the emissive treatment is ever restored - bloom made objects read as washed-out versions of their own band, which broke the meter-vs-object comparison.")]
            public float emissionIntensity = 1f;
        }

        public Band[] bands =
        {
            new Band { name = "ember",         minPercent = 0,  maxPercent = 19,  baseColor = new Color(1f, 0.420f, 0.169f), emissionIntensity = 1.0f }, // #FF6B2B
            new Band { name = "amber",         minPercent = 20, maxPercent = 39,  baseColor = new Color(1f, 0.584f, 0f),     emissionIntensity = 2.0f }, // #FF9500
            new Band { name = "golden yellow", minPercent = 40, maxPercent = 59,  baseColor = new Color(1f, 0.800f, 0.122f), emissionIntensity = 3.5f }, // #FFCC1F
            // Bands 4 and 5 pulled DOWN from #FFE9A8 / pure white - at full brightness they
            // glared against everything else on screen. Lightness still climbs across all
            // five (0.78 -> 0.85 -> 0.94), which is the ordering the whole ramp rests on.
            new Band { name = "pale gold",     minPercent = 60, maxPercent = 79,  baseColor = new Color(0.949f, 0.851f, 0.561f), emissionIntensity = 5.5f }, // #F2D98F
            new Band { name = "white-hot",     minPercent = 80, maxPercent = 100, baseColor = new Color(1f, 0.941f, 0.800f),     emissionIntensity = 9.0f }, // #FFF0CC
        };

        // THE one lookup, used by the meter and the interactables alike so the two can
        // never drift apart. Hard-edged banding: the answer SNAPS at each boundary -
        // never lerp between band colours, the discrete jump is the signal.
        public Band GetBand(float percent01)
        {
            if (bands == null || bands.Length == 0) return null;
            float percent = Mathf.Clamp01(percent01) * 100f;
            for (int i = 0; i < bands.Length; i++)
            {
                // The +1 closes the gaps between integer bounds (19|20): each band owns
                // everything below the NEXT band's floor.
                if (percent <= bands[i].maxPercent + (i < bands.Length - 1 ? 0.9999f : 0f)) return bands[i];
            }
            return bands[bands.Length - 1];
        }

        // 1-based band ordinal, for the countable pip row (band 3 = three pips).
        public int BandIndex(Band band)
        {
            for (int i = 0; i < bands.Length; i++)
            {
                if (bands[i] == band) return i + 1;
            }
            return 1;
        }
    }
}
