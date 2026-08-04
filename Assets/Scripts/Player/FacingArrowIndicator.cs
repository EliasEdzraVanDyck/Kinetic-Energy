using UnityEngine;

namespace KineticEnergy.Player
{
    // Always-flat, yaw-only directional marker shown on top of the player while StickAim is the
    // active control scheme - unlike AimArrowIndicator (full pitch+yaw, only visible while
    // actively charging a shot in the other schemes), this tracks the player's current facing
    // continuously and never tilts, regardless of KineticCubeControllerFreeMove's airborne lean
    // on the `visual` child. Parented under the player ROOT instead (see KineticEnergySetup.
    // BuildFacingArrow), which stays perfectly level (RigidbodyConstraints.FreezeRotation) no
    // matter what the visual is doing.
    public class FacingArrowIndicator : MonoBehaviour
    {
        public Color arrowColor = new Color(0.9f, 0.05f, 0.05f);
        public Transform shaft;
        public Transform head;

        void Awake()
        {
            ApplyColor();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            ApplyColor();
        }
#endif

        void ApplyColor()
        {
            ApplyColorTo(shaft);
            ApplyColorTo(head);
        }

        void ApplyColorTo(Transform t)
        {
            if (t == null) return;
            Renderer r = t.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == null) return;
            r.sharedMaterial.color = arrowColor;
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void SetFacingYaw(float yawDegrees)
        {
            transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }
    }
}
