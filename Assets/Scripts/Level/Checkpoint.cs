using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A checkpoint you have to EARN: a big button set into a frame that only depresses for
    // a committed arrival - a ground pound, or a launch coming down steeply enough. Skim
    // across it on a flat trajectory and nothing happens.
    //
    // Exactly one checkpoint is ever pressed. Claiming this one raises every other, and a
    // respawn or a jump from the Sections screen leaves the section's own button already
    // sunk, so the button state always reads as "here is where you come back to".
    public class Checkpoint : MonoBehaviour
    {
        [Tooltip("Where a death sends the player once this checkpoint is claimed. Empty = this object's own position.")]
        public Transform respawnPoint;

        [Header("Button")]
        [Tooltip("The part that sinks. Empty = this object's own transform.")]
        public Transform buttonVisual;
        [Tooltip("How far the button sinks when claimed, in local units. Keep it SHORT of the frame's top face - a claimed button should still stand proud, just visibly pushed in, rather than vanishing into the plinth.")]
        public float pressDepth = 0.45f;
        [Tooltip("Local units per second the button travels between up and down.")]
        public float pressSpeed = 2.5f;

        [Header("Activation")]
        [Tooltip("How steeply a landing must arrive to press the button: the dot of the incoming direction with straight DOWN. 1 = only dead vertical, 0.7 = within about 45 degrees of vertical. A ground pound always counts.")]
        [Range(0f, 1f)] public float minimumImpactSteepness = 0.7f;

        [Header("Colours")]
        [Tooltip("The button while this checkpoint is NOT the active one.")]
        public Color idleColor = new Color(0.25f, 0.55f, 1f);
        [Tooltip("The button while this checkpoint IS the active one - clearly apart from both the idle button and the frame around it.")]
        public Color pressedColor = new Color(0.25f, 0.95f, 0.45f);
        [Tooltip("Renderer of the pressable part. Empty = found on this object.")]
        public Renderer buttonRenderer;
        [Tooltip("Collider of the pressable part. Only contacts on THIS collider can claim the checkpoint - landing on the frame around it does nothing. Empty = any contact counts.")]
        public Collider buttonCollider;

        LevelSectionController sections;
        Vector3 raisedLocalPosition;
        bool claimed;

        public Transform RespawnTarget => respawnPoint != null ? respawnPoint : transform;
        public bool Claimed => claimed;

        Transform Button => buttonVisual != null ? buttonVisual : transform;

        void Awake()
        {
            raisedLocalPosition = Button.localPosition;
            if (buttonRenderer == null) buttonRenderer = GetComponent<Renderer>();
        }

        void Start()
        {
            sections = FindAnyObjectByType<LevelSectionController>();
            ApplyPressedPose(true); // the level opens with the buttons already in position
        }

        void Update()
        {
            // Eased on UNSCALED time so the button still travels while the midair aim's
            // bullet-time is running.
            Vector3 target = raisedLocalPosition + (claimed ? Vector3.down * pressDepth : Vector3.zero);
            Button.localPosition = Vector3.MoveTowards(Button.localPosition, target, pressSpeed * Time.unscaledDeltaTime);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (claimed) return;
            KineticCubeController player = collision.collider.GetComponent<KineticCubeController>();
            if (player == null || !HitTheButton(collision) || !ArrivedSteeplyEnough(player)) return;

            if (sections == null) sections = FindAnyObjectByType<LevelSectionController>();
            if (sections == null)
            {
                SetClaimed(true); // no section index in this scene - still show the press
                return;
            }
            // The controller owns "where back is", and its reset presses THIS button while
            // raising every other - so the two can never disagree about which is active.
            sections.SetActiveRespawn(RespawnTarget);
            sections.ResetCheckpoints();
        }

        // The frame and the button are both solid and both report through this component
        // (the frame carries the body), so the button face has to be identified explicitly.
        bool HitTheButton(Collision collision)
        {
            if (buttonCollider == null) return true;
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).thisCollider == buttonCollider) return true;
            }
            return false;
        }

        // A ground pound always counts. Otherwise the shot has to be coming DOWN: the
        // approach direction is measured against straight down, so a flat skim across the
        // pad leaves the button up however fast it was.
        bool ArrivedSteeplyEnough(KineticCubeController player)
        {
            if (player.LastCrashWasPound) return true;
            Vector3 approach = player.PreCollisionVelocity;
            if (approach.sqrMagnitude < 0.01f) return false;
            return Vector3.Dot(approach.normalized, Vector3.down) >= minimumImpactSteepness;
        }

        public void SetClaimed(bool value)
        {
            if (claimed == value) return;
            claimed = value;
            ApplyPressedPose(false);
        }

        void ApplyPressedPose(bool instant)
        {
            if (instant)
            {
                Button.localPosition = raisedLocalPosition + (claimed ? Vector3.down * pressDepth : Vector3.zero);
            }
            if (buttonRenderer != null) buttonRenderer.material.color = claimed ? pressedColor : idleColor;
        }
    }
}
