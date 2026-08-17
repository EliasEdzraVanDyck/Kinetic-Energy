using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Camera
{
    // The landing picture-in-picture (variants C/D): a second camera in a top-left screen
    // window that watches the PREDICTED LANDING POINT from a vantage part-way along the
    // aim arc. It appears ONLY when the landing cursor is NOT visible on the main screen
    // (off-viewport, behind the camera, or occluded by geometry) - if you can already see
    // where you'll land, the window would just be noise. Framed by a grey-white border
    // matching the other UI. Built and driven at runtime by AimCameraVariantController.
    public class LandingPipCamera : MonoBehaviour
    {
        UnityEngine.Camera pipCam;
        KineticCubeController controller;
        AimCameraPreset preset;
        GameObject borderRoot;
        RectTransform borderFrame;

        // The window renders into its OWN render texture rather than straight into a
        // screen viewport rect: that texture is supersampled and MSAA'd, so the small
        // view is sharper than the main screen instead of softer (direct request).
        // Rendering into a viewport rect gave no control over either.
        [Tooltip("Render-texture size multiplier over the window's on-screen pixel size (2 = double resolution, downsampled on display).")]
        public float supersample = 2f;
        [Tooltip("MSAA samples for the window's render texture (1, 2, 4 or 8).")]
        public int msaaSamples = 8;
        RenderTexture pipTexture;
        RawImage pipImage;
        int textureWidth;
        int textureHeight;

        [Tooltip("Fraction of the ACTIVE trail dots - counted from the player's end - left out of the landing window. The near dots only crowd the small view; the ones approaching the landing are what it exists to show.")]
        [Range(0f, 1f)] public float hideNearTrailFraction = 0.7f; // only the last 30% shows
        readonly List<Renderer> hiddenForPip = new List<Renderer>();

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HideNearTrail;
            RenderPipelineManager.endCameraRendering += RestoreNearTrail;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HideNearTrail;
            RenderPipelineManager.endCameraRendering -= RestoreNearTrail;
            RestoreHiddenTrail();
        }

        // Runs for the PIP CAMERA ONLY: the near half of the dots is switched off just
        // for its render pass and switched straight back on afterwards, so the main
        // screen keeps the complete trail.
        void HideNearTrail(ScriptableRenderContext context, UnityEngine.Camera renderingCamera)
        {
            if (renderingCamera != pipCam || controller == null) return;
            LandingPreviewController preview = controller.landingPreview;
            if (preview == null || preview.trailDots == null || hideNearTrailFraction <= 0f) return;

            int activeDots = 0;
            foreach (Transform dot in preview.trailDots)
            {
                if (dot != null && dot.gameObject.activeSelf) activeDots++;
            }
            int hideCount = Mathf.FloorToInt(activeDots * Mathf.Clamp01(hideNearTrailFraction));
            if (hideCount <= 0) return;

            // trailDots run from the PLAYER outward, so the first entries are the near ones.
            int seen = 0;
            foreach (Transform dot in preview.trailDots)
            {
                if (dot == null || !dot.gameObject.activeSelf) continue;
                if (seen >= hideCount) break;
                seen++;
                Renderer dotRenderer = dot.GetComponent<Renderer>();
                if (dotRenderer != null && dotRenderer.enabled)
                {
                    dotRenderer.enabled = false;
                    hiddenForPip.Add(dotRenderer);
                }
            }
        }

        void RestoreNearTrail(ScriptableRenderContext context, UnityEngine.Camera renderingCamera)
        {
            if (renderingCamera != pipCam) return;
            RestoreHiddenTrail();
        }

        void RestoreHiddenTrail()
        {
            foreach (Renderer dotRenderer in hiddenForPip)
            {
                if (dotRenderer != null) dotRenderer.enabled = true;
            }
            hiddenForPip.Clear();
        }

        public static LandingPipCamera Create(KineticCubeController controller)
        {
            GameObject go = new GameObject("LandingPipCamera");
            LandingPipCamera pip = go.AddComponent<LandingPipCamera>();
            pip.controller = controller;
            pip.pipCam = go.AddComponent<UnityEngine.Camera>();
            // Renders after (on top of) the main camera, into its own viewport window.
            UnityEngine.Camera main = UnityEngine.Camera.main;
            pip.pipCam.depth = main != null ? main.depth + 1 : 1;
            pip.pipCam.enabled = false;
            // SMAA on the window's own camera: a render-texture camera skips the main
            // camera's post AA entirely, which is why the window looked pixelated even
            // supersampled - it needs its own pass.
            var extraData = pip.pipCam.GetUniversalAdditionalCameraData();
            if (extraData != null)
            {
                extraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                extraData.antialiasingQuality = AntialiasingQuality.High;
            }
            pip.MatchMainCameraBackground();
            pip.BuildBorder();
            return pip;
        }

        // The window renders into its OWN texture, so it clears with its own background -
        // by default the bare camera default rather than whatever the level uses. Copied
        // from the main camera so the small screen sits on the same solid ground as the
        // big one.
        void MatchMainCameraBackground()
        {
            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main == null || pipCam == null) return;
            pipCam.clearFlags = main.clearFlags;
            pipCam.backgroundColor = main.backgroundColor;
        }

        public void SetPreset(AimCameraPreset activePreset)
        {
            preset = activePreset;
            if (preset == null || !preset.UsesPip) Hide();
        }

        void LateUpdate()
        {
            if (preset == null || !preset.UsesPip || controller == null)
            {
                Hide();
                return;
            }

            if (!controller.TryGetPredictedArcPoint(preset.pipArcFraction, out Vector3 vantage, out Vector3 landing))
            {
                Hide();
                return;
            }

            // The window only earns its screen space while the landing CURSOR can't be
            // seen on the main screen.
            if (LandingVisibleOnMainScreen(landing))
            {
                Hide();
                return;
            }

            // Keep a readable distance: a short arc's mid-point can sit nearly on the
            // landing - push the vantage back along the view direction until it clears.
            Vector3 fromLanding = vantage - landing;
            float distance = fromLanding.magnitude;
            if (distance < preset.pipMinDistance)
            {
                Vector3 direction = distance > 0.01f ? fromLanding / distance : Vector3.up;
                vantage = landing + direction * preset.pipMinDistance;
            }

            transform.position = vantage;
            transform.rotation = Quaternion.LookRotation(landing - vantage, Vector3.up);
            pipCam.fieldOfView = preset.pipFieldOfView;
            EnsureTexture(preset.pipViewport);
            // Re-matched here as well as at creation: Camera.main may not have existed yet
            // when the window was built, and the level can swap its background at runtime.
            MatchMainCameraBackground();
            if (!pipCam.enabled) pipCam.enabled = true;

            // Border frame anchored to the same normalized rect as the viewport.
            if (borderFrame != null)
            {
                borderFrame.anchorMin = new Vector2(preset.pipViewport.xMin, preset.pipViewport.yMin);
                borderFrame.anchorMax = new Vector2(preset.pipViewport.xMax, preset.pipViewport.yMax);
            }
            if (borderRoot != null && !borderRoot.activeSelf) borderRoot.SetActive(true);
        }

        // Visible = inside the main viewport (with a small margin), in front of the
        // camera, and not blocked by geometry. Occluded counts as NOT visible - exactly
        // when the second view helps.
        bool LandingVisibleOnMainScreen(Vector3 landing)
        {
            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main == null) return false;

            Vector3 viewport = main.WorldToViewportPoint(landing);
            if (viewport.z <= 0f
                || viewport.x < 0.02f || viewport.x > 0.98f
                || viewport.y < 0.02f || viewport.y > 0.98f)
            {
                return false;
            }

            Vector3 toLanding = landing - main.transform.position;
            float distance = toLanding.magnitude;
            // Stop the ray short of the landing surface itself so the surface can't count
            // as its own occluder.
            if (distance > 1.5f && Physics.Raycast(main.transform.position, toLanding / distance,
                out RaycastHit hit, distance - 1f, ~0, QueryTriggerInteraction.Ignore))
            {
                bool isPlayer = controller != null
                    && (hit.collider.transform == controller.transform || hit.collider.transform.IsChildOf(controller.transform));
                if (!isPlayer) return false;
            }
            return true;
        }

        // (Re)builds the render texture whenever the window's pixel size changes - screen
        // resize, viewport retune, or the first show.
        void EnsureTexture(Rect viewport)
        {
            int wantWidth = Mathf.Max(Mathf.RoundToInt(Screen.width * viewport.width * Mathf.Max(supersample, 1f)), 64);
            int wantHeight = Mathf.Max(Mathf.RoundToInt(Screen.height * viewport.height * Mathf.Max(supersample, 1f)), 64);
            if (pipTexture != null && wantWidth == textureWidth && wantHeight == textureHeight) return;

            if (pipTexture != null)
            {
                pipCam.targetTexture = null;
                if (pipImage != null) pipImage.texture = null;
                pipTexture.Release();
                Destroy(pipTexture);
            }

            pipTexture = new RenderTexture(wantWidth, wantHeight, 24, RenderTextureFormat.DefaultHDR)
            {
                name = "LandingPipTexture",
                antiAliasing = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(msaaSamples, 1)), 1, 8),
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
            };
            pipTexture.Create();
            textureWidth = wantWidth;
            textureHeight = wantHeight;

            pipCam.targetTexture = pipTexture;
            if (pipImage != null) pipImage.texture = pipTexture;
        }

        void Hide()
        {
            if (pipCam != null && pipCam.enabled) pipCam.enabled = false;
            if (borderRoot != null && borderRoot.activeSelf) borderRoot.SetActive(false);
        }

        void OnDestroy()
        {
            if (pipTexture == null) return;
            if (pipCam != null) pipCam.targetTexture = null;
            pipTexture.Release();
            Destroy(pipTexture);
        }

        // Grey-white outline matching the meters' styling: an anchored frame of four thin
        // strips on an overlay canvas, tracking the PiP viewport rect at any resolution.
        void BuildBorder()
        {
            borderRoot = new GameObject("LandingPipBorder");
            borderRoot.transform.SetParent(transform, false);
            Canvas canvas = borderRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;

            GameObject frameGo = new GameObject("Frame", typeof(RectTransform));
            frameGo.transform.SetParent(borderRoot.transform, false);
            borderFrame = frameGo.GetComponent<RectTransform>();
            borderFrame.offsetMin = Vector2.zero;
            borderFrame.offsetMax = Vector2.zero;

            // The camera's render texture fills the frame; the border strips are added
            // AFTER it, so they draw on top of the view.
            GameObject viewGo = new GameObject("PipView", typeof(RectTransform));
            viewGo.transform.SetParent(borderFrame, false);
            RectTransform viewRect = viewGo.GetComponent<RectTransform>();
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            pipImage = viewGo.AddComponent<RawImage>();
            pipImage.texture = pipTexture;

            Color borderColor = new Color(1f, 1f, 1f, 0.9f); // same as the meter outlines
            const float thickness = 3f;
            CreateStrip("Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero, borderColor);
            CreateStrip("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness), borderColor);
            CreateStrip("Left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f), borderColor);
            CreateStrip("Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), Vector2.zero, borderColor);

            borderRoot.SetActive(false);
        }

        void CreateStrip(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            GameObject strip = new GameObject(name, typeof(RectTransform));
            strip.transform.SetParent(borderFrame, false);
            RectTransform rect = strip.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            strip.AddComponent<Image>().color = color;
        }
    }
}
