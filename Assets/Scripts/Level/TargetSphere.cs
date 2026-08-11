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

        [Header("Respawn Area")]
        // Respawns land at a RANDOM point inside this world-space box (wired per scene by
        // the setup script - the Quarry uses its arena interior, capped at Y = 64). Leave
        // both at zero to respawn in place instead.
        public Vector3 respawnAreaMin;
        public Vector3 respawnAreaMax;
        [Tooltip("A respawn point must have at least this much empty space around it, so a sphere can't reappear inside geometry.")]
        public float respawnClearRadius = 2.5f;

        // Targets never sit below this height, wherever they spawn or respawn.
        const float MinHeightY = 7f;

        Renderer[] sphereRenderers;
        Collider[] sphereColliders;
        float respawnRemaining;

        // Visible and collectable right now (not waiting out a respawn).
        public bool IsActive => respawnRemaining <= 0f;

        void Awake()
        {
            sphereRenderers = GetComponentsInChildren<Renderer>(true);
            sphereColliders = GetComponentsInChildren<Collider>(true);

            // Enforced in code so hand-tuned scene values stay untouched.
            if (transform.position.y < MinHeightY)
            {
                Vector3 lifted = transform.position;
                lifted.y = MinHeightY;
                transform.position = lifted;
            }
        }

        void Start()
        {
            counter?.Register(this);
        }

        void Update()
        {
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);

            if (respawnRemaining > 0f)
            {
                respawnRemaining -= Time.deltaTime;
                if (respawnRemaining <= 0f)
                {
                    MoveToRandomRespawnPoint();
                    SetCollected(false);
                }
            }
        }

        // Backstop: the sphere hears the collision itself, so even if the controller's
        // crash pipeline drops the event for any reason (guard ordering, extreme speeds),
        // contact with the player ALWAYS collects the target.
        void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.GetComponent<KineticEnergy.Player.KineticCubeController>() == null) return;
            OnHitByCrash();
        }

        // Called by KineticCubeController the moment the player crash-lands on this sphere.
        public void OnHitByCrash()
        {
            if (respawnRemaining > 0f) return;
            respawnRemaining = respawnSeconds;
            SetCollected(true);
            // Reported AFTER hiding, so the counter's minimum-active check sees the true
            // count and can respawn a hidden sphere immediately if needed.
            counter?.ReportCollected();
        }

        // The counter's minimum-targets rule: bring this hidden sphere back right now, at a
        // random point.
        public void ForceRespawnNow()
        {
            if (respawnRemaining <= 0f) return;
            respawnRemaining = 0f;
            MoveToRandomRespawnPoint();
            SetCollected(false);
        }

        // Picks a random point inside the respawn box with enough clearance to not overlap
        // geometry. Bounded attempts - if the box is somehow too crowded, the sphere just
        // reappears where it was, which is always valid.
        void MoveToRandomRespawnPoint()
        {
            if (respawnAreaMin == respawnAreaMax) return;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector3 candidate = new Vector3(
                    Random.Range(respawnAreaMin.x, respawnAreaMax.x),
                    Mathf.Max(Random.Range(respawnAreaMin.y, respawnAreaMax.y), MinHeightY),
                    Random.Range(respawnAreaMin.z, respawnAreaMax.z));
                if (!Physics.CheckSphere(candidate, respawnClearRadius))
                {
                    transform.position = candidate;
                    return;
                }
            }
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
