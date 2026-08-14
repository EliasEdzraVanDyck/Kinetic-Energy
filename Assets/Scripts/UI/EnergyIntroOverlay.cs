using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using KineticEnergy.Camera;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{
    // Fullscreen black intro card shown the moment an EnergyRegulation scene loads (direct
    // request): white text explains how that scene's energy-regulation mode works, any button
    // (keyboard, mouse or gamepad) dismisses it, and the dismissing press is deliberately
    // eaten - gameplay (player controllers + pause menu) stays disabled and time frozen until
    // that control is RELEASED again, so the press can't carry over into the game. Builds its
    // own canvas at runtime, same pattern as EnergyCrankUI - no scene UI wiring needed.
    public class EnergyIntroOverlay : MonoBehaviour
    {
        [TextArea(3, 10)]
        public string message = "Charge a midair launch, then regulate how much energy goes into it before firing.";
        public string continueHint = "Press any button to start";
        public int fontSize = 36;
        public int hintFontSize = 24;
        public int sortingOrder = 200; // above the pause canvas (100), above everything else

        GameObject root;
        ButtonControl dismissControl;
        bool dismissed;
        float previousTimeScale = 1f;
        readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();
        IDisposable anyButtonListener;

        void Start()
        {
            BuildOverlay();

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            // Input still fires with timeScale 0, so the gameplay readers themselves have to be
            // off for the overlay's duration - restored control-for-control on dismissal.
            DisableIfPresent<KineticCubeController>();
            DisableIfPresent<KineticCubeControllerFreeMove>();
            DisableIfPresent<PauseController>();
            // The camera too - otherwise mouse/stick movement while the card is up silently
            // orbits the camera, and gameplay starts facing somewhere it never chose.
            DisableIfPresent<ThirdPersonOrbitCamera>();

            anyButtonListener = InputSystem.onAnyButtonPress.CallOnce(control =>
            {
                dismissControl = control as ButtonControl;
                dismissed = true;
                if (root != null) root.SetActive(false);
            });
        }

        void DisableIfPresent<T>() where T : Behaviour
        {
            foreach (T behaviour in FindObjectsOfType<T>())
            {
                if (!behaviour.enabled) continue;
                behaviour.enabled = false;
                disabledBehaviours.Add(behaviour);
            }
        }

        void Update()
        {
            if (!dismissed) return;
            // Wait out the dismissing press before handing control back - re-enabling on the
            // same (or a held) frame would let hold-style inputs (aim triggers) leak straight
            // into gameplay.
            if (dismissControl != null && dismissControl.isPressed) return;

            Time.timeScale = previousTimeScale;
            foreach (Behaviour behaviour in disabledBehaviours)
            {
                if (behaviour != null) behaviour.enabled = true;
            }
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            anyButtonListener?.Dispose();
        }

        void BuildOverlay()
        {
            root = new GameObject("EnergyIntroCanvas");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform));
            backdrop.transform.SetParent(root.transform, false);
            RectTransform backdropRt = backdrop.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            backdrop.AddComponent<Image>().color = Color.black;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject textGo = new GameObject("Message", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchoredPosition = new Vector2(0f, 40f);
            textRt.sizeDelta = new Vector2(1400f, 700f);
            Text text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = message;

            GameObject hintGo = new GameObject("ContinueHint", typeof(RectTransform));
            hintGo.transform.SetParent(root.transform, false);
            RectTransform hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0.5f, 0f);
            hintRt.anchorMax = new Vector2(0.5f, 0f);
            hintRt.anchoredPosition = new Vector2(0f, 90f);
            hintRt.sizeDelta = new Vector2(900f, 60f);
            Text hint = hintGo.AddComponent<Text>();
            hint.font = font;
            hint.fontSize = hintFontSize;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            hint.text = continueHint;
        }
    }
}
