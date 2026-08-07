using System.Collections.Generic;
using UnityEngine;

namespace KineticEnergy.Level
{
    // The launch-button prefab (Assets/Prefabs/LaunchButton.prefab): a flat socket slab with a
    // smaller, taller cap on top. Launch onto the cap to press it - the cap sinks back into the
    // socket and every assigned target object has its active state FLIPPED (inactive objects
    // turn on, active ones turn off), so one button can reveal some platforms and remove others
    // at once. The cap carries NonStickSurface, so touching it never crash-sticks the player:
    // they just fall away again.
    //
    // Assign targets per placed instance in the scene - the references must live on the scene
    // instance, never inside the prefab asset (a prefab cannot hold a reference to a scene
    // object; it would silently save as null).
    public class LaunchButton : MonoBehaviour
    {
        [Header("Targets")]
        [Tooltip("Scene objects whose active state flips when the button is pressed (on->off, off->on) - as many as you like. Assign these on each placed button instance; the prefab asset itself cannot reference scene objects.")]
        public List<GameObject> targets = new List<GameObject>();

        [Header("Cap (wired by the prefab)")]
        public Transform buttonCap;
        public Renderer capRenderer;
        [Tooltip("Material swapped onto the cap while pressed, as a clear 'done' signal.")]
        public Material pressedMaterial;

        [Header("Behavior")]
        [Tooltip("How far (local units) the cap sinks into the socket when pressed.")]
        public float pressDepth = 0.5f;
        [Tooltip("How fast (units/second) the cap moves between its out and pressed positions.")]
        public float pressSpeed = 4f;
        [Tooltip("On: pressing latches forever. Off: the button pops back out (and flips every target back) after releaseDelay seconds.")]
        public bool stayPressed = true;
        public float releaseDelay = 5f;

        bool pressed;
        float releaseTimer;
        Vector3 capRestLocalPosition;
        Material normalCapMaterial;

        void Awake()
        {
            capRestLocalPosition = buttonCap != null ? buttonCap.localPosition : Vector3.zero;
            normalCapMaterial = capRenderer != null ? capRenderer.sharedMaterial : null;
        }

        // Called by LaunchButtonCap the moment the player touches the cap.
        public void Press()
        {
            if (pressed) return;
            pressed = true;
            releaseTimer = releaseDelay;
            FlipTargets();
            if (capRenderer != null && pressedMaterial != null) capRenderer.sharedMaterial = pressedMaterial;
        }

        void FlipTargets()
        {
            foreach (GameObject target in targets)
            {
                if (target != null) target.SetActive(!target.activeSelf);
            }
        }

        void Update()
        {
            if (buttonCap != null)
            {
                Vector3 target = pressed ? capRestLocalPosition + Vector3.down * pressDepth : capRestLocalPosition;
                buttonCap.localPosition = Vector3.MoveTowards(buttonCap.localPosition, target, pressSpeed * Time.deltaTime);
            }

            if (pressed && !stayPressed)
            {
                releaseTimer -= Time.deltaTime;
                if (releaseTimer <= 0f)
                {
                    pressed = false;
                    FlipTargets(); // flip everything back to its pre-press state
                    if (capRenderer != null && normalCapMaterial != null) capRenderer.sharedMaterial = normalCapMaterial;
                }
            }
        }
    }
}
