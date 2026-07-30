using UnityEngine;

namespace KineticEnergy.Player
{
    public class AimArrowIndicator : MonoBehaviour
    {
        [Header("Size")]
        public float minLength = 0.6f;
        public float maxLength = 2f;
        public float shaftThickness = 0.12f;

        [Header("Color")]
        public Color arrowColor = new Color(1f, 0.85f, 0.1f);

        [Header("Parts (wired by setup)")]
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

        public void SetAim(Vector3 worldDirection, float chargeFraction01)
        {
            if (worldDirection.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(worldDirection, Vector3.up);

            float length = Mathf.Lerp(minLength, maxLength, Mathf.Clamp01(chargeFraction01));

            if (shaft != null)
            {
                shaft.localScale = new Vector3(shaftThickness, shaftThickness, length);
                shaft.localPosition = new Vector3(0f, 0f, length * 0.5f);
            }

            if (head != null)
            {
                head.localPosition = new Vector3(0f, 0f, length + 0.15f);
            }
        }
    }
}
