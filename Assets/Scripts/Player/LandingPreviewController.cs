using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Player
{
    public enum PredictionMode
    {
        Ghost,
        Trail,
        Crosshair,
        None
    }

    public class LandingPreviewController : MonoBehaviour
    {
        [Header("Visual Groups (wired by setup)")]
        public GameObject ghostGroup;
        public float ghostGroundOffset = 0f;
        public GameObject trailGroup;
        public GameObject crosshairGroup;
        public float markerGroundOffset = -0.5f;
        public Transform[] trailDots;
        public float maxDotSpacing = 1f;

        [Header("HUD (cross-instance wired after both prefabs save)")]
        public Text modeLabel;

        // The prediction recomputes from scratch every frame, so as charge (or aim) changes even
        // slightly, the landing point can legitimately move to a different platform entirely
        // between one frame and the next - snapping straight there each time is what was still
        // reading as "flickering" even after the didLand debounce above (which only covers the
        // show/hide edge, not the point jumping around while shown). Smoothing glides between
        // successive targets instead of popping; snapDistance still pops instantly across a
        // genuinely large jump (e.g. aim swung to a totally different spot) rather than visibly
        // flying the marker across the level.
        // Ghost/Crosshair still visibly flickered even with smoothing in place - the likely
        // reason is snapDistance was set far too low (4): near max charge, range grows with
        // roughly the SQUARE of launch speed, so even a small per-frame charge increase can
        // legitimately swing the predicted landing point by several units, and crossing a
        // platform-gap boundary is a genuinely discontinuous jump on top of that. Either could
        // easily exceed 4 units most frames, which means the "instant snap" branch below was
        // firing almost constantly instead of the rare "big deliberate jump" case it was meant
        // for - defeating the smoothing entirely. Raised well above realistic frame-to-frame
        // drift so smoothing actually applies in the normal case; it still exists as a genuine
        // safety net for aim being swung to a completely different part of the level.
        public float positionSmoothTime = 0.05f;
        public float snapDistance = 25f;

        [Header("Temporary mode restriction")]
        // Ghost/Crosshair disabled temporarily via code - flip this back to true to restore full
        // West/East mode switching. Trail is the default (and, while this is false, the only
        // real option besides None) so SetMode silently ignores requests for the other two.
        public bool ghostAndCrosshairEnabled = false;

        PredictionMode currentMode = PredictionMode.Trail;
        bool isVisible;
        bool hasLanding = true;
        Vector3 ghostSmoothVelocity;
        Vector3 crosshairSmoothVelocity;

        public PredictionMode CurrentMode => currentMode;

        void Start()
        {
            UpdateLabel();
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            ApplyModeVisibility();
        }

        public void SetMode(PredictionMode mode)
        {
            if (!ghostAndCrosshairEnabled && (mode == PredictionMode.Ghost || mode == PredictionMode.Crosshair)) return;

            currentMode = mode;
            ApplyModeVisibility();
            UpdateLabel();
        }

        // trajectory/trajectoryCount: the actual simulated arc (see KineticCubeController.
        // PredictLandingPoint) - the trail follows this arc rather than a straight line to
        // the landing point. Falls back to a straight lerp if no trajectory data is supplied.
        // landingPoint is now the cube's own CENTER at rest (PredictLandingPoint accounts for
        // its size via BoxCast), so the ghost - itself a cube - can sit there directly; the
        // crosshair marks the actual ground/platform surface, half the cube's height below that.
        // didLand: false when the shot falls into a gap with nothing to land on - Ghost/Crosshair
        // mark an actual landing SPOT, which doesn't exist for a miss, so they stay hidden in
        // that case (handled in ApplyModeVisibility). The Trail still shows the arc trailing off,
        // since "here's the path, and it doesn't land anywhere" is still meaningful information.
        public void SetLandingPoint(Vector3 lineStart, Vector3 landingPoint, Vector3[] trajectory, int trajectoryCount, bool didLand)
        {
            // Follows didLand directly, no debounce - an earlier version required several
            // consecutive "true" frames before showing, meant to filter single-frame flicker, but
            // charge climbs continuously while aiming and a platform's actual valid-landing charge
            // window can be narrower than that many frames' worth of charge increase, especially
            // for a close platform tightly bounded by undershoot on one side and the next gap on
            // the other - it could never accumulate enough consecutive good frames to ever show,
            // while a long shot backstopped by Level1's (wide, effectively open-ended until max
            // charge) safety floor easily could. That's what showed up as "impossible to make the
            // visuals appear unless the platform is incredibly far away". The flicker that
            // debounce was meant to fix turned out to be the finish-pad Z-fighting bug (a
            // rendering issue, fixed separately) - didLand genuinely changing as a monotonically
            // increasing charge sweeps past a real boundary isn't noise to be filtered.
            hasLanding = didLand;
            ApplyModeVisibility();

            if (hasLanding)
            {
                if (ghostGroup != null)
                {
                    MoveSmoothly(ghostGroup.transform, landingPoint + Vector3.up * ghostGroundOffset, ref ghostSmoothVelocity);
                }
                if (crosshairGroup != null)
                {
                    MoveSmoothly(crosshairGroup.transform, landingPoint + Vector3.up * markerGroundOffset, ref crosshairSmoothVelocity);
                }
            }

            if (trailDots == null || trailDots.Length == 0) return;

            // Real arc length (summed segment distance), not the straight-line chord to
            // landingPoint - a lofted shot's actual path is meaningfully longer than the chord
            // between its endpoints, and using the chord here would under-count how many dots are
            // needed to keep gaps under maxDotSpacing along the visible curve.
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

            int neededDots = Mathf.Clamp(Mathf.CeilToInt(totalLength / Mathf.Max(maxDotSpacing, 0.01f)), 1, trailDots.Length);

            if (trajectory != null && trajectoryCount > 1)
            {
                PlaceDotsAlongTrajectory(trajectory, trajectoryCount, totalLength, neededDots);
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

                    float t = (float)(i + 1) / (neededDots + 1);
                    trailDots[i].position = Vector3.Lerp(lineStart, landingPoint, t);
                }
            }
        }

        // Places each dot exactly ON the simulated trajectory, evenly spaced by REAL distance
        // travelled rather than by array index. Sampling by index (the previous approach) is
        // uniform in simulation TIME, not distance - since the cube moves at very different
        // speeds along a lofted arc (fast near launch/landing, slower near the apex), that bunched
        // dots together where it moves slowly and spread them out where it moves fast, so the
        // dots didn't evenly trace the real path the player would actually fly. Walking the
        // trajectory once and linearly interpolating between the two adjacent simulated points
        // (only Time.fixedDeltaTime apart, so this is effectively exactly on the curve) fixes
        // both: correct spacing AND exact correspondence to the real flight path.
        void PlaceDotsAlongTrajectory(Vector3[] trajectory, int trajectoryCount, float totalLength, int neededDots)
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

                float targetLength = totalLength * (d + 1) / (neededDots + 1);

                while (segmentEnd < trajectoryCount - 1 && segmentStartLength + segmentLength < targetLength)
                {
                    segmentStartLength += segmentLength;
                    segmentEnd++;
                    segmentLength = Vector3.Distance(trajectory[segmentEnd - 1], trajectory[segmentEnd]);
                }

                float segmentT = segmentLength > 0.0001f
                    ? Mathf.Clamp01((targetLength - segmentStartLength) / segmentLength)
                    : 0f;

                trailDots[d].position = Vector3.Lerp(trajectory[segmentEnd - 1], trajectory[segmentEnd], segmentT);
            }
        }

        void MoveSmoothly(Transform t, Vector3 target, ref Vector3 velocity)
        {
            float jump = Vector3.Distance(t.position, target);

            if (jump > snapDistance)
            {
                t.position = target;
                velocity = Vector3.zero;
                return;
            }
            t.position = Vector3.SmoothDamp(t.position, target, ref velocity, positionSmoothTime);
        }

        void ApplyModeVisibility()
        {
            ghostGroup?.SetActive(isVisible && currentMode == PredictionMode.Ghost && hasLanding);
            trailGroup?.SetActive(isVisible && currentMode == PredictionMode.Trail);
            crosshairGroup?.SetActive(isVisible && currentMode == PredictionMode.Crosshair && hasLanding);
        }

        void UpdateLabel()
        {
            if (modeLabel == null) return;
            modeLabel.text = ghostAndCrosshairEnabled
                ? $"West: Ghost   North: Trail   East: Crosshair   South: None   (current: {currentMode})"
                : $"North: Trail   South: None   (current: {currentMode})";
        }
    }
}
