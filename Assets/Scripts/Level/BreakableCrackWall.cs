using UnityEngine;

namespace KineticEnergy.Level
{

    public class BreakableCrackWall : MonoBehaviour
    {
        public void Smash()
        {
            Destroy(gameObject);
        }
    }
}
