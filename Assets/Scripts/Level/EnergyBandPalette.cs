using UnityEngine;

namespace KineticEnergy.Level
{

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
            new Band { name = "ember",         minPercent = 0,  maxPercent = 19,  baseColor = new Color(1f, 0.420f, 0.169f), emissionIntensity = 1.0f },
            new Band { name = "amber",         minPercent = 20, maxPercent = 39,  baseColor = new Color(1f, 0.584f, 0f),     emissionIntensity = 2.0f },
            new Band { name = "golden yellow", minPercent = 40, maxPercent = 59,  baseColor = new Color(1f, 0.800f, 0.122f), emissionIntensity = 3.5f },

            new Band { name = "pale gold",     minPercent = 60, maxPercent = 79,  baseColor = new Color(0.949f, 0.851f, 0.561f), emissionIntensity = 5.5f },
            new Band { name = "white-hot",     minPercent = 80, maxPercent = 100, baseColor = new Color(1f, 0.941f, 0.800f),     emissionIntensity = 9.0f },
        };

        public Band GetBand(float percent01)
        {
            if (bands == null || bands.Length == 0) return null;
            float percent = Mathf.Clamp01(percent01) * 100f;
            for (int i = 0; i < bands.Length; i++)
            {

                if (percent <= bands[i].maxPercent + (i < bands.Length - 1 ? 0.9999f : 0f)) return bands[i];
            }
            return bands[bands.Length - 1];
        }

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

