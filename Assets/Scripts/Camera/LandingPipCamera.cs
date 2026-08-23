using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Camera
{

    public class LandingPipCamera : MonoBehaviour
    {
        UnityEngine.Camera pipCam;
        KineticCubeController controller;
        AimCameraPreset preset;
        GameObject borderRoot;
        RectTransform borderFrame;

        [Tooltip("Render-texture size multiplier over the window's on-screen pixel size (2 = double resolution, downsampled on display).")]
        public float supersample = 2f;
        [Tooltip("MSAA samples for the window's render texture (1, 2, 4 or 8).")]
        public int msaaSamples = 8;
        RenderTexture pipTexture;
        RawImage pipImage;
        int textureWidth;
        int textureHeight;

        [Tooltip("Fraction of the ACTIVE trail dots - counted from the player's end - left out of the landing window. The near dots only crowd the small view; the ones approaching the landing are what it exists to show.")]
        [Range(0f, 1f)] public float hideNearTrailFraction = 0.7f;
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

            UnityEngine.Camera main = UnityEngine.Camera.main;
            pip.pipCam.depth = main != null ? main.depth + 1 : 1;
            pip.pipCam.enabled = false;

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

            if (LandingVisibleOnMainScreen(landing))
            {
                Hide();
                return;
            }

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

            MatchMainCameraBackground();
            if (!pipCam.enabled) pipCam.enabled = true;

            if (borderFrame != null)
            {
                borderFrame.anchorMin = new Vector2(preset.pipViewport.xMin, preset.pipViewport.yMin);
                borderFrame.anchorMax = new Vector2(preset.pipViewport.xMax, preset.pipViewport.yMax);
            }
            if (borderRoot != null && !borderRoot.activeSelf) borderRoot.SetActive(true);
        }

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

            if (distance > 1.5f && Physics.Raycast(main.transform.position, toLanding / distance,
                out RaycastHit hit, distance - 1f, ~0, QueryTriggerInteraction.Ignore))
            {
                bool isPlayer = controller != null
                    && (hit.collider.transform == controller.transform || hit.collider.transform.IsChildOf(controller.transform));
                if (!isPlayer) return false;
            }
            return true;
        }

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

            GameObject viewGo = new GameObject("PipView", typeof(RectTransform));
            viewGo.transform.SetParent(borderFrame, false);
            RectTransform viewRect = viewGo.GetComponent<RectTransform>();
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            pipImage = viewGo.AddComponent<RawImage>();
            pipImage.texture = pipTexture;

            Color borderColor = new Color(1f, 1f, 1f, 0.9f);
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

