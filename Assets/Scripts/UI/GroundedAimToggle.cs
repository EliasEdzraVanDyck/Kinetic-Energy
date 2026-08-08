using UnityEngine;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{
    // The fast-paced scenes' pause-menu option (direct request): switches the grounded launch
    // aim between WASD/stick (the default) and raw mouse delta, for playtesting the two input
    // styles against each other. Lives on the button itself, added per-scene as an instance
    // override on the PauseSystem - the shared prefab is untouched. The choice lives on that
    // scene's Player instance (KineticCubeController.groundedAimWithMouse), so it resets to
    // the Inspector value on scene load.
    public class GroundedAimToggle : MonoBehaviour
    {
        public KineticCubeController controller;
        public Text label;

        void Start()
        {
            RefreshLabel();
        }

        // Wired to the button's onClick by KineticEnergySetup.
        public void Toggle()
        {
            if (controller == null) return;
            controller.groundedAimWithMouse = !controller.groundedAimWithMouse;
            RefreshLabel();
        }

        void RefreshLabel()
        {
            if (label == null || controller == null) return;
            // "Always Mouse" (direct rename) - on controller the joystick still aims in this
            // mode; the option governs the mouse/WASD split, not gamepads.
            label.text = controller.groundedAimWithMouse ? "Aim: Always Mouse" : "Aim: WASD";
        }
    }
}
