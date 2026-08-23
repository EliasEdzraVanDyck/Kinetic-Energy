using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KineticEnergy.Level
{

    public class TargetSphereCounter : MonoBehaviour
    {
        [Tooltip("Label showing the count - wired by the setup script.")]
        public Text label;
        [Tooltip("Never fewer than this many targets visible at once.")]
        public int minActiveTargets = 5;

        readonly List<TargetSphere> spheres = new List<TargetSphere>();
        int collected;

        void Start()
        {
            Refresh();
        }

        public void Register(TargetSphere sphere)
        {
            if (sphere != null && !spheres.Contains(sphere)) spheres.Add(sphere);
        }

        public void ReportCollected()
        {
            collected++;
            Refresh();
            EnsureMinimumActive();
        }

        void EnsureMinimumActive()
        {
            int active = 0;
            foreach (TargetSphere sphere in spheres)
            {
                if (sphere != null && sphere.IsActive) active++;
            }

            foreach (TargetSphere sphere in spheres)
            {
                if (active >= minActiveTargets) break;
                if (sphere == null || sphere.IsActive) continue;
                sphere.ForceRespawnNow();
                active++;
            }
        }

        void Refresh()
        {
            if (label != null) label.text = "Targets: " + collected;
        }
    }
}

