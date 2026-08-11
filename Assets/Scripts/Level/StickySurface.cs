using UnityEngine;

namespace KineticEnergy.Level
{
    // The "sticky" property for walls and ceilings, read by KineticCubeController when a
    // crash lands on a non-flat surface: a surface carrying this component (with sticky on)
    // holds the crash-stick until the next launch; any surface WITHOUT it only clings for
    // nonStickyWallStickDuration seconds before dropping the cube back into gravity.
    // Near-flat ground is always walkable either way.
    // Purely data - the controller looks it up via GetComponentInParent, so it works on the
    // block itself or on a parent container covering many blocks. The material is just a
    // visual and doesn't affect behavior.
    public class StickySurface : MonoBehaviour
    {
        public bool sticky = true;
    }
}
