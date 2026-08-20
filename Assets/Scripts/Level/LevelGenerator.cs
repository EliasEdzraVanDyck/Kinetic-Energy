using UnityEngine;
using KineticEnergy.Camera;

namespace KineticEnergy.Level
{
    public class LevelGenerator : MonoBehaviour
    {
        [Header("Platforms")]
        public int platformCount = 9;
        public Vector3 platformSize = new Vector3(3f, 0.5f, 3f);
        public float minHorizontalDistance = 7f;
        public float maxHorizontalDistance = 13f;
        public float minHeightDifference = -1.5f;
        public float maxHeightDifference = 2f;

        public Material platformMaterial;
        public Color platformColor = new Color(0.5f, 0.5f, 0.55f);

        [Header("Finish")]
        public Color finishPadColor = new Color(0.2f, 1f, 0.5f, 0.45f);
        public string finishText = "Finish";
        public float finishTextHeight = 2.5f;
        public Color finishTextColor = new Color(0.15f, 0.45f, 1f);
        public int finishFontSize = 48;
        public float finishCharacterSize = 0.2f;

        [Header("Safety Floor")]

        public float safetyFloorMargin = 8f;
        public float safetyFloorSize = 260f;

        [Header("References")]
        public Transform player;
        public Transform cameraTransform;

        void Awake()
        {
            Generate();
        }

        void Generate()
        {
            Vector3 pos = Vector3.zero;
            Vector3 startPos = Vector3.zero;
            float lowestY = 0f;

            for (int i = 0; i < platformCount; i++)
            {
                bool isFinish = i == platformCount - 1;
                BuildPlatform(pos, isFinish, i);
                lowestY = Mathf.Min(lowestY, pos.y);

                if (i == 0)
                {
                    startPos = pos;
                    if (player != null)
                    {
                        player.position = pos + new Vector3(0f, platformSize.y * 0.5f + 0.5f, 0f);
                    }
                }

                if (isFinish)
                {
                    FaceCameraTowardFinish(startPos, pos);
                    break;
                }

                float dx = Random.Range(minHorizontalDistance, maxHorizontalDistance) * (Random.value < 0.5f ? -1f : 1f);
                float dz = Random.Range(minHorizontalDistance, maxHorizontalDistance);
                float dy = Random.Range(minHeightDifference, maxHeightDifference);
                pos += new Vector3(dx, dy, dz);
            }

            BuildSafetyFloor(lowestY - safetyFloorMargin);
        }

        void BuildSafetyFloor(float floorY)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "SafetyFloor";
            floor.transform.SetParent(transform, true);
            floor.transform.position = new Vector3(0f, floorY, 0f);

            floor.transform.localScale = new Vector3(safetyFloorSize / 10f, 1f, safetyFloorSize / 10f);
            Destroy(floor.GetComponent<Renderer>());

            if (player != null)
            {
                BoxCollider playerCollider = player.GetComponent<BoxCollider>();
                Collider floorCollider = floor.GetComponent<Collider>();
                if (playerCollider != null && floorCollider != null)
                {
                    Physics.IgnoreCollision(floorCollider, playerCollider, true);
                }
            }
        }

        void FaceCameraTowardFinish(Vector3 startPos, Vector3 finishPos)
        {
            if (cameraTransform == null) return;
            ThirdPersonOrbitCamera orbitCam = cameraTransform.GetComponent<ThirdPersonOrbitCamera>();
            if (orbitCam == null) return;

            Vector3 direction = finishPos - startPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            orbitCam.SetInitialYaw(yaw);
        }

        void BuildPlatform(Vector3 position, bool isFinish, int index)
        {
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = isFinish ? "FinishPlatform" : $"Platform{index}";
            platform.transform.SetParent(transform, true);
            platform.transform.position = position;
            platform.transform.localScale = platformSize;
            platform.GetComponent<Renderer>().sharedMaterial = platformMaterial != null ? platformMaterial : BuildMaterial(platformColor, false);

            if (isFinish) BuildFinishPad(position);
        }

        void BuildFinishPad(Vector3 platformPosition)
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "FinishPad";
            pad.transform.SetParent(transform, true);
            Destroy(pad.GetComponent<Collider>());

            const float padHeight = 0.05f;
            const float zFightGap = 0.03f;
            pad.transform.position = platformPosition + new Vector3(0f, platformSize.y * 0.5f + zFightGap + padHeight * 0.5f, 0f);
            pad.transform.localScale = new Vector3(platformSize.x, padHeight, platformSize.z);
            pad.GetComponent<Renderer>().sharedMaterial = BuildMaterial(finishPadColor, true);

            GameObject textGo = new GameObject("FinishText");
            textGo.transform.SetParent(transform, true);
            textGo.transform.position = platformPosition + new Vector3(0f, finishTextHeight, 0f);

            TextMesh textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = finishText;
            textMesh.color = finishTextColor;
            textMesh.fontSize = finishFontSize;
            textMesh.characterSize = finishCharacterSize;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;

            Billboard billboard = textGo.AddComponent<Billboard>();
            billboard.target = cameraTransform;

            GameObject trigger = new GameObject("FinishTrigger");
            trigger.transform.SetParent(transform, true);
            trigger.transform.position = platformPosition + new Vector3(0f, platformSize.y * 0.5f + 1f, 0f);
            BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(platformSize.x, 2f, platformSize.z);
            trigger.AddComponent<FinishLine>();
        }

        static Material BuildMaterial(Color color, bool transparent)
        {
            Material mat = new Material(FindShader());
            mat.color = color;
            if (transparent) MakeTransparent(mat, color.a);
            return mat;
        }

        static Shader FindShader()
        {
            string[] candidates =
            {
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Standard",
                "Diffuse"
            };

            foreach (string name in candidates)
            {
                Shader shader = Shader.Find(name);
                if (shader != null) return shader;
            }

            return null;
        }

        static void MakeTransparent(Material mat, float alpha)
        {
            Color c = mat.color;
            c.a = alpha;
            mat.color = c;

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
