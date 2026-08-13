using UnityEngine;

namespace KineticEnergy.Camera
{
    public enum AimCameraVariant
    {
        Baseline,      // A - frozen first person, FOV zoom (current behaviour)
        OtsDrift,      // B - over-the-shoulder follower, FOV zoom (drift zeroed in its preset)
        OtsDriftDolly, // C - as B, but the energy dial dollies back instead of zooming FOV
        OtsParallax,   // D - as B plus the subtle drift orbit: isolates the motion-parallax question
    }

    // All tuning for one midair-aim camera variant, kept as an asset so the numbers live
    // in the Inspector rather than code - tune without a recompile. Three of these (A/B/C)
    // are referenced by AimCameraVariantController; the depth-perception playtest cycles
    // between them at runtime.
    [CreateAssetMenu(menuName = "Kinetic Energy/Aim Camera Preset")]
    public class AimCameraPreset : ScriptableObject
    {
        public AimCameraVariant variant = AimCameraVariant.Baseline;
        [Tooltip("Shown on the HUD tag and the pause-menu selector, e.g. \"OTS + drift\".")]
        public string displayName = "Baseline";

        [Header("Over-the-shoulder placement (ignored by Baseline)")]
        [Tooltip("Metres behind the player along the launch vector (player 'radius' is ~0.5).")]
        public float otsBack = 1.4f;
        [Tooltip("Metres of world-up rise - a slight downward look shows more ground plane.")]
        public float otsRise = 0.35f;
        [Tooltip("Metres of sideways offset - breaks symmetry so the sphere doesn't sit on the reticle.")]
        public float otsSide = 0.30f;
        [Tooltip("Spherecast radius for the player-to-camera clearance check; the camera pulls in on a hit.")]
        public float camCollisionRadius = 0.25f;
        [Tooltip("UNSCALED seconds to blend from third person into the OTS frame - never snap.")]
        public float blendInTime = 0.12f;

        [Header("Drift parallax (ignored by Baseline)")]
        [Tooltip("Degrees of slow yaw orbit around the player, purely for motion parallax.")]
        public float driftYawAmplitude = 4f;
        public float driftPitchAmplitude = 1.5f;
        [Tooltip("Seconds per full drift cycle - driven UNSCALED so bullet-time doesn't freeze it.")]
        public float driftPeriod = 2.6f;
        [Tooltip("Degrees of phase between yaw and pitch - 90 traces a shallow ellipse, not a sweep.")]
        public float driftPhaseOffset = 90f;
        public float driftRampIn = 0.2f;
        public float driftRampOut = 0.1f;
        [Tooltip("Open test question: freeze the drift while look input is active. Expected OFF.")]
        public bool pauseDriftWhileAiming = false;

        [Header("Dolly (Variant C)")]
        [Tooltip("ON: the energy dial keeps FOV constant and dollies the camera back instead.")]
        public bool dollyInsteadOfZoom = false;
        [Tooltip("otsBack at a fully-dialled shot - eased across the energy range.")]
        public float dollyMaxBack = 5f;
        public float dollySmoothTime = 0.15f;
    }
}
