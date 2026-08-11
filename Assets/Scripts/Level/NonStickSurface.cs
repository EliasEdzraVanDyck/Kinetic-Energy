using UnityEngine;

namespace KineticEnergy.Level
{
    // Surfaces the crash-stick system ignores COMPLETELY (KineticCubeController.RegisterCrash
    // early-outs on them): touching one neither freezes the cube nor refunds energy - momentum
    // and gravity just carry on, so the player bounces/slides off and falls away again.
    // Distinct from a missing StickySurface, which still stops the cube dead for the brief
    // cling - this never registers a crash at all.
    public class NonStickSurface : MonoBehaviour
    {
    }
}
