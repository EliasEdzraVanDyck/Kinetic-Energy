using UnityEngine;

namespace KineticEnergy.Level
{
    // Marks the PositioningObject prefab (blue transparent sphere) as a valid AIM TARGET for
    // the Automatic Energy mode: its trigger collider never collides with the player, but the
    // auto-aim raycast accepts trigger hits carrying this component, so aiming at the sphere
    // makes the auto-charge solve for a trajectory reaching that point in space.
    public class PositioningTarget : MonoBehaviour
    {
    }
}
