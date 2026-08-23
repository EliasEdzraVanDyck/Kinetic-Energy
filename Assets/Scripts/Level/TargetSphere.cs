using UnityEngine;

namespace KineticEnergy.Level
{

    public class TargetSphere : MonoBehaviour
    {
        [Tooltip("Seconds after collection before this sphere reappears.")]
        public float respawnSeconds = 20f;
        [Tooltip("Degrees per second the sphere slowly spins, purely to catch the eye.")]
        public float spinSpeed = 45f;
        [Tooltip("Session counter HUD this sphere reports to - wired per scene by the setup script.")]
        public TargetSphereCounter counter;

        [Header("Respawn Area")]

        public Vector3 respawnAreaMin;
        public Vector3 respawnAreaMax;
        [Tooltip("A respawn point must have at least this much empty space around it, so a sphere can't reappear inside geometry.")]
        public float respawnClearRadius = 2.5f;

        const float MinHeightY = 7f;

        Renderer[] sphereRenderers;
        Collider[] sphereColliders;
        float respawnRemaining;

        public bool IsActive => respawnRemaining <= 0f;

        void Awake()
        {
            sphereRenderers = GetComponentsInChildren<Renderer>(true);
            sphereColliders = GetComponentsInChildren<Collider>(true);

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

        void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.GetComponent<KineticEnergy.Player.KineticCubeController>() == null) return;
            OnHitByCrash();
        }

        public static event System.Action Collected;

        public void OnHitByCrash()
        {
            if (respawnRemaining > 0f) return;
            respawnRemaining = respawnSeconds;
            SetCollected(true);
            Collected?.Invoke();

            counter?.ReportCollected();
        }

        public void ForceRespawnNow()
        {
            if (respawnRemaining <= 0f) return;
            respawnRemaining = 0f;
            MoveToRandomRespawnPoint();
            SetCollected(false);
        }

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

