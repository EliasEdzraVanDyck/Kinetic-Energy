using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using KineticEnergy.Camera;

namespace KineticEnergy.Player
{

    public class AimCameraLogger : MonoBehaviour
    {
        KineticCubeController controller;
        AimCameraVariantController variants;

        float aimOpenedAt;
        bool aimOpen;
        bool waitingForLanding;
        float pendingDuration;
        float pendingEnergy;
        Vector3 pendingPredicted;
        string pendingVariant = "?";

        string FilePath => Path.Combine(Application.persistentDataPath, "aim_camera_runs.csv");

        void Awake()
        {
            controller = GetComponent<KineticCubeController>();
            variants = GetComponent<AimCameraVariantController>();
        }

        void OnEnable()
        {
            if (controller == null) controller = GetComponent<KineticCubeController>();
            if (controller == null) return;
            controller.MidairAimOpened += OnAimOpened;
            controller.MidairAimFired += OnAimFired;
            controller.MidairAimReleased += OnAimReleased;
            controller.CrashRegistered += OnCrash;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.MidairAimOpened -= OnAimOpened;
            controller.MidairAimFired -= OnAimFired;
            controller.MidairAimReleased -= OnAimReleased;
            controller.CrashRegistered -= OnCrash;
        }

        void OnAimOpened()
        {
            aimOpen = true;
            aimOpenedAt = Time.unscaledTime;
        }

        void OnAimFired(float energyFraction, Vector3 predictedLanding)
        {
            if (!aimOpen) return;
            aimOpen = false;
            pendingDuration = Time.unscaledTime - aimOpenedAt;
            pendingEnergy = energyFraction;
            pendingPredicted = predictedLanding;
            pendingVariant = VariantName();
            waitingForLanding = true;
        }

        void OnAimReleased()
        {
            if (!aimOpen) return;
            aimOpen = false;
            WriteRow(VariantName(), Time.unscaledTime - aimOpenedAt, 0f, false, -1f);
        }

        void OnCrash(Vector3 position)
        {
            if (!waitingForLanding) return;
            waitingForLanding = false;
            WriteRow(pendingVariant, pendingDuration, pendingEnergy, true,
                Vector3.Distance(position, pendingPredicted));
        }

        string VariantName() => variants != null ? variants.currentVariant.ToString() : "?";

        void WriteRow(string variant, float duration, float energy, bool fired, float landingError)
        {
            try
            {
                bool fresh = !File.Exists(FilePath);
                var row = new StringBuilder();
                if (fresh) row.AppendLine("timestamp,scene,variant,aimSecondsUnscaled,energyAtFire,fired,landingErrorMeters");
                row.AppendLine(string.Join(",",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    SceneManager.GetActiveScene().name,
                    variant,
                    duration.ToString("F2", CultureInfo.InvariantCulture),
                    energy.ToString("F3", CultureInfo.InvariantCulture),
                    fired ? "1" : "0",
                    landingError < 0f ? "" : landingError.ToString("F2", CultureInfo.InvariantCulture)));
                File.AppendAllText(FilePath, row.ToString());
            }
            catch (Exception)
            {

            }
        }
    }
}

