using UnityEngine;

namespace KineticEnergy.Player
{

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
