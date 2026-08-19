using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KineticEnergy.UI
{
    // The one-time explainer (QuarryNew): a black full-screen overlay describing the aim
    // camera variants, shown ONLY on the first boot of the game process - a scene restart
    // does not bring it back (the session flag survives scene loads, not app quits).
    // Any key/button/click dismisses it; the game is frozen (timeScale 0) while it shows,
    // and the dismissing press cannot leak into gameplay - the player controller already
    // swallows the pause frame AND the first unpaused frame.
    public class AimIntroScreen : MonoBehaviour
    {
        // One first-boot showing per KEY per game process - the aim explainer and the
        // economy explainer track separately.
        static readonly System.Collections.Generic.HashSet<string> shownKeys = new System.Collections.Generic.HashSet<string>();

        [Tooltip("Session key - each distinct key shows its first-boot overlay once per game process.")]
        public string introKey = "aim";

        // PauseController checks this so Esc/Start can't toggle the pause menu underneath.
        public static bool InputBlocked { get; private set; }

        [TextArea(10, 30)]
        public string bodyText =
            "AIM CAMERA VARIANTS\n\n" +
            "This build contains six aiming cameras. Switch with V / D-pad Right (back: C / D-pad Left).\n\n" +
            "A - First person: the aim view you know - you see through the ball.\n" +
            "B - Behind the player: over-the-shoulder, the ball stays visible in the corner\n" +
            "     as a size reference. Q / Right Stick Click swaps the shoulder.\n" +
            "C - First person + landing window: A, plus a corner window showing your\n" +
            "     landing spot whenever the cursor itself is off-screen or hidden.\n" +
            "D - Behind player + landing window: B plus that same landing window.\n" +
            "E - First person + look around: WASD / Right Stick rotates your VIEW without\n" +
            "     moving the aim. Controller energy moves to RB (add) / LB (remove).\n" +
            "F - Behind player + look around: the same, over the shoulder.\n\n" +
            "The feedback form in the pause menu asks which variant you preferred.\n\n" +
            "Press any button to start.";

        GameObject overlayRoot;
        bool wasPausedWhenOpened;
        float dismissArmDelay; // ignore inputs for a beat so the opening click can't instantly close it

        [Tooltip("Open the overlay automatically on first boot. OFF leaves it reachable only through the pause menu's Info button.")]
        public bool showOnBoot = true;

        void Start()
        {
            if (!showOnBoot) return; // stays alive - the pause menu's Info button opens it on demand
            if (shownKeys.Contains(introKey)) return;
            shownKeys.Add(introKey);
            Open();
        }

        // Shows the overlay (again) - first boot does this automatically; the pause menu's
        // Info button calls it on demand. Opening from the pause menu returns TO the pause
        // menu on dismiss instead of unpausing into gameplay.
        public void Open()
        {
            if (overlayRoot != null) return;
            wasPausedWhenOpened = Time.timeScale <= 0f;
            InputBlocked = true;
            dismissArmDelay = 0.15f;
            Time.timeScale = 0f; // the player controller treats this as paused - input swallowed
            BuildOverlay();
        }

        void Update()
        {
            if (overlayRoot == null) return;
            if (dismissArmDelay > 0f)
            {
                dismissArmDelay -= Time.unscaledDeltaTime;
                return;
            }
            if (!AnyInputPressed()) return;

            InputBlocked = false;
            // Opened from the pause menu: stay paused underneath. First-boot: resume play
            // (the controller swallows this first unpaused frame too).
            Time.timeScale = wasPausedWhenOpened ? 0f : 1f;
            Destroy(overlayRoot);
            overlayRoot = null;
        }

        static bool AnyInputPressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame
                || Mouse.current.rightButton.wasPressedThisFrame
                || Mouse.current.middleButton.wasPressedThisFrame)) return true;
            if (Gamepad.current != null)
            {
                foreach (UnityEngine.InputSystem.Controls.ButtonControl button in new[]
                {
                    Gamepad.current.buttonSouth, Gamepad.current.buttonNorth,
                    Gamepad.current.buttonEast, Gamepad.current.buttonWest,
                    Gamepad.current.leftShoulder, Gamepad.current.rightShoulder,
                    Gamepad.current.startButton, Gamepad.current.selectButton,
                    Gamepad.current.leftTrigger, Gamepad.current.rightTrigger,
                })
                {
                    if (button.wasPressedThisFrame) return true;
                }
            }
            return false;
        }

        void BuildOverlay()
        {
            overlayRoot = new GameObject("AimIntroOverlay");
            Canvas canvas = overlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // above every other UI, pause menu included
            CanvasScaler scaler = overlayRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform));
            backdrop.transform.SetParent(overlayRoot.transform, false);
            RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            backdrop.AddComponent<Image>().color = Color.black;

            GameObject textGo = new GameObject("Body", typeof(RectTransform));
            textGo.transform.SetParent(overlayRoot.transform, false);
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(1250f, 900f);

            Text body = textGo.AddComponent<Text>();
            body.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            body.fontSize = 30;
            body.alignment = TextAnchor.MiddleLeft;
            body.color = Color.white;
            body.text = bodyText;
        }
    }
}
