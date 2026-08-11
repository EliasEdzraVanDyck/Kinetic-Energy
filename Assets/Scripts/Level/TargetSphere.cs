using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // An optional self-directed goal (The Quarry): a floating sphere the player collects by
    // touching it. It disappears, bumps the session counter, and respawns in place after
    // respawnSeconds. No timer, no score screen, no failure - just a spine for free play.
    // Trigger collider only, so it never alters a flight that passes through it.
    public class TargetSphere : MonoBehaviour
    {
        [Tooltip("Seconds after collection before this sphere reappears.")]
        public float respawnSeconds = 20f;
        [Tooltip("Degrees per second the sphere slowly spins, purely to catch the eye.")]
        public float spinSpeed = 45f;
        [Tooltip("Session counter HUD this sphere reports to - wired per scene by the setup script.")]
        public TargetSphereCounter counter;

        Renderer[] sphereRenderers;
        Collider[] sphereColliders;
        float respawnRemaining;

        void Awake()
        {
            sphereRenderers = GetComponentsInChildren<Renderer>(true);
            sphereColliders = GetComponentsInChildren<Collider>(true);
        }

        void Update()
        {
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);

            if (respawnRemaining > 0f)
            {
                respawnRemaining -= Time.deltaTime;
                if (respawnRemaining <= 0f) SetCollected(false);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (respawnRemaining > 0f) return;
            if (other.GetComponent<KineticCubeController>() == null) return;

            counter?.ReportCollected();
            respawnRemaining = respawnSeconds;
            SetCollected(true);
        }

        void SetCollected(bool collected)
        {
            foreach (Renderer rend in sphereRenderers)
            {
                if (rend != null) rend.enabled = !collected;
            }
            foreach (Collider col in sphereColliders)
            {
                if (col != null) col.enabled = !collected;
            }
        }
    }
}
