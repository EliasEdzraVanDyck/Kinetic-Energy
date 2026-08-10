using UnityEngine;

namespace KineticEnergy.Level
{
    // The Target prefab (Assets/Prefabs/Target.prefab): a sphere that is DESTROYED when the
    // player crashes into it. The crash itself is completely ordinary - it stops you dead and
    // refunds energy by whatever rules the scene uses, exactly like any platform - and then
    // the sphere disappears, leaving you hanging where it was: launch again to carry on, or
    // wait out the usual wall-cling and drop (see KineticCubeController.RegisterCrash, which
    // always arms that release timer for a target, since there is no longer any surface left
    // to rest against).
    public class LaunchTarget : MonoBehaviour
    {
        [Tooltip("Seconds before the sphere is removed - 0 destroys it the moment it's hit.")]
        public float destroyDelay = 0f;

        bool hit;

        public void Hit()
        {
            if (hit) return;
            hit = true;
            if (destroyDelay > 0f) Destroy(gameObject, destroyDelay);
            else Destroy(gameObject);
        }
    }
}
