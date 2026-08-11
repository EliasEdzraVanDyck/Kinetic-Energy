using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // Carries the tester's variant choice from the menu (or pause menu) into the Gauntlet
    // scene. Static so it survives the scene load; consumed once by GauntletRunLogger.
    public static class SlowdownVariantSelection
    {
        // null = no choice pending, keep whatever the scene's Player was saved with.
        public static bool? PendingVariantB;
    }

    // The Gauntlet's instrumentation (one per scene). Applies the chosen slowdown variant to
    // the Player, then records per run:
    //   - total slow-time used, and slow-time used per beat
    //   - number of separate midair aims opened, per beat
    //   - how often the slowdown resource hit zero, and at which beat
    //   - attempts per beat (re-entering a beat's start region = a fresh attempt)
    //   - energy remaining at the finish line, and total completion time
    // Everything is written to the Unity log AND appended as one CSV row per run to
    // gauntlet_runs.csv in Application.persistentDataPath.
    public class GauntletRunLogger : MonoBehaviour
    {
        [Tooltip("The scene's Player - wired by the setup script.")]
        public KineticCubeController controller;
        [Tooltip("Small HUD label naming the active variant - wired by the setup script.")]
        public Text variantLabel;

        const int BeatCount = 5;

        bool variantB;
        int currentBeat;
        float runStartTime;
        float lastSlowdownTotal;
        bool runCompleted;

        readonly int[] attemptsPerBeat = new int[BeatCount + 1];
        readonly float[] slowTimePerBeat = new float[BeatCount + 1];
        readonly int[] aimsPerBeat = new int[BeatCount + 1];
        readonly int[] resourceZeroPerBeat = new int[BeatCount + 1];

        void Awake()
        {
            if (controller != null && SlowdownVariantSelection.PendingVariantB.HasValue)
            {
                controller.slowdownMode = SlowdownVariantSelection.PendingVariantB.Value
                    ? SlowdownMode.EnergyTank
                    : SlowdownMode.AimBudget;
                SlowdownVariantSelection.PendingVariantB = null;
            }
            variantB = controller != null && controller.slowdownMode == SlowdownMode.EnergyTank;
        }

        void Start()
        {
            runStartTime = Time.realtimeSinceStartup;

            if (controller != null)
            {
                controller.MidairAimOpened += OnMidairAimOpened;
                controller.SlowdownDepleted += OnSlowdownDepleted;
                // Tuning parity is the whole experiment's precondition - always log the
                // numbers this run actually used.
                Debug.Log($"GauntletRun: variant={(variantB ? "B (energy tank)" : "A (aim budget)")}, "
                    + $"aimBudgetSeconds={controller.aimBudgetSeconds}, tankDrainPerSecond={controller.tankDrainPerSecond}, "
                    + $"startingEnergy={controller.startingEnergyFraction}");
            }

            if (variantLabel != null)
            {
                variantLabel.text = variantB ? "Variant B - aiming drains the tank" : "Variant A - separate aim budget";
            }
        }

        void OnDestroy()
        {
            if (controller != null)
            {
                controller.MidairAimOpened -= OnMidairAimOpened;
                controller.SlowdownDepleted -= OnSlowdownDepleted;
            }
        }

        void Update()
        {
            if (controller == null || runCompleted) return;

            // Attribute slow-time to whichever beat is currently being attempted.
            float total = controller.SlowdownSecondsUsed;
            float delta = total - lastSlowdownTotal;
            if (delta > 0f) slowTimePerBeat[Mathf.Clamp(currentBeat, 0, BeatCount)] += delta;
            lastSlowdownTotal = total;
        }

        public void ReportBeatEntered(int beatIndex)
        {
            if (runCompleted) return;
            int beat = Mathf.Clamp(beatIndex, 1, BeatCount);
            attemptsPerBeat[beat]++;
            currentBeat = beat;
        }

        void OnMidairAimOpened()
        {
            if (!runCompleted) aimsPerBeat[Mathf.Clamp(currentBeat, 0, BeatCount)]++;
        }

        void OnSlowdownDepleted()
        {
            if (!runCompleted) resourceZeroPerBeat[Mathf.Clamp(currentBeat, 0, BeatCount)]++;
        }

        // Called by the finish line. Logs the run summary and appends the CSV row.
        public void CompleteRun()
        {
            if (runCompleted || controller == null) return;
            runCompleted = true;

            float completionSeconds = Time.realtimeSinceStartup - runStartTime;
            float energyAtFinish = controller.EnergyFraction;

            var summary = new StringBuilder();
            summary.AppendLine($"GauntletRun COMPLETE: variant={(variantB ? "B" : "A")}, "
                + $"time={completionSeconds:F1}s, energyAtFinish={energyAtFinish:P0}, "
                + $"totalSlowTime={controller.SlowdownSecondsUsed:F2}s");
            for (int beat = 1; beat <= BeatCount; beat++)
            {
                summary.AppendLine($"  beat {beat}: attempts={attemptsPerBeat[beat]}, "
                    + $"slowTime={slowTimePerBeat[beat]:F2}s, midairAims={aimsPerBeat[beat]}, "
                    + $"resourceHitZero={resourceZeroPerBeat[beat]}");
            }
            Debug.Log(summary.ToString());

            AppendCsvRow(completionSeconds, energyAtFinish);
        }

        void AppendCsvRow(float completionSeconds, float energyAtFinish)
        {
            string path = Path.Combine(Application.persistentDataPath, "gauntlet_runs.csv");
            var inv = CultureInfo.InvariantCulture;
            var row = new StringBuilder();

            if (!File.Exists(path))
            {
                row.AppendLine("timestamp,variant,completionSeconds,energyAtFinish,totalSlowTime,"
                    + "attemptsB1,attemptsB2,attemptsB3,attemptsB4,attemptsB5,"
                    + "slowB1,slowB2,slowB3,slowB4,slowB5,"
                    + "aimsB1,aimsB2,aimsB3,aimsB4,aimsB5,"
                    + "zeroB1,zeroB2,zeroB3,zeroB4,zeroB5");
            }

            row.Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv));
            row.Append(',').Append(variantB ? "B" : "A");
            row.Append(',').Append(completionSeconds.ToString("F1", inv));
            row.Append(',').Append(energyAtFinish.ToString("F3", inv));
            row.Append(',').Append(controller.SlowdownSecondsUsed.ToString("F2", inv));
            for (int beat = 1; beat <= BeatCount; beat++) row.Append(',').Append(attemptsPerBeat[beat]);
            for (int beat = 1; beat <= BeatCount; beat++) row.Append(',').Append(slowTimePerBeat[beat].ToString("F2", inv));
            for (int beat = 1; beat <= BeatCount; beat++) row.Append(',').Append(aimsPerBeat[beat]);
            for (int beat = 1; beat <= BeatCount; beat++) row.Append(',').Append(resourceZeroPerBeat[beat]);
            row.AppendLine();

            try
            {
                File.AppendAllText(path, row.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"GauntletRunLogger: could not append to {path} - {e.Message}");
            }
        }
    }
}
