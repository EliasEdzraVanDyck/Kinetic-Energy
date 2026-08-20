using UnityEngine;
using KineticEnergy.Camera;

namespace KineticEnergy.Level
{

    public class CameraStartFacing : MonoBehaviour
    {
        public Transform player;
        public ThirdPersonOrbitCamera cameraOrbit;
        public Transform lookAtPoint;

        void Awake()
        {
            if (player == null || cameraOrbit == null || lookAtPoint == null) return;

            Vector3 direction = lookAtPoint.position - player.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            cameraOrbit.SetInitialYaw(yaw);
        }
    }
}
