using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

    [RequireComponent(typeof(Renderer))]
    public class TransparentWhenOccupied : MonoBehaviour
    {
        [Range(0f, 1f)] public float occupiedAlpha = 0.25f;

        Renderer rend;
        Color opaqueColor;

        void Awake()
        {
            rend = GetComponent<Renderer>();

            opaqueColor = rend.material.color;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.GetComponent<KineticCubeController>() == null) return;
            SetAlpha(occupiedAlpha);
        }

        void OnCollisionExit(Collision collision)
        {
            if (collision.collider.GetComponent<KineticCubeController>() == null) return;
            SetAlpha(opaqueColor.a);
        }

        void SetAlpha(float alpha)
        {
            Color c = opaqueColor;
            c.a = alpha;
            rend.material.color = c;
        }
    }
}
