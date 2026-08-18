using UnityEngine;

namespace KineticEnergy.Level
{
    // Declares WHAT an interactable costs (20/40/60/80/100% of the tank) and paints the
    // blackbody band language onto it at runtime: the band's FLAT colour, the exactly-100%
    // arc flicker, and the countable pip row. No HDR emission - a bloomed object reads as a
    // washed-out version of its own band, which breaks the one comparison the whole system
    // rests on ("is my meter at least as hot as that object?").
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
        Material bandMaterial;
        Transform tickRoot;
        float flickerSeed;
        // Enemies paint their own body every frame (red when untouchable, the band colour
        // while punishable), so this component must not fight them for the material.
        bool ownsColour;

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
            // The enemy owns its own body colour: red while untouchable, this band's colour
            // the moment a launch would kill. That switch IS the punishable tell - it needs
            // no glow on top of it.
            if (enemy != null) enemy.vulnerableColor = band.baseColor;
            ownsColour = enemy == null;
            WeakSpotFlyingEnemy weakSpotFlyer = GetComponent<WeakSpotFlyingEnemy>();
            if (weakSpotFlyer != null) weakSpotFlyer.SetSpotTier(band.baseColor);
            TurretEnemy turret = GetComponent<TurretEnemy>();
            if (turret != null) turret.SetTier(band.baseColor);
            Checkpoint checkpoint = GetComponent<Checkpoint>();
            if (checkpoint != null) checkpoint.SetTier(band.baseColor);

            // FLAT colour only - no HDR emission. An intensity above 1 pushes the colour
            // past white and blooms out, so the object stopped matching the flat band
            // colour the meter shows, and matching those two IS the read.
            if (targetRenderer != null) bandMaterial = targetRenderer.material;

            if (buildTickMarks) BuildTickMarks();
        }

        void Update()
        {
            // The exactly-100% tell: fast, irregular, low-amplitude BRIGHTNESS noise - an
            // arc flicker, not a pulse, and the only animation in the game. Perlin keeps it
            // aperiodic so it reads as electrical rather than as a throb. It rides the flat
            // colour now that there is no emission channel to carry it, and it only runs on
            // objects whose material this component actually owns.
            if (!Flickers || band == null || bandMaterial == null || !ownsColour) return;
            float noise = Mathf.PerlinNoise(Time.unscaledTime * 18f, flickerSeed);
            bandMaterial.color = band.baseColor * (0.86f + 0.14f * noise);
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
                tickRenderer.material.color = band.baseColor; // flat, like everything else
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
