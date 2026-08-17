using UnityEngine;
using UnityEngine.InputSystem;
using KineticEnergy.Level;

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
        [Tooltip("Extra nudge of the marker TOWARD the camera - the flat marker would otherwise sink its outer ring into curved or edged landings (target spheres, platform corners).")]
        public float markerCameraOffset = 0.12f;
        [Tooltip("Additional camera-ward nudge per metre of viewing distance, covering depth-buffer precision loss on far landings.")]
        public float markerCameraOffsetPerMetre = 0.004f;
        public Transform[] trailDots;
        [Tooltip("Maximum gap between adjacent trail dots, in meters of real arc length.")]
        public float maxDotSpacing = 1f;

        [Tooltip("What the preview shows before any runtime SetMode call.")]
        public PredictionMode initialMode = PredictionMode.Trail;

        [Header("Anti-jitter (visual smoothing only - the prediction itself is untouched)")]
        [Tooltip("Seconds of positional smoothing on the landing cursor. Short enough to stay under perceptible input latency, long enough to absorb per-frame prediction wobble.")]
        public float cursorSmoothTime = 0.09f;
        [Tooltip("A cursor whose target jumps farther than this snaps instantly instead of gliding - a landing teleporting across geometry must not sweep the marker through the air.")]
        public float smoothSnapDistance = 3f;
        [Tooltip("The cursor survives losing the landing for this long, holding its last valid spot - single-frame prediction misses no longer blink it out.")]
        public float cursorHideGraceSeconds = 0.12f;

        [Header("Landing Arrow (V / D-pad Left toggles)")]
        [Tooltip("Scene gate: the landing arrow (and its V / D-pad Left toggle) only exist where this is ON - the aim-lab scene for now, so the toggle can't collide with the variant-cycling keys elsewhere.")]
        public bool landingArrowAvailable = false;
        [Tooltip("The blue arrow hovering over the landing zone, billboarded to the viewer. On by default where the arrow is available; V or D-pad Left flips it.")]
        public bool landingArrowEnabled = true;
        public Color landingArrowColor = new Color(0.25f, 0.55f, 1f, 0.95f);
        [Tooltip("Arrow length in world units - a fixed editor value (3/4 of the cursor ring's 1.5 diameter), never measured at runtime.")]
        public float arrowLength = 1.125f;
        [Tooltip("Per-frame target movement at or below this gets FULL smoothing; faster (deliberate) sweeps proportionally bypass it - far dots and the cursor track camera turns raw instead of breaking away behind them.")]
        public float smoothNoiseReference = 0.12f;

        [Header("Landing outcome colours")]
        [Tooltip("Dots + cursor while the predicted landing is SUCCESSFUL: no respawn hazard, and not the side of grounded geometry (floating objects' sides are fine - you stick and relaunch).")]
        public Color successColor = new Color(0.25f, 0.95f, 0.62f, 0.95f);
        [Tooltip("Dots + cursor while the landing would fail - a hazard, the side of non-floating geometry, or no landing at all.")]
        public Color failColor = new Color(1f, 0.32f, 0.44f, 0.95f);
        [Tooltip("The arrow's darker take on the success colour - same relationship to the dots the blue arrow had to the yellow.")]
        public Color successArrowColor = new Color(0.08f, 0.5f, 0.32f, 0.95f);
        [Tooltip("The arrow's darker take on the fail colour.")]
        public Color failArrowColor = new Color(0.58f, 0.13f, 0.22f, 0.95f);

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
        Vector3 lastValidNormal = Vector3.up;
        float smoothedArcLength;

        // The landing arrow, built ONCE from the serialized arrowLength - no measuring,
        // no per-frame sizing. Material cloned blue from the cursor's own.
        GameObject landingArrowRoot;

        // Outcome tint state. Property blocks tint per-RENDERER, so the shared aim
        // materials (assets) are never mutated.
        bool outcomeSuccess;
        bool outcomeTintApplied;
        MaterialPropertyBlock tintBlock;
        Renderer[] dotRenderers;
        Renderer[] cursorRenderers;
        readonly System.Collections.Generic.List<Renderer> arrowRenderers = new System.Collections.Generic.List<Renderer>();
        // Steepness boundary between "a face you rest on" and "a side" - the same 0.7
        // dot-with-up the wall-launch stake uses, so the colours agree with the economy.
        const float SteepSurfaceDot = 0.7f;
        // Guaranteed clearance off the landing face, whatever the serialized lift says.
        const float MinSurfaceLift = 0.15f;

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
                if (landingArrowRoot != null) landingArrowRoot.SetActive(false);
            }
            ApplyModeVisibility();
        }

        public void SetMode(PredictionMode mode)
        {
            currentMode = mode;
            ApplyModeVisibility();
        }

        void Update()
        {
            // The landing-arrow toggle: V / D-pad Left, in the scenes that carry the arrow.
            if (!landingArrowAvailable) return;
            bool togglePressed = (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);
            if (togglePressed)
            {
                landingArrowEnabled = !landingArrowEnabled;
                if (!landingArrowEnabled && landingArrowRoot != null) landingArrowRoot.SetActive(false);
            }
        }

        // Built ONCE from the serialized arrowLength - nothing is measured or recomputed
        // at runtime. The material is the cursor's own, cloned and tinted blue.
        void EnsureLandingArrow()
        {
            if (landingArrowRoot != null || crosshairGroup == null || arrowLength <= 0.01f) return;

            Renderer[] cursorRenderers = crosshairGroup.GetComponentsInChildren<Renderer>(true);
            if (cursorRenderers.Length == 0) return;

            Material arrowMaterial = new Material(cursorRenderers[0].sharedMaterial);
            arrowMaterial.color = landingArrowColor;

            // Tip at the local ORIGIN, body extending up +Y: a shaft plus two angled
            // wings forming the point. All thin boxes sharing the blue material.
            landingArrowRoot = new GameObject("LandingArrow");
            landingArrowRoot.transform.SetParent(transform, false);
            AddArrowPart("Shaft", new Vector3(0f, arrowLength * 0.62f, 0f), Quaternion.identity,
                new Vector3(arrowLength * 0.16f, arrowLength * 0.76f, arrowLength * 0.06f), arrowMaterial);
            AddArrowPart("WingLeft", new Vector3(-arrowLength * 0.14f, arrowLength * 0.17f, 0f),
                Quaternion.Euler(0f, 0f, -45f),
                new Vector3(arrowLength * 0.42f, arrowLength * 0.14f, arrowLength * 0.06f), arrowMaterial);
            AddArrowPart("WingRight", new Vector3(arrowLength * 0.14f, arrowLength * 0.17f, 0f),
                Quaternion.Euler(0f, 0f, 45f),
                new Vector3(arrowLength * 0.42f, arrowLength * 0.14f, arrowLength * 0.06f), arrowMaterial);
            landingArrowRoot.SetActive(false);

            // The freshly built parts must catch up with the current outcome tint.
            outcomeTintApplied = false;
            ApplyOutcomeTint();
        }

        void AddArrowPart(string name, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(landingArrowRoot.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Renderer partRenderer = part.GetComponent<Renderer>();
            partRenderer.sharedMaterial = material;
            arrowRenderers.Add(partRenderer);
        }

        // ---------- Landing outcome (colour) ----------

        // A landing SUCCEEDS unless it respawns you (hazard surfaces, or no landing at
        // all) or it's the SIDE of an object that won't hold you. StickySurface is the
        // authority on holding: carrying the component makes EVERY face of that object
        // safe, whatever its sticky value - the same GetComponentInParent lookup the
        // controller's own crash-stick uses, so the colours can't disagree with it.
        bool LandingIsSuccessful(Collider landing, Vector3 normal)
        {
            if (landing == null) return false;
            if (landing.GetComponentInParent<DamageWalls>() != null) return false;
            if (landing.GetComponentInParent<DeathWall>() != null) return false;
            if (landing.GetComponentInParent<StickySurface>() != null) return true;
            // No sticky component: only the up-facing side is a landing - the one where
            // you end up grounded. Any true side face drops you.
            return Vector3.Dot(normal, Vector3.up) >= SteepSurfaceDot;
        }

        // Tints dots + cursor with the outcome colour and the arrow with its darker
        // sibling - via property blocks, so the shared aim material assets stay untouched.
        void ApplyOutcomeTint()
        {
            if (outcomeTintApplied) return;
            outcomeTintApplied = true;

            if (dotRenderers == null && trailDots != null)
            {
                dotRenderers = new Renderer[trailDots.Length];
                for (int i = 0; i < trailDots.Length; i++)
                {
                    if (trailDots[i] != null) dotRenderers[i] = trailDots[i].GetComponentInChildren<Renderer>(true);
                }
            }
            if (cursorRenderers == null && crosshairGroup != null)
            {
                cursorRenderers = crosshairGroup.GetComponentsInChildren<Renderer>(true);
            }

            Color main = outcomeSuccess ? successColor : failColor;
            Color arrow = outcomeSuccess ? successArrowColor : failArrowColor;
            TintRenderers(dotRenderers, main);
            TintRenderers(cursorRenderers, main);
            TintRenderers(arrowRenderers.ToArray(), arrow);
        }

        void TintRenderers(Renderer[] renderers, Color color)
        {
            if (renderers == null) return;
            if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
            foreach (Renderer target in renderers)
            {
                if (target == null) continue;
                target.GetPropertyBlock(tintBlock);
                tintBlock.SetColor("_BaseColor", color); // URP shaders
                tintBlock.SetColor("_Color", color);     // legacy/unlit fallbacks
                target.SetPropertyBlock(tintBlock);
            }
        }

        // Hovers over the landing marker along its surface normal, tip toward the zone,
        // BILLBOARDED: rotated around the normal so its face always turns to the viewer.
        void UpdateLandingArrow()
        {
            if (!landingArrowAvailable || !landingArrowEnabled)
            {
                if (landingArrowRoot != null && landingArrowRoot.activeSelf) landingArrowRoot.SetActive(false);
                return;
            }
            EnsureLandingArrow();
            if (landingArrowRoot == null) return;

            bool show = isVisible && hasLanding && currentMode == PredictionMode.TrailAndCrosshair;
            if (landingArrowRoot.activeSelf != show) landingArrowRoot.SetActive(show);
            if (!show) return;

            Vector3 tip = lastCursorPosition + lastValidNormal * (arrowLength * 0.18f);
            Quaternion facing = Quaternion.FromToRotation(Vector3.up, lastValidNormal);
            UnityEngine.Camera view = UnityEngine.Camera.main;
            if (view != null)
            {
                Vector3 toCamera = Vector3.ProjectOnPlane(view.transform.position - tip, lastValidNormal);
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    facing = Quaternion.LookRotation(toCamera.normalized, lastValidNormal);
                }
            }
            landingArrowRoot.transform.SetPositionAndRotation(tip, facing);
        }

        // trajectory/trajectoryCount: the simulated arc the dots follow. didLand: false when
        // the shot trails off into a bottomless gap - the crosshair marks an actual landing
        // SPOT, which doesn't exist then, so it hides; the trail still shows the arc, since
        // "here's the path, and it lands nowhere" is still meaningful. landingNormal orients
        // the marker flush against the landing face (wall, floor, ceiling alike).
        public void SetLandingPoint(Vector3 lineStart, Vector3 landingPoint, Vector3[] trajectory, int trajectoryCount, bool didLand, Vector3 landingNormal = default, Collider landingCollider = null, bool aimBlocked = false)
        {
            // Outcome colour: judged on real landings; a lost landing keeps its colour
            // through the same grace the cursor gets (single-frame prediction misses must
            // not flash red), then settles on fail - no landing means a fall reset.
            if (aimBlocked)
            {
                // Fired into the surface you are clinging to - the shot has nowhere to go,
                // whatever the simulation reports it lands on.
                if (outcomeSuccess)
                {
                    outcomeSuccess = false;
                    outcomeTintApplied = false;
                }
            }
            else if (didLand)
            {
                Vector3 judgedNormal = landingNormal.sqrMagnitude > 0.0001f ? landingNormal.normalized : lastValidNormal;
                bool success = LandingIsSuccessful(landingCollider, judgedNormal);
                if (success != outcomeSuccess)
                {
                    outcomeSuccess = success;
                    outcomeTintApplied = false;
                }
            }
            else if (noLandingTimer > cursorHideGraceSeconds && outcomeSuccess)
            {
                outcomeSuccess = false;
                outcomeTintApplied = false;
            }
            ApplyOutcomeTint();

            // GRACE on losing the landing: single-frame prediction misses used to blink
            // the cursor out (and a moving camera made it pop in late) - the cursor now
            // holds its last valid spot briefly and only hides if the miss persists.
            if (didLand)
            {
                noLandingTimer = 0f;
                // A degenerate normal keeps the LAST valid one instead of snapping to
                // world-up: the marker lies flat on the surface, so a one-frame flip
                // turned it edge-on to the camera - invisible for exactly a frame or two.
                if (landingNormal.sqrMagnitude > 0.0001f) lastValidNormal = landingNormal.normalized;
                Vector3 normal = lastValidNormal;
                // The marker sits markerGroundOffset below the landing point (the cube's
                // RESTING CENTRE) plus a lift off the face. With the serialized 0.06 lift
                // that margin is ~6cm - small enough that ordinary prediction variance
                // buried the marker inside the surface, which is the other way it
                // vanished for a frame. A code-side minimum keeps real clearance.
                float lift = Mathf.Max(markerSurfaceLift, MinSurfaceLift);
                lastCursorPosition = landingPoint + normal * (markerGroundOffset + lift);
                lastCursorRotation = Quaternion.FromToRotation(Vector3.up, normal);

                // ...and a nudge toward the CAMERA on top of that. The marker is FLAT,
                // so on anything non-planar - a target sphere, a platform edge or corner
                // - its outer ring is inside the geometry however the normal lift is
                // tuned, which is the clipping (direct diagnosis). A view-direction
                // offset makes it win the depth test at its own pixels whatever the
                // surface curves like; the distance term also keeps far landings clear
                // of depth-buffer precision loss. Far too small to shift where the
                // marker reads on screen.
                UnityEngine.Camera view = UnityEngine.Camera.main;
                if (view != null)
                {
                    Vector3 toCamera = view.transform.position - lastCursorPosition;
                    float camDistance = toCamera.magnitude;
                    if (camDistance > 0.01f)
                    {
                        float nudge = markerCameraOffset + camDistance * markerCameraOffsetPerMetre;
                        lastCursorPosition += toCamera / camDistance * nudge;
                    }
                }
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
            UpdateLandingArrow();

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
