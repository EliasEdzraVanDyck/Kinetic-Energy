using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // FastPacedLevel only (see KineticEnergySetup.BuildFastPacedSpiral) - direct bug report: in
    // first person, standing on (or stuck to) one of the spiral's tilted platforms puts the
    // camera right up against its surface, filling the screen with the opaque material. Fades
    // this platform out for as long as the player is actually touching it, opaque otherwise.
    // Identifies the player by component rather than tag, matching FinishLine's own reasoning -
    // nothing else in this codebase relies on Unity's separate tag system.
    [RequireComponent(typeof(Renderer))]
    public class TransparentWhenOccupied : MonoBehaviour
    {
        [Range(0f, 1f)] public float occupiedAlpha = 0.25f;

        Renderer rend;
        Color opaqueColor;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            // .material (not .sharedMaterial) clones a per-instance copy the first time it's
            // touched - every spiral platform shares the same material ASSET, so mutating alpha
            // through .material is what keeps one platform fading without affecting the rest.
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
