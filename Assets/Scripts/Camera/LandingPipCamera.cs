using UnityEngine;
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
            pip.BuildBorder();
            return pip;
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
            pipCam.rect = preset.pipViewport;
            pipCam.fieldOfView = preset.pipFieldOfView;
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

        void Hide()
        {
            if (pipCam != null && pipCam.enabled) pipCam.enabled = false;
            if (borderRoot != null && borderRoot.activeSelf) borderRoot.SetActive(false);
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
