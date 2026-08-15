using UnityEngine;

namespace KineticEnergy.Player
{
    public enum PredictionMode
    {
        None,
        // The dotted arc alone - the grounded aim's default preview.
        Trail,
        // The dotted arc plus the cross-and-ring reticle on the landing face - forced on by
        // the midair first-person aim.
        TrailAndCrosshair,
    }

    // Drives the launch preview visuals: a chain of dots laid along the ACTUAL simulated
    // trajectory (see KineticCubeController.PredictLandingPoint) and a cross-and-ring marker
    // lying flat against whatever face the shot would land on.
    public class LandingPreviewController : MonoBehaviour
    {
        [Header("Visual Groups (wired by setup)")]
        public GameObject trailGroup;
        public GameObject crosshairGroup;
        [Tooltip("Offset along the landing face's normal - negative moves the marker from the resting center onto the surface.")]
        public float markerGroundOffset = -0.5f;
        [Tooltip("Small extra lift off the landing face so the marker never clips into it.")]
        public float markerSurfaceLift = 0.06f;
        public Transform[] trailDots;
        [Tooltip("Maximum gap between adjacent trail dots, in meters of real arc length.")]
        public float maxDotSpacing = 1f;

        [Tooltip("What the preview shows before any runtime SetMode call.")]
        public PredictionMode initialMode = PredictionMode.Trail;

        [Header("Anti-jitter (visual smoothing only - the prediction itself is untouched)")]
        [Tooltip("Seconds of positional smoothing on the landing cursor. Short enough to stay under perceptible input latency, long enough to absorb per-frame prediction wobble.")]
        public float cursorSmoothTime = 0.055f;
        [Tooltip("A cursor whose target jumps farther than this snaps instantly instead of gliding - a landing teleporting across geometry must not sweep the marker through the air.")]
        public float smoothSnapDistance = 3f;
        [Tooltip("The cursor survives losing the landing for this long, holding its last valid spot - single-frame prediction misses no longer blink it out.")]
        public float cursorHideGraceSeconds = 0.12f;
        [Tooltip("Per-frame target movement at or below this gets FULL smoothing; faster (deliberate) sweeps proportionally bypass it - far dots and the cursor track camera turns raw instead of breaking away behind them.")]
        public float smoothNoiseReference = 0.08f;

        PredictionMode currentMode = PredictionMode.Trail;
        bool isVisible;
        bool hasLanding = true;

        // Anti-jitter state.
        Vector3 cursorVelocity;
        bool cursorWasShown;
        float noLandingTimer;
        Vector3 lastCursorPosition;
        Quaternion lastCursorRotation = Quaternion.identity;
        Vector3 cursorPrevTarget;
        float smoothedArcLength;

        float SmoothDt => Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);

        // Noise moves a target millimetres per frame; a deliberate camera sweep moves the
        // far targets much more. The smoothing time shrinks in proportion, so shimmer is
        // absorbed while intentional motion tracks essentially raw - the fixed smoothing
        // made the far dots and cursor visibly break away during turns.
        float AdaptiveTau(float baseTau, float targetDelta)
        {
            return baseTau * Mathf.Clamp01(smoothNoiseReference / Mathf.Max(targetDelta, 0.0001f));
        }

        public PredictionMode CurrentMode => currentMode;

        void Awake()
        {
            currentMode = initialMode;
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            if (!visible)
            {
                // A fresh aim must SNAP its visuals into place, never glide from where
                // the previous aim left them.
                cursorWasShown = false;
                noLandingTimer = 0f;
                smoothedArcLength = 0f;
            }
            ApplyModeVisibility();
        }

        public void SetMode(PredictionMode mode)
        {
            currentMode = mode;
            ApplyModeVisibility();
        }

        // trajectory/trajectoryCount: the simulated arc the dots follow. didLand: false when
        // the shot trails off into a bottomless gap - the crosshair marks an actual landing
        // SPOT, which doesn't exist then, so it hides; the trail still shows the arc, since
        // "here's the path, and it lands nowhere" is still meaningful. landingNormal orients
        // the marker flush against the landing face (wall, floor, ceiling alike).
        public void SetLandingPoint(Vector3 lineStart, Vector3 landingPoint, Vector3[] trajectory, int trajectoryCount, bool didLand, Vector3 landingNormal = default)
        {
            // GRACE on losing the landing: single-frame prediction misses used to blink
            // the cursor out (and a moving camera made it pop in late) - the cursor now
            // holds its last valid spot briefly and only hides if the miss persists.
            if (didLand)
            {
                noLandingTimer = 0f;
                Vector3 normal = landingNormal.sqrMagnitude > 0.0001f ? landingNormal.normalized : Vector3.up;
                lastCursorPosition = landingPoint + normal * (markerGroundOffset + markerSurfaceLift);
                lastCursorRotation = Quaternion.FromToRotation(Vector3.up, normal);
            }
            else
            {
                noLandingTimer += SmoothDt;
            }
            hasLanding = didLand || (cursorWasShown && noLandingTimer <= cursorHideGraceSeconds);
            ApplyModeVisibility();

            // The cursor follows the prediction through a VERY short smoothing (~3 frames):
            // under the latency a hand can feel, but enough to absorb the per-frame
            // prediction wobble that read as heavy cursor jitter. Far jumps (the landing
            // teleporting to different geometry) and fresh appearances still SNAP.
            if (hasLanding && crosshairGroup != null)
            {
                float cursorTargetDelta = Vector3.Distance(cursorPrevTarget, lastCursorPosition);
                float cursorTau = AdaptiveTau(cursorSmoothTime, cursorTargetDelta);
                if (!cursorWasShown || cursorTau <= 0.001f
                    || Vector3.Distance(crosshairGroup.transform.position, lastCursorPosition) > smoothSnapDistance)
                {
                    crosshairGroup.transform.position = lastCursorPosition;
                    crosshairGroup.transform.rotation = lastCursorRotation;
                    cursorVelocity = Vector3.zero;
                }
                else
                {
                    crosshairGroup.transform.position = Vector3.SmoothDamp(
                        crosshairGroup.transform.position, lastCursorPosition,
                        ref cursorVelocity, cursorTau, Mathf.Infinity, SmoothDt);
                    crosshairGroup.transform.rotation = Quaternion.Slerp(
                        crosshairGroup.transform.rotation, lastCursorRotation,
                        Mathf.Clamp01(SmoothDt / Mathf.Max(cursorTau, 0.0001f)));
                }
            }
            cursorPrevTarget = lastCursorPosition;
            cursorWasShown = hasLanding;

            if (trailDots == null || trailDots.Length == 0) return;

            // Real arc length (summed segment distance), not the straight-line chord - a
            // lofted shot's path is meaningfully longer than the chord between its endpoints.
            float totalLength = 0f;
            if (trajectory != null && trajectoryCount > 1)
            {
                for (int i = 1; i < trajectoryCount; i++)
                {
                    totalLength += Vector3.Distance(trajectory[i - 1], trajectory[i]);
                }
            }
            else
            {
                totalLength = Vector3.Distance(lineStart, landingPoint);
            }

            // The DOT COUNT derives from a time-smoothed arc length: the raw length
            // wobbles every frame, and count flapping blinked the tail dots on and off.
            smoothedArcLength = smoothedArcLength <= 0f
                ? totalLength
                : Mathf.Lerp(smoothedArcLength, totalLength, Mathf.Clamp01(SmoothDt / 0.08f));
            // FIXED arc-length spacing anchors every dot to the geometry. The old layout
            // divided the (wobbling) total length evenly among the dots, so every length
            // breath slid ALL of them along the arc - the whole line shimmered lengthwise.
            // At a constant interval, a length wobble only ever touches the tail dot.
            float dotSpacing = Mathf.Max(maxDotSpacing, 0.01f);
            int neededDots = Mathf.Clamp(Mathf.FloorToInt(smoothedArcLength / dotSpacing), 1, trailDots.Length);

            // A small FIXED buffer keeps the last dot from sitting exactly on the marker,
            // without growing with trajectory length.
            float endGap = Mathf.Min(0.2f, totalLength * 0.5f);
            float usableLength = Mathf.Max(totalLength - endGap, 0f);

            if (trajectory != null && trajectoryCount > 1)
            {
                PlaceDotsAlongTrajectory(trajectory, trajectoryCount, usableLength, neededDots, dotSpacing);
            }
            else
            {
                for (int i = 0; i < trailDots.Length; i++)
                {
                    if (trailDots[i] == null) continue;

                    if (i >= neededDots)
                    {
                        trailDots[i].gameObject.SetActive(false);
                        continue;
                    }
                    trailDots[i].gameObject.SetActive(true);

                    float t = totalLength > 0.0001f
                        ? Mathf.Min(dotSpacing * (i + 1), usableLength) / totalLength
                        : 0f;
                    trailDots[i].position = Vector3.Lerp(lineStart, landingPoint, t);
                }
            }
        }

        // Places each dot exactly ON the simulated trajectory, evenly spaced by REAL distance
        // travelled - sampling by array index would be uniform in simulation time instead,
        // bunching dots where the cube moves slowly (the apex) and spreading them where it
        // moves fast.
        void PlaceDotsAlongTrajectory(Vector3[] trajectory, int trajectoryCount, float usableLength, int neededDots, float dotSpacing)
        {
            int segmentEnd = 1;
            float segmentStartLength = 0f;
            float segmentLength = Vector3.Distance(trajectory[0], trajectory[1]);

            for (int d = 0; d < trailDots.Length; d++)
            {
                if (trailDots[d] == null) continue;

                if (d >= neededDots)
                {
                    trailDots[d].gameObject.SetActive(false);
                    continue;
                }
                trailDots[d].gameObject.SetActive(true);

                float targetLength = Mathf.Min(dotSpacing * (d + 1), usableLength);

                while (segmentEnd < trajectoryCount - 1 && segmentStartLength + segmentLength < targetLength)
                {
                    segmentStartLength += segmentLength;
                    segmentEnd++;
                    segmentLength = Vector3.Distance(trajectory[segmentEnd - 1], trajectory[segmentEnd]);
                }

                float segmentT = segmentLength > 0.0001f
                    ? Mathf.Clamp01((targetLength - segmentStartLength) / segmentLength)
                    : 0f;

                // Placed RAW on the simulated arc, always: per-dot positional smoothing
                // lagged each dot by a different amount while the aim moved, which BENT
                // the line off the true path. Dots live exactly on the arc; the visual
                // calm comes from the fixed spacing and the smoothed count instead.
                trailDots[d].position = Vector3.Lerp(trajectory[segmentEnd - 1], trajectory[segmentEnd], segmentT);
            }
        }

        void ApplyModeVisibility()
        {
            trailGroup?.SetActive(isVisible && (currentMode == PredictionMode.Trail || currentMode == PredictionMode.TrailAndCrosshair));
            crosshairGroup?.SetActive(isVisible && currentMode == PredictionMode.TrailAndCrosshair && hasLanding);
        }
    }
}
