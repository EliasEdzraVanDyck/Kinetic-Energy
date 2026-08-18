using UnityEngine;

namespace KineticEnergy.Level
{
    // Declares WHAT an interactable costs (20/40/60/80/100% of the tank) and paints the
    // blackbody band language onto it at runtime: the band's colour, its HDR emission
    // (always on for interactables - being emissive at depth is what separates them from
    // the flat HUD meter drawing the same hues), the exactly-100% arc flicker, and the
    // countable pip row.
    //
    // Everything is applied at runtime through per-renderer material instances and the
    // components' own colour hooks - no serialized inspector value on the object is
    // overwritten, so hand tuning survives and the palette stays the single authority.
    public class EnergyRequirement : MonoBehaviour
    {
        [Tooltip("Energy the interaction needs, as a whole percentage of the tank (20/40/60/80/100).")]
        public int requirementPercent = 20;
        [Tooltip("The shared band palette - colours live THERE, not here.")]
        public EnergyBandPalette palette;
        [Tooltip("The renderer that shows the band (an enemy body, the weak spot, a checkpoint button). Empty = this object's own renderer.")]
        public Renderer targetRenderer;
        [Tooltip("The countable pip row above the object - one pip per band ordinal, so bands 4 and 5 (close in hue) can be told apart by COUNT. Redundant encoding is required, not decoration.")]
        public bool buildTickMarks = true;
        [Tooltip("World metres between the target's top and the pip row.")]
        public float tickHeight = 0.35f;

        public float RequirementFraction => requirementPercent / 100f;
        public Color TierColor { get; private set; } = Color.white;

        EnergyBandPalette.Band band;
        Material emissiveMaterial;
        Transform tickRoot;
        float flickerSeed;
        // Set only for enemies that HAVE a kill window. The band glow is then gated on it:
        // the colour means "punishable right now", not "this thing costs 60%". Turrets and
        // weak-spot flyers leave this null - they are punishable whenever you can reach
        // them, so their band never switches off.
        Enemy vulnerabilityGate;
        bool emissionLit;

        // Flicker is derived from the requirement, never authored: ONLY the exactly-100%
        // case flickers, and nothing else in the game does - the flicker IS the 80-vs-100
        // disambiguator, since both share the white-hot band.
        bool Flickers => requirementPercent >= 100;

        void Start()
        {
            if (palette == null) return;
            band = palette.GetBand(RequirementFraction);
            if (band == null) return;
            TierColor = band.baseColor;
            flickerSeed = (GetInstanceID() & 0xFFFF) * 0.137f;

            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

            // Component hooks first: these repaint through their own colour logic, so the
            // band survives their per-tick colour writes.
            Enemy enemy = GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.vulnerableColor = band.baseColor;
                // A plain Always-window enemy is punishable whenever you reach it, so it
                // needs no gate; the hunter windows do.
                if (enemy.killWindow != EnemyKillWindow.Always) vulnerabilityGate = enemy;
            }
            WeakSpotFlyingEnemy weakSpotFlyer = GetComponent<WeakSpotFlyingEnemy>();
            if (weakSpotFlyer != null) weakSpotFlyer.SetSpotTier(band.baseColor);
            TurretEnemy turret = GetComponent<TurretEnemy>();
            if (turret != null) turret.SetTier(band.baseColor);
            Checkpoint checkpoint = GetComponent<Checkpoint>();
            if (checkpoint != null) checkpoint.SetTier(band.baseColor);

            // The glow: always on for interactables (treatment is the separator from the
            // HUD), set once on the renderer's own material instance. Colour writes by the
            // components above touch _BaseColor only, so the emission persists.
            if (targetRenderer != null)
            {
                emissiveMaterial = targetRenderer.material;
                emissiveMaterial.EnableKeyword("_EMISSION");
                emissiveMaterial.SetColor("_EmissionColor", band.baseColor * band.emissionIntensity);
                emissionLit = true;
            }

            if (buildTickMarks) BuildTickMarks();
        }

        void Update()
        {
            if (band == null || emissiveMaterial == null) return;

            // The glow IS the kill window on gated enemies. Outside it the body is plain
            // red and unlit - so the band colour never doubles as a nameplate, and lighting
            // up is itself the tell that the enemy can be taken right now.
            if (vulnerabilityGate != null)
            {
                bool lit = vulnerabilityGate.CanBeKilledByLaunch;
                if (lit != emissionLit)
                {
                    emissionLit = lit;
                    emissiveMaterial.SetColor("_EmissionColor",
                        lit ? band.baseColor * band.emissionIntensity : Color.black);
                }
                if (!lit) return;
            }

            // The exactly-100% tell: fast, irregular, low-amplitude emission noise - an arc
            // flicker, not a pulse. Perlin keeps it aperiodic so it reads as electrical.
            if (!Flickers) return;
            float noise = Mathf.PerlinNoise(Time.unscaledTime * 18f, flickerSeed);
            emissiveMaterial.SetColor("_EmissionColor",
                band.baseColor * (band.emissionIntensity * (0.8f + 0.35f * noise)));
        }

        // The countable read: one pip per band ORDINAL (band 3 = three pips), centred above
        // the target. Colour is the fast approximate read; pips are the exact one - bands 4
        // and 5 sit close in hue, so counting is what keeps them unambiguous.
        void BuildTickMarks()
        {
            if (targetRenderer == null || palette == null) return;
            int count = Mathf.Clamp(palette.BandIndex(band), 1, 5);

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
                tickRenderer.material.color = band.baseColor;
                tickRenderer.material.EnableKeyword("_EMISSION");
                tickRenderer.material.SetColor("_EmissionColor", band.baseColor * band.emissionIntensity);
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
