using UnityEngine;
using KineticEnergy.Camera;

namespace KineticEnergy.Level
{
    // Points the third-person camera's initial orbit yaw from player toward lookAtPoint the
    // instant the level loads, so the camera starts behind the player looking at the level's
    // first point of interest. Awake() runs before ThirdPersonOrbitCamera.Start() (Unity
    // always runs every Awake() in the scene before any Start()), so SetInitialYaw always
    // wins over the camera's own offset-based auto-calculation.
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
