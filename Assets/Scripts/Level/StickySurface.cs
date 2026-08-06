using UnityEngine;

namespace KineticEnergy.Level
{
    // The "sticky" property for wall blocks, read by KineticCubeController when its
    // stickyWallsOnly flag is on (SlowPacedLevel's Player instance only): crashing into a wall
    // carrying this component (with sticky enabled) keeps the permanent stick-until-you-launch
    // behavior every wall has in the other levels; crashing into any wall WITHOUT it only clings
    // for nonStickyWallStickDuration seconds before dropping the cube back into gravity.
    // Purely data - the controller looks it up via GetComponentInParent, so it works on the
    // block itself or on a parent container covering many blocks. Toggle `sticky` in the
    // Inspector (or add/remove the component) to change a block; the material is just a visual
    // and doesn't affect behavior.
    public class StickySurface : MonoBehaviour
    {
        public bool sticky = true;
    }
}
