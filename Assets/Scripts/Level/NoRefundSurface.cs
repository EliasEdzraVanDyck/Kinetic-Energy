using UnityEngine;

namespace KineticEnergy.Level
{
    // Marks a surface whose landings never refund launch energy - the crash itself still
    // registers normally (stick, launch budget, flight end), only the payout is skipped.
    // The economy harness adds/removes this on the big quarry floor at runtime for the
    // variants where a floor landing must not count as a successful landing.
    public class NoRefundSurface : MonoBehaviour
    {
    }
}
