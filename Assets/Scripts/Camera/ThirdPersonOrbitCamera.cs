using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KineticEnergy.Camera
{
    public class ThirdPersonOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        public float height = 2.5f;

        [Header("Orbit")]
        public float distance = 6f;
        public float rotationSpeed = 120f;
        // Raised in magnitude from -20 - direct request: "allow the camera to be pointed upwards
        // as well, for the vertical segment it was hard not being able to see what was above me".
        // Negative pitch swings the camera BELOW the target, looking UP (see the position formula
        // below: desiredPosition = focusPoint - rotation*forward*distance, and positive pitch
        // tilts forward toward -Y, so negative pitch tilts it toward +Y, putting the camera
        // underneath) - -20 only allowed a shallow 20-degree glance upward, nowhere near enough to
        // see a platform directly overhead. -75 mirrors maxPitch's own margin from the 90-degree
        // gimbal-adjacent instability described below, applied to the opposite pole.
        public float minPitch = -75f;
        // Pulled back from an earlier 89 - that was close enough to true top-down (90) that
        // Quaternion.LookRotation(lookDir, Vector3.up) started reading as visibly "spinning"
        // while rotating: as lookDir approaches anti-parallel to the Vector3.up hint, tiny
        // positional differences (SmoothDamp lag makes the ACTUAL camera position lag behind the
        // theoretical orbit spot, so this isn't just the exact 90-degree instant) flip which way
        // LookRotation resolves roll, which reads as spin. 75 keeps a real margin from that
        // degenerate zone while still allowing a dramatically steep, near-top-down view.
        public float maxPitch = 75f;
        public bool invertY = false;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.08f;
        public float maxDeltaTime = 0.05f;

        [Header("Auto Recenter")]
        // Used by StickAim launches (see KineticCubeController.QueueStickAimLaunch) to swing the
        // camera back behind the player after firing - NOT used by the charge-based schemes,
        // which need the camera to stay exactly where the player left it so the landing-preview
        // trail stays fully visible and un-yanked-at (see LandingPreviewController's own accuracy
        // requirements). Cancels itself the instant the player provides any manual look input, so
        // it never fights the player's own camera control.
        public float recenterSpeed = 240f;

        [Header("Input")]
        public InputActionReference lookAction;

        [Header("First Person Aim")]
        // FastPaced scheme only (see KineticCubeController.UpdateFastPacedScheme) -
        // SetFirstPersonMode collapses the orbit to sit exactly at the focus point instead of
        // orbiting at `distance`, and SetAimZoom narrows the field of view as charge builds so a
        // long-charged shot's distant landing spot stays legible instead of shrinking to a speck
        // - direct request: "the longer you charge the more you need to zoom in on the landing
        // spot". Both are no-ops for every other scheme, which never calls them.
        public float normalFov = 60f;
        public float maxZoomFov = 20f;

        UnityEngine.Camera cam;
        bool firstPerson;

        // The up direction the whole orbit is built around - world up everywhere except after a
        // FastPaced crash onto a tilted platform, where SetUpVector re-bases it to that
        // platform's surface normal (direct request: "the camera's up should be the platform's up
        // that the player crashed onto"). currentUp glides toward targetUp in LateUpdate rather
        // than snapping, so the horizon rolls smoothly over ~a second instead of jump-cutting.
        // All the orbit math below composes tiltRotation (world-up -> currentUp) with the
        // ordinary world-up yaw/pitch rotation, so yaw/pitch keep meaning "turn/look within the
        // current frame of reference" no matter which way that frame is tilted.
        Vector3 targetUp = Vector3.up;
        Vector3 currentUp = Vector3.up;
        public float upAlignSpeed = 3f;

        Quaternion TiltRotation => Quaternion.FromToRotation(Vector3.up, currentUp);

        // World-space direction this camera is currently looking - the FastPaced scheme fires
        // exactly along this, so the shot always goes exactly where the first-person view points.
        public Vector3 AimForward => TiltRotation * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        public void SetUpVector(Vector3 up)
        {
            targetUp = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        }

        public void SetFirstPersonMode(bool enabled)
        {
            firstPerson = enabled;
            if (!enabled) SetAimZoom(0f);
        }

        public void SetAimZoom(float chargeFraction01)
        {
            if (cam == null) cam = GetComponent<UnityEngine.Camera>();
            if (cam == null) return;
            cam.fieldOfView = Mathf.Lerp(normalFov, maxZoomFov, Mathf.Clamp01(chargeFraction01));
        }

        [Header("Wall Occlusion")]
        // "If the camera is looking at a wall from the outside, you should be able to look
        // through the wall" (direct request - commonly called camera occlusion culling). There
        // was no camera-collision handling here at all before this - a wall ending up between
        // the orbit position and the player (e.g. the camera orbits to just outside one of
        // Level2's hallway walls) just blocked the view outright. Hides (doesn't fade) whichever
        // renderers are directly between the camera and the player each frame, restoring them
        // the instant they're no longer in the way.
        public LayerMask occlusionMask = ~0;
        public float occlusionCheckRadius = 0.25f;

        float yaw;
        float pitch = 15f;
        Vector3 velocity;
        bool yawInitialized;
        bool recentering;
        float recenterTargetYaw;

        readonly List<Renderer> occludedRenderers = new List<Renderer>();
        readonly List<Renderer> stillOccludedThisFrame = new List<Renderer>();

        void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
            if (cam != null) cam.fieldOfView = normalFov;
        }

        void Start()
        {
            if (target == null) return;
            if (yawInitialized) return; // already set externally (e.g. LevelGenerator facing the finish) before this ran

            Vector3 offset = transform.position - (target.position + Vector3.up * height);
            if (offset.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
        }

        // Called from LevelGenerator.Awake() - guaranteed to run before this component's own
        // Start() (Unity runs every Awake() in the scene before any Start()), so it always wins
        // over the offset-based auto-calculation above. Also snaps position immediately rather
        // than letting LateUpdate's SmoothDamp ease into the new orbit spot over a few frames,
        // so the camera is already correctly framed on the very first rendered frame instead of
        // visibly sliding into place right as the level appears.
        public void SetInitialYaw(float yawDegrees)
        {
            yaw = yawDegrees;
            yawInitialized = true;

            if (target == null) return;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * height;
            transform.position = focusPoint - rotation * Vector3.forward * distance;

            Vector3 lookDir = focusPoint - transform.position;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }

        // Starts a smooth (not instant) swing of the orbit yaw back to directly behind
        // targetYawDegrees (the player's new facing) - "move behind the player again", not
        // "snap" (that's what SetInitialYaw is for, at level load). Actual interpolation happens
        // in LateUpdate so it can be interrupted cleanly by manual look input at any point.
        public void RecenterBehindTarget(float targetYawDegrees)
        {
            recenterTargetYaw = targetYawDegrees;
            recentering = true;
        }

        void OnEnable()
        {
            lookAction?.action?.Enable();
        }

        void OnDisable()
        {
            lookAction?.action?.Disable();
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (Time.timeScale <= 0f) return;

            Vector2 look = lookAction != null && lookAction.action != null
                ? lookAction.action.ReadValue<Vector2>()
                : Vector2.zero;

            // Unscaled, not Time.deltaTime - Time.deltaTime already shrinks 1:1 with
            // Time.timeScale, which would otherwise make the camera merely ride along with
            // whatever fraction chargeTimeScale happens to be. Direct request is a flat half
            // speed specifically WHENEVER the game isn't at full speed, not a proportional
            // slowdown that tracks the exact charge-time-scale value.
            //
            // The frame right after a scene reload (Restart, or the new fall-reset) can have an
            // abnormally large deltaTime - loading everything (Player/Camera/PauseSystem, plus
            // Level1's platform generation) takes real time before the next frame renders.
            // Multiplied straight into this accumulator, holding the stick at that exact moment
            // (plausible right after falling or hitting Restart) could snap yaw/pitch to a
            // garbage value in one frame, making the camera look broken/unresponsive afterward -
            // still a risk with unscaled time, so the clamp stays.
            bool gameSpeedNotNormal = !Mathf.Approximately(Time.timeScale, 1f);
            float dt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime) * (gameSpeedNotNormal ? 0.5f : 1f);

            // Manual input always wins outright, the instant there is any - recentering only
            // ever happens while the player isn't already telling the camera what to do.
            if (look.sqrMagnitude > 0.0001f) recentering = false;

            if (recentering)
            {
                yaw = Mathf.MoveTowardsAngle(yaw, recenterTargetYaw, recenterSpeed * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, recenterTargetYaw)) < 0.5f) recentering = false;
            }
            else
            {
                yaw += look.x * rotationSpeed * dt;
            }
            float pitchDelta = (invertY ? look.y : -look.y) * rotationSpeed * dt;
            pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);

            // Glide the reference-frame up toward wherever the last crash re-based it (see
            // SetUpVector) - Slerp by a rate*dt fraction gives a fast start that eases out, which
            // reads as the horizon "settling" onto the new platform rather than snapping.
            currentUp = Vector3.Slerp(currentUp, targetUp, Mathf.Clamp01(upAlignSpeed * dt)).normalized;
            Quaternion tilt = TiltRotation;

            // Traditional 3rd-person platformer orbit: position swings around the target on
            // both yaw and pitch, always framing it, rather than tilting/panning in place.
            // firstPerson (FastPaced scheme's RMB-aim only - see SetFirstPersonMode) collapses
            // this to sit exactly at the focus point instead of orbiting at `distance`, using the
            // raw look rotation directly rather than LookRotation-at-target (which degenerates at
            // zero distance, where focusPoint - transform.position is ~zero and has no reliable
            // direction). Reuses the same SmoothDamp position glide either way, so switching in
            // or out of first person eases smoothly rather than snapping. Everything is composed
            // on top of `tilt` so the whole orbit - height offset included - is expressed in the
            // crashed platform's frame of reference, not the world's (see currentUp above).
            Quaternion rotation = tilt * Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + currentUp * height;
            Vector3 desiredPosition = firstPerson ? focusPoint : focusPoint - rotation * Vector3.forward * distance;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, positionSmoothTime);

            if (firstPerson)
            {
                transform.rotation = rotation;
            }
            else
            {
                // Look directly at the target from wherever the camera ACTUALLY is, rather than
                // reusing the theoretical orbit rotation - position lags behind via SmoothDamp, so
                // during fast stick movement the two used to disagree and the camera briefly didn't
                // point exactly at the player.
                Vector3 lookDir = focusPoint - transform.position;
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir, currentUp);
                }
            }

            // Always called, even in first person - UpdateWallOcclusion's own distance check
            // already no-ops the sphere-cast when the camera sits ~on top of the target (exactly
            // the first-person case), but the loop that RESTORES a renderer hidden just before
            // switching into first person still needs to run every frame, or a wall occluded the
            // instant before RMB was pressed would stay disabled for the entire aim.
            UpdateWallOcclusion(focusPoint);
        }

        // Hides any renderer whose collider sits directly between the camera and the player,
        // restoring it the instant it no longer does - see occlusionMask's own comment.
        void UpdateWallOcclusion(Vector3 focusPoint)
        {
            stillOccludedThisFrame.Clear();

            Vector3 origin = transform.position;
            Vector3 toTarget = focusPoint - origin;
            float distance = toTarget.magnitude;

            if (distance > 0.0001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(origin, occlusionCheckRadius, toTarget / distance, distance, occlusionMask, QueryTriggerInteraction.Ignore);
                foreach (RaycastHit hit in hits)
                {
                    // Only interested in geometry actually BETWEEN the camera and the player -
                    // never hide the player's own collider/visual.
                    if (target != null && (hit.collider.transform == target || hit.collider.transform.IsChildOf(target))) continue;

                    Renderer hitRenderer = hit.collider.GetComponent<Renderer>();
                    if (hitRenderer == null) continue;

                    stillOccludedThisFrame.Add(hitRenderer);
                    if (!occludedRenderers.Contains(hitRenderer))
                    {
                        hitRenderer.enabled = false;
                        occludedRenderers.Add(hitRenderer);
                    }
                }
            }

            for (int i = occludedRenderers.Count - 1; i >= 0; i--)
            {
                if (stillOccludedThisFrame.Contains(occludedRenderers[i])) continue;

                if (occludedRenderers[i] != null) occludedRenderers[i].enabled = true;
                occludedRenderers.RemoveAt(i);
            }
        }
    }
}
