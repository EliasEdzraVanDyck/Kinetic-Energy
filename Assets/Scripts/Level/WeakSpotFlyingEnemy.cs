using UnityEngine;

namespace KineticEnergy.Level
{
    // A flyer that only dies to a launch landing on its back cube. Anything else registers
    // as an ordinary crash and the flyer survives - staggered, but alive.
    public class WeakSpotFlyingEnemy : FlyingEnemy
    {
        [Tooltip("The back cube's collider - the ONLY spot a launch can kill through.")]
        public Collider weakSpot;

        [Header("Weak spot tell")]
        // The spot is ALWAYS the kill window on this enemy, so unlike the hunter - whose
        // body pulses only while punishable - the tell simply never stops. Same read
        // though: a pulse toward white says "this is what you hit".
        [Tooltip("Colour the weak spot pulses toward, so it reads as the vulnerable part at a glance.")]
        public Color pulseColor = Color.white;
        [Tooltip("Pulses per second of the weak spot's tell. Slower than the hunter's body pulse - this one never stops, so it reads as a steady beacon rather than an urgent flicker.")]
        public float pulseSpeed = 1.6f;
        [Tooltip("How far toward the pulse colour it travels (0 = no tell, 1 = fully white at the peak).")]
        [Range(0f, 1f)] public float pulseAmount = 0.45f;

        Renderer spotRenderer;
        Color spotRestColor;

        void Awake()
        {
            if (weakSpot == null) return;
            spotRenderer = weakSpot.GetComponent<Renderer>();
            if (spotRenderer == null) spotRenderer = weakSpot.GetComponentInChildren<Renderer>();
            // Reading .material instances a per-renderer copy, so the shared weak-spot
            // material asset is never written to.
            if (spotRenderer != null) spotRestColor = spotRenderer.material.color;
        }

        void Update()
        {
            if (spotRenderer == null) return;
            // Unscaled, so the tell keeps beating through the midair aim's bullet-time -
            // which is exactly when the player is lining the shot up.
            float pulse = Mathf.PingPong(Time.unscaledTime * pulseSpeed, pulseAmount);
            spotRenderer.material.color = Color.Lerp(spotRestColor, pulseColor, pulse);
        }

        // The energy-tier hook: recolours the SPOT (material and the pulse's rest colour
        // together, so the beacon breathes around the tier colour rather than snapping
        // back to the old gold).
        public void SetSpotTier(Color tierColor)
        {
            if (spotRenderer == null && weakSpot != null)
            {
                spotRenderer = weakSpot.GetComponent<Renderer>();
                if (spotRenderer == null) spotRenderer = weakSpot.GetComponentInChildren<Renderer>();
            }
            spotRestColor = tierColor;
            if (spotRenderer != null) spotRenderer.material.color = tierColor;
        }

        public override bool LaunchKillAllowedFor(Collider hitCollider)
        {
            if (weakSpot == null || hitCollider == null) return false;
            return hitCollider == weakSpot || hitCollider.transform.IsChildOf(weakSpot.transform);
        }
    }
}
