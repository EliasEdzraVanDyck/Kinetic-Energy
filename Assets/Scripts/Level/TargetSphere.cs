using UnityEngine;

namespace KineticEnergy.Level
{
    // A SOLID target sphere: the player crashes into it like any surface (full energy
    // refund by the scene's rules), then the sphere vanishes, leaving them hanging where it
    // was - launch again to carry on, or ride out the brief cling and drop (the controller
    // arms that release unconditionally for targets, since there is no surface left to rest
    // on - see KineticCubeController.RegisterCrash). Being solid also makes it a genuine aim
    // target: the dotted trail terminates on it and the reticle focuses it.
    // Unlike the original one-shot version, it respawns in place after respawnSeconds and
    // reports to the session counter.
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

        // Called by KineticCubeController the moment the player crash-lands on this sphere.
        public void OnHitByCrash()
        {
            if (respawnRemaining > 0f) return;
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
