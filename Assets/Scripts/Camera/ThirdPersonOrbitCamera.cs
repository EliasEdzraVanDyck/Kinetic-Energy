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

        public float minPitch = -75f;

        public float maxPitch = 75f;
        public bool invertY = false;

        [Header("Smoothing")]
        public float positionSmoothTime = 0.08f;
        public float maxDeltaTime = 0.05f;

        [Header("Auto Recenter")]

        public float recenterSpeed = 240f;

        [Header("Input")]
        public InputActionReference lookAction;

        bool aimStickOverrideActive;
        Vector2 aimStickOverrideValue;

        bool mouseLookSuppressed;

        public void SetAimStickOverride(bool active, Vector2 stick)
        {
            aimStickOverrideActive = active;
            aimStickOverrideValue = active ? stick : Vector2.zero;
        }

        public void SetMouseLookSuppressed(bool suppressed)
        {
            mouseLookSuppressed = suppressed;
        }

        bool ignoreSlowMo;

        public void SetIgnoreSlowMo(bool ignore)
        {
            ignoreSlowMo = ignore;
        }

        public float firstPersonForwardOffset = 0.75f;

        public float modeSwitchSmoothTime = 0.02f;

        bool modeSwitching;

        public float framingTurnSpeed = 300f;

        public float framingMaxDeviation = 45f;

        bool framingActive;
        Vector3 framingPoint;
        bool framingJustStarted;

        public void SetTrajectoryFraming(bool active, Vector3 worldPoint)
        {
            if (active && !framingActive) framingJustStarted = true;
            framingActive = active;
            framingPoint = worldPoint;
        }

        [Header("Fine Aim")]

        [Range(0f, 1f)] public float fineAimMinFactor = 0.3f;
        public float fineAimStickReference = 0.9f;
        public float fineAimMouseReference = 8f;

        [Header("First Person Aim")]

        public float normalFov = 60f;
        public float maxZoomFov = 20f;

        public float firstPersonMinPitch = -75f;
        public float firstPersonMaxPitch = 75f;

        UnityEngine.Camera cam;
        bool firstPerson;

        Vector3 targetUp = Vector3.up;
        Vector3 currentUp = Vector3.up;
        public float upAlignSpeed = 3f;

        Quaternion TiltRotation => Quaternion.FromToRotation(Vector3.up, currentUp);

        public Vector3 AimForward => TiltRotation * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        public void SetUpVector(Vector3 up)
        {
            targetUp = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        }

        public void SetFirstPersonMode(bool enabled)
        {
            if (firstPerson != enabled) modeSwitching = true;
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
            if (yawInitialized) return;

            Vector3 offset = transform.position - (target.position + Vector3.up * height);
            if (offset.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }
        }

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

            bool lookIsMouseDriven = lookAction != null && lookAction.action != null
                && lookAction.action.activeControl != null
                && lookAction.action.activeControl.device is Mouse;

            if (aimStickOverrideActive && !lookIsMouseDriven) look = aimStickOverrideValue;

            if (mouseLookSuppressed && lookIsMouseDriven) look = Vector2.zero;

            bool gameRunningSlow = Time.timeScale < 1f && !ignoreSlowMo;
            float dt = Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime) * (gameRunningSlow ? 0.5f : 1f);

            float fineAimScale = 1f;
            if (look.sqrMagnitude > 0.0001f)
            {
                bool mouseDriven = lookAction != null && lookAction.action != null
                    && lookAction.action.activeControl != null
                    && lookAction.action.activeControl.device is Mouse;
                float reference = mouseDriven ? fineAimMouseReference : fineAimStickReference;
                float t = reference > 0.0001f ? Mathf.Clamp01(look.magnitude / reference) : 1f;
                fineAimScale = Mathf.Lerp(fineAimMinFactor, 1f, t);
            }

            if (look.sqrMagnitude > 0.0001f) recentering = false;

            if (recentering)
            {
                yaw = Mathf.MoveTowardsAngle(yaw, recenterTargetYaw, recenterSpeed * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, recenterTargetYaw)) < 0.5f) recentering = false;
            }
            else
            {
                yaw += look.x * rotationSpeed * fineAimScale * dt;
            }
            float pitchDelta = (invertY ? look.y : -look.y) * rotationSpeed * fineAimScale * dt;
            pitch = Mathf.Clamp(pitch + pitchDelta,
                firstPerson ? firstPersonMinPitch : minPitch,
                firstPerson ? firstPersonMaxPitch : maxPitch);

            currentUp = Vector3.Slerp(currentUp, targetUp, Mathf.Clamp01(upAlignSpeed * dt)).normalized;
            Quaternion tilt = TiltRotation;

            Quaternion rotation = tilt * Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + currentUp * height;

            Vector3 desiredPosition = firstPerson
                ? target.position + rotation * Vector3.forward * firstPersonForwardOffset
                : focusPoint - rotation * Vector3.forward * distance;

            float smoothTime = modeSwitching ? modeSwitchSmoothTime : positionSmoothTime;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);
            if (modeSwitching && (transform.position - desiredPosition).sqrMagnitude < 0.0025f) modeSwitching = false;

            if (firstPerson)
            {
                Vector3 framingDir = framingPoint - transform.position;
                Quaternion targetRotation = rotation;
                if (framingActive && framingDir.sqrMagnitude > 0.0001f)
                {

                    targetRotation = Quaternion.RotateTowards(rotation,
                        Quaternion.LookRotation(framingDir, currentUp), framingMaxDeviation);
                }

                if (framingJustStarted)
                {

                    transform.rotation = targetRotation;
                    framingJustStarted = false;
                }
                else
                {

                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation,
                        framingTurnSpeed * Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));
                }
            }
            else
            {

                Vector3 lookDir = focusPoint - transform.position;
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir, currentUp);
                }
            }

            UpdateWallOcclusion(focusPoint);
        }

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
