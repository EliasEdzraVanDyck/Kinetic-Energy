using UnityEngine;

namespace KineticEnergy.Level
{
    // Declares WHAT an interactable costs (20/40/60/80/100% of the tank) and paints the
    // tier's colour language onto it at runtime: base colour, HDR emission, the top tier's
    // pulse, and a row of countable tick marks (one per 20%) for colourblind readability.
    //
    // Everything is applied at runtime through per-renderer material instances and the
    // components' own colour hooks - no serialized inspector value on the object is
    // overwritten, so hand tuning survives and the palette stays the single authority.
    public class EnergyRequirement : MonoBehaviour
    {
        [Tooltip("Energy the interaction needs, as a whole percentage of the tank (20/40/60/80/100).")]
        public int requirementPercent = 20;
        [Tooltip("The shared tier palette - colours live THERE, not here.")]
        public EnergyTierPalette palette;
        [Tooltip("The renderer that shows the tier (an enemy body, the weak spot, a checkpoint button). Empty = this object's own renderer.")]
        public Renderer targetRenderer;
        [Tooltip("Build a row of small cubes above the object - one per 20% - so the requirement can be COUNTED rather than only colour-matched. OFF by default: the floating row reads as clutter in the world. The meter's own tick carries the same information.")]
        public bool buildTickMarks = false;
        [Tooltip("World metres between the target's top and the tick row.")]
        public float tickHeight = 0.35f;

        public float RequirementFraction => requirementPercent / 100f;
        public Color TierColor { get; private set; } = Color.white;

        EnergyTierPalette.Tier tier;
        Material emissiveMaterial;
        Transform tickRoot;
        // Set only for enemies that HAVE a kill window. The tier glow is then gated on it:
        // the colour means "punishable right now", not "this thing costs 60%". Turrets and
        // weak-spot flyers leave this null - they are punishable whenever you can reach
        // them, so their tier never switches off.
        Enemy vulnerabilityGate;
        bool emissionLit;

        void Start()
        {
            if (palette == null) return;
            tier = palette.TierFor(requirementPercent);
            if (tier == null) return;
            TierColor = tier.baseColor;

            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

            // Component hooks first: these repaint through their own colour logic, so the
            // tier survives their per-tick colour writes.
            Enemy enemy = GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.vulnerableColor = tier.baseColor;
                // A plain Always-window enemy is punishable whenever you reach it, so it
                // needs no gate; the hunter windows do.
                if (enemy.killWindow != EnemyKillWindow.Always) vulnerabilityGate = enemy;
            }
            WeakSpotFlyingEnemy weakSpotFlyer = GetComponent<WeakSpotFlyingEnemy>();
            if (weakSpotFlyer != null) weakSpotFlyer.SetSpotTier(tier.baseColor);
            TurretEnemy turret = GetComponent<TurretEnemy>();
            if (turret != null) turret.SetTier(tier.baseColor);
            Checkpoint checkpoint = GetComponent<Checkpoint>();
            if (checkpoint != null) checkpoint.SetTier(tier.baseColor);

            // The glow: set once on the renderer's own material instance. Colour writes by
            // the components above touch _BaseColor only, so the emission persists.
            if (targetRenderer != null)
            {
                emissiveMaterial = targetRenderer.material;
                emissiveMaterial.EnableKeyword("_EMISSION");
                emissiveMaterial.SetColor("_EmissionColor", tier.baseColor * tier.emissionIntensity);
                emissionLit = true;
            }

            if (buildTickMarks) BuildTickMarks();
        }

        void Update()
        {
            if (tier == null || emissiveMaterial == null) return;

            // The glow IS the kill window on gated enemies. Outside it the body is plain
            // red and unlit - so the tier colour never doubles as a nameplate, and lighting
            // up is itself the tell that the enemy can be taken right now.
            if (vulnerabilityGate != null)
            {
                bool lit = vulnerabilityGate.CanBeKilledByLaunch;
                if (lit != emissionLit)
                {
                    emissionLit = lit;
                    emissiveMaterial.SetColor("_EmissionColor",
                        lit ? tier.baseColor * tier.emissionIntensity : Color.black);
                }
                if (!lit) return;
            }

            // Only the TOP tier animates: a slow breath between its own glow and a
            // white-hot core - the one moving light in the scene reads at any distance.
            if (!tier.pulses) return;
            float wave = (Mathf.Sin(Time.unscaledTime * 2f) + 1f) * 0.5f;
            Color core = Color.Lerp(tier.baseColor, Color.white, wave * 0.7f);
            emissiveMaterial.SetColor("_EmissionColor", core * (tier.emissionIntensity * (0.8f + 0.4f * wave)));
        }

        // One mark per 20% of requirement, in a centred row above the target - countable
        // where the hue is not (cyan->magenta converges for protanopes at 40 vs 80).
        void BuildTickMarks()
        {
            if (targetRenderer == null) return;
            int count = Mathf.Clamp(requirementPercent / 20, 1, 5);

            tickRoot = new GameObject("RequirementTicks").transform;
            tickRoot.SetParent(transform, false);
            Bounds bounds = targetRenderer.bounds;
            tickRoot.position = new Vector3(bounds.center.x, bounds.max.y + tickHeight, bounds.center.z);

            const float spacing = 0.24f;
            const float size = 0.14f;
            for (int i = 0; i < count; i++)
            {
                GameObject tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tick.name = "Tick" + (i + 1);
                Destroy(tick.GetComponent<Collider>()); // never blocks or shows in the aim
                tick.transform.SetParent(tickRoot, false);
                tick.transform.localPosition = new Vector3((i - (count - 1) * 0.5f) * spacing, 0f, 0f);
                tick.transform.localScale = Vector3.one * size;
                Renderer tickRenderer = tick.GetComponent<Renderer>();
                tickRenderer.material.color = tier.baseColor;
                tickRenderer.material.EnableKeyword("_EMISSION");
                tickRenderer.material.SetColor("_EmissionColor", tier.baseColor * tier.emissionIntensity);
                tickRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void LateUpdate()
        {
            // The row faces the camera and rides the target (enemies move and scale).
            if (tickRoot == null || targetRenderer == null) return;
            Bounds bounds = targetRenderer.bounds;
            tickRoot.position = new Vector3(bounds.center.x, bounds.max.y + tickHeight, bounds.center.z);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                Vector3 look = tickRoot.position - cam.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.001f) tickRoot.rotation = Quaternion.LookRotation(look);
            }
        }
    }
}
