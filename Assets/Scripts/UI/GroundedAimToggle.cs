using UnityEngine;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.UI
{

    public class GroundedAimToggle : MonoBehaviour
    {
        public KineticCubeController controller;
        public Text label;

        public GameObject controllerWarning;

        void Start()
        {
            RefreshLabel();
        }

        public void Toggle()
        {
            if (controller == null) return;
            controller.groundedAimWithMouse = !controller.groundedAimWithMouse;

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
