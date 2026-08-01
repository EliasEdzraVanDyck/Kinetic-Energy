using UnityEngine;

namespace KineticEnergy.Level
{
    public class Billboard : MonoBehaviour
    {
        public Transform target;

        void LateUpdate()
        {
            if (target == null) return;
            transform.rotation = target.rotation;
        }
    }
}
