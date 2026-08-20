using UnityEngine;

namespace KineticEnergy.Level
{

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
