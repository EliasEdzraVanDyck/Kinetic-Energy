using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    public enum ChallengeStage
    {
        LimitedSlowdown,   // 1 - the midair aim slow-down runs on the budget meter
        OverchargeScatter, // 2 - big launches scatter (charge buys distance, costs precision)
        ChasingWall,       // 3 - a purple death wall sweeps the level behind the player
        SealingWalls,      // 4 - every platform-to-platform jump seals the gap behind
    }

    // Carries the chosen stage across the scene reload (the SlowdownVariantSelection
    // pattern): set before LoadScene, consumed exactly once by the controller's Start.
    public static class ChallengeStageSelection
    {
        public static ChallengeStage? PendingStage;
    }

    // Level 8's stage director (a scene object, never a prefab). The four challenges run
    // in SEQUENCE - reaching the end pad reloads the level on the next one - and the pause
    // menu's Scenes panel carries a second column that jumps straight to a stage (always a
    // restart from the beginning, like every scene button). No hotkey cycling here on
    // purpose: the run itself is the switch.
    public class ChallengeStageController : MonoBehaviour
    {
        [Tooltip("Stage played when no pause-menu selection is pending (a fresh boot).")]
        public ChallengeStage startingStage = ChallengeStage.LimitedSlowdown;

        [Header("1 - Limited slowdown")]
        [Tooltip("Seconds of midair slow-down in the budget (the meter refills on every crash).")]
        public float slowdownBudgetSeconds = 2f;

        [Header("2 - Overcharge scatter")]
        // Same tuning the QuarryChallenge harness uses for its scatter variant.
        [Tooltip("Scatter cone radius (degrees) at full charge.")]
        public float scatterMaxAngle = 14f;
        [Tooltip("Charge fraction where the cone starts opening.")]
        [Range(0f, 1f)] public float scatterStartFraction = 0.25f;

        [Header("3 - Chasing wall (wired by setup)")]
        [Tooltip("The moving wall instance - speed and start position are edited on it directly.")]
        public DeathWall chaseWall;

        [Header("4 - Sealing walls (wired by setup)")]
        [Tooltip("The DeathWall prefab cloned as a static seal in each jumped gap.")]
        public GameObject sealWallPrefab;
        [Tooltip("The course platforms in run order - landings are matched against these.")]
        public Transform[] coursePlatforms;
        [Tooltip("World size of a spawned seal wall (x thickness, y height, z width).")]
        public Vector3 sealWallSize = new Vector3(1.5f, 40f, 40f);

        [Header("Respawn")]
        [Tooltip("Where a death-wall touch puts the player - the level's ordinary respawn point.")]
        public Transform respawnPoint;

        KineticCubeController controller;
        ChallengeStage stage;
        Text hudLabel;
        Transform lastPlatform;
        readonly List<GameObject> sealWalls = new List<GameObject>();
        readonly HashSet<int> sealedGaps = new HashSet<int>(); // keyed by the gap's far platform index

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
            // Neutral baseline first, then the active stage's one twist on top.
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
                chaseWall.gameObject.SetActive(stage == ChallengeStage.ChasingWall);
                chaseWall.ResetToStart();
            }
            ClearSealWalls();
            lastPlatform = null;
            if (hudLabel != null) hudLabel.text = StageLabel;
        }

        void Update()
        {
            if (controller == null || Time.timeScale <= 0f) return;
            TrackPlatformLandings();
        }

        // Watches which course platform the player stands on. In the sealing stage, a
        // landing on a NEW platform walls off the gap behind it: a static death wall
        // midway between the landed platform and its predecessor in course order (the
        // "2 consecutive platforms") - no way back.
        void TrackPlatformLandings()
        {
            if (!controller.IsGrounded) return;
            if (!Physics.Raycast(controller.transform.position, Vector3.down, out RaycastHit hit, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;

            Transform platform = MatchCoursePlatform(hit.collider.transform);
            if (platform == null || platform == lastPlatform) return;

            Transform previous = lastPlatform;
            lastPlatform = platform;
            if (stage != ChallengeStage.SealingWalls || previous == null) return;

            int landedIndex = System.Array.IndexOf(coursePlatforms, platform);
            if (landedIndex <= 0 || sealedGaps.Contains(landedIndex)) return;

            Vector3 a = coursePlatforms[landedIndex - 1].position;
            Vector3 b = coursePlatforms[landedIndex].position;
            SpawnSealWall((a + b) * 0.5f, landedIndex);
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

        void SpawnSealWall(Vector3 gapCentre, int gapIndex)
        {
            if (sealWallPrefab == null) return;
            GameObject wall = Instantiate(sealWallPrefab);
            wall.name = "SealWall_" + gapIndex;
            // Centre lifted so the wall reaches well above the platform tops and a little
            // below them - over is a full launch away, under is the damage floor.
            wall.transform.position = gapCentre + Vector3.up * (sealWallSize.y * 0.4f);
            wall.transform.localScale = sealWallSize;
            DeathWall death = wall.GetComponent<DeathWall>();
            if (death != null) death.moveSpeed = 0f;
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

        // Any respawn resets the whole threat state - the chase wall returns to its start
        // and every seal clears, so a retry faces the level as the stage began.
        void ResetHazards()
        {
            if (chaseWall != null) chaseWall.ResetToStart();
            ClearSealWalls();
            lastPlatform = null;
        }

        // The end pad's trigger lands here: advance and reload, or win after the last stage.
        public void OnFinishReached()
        {
            int last = System.Enum.GetValues(typeof(ChallengeStage)).Length - 1;
            if ((int)stage < last)
            {
                ChallengeStageSelection.PendingStage = (ChallengeStage)((int)stage + 1);
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            // All four cleared - the ordinary win screen (the pause menu with the win
            // label showing). A restart from there begins the sequence fresh.
            ChallengeStageSelection.PendingStage = null;
            var pause = FindAnyObjectByType<KineticEnergy.UI.PauseController>(FindObjectsInactive.Include);
            if (pause != null) pause.ShowWin();
        }

        string StageLabel => stage switch
        {
            ChallengeStage.LimitedSlowdown => "Challenge 1/4 - Limited slowdown",
            ChallengeStage.OverchargeScatter => "Challenge 2/4 - Overcharge scatter",
            ChallengeStage.ChasingWall => "Challenge 3/4 - Chasing wall",
            ChallengeStage.SealingWalls => "Challenge 4/4 - Sealing walls",
            _ => "Challenge ?",
        };

        void BuildHudTag()
        {
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
