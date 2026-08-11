using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Level
{
    // The Quarry's small session counter: how many target spheres have been collected since
    // the scene loaded. Deliberately unceremonious - no score screen, no goal text - so
    // testers who want a goal can invent one and the rest can ignore it.
    public class TargetSphereCounter : MonoBehaviour
    {
        [Tooltip("Label showing the count - wired by the setup script.")]
        public Text label;

        int collected;

        void Start()
        {
            Refresh();
        }

        public void ReportCollected()
        {
            collected++;
            Refresh();
        }

        void Refresh()
        {
            if (label != null) label.text = "Targets: " + collected;
        }
    }
}
