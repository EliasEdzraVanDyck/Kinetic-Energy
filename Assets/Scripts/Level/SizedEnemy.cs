using UnityEngine;

namespace KineticEnergy.Level
{
    public enum EnemySizeClass
    {
        Small,  // faster, softer knockback, dies to a 20% launch
        Medium, // the unmodified base enemy, dies to a 40% launch
        Large,  // slower, harder knockback, dies to a 60% launch
    }

    // The sized ground-enemy family: ONE prefab, the class picked per instance in the
    // inspector. Inherits every behaviour and value from Enemy; the chosen class applies
    // its multipliers once at Start (Medium multiplies by nothing). All three demand a
    // minimum launch spend to kill while vulnerable - shown as a billboard percentage
    // above the body - and a cheaper launch bounces off, hurting the player with the
    // enemy's own (size-scaled) hit instead.
    public class SizedEnemy : Enemy
    {
        [Header("Size Class")]
        [Tooltip("Picked per placed instance - Small / Medium / Large.")]
        public EnemySizeClass sizeClass = EnemySizeClass.Medium;

        [Header("Small")]
        public float smallSpeedMultiplier = 1.35f;
        public float smallKnockbackMultiplier = 0.6f;
        public float smallScaleMultiplier = 0.65f;
        [Range(0f, 1f)] public float smallKillEnergyFraction = 0.2f;
        [Tooltip("Seconds added to this size's whole cool-down after an attack - NEGATIVE for the small one, which shakes it off quicker and so offers a shorter punish window.")]
        public float smallCooldownOffset = -0.2f;

        [Header("Medium")]
        [Range(0f, 1f)] public float mediumKillEnergyFraction = 0.4f;
        [Tooltip("The baseline - the medium enemy is the unmodified hunter, so this is 0 by default.")]
        public float mediumCooldownOffset = 0f;

        [Header("Large")]
        public float largeSpeedMultiplier = 0.7f;
        public float largeKnockbackMultiplier = 1.6f;
        public float largeScaleMultiplier = 1.45f;
        [Range(0f, 1f)] public float largeKillEnergyFraction = 0.6f;
        [Tooltip("Seconds added to this size's whole cool-down after an attack - POSITIVE for the large one, which is slow to recover and so hangs punishable for longer.")]
        public float largeCooldownOffset = 0.5f;

        [Header("Kill Label")]
        [Tooltip("Print the kill percentage above the body. OFF by default now - the body's band colour carries the price, and the floating figures read as clutter.")]
        public bool showKillLabel = false;
        [Tooltip("World metres between the body's top and the percentage label.")]
        public float labelHeight = 0.9f;
        public Color labelColor = new Color(1f, 1f, 1f, 0.9f);

        float killEnergyFraction;
        Transform labelTransform;

        // Read by the crash pipeline: the minimum launch-energy fraction a kill needs.
        public override float MinKillEnergyFraction => killEnergyFraction;

        // The same figure straight from the serialized class config - valid BEFORE Start
        // has run (killEnergyFraction is only assigned there), which is what display code
        // with an undefined Start order has to read.
        public float ConfiguredKillFraction => sizeClass switch
        {
            EnemySizeClass.Small => smallKillEnergyFraction,
            EnemySizeClass.Large => largeKillEnergyFraction,
            _ => mediumKillEnergyFraction,
        };

        protected override void Start()
        {
            // The class's twist is applied BEFORE the base wiring runs, so everything
            // downstream (spawn capture, body half-height from the scale) sees final values.
            float cooldownOffset;
            switch (sizeClass)
            {
                case EnemySizeClass.Small:
                    moveSpeed *= smallSpeedMultiplier;
                    knockbackForce *= smallKnockbackMultiplier;
                    transform.localScale *= smallScaleMultiplier;
                    killEnergyFraction = smallKillEnergyFraction;
                    cooldownOffset = smallCooldownOffset;
                    break;
                case EnemySizeClass.Large:
                    moveSpeed *= largeSpeedMultiplier;
                    knockbackForce *= largeKnockbackMultiplier;
                    transform.localScale *= largeScaleMultiplier;
                    killEnergyFraction = largeKillEnergyFraction;
                    cooldownOffset = largeCooldownOffset;
                    break;
                default:
                    killEnergyFraction = mediumKillEnergyFraction;
                    cooldownOffset = mediumCooldownOffset;
                    break;
            }

            // The cool-down after an attack, shifted whole. All THREE values move together
            // because the punish window is max(vulnerableAfterAttackSeconds, attackCooldown)
            // - on the hunter that is 2.5 against 2.0, so the cooldown dominates and moving
            // only the vulnerable figure would change nothing at all. Shifting the group
            // keeps them in their authored relationship while the window itself lands
            // exactly the requested amount earlier or later, and recoverSeconds keeps the
            // slumped pose in step with it.
            if (!Mathf.Approximately(cooldownOffset, 0f))
            {
                const float floor = 0.05f; // never zero or negative, however it is tuned
                recoverSeconds = Mathf.Max(recoverSeconds + cooldownOffset, floor);
                attackCooldown = Mathf.Max(attackCooldown + cooldownOffset, floor);
                vulnerableAfterAttackSeconds = Mathf.Max(vulnerableAfterAttackSeconds + cooldownOffset, floor);
            }

            base.Start();
            if (showKillLabel) BuildKillLabel();
        }

        // An under-charged kill attempt bounces off the armour: the enemy's ordinary hit,
        // aimed away from the body - same shove composition its attack landing uses.
        public override void PunishFailedKill()
        {
            if (player == null) return;
            Vector3 away = player.transform.position - transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            Vector3 shoveDirection = (away.normalized + Vector3.up * 0.6f).normalized;
            player.ApplyEnemyHit(shoveDirection * knockbackForce, attackEnergyDrain, postHitLaunchLockSeconds);
        }

        void BuildKillLabel()
        {
            GameObject go = new GameObject("KillEnergyLabel");
            go.transform.SetParent(transform, false);
            // The parent's scale would inflate the text - local placement and scale are
            // divided back out so the label reads the same size over every class.
            float bodyScale = Mathf.Max(transform.localScale.y, 0.01f);
            go.transform.localPosition = new Vector3(0f, 0.5f + labelHeight / bodyScale, 0f);
            go.transform.localScale = Vector3.one / bodyScale;

            TextMesh label = go.AddComponent<TextMesh>();
            label.text = Mathf.RoundToInt(killEnergyFraction * 100f) + "%";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 64;
            label.characterSize = 0.045f;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.color = labelColor;
            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
            if (label.font != null) meshRenderer.sharedMaterial = label.font.material;

            labelTransform = go.transform;
        }

        void LateUpdate()
        {
            if (labelTransform == null) return;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - cam.transform.position);
        }
    }
}
