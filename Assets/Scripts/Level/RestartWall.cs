using UnityEngine;

namespace KineticEnergy.Level
{
    // The "restart wall" tag, as a marker component (matching StickySurface/NonStickSurface's
    // pattern - components instead of Unity tags, so it works on any collider or a parent
    // container via GetComponentInParent): the moment the player touches a collider carrying
    // this, the level reloads. Checked by KineticCubeController.OnCollisionEnter before every
    // other crash guard, so a grounded walk-in restarts just as reliably as a mid-flight crash.
    public class RestartWall : MonoBehaviour
    {
    }
}
