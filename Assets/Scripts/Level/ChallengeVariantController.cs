using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum ChallengeVariant
    {
        OverchargeScatter, // A - big launches scatter: charge buys distance but costs precision
    }

    // The challenge playtest harness (QuarryChallenge scene only - a scene object, never a
    // prefab). Home of the challenge-style variations, starting with the overcharge
    // scatter moved out of the economy scene; future challenge variants slot into the
    // enum and cycle with V / D-pad Right and C / D-pad Left like every other harness.
    public class ChallengeVariantController : MonoBehaviour
    {
        [Tooltip("The challenge active at scene start.")]
        public ChallengeVariant currentVariant = ChallengeVariant.OverchargeScatter;

        [Header("A - Overcharge scatter")]
        [Tooltip("Scatter cone radius (degrees) at full charge.")]
        public float scatterMaxAngle = 14f;
        [Tooltip("Charge fraction where the cone starts opening.")]
        [Range(0f, 1f)] public float scatterStartFraction = 0.25f;
        [Tooltip("Dots drawn around the predicted landing to visualise the scatter radius.")]
        public int scatterRingDots = 24;
        public Color scatterRingColor = new Color(1f, 0.45f, 0.15f, 0.9f);

        KineticCubeController controller;
        Text hudLabel;
        Transform scatterRingRoot;
        Transform[] scatterDots;

        void Start()
        {
            controller = FindAnyObjectByType<KineticCubeController>();
            if (controller == null)
            {
                Debug.LogError("ChallengeVariantController: no KineticCubeController in the scene.");
                enabled = false;
                return;
            }
            BuildHudTag();
            ApplyVariant();
        }

        void Update()
        {
            if (Time.timeScale <= 0f || controller == null) return;

            int count = System.Enum.GetValues(typeof(ChallengeVariant)).Length;
            if (count > 1 && !controller.IsAimingOrCharging)
            {
                bool forward = (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame);
                bool back = (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);
                if (forward || back)
                {
                    currentVariant = (ChallengeVariant)(((int)currentVariant + (forward ? 1 : count - 1)) % count);
                    ApplyVariant();
                }
            }

            UpdateScatterRing();
        }

        void ApplyVariant()
        {
            controller.launchScatterMaxAngle = 0f;

            switch (currentVariant)
            {
                case ChallengeVariant.OverchargeScatter:
                    controller.launchScatterMaxAngle = scatterMaxAngle;
                    controller.launchScatterStartFraction = scatterStartFraction;
                    break;
            }

            if (hudLabel != null) hudLabel.text = CurrentLabel;
        }

        string CurrentLabel => currentVariant switch
        {
            ChallengeVariant.OverchargeScatter => "Variant A - Overcharge scatter",
            _ => "Variant ?",
        };

        void BuildHudTag()
        {
            GameObject root = new GameObject("ChallengeVariantTag");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-24f, 16f);
            rt.sizeDelta = new Vector2(560f, 34f);

            hudLabel = textGo.AddComponent<Text>();
            hudLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudLabel.fontSize = 22;
            hudLabel.alignment = TextAnchor.LowerRight;
            hudLabel.color = new Color(1f, 1f, 1f, 0.55f);
        }

        // The orange dot-ring around the predicted landing, showing the live scatter radius.
        void UpdateScatterRing()
        {
            bool show = currentVariant == ChallengeVariant.OverchargeScatter
                && controller.IsAimingOrCharging
                && controller.HasValidPredictedLanding;

            float cone = show ? controller.ScatterConeAngleFor(controller.CurrentChargeFraction) : 0f;
            show = show && cone > 0.05f;

            if (!show)
            {
                if (scatterRingRoot != null) scatterRingRoot.gameObject.SetActive(false);
                return;
            }

            if (scatterRingRoot == null) BuildScatterRing();
            scatterRingRoot.gameObject.SetActive(true);

            Vector3 landing = controller.LastPredictedLanding;
            float distance = Vector3.Distance(controller.transform.position, landing);
            float radius = Mathf.Tan(cone * Mathf.Deg2Rad) * distance;

            for (int i = 0; i < scatterDots.Length; i++)
            {
                float angle = i / (float)scatterDots.Length * Mathf.PI * 2f;
                scatterDots[i].position = landing + new Vector3(Mathf.Cos(angle) * radius, 0.08f, Mathf.Sin(angle) * radius);
                scatterDots[i].localScale = Vector3.one * Mathf.Clamp(radius * 0.06f, 0.12f, 0.5f);
            }
        }

        void BuildScatterRing()
        {
            scatterRingRoot = new GameObject("ScatterRing").transform;
            scatterDots = new Transform[Mathf.Max(scatterRingDots, 8)];

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material dotMaterial = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            dotMaterial.color = scatterRingColor;

            for (int i = 0; i < scatterDots.Length; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "ScatterDot" + i;
                Destroy(dot.GetComponent<Collider>());
                dot.GetComponent<Renderer>().sharedMaterial = dotMaterial;
                dot.transform.SetParent(scatterRingRoot, false);
                scatterDots[i] = dot.transform;
            }
        }
    }
}
