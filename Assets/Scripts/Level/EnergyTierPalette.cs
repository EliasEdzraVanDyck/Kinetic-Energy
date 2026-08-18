using UnityEngine;

namespace KineticEnergy.Level
{
    // The single source of truth for the energy-requirement colour tiers. Interactables
    // reference this asset and declare a percentage; they never store their own colours,
    // so retuning the whole language is one asset edit.
    //
    // Constraint the ramp honours: yellow = base energy, orange = boost energy, red =
    // damage - so requirements live on a cyan -> magenta ramp, with lightness climbing
    // monotonically (brighter always means "needs more"), and the emission intensity
    // carrying most of the signal under bloom.
    [CreateAssetMenu(menuName = "Kinetic Energy/Energy Tier Palette")]
    public class EnergyTierPalette : ScriptableObject
    {
        [System.Serializable]
        public class Tier
        {
            public int percent = 20;
            public Color baseColor = Color.cyan;
            [Tooltip("HDR emission intensity - the ramp's main carrier under bloom.")]
            public float emissionIntensity = 1f;
            [Tooltip("Only the top tier animates - the pulse is the disambiguator at distance.")]
            public bool pulses = false;
        }

        public Tier[] tiers =
        {
            new Tier { percent = 20,  baseColor = new Color(0.106f, 0.420f, 0.408f), emissionIntensity = 0.5f },              // #1B6B68 dark teal
            new Tier { percent = 40,  baseColor = new Color(0.165f, 0.561f, 0.831f), emissionIntensity = 1.0f },              // #2A8FD4 azure
            new Tier { percent = 60,  baseColor = new Color(0.361f, 0.361f, 0.878f), emissionIntensity = 1.8f },              // #5C5CE0 indigo-violet
            new Tier { percent = 80,  baseColor = new Color(0.725f, 0.247f, 0.910f), emissionIntensity = 3.0f },              // #B93FE8 magenta
            new Tier { percent = 100, baseColor = new Color(1.000f, 0.361f, 0.824f), emissionIntensity = 5.0f, pulses = true }, // #FF5CD2 hot magenta
        };

        // The tier a given amount of energy can AFFORD: the highest one whose price it
        // covers. This is the counterpart of TierFor - charge to exactly what an
        // interactable demands and this returns that interactable's own tier, so the charge
        // bar's colour and the target's colour become the same colour at the moment the
        // launch becomes lethal. Below the cheapest tier it reports NULL - a sub-20% charge
        // affords nothing, and showing the teal anyway made 0-19% and 20-39% read as the
        // same band (direct report). The caller shows its own neutral colour instead, so
        // every 20% step has a distinct look: neutral, then one tier colour each.
        public Tier TierAfforded(float fraction)
        {
            if (tiers == null || tiers.Length == 0) return null;
            Tier best = null;
            for (int i = 0; i < tiers.Length; i++)
            {
                if (fraction * 100f >= tiers[i].percent - 0.0001f) best = tiers[i];
            }
            return best;
        }

        // The tier at or above the requirement - an off-table value maps to the next tier
        // up rather than silently reading as cheaper than it is.
        public Tier TierFor(int percent)
        {
            Tier best = tiers.Length > 0 ? tiers[tiers.Length - 1] : null;
            for (int i = tiers.Length - 1; i >= 0; i--)
            {
                if (tiers[i].percent >= percent) best = tiers[i];
                else break;
            }
            return best;
        }
    }
}
