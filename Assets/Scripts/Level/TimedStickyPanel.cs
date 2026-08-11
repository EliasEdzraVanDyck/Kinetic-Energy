using System.Collections.Generic;
using UnityEngine;

namespace KineticEnergy.Level
{
    // A sticky panel that only holds for a while (The Gauntlet, beat 5): crashing onto it
    // sticks the player exactly like a StickySurface, but after holdSeconds the panel lets
    // go - the controller releases the crash-stick (see KineticCubeController.RegisterCrash)
    // and the panel simultaneously drops its colliders, so the player falls even when
    // standing on the panel's flat top. The panel re-solidifies shortly after, ready for the
    // next attempt. The landing-prediction proxies mirror collider state automatically, so
    // the dotted trail never shows a landing on a panel that is currently open.
    public class TimedStickyPanel : MonoBehaviour
    {
        [Tooltip("Seconds the panel holds the player before releasing them.")]
        public float holdSeconds = 2f;
        [Tooltip("Seconds the panel stays fallen-through (colliders off) after releasing.")]
        public float openSeconds = 1.5f;
        [Tooltip("Panel color while armed.")]
        public Color armedColor = new Color(0.25f, 0.8f, 0.45f);
        [Tooltip("Color the panel blinks toward as the hold runs out.")]
        public Color warningColor = new Color(0.95f, 0.35f, 0.2f);
        [Tooltip("Panel alpha while open (fallen-through).")]
        [Range(0f, 1f)] public float openAlpha = 0.25f;

        readonly List<Collider> panelColliders = new List<Collider>();
        readonly List<Renderer> panelRenderers = new List<Renderer>();
        float holdRemaining = -1f;
        float openRemaining;

        void Awake()
        {
            GetComponentsInChildren(true, panelColliders);
            GetComponentsInChildren(true, panelRenderers);
        }

        // Called by KineticCubeController the moment the player crash-sticks to this panel.
        public void OnPlayerStuck()
        {
            holdRemaining = holdSeconds;
        }

        void Update()
        {
            if (holdRemaining >= 0f)
            {
                holdRemaining -= Time.deltaTime;
                if (holdRemaining <= 0f)
                {
                    holdRemaining = -1f;
                    openRemaining = openSeconds;
                    SetSolid(false);
                }
                else
                {
                    // Blink faster as the release approaches.
                    float urgency = 1f - Mathf.Clamp01(holdRemaining / Mathf.Max(holdSeconds, 0.01f));
                    float blink = Mathf.PingPong(Time.time * (2f + urgency * 8f), 1f);
                    SetColor(Color.Lerp(armedColor, warningColor, blink * urgency), 1f);
                }
                return;
            }

            if (openRemaining > 0f)
            {
                openRemaining -= Time.deltaTime;
                if (openRemaining <= 0f)
                {
                    SetSolid(true);
                    SetColor(armedColor, 1f);
                }
            }
        }

        void SetSolid(bool solid)
        {
            foreach (Collider col in panelColliders)
            {
                if (col != null) col.enabled = solid;
            }
            if (!solid) SetColor(armedColor, openAlpha);
        }

        void SetColor(Color color, float alpha)
        {
            color.a = alpha;
            foreach (Renderer rend in panelRenderers)
            {
                if (rend != null) rend.material.color = color;
            }
        }
    }
}
