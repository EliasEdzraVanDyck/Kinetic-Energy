using UnityEngine;

namespace KineticEnergy.Level
{

    public class WeakSpotFlyingEnemy : FlyingEnemy
    {
        [Tooltip("The back cube's collider - the ONLY spot a launch can kill through.")]
        public Collider weakSpot;
        [Tooltip("How far the kill collider grows WHILE STUNNED - the visual never changes, only the hitbox, and only for the stagger. A staggered flyer is the follow-up opportunity, so the spot is easier to hit for exactly as long as that opening lasts. 1 = never grows.")]
        public float weakSpotColliderScale = 1.35f;

        [Header("Weak spot tell")]

        [Tooltip("Colour the weak spot pulses toward, so it reads as the vulnerable part at a glance.")]
        public Color pulseColor = Color.white;
        [Tooltip("Pulses per second of the weak spot's tell. Slower than the hunter's body pulse - this one never stops, so it reads as a steady beacon rather than an urgent flicker.")]
        public float pulseSpeed = 1.6f;
        [Tooltip("How far toward the pulse colour it travels (0 = no tell, 1 = fully white at the peak).")]
        [Range(0f, 1f)] public float pulseAmount = 0.45f;

        Renderer spotRenderer;
        Color spotRestColor;

        Vector3 spotBoxSize;
        float spotSphereRadius;
        bool spotWidened;

        void Awake()
        {
            if (weakSpot == null) return;
            if (weakSpot is BoxCollider box) spotBoxSize = box.size;
            else if (weakSpot is SphereCollider sphere) spotSphereRadius = sphere.radius;
            spotRenderer = weakSpot.GetComponent<Renderer>();
            if (spotRenderer == null) spotRenderer = weakSpot.GetComponentInChildren<Renderer>();

            if (spotRenderer != null) spotRestColor = spotRenderer.material.color;
        }

        void Update()
        {

            SetSpotWidened(IsStunned);

            if (spotRenderer == null) return;

            float pulse = Mathf.PingPong(Time.unscaledTime * pulseSpeed, pulseAmount);
            spotRenderer.material.color = Color.Lerp(spotRestColor, pulseColor, pulse);
        }

        void SetSpotWidened(bool widened)
        {
            if (weakSpot == null || spotWidened == widened) return;
            spotWidened = widened;

            float scale = widened ? Mathf.Max(weakSpotColliderScale, 1f) : 1f;
            if (weakSpot is BoxCollider box) box.size = spotBoxSize * scale;
            else if (weakSpot is SphereCollider sphere) sphere.radius = spotSphereRadius * scale;
        }

        void OnDisable()
        {
            SetSpotWidened(false);
        }

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

        public Vector3 StunnedWeakSpotCentre()
        {
            if (weakSpot == null) return transform.position;
            Vector3 offsetNow = weakSpot.bounds.center - transform.position;
            return transform.position + StunPose * (Quaternion.Inverse(transform.rotation) * offsetNow);
        }

        public float WeakSpotHalfExtent()
        {
            if (weakSpot == null) return 0.5f;
            Vector3 extents = weakSpot.bounds.extents;
            return Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
        }

        public override bool LaunchKillAllowedFor(Collider hitCollider)
        {
            if (weakSpot == null || hitCollider == null) return false;
            return hitCollider == weakSpot || hitCollider.transform.IsChildOf(weakSpot.transform);
        }
    }
}

