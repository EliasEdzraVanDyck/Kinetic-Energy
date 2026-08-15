using UnityEngine;

namespace KineticEnergy.Level
{
    // The armoured flyer: identical to FlyingEnemy in every behaviour and value, except a
    // launch only kills it through the cube on its back - hits anywhere else register as
    // an ordinary crash and the flyer survives.
    public class WeakSpotFlyingEnemy : FlyingEnemy
    {
        [Tooltip("The back cube's collider - the ONLY spot a launch can kill through.")]
        public Collider weakSpot;

        public override bool LaunchKillAllowedFor(Collider hitCollider)
        {
            if (weakSpot == null || hitCollider == null) return false;
            return hitCollider == weakSpot || hitCollider.transform.IsChildOf(weakSpot.transform);
        }
    }
}
