using UnityEngine;

namespace KineticEnergy.Camera
{
    public enum AimCameraVariant
    {
        Baseline,           // A - frozen first person, FOV zoom (current behaviour)
        OtsParallax,        // B - over-the-shoulder with the subtle drift parallax
        BaselinePip,        // C - A plus the landing picture-in-picture window
        OtsParallaxPip,     // D - B plus the landing picture-in-picture window
        FreeLookFirstPerson,// E - A, but WASD / right stick rotates the VIEW without moving the aim (energy on RB/LB)
        FreeLookOts,        // F - the same free-look concept on the OTS camera
    }

    // All tuning for one midair-aim camera variant, kept as an asset so the numbers live
    // in the Inspector rather than code - tune without a recompile. Three of these (A/B/C)
    // are referenced by AimCameraVariantController; the depth-perception playtest cycles
    // between them at runtime.
    [CreateAssetMenu(menuName = "Kinetic Energy/Aim Camera Preset")]
    public class AimCameraPreset : ScriptableObject
    {
        public AimCameraVariant variant = AimCameraVariant.Baseline;
        [Tooltip("Shown on the HUD tag and the pause-menu selector, e.g. \"OTS + parallax\".")]
        public string displayName = "Baseline";

        // Which subsystems this variant runs - derived from the variant so the camera and
        // the PiP owner don't each hardcode the mapping.
        public bool UsesOverShoulder => variant == AimCameraVariant.OtsParallax
            || variant == AimCameraVariant.OtsParallaxPip
            || variant == AimCameraVariant.FreeLookOts;
        public bool UsesPip => pipEnabled;
        // E/F: WASD / right stick rotates the view only (aim untouched); the energy dial
        // moves to RB (add) / LB (remove) because the right stick is busy free-looking.
        public bool UsesFreeLook => variant == AimCameraVariant.FreeLookFirstPerson
            || variant == AimCameraVariant.FreeLookOts;

        [Header("Over-the-shoulder placement (ignored by Baseline)")]
        [Tooltip("Camera distance from the player (metres) at rest.")]
        public float otsBack = 1.4f;
        [Tooltip("CAP on the zoom pull-back distance. The actual distance is derived from the live FOV so the player's on-screen size stays CONSTANT across the dial - this only stops extreme zooms from pushing the camera into far geometry.")]
        public float otsBackZoomed = 5f;
        [Tooltip("How much of the full optical (FOV) zoom the OTS variants use at max dial (1 = the first-person view's full zoom). Safe to keep high - the constant-size player pin and pull-back carry the readability.")]
        [Range(0.2f, 1f)] public float zoomFovFraction = 0.92f;
        [Tooltip("Zoom response curve: 1 = linear, higher = the zoom stays gentle early and ramps hard toward full energy - a big felt difference between min and max charge.")]
        [Range(1f, 3f)] public float zoomCurveExponent = 1.5f;
        [Tooltip("EXACT viewport point the player is pinned to, every frame, at any aim angle (0,0 = bottom-left of the screen). The shoulder swap mirrors the X around centre.")]
        public Vector2 playerViewportAnchor = new Vector2(0.22f, 0.24f);
        [Tooltip("LEGACY (dolly experiment) - no longer used.")]
        [Range(0.1f, 0.9f)] public float zoomDollyPortion = 0.35f;
        [Tooltip("The anchor at FULL energy zoom: the player glides toward (and partly past) the corner as the optical zoom magnifies them, so only a partial slice stays visible and the target lane is clear. Lower values = less player visible.")]
        public Vector2 playerViewportAnchorZoomed = new Vector2(0.05f, 0.07f);
        [Tooltip("LEGACY (pre screen-anchor) - no longer used by the pinned OTS placement.")]
        public float otsRise = 0.35f;
        [Tooltip("LEGACY (pre screen-anchor) - no longer used by the pinned OTS placement.")]
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

        [Header("Free look (E/F)")]
        [Tooltip("The view may rotate at most this many degrees from the default aim view, in any direction (a cone). Pure rotation - the camera never changes position for the free look.")]
        public float freeLookConeAngle = 45f;

        [Header("Landing picture-in-picture (C/D)")]
        [Tooltip("ON: a second camera in the top-left corner watches the landing point from a vantage along the predicted arc, live for the whole midair aim.")]
        public bool pipEnabled = false;
        [Tooltip("Normalized screen rect of the PiP window (origin bottom-left; defaults sit it top-left).")]
        public Rect pipViewport = new Rect(0.015f, 0.675f, 0.30f, 0.30f);
        [Tooltip("Where along the predicted arc the PiP camera sits: 0 = at the player, 1 = at the landing.")]
        [Range(0f, 0.95f)] public float pipArcFraction = 0.55f;
        [Tooltip("Minimum distance the PiP camera keeps from the landing point - short arcs push the vantage back along the arc direction.")]
        public float pipMinDistance = 8f;
        public float pipFieldOfView = 50f;
    }
}
