using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{

    [RequireComponent(typeof(KineticCubeController))]
    public class OutOfEnergyRestart : MonoBehaviour
    {
        [Tooltip("Seconds of silence after energy hits 0 before the counter even appears - energy is spent AT launch, so without this the counter would start while the last shot is still flying toward a platform.")]
        public float graceDelay = 3f;
        [Tooltip("Seconds the counter runs before the level reloads.")]
        public float restartDelay = 3f;
        [Tooltip("Energy at or below this counts as 'no energy left'.")]
        public float energyEpsilon = 0.0001f;
        [Tooltip("Speed at or below this counts as 'stopped dead' - drifting to a halt in open space strands you just as surely as running dry.")]
        public float idleSpeedThreshold = 0.5f;
        public Color labelColor = new Color(0.95f, 0.2f, 0.2f);
        public int fontSize = 54;

        KineticCubeController controller;
        Rigidbody rb;
        Text label;
        GameObject labelRoot;
        float remaining;
        float graceRemaining;
        bool counting;

        void Awake()
        {
            controller = GetComponent<KineticCubeController>();
            rb = GetComponent<Rigidbody>();
            remaining = restartDelay;
            graceRemaining = graceDelay;
        }

        void Update()
        {

            if (Time.timeScale <= 0f) return;

            bool unattached = controller != null && !controller.IsStuck && !controller.IsGrounded;
            bool outOfEnergy = controller != null && controller.EnergyFraction <= energyEpsilon;
            bool stoppedDead = rb != null && rb.linearVelocity.sqrMagnitude <= idleSpeedThreshold * idleSpeedThreshold;
            bool stranded = unattached
                && !controller.IsAimingOrCharging
                && (outOfEnergy || stoppedDead);

            if (!stranded)
            {
                if (counting) SetCounting(false);
                remaining = restartDelay;
                graceRemaining = graceDelay;
                return;
            }

            if (graceRemaining > 0f)
            {
                graceRemaining -= Time.unscaledDeltaTime;
                if (counting) SetCounting(false);
                return;
            }

            if (!counting) SetCounting(true);

            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            if (label != null) label.text = "Restarting in " + Mathf.CeilToInt(remaining);
        }

        void SetCounting(bool active)
        {
            counting = active;
            if (active && labelRoot == null) BuildLabel();
            if (labelRoot != null) labelRoot.SetActive(active);
        }

        void BuildLabel()
        {
            GameObject canvasGo = new GameObject("OutOfEnergyCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            labelRoot = new GameObject("RestartCountdown", typeof(RectTransform));
            labelRoot.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = labelRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 220f);
            rt.sizeDelta = new Vector2(900f, 120f);

            label = labelRoot.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = labelColor;
            label.text = "Restarting in " + Mathf.CeilToInt(restartDelay);

            Shadow shadow = labelRoot.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(2f, -2f);

            labelRoot.SetActive(false);
        }
    }
}
