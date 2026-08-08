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
        // Shown next to the button only while Always Mouse is active (direct request).
        public GameObject controllerWarning;

        void Start()
        {
            RefreshLabel();
        }

        // Wired to the button's onClick by KineticEnergySetup.
        public void Toggle()
        {
            if (controller == null) return;
            controller.groundedAimWithMouse = !controller.groundedAimWithMouse;
            // Masks/unmasks every gamepad gameplay binding - menus stay controller-usable
            // (their input runs on a separate asset, and pause has a direct Start-button read).
            controller.ApplyGamepadBlock();
            RefreshLabel();
        }

        void RefreshLabel()
        {
            if (controller == null) return;
            if (label != null)
            {
                label.text = controller.groundedAimWithMouse ? "Aim: Always Mouse" : "Aim: WASD";
            }
            if (controllerWarning != null)
            {
                controllerWarning.SetActive(controller.groundedAimWithMouse);
            }
        }
    }
}
