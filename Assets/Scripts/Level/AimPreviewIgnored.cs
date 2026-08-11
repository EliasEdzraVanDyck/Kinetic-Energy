using UnityEngine;

namespace KineticEnergy.Level
{
    // Colliders under this marker are excluded from the landing prediction: the dotted
    // trail passes straight through them and the reticle/camera never focus on them. Used
    // by the Quarry's invisible boundary - a landing marker floating on empty sky reads as
    // broken, and the boundary is a catch-net, not a place to aim at. The surface is still
    // physically solid; a real flight that reaches it clings briefly and drops back in.
    public class AimPreviewIgnored : MonoBehaviour
    {
    }
}
