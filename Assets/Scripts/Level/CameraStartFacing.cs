using UnityEngine;
using KineticEnergy.Camera;

namespace KineticEnergy.Level
{
    // Points the third-person camera's initial orbit yaw from player toward lookAtPoint the
    // instant the level loads, exactly like LevelGenerator.FaceCameraTowardFinish already does
    // for Level1's procedurally-placed finish platform - "camera should face behind the player
    // and look at the next platform on bootup" (direct request), pulled out into its own
    // reusable component for hand-placed levels (Level2 and on) that don't use LevelGenerator.
    // Awake() runs before ThirdPersonOrbitCamera.Start() (Unity always runs every Awake() in the
    // scene before any Start()), so SetInitialYaw always wins over the camera's own offset-based
    // auto-calculation.
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
