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
        [Tooltip("Print the requirement as a number above the pips. OFF by default: the object's own band colour carries the price, and floating figures over every enemy and pad read as clutter.")]
        public bool showPercentLabel = false;
        [Tooltip("World metres the number sits above the pip row.")]
        public float labelRise = 0.42f;
        [Tooltip("Material the pips are drawn with. MUST be set: primitives default to the built-in Standard material, whose shader is not part of URP and is stripped from builds - the pips then render solid magenta in a build while looking fine in the editor.")]
        public Material pipMaterial;

        public float RequirementFraction => requirementPercent / 100f;
        public Color TierColor { get; private set; } = Color.white;

        EnergyBandPalette.Band band;
        Material bandMaterial;
        Transform tickRoot;
        Transform labelTransform;
        float flickerSeed;
        // Enemies paint their own body every frame (red when untouchable, the band colour
        // while punishable), so this component must not fight them for the material.
        bool ownsColour;

        // Flicker is derived from the requirement, never authored: ONLY the exactly-100%
        // case flickers, and nothing else in the game does - the flicker IS the 80-vs-100
        // disambiguator, since both share the white-hot band.
        bool Flickers => requirementPercent >= 100;

        // The advertised requirement is READ OFF the gate that enforces it, so the display
        // can never disagree with the price: retune a kill fraction in the inspector and
        // the pips, the number, the band colour and the meter tick all follow.
        void SyncRequirementFromGate()
        {
            SizedEnemy sized = GetComponent<SizedEnemy>();
            FlyingEnemy anyFlyer = GetComponent<FlyingEnemy>();
            TurretEnemy anyTurret = GetComponent<TurretEnemy>();
            Checkpoint anyCheckpoint = GetComponent<Checkpoint>();

            float fraction = -1f;
            if (sized != null) fraction = sized.ConfiguredKillFraction;
            else if (anyFlyer != null) fraction = anyFlyer.minKillEnergyFraction;
            else if (anyTurret != null) fraction = anyTurret.minKillEnergyFraction;
            else if (anyCheckpoint != null) fraction = anyCheckpoint.minActivationEnergyFraction;
            if (fraction >= 0f) requirementPercent = Mathf.RoundToInt(fraction * 100f);
        }

        void Start()
        {
            if (palette == null) return;
            SyncRequirementFromGate();
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
            if (showPercentLabel) BuildPercentLabel();
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

        // The countable read: one pip per 10% of the requirement (60% = six pips), centred
        // above the target. Colour is the fast approximate read; pips are the exact one -
        // and because the requirement is synced off the enforcing gate, the count follows
        // any retune automatically.
        void BuildTickMarks()
        {
            if (targetRenderer == null || palette == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(requirementPercent / 10f), 1, 10);

            // UNPARENTED, deliberately. Parented, the row inherited the target's lossy
            // scale - a checkpoint scaled up in the scene, or a large sized enemy, blew the
            // pips up with it and skewed them on non-uniform scales, so they no longer read
            // as a fixed-size row. LateUpdate carries the position and facing instead, and
            // OnDestroy takes the row with the object.
            tickRoot = new GameObject("RequirementTicks").transform;
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
                // The primitive's own default material is the built-in Standard one, which
                // URP cannot render and the build strips - swap it for the wired asset
                // BEFORE tinting, then .material instances a per-pip copy to colour.
                if (pipMaterial != null) tickRenderer.sharedMaterial = pipMaterial;
                tickRenderer.material.color = band.baseColor; // flat, like everything else
                tickRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // The exact read, next to the pips' countable one: the requirement as a number.
        // Sized enemies print their own, so this only fills the gap on everything else -
        // checkpoints above all, which had no figure on them at all.
        void BuildPercentLabel()
        {
            if (GetComponent<SizedEnemy>() != null) return; // already prints its own

            GameObject go = new GameObject("RequirementLabel");
            TextMesh label = go.AddComponent<TextMesh>();
            label.text = requirementPercent + "%";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 64;
            label.characterSize = 0.045f;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.color = band.baseColor;
            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
            if (label.font != null) meshRenderer.sharedMaterial = label.font.material;
            // Unparented for the same reason as the pips: no inherited scale, ever.
            labelTransform = go.transform;
        }

        void LateUpdate()
        {
            if (targetRenderer == null) return;

            // Both rows ride the target and face the camera. Because neither is parented,
            // their size is fixed in world units no matter how the target is scaled.
            Bounds bounds = targetRenderer.bounds;
            UnityEngine.Camera cam = UnityEngine.Camera.main;

            if (tickRoot != null)
            {
                tickRoot.position = new Vector3(bounds.center.x, bounds.max.y + tickHeight, bounds.center.z);
                if (cam != null)
                {
                    Vector3 look = tickRoot.position - cam.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.001f) tickRoot.rotation = Quaternion.LookRotation(look);
                }
            }

            if (labelTransform != null)
            {
                labelTransform.position = new Vector3(
                    bounds.center.x, bounds.max.y + tickHeight + labelRise, bounds.center.z);
                if (cam != null)
                {
                    labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - cam.transform.position);
                }
            }
        }

        // Unparented rows do not follow their object's lifecycle, so the whole of it is
        // mirrored by hand. A killed enemy is DEACTIVATED rather than destroyed (it is
        // revived on respawn), so without this its pips and number were left hanging in
        // mid-air over an empty platform. Enable/Disable covers the kill and the revival;
        // Destroy covers the scene teardown.
        void SetRowsVisible(bool visible)
        {
            if (tickRoot != null) tickRoot.gameObject.SetActive(visible);
            if (labelTransform != null) labelTransform.gameObject.SetActive(visible);
        }

        void OnEnable() => SetRowsVisible(true);   // no-ops before Start builds them
        void OnDisable() => SetRowsVisible(false);

        void OnDestroy()
        {
            if (tickRoot != null) Destroy(tickRoot.gameObject);
            if (labelTransform != null) Destroy(labelTransform.gameObject);
        }
    }
}
