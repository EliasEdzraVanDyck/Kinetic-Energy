using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum ChallengeStage
    {
        LimitedSlowdown,
        OverchargeScatter,
        ChasingWall,
        SealingWalls,
        ShrinkingPlatforms,
    }

    public static class ChallengeStageSelection
    {
        public static ChallengeStage? PendingStage;
    }

    public class ChallengeStageController : MonoBehaviour
    {
        [Tooltip("Stage played when no pause-menu selection is pending (a fresh boot).")]
        public ChallengeStage startingStage = ChallengeStage.LimitedSlowdown;

        [Tooltip("The stages this scene cycles through, in play order - each finish reloads onto the next; the last one wins. The default keeps Level 8 on its original four.")]
        public ChallengeStage[] stageSequence =
        {
            ChallengeStage.LimitedSlowdown,
            ChallengeStage.OverchargeScatter,
            ChallengeStage.ChasingWall,
            ChallengeStage.SealingWalls,
        };

        [Tooltip("Win with the locked pause screen ('You win!', no resume) instead of the ordinary one - for the self-contained test scenes.")]
        public bool lockedWinScreen = false;

        [Header("1 - Limited slowdown")]
        [Tooltip("Seconds of midair slow-down in the budget (the meter refills on every crash).")]
        public float slowdownBudgetSeconds = 2f;

        [Header("2 - Overcharge scatter")]

        [Tooltip("Scatter cone radius (degrees) at full charge.")]
        public float scatterMaxAngle = 14f;
        [Tooltip("Charge fraction where the cone leaves zero. 0 keeps the pure square-root curve (scatter = sqrt(energy%) x maxAngle/10), which is what this scene plays.")]
        [Range(0f, 1f)] public float scatterStartFraction = 0f;
        [Tooltip("Dots drawn around the predicted landing to visualise how far the shot could drift.")]
        public int scatterRingDots = 24;
        public Color scatterRingColor = new Color(1f, 0.45f, 0.15f, 0.9f);

        [Header("3 - Chasing wall (wired by setup)")]
        [Tooltip("The moving wall instance - its start position is edited on it directly; the speeds below drive it.")]
        public DeathWall chaseWall;
        [Tooltip("Speed the wall STARTS the stage at, in metres per second.")]
        public float chaseWallSpeed = 4f;
        [Tooltip("The BUILDUP: metres per second gained per second while the wall runs. 0 = a constant pace.")]
        public float chaseWallAcceleration = 0.25f;
        [Tooltip("Speed ceiling in metres per second - the wall stops gaining here. 0 = no cap.")]
        public float chaseWallMaxSpeed = 0f;

        [Header("4 - Sealing walls (wired by setup)")]
        [Tooltip("The DeathWall prefab cloned as a static seal in each jumped gap.")]
        public GameObject sealWallPrefab;
        [Tooltip("The course platforms in run order - landings are matched against these.")]
        public Transform[] coursePlatforms;
        [Tooltip("World size of a spawned seal wall (x thickness, y height, z width).")]
        public Vector3 sealWallSize = new Vector3(1.5f, 40f, 40f);

        [Header("5 - Shrinking platforms")]
        [Tooltip("The LAST course platform's size as a percentage of the first (50 = half). The first keeps 100%, every platform between interpolates in equal steps. Only y and z scale - x is untouched, so course gaps never change.")]
        [Range(1f, 100f)] public float shrinkFinalScalePercent = 50f;

        [Header("Respawn")]
        [Tooltip("Where a death-wall touch puts the player - the level's ordinary respawn point.")]
        public Transform respawnPoint;

        [Tooltip("Show the bottom-right challenge tag.")]
        public bool showHudTag = true;

        KineticCubeController controller;
        ChallengeStage stage;
        Text hudLabel;
        Transform lastPlatform;
        Vector3[] courseOriginalScales;
        Transform scatterRingRoot;
        Transform[] scatterDots;
        readonly List<GameObject> sealWalls = new List<GameObject>();
        readonly HashSet<int> sealedGaps = new HashSet<int>();

        void Start()
        {
            controller = FindAnyObjectByType<KineticCubeController>();
            if (controller == null)
            {
                Debug.LogError("ChallengeStageController: no KineticCubeController in the scene.");
                enabled = false;
                return;
            }

            stage = ChallengeStageSelection.PendingStage ?? startingStage;
            ChallengeStageSelection.PendingStage = null;

            if (stageSequence == null || stageSequence.Length == 0)
            {
                stageSequence = new[] { startingStage };
            }
            if (SequenceIndex(stage) < 0) stage = stageSequence[0];

            CaptureCourseScales();
            BuildHudTag();
            ApplyStage();
        }

        void OnEnable()
        {
            DeathWall.PlayerTouched += OnWallTouched;
            DamageWalls.PlayerRespawned += OnPlayerRespawned;
        }

        void OnDisable()
        {
            DeathWall.PlayerTouched -= OnWallTouched;
            DamageWalls.PlayerRespawned -= OnPlayerRespawned;
        }

        void ApplyStage()
        {

            controller.slowdownMode = SlowdownMode.Unlimited;
            controller.launchScatterMaxAngle = 0f;

            switch (stage)
            {
                case ChallengeStage.LimitedSlowdown:
                    controller.slowdownMode = SlowdownMode.AimBudget;
                    controller.aimBudgetSeconds = slowdownBudgetSeconds;
                    break;
                case ChallengeStage.OverchargeScatter:
                    controller.launchScatterMaxAngle = scatterMaxAngle;
                    controller.launchScatterStartFraction = scatterStartFraction;
                    break;
            }

            if (chaseWall != null)
            {

                chaseWall.moveSpeed = chaseWallSpeed;
                chaseWall.moveAcceleration = chaseWallAcceleration;
                chaseWall.maxMoveSpeed = chaseWallMaxSpeed;
                chaseWall.gameObject.SetActive(stage == ChallengeStage.ChasingWall);
                chaseWall.ResetToStart();
            }
            RestoreCourseScales();
            if (stage == ChallengeStage.ShrinkingPlatforms) ApplyShrinkScales();
            if (scatterRingRoot != null) scatterRingRoot.gameObject.SetActive(false);
            ClearSealWalls();
            lastPlatform = null;
            if (hudLabel != null) hudLabel.text = StageLabel;
        }

        int SequenceIndex(ChallengeStage lookFor)
        {
            return stageSequence == null ? -1 : System.Array.IndexOf(stageSequence, lookFor);
        }

        void CaptureCourseScales()
        {
            if (coursePlatforms == null) return;
            courseOriginalScales = new Vector3[coursePlatforms.Length];
            for (int i = 0; i < coursePlatforms.Length; i++)
            {
                if (coursePlatforms[i] != null) courseOriginalScales[i] = coursePlatforms[i].localScale;
            }
        }

        void RestoreCourseScales()
        {
            if (coursePlatforms == null || courseOriginalScales == null) return;
            for (int i = 0; i < coursePlatforms.Length && i < courseOriginalScales.Length; i++)
            {
                if (coursePlatforms[i] != null) coursePlatforms[i].localScale = courseOriginalScales[i];
            }
        }

        void ApplyShrinkScales()
        {
            if (coursePlatforms == null || coursePlatforms.Length < 2 || courseOriginalScales == null) return;
            float finalFactor = Mathf.Clamp(shrinkFinalScalePercent, 1f, 100f) / 100f;
            for (int i = 0; i < coursePlatforms.Length && i < courseOriginalScales.Length; i++)
            {
                if (coursePlatforms[i] == null) continue;
                float factor = Mathf.Lerp(1f, finalFactor, i / (float)(coursePlatforms.Length - 1));
                coursePlatforms[i].localScale = ShrinkTwoLargestAxes(courseOriginalScales[i], factor);
            }
        }

        static Vector3 ShrinkTwoLargestAxes(Vector3 size, float factor)
        {
            if (size.x <= size.y && size.x <= size.z) return new Vector3(size.x, size.y * factor, size.z * factor);
            if (size.y <= size.x && size.y <= size.z) return new Vector3(size.x * factor, size.y, size.z * factor);
            return new Vector3(size.x * factor, size.y * factor, size.z);
        }

        void Update()
        {
            if (controller == null) return;

            UpdateScatterRing();
            if (Time.timeScale <= 0f) return;
            TrackPlatformLandings();
        }

        void UpdateScatterRing()
        {
            bool show = stage == ChallengeStage.OverchargeScatter
                && controller.IsAimingOrCharging
                && controller.HasValidPredictedLanding;

            float cone = show ? controller.ScatterConeAngleFor(controller.CurrentChargeFraction) : 0f;
            show = show && cone > 0.05f;

            if (!show)
            {
                if (scatterRingRoot != null && scatterRingRoot.gameObject.activeSelf)
                {
                    scatterRingRoot.gameObject.SetActive(false);
                }
                return;
            }

            if (scatterRingRoot == null) BuildScatterRing();
            scatterRingRoot.gameObject.SetActive(true);

            Vector3 landing = controller.LastPredictedLanding;
            float distance = Vector3.Distance(controller.transform.position, landing);
            float radius = Mathf.Tan(cone * Mathf.Deg2Rad) * distance;

            Vector3 normal = controller.LastPredictedLandingNormal;
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;
            normal.Normalize();
            Vector3 right = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(right, normal).normalized;
            Vector3 centre = landing + normal * 0.08f;

            for (int i = 0; i < scatterDots.Length; i++)
            {
                float angle = i / (float)scatterDots.Length * Mathf.PI * 2f;
                scatterDots[i].position = centre + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                scatterDots[i].localScale = Vector3.one * Mathf.Clamp(radius * 0.06f, 0.12f, 0.5f);
            }
        }

        void BuildScatterRing()
        {
            scatterRingRoot = new GameObject("ScatterRing").transform;
            scatterDots = new Transform[Mathf.Max(scatterRingDots, 8)];

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material dotMaterial = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            dotMaterial.color = scatterRingColor;

            for (int i = 0; i < scatterDots.Length; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "ScatterDot" + i;
                Destroy(dot.GetComponent<Collider>());
                dot.GetComponent<Renderer>().sharedMaterial = dotMaterial;
                dot.transform.SetParent(scatterRingRoot, false);
                scatterDots[i] = dot.transform;
            }
        }

        void TrackPlatformLandings()
        {
            Transform platform = CurrentSupportPlatform();
            if (platform == null || platform == lastPlatform) return;

            Transform previous = lastPlatform;
            lastPlatform = platform;
            if (stage != ChallengeStage.SealingWalls || previous == null) return;

            int landedIndex = System.Array.IndexOf(coursePlatforms, platform);
            if (landedIndex < 0 || sealedGaps.Contains(landedIndex)) return;

            SpawnSealWall(previous.position, platform.position, landedIndex);
        }

        Transform CurrentSupportPlatform()
        {
            if (controller.IsGrounded && Physics.Raycast(controller.transform.position, Vector3.down,
                out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Transform standing = MatchCoursePlatform(hit.collider.transform);
                if (standing != null) return standing;
            }

            if (controller.IsStuck || controller.IsGrounded)
            {
                Collider crashSurface = controller.LastCrashSurface;
                if (crashSurface != null) return MatchCoursePlatform(crashSurface.transform);
            }
            return null;
        }

        Transform MatchCoursePlatform(Transform hitTransform)
        {
            if (coursePlatforms == null) return null;
            foreach (Transform platform in coursePlatforms)
            {
                if (platform != null && (hitTransform == platform || hitTransform.IsChildOf(platform)))
                {
                    return platform;
                }
            }
            return null;
        }

        void SpawnSealWall(Vector3 from, Vector3 to, int gapIndex)
        {
            if (sealWallPrefab == null) return;
            GameObject wall = Instantiate(sealWallPrefab);
            wall.name = "SealWall_" + gapIndex;

            Vector3 gapCentre = (from + to) * 0.5f;

            wall.transform.position = gapCentre + Vector3.up * (sealWallSize.y * 0.4f);

            wall.transform.rotation = Quaternion.identity;
            wall.transform.localScale = sealWallSize;

            DeathWall death = wall.GetComponent<DeathWall>();
            if (death != null)
            {
                death.moveSpeed = 0f;
                death.moveAcceleration = 0f;
            }
            wall.SetActive(true);
            sealWalls.Add(wall);
            sealedGaps.Add(gapIndex);
        }

        void ClearSealWalls()
        {
            foreach (GameObject wall in sealWalls)
            {
                if (wall != null) Destroy(wall);
            }
            sealWalls.Clear();
            sealedGaps.Clear();
        }

        void OnWallTouched(DeathWall wall)
        {
            if (controller == null) return;
            controller.RespawnAtPoint(respawnPoint != null ? respawnPoint.position : Vector3.zero);
            ResetHazards();
        }

        void OnPlayerRespawned()
        {
            ResetHazards();
        }

        void ResetHazards()
        {
            if (chaseWall != null) chaseWall.ResetToStart();
            ClearSealWalls();
            lastPlatform = null;
        }

        public void OnFinishReached()
        {
            int index = SequenceIndex(stage);
            if (index >= 0 && index < stageSequence.Length - 1)
            {
                ChallengeStageSelection.PendingStage = stageSequence[index + 1];
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            ChallengeStageSelection.PendingStage = null;
            var pause = FindAnyObjectByType<KineticEnergy.UI.PauseController>(FindObjectsInactive.Include);
            if (pause == null) return;
            if (lockedWinScreen) pause.ShowWinLocked();
            else pause.ShowWin();
        }

        string StageLabel
        {
            get
            {
                int index = SequenceIndex(stage);
                return "Challenge " + (index >= 0 ? index + 1 : 1) + "/"
                    + (stageSequence != null ? stageSequence.Length : 1) + " - " + StageName(stage);
            }
        }

        static string StageName(ChallengeStage named) => named switch
        {
            ChallengeStage.LimitedSlowdown => "Limited slowdown",
            ChallengeStage.OverchargeScatter => "Overcharge scatter",
            ChallengeStage.ChasingWall => "Chasing wall",
            ChallengeStage.SealingWalls => "Sealing walls",
            ChallengeStage.ShrinkingPlatforms => "Shrinking platforms",
            _ => "?",
        };

        void BuildHudTag()
        {
            if (!showHudTag) return;
            GameObject root = new GameObject("ChallengeStageTag");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-24f, 16f);
            rt.sizeDelta = new Vector2(620f, 34f);

            hudLabel = textGo.AddComponent<Text>();
            hudLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudLabel.fontSize = 22;
            hudLabel.alignment = TextAnchor.LowerRight;
            hudLabel.color = new Color(1f, 1f, 1f, 0.55f);
        }
    }
}

