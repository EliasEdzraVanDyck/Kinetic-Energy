using UnityEngine;

namespace KineticEnergy.Level
{
    // The breakable crack pane (Assets/Prefabs/BreakableCrackWall.prefab): a flat, light-beige
    // slab - a wall rotated 90 degrees to lie horizontal - with a big crack drawn across its
    // top. Solid to everything EXCEPT a downward launch: KineticCubeController.OnCollisionEnter
    // checks for this marker while in a downward flight and calls Smash, restoring the cube's
    // pre-impact velocity so the slam punches straight through. Any other touch treats it as an
    // ordinary solid surface (normal crash/cling rules for the scene). The landing-prediction
    // proxies clean themselves up when the pane is destroyed (SyncPredictionGeometry), so the
    // dotted line stops showing a floor that no longer exists.
    public class BreakableCrackWall : MonoBehaviour
    {
        public void Smash()
        {
            Destroy(gameObject);
        }
    }
}
