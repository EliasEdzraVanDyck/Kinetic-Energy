using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            BuildArrow();
        }

        public Vector3 CurrentVelocity { get; private set; }

        void FixedUpdate()
        {
            clock += WorldMotionTime.FixedDeltaTime;
            Vector3 target = CentreAt(clock);

            CurrentVelocity = (target - body.position) / Time.fixedDeltaTime;
            body.MovePosition(target);
        }

        Vector3 CentreAt(float time)
        {
            if (lapSeconds <= 0.01f) return startPosition;
            float t01 = Mathf.PingPong(time / lapSeconds * 2f, 1f);
            return startPosition + moveOffset * t01;
        }

        void ShapeGhost(Vector3 direction, float length)
        {

            Vector3 localDirection = transform.InverseTransformDirection(direction);
            int axis = 0;
            if (Mathf.Abs(localDirection.y) > Mathf.Abs(localDirection[axis])) axis = 1;
            if (Mathf.Abs(localDirection.z) > Mathf.Abs(localDirection[axis])) axis = 2;

            Vector3 fullScale = transform.lossyScale;
            float fullThickness = Mathf.Abs(fullScale[axis]);
            float thickness = Mathf.Min(length, fullThickness);

            Vector3 scale = fullScale;
            scale[axis] = thickness;

            const float seamNudge = 0.02f;
            Vector3 centre = transform.position
                + direction * (length + fullThickness * 0.5f - thickness * 0.5f + seamNudge);

            ghost.SetPositionAndRotation(centre, transform.rotation);
            ghost.localScale = scale;
        }

        public Vector3 LeadOffset(float seconds)
        {
            return CentreAt(clock + seconds) - CentreAt(clock);
        }

        void Update()
        {
            if (arrowRoot == null) return;

            bool show = player != null && player.IsAirAiming && player.GroundPlatform != this;
            if (show)
            {

                Vector3 current = transform.position;
                Vector3 future = current + LeadOffset(player.PredictedFlightRealSecondsLive);
                Vector3 delta = future - current;
                float length = delta.magnitude;
                if (length < 0.05f) show = false;
                else
                {

                    arrowRoot.SetPositionAndRotation(current, Quaternion.LookRotation(delta / length, Vector3.up));
                    shaft.localScale = new Vector3(arrowThickness, arrowThickness, length);
                    shaft.localPosition = new Vector3(0f, 0f, length * 0.5f);
                    head.localPosition = new Vector3(0f, 0f, length);

                    if (ghost != null) ShapeGhost(delta / length, length);
                }
            }

            if (arrowRoot.gameObject.activeSelf != show) arrowRoot.gameObject.SetActive(show);
            if (ghost != null && ghost.gameObject.activeSelf != show) ghost.gameObject.SetActive(show);
        }

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

