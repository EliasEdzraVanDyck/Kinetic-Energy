using UnityEngine;

namespace KineticEnergy.Level
{
    // A laser GATE: two tall grey columns with red cylinder beams spanning between them,
    // switching on and off on a settable cycle. While ON, touching a beam does exactly
    // what touching a DamageWall does (the beams' root carries a DamageWalls component -
    // respawn + full enemy reset). The columns themselves are ordinary solid geometry.
    //
    // The cycle clock runs on WorldMotionTime: the gate slows with the aim's bullet-time
    // and ignores the launch game-speed-up, like every other non-player actor - so what
    // you see while planning in slow-mo is exactly the rhythm you get.
    public class LaserWall : MonoBehaviour
    {
        [Tooltip("Seconds the beams stay ON per cycle.")]
        public float onSeconds = 1.5f;
        [Tooltip("Seconds the beams stay OFF per cycle.")]
        public float offSeconds = 1.5f;
        [Tooltip("Phase offset in seconds - stagger gates so they don't all blink together.")]
        public float phaseOffset = 0f;
        [Tooltip("The object holding the red beam cylinders (wired by setup) - toggled as one.")]
        public GameObject barsRoot;

        [Header("Beams (built at runtime from these)")]
        [Tooltip("How many beams span the gate, evenly spaced bottom to top.")]
        public int beamCount = 4;
        [Tooltip("Radius of each beam cylinder.")]
        public float beamRadius = 0.15f;
        [Tooltip("Half the beam's length (gate half-width minus column clearance) - wired by setup.")]
        public float beamHalfLength = 11f;
        [Tooltip("Height of the lowest beam above the gate's base.")]
        public float beamBottomHeight = 1.5f;
        [Tooltip("Height of the highest beam.")]
        public float beamTopHeight = 9f;
        [Tooltip("Red beam material - wired by setup.")]
        public Material beamMaterial;

        float clock;

        // Beams are (re)built at RUNTIME from the public fields above, so beam count and
        // radius are tunable in the Inspector without rebuilding the scene. Any baked
        // beams under barsRoot are replaced.
        void Start()
        {
            if (barsRoot == null) return;
            for (int i = barsRoot.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(barsRoot.transform.GetChild(i).gameObject);
            }

            int count = Mathf.Max(beamCount, 1);
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float height = Mathf.Lerp(beamBottomHeight, beamTopHeight, t);

                GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beam.name = "Beam" + i;
                beam.transform.SetParent(barsRoot.transform, false);
                beam.transform.localPosition = new Vector3(0f, height, 0f);
                // Cylinder long axis is Y - rotate it to span the gate along Z.
                beam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                beam.transform.localScale = new Vector3(beamRadius * 2f, beamHalfLength, beamRadius * 2f);
                if (beamMaterial != null) beam.GetComponent<Renderer>().sharedMaterial = beamMaterial;
                beam.GetComponent<Collider>().isTrigger = true;
            }
        }

        void Update()
        {
            if (barsRoot == null) return;
            clock += WorldMotionTime.DeltaTime;

            float cycle = Mathf.Max(onSeconds + offSeconds, 0.05f);
            bool lasersOn = Mathf.Repeat(clock + phaseOffset, cycle) < onSeconds;
            if (barsRoot.activeSelf != lasersOn) barsRoot.SetActive(lasersOn);
        }
    }
}
