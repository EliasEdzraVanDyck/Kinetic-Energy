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
        // Wired by KineticEnergySetup to Assets/CheckeredFloor.mat - the same material asset
        // Sandbox Scene's own floor uses, shared (not cloned) so both surfaces genuinely match.
        // platformColor is a fallback only used if this is ever left unassigned.
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
        // A real, solid, invisible catch-floor well below the lowest platform - a miss currently
        // falls all the way to fallResetY and triggers a full level reload; this gives a miss a
        // forgiving landing spot instead. Being a plain static Collider with no Rigidbody and no
        // isTrigger, it needs no special-casing to be included in landing prediction:
        // KineticCubeController.BuildPredictionGeometryProxies already scans for exactly this
        // kind of object and clones it into the prediction's own PhysicsScene automatically -
        // the same way Sandbox Scene's own floor already does.
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

                // Z always advances forward by the random horizontal-distance range; X reuses
                // the same range but can go either way, so the path drifts side to side instead
                // of running in a dead-straight line.
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
            // Unity's default Plane is a 10x10 unit quad at scale 1 - dividing by that gives a
            // scale that makes the finished floor safetyFloorSize units wide/deep regardless of
            // how far the randomized platform path happens to drift.
            floor.transform.localScale = new Vector3(safetyFloorSize / 10f, 1f, safetyFloorSize / 10f);
            Destroy(floor.GetComponent<Renderer>());

            // Solid for PREDICTION only, not for the real player - the player should still fall
            // straight through it (a miss stays a real miss, all the way to fallResetY). This
            // Collider still needs to physically exist and stay non-trigger, non-Rigidbody so
            // KineticCubeController.BuildPredictionGeometryProxies keeps including it when it
            // clones static geometry into the prediction's own isolated PhysicsScene - only the
            // ONE specific pairing below (this floor's collider vs the real player's own
            // collider) gets ignored, which has no effect on that separate clone/proxy pair the
            // prediction system uses, since those are entirely different Collider instances.
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

        // "The camera should already face in the direction of the finish on bootup" - points the
        // camera's initial orbit yaw along the straight-line direction from the start platform to
        // the finish platform, so the player can see (roughly) where they're headed from frame
        // one, rather than whatever direction the authored camera rig happened to start facing.
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

            // zFightGap: without this, the pad's bottom face sits at EXACTLY the same Y as the
            // platform's top surface (platformSize.y*0.5 + padHeight*0.5 puts the pad's own
            // bottom face precisely on that plane) - two coincident faces is the textbook
            // Z-fighting setup, and since it's baked into every Level1 run, it read as the
            // level's visuals "flickering constantly" even though it had nothing to do with the
            // landing-preview system three earlier rounds of fixes targeted. A small vertical
            // gap lifts the pad clear of the platform surface so the faces never overlap.
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
            textMesh.color = finishTextColor; // vivid blue by default - contrast via color, not a backing plate
            textMesh.fontSize = finishFontSize;
            textMesh.characterSize = finishCharacterSize;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;

            Billboard billboard = textGo.AddComponent<Billboard>();
            billboard.target = cameraTransform;

            // Separate invisible trigger volume, not the pad's own collider (which was removed
            // above) - covers the platform footprint and reaches generously above the surface so
            // it reliably catches the player even mid-bounce, without needing the visual pad
            // itself to carry physics behavior.
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

        // See KineticEnergySetup.MakeTransparent (Editor-only, can't be shared from here) -
        // URP Lit/Unlit default to Opaque, alpha is ignored without this explicit switch.
        // _ALPHABLEND_ON is the BUILT-IN RENDER PIPELINE's Standard-shader keyword, not URP's -
        // requesting it on a URP shader asks for a keyword/property combination with no matching
        // compiled variant, which is exactly what renders as Unity's pink/magenta error material.
        // URP's actual surface-type keyword is _SURFACE_TYPE_TRANSPARENT.
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
