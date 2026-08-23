using UnityEngine;

namespace KineticEnergy.Level
{

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

        bool ownsColour;

        bool Flickers => requirementPercent >= 100;

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

            Enemy enemy = GetComponent<Enemy>();

            if (enemy != null) enemy.vulnerableColor = band.baseColor;
            ownsColour = enemy == null;
            WeakSpotFlyingEnemy weakSpotFlyer = GetComponent<WeakSpotFlyingEnemy>();
            if (weakSpotFlyer != null) weakSpotFlyer.SetSpotTier(band.baseColor);
            TurretEnemy turret = GetComponent<TurretEnemy>();
            if (turret != null) turret.SetTier(band.baseColor);
            Checkpoint checkpoint = GetComponent<Checkpoint>();
            if (checkpoint != null) checkpoint.SetTier(band.baseColor);

            if (targetRenderer != null) bandMaterial = targetRenderer.material;

            if (buildTickMarks) BuildTickMarks();
            if (showPercentLabel) BuildPercentLabel();
        }

        void Update()
        {

            if (!Flickers || band == null || bandMaterial == null || !ownsColour) return;
            float noise = Mathf.PerlinNoise(Time.unscaledTime * 18f, flickerSeed);
            bandMaterial.color = band.baseColor * (0.86f + 0.14f * noise);
        }

        void BuildTickMarks()
        {
            if (targetRenderer == null || palette == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(requirementPercent / 10f), 1, 10);

            tickRoot = new GameObject("RequirementTicks").transform;
            Bounds bounds = targetRenderer.bounds;
            tickRoot.position = new Vector3(bounds.center.x, bounds.max.y + tickHeight, bounds.center.z);

            const float spacing = 0.24f;
            const float size = 0.14f;
            for (int i = 0; i < count; i++)
            {
                GameObject tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tick.name = "Tick" + (i + 1);
                Destroy(tick.GetComponent<Collider>());
                tick.transform.SetParent(tickRoot, false);
                tick.transform.localPosition = new Vector3((i - (count - 1) * 0.5f) * spacing, 0f, 0f);
                tick.transform.localScale = Vector3.one * size;
                Renderer tickRenderer = tick.GetComponent<Renderer>();

                if (pipMaterial != null) tickRenderer.sharedMaterial = pipMaterial;
                tickRenderer.material.color = band.baseColor;
                tickRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void BuildPercentLabel()
        {
            if (GetComponent<SizedEnemy>() != null) return;

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

            labelTransform = go.transform;
        }

        void LateUpdate()
        {
            if (targetRenderer == null) return;

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

        void SetRowsVisible(bool visible)
        {
            if (tickRoot != null) tickRoot.gameObject.SetActive(visible);
            if (labelTransform != null) labelTransform.gameObject.SetActive(visible);
        }

        void OnEnable() => SetRowsVisible(true);
        void OnDisable() => SetRowsVisible(false);

        void OnDestroy()
        {
            if (tickRoot != null) Destroy(tickRoot.gameObject);
            if (labelTransform != null) Destroy(labelTransform.gameObject);
        }
    }
}

