using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A platform that ping-pongs between its placed position and placedPosition + moveOffset.
    // lapSeconds is the time for one FULL lap - there AND back.
    //
    // While the player is in the midair aim, a blue arrow grows out of the platform's
    // centre to the exact spot that centre will occupy when the player's currently
    // previewed shot lands (the lead time updates live with the aim's predicted flight
    // duration) - aim at the arrow's TIP and you and the platform arrive together. The
    // arrow follows the platform's real ping-pong maths, so it stays honest across
    // direction reversals too.
    public class MovingPlatform : MonoBehaviour
    {
        [Tooltip("Where the platform travels to, relative to its placed position.")]
        public Vector3 moveOffset = new Vector3(0f, 0f, 20f);
        [Tooltip("Seconds one FULL lap takes: out to the far point and back again.")]
        public float lapSeconds = 6f;
        [Tooltip("Colour of the midair-aim lead arrow.")]
        public Color arrowColor = new Color(0.2f, 0.5f, 1f);
        [Tooltip("Thickness of the lead arrow's shaft.")]
        public float arrowThickness = 0.35f;
        [Tooltip("Colour of the ghost showing where the platform will be when the shot lands.")]
        public Color ghostColor = new Color(0.35f, 0.65f, 1f, 0.3f);

        Vector3 startPosition;
        KineticCubeController player;
        Rigidbody body;
        // The platform's own clock, advanced by WorldMotionTime - the project-wide rule
        // for every non-player moving object: follows the bullet-time while aiming, does
        // NOT accelerate with a launch's speed-up, and freezes cleanly through pauses.
        float clock;
        Transform arrowRoot;
        Transform shaft;
        Transform head;
        Material arrowMaterial;
        Transform ghost;
        Material ghostMaterial;

        void Start()
        {
            startPosition = transform.position;
            player = FindAnyObjectByType<KineticCubeController>();

            // A KINEMATIC, INTERPOLATED rigidbody drives the motion - moving a plain
            // transform teleports once per physics tick with nothing drawn in between,
            // which made the whole scene appear to vibrate while riding. Added at runtime
            // so existing prefab instances need no editing.
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            BuildArrow();
        }

        // The platform's velocity this physics tick - the player's movement code reads it
        // (via the ground check's hit collider) and adds it to his own velocity, which is
        // how the carry stays perfectly smooth: it rides the rigidbody interpolation
        // instead of teleporting positions.
        public Vector3 CurrentVelocity { get; private set; }

        void FixedUpdate()
        {
            clock += WorldMotionTime.FixedDeltaTime;
            Vector3 target = CentreAt(clock);
            // Divided by the SCALED step so a carried rider covers the same per-tick
            // distance as the platform, whatever the current time scale.
            CurrentVelocity = (target - body.position) / Time.fixedDeltaTime;
            body.MovePosition(target);
        }

        // Deterministic ping-pong: placed position -> +offset -> back, over lapSeconds.
        Vector3 CentreAt(float time)
        {
            if (lapSeconds <= 0.01f) return startPosition;
            float t01 = Mathf.PingPong(time / lapSeconds * 2f, 1f);
            return startPosition + moveOffset * t01;
        }

        // How far this platform will have travelled in `seconds` from now. The landing
        // PREDICTION shifts its stand-in for this platform by exactly this, so the trail and
        // cursor land where the platform will actually be - which is what the ghost draws.
        public Vector3 LeadOffset(float seconds)
        {
            return CentreAt(clock + seconds) - CentreAt(clock);
        }

        void Update()
        {
            if (arrowRoot == null) return;

            bool show = player != null && player.IsAirAiming;
            if (show)
            {
                // Endpoint = this centre's position at the moment the previewed shot lands.
                // The platform runs on REAL time, so the lead uses the flight's estimated
                // real-world duration (the prediction itself is in game-time).
                Vector3 current = transform.position;
                Vector3 future = CentreAt(clock + player.PredictedFlightRealSecondsLive);
                Vector3 delta = future - current;
                float length = delta.magnitude;
                if (length < 0.05f) show = false; // effectively stationary over the lead time
                else
                {
                    // CENTRE to CENTRE: the arrow starts inside the platform and ends inside
                    // the ghost, so it reads as one object pointing at the other.
                    arrowRoot.SetPositionAndRotation(current, Quaternion.LookRotation(delta / length, Vector3.up));
                    shaft.localScale = new Vector3(arrowThickness, arrowThickness, length);
                    shaft.localPosition = new Vector3(0f, 0f, length * 0.5f);
                    head.localPosition = new Vector3(0f, 0f, length);

                    if (ghost != null)
                    {
                        ghost.SetPositionAndRotation(future, transform.rotation);
                        ghost.localScale = transform.lossyScale;
                    }
                }
            }

            if (arrowRoot.gameObject.activeSelf != show) arrowRoot.gameObject.SetActive(show);
            if (ghost != null && ghost.gameObject.activeSelf != show) ghost.gameObject.SetActive(show);
        }

        // Built in code so the prefab stays a single self-contained piece. No colliders on
        // the arrow - it must never block flights or join the landing prediction.
        void BuildArrow()
        {
            arrowRoot = new GameObject("MoveLeadArrow").transform;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");
            arrowMaterial = new Material(shader) { color = arrowColor };

            shaft = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            shaft.name = "Shaft";
            Destroy(shaft.GetComponent<Collider>());
            shaft.SetParent(arrowRoot, false);
            ConfigureArrowPart(shaft);

            head = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            head.name = "Head";
            Destroy(head.GetComponent<Collider>());
            head.SetParent(arrowRoot, false);
            head.localScale = Vector3.one * (arrowThickness * 2.2f);
            ConfigureArrowPart(head);

            arrowRoot.gameObject.SetActive(false);
            BuildGhost();
        }

        // A see-through copy of the platform, parked where it will be when the previewed
        // shot arrives. NO COLLIDER: the flight must pass straight through it, and the
        // landing prediction already accounts for the platform's travel by moving its own
        // stand-in (see MovingPlatform.LeadOffset), so the cursor settles on the ghost
        // without the ghost itself being solid.
        void BuildGhost()
        {
            GameObject ghostGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghostGo.name = "MoveLeadGhost";
            Destroy(ghostGo.GetComponent<Collider>());
            ghost = ghostGo.transform;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            ghostMaterial = new Material(shader);
            ghostMaterial.color = ghostColor;
            if (ghostMaterial.HasProperty("_BaseColor")) ghostMaterial.SetColor("_BaseColor", ghostColor);
            if (ghostMaterial.HasProperty("_Surface")) ghostMaterial.SetFloat("_Surface", 1f);
            if (ghostMaterial.HasProperty("_SrcBlend")) ghostMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (ghostMaterial.HasProperty("_DstBlend")) ghostMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (ghostMaterial.HasProperty("_ZWrite")) ghostMaterial.SetFloat("_ZWrite", 0f);
            ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghostMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            Renderer ghostRenderer = ghostGo.GetComponent<Renderer>();
            ghostRenderer.sharedMaterial = ghostMaterial;
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghostGo.SetActive(false);
        }

        void ConfigureArrowPart(Transform part)
        {
            Renderer partRenderer = part.GetComponent<Renderer>();
            partRenderer.sharedMaterial = arrowMaterial;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnDestroy()
        {
            if (arrowRoot != null) Destroy(arrowRoot.gameObject);
            if (arrowMaterial != null) Destroy(arrowMaterial);
            if (ghost != null) Destroy(ghost.gameObject);
            if (ghostMaterial != null) Destroy(ghostMaterial);
        }
    }
}
