using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KineticEnergy.Player;
using KineticEnergy.Camera;
using KineticEnergy.UI;
using KineticEnergy.Level;

namespace KineticEnergy.EditorSetup
{

    public static class KineticEnergySetup
    {
        const string QuarryScenePath = "Assets/Scenes/Quarry.unity";
        const string Level1ScenePath = "Assets/Scenes/Level1.unity";
        const string Level2ScenePath = "Assets/Scenes/Level2.unity";
        const string Level3ScenePath = "Assets/Scenes/Level3.unity";
        const string GauntletScenePath = "Assets/Scenes/Gauntlet.unity";
        const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string PrefabFolder = "Assets/Prefabs";
        const string MaterialFolder = "Assets/Materials";
        const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";

        const float MinLaunchForce = 60f;
        const float MaxLaunchForce = 130f;
        const float MaxChargeTime = 1.5f;
        const float MinLaunchDamping = 2.8f;
        const float MaxLaunchDamping = 1.0f;
        const float DownLaunchDamping = 0.2f;
        const float Gravity = -30f;
        const float ChargeAccumulationRate = 0.3f;

        const float ChargeAcceleration = 1f;

        const float AirControlAcceleration = 14f;
        const float EnergyCostPerFullCharge = 1f;
        const float MinEnergyReserve = 0.05f;
        const float GroundedRefundMultiplier = 1f;
        const float MidairRefundBaseMultiplier = 1f;
        const float MidairRefundSpendFactor = 0.3f;
        const float PoundFlightRefundMultiplier = 1f;
        const float PlainFallDamping = 0.2f;

        const float GroundPoundBoostMultiplier = 1.5f;
        const float GroundPoundHopHeight = 0.2f;
        const float GroundPoundSlowDuration = 0.5f;
        const float GroundPoundChargeBaseSpeed = 1.5f;
        const float GroundPoundChargeSpeedGrowth = 5f;
        const float ChargeTimeScale = 0.2f;

        const float LaunchFlightTimeScale = 2f;
        const float FlightTimeScaleEnergyBonus = 1f;

        const float FallSpeedUpStart = 0.01f;
        const float FallSpeedUpEnd = 0.5f;
        const float AimBudgetSeconds = 2f;

        const float TankDrainPerSecond = 1f / AimBudgetSeconds;
        const float DialStickRate = 0.5f;
        const float DialWheelStep = 0.05f;
        const float ReferenceAimPitchDegrees = 30f;
        const float SimulationTimestep = 0.02f;

        static void ApplyPlayerTuning(KineticCubeController controller)
        {
            controller.minLaunchForce = MinLaunchForce;
            controller.maxLaunchForce = MaxLaunchForce;
            controller.maxChargeTime = MaxChargeTime;
            controller.minLaunchDamping = MinLaunchDamping;
            controller.maxLaunchDamping = MaxLaunchDamping;
            controller.downLaunchDamping = DownLaunchDamping;
            controller.gravity = Gravity;
            controller.chargeAccumulationRate = ChargeAccumulationRate;
            controller.chargeAcceleration = ChargeAcceleration;
            controller.groundPoundBoostMultiplier = GroundPoundBoostMultiplier;
            controller.groundPoundHopHeight = GroundPoundHopHeight;
            controller.groundPoundSlowDuration = GroundPoundSlowDuration;
            controller.groundPoundChargeBaseSpeed = GroundPoundChargeBaseSpeed;
            controller.groundPoundChargeSpeedGrowth = GroundPoundChargeSpeedGrowth;
            controller.energyCostPerFullCharge = EnergyCostPerFullCharge;
            controller.minEnergyReserve = MinEnergyReserve;
            controller.groundedRefundMultiplier = GroundedRefundMultiplier;
            controller.midairRefundBaseMultiplier = MidairRefundBaseMultiplier;
            controller.midairRefundSpendFactor = MidairRefundSpendFactor;
            controller.poundFlightRefundMultiplier = PoundFlightRefundMultiplier;
            controller.plainFallDamping = PlainFallDamping;
            controller.chargeTimeScale = ChargeTimeScale;
            controller.launchFlightTimeScale = LaunchFlightTimeScale;
            controller.flightTimeScaleEnergyBonus = FlightTimeScaleEnergyBonus;
            controller.fallSpeedUpStart = FallSpeedUpStart;
            controller.fallSpeedUpEnd = FallSpeedUpEnd;
            controller.aimBudgetSeconds = AimBudgetSeconds;
            controller.tankDrainPerSecond = TankDrainPerSecond;
            controller.dialStickRate = DialStickRate;
            controller.dialWheelStep = DialWheelStep;
            controller.defaultAimPitch = -ReferenceAimPitchDegrees;
            controller.aimDeadzone = 0.15f;
            controller.aimRotationSpeed = 90f;
            controller.minAimPitch = -80f;
            controller.maxAimPitch = 80f;
            controller.maxPredictionSteps = 3000;
            controller.previewLineHeight = 0.65f;
            controller.groundCheckDistance = 0.6f;
            controller.launchGraceDuration = 0.15f;
            controller.minLaunchClearDistance = 2f;
            controller.flatGroundStickThreshold = 0.9f;
            controller.slamDownwardThreshold = 0.7f;
            controller.stuckOnGroundTickThreshold = 10;
            controller.nonStickyWallStickDuration = 0.3f;
            controller.maxLaunchesPerFlight = 2;
            controller.startingEnergyFraction = 0.2f;
            controller.infiniteEnergy = false;
            controller.slowdownMode = SlowdownMode.Unlimited;
            controller.fallResetY = -30f;

            controller.groundedAimWithMouse = true;
            controller.groundedMouseAimSensitivity = 0.15f;
            controller.wasdCameraTurnMultiplier = 1.5f;

            controller.moveAction = FindActionReference("Player", "Move");
            controller.groundedAimAction = FindActionReference("Player", "Launch");
            controller.groundedLaunchAction = FindActionReference("Player", "Fire");
            controller.upLaunchAction = FindActionReference("Player", "LaunchUp");

            controller.groundPoundAction = FindActionReference("Player", "SelectGhostPreview");
            controller.cancelChargeAction = FindActionReference("Player", "CancelCharge");
            controller.airAimAction = FindActionReference("Player", "FastPacedAim");
            controller.airLaunchAction = FindActionReference("Player", "FastPacedLaunch");
        }

        static void MeasureLaunchDistances(out float unitL, out float unitH)
        {

            float angleRad = ReferenceAimPitchDegrees * Mathf.Deg2Rad;
            Vector2 velocity = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * MaxLaunchForce;
            Vector2 position = Vector2.zero;
            unitL = 0f;
            for (int i = 0; i < 5000; i++)
            {
                velocity.y += Gravity * SimulationTimestep;
                velocity /= 1f + MaxLaunchDamping * SimulationTimestep;
                position += velocity * SimulationTimestep;
                if (position.y < 0f && velocity.y < 0f)
                {
                    unitL = position.x;
                    break;
                }
            }

            float upVelocity = MaxLaunchForce;
            float height = 0f;
            unitH = 0f;
            for (int i = 0; i < 5000; i++)
            {
                upVelocity += Gravity * SimulationTimestep;
                upVelocity /= 1f + MaxLaunchDamping * SimulationTimestep;
                if (upVelocity <= 0f) break;
                height += upVelocity * SimulationTimestep;
            }
            unitH = height;

            if (unitL <= 1f || unitH <= 1f)
            {
                throw new Exception($"KineticEnergySetup: implausible launch measurement (L={unitL}, H={unitH}).");
            }
        }

        [MenuItem("Tools/Kinetic Energy/Setup All")]
        public static void SetupAll()
        {
            RefreshPlayerPrefab();
            RefreshPauseSystemPrefab();
            SetupMainMenu();
            SetupQuarry();
            SetupGauntlet();
            UpdateBuildSettings();

            Debug.Log("KineticEnergySetup: SetupAll complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Build Quarry Only")]
        public static void BuildQuarryOnly()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { QuarryScenePath },
                locationPathName = "Builds/QuarryOnly/GD3 Retake Kinetic Energy.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception($"KineticEnergySetup: Quarry-only build FAILED - {report.summary.result}, {report.summary.totalErrors} errors.");
            }
            Debug.Log($"KineticEnergySetup: Quarry-only build complete OK -> {options.locationPathName} ({report.summary.totalSize / (1024 * 1024)} MB)");
        }

        [MenuItem("Tools/Kinetic Energy/Build From Build Settings")]
        public static void BuildFromBuildSettings()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new Exception("KineticEnergySetup: no scenes are enabled in Build Settings - nothing to build.");
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/GD3RetakeKineticEnergy/GD3 Retake Kinetic Energy.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception($"KineticEnergySetup: build FAILED - {report.summary.result}, {report.summary.totalErrors} errors.");
            }
            Debug.Log($"KineticEnergySetup: build complete OK -> {options.locationPathName} ({report.summary.totalSize / (1024 * 1024)} MB, scenes: {string.Join(", ", scenes)})");
        }

        [MenuItem("Tools/Kinetic Energy/Build Web From Build Settings")]
        public static void BuildWebFromBuildSettings()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new Exception("KineticEnergySetup: no scenes are enabled in Build Settings - nothing to build.");
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/GD3RetakeKineticEnergyWeb",
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception($"KineticEnergySetup: web build FAILED - {report.summary.result}, {report.summary.totalErrors} errors.");
            }
            Debug.Log($"KineticEnergySetup: web build complete OK -> {options.locationPathName} ({report.summary.totalSize / (1024 * 1024)} MB, scenes: {string.Join(", ", scenes)})");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Aim Camera Variants")]
        public static void SetupAimCameraVariants()
        {
            const string presetFolder = "Assets/Settings/AimCameraPresets";
            if (!AssetDatabase.IsValidFolder(presetFolder)) AssetDatabase.CreateFolder("Assets/Settings", "AimCameraPresets");

            AssetDatabase.DeleteAsset(presetFolder + "/AimCameraC_OtsDriftDolly.asset");
            AssetDatabase.DeleteAsset(presetFolder + "/AimCameraD_OtsParallax.asset");

            AimCameraPreset a = LoadOrCreatePreset(presetFolder + "/AimCameraA_Baseline.asset", p =>
            {
                p.variant = AimCameraVariant.Baseline;
                p.displayName = "Frozen first person";
            });

            AimCameraPreset b = LoadOrCreatePreset(presetFolder + "/AimCameraB_OtsDrift.asset", p =>
            {
                p.variant = AimCameraVariant.OtsParallax;
                p.otsBack = 2.4f;
                p.otsRise = 0.6f;
                p.otsSide = 0.7f;
            });
            b.variant = AimCameraVariant.OtsParallax;
            b.driftYawAmplitude = 1.5f;
            b.driftPitchAmplitude = 0.5f;
            b.driftPeriod = 3.5f;
            EditorUtility.SetDirty(b);

            AimCameraPreset c = LoadOrCreatePreset(presetFolder + "/AimCameraC_BaselinePip.asset", p =>
            {
                p.variant = AimCameraVariant.BaselinePip;
                p.displayName = "First person + landing view";
                p.pipEnabled = true;
            });

            AimCameraPreset d = LoadOrCreatePreset(presetFolder + "/AimCameraD_OtsParallaxPip.asset", p =>
            {
                p.variant = AimCameraVariant.OtsParallaxPip;
                p.displayName = "OTS + parallax + landing view";
                p.pipEnabled = true;
                p.otsRise = 0.6f;
                p.otsSide = 0.7f;
                p.driftYawAmplitude = 1.5f;
                p.driftPitchAmplitude = 0.5f;
                p.driftPeriod = 3.5f;
            });

            b.otsBack = 1.8f;
            d.otsBack = 1.8f;
            EditorUtility.SetDirty(b);
            EditorUtility.SetDirty(d);

            AimCameraPreset e = LoadOrCreatePreset(presetFolder + "/AimCameraE_FreeLookFp.asset", p =>
            {
                p.variant = AimCameraVariant.FreeLookFirstPerson;
                p.displayName = "First person + free look";
            });

            AimCameraPreset f = LoadOrCreatePreset(presetFolder + "/AimCameraF_FreeLookOts.asset", p =>
            {
                p.variant = AimCameraVariant.FreeLookOts;
                p.otsBack = 1.8f;
                p.otsRise = 0.6f;
                p.otsSide = 0.7f;
                p.driftYawAmplitude = 1.5f;
                p.driftPitchAmplitude = 0.5f;
                p.driftPeriod = 3.5f;
            });

            a.displayName = "First person";
            b.displayName = "Behind the player";
            c.displayName = "First person + landing window";
            d.displayName = "Behind player + landing window";
            e.displayName = "First person + look around";
            f.displayName = "Behind player + look around";
            EditorUtility.SetDirty(a);
            EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(e);
            EditorUtility.SetDirty(f);

            string playerPath = PrefabFolder + "/Player.prefab";
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                AimCameraVariantController variants = playerRoot.GetComponent<AimCameraVariantController>();
                if (variants == null) variants = playerRoot.AddComponent<AimCameraVariantController>();
                variants.baselinePreset = a;
                variants.otsParallaxPreset = b;
                variants.baselinePipPreset = c;
                variants.otsParallaxPipPreset = d;
                variants.freeLookFirstPersonPreset = e;
                variants.freeLookOtsPreset = f;
                if (playerRoot.GetComponent<AimCameraLogger>() == null) playerRoot.AddComponent<AimCameraLogger>();
                PrefabUtility.SaveAsPrefabAsset(playerRoot, playerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(playerRoot); }

            string pausePath = PrefabFolder + "/PauseSystem.prefab";
            GameObject pauseRoot = PrefabUtility.LoadPrefabContents(pausePath);
            try
            {
                Transform pausePanel = pauseRoot.transform.Find("PauseCanvas/PausePanel");
                PauseController pauseController = pauseRoot.GetComponentInChildren<PauseController>(true);
                if (pausePanel == null || pauseController == null)
                {
                    throw new Exception("KineticEnergySetup: PauseSystem.prefab is missing PauseCanvas/PausePanel or PauseController.");
                }

                DestroyDirectChildIfExists(pausePanel, "CameraVariantButton");
                DestroyDirectChildIfExists(pausePanel, "CameraVariantHint");
                DestroyDirectChildIfExists(pausePanel, "CameraVariantEnergyNote");

                Font font = FindBestFont();
                Color accent = new Color(1f, 0.82f, 0.2f);
                GameObject button = CreateButton("CameraVariantButton", pausePanel, "Camera: Variant A", font, accent,
                    Vector2.zero, new Vector2(600f, 60f));
                RectTransform buttonRect = button.GetComponent<RectTransform>();
                buttonRect.anchorMin = Vector2.zero;
                buttonRect.anchorMax = Vector2.zero;
                buttonRect.pivot = Vector2.zero;
                buttonRect.anchoredPosition = new Vector2(24f, 24f);
                WireButton(button, pauseController.OnCameraVariantClicked);
                Text buttonLabel = button.GetComponentInChildren<Text>(true);

                buttonLabel.resizeTextForBestFit = true;
                buttonLabel.resizeTextMinSize = 12;
                buttonLabel.resizeTextMaxSize = 26;
                pauseController.cameraVariantLabel = buttonLabel;
                EditorUtility.SetDirty(pauseController);

                Text energyNote = CreateText("CameraVariantEnergyNote", pausePanel, "",
                    font, 20, Vector2.zero, new Vector2(640f, 30f));
                RectTransform energyNoteRect = energyNote.rectTransform;
                energyNoteRect.anchorMin = Vector2.zero;
                energyNoteRect.anchorMax = Vector2.zero;
                energyNoteRect.pivot = Vector2.zero;
                energyNoteRect.anchoredPosition = new Vector2(24f, 84f);
                energyNote.alignment = TextAnchor.LowerLeft;
                energyNote.color = accent;
                pauseController.cameraVariantEnergyNote = energyNote;

                Text hint = CreateText("CameraVariantHint", pausePanel,
                    "V / D-pad Right: next camera variant. C / D-pad Left: previous. Q / Right Stick Click: swap shoulder.\nThe feedback form asks which variant you preferred.",
                    font, 20, Vector2.zero, new Vector2(820f, 64f));
                RectTransform hintRect = hint.rectTransform;
                hintRect.anchorMin = Vector2.zero;
                hintRect.anchorMax = Vector2.zero;
                hintRect.pivot = Vector2.zero;
                hintRect.anchoredPosition = new Vector2(24f, 122f);
                hint.alignment = TextAnchor.LowerLeft;
                hint.color = new Color(1f, 1f, 1f, 0.75f);
                pauseController.cameraVariantHint = hint.gameObject;

                PrefabUtility.SaveAsPrefabAsset(pauseRoot, pausePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(pauseRoot); }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: aim camera variants setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Add Hud Meters To Levels 2 And 3")]
        public static void AddHudMetersToLevels2And3()
        {
            foreach (string scenePath in new[] { Level2ScenePath, Level3ScenePath })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                KineticCubeController controller = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
                GameObject pauseSystemGo = GameObject.Find("PauseSystem");
                Transform pauseCanvas = pauseSystemGo != null ? pauseSystemGo.transform.Find("PauseCanvas") : null;
                if (controller == null || pauseCanvas == null)
                {
                    throw new Exception($"KineticEnergySetup: {scenePath} is missing its Player or PauseSystem/PauseCanvas.");
                }

                Transform embeddedUi = pauseCanvas.Find("EnergyMeter");
                if (embeddedUi != null && !PrefabUtility.IsAnyPrefabInstanceRoot(embeddedUi.gameObject))
                {
                    embeddedUi.gameObject.SetActive(false);
                }
                Transform embeddedController = pauseSystemGo.transform.Find("EnergyMeter");
                if (embeddedController != null) embeddedController.gameObject.SetActive(false);

                GameObject energyMeter = InstantiatePrefab("EnergyMeter");
                energyMeter.transform.SetParent(pauseCanvas, false);
                controller.energyMeter = energyMeter.GetComponent<EnergyMeterController>();

                GameObject slowdownMeter = InstantiatePrefab("SlowdownMeter");
                slowdownMeter.transform.SetParent(pauseCanvas, false);
                controller.slowdownMeter = slowdownMeter.GetComponent<EnergyMeterController>();

                EditorUtility.SetDirty(controller);
                SaveOpenScene(scenePath);
                Debug.Log($"KineticEnergySetup: HUD meters added to {scenePath} OK");
            }
        }

        [MenuItem("Tools/Kinetic Energy/Wire Controls Hint To Pause")]
        public static void WireControlsHintToPause()
        {
            string pausePath = PrefabFolder + "/PauseSystem.prefab";
            GameObject pauseRoot = PrefabUtility.LoadPrefabContents(pausePath);
            try
            {
                PauseController pauseController = pauseRoot.GetComponentInChildren<PauseController>(true);
                Transform hint = FindChildRecursive(pauseRoot.transform, "ControlsHintLabel");
                if (pauseController == null || hint == null)
                {
                    throw new Exception("KineticEnergySetup: PauseSystem.prefab is missing PauseController or ControlsHintLabel.");
                }
                pauseController.controlsHintLabel = hint.gameObject;
                EditorUtility.SetDirty(pauseController);
                PrefabUtility.SaveAsPrefabAsset(pauseRoot, pausePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(pauseRoot); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: controls hint wired to pause OK");
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName) return child;
            }
            return null;
        }

        [MenuItem("Tools/Kinetic Energy/Add Info Button To Pause Menu")]
        public static void AddInfoButtonToPauseMenu()
        {
            string pausePath = PrefabFolder + "/PauseSystem.prefab";
            GameObject pauseRoot = PrefabUtility.LoadPrefabContents(pausePath);
            try
            {
                Transform pausePanel = pauseRoot.transform.Find("PauseCanvas/PausePanel");
                PauseController pauseController = pauseRoot.GetComponentInChildren<PauseController>(true);
                if (pausePanel == null || pauseController == null)
                {
                    throw new Exception("KineticEnergySetup: PauseSystem.prefab is missing PauseCanvas/PausePanel or PauseController.");
                }

                DestroyDirectChildIfExists(pausePanel, "InfoButton");
                DestroyDirectChildIfExists(pausePanel, "BuildInfoButton");
                Font font = FindBestFont();
                Color accent = new Color(1f, 0.82f, 0.2f);
                GameObject info = CreateButton("BuildInfoButton", pausePanel, "BuildInfo", font, accent,
                    Vector2.zero, new Vector2(200f, 56f));
                RectTransform infoRect = info.GetComponent<RectTransform>();
                infoRect.anchorMin = new Vector2(1f, 0f);
                infoRect.anchorMax = new Vector2(1f, 0f);
                infoRect.pivot = new Vector2(1f, 0f);
                infoRect.anchoredPosition = new Vector2(-24f, 24f);
                WireButton(info, pauseController.OnInfoClicked);
                pauseController.infoButton = info;
                EditorUtility.SetDirty(pauseController);

                PrefabUtility.SaveAsPrefabAsset(pauseRoot, pausePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(pauseRoot); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: info button added to pause menu OK");
        }

        [MenuItem("Tools/Kinetic Energy/Add Resume Button To Pause Menu")]
        public static void AddResumeButtonToPauseMenu()
        {
            string pausePath = PrefabFolder + "/PauseSystem.prefab";
            GameObject pauseRoot = PrefabUtility.LoadPrefabContents(pausePath);
            try
            {
                Transform pausePanel = pauseRoot.transform.Find("PauseCanvas/PausePanel");
                PauseController pauseController = pauseRoot.GetComponentInChildren<PauseController>(true);
                if (pausePanel == null || pauseController == null)
                {
                    throw new Exception("KineticEnergySetup: PauseSystem.prefab is missing PauseCanvas/PausePanel or PauseController.");
                }

                DestroyDirectChildIfExists(pausePanel, "ResumeButton");
                Font font = FindBestFont();
                Color accent = new Color(1f, 0.82f, 0.2f);
                GameObject resume = CreateButton("ResumeButton", pausePanel, "Resume", font, accent,
                    new Vector2(0f, 185f), new Vector2(300f, 70f));
                resume.transform.SetSiblingIndex(0);
                WireButton(resume, pauseController.TogglePause);
                pauseController.firstPauseButton = resume;
                EditorUtility.SetDirty(pauseController);

                PrefabUtility.SaveAsPrefabAsset(pauseRoot, pausePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(pauseRoot); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: resume button added to pause menu OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Quarry Economy Scene")]
        public static void SetupQuarryEconomy()
        {
            const string scenePath = "Assets/Scenes/QuarryEconomy.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                throw new Exception("KineticEnergySetup: QuarryEconomy.unity does not exist - duplicate QuarryNew first.");
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var variants = UnityEngine.Object.FindAnyObjectByType<AimCameraVariantController>(FindObjectsInactive.Include);
            if (variants != null)
            {
                variants.variantSwitchingEnabled = false;
                variants.currentVariant = AimCameraVariant.Baseline;
                EditorUtility.SetDirty(variants);
            }

            if (UnityEngine.Object.FindAnyObjectByType<EconomyVariantController>(FindObjectsInactive.Include) == null)
            {
                GameObject harness = new GameObject("EconomyVariants");
                harness.AddComponent<EconomyVariantController>();
            }

            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: quarry economy scene setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Create Premium Energy Meter Prefab")]
        public static void CreatePremiumEnergyMeterPrefab()
        {
            BuildPremiumMeterVariant(PrefabFolder + "/PremiumEnergyMeter.prefab", 8);
        }

        static void BuildPremiumMeterVariant(string path, int normalBlocks)
        {
            string sourcePath = PrefabFolder + "/EnergyMeter.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Debug.Log($"KineticEnergySetup: {path} already exists OK");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null)
            {
                throw new Exception("KineticEnergySetup: EnergyMeter.prefab missing - the premium meter is its variant.");
            }
            if (!AssetDatabase.CopyAsset(sourcePath, path))
            {
                throw new Exception("KineticEnergySetup: copying EnergyMeter.prefab failed.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform body = root.transform.Find("Body");
                RectTransform bodyRect = body.GetComponent<RectTransform>();

                const float inset = 3f;
                const float blockWidth = 31.4f;
                float oldWidth = bodyRect.sizeDelta.x;
                float newWidth = inset + normalBlocks * blockWidth + inset;
                float zoneWidth = oldWidth - newWidth;
                float bodyHeight = bodyRect.sizeDelta.y;
                float zoneHeight = bodyHeight * 1.3f;

                bodyRect.sizeDelta = new Vector2(newWidth, bodyHeight);
                bodyRect.anchoredPosition -= new Vector2(zoneWidth, 0f);

                Transform dividers = body.Find("MeterDividers");
                if (dividers != null)
                {

                    for (int i = normalBlocks; i <= 9; i++)
                    {
                        Transform retired = dividers.Find("Divider" + i);
                        if (retired != null) retired.gameObject.SetActive(false);
                    }
                }

                Image outlineImage = body.Find("Outline").GetComponent<Image>();
                Image backdropImage = body.Find("Backdrop").GetComponent<Image>();
                Image bonusImage = body.Find("BonusFill").GetComponent<Image>();
                Image chargeImage = body.Find("ChargeFill").GetComponent<Image>();

                GameObject zone = new GameObject("PremiumZone", typeof(RectTransform));
                zone.transform.SetParent(body, false);
                RectTransform zoneRect = zone.GetComponent<RectTransform>();
                zoneRect.anchorMin = new Vector2(1f, 0.5f);
                zoneRect.anchorMax = new Vector2(1f, 0.5f);
                zoneRect.pivot = new Vector2(0f, 0.5f);
                zoneRect.anchoredPosition = Vector2.zero;
                zoneRect.sizeDelta = new Vector2(zoneWidth, zoneHeight);

                MakePremiumImage(zone.transform, "PremiumOutline", outlineImage.color, null, false, Vector2.zero);
                MakePremiumImage(zone.transform, "PremiumBackdrop", backdropImage.color, null, false, new Vector2(-6f, -6f));
                MakePremiumImage(zone.transform, "PremiumBoostFill", bonusImage.color, bonusImage.sprite, true, new Vector2(-6f, -6f));
                MakePremiumImage(zone.transform, "PremiumChargeFill", chargeImage.color, chargeImage.sprite, true, new Vector2(-6f, -6f));

                int zoneBlocks = Mathf.Max(10 - normalBlocks, 1);
                for (int i = 1; i < zoneBlocks; i++)
                {
                    GameObject line = new GameObject("PremiumDivider" + i, typeof(RectTransform));
                    line.transform.SetParent(zone.transform, false);
                    RectTransform lineRect = line.GetComponent<RectTransform>();
                    float anchorX = (float)i / zoneBlocks;
                    lineRect.anchorMin = new Vector2(anchorX, 0f);
                    lineRect.anchorMax = new Vector2(anchorX, 1f);
                    lineRect.pivot = new Vector2(0.5f, 0.5f);
                    lineRect.sizeDelta = new Vector2(3f, -6f);
                    line.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log($"KineticEnergySetup: premium meter prefab created OK ({normalBlocks} + {10 - normalBlocks} taller blocks) - {path}");
        }

        static void MakePremiumImage(Transform parent, string name, Color color, Sprite sprite, bool filled, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = sizeDelta;
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left;
                image.fillAmount = 0f;
            }
        }

        [MenuItem("Tools/Kinetic Energy/Setup Quarry Economy 2 Scene")]
        public static void SetupQuarryEconomy2()
        {
            const string sourcePath = "Assets/Scenes/QuarryEconomy.unity";
            const string scenePath = "Assets/Scenes/QuarryEconomy2.unity";

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sourcePath) == null)
                {
                    throw new Exception("KineticEnergySetup: QuarryEconomy.unity does not exist - set it up first.");
                }
                if (!AssetDatabase.CopyAsset(sourcePath, scenePath))
                {
                    throw new Exception("KineticEnergySetup: copying QuarryEconomy.unity to QuarryEconomy2.unity failed.");
                }
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var oldHarness = UnityEngine.Object.FindAnyObjectByType<EconomyVariantController>(FindObjectsInactive.Include);
            if (oldHarness != null) UnityEngine.Object.DestroyImmediate(oldHarness.gameObject);

            if (UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include) == null)
            {
                new GameObject("MergedEconomy").AddComponent<MergedEconomyController>();
            }

            CreatePremiumEnergyMeterPrefab();
            KineticCubeController playerController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            if (playerController != null && playerController.energyMeter != null
                && playerController.energyMeter.gameObject.name != "PremiumEnergyMeter")
            {
                EnergyMeterController oldMeter = playerController.energyMeter;
                RectTransform oldRect = oldMeter.GetComponent<RectTransform>();

                GameObject premium = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PremiumEnergyMeter.prefab"));
                premium.name = "PremiumEnergyMeter";
                premium.transform.SetParent(oldMeter.transform.parent, false);
                RectTransform newRect = premium.GetComponent<RectTransform>();
                if (oldRect != null && newRect != null)
                {
                    newRect.anchorMin = oldRect.anchorMin;
                    newRect.anchorMax = oldRect.anchorMax;
                    newRect.pivot = oldRect.pivot;
                    newRect.anchoredPosition = oldRect.anchoredPosition;
                    newRect.localScale = oldRect.localScale;
                }

                playerController.energyMeter = premium.GetComponent<EnergyMeterController>();
                EditorUtility.SetDirty(playerController);
                UnityEngine.Object.DestroyImmediate(oldMeter.gameObject);
            }

            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: quarry economy 2 (merged) scene setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Create Combo Meter Prefab")]
        public static void CreateComboMeterPrefab()
        {
            string sourcePath = PrefabFolder + "/SlowdownMeter.prefab";
            string path = PrefabFolder + "/ComboMeter.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Debug.Log("KineticEnergySetup: ComboMeter prefab already exists OK");
                return;
            }
            if (!AssetDatabase.CopyAsset(sourcePath, path))
            {
                throw new Exception("KineticEnergySetup: copying SlowdownMeter.prefab failed.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RectTransform bodyRect = root.transform.Find("Body").GetComponent<RectTransform>();
                bodyRect.sizeDelta = new Vector2(503.6f, bodyRect.sizeDelta.y);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("KineticEnergySetup: ComboMeter prefab created OK (width 503.6, circle aligns with the energy meter's left edge)");
        }

        [MenuItem("Tools/Kinetic Energy/Add Camera Settings Screen To Pause Menu")]
        public static void AddCameraSpeedSlidersToPauseMenu()
        {
            string pausePath = PrefabFolder + "/PauseSystem.prefab";
            GameObject pauseRoot = PrefabUtility.LoadPrefabContents(pausePath);
            try
            {
                Transform pauseCanvas = pauseRoot.transform.Find("PauseCanvas");
                Transform pausePanel = pauseCanvas != null ? pauseCanvas.Find("PausePanel") : null;
                PauseController pauseController = pauseRoot.GetComponentInChildren<PauseController>(true);
                if (pauseCanvas == null || pausePanel == null || pauseController == null)
                {
                    throw new Exception("KineticEnergySetup: PauseSystem.prefab is missing PauseCanvas/PausePanel or PauseController.");
                }

                DestroyDirectChildIfExists(pausePanel, "CameraSpeedSliders");
                DestroyDirectChildIfExists(pausePanel, "CameraSettingsButton");
                DestroyDirectChildIfExists(pauseCanvas, "CameraSettingsPanel");

                Font font = FindBestFont();
                Color accent = new Color(1f, 0.82f, 0.2f);

                GameObject panel = CreatePanel("CameraSettingsPanel", pauseCanvas, new Color(0.05f, 0.06f, 0.08f, 0.96f));

                Text title = CreateText("CameraSettingsTitle", panel.transform, "CAMERA SETTINGS",
                    font, 48, new Vector2(0f, 260f), new Vector2(900f, 70f));
                title.alignment = TextAnchor.MiddleCenter;

                Text subtitle = CreateText("CameraSettingsSubtitle", panel.transform,
                    "Speed multipliers applied to every camera movement, per device.",
                    font, 20, new Vector2(0f, 205f), new Vector2(900f, 32f));
                subtitle.alignment = TextAnchor.MiddleCenter;
                subtitle.color = new Color(1f, 1f, 1f, 0.7f);

                BuildCameraSpeedSlider(panel.transform, "MouseCameraSpeedSlider", "Mouse & keyboard",
                    false, new Vector2(0f, 90f), font);
                BuildCameraSpeedSlider(panel.transform, "GamepadCameraSpeedSlider", "Controller",
                    true, new Vector2(0f, 0f), font);

                Text hint = CreateText("CameraSettingsHint", panel.transform,
                    "Drag with the mouse, or select a bar and push the left stick left/right.\n50% - 150%, in 5% steps. The selected bar is highlighted.",
                    font, 19, new Vector2(0f, -90f), new Vector2(900f, 60f));
                hint.alignment = TextAnchor.MiddleCenter;
                hint.color = new Color(1f, 1f, 1f, 0.6f);

                GameObject backButton = CreateButton("CameraSettingsBackButton", panel.transform, "Back",
                    font, accent, new Vector2(0f, -200f), new Vector2(300f, 70f));
                WireButton(backButton, pauseController.OnCameraSettingsBackClicked);

                panel.SetActive(false);
                pauseController.cameraSettingsPanel = panel;
                pauseController.firstCameraSettingsButton = backButton;

                GameObject openButton = CreateButton("CameraSettingsButton", pausePanel, "Camera Settings",
                    font, accent, new Vector2(0f, -190f), new Vector2(300f, 70f));
                WireButton(openButton, pauseController.OnCameraSettingsClicked);
                LayOutPausePanelButtons(pausePanel);
                EditorUtility.SetDirty(pauseController);

                PrefabUtility.SaveAsPrefabAsset(pauseRoot, pausePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(pauseRoot); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: camera settings screen added to the pause menu OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level1Challenge Cycle")]
        public static void SetupLevel1ChallengeCycle()
        {
            const string scenePath = "Assets/Scenes/Level1Challenge.unity";
            CreateDeathWallPrefab();
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            foreach (WinOnFinish win in UnityEngine.Object.FindObjectsByType<WinOnFinish>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject finishGo = win.gameObject;
                UnityEngine.Object.DestroyImmediate(win);
                if (finishGo.GetComponent<ChallengeFinishTrigger>() == null)
                {
                    finishGo.AddComponent<ChallengeFinishTrigger>();
                }
                EditorUtility.SetDirty(finishGo);
            }

            string[] courseNames =
            {
                "StartPlatform", "Platform1", "Platform2", "Platform3", "Platform4",
                "Platform5", "Platform6", "FloatingWall1", "FloatingWall2", "FloatingWall3",
                "EndPlatform", "UpsidePlatform", "EndPlatform (2)",
            };
            var course = new List<Transform>();
            foreach (string courseName in courseNames)
            {
                GameObject courseGo = GameObject.Find(courseName);
                if (courseGo == null) throw new Exception("KineticEnergySetup: Level1Challenge is missing course object " + courseName);
                course.Add(courseGo.transform);
            }
            course.Sort((a, b) => a.position.x.CompareTo(b.position.x));

            GameObject respawn = GameObject.Find("RespawnPoint");
            if (respawn == null) throw new Exception("KineticEnergySetup: Level1Challenge has no RespawnPoint.");

            GameObject oldChase = GameObject.Find("ChaseWall");
            if (oldChase != null) UnityEngine.Object.DestroyImmediate(oldChase);
            GameObject chaseGo = InstantiatePrefab("DeathWall");
            chaseGo.name = "ChaseWall";
            chaseGo.transform.position = new Vector3(-60f, 13f, -12f);
            chaseGo.transform.localScale = new Vector3(2f, 46f, 140f);
            DeathWall chase = chaseGo.GetComponent<DeathWall>();
            chase.moveSpeed = 4f;

            chase.moveAcceleration = 0.25f;
            chase.maxMoveSpeed = 0f;
            chase.moveDirection = Vector3.right;
            EditorUtility.SetDirty(chase);

            GameObject oldStages = GameObject.Find("ChallengeStages");
            if (oldStages != null) UnityEngine.Object.DestroyImmediate(oldStages);
            GameObject stagesGo = new GameObject("ChallengeStages");
            ChallengeStageController stages = stagesGo.AddComponent<ChallengeStageController>();
            stages.stageSequence = new[]
            {
                ChallengeStage.LimitedSlowdown,
                ChallengeStage.OverchargeScatter,
                ChallengeStage.SealingWalls,
                ChallengeStage.ChasingWall,
                ChallengeStage.ShrinkingPlatforms,
            };
            stages.lockedWinScreen = true;
            stages.chaseWall = chase;
            stages.sealWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/DeathWall.prefab");
            stages.coursePlatforms = course.ToArray();

            stages.sealWallSize = new Vector3(1.5f, 40f, 90f);
            stages.respawnPoint = respawn.transform;
            EditorUtility.SetDirty(stages);

            KineticCubeController playerController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            MergedEconomyController economy = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            GameObject comboMeterGo = GameObject.Find("ComboMeter");
            if (playerController == null || economy == null || comboMeterGo == null)
            {
                throw new Exception("KineticEnergySetup: Level1Challenge is missing the player, MergedEconomy or ComboMeter.");
            }
            EnergyMeterController comboMeterController = comboMeterGo.GetComponentInChildren<EnergyMeterController>(true);
            economy.comboMeter = comboMeterController;
            EditorUtility.SetDirty(economy);

            GameObject oldBudget = GameObject.Find("SlowBudgetMeter");
            if (oldBudget != null) UnityEngine.Object.DestroyImmediate(oldBudget);
            GameObject budgetGo = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ComboMeter.prefab"),
                comboMeterGo.transform.parent);
            budgetGo.name = "SlowBudgetMeter";
            RectTransform comboRect = comboMeterGo.GetComponent<RectTransform>();
            RectTransform budgetRect = budgetGo.GetComponent<RectTransform>();
            budgetRect.anchorMin = comboRect.anchorMin;
            budgetRect.anchorMax = comboRect.anchorMax;
            budgetRect.pivot = comboRect.pivot;

            budgetRect.anchoredPosition = comboRect.anchoredPosition + new Vector2(0f, -45f);
            EnergyMeterController budgetMeter = budgetGo.GetComponentInChildren<EnergyMeterController>(true);
            if (budgetMeter != null && budgetMeter.energyFillImage != null)
            {
                budgetMeter.energyFillImage.color = new Color(0.3f, 0.65f, 1f);
                EditorUtility.SetDirty(budgetMeter.energyFillImage);
            }
            playerController.slowdownMeter = budgetMeter;
            EditorUtility.SetDirty(playerController);

            Material shellMaterial = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));
            foreach (string shellTarget in new[] { "FloatingWall1", "FloatingWall2", "FloatingWall3", "UpsidePlatform" })
            {
                GameObject shellGo = GameObject.Find(shellTarget);
                if (shellGo == null) throw new Exception("KineticEnergySetup: Level1Challenge is missing " + shellTarget);
                AddDamageShell(shellGo.transform, respawn.transform, shellMaterial);
            }

            economy.introText = Level1ChallengeInfoText;
            EditorUtility.SetDirty(economy);

            EnsureSceneInBuildSettings(scenePath);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Level1Challenge five-stage cycle configured OK ("
                + course.Count + " course platforms, damage shells + build info updated)");
        }

        [MenuItem("Tools/Kinetic Energy/Add Variants Screen To Level1Challenge")]
        public static void AddVariantsScreenToLevel1Challenge()
        {
            const string scenePath = "Assets/Scenes/Level1Challenge.unity";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            PauseController pause = UnityEngine.Object.FindAnyObjectByType<PauseController>(FindObjectsInactive.Include);
            if (pause == null) throw new Exception("KineticEnergySetup: Level1Challenge has no PauseController.");
            Transform pauseCanvas = pause.transform.parent != null && pause.transform.parent.Find("PausePanel") != null
                ? pause.transform.parent
                : pause.transform.root.Find("PauseCanvas");
            if (pauseCanvas == null) throw new Exception("KineticEnergySetup: could not locate PauseCanvas in Level1Challenge.");
            Transform pausePanel = pauseCanvas.Find("PausePanel");
            if (pausePanel == null) throw new Exception("KineticEnergySetup: Level1Challenge's PauseCanvas has no PausePanel.");

            DestroyDirectChildIfExists(pausePanel, "VariantsButton");
            DestroyDirectChildIfExists(pauseCanvas, "VariantsPanel");

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);

            GameObject panel = CreatePanel("VariantsPanel", pauseCanvas, new Color(0.05f, 0.06f, 0.08f, 0.96f));
            Text title = CreateText("VariantsTitle", panel.transform, "CHALLENGE VARIANTS",
                font, 48, new Vector2(0f, 300f), new Vector2(900f, 70f));
            title.alignment = TextAnchor.MiddleCenter;
            Text subtitle = CreateText("VariantsSubtitle", panel.transform,
                "Restarts the level on the chosen challenge.", font, 20,
                new Vector2(0f, 248f), new Vector2(900f, 32f));
            subtitle.alignment = TextAnchor.MiddleCenter;
            subtitle.color = new Color(1f, 1f, 1f, 0.7f);

            string[] variantLabels =
            {
                "1 - Limited slowdown", "2 - Overcharge scatter", "3 - Sealing walls",
                "4 - Chasing wall", "5 - Shrinking platforms",
            };
            UnityEngine.Events.UnityAction<string>[] variantCalls =
            {
                pause.LoadChallengeStage1, pause.LoadChallengeStage2, pause.LoadChallengeStage4,
                pause.LoadChallengeStage3, pause.LoadChallengeStage5,
            };
            GameObject firstVariantButton = null;
            float y = 160f;
            for (int i = 0; i < variantLabels.Length; i++)
            {
                GameObject variantButton = CreateButton("Variant_" + (i + 1) + "Button", panel.transform,
                    variantLabels[i], font, accent, new Vector2(0f, y), new Vector2(420f, 70f));
                WireSceneButton(variantButton, variantCalls[i], "Level1Challenge");
                if (i == 0) firstVariantButton = variantButton;
                y -= 90f;
            }

            GameObject backButton = CreateButton("VariantsBackButton", panel.transform, "Back",
                font, accent, new Vector2(0f, y - 30f), new Vector2(300f, 70f));
            WireButton(backButton, pause.OnVariantsBackClicked);

            panel.SetActive(false);
            pause.variantsPanel = panel;
            pause.firstVariantsButton = firstVariantButton;

            GameObject openButton = CreateButton("VariantsButton", pausePanel, "Variants",
                font, accent, new Vector2(0f, 140f), new Vector2(300f, 70f));
            WireButton(openButton, pause.OnVariantsClicked);
            LayOutPausePanelButtons(pausePanel);
            EditorUtility.SetDirty(pause);

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Level1Challenge variants screen added OK (5 variants + back)");
        }

        [MenuItem("Tools/Kinetic Energy/Set Grounded Charge Ramp (Aim Scenes)")]
        public static void SetGroundedChargeRampAimScenes()
        {
            const float ramp = 2.5f;
            foreach (string scenePath in new[] { "Assets/Scenes/Level1Aim1.1.unity", "Assets/Scenes/Level1Challenge.unity" })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                KineticCubeController player = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
                if (player == null) throw new Exception("KineticEnergySetup: no player in " + scenePath);
                player.groundedAimChargeAcceleration = ramp;
                EditorUtility.SetDirty(player);
                EditorSceneManager.SaveOpenScenes();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: grounded charge ramp stamped OK (" + ramp + " in both aim scenes)");
        }

        [MenuItem("Tools/Kinetic Energy/Snap Ground Enemies To Their Platforms")]
        public static void SnapGroundEnemiesToPlatforms()
        {
            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                int moved = 0;
                foreach (Enemy walker in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Transform body = walker.transform;
                    float bodyRadius = body.localScale.x * 0.5f;

                    Vector3 origin = body.position + Vector3.up * 30f;
                    RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 200f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

                    float bestY = float.NegativeInfinity;
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.collider == null) continue;
                        if (hit.collider.GetComponentInParent<Enemy>() != null) continue;
                        if (hit.collider.GetComponentInParent<FlyingEnemy>() != null) continue;
                        if (hit.collider.GetComponentInParent<KineticCubeController>() != null) continue;
                        if (hit.collider.GetComponentInParent<DamageWalls>() != null) continue;
                        if (hit.point.y > body.position.y + 1f) continue;
                        if (hit.point.y > bestY) bestY = hit.point.y;
                    }
                    if (float.IsNegativeInfinity(bestY)) continue;

                    Vector3 settled = new Vector3(body.position.x, bestY + bodyRadius, body.position.z);
                    if ((settled - body.position).sqrMagnitude < 0.0001f) continue;
                    body.position = settled;
                    EditorUtility.SetDirty(body);
                    moved++;
                }

                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: settled " + moved + " ground enemies in " + scenePath);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Use Hunter Enemies In LevelElementsTest2")]
        public static void UseHunterEnemiesInLevelElementsTest2()
        {
            const string scenePath = "Assets/Scenes/LevelElementsTest2.unity";
            CreateHunterEnemyPrefab();
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/HunterEnemy.prefab");
            if (hunterPrefab == null) throw new Exception("KineticEnergySetup: HunterEnemy.prefab is missing.");

            int swapped = 0;
            foreach (Enemy walker in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {

                if (walker.killWindow == EnemyKillWindow.WhileCoolingDown) continue;

                GameObject old = walker.gameObject;
                GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(hunterPrefab, old.transform.parent);
                replacement.name = old.name;
                replacement.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                replacement.transform.localScale = old.transform.localScale;
                UnityEngine.Object.DestroyImmediate(old);
                swapped++;
            }

            if (swapped > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: LevelElementsTest2 now uses hunters OK (" + swapped + " swapped)");
        }

        [MenuItem("Tools/Kinetic Energy/Tune Element Scenes")]
        public static void TuneElementScenes()
        {
            const float laserKnockback = 16.5f;

            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                int beams = 0;
                foreach (LaserHazard beam in UnityEngine.Object.FindObjectsByType<LaserHazard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    beam.knockbackForce = laserKnockback;
                    EditorUtility.SetDirty(beam);
                    beams++;
                }

                MergedEconomyController economy = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
                if (economy != null)
                {

                    economy.regenWhileComboRunning = true;
                    EditorUtility.SetDirty(economy);
                }

                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: tuned " + scenePath + " OK (" + beams + " laser beams at "
                    + laserKnockback + ", regen-during-combo on)");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Restore Turret Section In LevelElementsTest2")]
        public static void RestoreTurretSectionInTest2()
        {
            string[] copyNames = { "TurretRun1", "TurretRun2", "TurretWallLeft", "TurretWallRight" };

            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest.unity", OpenSceneMode.Single);
            var placements = new List<(string name, Vector3 pos, Quaternion rot, Vector3 scale)>();
            foreach (string name in copyNames)
            {
                GameObject go = GameObject.Find(name);
                if (go == null) continue;
                placements.Add((name, go.transform.position, go.transform.rotation, go.transform.localScale));
            }
            var turretPlacements = new List<(string name, Vector3 pos, Quaternion rot)>();
            foreach (TurretEnemy turret in UnityEngine.Object.FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                turretPlacements.Add((turret.name, turret.transform.position, turret.transform.rotation));
            }
            GameObject sourcePad = GameObject.Find("7 - TurretsPad");
            GameObject sourceSpawn = GameObject.Find("7 - TurretsSpawn");
            GameObject sourceCheckpoint = GameObject.Find("7 - TurretsCheckpoint");
            if (sourcePad == null || sourceSpawn == null)
            {
                throw new Exception("KineticEnergySetup: LevelElementsTest has no turret section to copy.");
            }
            Vector3 padPos = sourcePad.transform.position, padScale = sourcePad.transform.localScale;
            Vector3 spawnPos = sourceSpawn.transform.position;
            Vector3 cpPos = sourceCheckpoint != null ? sourceCheckpoint.transform.position : spawnPos;
            Vector3 cpScale = sourceCheckpoint != null ? sourceCheckpoint.transform.localScale : Vector3.one;

            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest2.unity", OpenSceneMode.Single);
            Material platformMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/QuarryPlatformMaterial.mat");
            Material wallMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/ElementsWallMaterial.mat");
            Material damageMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/DamageWallMaterial.mat");
            GameObject course = GameObject.Find("ElementsCourse");
            Transform tf = course != null ? course.transform : null;

            foreach (string stale in new[] { "7 - TurretsPad", "7 - TurretsSpawn", "7 - TurretsCheckpoint" })
            {
                GameObject old = GameObject.Find(stale);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
            }

            GameObject pad = CreateBlock(tf, "7 - TurretsPad", padPos, padScale, platformMat);
            pad.AddComponent<StickySurface>();
            GameObject spawn = new GameObject("7 - TurretsSpawn");
            spawn.transform.SetParent(tf, false);
            spawn.transform.position = spawnPos;

            GameObject checkpoint = InstantiatePrefab("Checkpoint");
            checkpoint.name = "7 - TurretsCheckpoint";
            checkpoint.transform.SetParent(tf, false);
            checkpoint.transform.position = cpPos;
            checkpoint.transform.localScale = cpScale;
            checkpoint.GetComponent<Checkpoint>().respawnPoint = spawn.transform;

            foreach (var placement in placements)
            {
                GameObject old = GameObject.Find(placement.name);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                bool isWall = placement.name.Contains("Wall");
                GameObject block = CreateBlock(tf, placement.name, placement.pos, placement.scale,
                    isWall ? wallMat : platformMat);
                block.transform.rotation = placement.rot;
                if (isWall && damageMat != null) AddDamageShell(block.transform, spawn.transform, damageMat);
            }
            foreach (var placement in turretPlacements)
            {
                GameObject turretGo = InstantiatePrefab("TurretEnemy");
                turretGo.name = placement.name;
                turretGo.transform.SetPositionAndRotation(placement.pos, placement.rot);
            }

            LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
            if (sections != null)
            {
                var list = new List<LevelSectionController.Section>(sections.sections);
                list.Add(new LevelSectionController.Section { label = "7 - Turrets", spawnPoint = spawn.transform });
                sections.sections = list.ToArray();
                RefreshSectionHazards(sections);
                RebuildSectionsScreenButtons(sections);
            }

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: turret section restored in LevelElementsTest2 OK ("
                + turretPlacements.Count + " turrets)");
        }

        [MenuItem("Tools/Kinetic Energy/Setup LevelElementsTest2")]
        public static void SetupLevelElementsTest2()
        {
            const string sourcePath = "Assets/Scenes/LevelElementsTest.unity";
            const string targetPath = "Assets/Scenes/LevelElementsTest2.unity";

            AngleWeakSpotFlyerWeakSpot();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(targetPath) != null) AssetDatabase.DeleteAsset(targetPath);
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                throw new Exception("KineticEnergySetup: could not copy LevelElementsTest to LevelElementsTest2.");
            }
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);

            ReplaceLooseRotatingWalls();

            Material platformMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/QuarryPlatformMaterial.mat");
            Material wallMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/ElementsWallMaterial.mat");
            if (platformMat == null || wallMat == null) throw new Exception("KineticEnergySetup: course materials are missing.");

            GameObject course = GameObject.Find("ElementsCourse");
            Transform tf = course != null ? course.transform : null;
            LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);

            foreach (string arenaName in new[] { "GroundArena1", "GroundArena2" })
            {
                GameObject arena = GameObject.Find(arenaName);
                if (arena == null) continue;
                Vector3 size = arena.transform.localScale;
                arena.transform.localScale = new Vector3(size.x * 1.25f, size.y, size.z * 1.25f);
                EditorUtility.SetDirty(arena);

                float deckY = arena.transform.position.y;
                float halfZ = arena.transform.localScale.z * 0.5f;
                SpawnTiltedLedge(tf, arenaName + "LedgeA", arena.transform.position + new Vector3(-4f, deckY + 9f, -halfZ - 7f), new Vector3(0f, 0f, 22f), wallMat);
                SpawnTiltedLedge(tf, arenaName + "LedgeB", arena.transform.position + new Vector3(3f, deckY + 6f, halfZ + 7f), new Vector3(0f, 0f, -18f), wallMat);
                SpawnTiltedLedge(tf, arenaName + "LedgeC", arena.transform.position + new Vector3(-6f, deckY + 14f, halfZ + 12f), new Vector3(12f, 0f, -26f), wallMat);
            }
            ReplaceEnemies("StalkerEnemy", isFlyer: false);

            ReplaceEnemies("WeakSpotFlyer", isFlyer: true);
            GameObject flyStep = GameObject.Find("FlyStep1");
            if (flyStep != null)
            {
                Vector3 centre = flyStep.transform.position;
                SpawnWeakSpotFlyer("ElementsFlyer3", centre + new Vector3(18f, 9f, 12f), 10f, 25f);
                SpawnTiltedLedge(tf, "FlySideWallA", centre + new Vector3(6f, 5f, -18f), new Vector3(0f, 0f, 20f), wallMat);
                SpawnTiltedLedge(tf, "FlySideWallB", centre + new Vector3(20f, 9f, 18f), new Vector3(0f, 0f, -24f), wallMat);
                SpawnTiltedLedge(tf, "FlySideWallC", centre + new Vector3(30f, 3f, -14f), new Vector3(10f, 0f, 16f), wallMat);
            }

            foreach (string turretObject in new[]
            {
                "ElementsTurret1", "ElementsTurret2", "TurretWallLeft", "TurretWallRight",
                "TurretRun1", "TurretRun2", "7 - TurretsPad", "7 - TurretsSpawn", "7 - TurretsCheckpoint",
            })
            {
                GameObject stale = GameObject.Find(turretObject);
                if (stale != null) UnityEngine.Object.DestroyImmediate(stale);
            }
            foreach (TurretEnemy stale in UnityEngine.Object.FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }

            if (sections != null)
            {

                var kept = new List<LevelSectionController.Section>();
                foreach (LevelSectionController.Section section in sections.sections)
                {
                    if (section != null && section.spawnPoint != null) kept.Add(section);
                }
                sections.sections = kept.ToArray();
                RefreshSectionHazards(sections);
                RebuildSectionsScreenButtons(sections);
            }

            EnsureSceneInBuildSettings(targetPath);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: LevelElementsTest2 built OK ("
                + (sections != null ? sections.sections.Length : 0) + " sections, turrets removed)");
        }

        static void SpawnTiltedLedge(Transform parent, string name, Vector3 position, Vector3 eulerTilt, Material material)
        {
            GameObject ledge = CreateBlock(parent, name, position, new Vector3(12f, 1.5f, 9f), material);
            ledge.transform.rotation = Quaternion.Euler(eulerTilt);
            EditorUtility.SetDirty(ledge);
        }

        static void ReplaceEnemies(string prefabName, bool isFlyer)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + prefabName + ".prefab");
            if (prefab == null) throw new Exception("KineticEnergySetup: missing prefab " + prefabName);

            var targets = new List<GameObject>();
            if (isFlyer)
            {
                foreach (FlyingEnemy flyer in UnityEngine.Object.FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (flyer is WeakSpotFlyingEnemy) continue;
                    targets.Add(flyer.gameObject);
                }
            }
            else
            {
                foreach (Enemy walker in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (walker.GetType() != typeof(Enemy)) continue;
                    targets.Add(walker.gameObject);
                }
            }

            foreach (GameObject old in targets)
            {
                GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.transform.parent);
                replacement.name = old.name;
                replacement.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                UnityEngine.Object.DestroyImmediate(old);
            }
        }

        static void RebuildSectionsScreenButtons(LevelSectionController sections)
        {
            PauseController pause = UnityEngine.Object.FindAnyObjectByType<PauseController>(FindObjectsInactive.Include);
            if (pause == null || pause.sectionsPanel == null) return;

            var list = new List<LevelSectionController.Section>(sections.sections);
            Transform panel = pause.sectionsPanel.transform;
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                if (panel.GetChild(i).name.StartsWith("Section_")) UnityEngine.Object.DestroyImmediate(panel.GetChild(i).gameObject);
            }

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);
            GameObject firstButton = null;
            float y = 200f;
            for (int i = 0; i < list.Count; i++)
            {
                GameObject sectionButton = CreateButton("Section_" + (i + 1) + "Button", panel,
                    list[i].label, font, accent, new Vector2(0f, y), new Vector2(460f, 62f));
                WireSceneButton(sectionButton, sections.GoToSection, i.ToString());
                WireButton(sectionButton, pause.ResumeAfterSectionJump);
                if (i == 0) firstButton = sectionButton;
                y -= 76f;
            }
            pause.firstSectionsButton = firstButton;
            EditorUtility.SetDirty(pause);
        }

        [MenuItem("Tools/Kinetic Energy/Angle WeakSpotFlyer Weak Spot")]
        public static void AngleWeakSpotFlyerWeakSpot()
        {
            const float tiltDegrees = 42f;
            const float spotDistance = 0.55f;

            string path = PrefabFolder + "/WeakSpotFlyer.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform spot = root.transform.Find("WeakSpot");
                if (spot == null) throw new Exception("KineticEnergySetup: WeakSpotFlyer has no WeakSpot child.");

                Quaternion lean = Quaternion.Euler(-tiltDegrees, 0f, 0f);
                spot.localRotation = lean;
                spot.localPosition = lean * Vector3.up * spotDistance;
                EditorUtility.SetDirty(spot);

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: weak spot angled " + tiltDegrees + " degrees toward the back OK");
        }

        [MenuItem("Tools/Kinetic Energy/Replace Loose Rotating Walls With Prefab")]
        public static void ReplaceLooseRotatingWallsWithPrefab()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest.unity", OpenSceneMode.Single);
            int replaced = ReplaceLooseRotatingWalls();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: loose rotating walls replaced with the prefab OK (" + replaced + ")");
        }

        static int ReplaceLooseRotatingWalls()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/SpinWall1.prefab");
            if (prefab == null) throw new Exception("KineticEnergySetup: SpinWall1.prefab is missing.");
            Material damageMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/DamageWallMaterial.mat");
            LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
            Transform fallbackSpawn = sections != null && sections.sections.Length > 0 ? sections.sections[0].spawnPoint : null;

            int replaced = 0;
            foreach (RotatingWall loose in UnityEngine.Object.FindObjectsByType<RotatingWall>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(loose.gameObject)) continue;

                Transform old = loose.transform;
                GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.parent);
                replacement.name = old.name;
                replacement.transform.SetPositionAndRotation(old.position, old.rotation);
                replacement.transform.localScale = old.localScale;

                RotatingWall spin = replacement.GetComponent<RotatingWall>();
                spin.degreesPerSecond = loose.degreesPerSecond;
                spin.spinAxis = loose.spinAxis;
                spin.startAngleOffset = loose.startAngleOffset;
                EditorUtility.SetDirty(spin);

                if (damageMat != null) AddEdgeDamageShell(replacement.transform, fallbackSpawn, damageMat);
                UnityEngine.Object.DestroyImmediate(loose.gameObject);
                replaced++;
            }

            if (replaced > 0 && sections != null) RefreshSectionHazards(sections);
            return replaced;
        }

        static void RefreshSectionHazards(LevelSectionController sections)
        {
            var hazards = new List<DamageWalls>();
            foreach (DamageWalls hazard in UnityEngine.Object.FindObjectsByType<DamageWalls>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                hazards.Add(hazard);
            }
            sections.hazards = hazards.ToArray();
            EditorUtility.SetDirty(sections);
        }

        [MenuItem("Tools/Kinetic Energy/Add Damage Edges To LevelElementsTest")]
        public static void AddElementsDamageEdges()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest.unity", OpenSceneMode.Single);

            Material damageMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/DamageWallMaterial.mat");
            if (damageMat == null) throw new Exception("KineticEnergySetup: DamageWallMaterial is missing.");

            LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
            Transform fallbackSpawn = sections != null && sections.sections.Length > 0 ? sections.sections[0].spawnPoint : null;

            int shelled = 0;

            foreach (RotatingWall wall in UnityEngine.Object.FindObjectsByType<RotatingWall>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                AddEdgeDamageShell(wall.transform, fallbackSpawn, damageMat);
                shelled++;
            }

            foreach (string wallName in new[] { "TurretWallLeft", "TurretWallRight" })
            {
                GameObject wallGo = GameObject.Find(wallName);
                if (wallGo == null) continue;
                AddDamageShell(wallGo.transform, fallbackSpawn, damageMat);
                shelled++;
            }

            if (sections != null)
            {
                var hazards = new List<DamageWalls>(sections.hazards);
                foreach (DamageWalls shell in UnityEngine.Object.FindObjectsByType<DamageWalls>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!shell.name.StartsWith("DamageShell")) continue;
                    if (!hazards.Contains(shell)) hazards.Add(shell);
                }
                sections.hazards = hazards.ToArray();
                EditorUtility.SetDirty(sections);
            }

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: damage edges added OK (" + shelled + " walls shelled, nothing else touched)");
        }

        [MenuItem("Tools/Kinetic Energy/Validate LevelElementsTest Pause Menu")]
        public static void ValidateLevelElementsPauseMenu()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest.unity", OpenSceneMode.Single);

            PauseController[] controllers = UnityEngine.Object.FindObjectsByType<PauseController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log("PAUSECHECK controllers=" + controllers.Length);
            foreach (PauseController pause in controllers)
            {
                Debug.Log("PAUSECHECK root=" + pause.transform.root.name
                    + " activeInHierarchy=" + pause.gameObject.activeInHierarchy
                    + " componentEnabled=" + pause.enabled
                    + " pausePanel=" + (pause.pausePanel != null ? pause.pausePanel.name : "NULL")
                    + " pausePanelActiveSelf=" + (pause.pausePanel != null && pause.pausePanel.activeSelf)
                    + " firstPauseButton=" + (pause.firstPauseButton != null ? pause.firstPauseButton.name : "NULL")
                    + " pauseAction=" + (pause.pauseAction != null ? pause.pauseAction.name : "NULL")
                    + " sectionsPanel=" + (pause.sectionsPanel != null ? pause.sectionsPanel.name : "NULL"));

                foreach (Canvas canvas in pause.transform.root.GetComponentsInChildren<Canvas>(true))
                {
                    Debug.Log("PAUSECHECK canvas=" + canvas.name
                        + " enabled=" + canvas.enabled
                        + " activeInHierarchy=" + canvas.gameObject.activeInHierarchy
                        + " renderMode=" + canvas.renderMode
                        + " sortingOrder=" + canvas.sortingOrder
                        + " children=" + canvas.transform.childCount);
                }
                foreach (Transform child in pause.transform.root)
                {
                    Debug.Log("PAUSECHECK rootChild=" + child.name + " active=" + child.gameObject.activeSelf);
                }
            }

            var eventSystems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log("PAUSECHECK eventSystems=" + eventSystems.Length
                + (eventSystems.Length > 0 ? " firstActive=" + eventSystems[0].gameObject.activeInHierarchy : ""));

            GameObject pauseSystem = GameObject.Find("PauseSystem");
            Debug.Log("PAUSECHECK pauseSystemObject=" + (pauseSystem != null ? "FOUND active=" + pauseSystem.activeInHierarchy : "MISSING"));

            LevelSectionController sectionController = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
            PauseController owner = controllers.Length > 0 ? controllers[0] : null;
            if (sectionController == null || owner == null || owner.sectionsPanel == null) return;

            foreach (Button button in owner.sectionsPanel.GetComponentsInChildren<Button>(true))
            {
                Text label = button.GetComponentInChildren<Text>(true);
                var so = new SerializedObject(button);
                SerializedProperty calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                string wiring = "";
                for (int c = 0; c < calls.arraySize; c++)
                {
                    SerializedProperty call = calls.GetArrayElementAtIndex(c);
                    string method = call.FindPropertyRelative("m_MethodName").stringValue;
                    string arg = call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                    wiring += method + "(" + arg + ") ";
                    if (method != "GoToSection" || !int.TryParse(arg, out int index)) continue;
                    string resolved = index >= 0 && index < sectionController.sections.Length
                        ? sectionController.sections[index].label + " @x="
                            + (sectionController.sections[index].spawnPoint != null
                                ? sectionController.sections[index].spawnPoint.position.x.ToString("F0") : "?")
                        : "OUT OF RANGE";
                    wiring += "-> " + resolved + " ";
                }
                Debug.Log("SECTIONCHECK button=" + button.name
                    + " label='" + (label != null ? label.text : "?") + "' " + wiring);
            }
        }

        [MenuItem("Tools/Kinetic Energy/Setup LevelElementsTest")]
        public static void SetupLevelElementsTest()
        {
            const string scenePath = "Assets/Scenes/LevelElementsTest.unity";
            CreateLaserGatePrefab();
            CreateCheckpointPrefab();
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            KineticCubeController player = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            MergedEconomyController economy = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            PauseController pause = UnityEngine.Object.FindAnyObjectByType<PauseController>(FindObjectsInactive.Include);
            if (player == null || economy == null || pause == null)
            {
                throw new Exception("KineticEnergySetup: LevelElementsTest is missing the player, MergedEconomy or PauseController.");
            }

            foreach (ChallengeStageController stale in UnityEngine.Object.FindObjectsByType<ChallengeStageController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
            foreach (DeathWall stale in UnityEngine.Object.FindObjectsByType<DeathWall>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
            foreach (ChallengeFinishTrigger stale in UnityEngine.Object.FindObjectsByType<ChallengeFinishTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale);
            }
            foreach (string staleCourse in new[] { "Level1Course", "ElementsCourse" })
            {
                GameObject courseGo = GameObject.Find(staleCourse);
                if (courseGo != null) UnityEngine.Object.DestroyImmediate(courseGo);
            }
            foreach (string loose in new[] { "DamageFloor", "RespawnPoint", "ChaseWall", "FinishTrigger", "FinishTrigger (1)" })
            {
                GameObject looseGo = GameObject.Find(loose);
                if (looseGo != null) UnityEngine.Object.DestroyImmediate(looseGo);
            }
            foreach (Enemy stale in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
            foreach (FlyingEnemy stale in UnityEngine.Object.FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
            foreach (TurretEnemy stale in UnityEngine.Object.FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }

            BuildElementsCourse(player, economy, pause);

            EnsureSceneInBuildSettings(scenePath);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: LevelElementsTest built OK (7 sections)");
        }

        static void BuildElementsCourse(KineticCubeController player, MergedEconomyController economy, PauseController pause)
        {
            MeasureLaunchDistances(out float L, out float H);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material wallMat = MakeMaterial("ElementsWallMaterial", new Color(0.42f, 0.45f, 0.55f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("ElementsCourse");
            Transform tf = course.transform;
            Vector3 pad = new Vector3(12f, 2f, 12f);
            var sections = new List<LevelSectionController.Section>();
            var hazards = new List<DamageWalls>();

            Transform OpenSection(string label, Vector3 padCentre)
            {
                GameObject sectionPad = CreateBlock(tf, label + "Pad", padCentre, pad, platformMat);
                sectionPad.AddComponent<StickySurface>();
                GameObject spawn = new GameObject(label + "Spawn");
                spawn.transform.SetParent(tf, false);
                spawn.transform.position = padCentre + new Vector3(0f, pad.y * 0.5f + 1.5f, 0f);
                sections.Add(new LevelSectionController.Section { label = label, spawnPoint = spawn.transform });

                GameObject checkpoint = InstantiatePrefab("Checkpoint");
                checkpoint.name = label + "Checkpoint";
                checkpoint.transform.SetParent(tf, false);

                checkpoint.transform.position = padCentre + new Vector3(0f, pad.y * 0.5f + 0.75f, 0f);
                checkpoint.transform.localScale = new Vector3(pad.x * 0.9f, 0.5f, pad.z * 0.9f);
                checkpoint.GetComponent<Checkpoint>().respawnPoint = spawn.transform;
                EditorUtility.SetDirty(checkpoint);

                return spawn.transform;
            }

            float gap = 0.42f * L;

            float lead = gap * 0.5f;
            float x = 0f;
            float e1, e2;

            OpenSection("1 - Basics", new Vector3(x, -1f, 0f));
            Vector3 playerSpawn = new Vector3(x, 1.5f, 0f);
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "BasicsHop1", new Vector3(e1, 1f, 10f), pad, platformMat);
            CreateBlock(tf, "BasicsHop2", new Vector3(e2, 4f, -8f), pad, platformMat);

            x = e2 + gap;
            OpenSection("2 - Moving platforms", new Vector3(x, 4f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            SpawnMovingPlatform(tf, "MoverSide", new Vector3(e1, 5f, -14f), new Vector3(0f, 0f, 28f), 7f);
            SpawnMovingPlatform(tf, "MoverLift", new Vector3(e2, 2f, 6f), new Vector3(0f, 14f, 0f), 6f);

            x = e2 + gap;
            Transform spinSpawn = OpenSection("3 - Rotating walls", new Vector3(x, 8f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            GameObject spinWall1 = SpawnRotatingWall(tf, "SpinWall1", new Vector3(e1, 10f, -10f), 28f, 0f, wallMat);
            GameObject spinWall2 = SpawnRotatingWall(tf, "SpinWall2", new Vector3(e2, 12f, 8f), -36f, 90f, wallMat);

            AddEdgeDamageShell(spinWall1.transform, spinSpawn, damageMat);
            AddEdgeDamageShell(spinWall2.transform, spinSpawn, damageMat);

            x = e2 + gap;
            Transform laserSpawn = OpenSection("4 - Lasers", new Vector3(x, 8f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "LaserRun1", new Vector3(e1, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            CreateBlock(tf, "LaserRun2", new Vector3(e2, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            CreateLaserGate(tf, "ElementsGate1", new Vector3(x + lead * 0.5f, 9f, 0f), 24f, 12f, 1.5f, 1.5f, 0f, laserSpawn);
            CreateLaserGate(tf, "ElementsGate2", new Vector3((e1 + e2) * 0.5f, 9f, 0f), 24f, 12f, 1.2f, 1.4f, 0.7f, laserSpawn);

            x = e2 + gap;
            OpenSection("5 - Ground enemies", new Vector3(x, 4f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "GroundArena1", new Vector3(e1, 2f, 12f), new Vector3(26f, 2f, 26f), platformMat);
            SpawnEnemy("ElementsEnemy1", new Vector3(e1, 4f, 12f), EnemyWanderMode.PlatformSurface, 10f, 2f);
            CreateBlock(tf, "GroundArena2", new Vector3(e2, 2f, -12f), new Vector3(26f, 2f, 26f), platformMat);
            SpawnEnemy("ElementsEnemy2", new Vector3(e2 - 5f, 4f, -12f), EnemyWanderMode.PlatformSurface, 10f, 2f);
            SpawnEnemy("ElementsEnemy3", new Vector3(e2 + 5f, 4f, -12f), EnemyWanderMode.WithinRadius, 8f, 2f);

            x = e2 + gap;
            OpenSection("6 - Flying enemies", new Vector3(x, 6f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "FlyStep1", new Vector3(e1, 9f, -10f), pad, platformMat);
            SpawnFlyingEnemy("ElementsFlyer1", new Vector3(x + lead * 0.6f, 16f, -4f), 9f, 24f);
            CreateBlock(tf, "FlyStep2", new Vector3(e2, 12f, 10f), pad, platformMat);
            SpawnFlyingEnemy("ElementsFlyer2", new Vector3(e1 + gap * 0.6f, 20f, 6f), 11f, 26f);

            x = e2 + gap;
            Transform turretSpawn = OpenSection("7 - Turrets", new Vector3(x, 10f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "TurretRun1", new Vector3(e1, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            GameObject turretWallLeft = CreateBlock(tf, "TurretWallLeft", new Vector3(e1, 12f, -16f), new Vector3(20f, 16f, 2f), wallMat);
            SpawnTurret("ElementsTurret1", new Vector3(e1, 13f, -14.8f), new Vector3(-90f, 0f, 0f));
            CreateBlock(tf, "TurretRun2", new Vector3(e2, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            GameObject turretWallRight = CreateBlock(tf, "TurretWallRight", new Vector3(e2, 12f, 16f), new Vector3(20f, 16f, 2f), wallMat);
            SpawnTurret("ElementsTurret2", new Vector3(e2, 13f, 14.8f), new Vector3(90f, 0f, 0f));

            AddDamageShell(turretWallLeft.transform, turretSpawn, damageMat);
            AddDamageShell(turretWallRight.transform, turretSpawn, damageMat);

            x = e2 + gap;
            CreateBlock(tf, "FinishPad", new Vector3(x, -1f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            float courseLength = x;

            GameObject finish = new GameObject("ElementsFinish");
            finish.transform.SetParent(tf, false);
            finish.transform.position = new Vector3(x, 2f, 0f);
            BoxCollider finishBox = finish.AddComponent<BoxCollider>();
            finishBox.isTrigger = true;
            finishBox.size = new Vector3(6f, 6f, 12f);
            finish.AddComponent<WinOnFinish>();

            foreach (LaserWall gate in course.GetComponentsInChildren<LaserWall>(true))
            {
                foreach (DamageWalls beamDamage in gate.GetComponentsInChildren<DamageWalls>(true))
                {
                    GameObject beamsGo = beamDamage.gameObject;
                    UnityEngine.Object.DestroyImmediate(beamDamage);
                    if (beamsGo.GetComponent<LaserHazard>() == null) beamsGo.AddComponent<LaserHazard>();
                    EditorUtility.SetDirty(beamsGo);
                }
            }

            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(courseLength * 0.5f, -26f, 0f), new Vector3(courseLength + 120f, 2f, 220f), damageMat);
            DamageWalls floorDamage = damageFloor.AddComponent<DamageWalls>();
            hazards.Add(floorDamage);

            foreach (DamageWalls courseHazard in course.GetComponentsInChildren<DamageWalls>(true))
            {
                hazards.Add(courseHazard);
            }

            GameObject sectionsGo = GameObject.Find("LevelSections");
            if (sectionsGo != null) UnityEngine.Object.DestroyImmediate(sectionsGo);
            sectionsGo = new GameObject("LevelSections");
            LevelSectionController sectionController = sectionsGo.AddComponent<LevelSectionController>();
            sectionController.sections = sections.ToArray();
            sectionController.hazards = hazards.ToArray();
            EditorUtility.SetDirty(sectionController);

            foreach (DamageWalls hazard in hazards)
            {
                hazard.respawnPoint = sections[0].spawnPoint;
                EditorUtility.SetDirty(hazard);
            }

            player.transform.position = playerSpawn;
            EditorUtility.SetDirty(player);

            economy.dropPlayerWhenWindowExpires = true;

            economy.safetyTriggerFraction = economy.safetyCeilingFraction;
            economy.dualSafetyTriggerFraction = economy.dualSafetyCeilingFraction;
            economy.totalLossSafetyTriggerFraction = economy.totalLossSafetyCeilingFraction;
            EditorUtility.SetDirty(economy);

            BuildSectionsScreen(pause, sectionController, sections);
        }

        static void BuildSectionsScreen(PauseController pause, LevelSectionController sectionController, List<LevelSectionController.Section> sections)
        {
            Transform pauseCanvas = pause.transform.parent != null && pause.transform.parent.Find("PausePanel") != null
                ? pause.transform.parent
                : pause.transform.root.Find("PauseCanvas");
            if (pauseCanvas == null) throw new Exception("KineticEnergySetup: could not locate PauseCanvas in LevelElementsTest.");
            Transform pausePanel = pauseCanvas.Find("PausePanel");
            if (pausePanel == null) throw new Exception("KineticEnergySetup: LevelElementsTest's PauseCanvas has no PausePanel.");

            DestroyDirectChildIfExists(pausePanel, "VariantsButton");
            DestroyDirectChildIfExists(pauseCanvas, "VariantsPanel");
            DestroyDirectChildIfExists(pausePanel, "SectionsButton");
            DestroyDirectChildIfExists(pauseCanvas, "SectionsPanel");
            pause.variantsPanel = null;
            pause.firstVariantsButton = null;

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);

            GameObject panel = CreatePanel("SectionsPanel", pauseCanvas, new Color(0.05f, 0.06f, 0.08f, 0.96f));
            Text title = CreateText("SectionsTitle", panel.transform, "SECTIONS", font, 48,
                new Vector2(0f, 330f), new Vector2(900f, 70f));
            title.alignment = TextAnchor.MiddleCenter;
            Text subtitle = CreateText("SectionsSubtitle", panel.transform,
                "Jump to an element and respawn there while you test it.", font, 20,
                new Vector2(0f, 278f), new Vector2(900f, 32f));
            subtitle.alignment = TextAnchor.MiddleCenter;
            subtitle.color = new Color(1f, 1f, 1f, 0.7f);

            GameObject firstButton = null;
            float y = 200f;
            for (int i = 0; i < sections.Count; i++)
            {
                GameObject sectionButton = CreateButton("Section_" + (i + 1) + "Button", panel.transform,
                    sections[i].label, font, accent, new Vector2(0f, y), new Vector2(460f, 62f));

                WireSceneButton(sectionButton, sectionController.GoToSection, i.ToString());
                WireButton(sectionButton, pause.ResumeAfterSectionJump);
                if (i == 0) firstButton = sectionButton;
                y -= 76f;
            }

            GameObject backButton = CreateButton("SectionsBackButton", panel.transform, "Back",
                font, accent, new Vector2(0f, y - 20f), new Vector2(300f, 62f));
            WireButton(backButton, pause.OnSectionsBackClicked);

            panel.SetActive(false);
            pause.sectionsPanel = panel;
            pause.firstSectionsButton = firstButton;

            GameObject openButton = CreateButton("SectionsButton", pausePanel, "Sections",
                font, accent, new Vector2(0f, 140f), new Vector2(300f, 70f));
            WireButton(openButton, pause.OnSectionsClicked);
            LayOutPausePanelButtons(pausePanel);
            EditorUtility.SetDirty(pause);
        }

        static void SpawnMovingPlatform(Transform parent, string name, Vector3 position, Vector3 moveOffset, float lapSeconds)
        {
            GameObject instance = InstantiatePrefab("MovingPlatform");
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            MovingPlatform mover = instance.GetComponent<MovingPlatform>();
            mover.moveOffset = moveOffset;
            mover.lapSeconds = lapSeconds;
            EditorUtility.SetDirty(mover);
        }

        static GameObject SpawnRotatingWall(Transform parent, string name, Vector3 position, float degreesPerSecond, float startAngle, Material material)
        {
            GameObject wall = CreateBlock(parent, name, position, new Vector3(14f, 12f, 2f), material);
            wall.AddComponent<StickySurface>();
            RotatingWall spin = wall.AddComponent<RotatingWall>();
            spin.degreesPerSecond = degreesPerSecond;
            spin.startAngleOffset = startAngle;
            spin.spinAxis = Vector3.up;
            EditorUtility.SetDirty(spin);
            return wall;
        }

        const string LevelElementsInfoText =
            "LEVEL ELEMENTS TEST\n\n" +
            "A straight run along one axis, introducing one element at a time. Pause > Sections jumps " +
            "straight to any of them - you respawn there while you keep testing it.\n\n" +
            "1 - BASICS: plain platforms.\n" +
            "2 - MOVING PLATFORMS: one slides sideways, one rides up and down. While you aim, a blue " +
            "arrow shows where the platform will BE when your shot lands - aim at the tip.\n" +
            "3 - ROTATING WALLS: sticky faces that keep turning. Time the shot as well as aiming it.\n" +
            "4 - LASERS: gates that blink on and off. Cross while they are dark.\n" +
            "5 - GROUND ENEMIES: they wander their platform and leap at you when you land nearby.\n" +
            "6 - FLYING ENEMIES: they drift over the gaps and shoot on sight.\n" +
            "7 - TURRETS: fixed wall guns that flash before firing.\n\n" +
            "FALLING: if the combo window runs out while you are in the air, the aim is cut and you DROP. " +
            "Keep landing before the meter empties.\n\n" +
            "Press any button to start.";

        [MenuItem("Tools/Kinetic Energy/Swap Sealing And Chasing Order (Level1Challenge)")]
        public static void SwapSealingChasingOrder()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Level1Challenge.unity", OpenSceneMode.Single);
            ChallengeStageController stages = UnityEngine.Object.FindAnyObjectByType<ChallengeStageController>(FindObjectsInactive.Include);
            MergedEconomyController economy = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            if (stages == null || economy == null) throw new Exception("KineticEnergySetup: Level1Challenge is missing ChallengeStages or MergedEconomy.");

            int chasing = System.Array.IndexOf(stages.stageSequence, ChallengeStage.ChasingWall);
            int sealing = System.Array.IndexOf(stages.stageSequence, ChallengeStage.SealingWalls);
            if (chasing < 0 || sealing < 0) throw new Exception("KineticEnergySetup: the scene's stage sequence is missing a stage to swap.");
            if (chasing < sealing)
            {
                (stages.stageSequence[chasing], stages.stageSequence[sealing])
                    = (stages.stageSequence[sealing], stages.stageSequence[chasing]);
            }
            EditorUtility.SetDirty(stages);

            economy.introText = Level1ChallengeInfoText;
            EditorUtility.SetDirty(economy);
            EditorSceneManager.SaveOpenScenes();

            AddVariantsScreenToLevel1Challenge();
            Debug.Log("KineticEnergySetup: sealing/chasing order swapped OK");
        }

        [MenuItem("Tools/Kinetic Energy/Create Checkpoint Prefab")]
        public static void CreateCheckpointPrefab()
        {
            string path = PrefabFolder + "/Checkpoint.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                GameObject seed = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seed.name = "Checkpoint";
                seed.AddComponent<Checkpoint>();
                PrefabUtility.SaveAsPrefabAsset(seed, path);
                UnityEngine.Object.DestroyImmediate(seed);
            }

            Material frameMat = MakeMaterial("CheckpointFrameMaterial", new Color(0.30f, 0.32f, 0.38f));
            Material buttonMat = MakeMaterial("CheckpointButtonMaterial", new Color(0.25f, 0.55f, 1f));

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {

                Collider rootCollider = root.GetComponent<Collider>();
                if (rootCollider != null) rootCollider.isTrigger = false;
                Renderer rootRenderer = root.GetComponent<Renderer>();
                if (rootRenderer != null) rootRenderer.sharedMaterial = frameMat;

                Rigidbody body = root.GetComponent<Rigidbody>();
                if (body == null) body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;

                Transform existingButton = root.transform.Find("Button");
                if (existingButton != null) UnityEngine.Object.DestroyImmediate(existingButton.gameObject);

                GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
                button.name = "Button";
                button.transform.SetParent(root.transform, false);
                button.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                button.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
                button.GetComponent<Renderer>().sharedMaterial = buttonMat;

                Checkpoint checkpoint = root.GetComponent<Checkpoint>();
                if (checkpoint == null) checkpoint = root.AddComponent<Checkpoint>();
                checkpoint.buttonVisual = button.transform;
                checkpoint.buttonRenderer = button.GetComponent<Renderer>();
                checkpoint.buttonCollider = button.GetComponent<Collider>();

                checkpoint.pressDepth = 0.45f;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Checkpoint button prefab updated in place OK");
        }

        [MenuItem("Tools/Kinetic Energy/Clear Requirement Tick Marks")]
        public static void ClearRequirementTickMarks()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest3.unity", OpenSceneMode.Single);
            int cleared = 0;
            foreach (EnergyRequirement requirement in UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!requirement.buildTickMarks) continue;
                requirement.buildTickMarks = false;
                EditorUtility.SetDirty(requirement);
                cleared++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("KineticEnergySetup: requirement tick marks cleared OK (" + cleared + " objects)");
        }

        [MenuItem("Tools/Kinetic Energy/Validate Enemy Tier Colours")]
        public static void ValidateEnemyTierColours()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest3.unity", OpenSceneMode.Single);

            foreach (EnergyRequirement requirement in UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Enemy enemy = requirement.GetComponent<Enemy>();
                if (enemy == null) continue;

                EnergyBandPalette.Band tier = requirement.palette != null
                    ? requirement.palette.GetBand(requirement.requirementPercent / 100f) : null;

                Renderer bodyRenderer = enemy.GetComponentInChildren<Renderer>();

                Debug.Log("ENEMYCOLOUR " + requirement.name
                    + " requires=" + requirement.requirementPercent
                    + " reqEnabled=" + requirement.enabled
                    + " reqGOActive=" + requirement.gameObject.activeSelf
                    + " bandHex=" + (tier != null ? "#" + ColorUtility.ToHtmlStringRGB(tier.baseColor) : "NULL")
                    + " emission=" + (tier != null ? tier.emissionIntensity.ToString() : "-")
                    + " serializedVulnerable=#" + ColorUtility.ToHtmlStringRGB(enemy.vulnerableColor)
                    + " killWindow=" + enemy.killWindow
                    + " targetRenderer=" + (requirement.targetRenderer != null ? requirement.targetRenderer.name : "NULL")
                    + " bodyRenderer=" + (bodyRenderer != null ? bodyRenderer.name : "NULL")
                    + " sameRenderer=" + (requirement.targetRenderer == bodyRenderer));
            }
        }

        [MenuItem("Tools/Kinetic Energy/Setup SecondLevel")]
        public static void SetupSecondLevel()
        {
            const string sourcePath = "Assets/Scenes/LevelElementsTest3.unity";
            const string targetPath = "Assets/Scenes/SecondLevel.unity";

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(targetPath) != null) AssetDatabase.DeleteAsset(targetPath);
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                throw new Exception("KineticEnergySetup: could not copy LevelElementsTest3 to SecondLevel.");
            }
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);

            EnergyBandPalette palette = EnsureBandPalette();
            Material pipMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/EnergyPipMaterial.mat");
            Material platformMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/QuarryPlatformMaterial.mat");
            Material wallMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/ElementsWallMaterial.mat");
            Material damageMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/DamageWallMaterial.mat");
            if (platformMat == null || wallMat == null || damageMat == null)
            {
                throw new Exception("KineticEnergySetup: course materials missing - build an element scene first.");
            }

            foreach (string stale in new[] { "ElementsCourse", "LevelSections", "DamageFloor" })
            {
                GameObject go = GameObject.Find(stale);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            DestroyAllOfType<Enemy>();
            DestroyAllOfType<FlyingEnemy>();
            DestroyAllOfType<TurretEnemy>();
            DestroyAllOfType<Checkpoint>();
            DestroyAllOfType<WinOnFinish>();
            DestroyAllOfType<MovingPlatform>();

            KineticCubeController player = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            if (player == null) throw new Exception("KineticEnergySetup: SecondLevel has no player.");

            MeasureLaunchDistances(out float L, out float H);

            GameObject course = new GameObject("SecondLevelCourse");
            Transform tf = course.transform;
            Vector3 pad = new Vector3(12f, 2f, 12f);
            var sections = new List<LevelSectionController.Section>();
            var hazards = new List<DamageWalls>();

            Transform OpenSection(string label, Vector3 padCentre, float checkpointPrice)
            {
                GameObject sectionPad = CreateBlock(tf, label + "Pad", padCentre, pad, platformMat);
                sectionPad.AddComponent<StickySurface>();
                GameObject spawn = new GameObject(label + "Spawn");
                spawn.transform.SetParent(tf, false);
                spawn.transform.position = padCentre + new Vector3(0f, pad.y * 0.5f + 1.5f, 0f);
                sections.Add(new LevelSectionController.Section { label = label, spawnPoint = spawn.transform });

                GameObject checkpoint = InstantiatePrefab("Checkpoint");
                checkpoint.name = label + "Checkpoint";
                checkpoint.transform.SetParent(tf, false);
                checkpoint.transform.position = padCentre + new Vector3(0f, pad.y * 0.5f + 0.75f, 0f);
                checkpoint.transform.localScale = new Vector3(pad.x * 0.9f, 0.5f, pad.z * 0.9f);
                Checkpoint point = checkpoint.GetComponent<Checkpoint>();
                point.respawnPoint = spawn.transform;
                point.minActivationEnergyFraction = checkpointPrice;
                EditorUtility.SetDirty(checkpoint);
                AddBandRequirement(checkpoint, palette, pipMaterial, point.buttonRenderer);
                return spawn.transform;
            }

            float gap = 0.42f * L;
            float lead = gap * 0.5f;
            float x = 0f;
            float e1, e2, e3;

            Transform s1 = OpenSection("1 - Foundations", new Vector3(x, -1f, 0f), 0.2f);
            Vector3 playerSpawn = new Vector3(x, 1.5f, 0f);
            e1 = x + lead; e2 = e1 + gap; e3 = e2 + gap;
            CreateBlock(tf, "F_Hop1", new Vector3(e1, 1f, 9f), pad, platformMat);
            CreateBlock(tf, "F_Hop2", new Vector3(e2, 4f, -8f), pad, platformMat);
            SpawnMovingPlatform(tf, "F_Mover", new Vector3(e2 + gap * 0.5f, 5f, 2f), new Vector3(0f, 0f, 20f), 8f);
            GameObject f_deck = CreateBlock(tf, "F_Arena", new Vector3(e3, 5f, 0f), new Vector3(26f, 2f, 26f), platformMat);
            f_deck.AddComponent<StickySurface>();
            SpawnBandedSizedEnemy("SL_Small1", new Vector3(e3, 7f, 0f), EnemySizeClass.Small, 9f, palette, pipMaterial);

            x = e3 + gap;
            Transform s2 = OpenSection("2 - Momentum", new Vector3(x, 4f, 0f), 0.2f);
            e1 = x + lead; e2 = e1 + gap; e3 = e2 + gap;
            SpawnMovingPlatform(tf, "M_Side", new Vector3(e1, 6f, -13f), new Vector3(0f, 0f, 26f), 7f);
            GameObject m_spin = SpawnRotatingWall(tf, "M_Spin", new Vector3(e2, 10f, 6f), 30f, 0f, wallMat);
            AddEdgeDamageShell(m_spin.transform, s2, damageMat);
            SpawnMovingPlatform(tf, "M_Lift", new Vector3(e2 + gap * 0.5f, 3f, -6f), new Vector3(0f, 15f, 0f), 6.5f);
            CreateBlock(tf, "M_Landing", new Vector3(e3, 9f, 4f), pad, platformMat);
            SpawnBandedSizedEnemy("SL_Medium1", new Vector3(e3, 11f, 4f), EnemySizeClass.Medium, 5f, palette, pipMaterial);

            x = e3 + gap;
            Transform s3 = OpenSection("3 - Crossfire", new Vector3(x, 8f, 0f), 0.4f);
            e1 = x + lead; e2 = e1 + gap; e3 = e2 + gap;
            CreateBlock(tf, "C_Run1", new Vector3(e1, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            CreateLaserGate(tf, "C_Gate1", new Vector3(x + lead * 0.5f, 9f, 0f), 24f, 12f, 1.5f, 1.6f, 0f, s3);
            CreateBlock(tf, "C_Run2", new Vector3(e2, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            CreateLaserGate(tf, "C_Gate2", new Vector3((e1 + e2) * 0.5f, 9f, 0f), 24f, 12f, 1.1f, 1.3f, 0.6f, s3);
            GameObject c_wall = CreateBlock(tf, "C_TurretWall", new Vector3(e2, 12f, -15f), new Vector3(20f, 16f, 2f), wallMat);
            AddDamageShell(c_wall.transform, s3, damageMat);
            SpawnBandedTurret("SL_Turret1", new Vector3(e2, 13f, -13.8f), new Vector3(-90f, 0f, 0f), palette, pipMaterial);
            SpawnMovingPlatform(tf, "C_Ferry", new Vector3(e3, 8f, 0f), new Vector3(0f, 0f, 18f), 6f);

            x = e3 + gap;
            Transform s4 = OpenSection("4 - Aerial", new Vector3(x, 8f, 0f), 0.4f);
            e1 = x + lead; e2 = e1 + gap; e3 = e2 + gap;
            CreateBlock(tf, "A_Step1", new Vector3(e1, 10f, -9f), pad, platformMat);
            SpawnBandedFlyer("SL_Flyer1", new Vector3(x + lead * 0.7f, 17f, -3f), 10f, 26f, palette, pipMaterial);
            GameObject a_spin = SpawnRotatingWall(tf, "A_Spin", new Vector3(e2, 14f, 4f), -34f, 90f, wallMat);
            AddEdgeDamageShell(a_spin.transform, s4, damageMat);
            SpawnTiltedLedge(tf, "A_LedgeA", new Vector3(e2, 8f, -16f), new Vector3(0f, 0f, 22f), wallMat);
            CreateBlock(tf, "A_Step2", new Vector3(e3, 13f, 10f), pad, platformMat);
            SpawnBandedFlyer("SL_Flyer2", new Vector3(e2 + gap * 0.6f, 21f, 6f), 12f, 28f, palette, pipMaterial);

            x = e3 + gap;
            Transform s5 = OpenSection("5 - Siege", new Vector3(x, 6f, 0f), 0.6f);
            e1 = x + lead; e2 = e1 + gap; e3 = e2 + gap;
            GameObject s_arena = CreateBlock(tf, "S_Arena", new Vector3(e1, 4f, 0f), new Vector3(30f, 2f, 30f), platformMat);
            s_arena.AddComponent<StickySurface>();
            SpawnBandedSizedEnemy("SL_Small2", new Vector3(e1 - 7f, 6f, 4f), EnemySizeClass.Small, 9f, palette, pipMaterial);
            SpawnBandedSizedEnemy("SL_Large1", new Vector3(e1 + 7f, 6f, -4f), EnemySizeClass.Large, 9f, palette, pipMaterial);
            GameObject s_wallL = CreateBlock(tf, "S_WallLeft", new Vector3(e1, 10f, -19f), new Vector3(20f, 16f, 2f), wallMat);
            GameObject s_wallR = CreateBlock(tf, "S_WallRight", new Vector3(e2, 10f, 19f), new Vector3(20f, 16f, 2f), wallMat);
            AddDamageShell(s_wallL.transform, s5, damageMat);
            AddDamageShell(s_wallR.transform, s5, damageMat);
            SpawnBandedTurret("SL_Turret2", new Vector3(e1, 11f, -17.8f), new Vector3(-90f, 0f, 0f), palette, pipMaterial);
            SpawnBandedTurret("SL_Turret3", new Vector3(e2, 11f, 17.8f), new Vector3(90f, 0f, 0f), palette, pipMaterial);
            CreateBlock(tf, "S_Run", new Vector3(e2, 6f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            CreateLaserGate(tf, "S_Gate", new Vector3((e2 + e3) * 0.5f, 7f, 0f), 24f, 12f, 1.0f, 1.2f, 0.3f, s5);
            SpawnMovingPlatform(tf, "S_Ferry", new Vector3(e3, 7f, 0f), new Vector3(0f, 9f, 14f), 7f);

            x = e3 + gap;
            Transform s6 = OpenSection("6 - Gauntlet", new Vector3(x, 8f, 0f), 0.6f);
            e1 = x + lead; e2 = e1 + gap; e3 = e2 + gap;
            SpawnMovingPlatform(tf, "G_Approach", new Vector3(e1, 9f, -8f), new Vector3(0f, 0f, 22f), 5.5f);
            GameObject g_wall = CreateBlock(tf, "G_TurretWall", new Vector3(e1, 14f, 18f), new Vector3(20f, 16f, 2f), wallMat);
            AddDamageShell(g_wall.transform, s6, damageMat);
            SpawnBandedTurret("SL_Turret4", new Vector3(e1, 15f, 16.8f), new Vector3(90f, 0f, 0f), palette, pipMaterial);
            CreateBlock(tf, "G_Perch", new Vector3(e2, 11f, 2f), pad, platformMat);
            CreateLaserGate(tf, "G_Gate", new Vector3(e2, 12f, 0f), 24f, 12f, 0.9f, 1.1f, 0.4f, s6);
            SpawnBandedFlyer("SL_Flyer3", new Vector3(e2 - gap * 0.3f, 19f, -6f), 11f, 28f, palette, pipMaterial);
            SpawnBandedFlyer("SL_Flyer4", new Vector3(e2 + gap * 0.3f, 21f, 8f), 11f, 28f, palette, pipMaterial);
            GameObject g_spin = SpawnRotatingWall(tf, "G_Spin", new Vector3(e2 + gap * 0.55f, 13f, -4f), 40f, 45f, wallMat);
            AddEdgeDamageShell(g_spin.transform, s6, damageMat);
            GameObject g_arena = CreateBlock(tf, "G_Arena", new Vector3(e3, 8f, 0f), new Vector3(30f, 2f, 30f), platformMat);
            g_arena.AddComponent<StickySurface>();
            SpawnBandedSizedEnemy("SL_Medium2", new Vector3(e3 - 7f, 10f, 5f), EnemySizeClass.Medium, 9f, palette, pipMaterial);
            SpawnBandedSizedEnemy("SL_Large2", new Vector3(e3 + 7f, 10f, -5f), EnemySizeClass.Large, 9f, palette, pipMaterial);

            x = e3 + gap;
            CreateBlock(tf, "FinishPad", new Vector3(x, 6f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            float courseLength = x;

            CreateFinishVolumePrefab();
            GameObject finish = InstantiatePrefab("FinishVolume");
            finish.name = "SecondLevelFinish";
            finish.transform.SetParent(tf, false);
            finish.transform.position = new Vector3(x, 9f, 0f);
            finish.transform.localScale = new Vector3(6f, 6f, 12f);
            EditorUtility.SetDirty(finish);

            foreach (LaserWall gate in course.GetComponentsInChildren<LaserWall>(true))
            {
                foreach (DamageWalls beamDamage in gate.GetComponentsInChildren<DamageWalls>(true))
                {
                    GameObject beamsGo = beamDamage.gameObject;
                    UnityEngine.Object.DestroyImmediate(beamDamage);
                    if (beamsGo.GetComponent<LaserHazard>() == null) beamsGo.AddComponent<LaserHazard>();
                    EditorUtility.SetDirty(beamsGo);
                }
            }

            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(courseLength * 0.5f, -26f, 0f), new Vector3(courseLength + 120f, 2f, 240f), damageMat);
            DamageWalls floorDamage = damageFloor.AddComponent<DamageWalls>();
            hazards.Add(floorDamage);
            foreach (DamageWalls courseHazard in course.GetComponentsInChildren<DamageWalls>(true))
            {
                hazards.Add(courseHazard);
            }

            GameObject sectionsGo = new GameObject("LevelSections");
            LevelSectionController sectionController = sectionsGo.AddComponent<LevelSectionController>();
            sectionController.sections = sections.ToArray();
            sectionController.hazards = hazards.ToArray();
            EditorUtility.SetDirty(sectionController);
            foreach (DamageWalls hazard in hazards)
            {
                hazard.respawnPoint = sections[0].spawnPoint;
                EditorUtility.SetDirty(hazard);
            }
            RebuildSectionsScreenButtons(sectionController);

            player.transform.position = playerSpawn;
            EditorUtility.SetDirty(player);

            SectionIntroHud hud = UnityEngine.Object.FindAnyObjectByType<SectionIntroHud>(FindObjectsInactive.Include);
            if (hud != null)
            {
                hud.sections = sectionController;
                hud.sectionTexts = new[]
                {
                    "FOUNDATIONS\nCharge a launch and release to fire. Midair, hold the aim button to slow time and re-aim - the wheel or right stick dials the energy up and down. Ground pound with E / pad-West while airborne.\n\nCheckpoints are BUTTONS: land steeply or pound them, using at least the energy they show.",
                    "MOMENTUM\nThe ghost copy shows where a platform will be WHEN YOU LAND - aim at the ghost. The rotating wall's broad faces carry you; its thin red edges cost energy.",
                    "CROSSFIRE\nBeams shove you back and drain energy. The turret telegraphs, then fires a burst - cross while it reloads.",
                    "AERIAL\nFlyers only die to a hit on the marked back cube; anything else staggers them and drops you on top. The tilted ledges will not hold a cling.",
                    "SIEGE\nGuns on both flanks. The arena holds a cheap kill and an expensive one - check the pips before you commit the energy.",
                    "GAUNTLET\nEverything at once. Energy is the real resource here: every kill has a price, and the meter's marker shows whether this shot can pay it.",
                };
                EditorUtility.SetDirty(hud);
            }

            EnsureSceneInBuildSettings(targetPath);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: SecondLevel built OK (" + sections.Count + " sections, length "
                + courseLength.ToString("F0") + ")");
        }

        [MenuItem("Tools/Kinetic Energy/Validate Checkpoint Prices")]
        public static void ValidateCheckpointPrices()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest3.unity", OpenSceneMode.Single);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Checkpoint.prefab");
            Checkpoint prefabPoint = prefab != null ? prefab.GetComponent<Checkpoint>() : null;
            Debug.Log("CPCHECK prefabValue=" + (prefabPoint != null ? prefabPoint.minActivationEnergyFraction.ToString("F2") : "NONE"));

            foreach (Checkpoint point in UnityEngine.Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool overridden = false;
                if (PrefabUtility.IsPartOfPrefabInstance(point.gameObject))
                {
                    foreach (PropertyModification mod in PrefabUtility.GetPropertyModifications(point.gameObject))
                    {
                        if (mod != null && mod.propertyPath == "minActivationEnergyFraction") overridden = true;
                    }
                }
                Debug.Log("CPCHECK '" + point.name + "' x=" + point.transform.position.x.ToString("F0")
                    + " price=" + point.minActivationEnergyFraction.ToString("F2")
                    + " instanceOverride=" + overridden);
            }
        }

        [MenuItem("Tools/Kinetic Energy/Validate SecondLevel")]
        public static void ValidateSecondLevel()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SecondLevel.unity", OpenSceneMode.Single);

            LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
            Debug.Log("SL sections=" + (sections != null ? sections.sections.Length.ToString() : "NONE")
                + " hazards=" + (sections != null ? sections.hazards.Length.ToString() : "-"));
            if (sections != null)
            {
                foreach (LevelSectionController.Section s in sections.sections)
                {
                    Debug.Log("SL section '" + s.label + "' x=" + (s.spawnPoint != null ? s.spawnPoint.position.x.ToString("F0") : "NULL"));
                }
            }

            Debug.Log("SL sized=" + UnityEngine.Object.FindObjectsByType<SizedEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " flyers=" + UnityEngine.Object.FindObjectsByType<WeakSpotFlyingEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " turrets=" + UnityEngine.Object.FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " movers=" + UnityEngine.Object.FindObjectsByType<MovingPlatform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " lasers=" + UnityEngine.Object.FindObjectsByType<LaserWall>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " spins=" + UnityEngine.Object.FindObjectsByType<RotatingWall>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " checkpoints=" + UnityEngine.Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " finishes=" + UnityEngine.Object.FindObjectsByType<WinOnFinish>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);

            int missingPalette = 0, missingPip = 0;
            foreach (EnergyRequirement r in UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.palette == null) missingPalette++;
                if (r.pipMaterial == null) missingPip++;
            }
            Debug.Log("SL requirements=" + UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                + " missingPalette=" + missingPalette + " missingPipMaterial=" + missingPip);

            SectionIntroHud hud = UnityEngine.Object.FindAnyObjectByType<SectionIntroHud>(FindObjectsInactive.Include);
            Debug.Log("SL hud=" + (hud != null ? "texts=" + hud.sectionTexts.Length + " wired=" + (hud.sections != null) : "NONE"));
        }

        static void DestroyAllOfType<T>() where T : Component
        {
            foreach (T victim in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (victim != null) UnityEngine.Object.DestroyImmediate(victim.gameObject);
            }
        }

        static void AddBandRequirement(GameObject target, EnergyBandPalette palette, Material pipMaterial, Renderer shows)
        {
            EnergyRequirement requirement = target.GetComponent<EnergyRequirement>();
            if (requirement == null) requirement = target.AddComponent<EnergyRequirement>();
            requirement.palette = palette;
            requirement.pipMaterial = pipMaterial;
            requirement.targetRenderer = shows != null ? shows : target.GetComponentInChildren<Renderer>();
            EditorUtility.SetDirty(requirement);
        }

        static void SpawnBandedSizedEnemy(string name, Vector3 position, EnemySizeClass sizeClass, float radius,
            EnergyBandPalette palette, Material pipMaterial)
        {
            SpawnSizedEnemy(name, position, sizeClass, EnemyWanderMode.PlatformSurface, radius);
            GameObject instance = GameObject.Find(name);
            if (instance != null) AddBandRequirement(instance, palette, pipMaterial, instance.GetComponentInChildren<Renderer>());
        }

        static void SpawnBandedFlyer(string name, Vector3 position, float radius, float detection,
            EnergyBandPalette palette, Material pipMaterial)
        {
            SpawnWeakSpotFlyer(name, position, radius, detection);
            GameObject instance = GameObject.Find(name);
            if (instance == null) return;
            WeakSpotFlyingEnemy flyer = instance.GetComponent<WeakSpotFlyingEnemy>();
            if (flyer != null)
            {
                flyer.minKillEnergyFraction = 0.4f;
                EditorUtility.SetDirty(flyer);
                AddBandRequirement(instance, palette, pipMaterial,
                    flyer.weakSpot != null ? flyer.weakSpot.GetComponent<Renderer>() : null);
            }
        }

        static void SpawnBandedTurret(string name, Vector3 position, Vector3 eulerRotation,
            EnergyBandPalette palette, Material pipMaterial)
        {
            SpawnTurret(name, position, eulerRotation);
            GameObject instance = GameObject.Find(name);
            if (instance == null) return;
            TurretEnemy turret = instance.GetComponent<TurretEnemy>();
            if (turret != null)
            {
                turret.minKillEnergyFraction = 0.4f;
                EditorUtility.SetDirty(turret);
            }
            AddBandRequirement(instance, palette, pipMaterial, instance.GetComponentInChildren<Renderer>());
        }

        [MenuItem("Tools/Kinetic Energy/Bake Laser Charging Loop")]
        public static void BakeLaserChargingLoop()
        {
            const string sourcePath = "Assets/Audio/LaserCharging.mp3";
            const string bakedPath = "Assets/Audio/LaserChargingLoop.wav";
            const float keepSeconds = 5f;

            AudioImporter importer = AssetImporter.GetAtPath(sourcePath) as AudioImporter;
            if (importer == null) throw new Exception("KineticEnergySetup: " + sourcePath + " missing.");
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();

            AudioClip source = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath);
            if (source == null) throw new Exception("KineticEnergySetup: clip failed to import.");
            source.LoadAudioData();

            int keepFrames = Mathf.Min(Mathf.RoundToInt(keepSeconds * source.frequency), source.samples);
            float[] samples = new float[keepFrames * source.channels];
            if (!source.GetData(samples, 0)) throw new Exception("KineticEnergySetup: GetData failed - check the clip's load type.");

            WriteWav(bakedPath, samples, source.channels, source.frequency);
            AssetDatabase.ImportAsset(bakedPath);
            AudioClip baked = AssetDatabase.LoadAssetAtPath<AudioClip>(bakedPath);
            if (baked == null) throw new Exception("KineticEnergySetup: baked wav failed to import.");

            string prefabPath = PrefabFolder + "/Player.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Polish polish = root.GetComponent<Polish>();
                if (polish == null) throw new Exception("KineticEnergySetup: Player has no Polish.");
                polish.chargingLoopSound = baked;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: laser charging loop baked OK (" + keepFrames + " frames @" + source.frequency
                + "Hz, " + source.channels + "ch, source " + source.length.ToString("F2") + "s)");
        }

        static void WriteWav(string assetPath, float[] samples, int channels, int frequency)
        {
            using (var stream = new FileStream(assetPath, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(frequency);
                writer.Write(frequency * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                foreach (float sample in samples)
                {
                    writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
                }
            }
        }

        [MenuItem("Tools/Kinetic Energy/Tune Trail Speed Lines")]
        public static void TuneTrailSpeedLines()
        {
            Material trail = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/TrailScreenspace.mat");
            if (trail == null) throw new Exception("KineticEnergySetup: TrailScreenspace.mat missing.");
            trail.SetFloat("_Intensity", 0.25f);
            trail.SetFloat("_LineCount", 90f);
            trail.SetFloat("_LineWidth", 0.045f);
            trail.SetFloat("_Density", 0.55f);
            trail.SetFloat("_InnerRadius", 1f);
            trail.SetFloat("_OuterRadius", 2f);
            trail.SetFloat("_TailSoftness", 0.05f);
            trail.SetFloat("_OutwardTaper", 0.15f);
            trail.SetFloat("_StreamSpeed", 0.35f);
            trail.SetFloat("_StreamLength", 0.7f);
            EditorUtility.SetDirty(trail);
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: trail speed lines tuned OK");
        }

        [MenuItem("Tools/Kinetic Energy/Give Shells Own Material")]
        public static void GiveShellsOwnMaterial()
        {
            Material shellMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/DamageShellMaterial.mat");
            if (shellMaterial == null)
            {
                Material fresh = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                fresh.color = new Color(0.95f, 0.08f, 0.05f);
                fresh.SetFloat("_Smoothness", 0f);
                shellMaterial = SaveMaterialAsset(fresh, "DamageShellMaterial");
            }

            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest3.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
                "Assets/Scenes/SecondLevel.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                int assigned = 0;
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!t.name.StartsWith("DamageShell")) continue;
                    Renderer shellRenderer = t.GetComponent<Renderer>();
                    if (shellRenderer == null) continue;
                    shellRenderer.sharedMaterial = shellMaterial;
                    EditorUtility.SetDirty(t.gameObject);
                    assigned++;
                }
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - " + assigned + " shells on their own material");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Exempt Visual Materials From Outline")]
        public static void ExemptVisualMaterialsFromOutline()
        {
            foreach (string materialName in new[] { "PreviewSolidMaterial", "AimArrowMaterial", "LaserBeamMaterial" })
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + materialName + ".mat");
                if (material == null) { Debug.LogWarning("KineticEnergySetup: " + materialName + " missing"); continue; }
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(material);
                Debug.Log("KineticEnergySetup: " + materialName + " now transparent-surface (outline-exempt)");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Setup NoOutline Layer")]
        public static void SetupNoOutlineLayer()
        {

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            int layerIndex = -1;
            for (int i = 8; i < layers.arraySize; i++)
            {
                string current = layers.GetArrayElementAtIndex(i).stringValue;
                if (current == "NoOutline") { layerIndex = i; break; }
                if (string.IsNullOrEmpty(current) && layerIndex < 0) layerIndex = i;
            }
            if (layerIndex < 0) throw new Exception("KineticEnergySetup: no free layer slot.");
            layers.GetArrayElementAtIndex(layerIndex).stringValue = "NoOutline";
            tagManager.ApplyModifiedPropertiesWithoutUndo();

            foreach (string rendererPath in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(rendererPath);
                if (data == null) continue;
                var so = new SerializedObject(data);
                SerializedProperty mask = so.FindProperty("m_PrepassLayerMask.m_Bits");
                mask.longValue = unchecked((uint)~(1 << layerIndex));
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
            }

            void SetSubtree(Transform t)
            {
                foreach (Transform child in t.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layerIndex;
            }
            string playerPath = PrefabFolder + "/Player.prefab";
            GameObject player = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                int marked = 0;
                foreach (string name in new[] { "Trails", "Circle", "Crosshair", "AimArrow", "Debris", "Dust" })
                {
                    Transform t = FindDeep(player.transform, name);
                    if (t != null) { SetSubtree(t); marked++; }
                    else Debug.LogWarning("KineticEnergySetup: player child '" + name + "' not found for NoOutline.");
                }
                PrefabUtility.SaveAsPrefabAsset(player, playerPath);
                Debug.Log("KineticEnergySetup: NoOutline on " + marked + " player subtrees");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }

            string gatePath = PrefabFolder + "/LaserGate.prefab";
            GameObject gate = PrefabUtility.LoadPrefabContents(gatePath);
            try
            {
                Transform beams = FindDeep(gate.transform, "Beams");
                if (beams != null) { SetSubtree(beams); PrefabUtility.SaveAsPrefabAsset(gate, gatePath); }
                Debug.Log("KineticEnergySetup: NoOutline on laser beams=" + (beams != null));
            }
            finally { PrefabUtility.UnloadPrefabContents(gate); }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: NoOutline layer is index " + layerIndex);
        }

        [MenuItem("Tools/Kinetic Energy/Add Edge Outline Feature")]
        public static void AddEdgeOutlineFeature()
        {
            Shader shader = Shader.Find("Custom/URP/EdgeOutline");
            if (shader == null) throw new Exception("KineticEnergySetup: EdgeOutline shader failed to compile or import.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/EdgeOutlineMaterial.mat");
            if (material == null) material = SaveMaterialAsset(new Material(shader), "EdgeOutlineMaterial");

            foreach (string rendererPath in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(rendererPath);
                if (data == null) { Debug.LogWarning("KineticEnergySetup: " + rendererPath + " missing"); continue; }

                UnityEngine.Rendering.Universal.FullScreenPassRendererFeature feature = null;
                foreach (var existing in data.rendererFeatures)
                {
                    if (existing is UnityEngine.Rendering.Universal.FullScreenPassRendererFeature f
                        && existing.name == "EdgeOutline") feature = f;
                }
                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.FullScreenPassRendererFeature>();
                    feature.name = "EdgeOutline";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    data.rendererFeatures.Add(feature);
                }
                feature.passMaterial = material;

                feature.injectionPoint = UnityEngine.Rendering.Universal.FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
                feature.requirements = UnityEngine.Rendering.Universal.ScriptableRenderPassInput.Depth
                    | UnityEngine.Rendering.Universal.ScriptableRenderPassInput.Normal;
                feature.fetchColorBuffer = true;
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
                Debug.Log("KineticEnergySetup: edge outline wired into " + rendererPath);
            }

            foreach (string rendererPath in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(rendererPath);
                if (data == null) continue;
                var so = new SerializedObject(data);
                SerializedProperty features = so.FindProperty("m_RendererFeatures");
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize = features.arraySize;
                for (int i = 0; i < features.arraySize; i++)
                {
                    UnityEngine.Object featureObject = features.GetArrayElementAtIndex(i).objectReferenceValue;
                    long localId = 0;
                    if (featureObject != null)
                    {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(featureObject, out _, out localId);
                    }
                    map.GetArrayElementAtIndex(i).longValue = localId;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log("KineticEnergySetup: feature map repaired for " + rendererPath
                    + " (" + features.arraySize + " features)");
            }

            foreach (string rpPath in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var rp = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(rpPath);
                if (rp != null && !rp.supportsCameraDepthTexture)
                {
                    rp.supportsCameraDepthTexture = true;
                    EditorUtility.SetDirty(rp);
                    Debug.Log("KineticEnergySetup: depth texture enabled on " + rpPath);
                }
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Bake Player Hurt Sound")]
        public static void BakePlayerHurtSound()
        {
            const string sourcePath = "Assets/Audio/PlayerHurt.mp3";
            const string bakedPath = "Assets/Audio/PlayerHurtLoud.wav";

            AudioImporter importer = AssetImporter.GetAtPath(sourcePath) as AudioImporter;
            if (importer == null) throw new Exception("KineticEnergySetup: " + sourcePath + " missing.");
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();

            AudioClip source = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath);
            source.LoadAudioData();
            float[] samples = new float[source.samples * source.channels];
            if (!source.GetData(samples, 0)) throw new Exception("KineticEnergySetup: GetData failed.");

            float peak = 0f;
            foreach (float s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            float gain = peak > 0.0001f ? 0.98f / peak : 1f;
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);

            WriteWav(bakedPath, samples, source.channels, source.frequency);
            AssetDatabase.ImportAsset(bakedPath);
            AudioClip baked = AssetDatabase.LoadAssetAtPath<AudioClip>(bakedPath);

            string prefabPath = PrefabFolder + "/Player.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Polish polish = root.GetComponent<Polish>();
                polish.playerHurtSound = baked;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: hurt sound baked OK (source peak " + peak.ToString("F3")
                + ", gain x" + gain.ToString("F2") + ")");
        }

        [MenuItem("Tools/Kinetic Energy/Convert All Lethal Shells")]
        public static void ConvertAllLethalShells()
        {
            foreach (string scenePath in new[] { "Assets/Scenes/LevelElementsTest3.unity", "Assets/Scenes/SecondLevel.unity" })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                int converted = 0;
                foreach (DamageWalls lethal in UnityEngine.Object.FindObjectsByType<DamageWalls>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    string n = lethal.gameObject.name;

                    if (n == "DamageFloor") continue;
                    Debug.Log("SHELLFIX converting '" + n + "' at " + lethal.transform.position.ToString("F0") + " in " + scenePath);
                    GameObject go = lethal.gameObject;
                    UnityEngine.Object.DestroyImmediate(lethal);
                    if (go.GetComponent<LaserHazard>() == null) go.AddComponent<LaserHazard>();
                    EditorUtility.SetDirty(go);
                    converted++;
                }

                LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
                if (sections != null)
                {
                    sections.hazards = UnityEngine.Object.FindObjectsByType<DamageWalls>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    EditorUtility.SetDirty(sections);
                }

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - " + converted + " lethal shells converted");
            }
        }

        [MenuItem("Tools/Kinetic Energy/Setup Crash Decal")]
        public static void SetupCrashDecal()
        {
            const string texturePath = "Assets/Textures/CrashDecal.png";
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) throw new Exception("KineticEnergySetup: " + texturePath + " missing.");
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/CrashDecalMaterial.mat");
            if (material == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit == null) throw new Exception("KineticEnergySetup: URP/Unlit shader not found.");
                Material fresh = new Material(unlit);
                fresh.SetFloat("_Surface", 1f);
                fresh.SetFloat("_Blend", 0f);
                fresh.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fresh.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fresh.SetInt("_ZWrite", 0);
                fresh.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                fresh.SetOverrideTag("RenderType", "Transparent");
                fresh.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material = SaveMaterialAsset(fresh, "CrashDecalMaterial");
            }

            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);

            string prefabPath = PrefabFolder + "/Player.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Polish polish = root.GetComponent<Polish>();
                if (polish == null) polish = root.AddComponent<Polish>();
                polish.crashDecalMaterial = material;

                Transform debris = FindDeep(root.transform, "Debris");
                Transform dust = FindDeep(root.transform, "Dust");
                if (debris != null) polish.debrisParticles = debris.GetComponent<ParticleSystem>();
                if (dust != null) polish.dustParticles = dust.GetComponent<ParticleSystem>();

                Transform trails = FindDeep(root.transform, "Trails");
                if (trails != null) polish.trailImage = trails.GetComponent<UnityEngine.UI.Image>();

                polish.playerSounds = root.GetComponentInChildren<AudioSource>(true);
                polish.flyingSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Whoosh.mp3");

                polish.chargingLoopSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/LaserChargingLoop.wav");
                polish.crashSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Thud.wav");
                polish.energyClickSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EnergyClick.wav");
                polish.enemyKillSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EnemyBreak.mp3");

                AudioClip hurtLoud = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/PlayerHurtLoud.wav");
                polish.playerHurtSound = hurtLoud != null ? hurtLoud
                    : AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/PlayerHurt.mp3");

                polish.motionTrail = root.GetComponentInChildren<TrailRenderer>(true);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("KineticEnergySetup: debris wired=" + (polish.debrisParticles != null)
                    + " dust wired=" + (polish.dustParticles != null));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: crash decal wired OK");
        }

        [MenuItem("Tools/Kinetic Energy/Add Polish To Player")]
        public static void AddPolishToPlayer()
        {
            string prefabPath = PrefabFolder + "/Player.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Polish polish = root.GetComponent<Polish>();
                if (polish == null) polish = root.AddComponent<Polish>();
                KineticCubeControllerFreeMove freeMove = root.GetComponent<KineticCubeControllerFreeMove>();
                if (freeMove != null && freeMove.visual != null) polish.visual = freeMove.visual;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("KineticEnergySetup: Polish added to Player OK (visual="
                    + (polish.visual != null ? polish.visual.name : "NULL") + ")");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/Kinetic Energy/Fix Runtime Visual Materials")]
        public static void FixRuntimeVisualMaterials()
        {

            Material pip = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/EnergyPipMaterial.mat");
            if (pip == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit == null) throw new Exception("KineticEnergySetup: URP/Unlit shader not found.");
                pip = SaveMaterialAsset(new Material(unlit), "EnergyPipMaterial");
            }

            string[] scenes =
            {
                "Assets/Scenes/LevelElementsTest3.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            };
            foreach (string scenePath in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                int wired = 0;
                foreach (EnergyRequirement requirement in UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    requirement.pipMaterial = pip;
                    EditorUtility.SetDirty(requirement);
                    wired++;
                }
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - pip material wired to " + wired + " interactables");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Validate Controls Image")]
        public static void ValidateControlsImage()
        {
            const string texturePath = "Assets/Textures/ControlsSheet.png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;

            Debug.Log("CTRLIMG texture=" + (tex != null ? tex.width + "x" + tex.height + " format=" + tex.format + " mips=" + tex.mipmapCount + " filter=" + tex.filterMode : "NULL"));
            Debug.Log("CTRLIMG sprite=" + (sprite != null ? sprite.rect.width + "x" + sprite.rect.height + " ppu=" + sprite.pixelsPerUnit : "NULL"));
            if (importer != null)
            {
                Debug.Log("CTRLIMG importer maxSize=" + importer.maxTextureSize
                    + " compression=" + importer.textureCompression
                    + " type=" + importer.textureType
                    + " npot=" + importer.npotScale);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (prefab != null)
            {
                Transform img = FindDeep(prefab.transform, "ControlsImage");
                if (img != null)
                {
                    RectTransform rect = img.GetComponent<RectTransform>();
                    Debug.Log("CTRLIMG rect=" + rect.sizeDelta.x + "x" + rect.sizeDelta.y
                        + " (canvas draws this many UI units; scale 1.0 = that many screen pixels)");
                }
            }
        }

        [MenuItem("Tools/Kinetic Energy/Add Controls Image")]
        public static void AddControlsImage()
        {
            const string texturePath = "Assets/Textures/ControlsSheet.png";
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) throw new Exception("KineticEnergySetup: " + texturePath + " missing - copy the controls sheet in first.");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            Sprite sheet = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
            if (sheet == null) throw new Exception("KineticEnergySetup: " + texturePath + " did not import as a Sprite.");

            string prefabPath = PrefabFolder + "/PauseSystem.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform panel = FindDeep(root.transform, "ControlsPanel");
                if (panel == null) throw new Exception("KineticEnergySetup: ControlsPanel not found in PauseSystem.prefab.");

                Transform body = FindDeep(panel, "ControlsBody");
                if (body != null) body.gameObject.SetActive(false);

                Transform existing = panel.Find("ControlsImage");
                GameObject imageGo = existing != null ? existing.gameObject : null;
                if (imageGo == null)
                {
                    imageGo = new GameObject("ControlsImage", typeof(RectTransform));
                    imageGo.transform.SetParent(panel, false);
                }
                Image image = imageGo.GetComponent<Image>();
                if (image == null) image = imageGo.AddComponent<Image>();
                image.sprite = sheet;
                image.preserveAspect = true;
                image.raycastTarget = false;

                RectTransform rect = imageGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, -10f);
                rect.sizeDelta = new Vector2(1180f, 787f);

                imageGo.transform.SetAsFirstSibling();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("KineticEnergySetup: controls image added to the pause menu OK");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }
            return null;
        }

        [MenuItem("Tools/Kinetic Energy/Setup Section Intro HUD")]
        public static void SetupSectionIntroHud()
        {
            string[] scenes =
            {
                "Assets/Scenes/LevelElementsTest3.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            };

            foreach (string scenePath in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>();
                if (sections == null)
                {
                    Debug.LogWarning("KineticEnergySetup: " + scenePath + " has no LevelSectionController - HUD skipped.");
                    continue;
                }

                SectionIntroHud hud = UnityEngine.Object.FindAnyObjectByType<SectionIntroHud>();
                if (hud == null)
                {
                    GameObject go = new GameObject("SectionIntroHud");
                    hud = go.AddComponent<SectionIntroHud>();
                }
                hud.sections = sections;

                bool sized = scenePath.Contains("Test3");
                string[] texts = new string[sections.sections.Length];
                for (int i = 0; i < sections.sections.Length; i++)
                {
                    texts[i] = SectionIntroTextFor(sections.sections[i].label, i == 0, sized);
                }
                hud.sectionTexts = texts;
                EditorUtility.SetDirty(hud);

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - section intro HUD wired OK (" + texts.Length + " sections)");
            }
        }

        static string SectionIntroTextFor(string label, bool isFirst, bool sizedEnemies)
        {
            string lower = (label ?? "").ToLowerInvariant();
            string body;
            if (lower.Contains("basic"))
            {
                body = "THE BASICS\nCharge a launch and release to fire. Midair, hold the aim button to slow time and re-aim - the scroll wheel or right stick dials the energy up and down. Ground pound with E / pad-West while airborne.\n\nCheckpoints are BUTTONS: press one with a steep landing or a ground pound, using at least 60% energy.";
            }
            else if (lower.Contains("moving"))
            {
                body = "MOVING PLATFORMS\nThe ghost copy shows where a platform will be WHEN YOU LAND. Aim at the ghost, not at the platform.";
            }
            else if (lower.Contains("rotating"))
            {
                body = "ROTATING WALLS\nThe broad faces are safe to land on and ride. The thin red edges knock you back and cost energy - time your launch through the gaps.";
            }
            else if (lower.Contains("laser"))
            {
                body = "LASERS\nBeams shove you back the way you came and drain energy. The dark columns are safe to land on.";
            }
            else if (lower.Contains("ground"))
            {
                body = sizedEnemies
                    ? "SIZED HUNTERS\nTheir colour and the pips above them show the energy a kill needs: small 20%, medium 40%, large 60%. They dodge launches and only die while cooling down after an attack - when their body lights up in their colour."
                    : "HUNTERS\nThey dodge your launches and attack even midair. They can only be killed while cooling down after an attack - when their body turns purple.";
            }
            else if (lower.Contains("fly"))
            {
                body = "WEAK-SPOT FLYERS\nOnly a hit on the marked back cube kills them" + (sizedEnemies ? " (at least 40% energy)" : "") + ". Anything else staggers them and knocks you back.";
            }
            else if (lower.Contains("turret"))
            {
                body = "TURRETS\nThey telegraph, then fire a burst of shots" + (sizedEnemies ? " and need at least 40% energy to destroy" : "") + ". Closing the distance between bursts is the opening.";
            }
            else
            {
                body = label;
            }
            return isFirst && !lower.Contains("basic")
                ? "CONTROLS\nCharge a launch and release to fire; midair, hold the aim button to slow time and re-aim.\n\n" + body
                : body;
        }

        [MenuItem("Tools/Kinetic Energy/Apply Energy Bands")]
        public static void ApplyEnergyBands()
        {
            EnergyBandPalette palette = EnsureBandPalette();

            Material damageMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/DamageWallMaterial.mat");
            if (damageMaterial != null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit != null && damageMaterial.shader != unlit)
                {
                    Color keep = damageMaterial.color;
                    damageMaterial.shader = unlit;
                    damageMaterial.color = keep;
                    EditorUtility.SetDirty(damageMaterial);
                }
            }
            else Debug.LogWarning("KineticEnergySetup: DamageWallMaterial not found at Assets/Materials - unlit switch skipped.");

            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest3.unity", OpenSceneMode.Single);

            int rewired = 0;
            foreach (EnergyRequirement requirement in UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                requirement.palette = palette;
                requirement.buildTickMarks = true;
                EditorUtility.SetDirty(requirement);
                rewired++;
            }

            int wired = 0;
            KineticCubeController player = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>();
            if (player != null && player.energyMeter != null)
            {
                player.energyMeter.bandPalette = palette;
                EditorUtility.SetDirty(player.energyMeter);
                wired++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: energy bands applied OK (" + rewired + " interactables, " + wired + " meter)");
        }

        [MenuItem("Tools/Kinetic Energy/Validate Meter Geometry")]
        public static void ValidateMeterGeometry()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest3.unity", OpenSceneMode.Single);

            KineticCubeController player = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>();
            if (player == null || player.energyMeter == null)
            {
                Debug.Log("METERCHECK no player or no energyMeter reference");
                return;
            }

            EnergyMeterController meter = player.energyMeter;
            Debug.Log("METERCHECK meterObject=" + meter.gameObject.name);

            RectTransform body = meter.transform.Find("Body") as RectTransform;
            if (body != null)
            {
                Debug.Log("METERCHECK body width=" + body.sizeDelta.x + " height=" + body.sizeDelta.y);
                int activeDividers = 0;
                Transform dividers = body.Find("MeterDividers");
                if (dividers != null)
                {
                    foreach (Transform d in dividers)
                    {
                        if (d.gameObject.activeSelf) activeDividers++;
                    }
                }
                Debug.Log("METERCHECK activeMainDividers=" + activeDividers + " (blocks=" + (activeDividers + 1) + ")");

                RectTransform zone = body.Find("PremiumZone") as RectTransform;
                if (zone == null) Debug.Log("METERCHECK premiumZone=NONE");
                else
                {
                    int zoneDividers = 0;
                    foreach (Transform d in zone)
                    {
                        if (d.name.StartsWith("PremiumDivider") && d.gameObject.activeSelf) zoneDividers++;
                    }
                    Debug.Log("METERCHECK premiumZone width=" + zone.sizeDelta.x + " height=" + zone.sizeDelta.y
                        + " dividers=" + zoneDividers + " (blocks=" + (zoneDividers + 1) + ")");
                }
            }

            MergedEconomyController economy = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>();
            Debug.Log("METERCHECK economy=" + (economy != null ? economy.name : "NONE"));
        }

        [MenuItem("Tools/Kinetic Energy/Validate Energy Tiers")]
        public static void ValidateEnergyTiers()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest3.unity", OpenSceneMode.Single);

            foreach (EnergyRequirement requirement in UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SizedEnemy sized = requirement.GetComponent<SizedEnemy>();
                FlyingEnemy flyer = requirement.GetComponent<FlyingEnemy>();
                TurretEnemy turret = requirement.GetComponent<TurretEnemy>();
                Checkpoint checkpoint = requirement.GetComponent<Checkpoint>();

                string gate =
                    sized != null ? "size=" + sized.sizeClass + " killWindow=" + sized.killWindow + " dodge=" + sized.dodgePlayerLaunches
                    : flyer != null ? "flyerMinKill=" + flyer.minKillEnergyFraction
                    : turret != null ? "turretMinKill=" + turret.minKillEnergyFraction
                    : checkpoint != null ? "checkpointMinEnergy=" + checkpoint.minActivationEnergyFraction
                    : "?";

                Debug.Log("TIERCHECK " + requirement.name
                    + " x=" + requirement.transform.position.x.ToString("F0")
                    + " requires=" + requirement.requirementPercent + "%"
                    + " palette=" + (requirement.palette != null ? "OK" : "NULL")
                    + " renderer=" + (requirement.targetRenderer != null ? requirement.targetRenderer.name : "NULL")
                    + " " + gate);
            }
        }

        const string BandPalettePath = "Assets/Settings/EnergyBandPalette.asset";

        static EnergyBandPalette EnsureBandPalette()
        {
            EnergyBandPalette palette = AssetDatabase.LoadAssetAtPath<EnergyBandPalette>(BandPalettePath);
            if (palette != null) return palette;
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            palette = ScriptableObject.CreateInstance<EnergyBandPalette>();
            AssetDatabase.CreateAsset(palette, BandPalettePath);
            AssetDatabase.SaveAssets();
            return palette;
        }

        [MenuItem("Tools/Kinetic Energy/Setup LevelElementsTest3")]
        public static void SetupLevelElementsTest3()
        {
            const string sourcePath = "Assets/Scenes/LevelElementsTest2.unity";
            const string targetPath = "Assets/Scenes/LevelElementsTest3.unity";

            EnergyBandPalette palette = EnsureBandPalette();

            string sizedPath = PrefabFolder + "/SizedEnemy.prefab";
            GameObject sizedRoot = PrefabUtility.LoadPrefabContents(sizedPath);
            try
            {
                SizedEnemy sized = sizedRoot.GetComponent<SizedEnemy>();
                if (sized == null) throw new Exception("KineticEnergySetup: SizedEnemy prefab has no SizedEnemy component.");

                Enemy hunter = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/HunterEnemy.prefab")
                    ?.GetComponent<Enemy>();
                if (hunter == null) throw new Exception("KineticEnergySetup: HunterEnemy prefab missing - cannot mirror its behaviour.");

                var baseFields = typeof(Enemy).GetFields(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                int copied = 0;
                foreach (var field in baseFields)
                {

                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                    object mine = field.GetValue(sized);
                    object theirs = field.GetValue(hunter);
                    if (Equals(mine, theirs)) continue;
                    field.SetValue(sized, theirs);
                    copied++;
                }
                if (copied > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(sizedRoot, sizedPath);
                    Debug.Log("KineticEnergySetup: SizedEnemy matched to hunter behaviour OK (" + copied + " fields)");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(sizedRoot); }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(targetPath) != null) AssetDatabase.DeleteAsset(targetPath);
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                throw new Exception("KineticEnergySetup: could not copy LevelElementsTest2 to LevelElementsTest3.");
            }
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);

            GameObject sizedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sizedPath);

            var walkers = new List<Enemy>(UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            walkers.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            EnemySizeClass[] classes = { EnemySizeClass.Medium, EnemySizeClass.Small, EnemySizeClass.Large };
            int[] classPercents = { 40, 20, 60 };
            for (int i = 0; i < walkers.Count && i < classes.Length; i++)
            {
                Enemy old = walkers[i];
                GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(sizedPrefab, old.transform.parent);
                replacement.name = old.name;
                replacement.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                SizedEnemy sizedInstance = replacement.GetComponent<SizedEnemy>();
                sizedInstance.sizeClass = classes[i];
                AddRequirement(replacement, classPercents[i], palette, replacement.GetComponentInChildren<Renderer>());
                EditorUtility.SetDirty(sizedInstance);
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            foreach (WeakSpotFlyingEnemy flyerInstance in UnityEngine.Object.FindObjectsByType<WeakSpotFlyingEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                flyerInstance.minKillEnergyFraction = 0.4f;
                Renderer spot = flyerInstance.weakSpot != null ? flyerInstance.weakSpot.GetComponent<Renderer>() : null;
                AddRequirement(flyerInstance.gameObject, 40, palette, spot);
                EditorUtility.SetDirty(flyerInstance);
            }

            foreach (TurretEnemy turretInstance in UnityEngine.Object.FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                turretInstance.minKillEnergyFraction = 0.8f;
                AddRequirement(turretInstance.gameObject, 80, palette, turretInstance.GetComponentInChildren<Renderer>());
                EditorUtility.SetDirty(turretInstance);
            }

            foreach (Checkpoint checkpointInstance in UnityEngine.Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                checkpointInstance.minActivationEnergyFraction = 0.6f;
                Renderer button = checkpointInstance.buttonRenderer != null
                    ? checkpointInstance.buttonRenderer
                    : checkpointInstance.GetComponentInChildren<Renderer>();
                AddRequirement(checkpointInstance.gameObject, 60, palette, button);
                EditorUtility.SetDirty(checkpointInstance);
            }

            EnsureSceneInBuildSettings(targetPath);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: LevelElementsTest3 built OK (sized hunters + tiered requirements)");
        }

        static void AddRequirement(GameObject target, int percent, EnergyBandPalette palette, Renderer shows)
        {
            EnergyRequirement requirement = target.GetComponent<EnergyRequirement>();
            if (requirement == null) requirement = target.AddComponent<EnergyRequirement>();
            requirement.requirementPercent = percent;
            requirement.palette = palette;
            requirement.targetRenderer = shows;
            EditorUtility.SetDirty(requirement);
        }

        [MenuItem("Tools/Kinetic Energy/Create Finish Volume Prefab")]
        public static void CreateFinishVolumePrefab()
        {
            string path = PrefabFolder + "/FinishVolume.prefab";
            Material finishMat = MakeTransparentMaterial("FinishVolumeMaterial", new Color(0.25f, 0.95f, 0.45f, 0.3f));

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                GameObject seed = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seed.name = "FinishVolume";
                PrefabUtility.SaveAsPrefabAsset(seed, path);
                UnityEngine.Object.DestroyImmediate(seed);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;

                Renderer bodyRenderer = root.GetComponent<Renderer>();
                if (bodyRenderer != null)
                {
                    bodyRenderer.sharedMaterial = finishMat;
                    bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (root.GetComponent<WinOnFinish>() == null) root.AddComponent<WinOnFinish>();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: FinishVolume prefab ready OK");
        }

        [MenuItem("Tools/Kinetic Energy/Validate Finish Volumes")]
        public static void ValidateFinishVolumes()
        {
            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (WinOnFinish finish in UnityEngine.Object.FindObjectsByType<WinOnFinish>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    BoxCollider box = finish.GetComponent<BoxCollider>();
                    Renderer bodyRenderer = finish.GetComponent<Renderer>();
                    Vector3 worldSize = box != null ? Vector3.Scale(box.size, finish.transform.lossyScale) : Vector3.zero;
                    Debug.Log("FINISHCHECK " + System.IO.Path.GetFileNameWithoutExtension(scenePath)
                        + " name=" + finish.name
                        + " prefabInstance=" + PrefabUtility.IsPartOfPrefabInstance(finish.gameObject)
                        + " pos=" + finish.transform.position
                        + " lossyScale=" + finish.transform.lossyScale
                        + " triggerWorldSize=" + worldSize
                        + " isTrigger=" + (box != null && box.isTrigger)
                        + " renderer=" + (bodyRenderer != null ? bodyRenderer.sharedMaterial.name : "NONE"));
                }
            }
        }

        [MenuItem("Tools/Kinetic Energy/Give Element Finishes A Visual")]
        public static void GiveElementFinishesAVisual()
        {
            CreateFinishVolumePrefab();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/FinishVolume.prefab");

            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                int swapped = 0;
                foreach (WinOnFinish finish in UnityEngine.Object.FindObjectsByType<WinOnFinish>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(finish.gameObject)) continue;

                    Transform old = finish.transform;
                    BoxCollider oldBox = finish.GetComponent<BoxCollider>();

                    Vector3 worldSize = oldBox != null
                        ? Vector3.Scale(oldBox.size, old.lossyScale)
                        : old.lossyScale;

                    GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.parent);
                    replacement.name = old.name;
                    replacement.transform.SetPositionAndRotation(old.position, old.rotation);
                    replacement.transform.localScale = worldSize;

                    UnityEngine.Object.DestroyImmediate(finish.gameObject);
                    swapped++;
                }

                if (swapped > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - " + swapped + " finish volumes given a visual");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Update Element Scene Intro Text")]
        public static void UpdateElementSceneIntroText()
        {
            const string shared =
                "LEVEL ELEMENTS TEST\n\n" +
                "A straight run along one axis, introducing one element at a time. " +
                "Pause > Sections jumps straight to any of them - you respawn there while you keep testing it.\n\n" +
                "1 - BASICS: plain platforms.\n\n" +
                "2 - MOVING PLATFORMS: one slides sideways, one rides up and down. While you aim, a ghost " +
                "of the platform and a blue arrow show where it will be when your shot lands - aim at the ghost.\n\n" +
                "3 - ROTATING WALLS: sticky faces that keep turning. Land on one and it carries you round with it.\n\n" +
                "4 - LASERS: gates that blink on and off. Cross while they are turned off - touching a beam " +
                "knocks you back and drains energy rather than killing you.\n\n";

            const string checkpointsAndFalling =
                "CHECKPOINTS: the blue button on each section pad. Ground pound it, or land on it steeply " +
                "from above, to claim it - it sinks in and turns green while every other checkpoint pops back " +
                "up. That is where you respawn until you claim another, and a claimed button stops blocking " +
                "your launches. Sections you have already passed stay cleared: their enemies do not come back.\n\n" +
                "COLOURS: red means an enemy cannot be killed right now, purple means it can, and a yellow " +
                "flash means an attack is coming.\n\n" +
                "FALLING: if the combo window runs out while you are in the air, the aim is cut short and you " +
                "fall down. Keep landing before the meter empties.\n\n" +
                "Press any button to start.";

            string plainText = shared +
                "5 - GROUND ENEMIES: they wander their platform and leap at you when you are nearby. " +
                "Any launch kills them, so they stay purple.\n\n" +
                "6 - TURRETS: fixed wall enemies that fire a BURST of three shots, then cool down. " +
                "The yellow flash is your warning; each shot leads where you are going.\n\n" +
                "7 - FLYING ENEMIES: they drift over the gaps and shoot on sight.\n\n" +
                checkpointsAndFalling;

            string variantText = shared +
                "5 - HUNTERS: they leap at you from range, even in midair, and dodge shots aimed at them. " +
                "Red while dangerous - the only way in is to survive a leap: a MISSED attack leaves them " +
                "purple and rooted to the spot, unable to dodge. Land the punish before it wears off. " +
                "If their attack connects, they get no such opening.\n\n" +
                "6 - TURRETS: fixed wall enemies that fire a BURST of three shots, then cool down. " +
                "The yellow flash is your warning; each shot leads where you are going.\n\n" +
                "7 - WEAK SPOT FLYERS: they hang nose-down so the pulsing golden spot on their back faces " +
                "upward - that spot is the ONLY thing that kills them. Hit them anywhere else and they are " +
                "staggered instead, slumping forward for a second with the spot presented: come back around " +
                "and take it. They pause after every shot and steer around the walls they patrol.\n\n" +
                checkpointsAndFalling;

            ApplyIntroText("Assets/Scenes/LevelElementsTest.unity", plainText);
            ApplyIntroText("Assets/Scenes/LevelElementsTest2.unity", variantText);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Set BuildInfo Button Hidden")]
        public static void SetBuildInfoButtonHidden()
        {
            const string prefabPath = PrefabFolder + "/PauseSystem.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform button = FindDeep(root.transform, "BuildInfoButton");
                if (button == null) throw new Exception("KineticEnergySetup: BuildInfoButton not found.");
                button.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("KineticEnergySetup: BuildInfo button hidden OK");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest3.unity",
                "Assets/Scenes/SecondLevel.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                int hidden = 0;
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.name != "BuildInfoButton" || !t.gameObject.activeSelf) continue;
                    t.gameObject.SetActive(false);
                    EditorUtility.SetDirty(t.gameObject);
                    hidden++;
                }
                if (hidden > 0)
                {
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                    EditorSceneManager.SaveOpenScenes();
                }
                Debug.Log("KineticEnergySetup: " + scenePath + " - BuildInfo instances hidden: " + hidden);
            }
        }

        [MenuItem("Tools/Kinetic Energy/Build WebGL ItchBuild03")]
        public static void BuildWebGLItch03()
        {
            const string scene = "Assets/Scenes/LevelElementsTest3.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
            {
                throw new Exception("KineticEnergySetup: " + scene + " is missing.");
            }

            PlayerSettings.WebGL.template = "PROJECT:ItchFullscreen";
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            string output = "Builds/ItchBuild03";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            UnityEditor.Build.Reporting.BuildSummary summary = report.summary;
            Debug.Log("WEBBUILD result=" + summary.result
                + " errors=" + summary.totalErrors
                + " sizeMB=" + (summary.totalSize / (1024f * 1024f)).ToString("F1")
                + " output=" + output);
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception("KineticEnergySetup: ItchBuild03 did not succeed.");
            }
        }

        [MenuItem("Tools/Kinetic Energy/Build WebGL ItchBuild02")]
        public static void BuildWebGLItch02()
        {
            const string scene = "Assets/Scenes/LevelElementsTest3.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
            {
                throw new Exception("KineticEnergySetup: " + scene + " is missing.");
            }

            PlayerSettings.WebGL.template = "PROJECT:ItchFullscreen";
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.runInBackground = false;

            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            string output = "Builds/ItchBuild02";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            UnityEditor.Build.Reporting.BuildSummary summary = report.summary;
            Debug.Log("WEBBUILD result=" + summary.result
                + " errors=" + summary.totalErrors
                + " sizeMB=" + (summary.totalSize / (1024f * 1024f)).ToString("F1")
                + " output=" + output);
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception("KineticEnergySetup: ItchBuild02 did not succeed.");
            }
        }

        [MenuItem("Tools/Kinetic Energy/Build WebGL (LevelElementsTest3)")]
        public static void BuildWebGLTest3()
        {
            const string scene = "Assets/Scenes/LevelElementsTest3.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
            {
                throw new Exception("KineticEnergySetup: " + scene + " is missing.");
            }

            PlayerSettings.WebGL.template = "PROJECT:ItchFullscreen";

            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.runInBackground = false;

            string output = "Builds/WebGL_LevelElementsTest3";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            UnityEditor.Build.Reporting.BuildSummary summary = report.summary;
            Debug.Log("WEBBUILD result=" + summary.result
                + " errors=" + summary.totalErrors
                + " sizeMB=" + (summary.totalSize / (1024f * 1024f)).ToString("F1")
                + " output=" + output);
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception("KineticEnergySetup: WebGL build did not succeed.");
            }
        }

        [MenuItem("Tools/Kinetic Energy/Split Basics HUD Steps")]
        public static void SplitBasicsHudSteps()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest3.unity", OpenSceneMode.Single);

            SectionIntroHud hud = UnityEngine.Object.FindAnyObjectByType<SectionIntroHud>(FindObjectsInactive.Include);
            if (hud == null) throw new Exception("KineticEnergySetup: LevelElementsTest3 has no SectionIntroHud.");

            if (hud.sectionTexts.Length > 0)
            {
                hud.sectionTexts[0] =
                    "THE BASICS\n" +
                    "Charge a launch by holding Right Mouse and press Left Mouse to fire.";

                string[] padTexts = new string[hud.sectionTexts.Length];
                padTexts[0] =
                    "THE BASICS\n" +
                    "Charge a launch by holding Left Trigger and press Right Trigger to fire.";
                hud.sectionTextsGamepad = padTexts;
            }

            GameObject secondPlatform = GameObject.Find("BasicsHop1");

            GameObject firstCheckpoint = GameObject.Find("2 - Moving platformsCheckpoint");
            if (secondPlatform == null || firstCheckpoint == null)
            {
                throw new Exception("KineticEnergySetup: BasicsHop1 or the moving-platforms checkpoint is missing.");
            }

            GameObject thirdPlatform = GameObject.Find("BasicsHop2");
            if (thirdPlatform == null) throw new Exception("KineticEnergySetup: BasicsHop2 is missing.");

            hud.proximitySteps = new[]
            {
                new SectionIntroHud.ProximityStep
                {
                    label = "Basics - re-aim",

                    target = secondPlatform.transform,
                    targetEnd = thirdPlatform.transform,
                    radius = 14f,
                    text =
                        "THE BASICS\n" +
                        "When midair, hold the aim button again to slow time and re-aim. " +
                        "Move the Scroll Wheel up/down to add or remove energy. " +
                        "You can also launch upwards by holding and releasing Space.",
                    gamepadText =
                        "THE BASICS\n" +
                        "When midair, hold the aim button again to slow time and re-aim. " +
                        "Move the Right Stick up/down to add or remove energy. " +
                        "You can also launch upwards by holding and releasing A.",
                },
                new SectionIntroHud.ProximityStep
                {
                    label = "Basics - checkpoints",
                    target = firstCheckpoint.transform,
                    radius = 22f,
                    text =
                        "CHECKPOINTS\n" +
                        "Activate one by crashing using a regular launch down or a ground pound by " +
                        "pressing E. You need atleast as much energy as is indicated by the colour of " +
                        "the button, which matches the colour the amount on the energy meter. " +
                        "This also resets your energy meter.",
                    gamepadText =
                        "CHECKPOINTS\n" +
                        "Activate one by crashing using a regular launch down or a ground pound by " +
                        "pressing X. You need atleast as much energy as is indicated by the colour of " +
                        "the button, which matches the colour the amount on the energy meter. " +
                        "This also resets your energy meter.",
                },
            };
            EditorUtility.SetDirty(hud);

            MergedEconomyController economy = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            if (economy != null)
            {
                economy.showIntroOnBoot = false;
                EditorUtility.SetDirty(economy);
            }

            int quieted = 0;
            foreach (EnergyRequirement requirement in UnityEngine.Object.FindObjectsByType<EnergyRequirement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                requirement.buildTickMarks = false;
                requirement.showPercentLabel = false;
                EditorUtility.SetDirty(requirement);
                quieted++;
            }
            foreach (SizedEnemy sized in UnityEngine.Object.FindObjectsByType<SizedEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                sized.showKillLabel = false;
                EditorUtility.SetDirty(sized);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("KineticEnergySetup: basics HUD split into 3 beats OK (re-aim spans "
                + secondPlatform.transform.position.x.ToString("F0") + "-"
                + thirdPlatform.transform.position.x.ToString("F0") + ", checkpoint at "
                + firstCheckpoint.transform.position.x.ToString("F0") + "); intro off, "
                + quieted + " requirement displays quieted");
        }

        [MenuItem("Tools/Kinetic Energy/Update Test3 Intro Text")]
        public static void UpdateTest3IntroText()
        {
            const string text =
                "LEVEL ELEMENTS TEST\n\n" +
                "A straight run along one axis, introducing one element at a time. Pause > Sections jumps " +
                "straight to any of them - you respawn there while you keep testing it.\n\n" +

                "ENERGY IS THE PRICE: every enemy and every checkpoint shows what it costs to kill or claim - " +
                "as a heat colour, as a row of pips (one per 10%), and as a number above it. The scale runs " +
                "cool to hot: ember, amber, golden yellow, pale gold, white-hot. While you charge, the charge " +
                "bar climbs that same scale and a marker on the meter shows where the threshold sits - when " +
                "the bar reaches the marker, the shot can pay for it. Aim at something you cannot afford and " +
                "the trail, cursor and arrow turn red.\n\n" +

                "1 - BASICS: plain platforms.\n\n" +

                "2 - MOVING PLATFORMS: while you aim, a ghost of the platform and a blue arrow show where it " +
                "will be when your shot lands.\n\n" +

                "3 - ROTATING WALLS: sticky faces that keep turning. Land on one and it carries you round with " +
                "it. The thin red edges are not landings - they shove you off and drain energy.\n\n" +

                "4 - LASERS: gates that blink on and off. Cross while they are turned off - touching a beam " +
                "knocks you back and drains energy.\n\n" +

                "5 - HUNTERS: three sizes, costing 20%, 40% and 60% to kill. They leap at you from a distance, " +
                "even in midair, and dodge launches aimed at them. Red means you cannot hurt them yet. If an " +
                "attack MISSES, they stand still wearing their own cost colour and cannot dodge - that is the " +
                "window, and the bigger the enemy the longer it stays open. A hit that CONNECTS earns them no " +
                "such opening. Sharing their platform is enough to be spotted: they stop wandering and hold " +
                "their ground. Knock one off its platform and it launches its own way back.\n\n" +

                "6 - TURRETS: fixed wall enemies that fire a burst of two shots, then cool down. The yellow " +
                "flash is their wind up warning. 40% to destroy.\n\n" +

                "7 - FLYERS: they hang forward with a pulsing spot on their back - that spot is the only place " +
                "that kills them, and only with 40% or more behind the shot. Hit them anywhere else and they " +
                "are knocked out: they turn BLUE and you land square on the middle of that spot, set up to " +
                "ground pound it. The spot is easier to hit while they are out, and they blink blue and red " +
                "just before they come round.\n\n" +

                "CHECKPOINTS: the button on each section pad. Ground pound it or crash steeply from above - " +
                "and it charges energy like everything else, shown on the button itself. Claiming one sets " +
                "your energy to exactly that amount, and so does respawning there. Your combo multiplier " +
                "survives it. Sections you have already passed stay cleared: their enemies do not come back.\n\n" +

                "COMBOS: land somewhere NEW to build the multiplier. Coming straight back to the object you " +
                "just left returns what that launch cost and nothing more - it never raises the chain.\n\n" +

                "FALLING: if the combo window runs out while you are in the air, the aim is cut short and you " +
                "fall down. Keep landing before the meter empties.\n\n" +

                "Press any button to start.";

            ApplyIntroText("Assets/Scenes/LevelElementsTest3.unity", text);
            AssetDatabase.SaveAssets();
        }

        static void ApplyIntroText(string scenePath, string text)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) return;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            MergedEconomyController economy = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            if (economy == null) throw new Exception("KineticEnergySetup: " + scenePath + " has no MergedEconomy.");
            economy.introText = text;
            EditorUtility.SetDirty(economy);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("KineticEnergySetup: intro text updated in " + scenePath);
        }

        [MenuItem("Tools/Kinetic Energy/Swap Flying And Turret Sections")]
        public static void SwapFlyingAndTurretSections()
        {
            string[] flyingObjects =
            {
                "6 - Flying enemiesPad", "6 - Flying enemiesSpawn", "6 - Flying enemiesCheckpoint",
                "FlyStep1", "FlyStep2", "ElementsFlyer1", "ElementsFlyer2", "ElementsFlyer3",
                "FlySideWallA", "FlySideWallB", "FlySideWallC",
            };
            string[] turretObjects =
            {
                "7 - TurretsPad", "7 - TurretsSpawn", "7 - TurretsCheckpoint",
                "TurretRun1", "TurretRun2", "TurretWallLeft", "TurretWallRight",
                "ElementsTurret1", "ElementsTurret2",
            };

            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                GameObject flyingAnchor = GameObject.Find("6 - Flying enemiesSpawn");
                GameObject turretAnchor = GameObject.Find("7 - TurretsSpawn");
                if (flyingAnchor == null || turretAnchor == null)
                {
                    Debug.Log("KineticEnergySetup: " + scenePath + " - no flying/turret pair to swap, skipped");
                    continue;
                }

                Vector3 delta = turretAnchor.transform.position - flyingAnchor.transform.position;
                MoveNamedObjects(flyingObjects, delta);
                MoveNamedObjects(turretObjects, -delta);

                LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
                if (sections != null)
                {
                    int flyingIndex = -1, turretIndex = -1;
                    for (int i = 0; i < sections.sections.Length; i++)
                    {
                        string label = sections.sections[i] != null ? sections.sections[i].label : "";
                        if (label.Contains("Flying")) flyingIndex = i;
                        else if (label.Contains("Turret")) turretIndex = i;
                    }
                    if (flyingIndex >= 0 && turretIndex >= 0)
                    {

                        (sections.sections[flyingIndex], sections.sections[turretIndex])
                            = (sections.sections[turretIndex], sections.sections[flyingIndex]);
                        sections.sections[flyingIndex].label = (flyingIndex + 1) + " - Turrets";
                        sections.sections[turretIndex].label = (turretIndex + 1) + " - Flying enemies";
                        EditorUtility.SetDirty(sections);
                        RebuildSectionsScreenButtons(sections);
                    }
                }

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - flying and turret sections swapped (offset "
                    + delta.x.ToString("F1") + " on x)");
            }
            AssetDatabase.SaveAssets();
        }

        static void MoveNamedObjects(string[] names, Vector3 delta)
        {
            foreach (string name in names)
            {
                GameObject go = GameObject.Find(name);
                if (go == null) continue;
                go.transform.position += delta;
                EditorUtility.SetDirty(go);
            }
        }

        [MenuItem("Tools/Kinetic Energy/Match Turret Telegraph To Hunter")]
        public static void MatchTurretTelegraphToHunter()
        {
            string path = PrefabFolder + "/TurretEnemy.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                TurretEnemy turret = root.GetComponent<TurretEnemy>();
                if (turret == null) throw new Exception("KineticEnergySetup: TurretEnemy prefab has no TurretEnemy component.");
                turret.windUpColor = new Color(1f, 0.93f, 0.32f);
                turret.pulseSpeed = 6f;
                EditorUtility.SetDirty(turret);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: turret telegraph matched to the hunter OK");
        }

        [MenuItem("Tools/Kinetic Energy/Tune WeakSpot Flyer Posture")]
        public static void TuneWeakSpotFlyerPosture()
        {
            string path = PrefabFolder + "/WeakSpotFlyer.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                FlyingEnemy flyer = root.GetComponent<FlyingEnemy>();
                if (flyer == null) throw new Exception("KineticEnergySetup: WeakSpotFlyer has no FlyingEnemy component.");

                flyer.hunchPitchDegrees = 22f;

                flyer.turnSpeed = 4.8f;

                flyer.postFireHoldSeconds = 1f;

                flyer.avoidObstacles = true;
                flyer.obstacleClearance = 3.5f;
                EditorUtility.SetDirty(flyer);

                if (flyer is WeakSpotFlyingEnemy weakSpotFlyer)
                {
                    weakSpotFlyer.pulseSpeed = 1.6f;
                    EditorUtility.SetDirty(weakSpotFlyer);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: WeakSpotFlyer posture tuned OK (hunch 22, turn 4.8, hold 1s)");
        }

        [MenuItem("Tools/Kinetic Energy/Mark Flying Ledges Safe And Ridged")]
        public static void MarkFlyingLedgesSafeAndRidged()
        {
            Material damageMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/DamageWallMaterial.mat");
            if (damageMat == null) throw new Exception("KineticEnergySetup: DamageWallMaterial is missing.");

            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
                Transform fallbackSpawn = sections != null && sections.sections.Length > 0 ? sections.sections[0].spawnPoint : null;

                int marked = 0, ridged = 0;
                foreach (Transform candidate in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    string name = candidate.name;
                    bool isTurretWall = name.StartsWith("TurretWall");
                    bool isSideWall = name.StartsWith("FlySideWall");
                    bool isLedge = name.Contains("Ledge");
                    if (!isTurretWall && !isSideWall && !isLedge) continue;

                    StickySurface marker = candidate.GetComponent<StickySurface>();
                    if (marker == null) marker = candidate.gameObject.AddComponent<StickySurface>();
                    marker.sticky = false;
                    EditorUtility.SetDirty(marker);
                    marked++;

                    if (isTurretWall) continue;
                    AddEdgeDamageShell(candidate, fallbackSpawn, damageMat);
                    ridged++;
                }

                if (sections != null) RefreshSectionHazards(sections);
                if (marked > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - " + marked + " surfaces marked safe, " + ridged + " ridged");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Validate Checkpoints")]
        public static void ValidateCheckpoints()
        {
            foreach (string scenePath in new[]
            {
                "Assets/Scenes/LevelElementsTest.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (Checkpoint cp in UnityEngine.Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Transform root = cp.transform.parent != null ? cp.transform.parent : cp.transform;
                    Debug.Log("CPCHECK " + System.IO.Path.GetFileNameWithoutExtension(scenePath)
                        + " root=" + root.name
                        + " onObject=" + cp.name
                        + " respawn=" + (cp.respawnPoint != null ? cp.respawnPoint.name : "NULL")
                        + " buttonVisual=" + (cp.buttonVisual != null ? cp.buttonVisual.name : "NULL")
                        + " renderer=" + (cp.buttonRenderer != null ? "OK" : "NULL")
                        + " collider=" + (cp.GetComponent<Collider>() != null ? "OK" : "NULL")
                        + " frameSibling=" + (root.Find("Frame") != null ? "OK" : "NULL")
                        + " worldScale=" + root.lossyScale);
                }
            }
        }

        const float DamageShellThickness = 0.5f;

        static void AddDamageShell(Transform platform, Transform respawnPoint, Material material)
        {
            for (int i = platform.childCount - 1; i >= 0; i--)
            {
                if (platform.GetChild(i).name.StartsWith("DamageShell"))
                {
                    UnityEngine.Object.DestroyImmediate(platform.GetChild(i).gameObject);
                }
            }

            Vector3 courseReference = new Vector3(platform.position.x, -1f, 0f);
            Vector3 toCourseLocal = platform.InverseTransformDirection(courseReference - platform.position);
            int landingAxis = 0;
            for (int axis = 1; axis < 3; axis++)
            {
                if (Mathf.Abs(toCourseLocal[axis]) > Mathf.Abs(toCourseLocal[landingAxis])) landingAxis = axis;
            }
            float landingSign = Mathf.Sign(toCourseLocal[landingAxis]);
            float outwardSign = -landingSign;

            Vector3 scale = platform.localScale;
            Vector3 t = new Vector3(
                DamageShellThickness / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                DamageShellThickness / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                DamageShellThickness / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
            float outwardThickness = t[landingAxis];

            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == landingAxis)
                {

                    Vector3 position = Vector3.zero;
                    position[axis] = outwardSign * (0.5f + outwardThickness * 0.5f);
                    Vector3 size = Vector3.one;
                    size[axis] = outwardThickness;
                    CreateDamageSlab(platform, "DamageShell_Outward", position, size, respawnPoint, material);
                    continue;
                }

                int otherAxis = 3 - landingAxis - axis;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 position = Vector3.zero;
                    position[axis] = sign * (0.5f + t[axis] * 0.5f);
                    position[landingAxis] = outwardSign * outwardThickness * 0.5f;
                    Vector3 size = Vector3.one;
                    size[axis] = t[axis];
                    size[landingAxis] = 1f + outwardThickness;
                    size[otherAxis] = 1f + 2f * t[otherAxis];
                    CreateDamageSlab(platform, "DamageShell_" + axis + (sign > 0 ? "P" : "N"),
                        position, size, respawnPoint, material);
                }
            }
        }

        static void AddEdgeDamageShell(Transform platform, Transform respawnPoint, Material material)
        {
            for (int i = platform.childCount - 1; i >= 0; i--)
            {
                if (platform.GetChild(i).name.StartsWith("DamageShell"))
                {
                    UnityEngine.Object.DestroyImmediate(platform.GetChild(i).gameObject);
                }
            }

            Vector3 scale = platform.localScale;
            Vector3 t = new Vector3(
                DamageShellThickness / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                DamageShellThickness / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                DamageShellThickness / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));

            int openAxis = 0;
            if (Mathf.Abs(scale.y) < Mathf.Abs(scale[openAxis])) openAxis = 1;
            if (Mathf.Abs(scale.z) < Mathf.Abs(scale[openAxis])) openAxis = 2;

            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == openAxis) continue;
                int otherAxis = 3 - openAxis - axis;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 position = Vector3.zero;
                    position[axis] = sign * (0.5f + t[axis] * 0.5f);
                    Vector3 size = Vector3.one;
                    size[axis] = t[axis];

                    size[openAxis] = 1f;
                    size[otherAxis] = 1f + 2f * t[otherAxis];
                    CreateDamageSlab(platform, "DamageShell_Edge" + axis + (sign > 0 ? "P" : "N"),
                        position, size, respawnPoint, material);
                }
            }
        }

        static void AddRidgeHazard(GameObject slab)
        {
            DamageWalls lethal = slab.GetComponent<DamageWalls>();
            if (lethal != null) UnityEngine.Object.DestroyImmediate(lethal);

            LaserHazard hazard = slab.GetComponent<LaserHazard>();
            if (hazard == null) hazard = slab.AddComponent<LaserHazard>();
            hazard.knockbackForce = 16.5f;
            hazard.energyDrain = 0.1f;
            hazard.launchLockSeconds = 0.5f;
            hazard.retriggerDelay = 0.6f;
            hazard.upwardBias = 0.35f;
        }

        [MenuItem("Tools/Kinetic Energy/Retune Hazards And Bands")]
        public static void RetuneHazardsAndBands()
        {

            Material beam = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/LaserBeamMaterial.mat");
            if (beam == null) Debug.LogWarning("KineticEnergySetup: LaserBeamMaterial missing - shells cannot share it.");

            EnergyBandPalette palette = EnsureBandPalette();
            EnergyBandPalette defaults = ScriptableObject.CreateInstance<EnergyBandPalette>();
            palette.bands = defaults.bands;
            UnityEngine.Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(palette);

            string[] scenes =
            {
                "Assets/Scenes/LevelElementsTest3.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            };

            foreach (string scenePath in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                int shells = 0;
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!t.name.StartsWith("DamageShell")) continue;
                    Renderer renderer = t.GetComponent<Renderer>();
                    if (renderer != null && beam != null)
                    {
                        renderer.sharedMaterial = beam;
                        EditorUtility.SetDirty(t.gameObject);
                        shells++;
                    }
                }

                int turrets = 0;
                foreach (TurretEnemy turret in UnityEngine.Object.FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    turret.minKillEnergyFraction = 0.4f;
                    EditorUtility.SetDirty(turret);
                    EnergyRequirement requirement = turret.GetComponent<EnergyRequirement>();
                    if (requirement != null)
                    {
                        requirement.requirementPercent = 40;
                        EditorUtility.SetDirty(requirement);
                    }
                    turrets++;
                }

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - " + shells + " shells on beam material, " + turrets + " turrets at 40%");
            }
            AssetDatabase.SaveAssets();

            AssetDatabase.DeleteAsset(MaterialFolder + "/DamageRidgeMaterial.mat");
        }

        [MenuItem("Tools/Kinetic Energy/Retune Damage Ridges")]
        public static void RetuneDamageRidges()
        {

            Material ridgeMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/LaserBeamMaterial.mat");

            string[] scenes =
            {
                "Assets/Scenes/LevelElementsTest3.unity",
                "Assets/Scenes/LevelElementsTest2.unity",
            };

            foreach (string scenePath in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                int converted = 0;
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!t.name.StartsWith("DamageShell")) continue;
                    AddRidgeHazard(t.gameObject);
                    Renderer renderer = t.GetComponent<Renderer>();
                    if (renderer != null) renderer.sharedMaterial = ridgeMaterial;
                    EditorUtility.SetDirty(t.gameObject);
                    converted++;
                }

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: " + scenePath + " - " + converted + " damage ridges retuned OK");
            }
            AssetDatabase.SaveAssets();
        }

        static void CreateDamageSlab(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Transform respawnPoint, Material material)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.localPosition = localPosition;
            slab.transform.localRotation = Quaternion.identity;
            slab.transform.localScale = localScale;
            slab.GetComponent<Renderer>().sharedMaterial = material;

            slab.GetComponent<BoxCollider>().isTrigger = false;

            AddRidgeHazard(slab);
            EditorUtility.SetDirty(slab);
        }

        const string Level1ChallengeInfoText =
            "CHALLENGE RUN - 5 VARIATIONS\n\n" +
            "Reach the finish and the level restarts on the next challenge. Clear all five to win.\n" +
            "Pause > Variants jumps straight to one.\n\n" +
            "1 - LIMITED SLOWDOWN: midair aiming runs on a budget (the blue bar under the combo meter). " +
            "Aim too long and the slow-mo cuts out mid-flight. Every crash refills it.\n" +
            "2 - OVERCHARGE SCATTER: launches drift off target, and the orange ring at your landing spot shows how far. " +
            "The spread follows a square root curve, so the first energy you commit costs the most accuracy.\n" +
            "3 - SEALING WALLS: every platform you land on walls off the gap behind you. There is no way back.\n" +
            "4 - CHASING WALL: a purple wall sweeps the level and SPEEDS UP the longer it runs. Touching it respawns you.\n" +
            "5 - SHRINKING PLATFORMS: each platform is smaller than the last, down to half size at the finish.\n\n" +
            "THE METER: the first 4 blocks (40%) are normal energy. The 6 taller blocks are boosted energy, " +
            "only combo bonuses and the groundpound boost can fill them.\n\n" +
            "COMBOS: a successful launch refunds the energy of your launch(es) times the combo multiplier. " +
            "Relaunch within the window to raise the multiplier - it keeps draining while you fly. " +
            "Landing back on the object you launched from pays nothing.\n" +
            "If you miss the window, your energy reverts to 40% and everything boosted is lost.\n\n" +
            "GROUND POUND: if you groundpound and aim within the slow-mo window, the pound pays back 1.5x what you put in.\n\n" +
            "AIM COLOURS: the dots and cursor turn green where the landing holds you and red where it does not - " +
            "the red shells on the floating walls and the ceiling platform kill on contact.\n\n" +
            "Press any button to start.";

        static void EnsureSceneInBuildSettings(string scenePath)
        {
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == scenePath) return;
            }
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(scenePath, true),
            };
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        const float PauseButtonTopY = 230f;
        const float PauseButtonSpacing = 90f;

        static readonly string[] PauseButtonOrder =
        {
            "ResumeButton", "VariantsButton", "SectionsButton", "RestartButton", "FeedbackButton",
            "CameraSettingsButton", "ControlsButton", "QuitButton", "MainMenuButton",
        };

        static void LayOutPausePanelButtons(Transform pausePanel)
        {
            int slot = 0;
            foreach (string buttonName in PauseButtonOrder)
            {
                Transform button = pausePanel.Find(buttonName);
                if (button == null) continue;
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(0f, PauseButtonTopY - slot * PauseButtonSpacing);
                EditorUtility.SetDirty(button);
                slot++;
            }
        }

        static void BuildCameraSpeedSlider(Transform parent, string name, string label, bool gamepadSlider, Vector2 anchoredPosition, Font font)
        {
            GameObject row = new GameObject(name + "Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = anchoredPosition;
            rowRect.sizeDelta = new Vector2(460f, 60f);

            Text nameLabel = CreateText(name + "Name", row.transform, label, font, 20,
                new Vector2(-60f, 18f), new Vector2(340f, 26f));
            nameLabel.alignment = TextAnchor.MiddleLeft;

            Text valueLabel = CreateText(name + "Value", row.transform, "100%", font, 20,
                new Vector2(190f, 18f), new Vector2(80f, 26f));
            valueLabel.alignment = TextAnchor.MiddleRight;

            GameObject sliderGo = new GameObject(name, typeof(RectTransform));
            sliderGo.transform.SetParent(row.transform, false);
            RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.5f, 0.5f);
            sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.anchoredPosition = new Vector2(0f, -12f);
            sliderRect.sizeDelta = new Vector2(440f, 22f);

            GameObject background = CreateSliderImage("Background", sliderGo.transform, new Color(0f, 0f, 0f, 0.55f));
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.25f);
            backgroundRect.anchorMax = new Vector2(1f, 0.75f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRect.offsetMin = new Vector2(10f, 0f);
            fillAreaRect.offsetMax = new Vector2(-10f, 0f);
            GameObject fill = CreateSliderImage("Fill", fillArea.transform, new Color(1f, 1f, 1f, 0.75f));
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.sizeDelta = new Vector2(20f, 0f);

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(10f, 0f);
            handleAreaRect.offsetMax = new Vector2(-10f, 0f);
            GameObject handle = CreateSliderImage("Handle", handleArea.transform, new Color(1f, 1f, 1f, 0.9f));
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(22f, 30f);

            Slider slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.None;

            CameraSpeedSlider speedSlider = sliderGo.AddComponent<CameraSpeedSlider>();
            speedSlider.gamepadSlider = gamepadSlider;
            speedSlider.valueLabel = valueLabel;
            speedSlider.nameLabel = nameLabel;
            speedSlider.fillImage = fill.GetComponent<Image>();
            speedSlider.handleImage = handle.GetComponent<Image>();
        }

        static GameObject CreateSliderImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            return go;
        }

        [MenuItem("Tools/Kinetic Energy/Expand Aim Trail Dots")]
        public static void ExpandAimTrailDots()
        {
            const int wantDots = 100;
            const float wantSpacing = 1.2f;

            string playerPath = PrefabFolder + "/Player.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                LandingPreviewController preview = root.GetComponentInChildren<LandingPreviewController>(true);
                if (preview == null) throw new Exception("KineticEnergySetup: Player.prefab has no LandingPreviewController.");
                if (preview.trailDots == null || preview.trailDots.Length == 0)
                {
                    throw new Exception("KineticEnergySetup: the trail dot pool is empty - nothing to clone from.");
                }

                var dots = new List<Transform>(preview.trailDots);
                Transform template = dots[dots.Count - 1];
                Transform container = template.parent;
                int added = 0;
                while (dots.Count < wantDots)
                {
                    GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, container);
                    clone.name = "Dot" + dots.Count;
                    clone.transform.localPosition = template.localPosition;
                    clone.transform.localRotation = template.localRotation;
                    clone.transform.localScale = template.localScale;
                    dots.Add(clone.transform);
                    added++;
                }

                preview.trailDots = dots.ToArray();
                preview.maxDotSpacing = wantSpacing;
                EditorUtility.SetDirty(preview);
                PrefabUtility.SaveAsPrefabAsset(root, playerPath);
                Debug.Log($"KineticEnergySetup: aim trail dots expanded OK ({dots.Count} dots, +{added} new, spacing {wantSpacing})");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Configure Level1Aim1.1 Camera")]
        public static void ConfigureLevel1Aim11Camera()
        {
            const string aimScenePath = "Assets/Scenes/Level1Aim1.1.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(aimScenePath) == null)
            {
                throw new Exception("KineticEnergySetup: Level1Aim1.1.unity does not exist.");
            }
            EditorSceneManager.OpenScene(aimScenePath, OpenSceneMode.Single);

            ThirdPersonOrbitCamera orbit = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (orbit == null) throw new Exception("KineticEnergySetup: Level1Aim1.1 has no ThirdPersonOrbitCamera.");
            orbit.trajectoryFramingEnabled = false;
            EditorUtility.SetDirty(orbit);

            KineticCubeController aimPlayer = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            if (aimPlayer == null) throw new Exception("KineticEnergySetup: Level1Aim1.1 has no Player.");
            aimPlayer.groundedAimCameraFollow = true;
            aimPlayer.groundedAimFollowThreshold = 60f;
            aimPlayer.groundedAimFollowBand = 5f;
            aimPlayer.groundedAimFollowSpeed = 45f;
            EditorUtility.SetDirty(aimPlayer);

            var aimPreview = UnityEngine.Object.FindAnyObjectByType<LandingPreviewController>(FindObjectsInactive.Include);
            if (aimPreview != null)
            {
                aimPreview.landingArrowAvailable = true;
                aimPreview.landingArrowEnabled = true;
                EditorUtility.SetDirty(aimPreview);
            }

            var aimMerged = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            if (aimMerged != null)
            {
                aimMerged.momentumLaunches = true;
                EditorUtility.SetDirty(aimMerged);
            }

            SaveOpenScene(aimScenePath);
            Debug.Log("KineticEnergySetup: Level1Aim1.1 camera configured OK (framing off, grounded 60-65 follow on)");
        }

        [MenuItem("Tools/Kinetic Energy/Configure Level1 Test Scene Huds")]
        public static void ConfigureLevel1TestSceneHuds()
        {
            CreateComboMeterPrefab();
            foreach (string scenePath in new[] { "Assets/Scenes/Level1Economy.unity", "Assets/Scenes/Level1Challenge.unity" })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var merged = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
                if (merged == null) continue;
                merged.showHudTag = false;

                merged.comboMeterDropPixels = 20f;

                merged.introKey = scenePath.Contains("Challenge") ? "level1challenge" : "level1economy";
                merged.introText =
                    "HOW THIS LEVEL'S ENERGY WORKS\n\n" +
                    "Only the merged E economy runs here - no variant switching, and midair launches\n" +
                    "always carry the velocity you aimed with (momentum launches).\n\n" +
                    "THE METER: the first 4 blocks (40%) are normal energy. The 6 taller blocks are\n" +
                    "BOOSTED energy - only combo bonuses and the ground-pound boost can fill them.\n\n" +
                    "COMBOS: a landed launch refunds (first launch + midair relaunches) x the combo\n" +
                    "multiplier, capped at the energy you started the flight with. Relaunch within the\n" +
                    "window to raise the multiplier - the circle by the combo bar always shows what\n" +
                    "your next landing pays (grey while no chain runs). Landing back on the object you\n" +
                    "launched from pays nothing.\n\n" +
                    "MISS THE WINDOW and your energy reverts to 40% - everything boosted is lost.\n\n" +
                    "RECHARGE: below 10%, standing still slowly refills you to 40% (fresh energy shows\n" +
                    "orange, then banks yellow). Completely empty = you cannot launch.\n\n" +
                    "WALLS: a launch from a wall or another object counts as carrying at least a 40%\n" +
                    "launch's speed - more if your previous launch was stronger.\n\n" +
                    "GROUND POUND: pound the ground and aim within the slow-mo window - the pound pays\n" +
                    "back 1.5x what you put in, and it can fill the boosted blocks.\n\n" +
                    "Reach the finish to win.\n\n" +
                    "Press any button to start.";
                EditorUtility.SetDirty(merged);

                var finishLine = UnityEngine.Object.FindAnyObjectByType<FinishLineNextScene>(FindObjectsInactive.Include);
                if (finishLine != null)
                {
                    finishLine.nextSceneName = "";
                    EditorUtility.SetDirty(finishLine);
                    if (finishLine.GetComponent<WinOnFinish>() == null)
                    {
                        finishLine.gameObject.AddComponent<WinOnFinish>();
                    }
                }

                KineticCubeController scenePlayer = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
                if (scenePlayer != null && (scenePlayer.slowdownMeter == null
                    || scenePlayer.slowdownMeter.gameObject.name != "ComboMeter"))
                {
                    Transform meterParent = null;
                    if (scenePlayer.slowdownMeter != null)
                    {
                        meterParent = scenePlayer.slowdownMeter.transform.parent;
                        UnityEngine.Object.DestroyImmediate(scenePlayer.slowdownMeter.gameObject);
                    }
                    else
                    {
                        GameObject ps = GameObject.Find("PauseSystem");
                        meterParent = ps != null ? ps.transform.Find("PauseCanvas") : null;
                    }
                    if (meterParent != null)
                    {
                        GameObject combo = (GameObject)PrefabUtility.InstantiatePrefab(
                            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ComboMeter.prefab"));
                        combo.name = "ComboMeter";
                        combo.transform.SetParent(meterParent, false);
                        scenePlayer.slowdownMeter = combo.GetComponent<EnergyMeterController>();
                        EditorUtility.SetDirty(scenePlayer);
                    }
                }
                SaveOpenScene(scenePath);
            }

            EditorSceneManager.OpenScene("Assets/Scenes/Level8.unity", OpenSceneMode.Single);
            var stages = UnityEngine.Object.FindAnyObjectByType<ChallengeStageController>(FindObjectsInactive.Include);
            if (stages != null)
            {
                stages.showHudTag = true;
                EditorUtility.SetDirty(stages);
                SaveOpenScene("Assets/Scenes/Level8.unity");
            }

            Debug.Log("KineticEnergySetup: Level1 test scene HUDs configured OK (tags off, combo meter raised; Level8 tag restored)");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Economy OTS Scenes")]
        public static void SetupEconomyOtsScenes()
        {
            MakeOtsCopy("Assets/Scenes/QuarryEconomy.unity", "Assets/Scenes/QuarryEconomyOts.unity");
            MakeOtsCopy("Assets/Scenes/QuarryEconomy2.unity", "Assets/Scenes/QuarryEconomy2Ots.unity");
            Debug.Log("KineticEnergySetup: economy OTS scenes setup complete OK");
        }

        static void MakeOtsCopy(string sourcePath, string copyPath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(copyPath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sourcePath) == null)
                {
                    throw new Exception($"KineticEnergySetup: {sourcePath} does not exist.");
                }
                if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
                {
                    throw new Exception($"KineticEnergySetup: copying {sourcePath} to {copyPath} failed.");
                }
            }
            EditorSceneManager.OpenScene(copyPath, OpenSceneMode.Single);

            var cameraVariants = UnityEngine.Object.FindAnyObjectByType<AimCameraVariantController>(FindObjectsInactive.Include);
            if (cameraVariants == null)
            {
                throw new Exception($"KineticEnergySetup: no AimCameraVariantController in {copyPath}.");
            }
            cameraVariants.variantSwitchingEnabled = false;
            cameraVariants.currentVariant = AimCameraVariant.OtsParallaxPip;
            EditorUtility.SetDirty(cameraVariants);

            SaveOpenScene(copyPath);
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 1 Economy Scene")]
        public static void SetupLevel1Economy()
        {
            const string scenePath = "Assets/Scenes/Level1Economy.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Level1ScenePath) == null)
                {
                    throw new Exception("KineticEnergySetup: Level1.unity does not exist.");
                }
                if (!AssetDatabase.CopyAsset(Level1ScenePath, scenePath))
                {
                    throw new Exception("KineticEnergySetup: copying Level1.unity to Level1Economy.unity failed.");
                }
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            KineticCubeController playerController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            if (playerController == null) throw new Exception("KineticEnergySetup: Level1Economy has no Player.");

            var cameraVariants = UnityEngine.Object.FindAnyObjectByType<AimCameraVariantController>(FindObjectsInactive.Include);
            if (cameraVariants == null)
            {
                throw new Exception("KineticEnergySetup: no AimCameraVariantController on the Player - run Setup Aim Camera Variants first.");
            }
            cameraVariants.variantSwitchingEnabled = false;
            cameraVariants.currentVariant = AimCameraVariant.OtsParallaxPip;
            EditorUtility.SetDirty(cameraVariants);

            MergedEconomyController merged = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            if (merged == null)
            {
                merged = new GameObject("MergedEconomy").AddComponent<MergedEconomyController>();
            }
            merged.currentVariant = MergedEconomyVariant.VariantE;
            merged.lockSettings = true;
            merged.momentumLaunches = true;
            merged.premiumBoundaryFraction = 0.4f;
            merged.totalLossKeepFraction = 0.4f;
            merged.showHudTag = false;
            EditorUtility.SetDirty(merged);

            GameObject pauseSystemGo = GameObject.Find("PauseSystem");
            Transform pauseCanvas = pauseSystemGo != null ? pauseSystemGo.transform.Find("PauseCanvas") : null;
            if (pauseCanvas == null) throw new Exception("KineticEnergySetup: Level1Economy has no PauseSystem/PauseCanvas.");

            BuildPremiumMeterVariant(PrefabFolder + "/PremiumEnergyMeter4.prefab", 4);
            if (playerController.energyMeter == null
                || playerController.energyMeter.gameObject.name != "PremiumEnergyMeter4")
            {
                Transform embeddedUi = pauseCanvas.Find("EnergyMeter");
                if (embeddedUi != null) embeddedUi.gameObject.SetActive(false);
                Transform embeddedController = pauseSystemGo.transform.Find("EnergyMeter");
                if (embeddedController != null) embeddedController.gameObject.SetActive(false);
                if (playerController.energyMeter != null) playerController.energyMeter.gameObject.SetActive(false);

                GameObject premium = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PremiumEnergyMeter4.prefab"));
                premium.name = "PremiumEnergyMeter4";
                premium.transform.SetParent(pauseCanvas, false);
                playerController.energyMeter = premium.GetComponent<EnergyMeterController>();
            }

            if (playerController.slowdownMeter == null)
            {
                GameObject slowdownMeter = InstantiatePrefab("SlowdownMeter");
                slowdownMeter.transform.SetParent(pauseCanvas, false);
                playerController.slowdownMeter = slowdownMeter.GetComponent<EnergyMeterController>();
            }
            EditorUtility.SetDirty(playerController);

            var momentumToggle = UnityEngine.Object.FindAnyObjectByType<MomentumLaunchToggle>(FindObjectsInactive.Include);
            if (momentumToggle != null) UnityEngine.Object.DestroyImmediate(momentumToggle.gameObject);

            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: Level 1 Economy scene setup complete OK (merged economy + OTS/landing-window camera)");
        }

        [MenuItem("Tools/Kinetic Energy/Add Aim Intro To QuarryNew")]
        public static void AddAimIntroToQuarryNew()
        {
            const string scenePath = "Assets/Scenes/QuarryNew.unity";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindAnyObjectByType<AimIntroScreen>(FindObjectsInactive.Include) == null)
            {
                GameObject intro = new GameObject("AimVariantIntro");
                intro.AddComponent<AimIntroScreen>();
            }
            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: aim intro added to QuarryNew OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Quarry Challenge Scene")]
        public static void SetupQuarryChallenge()
        {
            const string scenePath = "Assets/Scenes/QuarryChallenge.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                throw new Exception("KineticEnergySetup: QuarryChallenge.unity does not exist - duplicate QuarryNew first.");
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var variants = UnityEngine.Object.FindAnyObjectByType<AimCameraVariantController>(FindObjectsInactive.Include);
            if (variants != null)
            {
                variants.variantSwitchingEnabled = false;
                variants.currentVariant = AimCameraVariant.Baseline;
                EditorUtility.SetDirty(variants);
            }

            var intro = UnityEngine.Object.FindAnyObjectByType<AimIntroScreen>(FindObjectsInactive.Include);
            if (intro != null) UnityEngine.Object.DestroyImmediate(intro.gameObject);

            if (UnityEngine.Object.FindAnyObjectByType<ChallengeVariantController>(FindObjectsInactive.Include) == null)
            {
                GameObject harness = new GameObject("ChallengeVariants");
                harness.AddComponent<ChallengeVariantController>();
            }

            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: quarry challenge scene setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Quarry Aim Controls")]
        public static void SetupQuarryAimControls()
        {
            const string scenePath = "Assets/Scenes/QuarryAim.unity";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var cameraVariants = UnityEngine.Object.FindAnyObjectByType<AimCameraVariantController>(FindObjectsInactive.Include);
            if (cameraVariants != null)
            {
                cameraVariants.variantSwitchingEnabled = false;
                cameraVariants.currentVariant = AimCameraVariant.Baseline;
                EditorUtility.SetDirty(cameraVariants);
            }

            if (UnityEngine.Object.FindAnyObjectByType<ControlSchemeVariantController>(FindObjectsInactive.Include) == null)
            {
                GameObject harness = new GameObject("ControlSchemeVariants");
                harness.AddComponent<ControlSchemeVariantController>();
            }

            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: quarry aim controls setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Quarry Aim Lab Scene")]
        public static void SetupQuarryAimLab()
        {
            const string scenePath = "Assets/Scenes/QuarryAim.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                throw new Exception("KineticEnergySetup: QuarryAim.unity does not exist - duplicate QuarryNew first.");
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            if (UnityEngine.Object.FindAnyObjectByType<AimRefinementSettings>(FindObjectsInactive.Include) == null)
            {
                GameObject settings = new GameObject("AimRefinement");
                settings.AddComponent<AimRefinementSettings>();
            }

            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: quarry aim lab scene setup complete OK");
        }

        static AimCameraPreset LoadOrCreatePreset(string path, Action<AimCameraPreset> initialize)
        {
            AimCameraPreset preset = AssetDatabase.LoadAssetAtPath<AimCameraPreset>(path);
            if (preset != null) return preset;
            preset = ScriptableObject.CreateInstance<AimCameraPreset>();
            initialize(preset);
            AssetDatabase.CreateAsset(preset, path);
            return preset;
        }

        static void UpdateBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(QuarryScenePath, true),
                new EditorBuildSettingsScene(GauntletScenePath, true),
            };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Kinetic Energy/Refresh Player Prefab")]
        public static void RefreshPlayerPrefab()
        {
            string path = PrefabFolder + "/Player.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RemoveMissingScripts(root);
                DestroyChildrenMatching(root.transform, "FacingArrow");
                DestroyChildrenMatching(root.transform, "Ghost");

                KineticCubeController controller = root.GetComponent<KineticCubeController>();
                if (controller == null) throw new Exception("KineticEnergySetup: Player.prefab has no KineticCubeController.");
                ApplyPlayerTuning(controller);

                LandingPreviewController preview = root.GetComponentInChildren<LandingPreviewController>(true);
                if (preview != null)
                {
                    preview.initialMode = PredictionMode.TrailAndCrosshair;
                    controller.landingPreview = preview;

                    Mesh sphereMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
                    if (preview.trailDots != null)
                    {
                        foreach (Transform dot in preview.trailDots)
                        {
                            MeshFilter dotMesh = dot != null ? dot.GetComponent<MeshFilter>() : null;
                            if (dotMesh != null) dotMesh.sharedMesh = sphereMesh;
                        }
                    }
                }
                controller.aimArrow = root.GetComponentInChildren<AimArrowIndicator>(true);

                KineticCubeControllerFreeMove freeMove = root.GetComponent<KineticCubeControllerFreeMove>();
                if (freeMove != null)
                {
                    freeMove.airControlAcceleration = AirControlAcceleration;
                    if (freeMove.visual != null)
                    {
                        MeshFilter visualMesh = freeMove.visual.GetComponentInChildren<MeshFilter>(true);
                        if (visualMesh != null)
                        {
                            visualMesh.sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
                        }
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("KineticEnergySetup: Player prefab refresh complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Refresh PauseSystem Prefab")]
        public static void RefreshPauseSystemPrefab()
        {
            string path = PrefabFolder + "/PauseSystem.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RemoveMissingScripts(root);
                DestroyChildrenMatching(root.transform, "RadialMenu");
                DestroyChildrenMatching(root.transform, "PreviewModeLabel");

                Transform meterUi = root.transform.Find("PauseCanvas/EnergyMeter");
                Transform meterControllerChild = root.transform.Find("EnergyMeter");
                EnergyMeterController meterController = meterControllerChild != null ? meterControllerChild.GetComponent<EnergyMeterController>() : null;
                if (meterUi != null && meterController != null)
                {
                    Transform staleBonus = meterUi.Find("BonusFill");
                    if (staleBonus != null) UnityEngine.Object.DestroyImmediate(staleBonus.gameObject);

                    Image bonusFill = CreateFillBar("BonusFill", meterUi, new Color(1f, 0.55f, 0.1f, 0.95f), 3f);
                    Transform energyFill = meterUi.Find("EnergyFill");
                    if (energyFill != null) bonusFill.transform.SetSiblingIndex(energyFill.GetSiblingIndex());
                    bonusFill.gameObject.SetActive(false);
                    meterController.bonusFillImage = bonusFill;
                }

                Transform scenesPanel = root.transform.Find("PauseCanvas/ScenesPanel");
                if (scenesPanel != null)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Transform legacy = scenesPanel.Find("Scene_" + i + "Button");
                        if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy.gameObject);
                    }

                    PauseController pause = root.GetComponentInChildren<PauseController>(true);
                    Transform backButton = scenesPanel.Find("ScenesBackButton");
                    if (pause != null && backButton != null) pause.firstScenesButton = backButton.gameObject;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("KineticEnergySetup: PauseSystem prefab refresh complete OK");
        }

        static void RemoveMissingScripts(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
        }

        static void DestroyChildrenMatching(Transform root, string nameContains)
        {
            var doomed = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != root && t.name.Contains(nameContains)) doomed.Add(t.gameObject);
            }
            foreach (GameObject go in doomed)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        class CoreRig
        {
            public GameObject player;
            public KineticCubeController controller;
            public KineticCubeControllerFreeMove freeMove;
            public ThirdPersonOrbitCamera orbitCamera;
            public PauseController pauseController;
            public Transform pauseCanvas;
            public GameObject pausePanel;
            public GameObject scenesPanel;
        }

        static CoreRig SpawnCoreRig(Vector3 playerSpawn, (string label, string sceneName, int variant)[] pauseSceneButtons)
        {
            GameObject player = InstantiatePrefab("Player");
            GameObject cameraRig = InstantiatePrefab("ThirdPersonCameraRig");
            GameObject pauseSystem = InstantiatePrefab("PauseSystem");

            player.transform.position = playerSpawn;
            cameraRig.transform.position = playerSpawn + new Vector3(0f, 2.5f, -6f);

            var rig = new CoreRig
            {
                player = player,
                controller = player.GetComponent<KineticCubeController>(),
                freeMove = player.GetComponent<KineticCubeControllerFreeMove>(),
                orbitCamera = cameraRig.GetComponent<ThirdPersonOrbitCamera>(),
            };
            if (rig.controller == null || rig.freeMove == null || rig.orbitCamera == null)
            {
                throw new Exception("KineticEnergySetup: Player/camera prefabs are missing their core components.");
            }

            ApplyPlayerTuning(rig.controller);
            rig.freeMove.airControlAcceleration = AirControlAcceleration;
            rig.controller.cameraTransform = cameraRig.transform;
            rig.controller.cameraOrbit = rig.orbitCamera;
            rig.freeMove.cameraTransform = cameraRig.transform;
            rig.orbitCamera.target = player.transform;
            rig.orbitCamera.lookAction = FindActionReference("Player", "Look");
            rig.orbitCamera.minPitch = -75f;
            rig.orbitCamera.maxPitch = 75f;
            rig.orbitCamera.recenterSpeed = 240f;

            rig.orbitCamera.firstPersonMinPitch = -89f;
            rig.orbitCamera.firstPersonMaxPitch = 89f;
            rig.orbitCamera.framingMaxDeviation = 45f;

            rig.pauseCanvas = pauseSystem.transform.Find("PauseCanvas");
            rig.pausePanel = rig.pauseCanvas?.Find("PausePanel")?.gameObject;
            rig.scenesPanel = rig.pauseCanvas?.Find("ScenesPanel")?.gameObject;
            rig.pauseController = pauseSystem.GetComponentInChildren<PauseController>(true);
            if (rig.pauseCanvas == null || rig.pausePanel == null || rig.scenesPanel == null || rig.pauseController == null)
            {
                throw new Exception("KineticEnergySetup: PauseSystem prefab is missing expected children.");
            }

            Text controlsBody = rig.pauseCanvas.Find("ControlsPanel/ControlsBody")?.GetComponent<Text>();
            rig.controller.controlsPanelBody = controlsBody;

            Transform meterControllerChild = pauseSystem.transform.Find("EnergyMeter");
            EnergyMeterController meter = meterControllerChild != null ? meterControllerChild.GetComponent<EnergyMeterController>() : null;
            if (meter != null) rig.controller.energyMeter = meter;
            AddMeterDividers(rig.pauseCanvas);

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);
            DestroyDirectChildIfExists(rig.pausePanel.transform, "MainMenuButton");
            GameObject menuButton = CreateButton("MainMenuButton", rig.pausePanel.transform, "Main Menu", font, accent, new Vector2(0f, -265f), new Vector2(300f, 70f));
            WireSceneButton(menuButton, rig.pauseController.LoadSceneByName, "MainMenu");

            float buttonY = 100f;
            GameObject firstSceneButton = null;
            for (int i = 0; i < pauseSceneButtons.Length; i++)
            {
                (string label, string sceneName, int variant) = pauseSceneButtons[i];
                GameObject sceneButton = CreateButton("LevelScene_" + i + "Button", rig.scenesPanel.transform, label, font, accent, new Vector2(0f, buttonY), new Vector2(340f, 70f));
                if (variant == 0) WireSceneButton(sceneButton, rig.pauseController.LoadSceneByName, sceneName);
                else if (variant == 1) WireSceneButton(sceneButton, rig.pauseController.LoadSceneVariantA, sceneName);
                else WireSceneButton(sceneButton, rig.pauseController.LoadSceneVariantB, sceneName);
                if (firstSceneButton == null) firstSceneButton = sceneButton;
                buttonY -= 90f;
            }
            if (firstSceneButton != null) rig.pauseController.firstScenesButton = firstSceneButton;

            BuildPlayerShadow(player.transform);
            BuildDirectionalLight();
            BuildGlobalVolume();
            ConfigureShadowDistance(300f);

            EditorUtility.SetDirty(rig.controller);
            EditorUtility.SetDirty(rig.freeMove);
            EditorUtility.SetDirty(rig.orbitCamera);
            EditorUtility.SetDirty(rig.pauseController);
            return rig;
        }

        static GameObject InstantiatePrefab(string name)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + name + ".prefab");
            if (asset == null) throw new Exception($"KineticEnergySetup: prefab missing - {PrefabFolder}/{name}.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = name;
            return instance;
        }

        static void PointCameraAt(CoreRig rig, Vector3 lookTarget)
        {
            GameObject facingGo = new GameObject("CameraStartFacing");
            CameraStartFacing facing = facingGo.AddComponent<CameraStartFacing>();
            GameObject lookPoint = new GameObject("CameraLookAtPoint");
            lookPoint.transform.position = lookTarget;
            lookPoint.transform.SetParent(facingGo.transform, true);
            facing.player = rig.player.transform;
            facing.cameraOrbit = rig.orbitCamera;
            facing.lookAtPoint = lookPoint.transform;
            EditorUtility.SetDirty(facing);
        }

        static void AddMeterDividers(Transform pauseCanvas)
        {
            Transform meter = pauseCanvas.Find("EnergyMeter");
            if (meter == null) throw new Exception("KineticEnergySetup: no PauseCanvas/EnergyMeter to divide.");
            DestroyDirectChildIfExists(meter, "MeterDividers");

            GameObject dividers = new GameObject("MeterDividers", typeof(RectTransform));
            dividers.transform.SetParent(meter, false);
            RectTransform dividersRt = dividers.GetComponent<RectTransform>();
            dividersRt.anchorMin = Vector2.zero;
            dividersRt.anchorMax = Vector2.one;
            dividersRt.offsetMin = Vector2.zero;
            dividersRt.offsetMax = Vector2.zero;

            const float inset = 3f;
            const float meterWidth = 320f;
            float innerWidth = meterWidth - inset * 2f;
            for (int i = 1; i <= 9; i++)
            {
                GameObject line = new GameObject("Divider" + i, typeof(RectTransform));
                line.transform.SetParent(dividers.transform, false);
                RectTransform lineRt = line.GetComponent<RectTransform>();
                lineRt.anchorMin = new Vector2(0f, 0f);
                lineRt.anchorMax = new Vector2(0f, 1f);
                lineRt.pivot = new Vector2(0.5f, 0.5f);
                lineRt.sizeDelta = new Vector2(inset, -inset * 2f);
                lineRt.anchoredPosition = new Vector2(inset + innerWidth * i / 10f, 0f);
                line.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);
            }
        }

        static void AddSlowdownMeter(CoreRig rig)
        {
            AddSlowdownMeterUi(rig.pauseCanvas, rig.controller);
        }

        static void AddSlowdownMeterUi(Transform pauseCanvas, KineticCubeController controller)
        {
            DestroyDirectChildIfExists(pauseCanvas, "SlowdownMeter");
            GameObject container = new GameObject("SlowdownMeter", typeof(RectTransform));
            container.transform.SetParent(pauseCanvas, false);
            RectTransform rt = container.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -66f);
            rt.sizeDelta = new Vector2(320f, 20f);

            const float outline = 3f;
            CreatePanel("Outline", container.transform, new Color(1f, 1f, 1f, 0.9f));
            InsetRect(CreatePanel("Backdrop", container.transform, new Color(0f, 0f, 0f, 0.5f)), outline);
            Image fill = CreateFillBar("BudgetFill", container.transform, new Color(0.35f, 0.9f, 0.95f), outline);

            EnergyMeterController meter = container.AddComponent<EnergyMeterController>();
            meter.energyFillImage = fill;
            controller.slowdownMeter = meter;
            EditorUtility.SetDirty(controller);
        }

        [MenuItem("Tools/Kinetic Energy/Add Slowdown Meter To Level 1")]
        public static void AddSlowdownMeterToLevel1()
        {
            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);
            KineticCubeController controller = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            GameObject pauseSystem = GameObject.Find("PauseSystem");
            Transform pauseCanvas = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas") : null;
            if (controller == null || pauseCanvas == null)
            {
                throw new Exception("KineticEnergySetup: Level1.unity is missing its Player or PauseSystem/PauseCanvas.");
            }
            AddSlowdownMeterUi(pauseCanvas, controller);
            SaveOpenScene(Level1ScenePath);
            Debug.Log("KineticEnergySetup: slowdown meter added to Level 1 OK");
        }

        [MenuItem("Tools/Kinetic Energy/Add Slowdown Meter To QuarryNew")]
        public static void AddSlowdownMeterToQuarryNew()
        {
            const string scenePath = "Assets/Scenes/QuarryNew.unity";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            KineticCubeController controller = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            GameObject pauseSystem = GameObject.Find("PauseSystem");
            Transform pauseCanvas = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas") : null;
            if (controller == null || pauseCanvas == null)
            {
                throw new Exception("KineticEnergySetup: QuarryNew.unity is missing its Player or PauseSystem/PauseCanvas.");
            }

            Transform existing = pauseCanvas.Find("SlowdownMeter");
            if (existing != null && PrefabUtility.IsPartOfPrefabInstance(existing.gameObject))
            {
                controller.slowdownMeter = existing.GetComponent<EnergyMeterController>();
                EditorUtility.SetDirty(controller);
            }
            else
            {
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                GameObject meter = InstantiatePrefab("SlowdownMeter");
                meter.transform.SetParent(pauseCanvas, false);
                controller.slowdownMeter = meter.GetComponent<EnergyMeterController>();
                EditorUtility.SetDirty(controller);
            }
            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: slowdown meter added to QuarryNew OK");
        }

        static Text BuildHudLabel(string rootName, string text, Vector2 anchor, Vector2 anchoredPos, TextAnchor alignment, int fontSize)
        {
            GameObject root = new GameObject(rootName);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(700f, 60f);

            Text label = textGo.AddComponent<Text>();
            label.font = FindBestFont();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.text = text;

            Shadow shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return label;
        }

        const float QuarryScale = 0.25f;

        [MenuItem("Tools/Kinetic Energy/Setup Quarry")]
        public static void SetupQuarry()
        {
            MeasureLaunchDistances(out float L, out float H);

            float realMaxLaunchHeight = H;
            L *= QuarryScale;
            H *= QuarryScale;
            Debug.Log($"KineticEnergySetup: measured launch units L={L:F1}m (max-charge grounded launch at {ReferenceAimPitchDegrees} degrees), H={H:F1}m (max-charge straight-up apex).");

            NewEmptyScene(QuarryScenePath);

            float width = 3f * L;
            float depth = 3f * L;
            float rimHeight = 2.5f * H;

            Material rockFloor = MakeMaterial("QuarryFloorMaterial", new Color(0.42f, 0.52f, 0.42f));
            Material rockWall = MakeMaterial("QuarryWallMaterial", new Color(0.32f, 0.45f, 0.36f));

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));

            GameObject terrain = new GameObject("QuarryTerrain");
            Transform tf = terrain.transform;

            CreateBlock(tf, "QuarryFloor", new Vector3(0f, -1f, 0f), new Vector3(width, 2f, depth), rockFloor);

            GameObject rimWalls = new GameObject("QuarryRimWalls");
            Transform wallsTf = rimWalls.transform;
            const float wallThickness = 8f;
            float wallHeight = rimHeight + 20f;
            float wallCenterY = rimHeight - wallHeight * 0.5f;
            CreateBlock(wallsTf, "WallSouth", new Vector3(0f, wallCenterY, -depth * 0.5f - wallThickness * 0.5f),
                new Vector3(width + wallThickness * 2f, wallHeight, wallThickness), rockWall);
            CreateBlock(wallsTf, "WallNorth", new Vector3(0f, wallCenterY, depth * 0.5f + wallThickness * 0.5f),
                new Vector3(width + wallThickness * 2f, wallHeight, wallThickness), rockWall);
            CreateBlock(wallsTf, "WallWest", new Vector3(-width * 0.5f - wallThickness * 0.5f, wallCenterY, 0f),
                new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f), rockWall);
            CreateBlock(wallsTf, "WallEast", new Vector3(width * 0.5f + wallThickness * 0.5f, wallCenterY, 0f),
                new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f), rockWall);

            float ledgeY = rimHeight * 0.5f;
            Vector3 spawnLedgeCenter = new Vector3(0f, ledgeY - 1f, -depth * 0.5f + 8f);
            CreateBlock(tf, "SpawnLedge", spawnLedgeCenter, new Vector3(16f, 2f, 16f), platformMat);
            Vector3 playerSpawn = spawnLedgeCenter + new Vector3(0f, 1f + 1f, 0f);

            Vector3[] wallInward = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            string[] wallNames = { "South", "North", "West", "East" };
            float[] alongOffsets = { -0.85f * L, 0.05f * L, 0.9f * L };
            float[] baseHeights = { 0.35f * H, 0.8f * H, 1.25f * H };
            for (int wall = 0; wall < 4; wall++)
            {
                Vector3 inward = wallInward[wall];
                Vector3 along = new Vector3(inward.z, 0f, inward.x);
                Vector3 wallFaceCenter = -inward * (1.5f * L - 5f);
                for (int i = 0; i < 3; i++)
                {
                    float height = baseHeights[(i + wall) % 3] + 0.06f * H * wall;
                    CreateBlock(tf, "Wall" + wallNames[wall] + "Platform" + (i + 1),
                        wallFaceCenter + along * alongOffsets[i] + Vector3.up * height,
                        new Vector3(8f, 1f, 8f), platformMat);
                }
            }

            GameObject perches = new GameObject("QuarryPerches");
            Vector3[] perchPositions =
            {
                new Vector3(1.15f * L, 0.55f * H, -0.6f * L),
                new Vector3(-1.15f * L, 0.95f * H, 0.55f * L),
                new Vector3(0.55f * L, 1.35f * H, 1.15f * L),
                new Vector3(-0.55f * L, 1.7f * H, -1.15f * L),
            };
            for (int i = 0; i < perchPositions.Length; i++)
            {
                CreateBlock(perches.transform, "Perch" + (i + 1), perchPositions[i], new Vector3(8f, 1f, 8f), platformMat);
            }

            GameObject cage = new GameObject("BoundaryCage");
            cage.AddComponent<AimPreviewIgnored>();
            float cageTop = rimHeight + realMaxLaunchHeight + 15f;
            float cageWallHeight = cageTop - rimHeight + 8f;
            float cageCenterY = (rimHeight + cageTop) * 0.5f;

            CreateInvisibleBox(cage.transform, "CageCeiling", new Vector3(0f, cageTop + 2f, 0f), new Vector3(width + 40f, 4f, depth + 40f));
            CreateInvisibleBox(cage.transform, "CageSouth", new Vector3(0f, cageCenterY, -depth * 0.5f - 6f), new Vector3(width + 40f, cageWallHeight, 4f));
            CreateInvisibleBox(cage.transform, "CageNorth", new Vector3(0f, cageCenterY, depth * 0.5f + 6f), new Vector3(width + 40f, cageWallHeight, 4f));
            CreateInvisibleBox(cage.transform, "CageWest", new Vector3(-width * 0.5f - 6f, cageCenterY, 0f), new Vector3(4f, cageWallHeight, depth + 40f));
            CreateInvisibleBox(cage.transform, "CageEast", new Vector3(width * 0.5f + 6f, cageCenterY, 0f), new Vector3(4f, cageWallHeight, depth + 40f));

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            rig.controller.slowdownMode = SlowdownMode.Unlimited;

            rig.controller.fallResetY = -200f;
            rig.freeMove.fallResetY = -200f;
            EditorUtility.SetDirty(rig.controller);
            EditorUtility.SetDirty(rig.freeMove);
            PointCameraAt(rig, new Vector3(0f, 0f, 0f));

            Material menuPadMat = MakeMaterial("MenuPadMaterial", new Color(0.95f, 0.6f, 0.15f));
            Vector3 padCenter = spawnLedgeCenter + new Vector3(6f, 1f + 0.1f, -5f);
            CreateBlock(tf, "MenuPadBase", padCenter, new Vector3(3f, 0.2f, 3f), menuPadMat);
            GameObject menuTrigger = new GameObject("MenuPadTrigger");
            menuTrigger.transform.position = padCenter + Vector3.up * 1.5f;
            BoxCollider menuTriggerBox = menuTrigger.AddComponent<BoxCollider>();
            menuTriggerBox.isTrigger = true;
            menuTriggerBox.size = new Vector3(3f, 3f, 3f);
            menuTrigger.AddComponent<FinishLineNextScene>().nextSceneName = "MainMenu";

            Text counterLabel = BuildHudLabel("TargetCounterHud", "Targets: 0", new Vector2(0.5f, 1f), new Vector2(0f, -24f), TextAnchor.UpperCenter, 30);
            TargetSphereCounter counter = counterLabel.transform.parent.gameObject.AddComponent<TargetSphereCounter>();
            counter.label = counterLabel;

            Material sphereMat = MakeMaterial("TargetSphereMaterial", new Color(1f, 0.55f, 0.1f));

            Vector3[] spherePositions =
            {
                new Vector3(0f, 0.35f * H, 0f),
                new Vector3(0.12f * L, 0.8f * H, -0.1f * L),
                new Vector3(-0.1f * L, 1.3f * H, 0.12f * L),
                new Vector3(0f, 1.8f * H, 0f),
                new Vector3(0f, 0.9f * H, -1.25f * L),
                new Vector3(0.2f * L, 1.1f * H, 1.25f * L),
                new Vector3(-1.25f * L, 0.6f * H, 0.2f * L),
                new Vector3(1.25f * L, 1.5f * H, -0.2f * L),
            };

            Vector3 respawnMin = new Vector3(-width * 0.5f + 10f, 4f, -depth * 0.5f + 10f);
            Vector3 respawnMax = new Vector3(width * 0.5f - 10f, Mathf.Min(TargetSphereMaxY, rimHeight - 4f), depth * 0.5f - 10f);
            GameObject spheres = new GameObject("TargetSpheres");
            for (int i = 0; i < spherePositions.Length; i++)
            {
                CreateTargetSphere(spheres.transform, "TargetSphere" + (i + 1), spherePositions[i], sphereMat, counter, respawnMin, respawnMax);
            }

            Text hint = BuildHudLabel("QuarryIntroHud", "Mess around. Stop whenever you want.\n(The orange pad by the spawn returns to the menu.)", new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), TextAnchor.MiddleCenter, 34);
            hint.gameObject.AddComponent<TimedMessage>().displayDuration = 6f;

            SaveOpenScene(QuarryScenePath);
            Debug.Log("KineticEnergySetup: Quarry setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Gauntlet")]
        public static void SetupGauntlet()
        {
            MeasureLaunchDistances(out float L, out float H);

            NewEmptyScene(GauntletScenePath);

            Material platformMat = MakeMaterial("GauntletPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material recoveryMat = MakeMaterial("GauntletRecoveryMaterial", new Color(0.35f, 0.35f, 0.4f));
            Material wallMat = MakeMaterial("GauntletWallMaterial", new Color(0.5f, 0.55f, 0.65f));
            Material stripMat = MakeMaterial("GauntletStickyStripMaterial", new Color(0.25f, 0.9f, 0.45f));
            Material panelMat = MakeMaterial("GauntletTimedPanelMaterial", new Color(0.25f, 0.8f, 0.45f));
            Material finishMat = MakeMaterial("GauntletFinishMaterial", new Color(0.2f, 0.9f, 0.95f));

            float corridorHalfWidth = 0.75f * L;
            float recoveryY = -0.35f * H;
            float fallReset = -0.6f * H;

            GameObject terrain = new GameObject("GauntletPlatforms");
            Transform tf = terrain.transform;

            var beatRegions = new List<(int beat, Vector3 center, Vector3 size)>();

            Vector3 startTop = new Vector3(0.1f * L, 0f, 0f);
            CreateBlock(tf, "Beat1_Start", new Vector3(0.1f * L, -1f, 0f), new Vector3(0.25f * L, 2f, 0.3f * L), platformMat);
            CreateBlock(tf, "Beat1_Landing", new Vector3(0.85f * L, -1f, 0f), new Vector3(0.25f * L, 2f, 0.3f * L), platformMat);
            beatRegions.Add((1, new Vector3(0.1f * L, 4f, 0f), new Vector3(0.25f * L, 10f, 0.3f * L)));
            Vector3 playerSpawn = startTop + new Vector3(-0.05f * L, 1.5f, 0f);

            beatRegions.Add((2, new Vector3(0.85f * L, 4f, 0f), new Vector3(0.25f * L, 10f, 0.3f * L)));
            CreateBlock(tf, "Beat2_LowLedge", new Vector3(1.6f * L, -0.15f * H - 1f, -0.35f * L), new Vector3(0.25f * L, 2f, 0.25f * L), platformMat);
            CreateBlock(tf, "Beat2_HighLedge", new Vector3(1.75f * L, 0.12f * H - 1f, 0.45f * L), new Vector3(8f, 2f, 8f), platformMat);

            Vector3 beat3Start = new Vector3(2.3f * L, 0f, 0f);
            CreateBlock(tf, "Beat3_Start", new Vector3(2.3f * L, -1f, 0f), new Vector3(0.2f * L, 2f, 0.3f * L), platformMat);
            beatRegions.Add((3, new Vector3(2.3f * L, 4f, 0f), new Vector3(0.2f * L, 10f, 0.3f * L)));

            GameObject correctionWall = new GameObject("Beat3_Wall");
            float wallX = 2.9f * L;
            float wallHeight = 0.35f * H;
            CreateBlock(correctionWall.transform, "WallFace", new Vector3(wallX, wallHeight * 0.5f - 0.05f * H, 0f), new Vector3(4f, wallHeight, corridorHalfWidth * 2f), wallMat);

            GameObject strip = CreateBlock(null, "Beat3_StickyStrip",
                new Vector3(wallX - 2.05f, wallHeight * 0.75f - 0.05f * H, 0f), new Vector3(0.3f, 6f, 1.5f), stripMat);
            strip.AddComponent<StickySurface>().sticky = true;

            CreateBlock(tf, "Beat3_Recovery", new Vector3(wallX - 0.06f * L, recoveryY, 0f), new Vector3(0.15f * L, 2f, 0.3f * L), recoveryMat);

            CreateBlock(tf, "Beat4_Start", new Vector3(3.1f * L, -1f, 0f), new Vector3(0.2f * L, 2f, 0.3f * L), platformMat);

            beatRegions.Add((4, new Vector3(3.1f * L, 4f, 0f), new Vector3(0.2f * L, 10f, 0.3f * L)));
            CreateBlock(tf, "Beat4_LandingPad", new Vector3(4.7f * L, -1f, 0f), new Vector3(8f, 2f, 8f), platformMat);

            CreateBlock(tf, "Beat4_Recovery", new Vector3(3.95f * L, recoveryY, 0f), new Vector3(1.4f * L, 2f, 0.5f * L), recoveryMat);

            beatRegions.Add((5, new Vector3(4.7f * L, 4f, 0f), new Vector3(8f, 10f, 8f)));

            GameObject clamp = new GameObject("Beat5_EnergyClamp");
            clamp.transform.position = new Vector3(4.7f * L, 4f, 0f);
            BoxCollider clampBox = clamp.AddComponent<BoxCollider>();
            clampBox.isTrigger = true;
            clampBox.size = new Vector3(10f, 10f, 10f);
            clamp.AddComponent<EnergyClampTrigger>().clampFraction = 0.25f;

            GameObject panel = CreateBlock(null, "Beat5_TimedPanel", new Vector3(5.0f * L, -0.05f * H, 0f), new Vector3(6f, 1f, 6f), panelMat);
            TimedStickyPanel timedPanel = panel.AddComponent<TimedStickyPanel>();
            timedPanel.holdSeconds = 2f;
            CreateBlock(tf, "Beat5_TargetPad", new Vector3(5.35f * L, -1f, 0f), new Vector3(6f, 2f, 6f), platformMat);

            CreateBlock(tf, "Beat5_Recovery", new Vector3(5.05f * L, recoveryY, 0f), new Vector3(0.6f * L, 2f, 0.4f * L), recoveryMat);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            rig.controller.startingEnergyFraction = 1f;
            rig.controller.slowdownMode = SlowdownMode.AimBudget;
            rig.controller.maxLaunchesPerFlight = 3;
            rig.controller.fallResetY = fallReset;
            rig.freeMove.fallResetY = fallReset;
            EditorUtility.SetDirty(rig.controller);
            EditorUtility.SetDirty(rig.freeMove);
            PointCameraAt(rig, new Vector3(0.85f * L, 0f, 0f));
            AddSlowdownMeter(rig);

            Text variantLabel = BuildHudLabel("VariantHud", "", new Vector2(0.5f, 1f), new Vector2(0f, -24f), TextAnchor.UpperCenter, 26);
            GameObject loggerGo = new GameObject("GauntletRunLogger");
            GauntletRunLogger logger = loggerGo.AddComponent<GauntletRunLogger>();
            logger.controller = rig.controller;
            logger.variantLabel = variantLabel;
            EditorUtility.SetDirty(logger);

            GameObject regions = new GameObject("BeatRegions");
            foreach ((int beat, Vector3 center, Vector3 size) in beatRegions)
            {
                GameObject regionGo = new GameObject("BeatRegion" + beat);
                regionGo.transform.SetParent(regions.transform, true);
                regionGo.transform.position = center;
                BoxCollider box = regionGo.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = size;
                GauntletBeatRegion region = regionGo.AddComponent<GauntletBeatRegion>();
                region.beatIndex = beat;
                region.logger = logger;
            }

            CreateBlock(null, "FinishMarker", new Vector3(5.45f * L, 1.5f, 0f), new Vector3(0.5f, 5f, 6f), finishMat);
            GameObject finish = new GameObject("FinishLine");
            finish.transform.position = new Vector3(5.45f * L, 4f, 0f);
            BoxCollider finishBox = finish.AddComponent<BoxCollider>();
            finishBox.isTrigger = true;
            finishBox.size = new Vector3(2f, 10f, corridorHalfWidth * 2f);
            GauntletFinishLine finishLine = finish.AddComponent<GauntletFinishLine>();
            finishLine.logger = logger;
            finishLine.pauseController = rig.pauseController;
            EditorUtility.SetDirty(finishLine);

            SaveOpenScene(GauntletScenePath);
            Debug.Log($"KineticEnergySetup: Gauntlet setup complete OK (L={L:F1}m, H={H:F1}m, budget={AimBudgetSeconds}s, drain={TankDrainPerSecond}/s)");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 1")]
        public static void SetupLevel1()
        {

            EditorSceneManager.OpenScene(QuarryScenePath, OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find the Quarry's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(Level1ScenePath);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level1Course");
            Transform tf = course.transform;

            Vector3 platformSize = new Vector3(10f, 2f, 10f);
            float[] gapFractions = { 0.15f, 0.25f, 0.35f, 0.5f, 0.65f, 0.8f };
            float x = 0f;
            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);
            for (int i = 0; i < gapFractions.Length; i++)
            {
                x += platformSize.x + gapFractions[i] * L;
                CreateBlock(tf, "Platform" + (i + 1), new Vector3(x, -1f, 0f), platformSize, platformMat);
            }

            float wallSpacing = 0.3f * L;
            float wallStartX = x + platformSize.x * 0.5f + 0.25f * L;
            for (int i = 0; i < 3; i++)
            {
                float wallX = wallStartX + i * wallSpacing;
                MakeSticky(CreateBlock(tf, "FloatingWall" + (i + 1),
                    new Vector3(wallX, 7f, 0f), new Vector3(2f, 14f, 10f), platformMat));
            }
            float endX = wallStartX + 3f * wallSpacing + 0.2f * L;
            CreateBlock(tf, "EndPlatform", new Vector3(endX, -1f, 0f), platformSize, platformMat);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(endX * 0.5f, -12f, 0f), new Vector3(endX + 60f, 2f, 80f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = new GameObject("FinishTrigger");
            finish.transform.position = new Vector3(endX, 2f, 0f);
            BoxCollider finishBox = finish.AddComponent<BoxCollider>();
            finishBox.isTrigger = true;
            finishBox.size = new Vector3(4f, 4f, 8f);
            finish.AddComponent<FinishLineNextScene>().nextSceneName = "MainMenu";

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(platformSize.x + gapFractions[0] * L, 0f, 0f));

            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);

            SaveOpenScene(Level1ScenePath);
            Debug.Log($"KineticEnergySetup: Level 1 setup complete OK (L={L:F1}m; gaps "
                + string.Join(", ", Array.ConvertAll(gapFractions, g => (g * L).ToString("F0"))) + "m)");
        }

        [MenuItem("Tools/Kinetic Energy/Enable Gradual Drain In Level 1")]
        public static void EnableGradualDrainInLevel1()
        {
            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);
            KineticCubeController controller = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            if (controller == null) throw new Exception("KineticEnergySetup: no Player in Level1.unity.");
            controller.gradualLaunchDrain = true;
            EditorUtility.SetDirty(controller);
            SaveOpenScene(Level1ScenePath);
            Debug.Log("KineticEnergySetup: gradual launch drain enabled in Level 1 OK");
        }

        [MenuItem("Tools/Kinetic Energy/Enable Wall-Crash Launch Limit In Level 1")]
        public static void EnableWallCrashLimitInLevel1()
        {
            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);
            KineticCubeController controller = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            if (controller == null) throw new Exception("KineticEnergySetup: no Player in Level1.unity.");
            controller.wallCrashLaunchAllowance = 1;
            EditorUtility.SetDirty(controller);
            SaveOpenScene(Level1ScenePath);
            Debug.Log("KineticEnergySetup: wall-crash launch limit enabled in Level 1 OK");
        }

        [MenuItem("Tools/Kinetic Energy/Make HUD Prefabs From Level 1")]
        public static void MakeHudPrefabsFromLevel1()
        {
            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);

            GameObject finishTrigger = GameObject.Find("FinishTrigger");
            if (finishTrigger != null)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(finishTrigger, PrefabFolder + "/FinishTrigger.prefab", InteractionMode.AutomatedAction);
            }

            GameObject shadowGo = GameObject.Find("PlayerShadow");
            if (shadowGo != null)
            {
                PlayerShadow shadow = shadowGo.GetComponent<PlayerShadow>();
                Transform playerRef = shadow != null ? shadow.player : null;
                PrefabUtility.SaveAsPrefabAssetAndConnect(shadowGo, PrefabFolder + "/PlayerShadow.prefab", InteractionMode.AutomatedAction);

                if (shadow != null)
                {
                    shadow.player = playerRef;
                    EditorUtility.SetDirty(shadow);
                }
            }

            GameObject pauseSystem = GameObject.Find("PauseSystem");
            Transform slowdownMeter = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas/SlowdownMeter") : null;
            if (slowdownMeter != null)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(slowdownMeter.gameObject, PrefabFolder + "/SlowdownMeter.prefab", InteractionMode.AutomatedAction);
            }

            SaveOpenScene(Level1ScenePath);
            CreateEnergyMeterPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: HUD prefabs created OK (FinishTrigger, PlayerShadow, SlowdownMeter, EnergyMeter)");
        }

        static void CreateEnergyMeterPrefab()
        {
            GameObject container = new GameObject("EnergyMeter", typeof(RectTransform));
            try
            {
                RectTransform rt = container.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-24f, -24f);
                rt.sizeDelta = new Vector2(320f, 36f);

                const float outline = 3f;
                CreatePanel("Outline", container.transform, new Color(1f, 1f, 1f, 0.9f));
                InsetRect(CreatePanel("Backdrop", container.transform, new Color(0f, 0f, 0f, 0.5f)), outline);
                Image bonusFill = CreateFillBar("BonusFill", container.transform, new Color(1f, 0.55f, 0.1f, 0.95f), outline);
                bonusFill.gameObject.SetActive(false);
                Image energyFill = CreateFillBar("EnergyFill", container.transform, new Color(0.95f, 0.82f, 0.15f), outline);
                Image chargeFill = CreateFillBar("ChargeFill", container.transform, new Color(0.3f, 0.65f, 1f), outline);
                chargeFill.gameObject.SetActive(false);

                GameObject dividers = new GameObject("MeterDividers", typeof(RectTransform));
                dividers.transform.SetParent(container.transform, false);
                RectTransform dividersRt = dividers.GetComponent<RectTransform>();
                dividersRt.anchorMin = Vector2.zero;
                dividersRt.anchorMax = Vector2.one;
                dividersRt.offsetMin = Vector2.zero;
                dividersRt.offsetMax = Vector2.zero;
                float innerWidth = 320f - outline * 2f;
                for (int i = 1; i <= 9; i++)
                {
                    GameObject line = new GameObject("Divider" + i, typeof(RectTransform));
                    line.transform.SetParent(dividers.transform, false);
                    RectTransform lineRt = line.GetComponent<RectTransform>();
                    lineRt.anchorMin = new Vector2(0f, 0f);
                    lineRt.anchorMax = new Vector2(0f, 1f);
                    lineRt.pivot = new Vector2(0.5f, 0.5f);
                    lineRt.sizeDelta = new Vector2(outline, -outline * 2f);
                    lineRt.anchoredPosition = new Vector2(outline + innerWidth * i / 10f, 0f);
                    line.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);
                }

                EnergyMeterController meter = container.AddComponent<EnergyMeterController>();
                meter.energyFillImage = energyFill;
                meter.chargeFillImage = chargeFill;
                meter.bonusFillImage = bonusFill;

                PrefabUtility.SaveAsPrefabAsset(container, PrefabFolder + "/EnergyMeter.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [MenuItem("Tools/Kinetic Energy/Replace HUD Instances With Prefabs")]
        public static void ReplaceHudInstancesWithPrefabs()
        {
            string[] scenePaths = { Level1ScenePath, QuarryScenePath, GauntletScenePath };
            foreach (string scenePath in scenePaths)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                KineticCubeController controller = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
                if (controller == null) continue;

                GameObject oldShadow = GameObject.Find("PlayerShadow");
                if (oldShadow != null && !PrefabUtility.IsPartOfPrefabInstance(oldShadow))
                {
                    PlayerShadow oldComp = oldShadow.GetComponent<PlayerShadow>();
                    GameObject newShadow = InstantiatePrefab("PlayerShadow");
                    newShadow.transform.position = oldShadow.transform.position;
                    PlayerShadow newComp = newShadow.GetComponent<PlayerShadow>();
                    if (newComp != null && oldComp != null)
                    {
                        newComp.player = oldComp.player;
                        newComp.maxDistance = oldComp.maxDistance;
                        newComp.surfaceOffset = oldComp.surfaceOffset;
                        EditorUtility.SetDirty(newComp);
                    }
                    UnityEngine.Object.DestroyImmediate(oldShadow);
                }

                GameObject pauseSystemGo = GameObject.Find("PauseSystem");
                Transform pauseCanvas = pauseSystemGo != null ? pauseSystemGo.transform.Find("PauseCanvas") : null;
                if (pauseCanvas != null)
                {
                    Transform oldMeterUi = pauseCanvas.Find("EnergyMeter");
                    bool oldMeterIsPrefabPart = oldMeterUi != null && PrefabUtility.IsPartOfPrefabInstance(oldMeterUi.gameObject)
                        && !PrefabUtility.IsAnyPrefabInstanceRoot(oldMeterUi.gameObject);
                    if (oldMeterUi != null && oldMeterIsPrefabPart)
                    {
                        int slot = oldMeterUi.GetSiblingIndex();
                        oldMeterUi.gameObject.SetActive(false);
                        Transform oldMeterController = pauseSystemGo.transform.Find("EnergyMeter");
                        if (oldMeterController != null) oldMeterController.gameObject.SetActive(false);

                        GameObject newMeter = InstantiatePrefab("EnergyMeter");
                        newMeter.transform.SetParent(pauseCanvas, false);
                        newMeter.transform.SetSiblingIndex(slot);
                        controller.energyMeter = newMeter.GetComponent<EnergyMeterController>();
                        EditorUtility.SetDirty(controller);
                    }

                    Transform oldSlowdown = pauseCanvas.Find("SlowdownMeter");
                    if (oldSlowdown != null && !PrefabUtility.IsPartOfPrefabInstance(oldSlowdown.gameObject))
                    {
                        int slot = oldSlowdown.GetSiblingIndex();
                        UnityEngine.Object.DestroyImmediate(oldSlowdown.gameObject);
                        GameObject newSlowdown = InstantiatePrefab("SlowdownMeter");
                        newSlowdown.transform.SetParent(pauseCanvas, false);
                        newSlowdown.transform.SetSiblingIndex(slot);
                        controller.slowdownMeter = newSlowdown.GetComponent<EnergyMeterController>();
                        EditorUtility.SetDirty(controller);
                    }
                }

                foreach (FinishLineNextScene oldFinish in UnityEngine.Object.FindObjectsByType<FinishLineNextScene>(FindObjectsInactive.Include))
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(oldFinish.gameObject)) continue;
                    GameObject oldGo = oldFinish.gameObject;
                    BoxCollider oldBox = oldGo.GetComponent<BoxCollider>();
                    Vector3 position = oldGo.transform.position;
                    string nextScene = oldFinish.nextSceneName;

                    GameObject newFinish = InstantiatePrefab("FinishTrigger");
                    newFinish.name = oldGo.name;
                    newFinish.transform.position = position;
                    FinishLineNextScene newComp = newFinish.GetComponent<FinishLineNextScene>();
                    if (newComp != null) newComp.nextSceneName = nextScene;
                    BoxCollider newBox = newFinish.GetComponent<BoxCollider>();
                    if (newBox != null && oldBox != null)
                    {
                        newBox.size = oldBox.size;
                        newBox.center = oldBox.center;
                    }
                    EditorUtility.SetDirty(newFinish);
                    UnityEngine.Object.DestroyImmediate(oldGo);
                }

                SaveOpenScene(scenePath);
            }
            Debug.Log("KineticEnergySetup: HUD prefab instance replacement complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Fix HUD Prefab Layout Propagation")]
        public static void FixHudPrefabLayoutPropagation()
        {
            RestructureMeterPrefab(PrefabFolder + "/EnergyMeter.prefab");
            RestructureMeterPrefab(PrefabFolder + "/SlowdownMeter.prefab");

            string[] scenePaths = { Level1ScenePath, QuarryScenePath, GauntletScenePath };
            foreach (string scenePath in scenePaths)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                GameObject pauseSystemGo = GameObject.Find("PauseSystem");
                Transform pauseCanvas = pauseSystemGo != null ? pauseSystemGo.transform.Find("PauseCanvas") : null;
                if (pauseCanvas == null) continue;

                bool changed = false;
                foreach (string meterName in new[] { "EnergyMeter", "SlowdownMeter" })
                {
                    foreach (Transform child in pauseCanvas)
                    {
                        if (child.name != meterName || !PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)) continue;
                        RectTransform rt = child as RectTransform;
                        if (rt == null) continue;
                        rt.anchoredPosition = Vector2.zero;
                        rt.sizeDelta = Vector2.zero;
                        EditorUtility.SetDirty(rt);
                        changed = true;
                    }
                }
                if (changed) SaveOpenScene(scenePath);
            }
            Debug.Log("KineticEnergySetup: HUD prefab layout propagation fixed OK (edit the prefabs' Body child from now on)");
        }

        static void RestructureMeterPrefab(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (root.transform.Find("Body") != null) return;

                RectTransform rootRt = root.GetComponent<RectTransform>();
                GameObject body = new GameObject("Body", typeof(RectTransform));
                RectTransform bodyRt = body.GetComponent<RectTransform>();
                body.transform.SetParent(root.transform, false);

                bodyRt.anchorMin = rootRt.anchorMin;
                bodyRt.anchorMax = rootRt.anchorMax;
                bodyRt.pivot = rootRt.pivot;
                bodyRt.anchoredPosition = rootRt.anchoredPosition;
                bodyRt.sizeDelta = rootRt.sizeDelta;

                var toMove = new List<Transform>();
                foreach (Transform child in root.transform)
                {
                    if (child != body.transform) toMove.Add(child);
                }
                foreach (Transform child in toMove)
                {
                    child.SetParent(body.transform, false);
                }

                rootRt.anchoredPosition = Vector2.zero;
                rootRt.sizeDelta = Vector2.zero;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/Kinetic Energy/Create MovingPlatform Prefab")]
        public static void CreateMovingPlatformPrefab()
        {
            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.name = "MovingPlatform";
                go.transform.localScale = new Vector3(8f, 1f, 8f);
                go.GetComponent<Renderer>().sharedMaterial = platformMat;
                go.AddComponent<MovingPlatform>();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/MovingPlatform.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: MovingPlatform prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Apply Moving Platform Material")]
        public static void ApplyMovingPlatformMaterial()
        {
            Material moverMat = MakeMaterial("MovingPlatformMaterial", new Color(0.16f, 0.58f, 0.56f));
            string path = PrefabFolder + "/MovingPlatform.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Renderer renderer = root.GetComponentInChildren<Renderer>(true);
                if (renderer != null) renderer.sharedMaterial = moverMat;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: moving platform material applied OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 2")]
        public static void SetupLevel2()
        {

            EditorSceneManager.OpenScene(QuarryScenePath, OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find the Quarry's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateMovingPlatformPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(Level2ScenePath);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level2Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(10f, 2f, 10f);

            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);
            CreateBlock(tf, "Static1", new Vector3(0.3f * L, -1f, 0f), platformSize, platformMat);
            CreateBlock(tf, "Static2", new Vector3(0.95f * L, -1f, 0.3f * L), platformSize, platformMat);
            CreateBlock(tf, "Static3", new Vector3(1.75f * L, -1f, 0.3f * L), platformSize, platformMat);
            CreateBlock(tf, "EndPlatform", new Vector3(2.3f * L, -1f, 0.3f * L), platformSize, platformMat);

            SpawnMovingPlatform("Mover1_SidewaysFerry", new Vector3(0.62f * L, -1f, 0f), new Vector3(0f, 0f, 0.3f * L), 7f);
            SpawnMovingPlatform("Mover2_PathShuttle", new Vector3(1.25f * L, -1f, 0.3f * L), new Vector3(0.25f * L, 0f, 0f), 5f);
            SpawnMovingPlatform("Mover3_Lift", new Vector3(2.05f * L, -1f, 0.3f * L), new Vector3(0f, 14f, 0f), 6f);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(1.15f * L, -12f, 0.15f * L), new Vector3(2.3f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(2.3f * L, 2f, 0.3f * L);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.3f * L, 0f, 0f));

            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);

            SaveOpenScene(Level2ScenePath);
            Debug.Log($"KineticEnergySetup: Level 2 setup complete OK (L={L:F1}m, 3 movers)");
        }

        [MenuItem("Tools/Kinetic Energy/Create Enemy Prefab")]
        public static void CreateEnemyPrefab()
        {
            Material enemyMat = MakeMaterial("EnemyMaterial", new Color(0.72f, 0.15f, 0.6f));
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                go.name = "Enemy";
                go.transform.localScale = Vector3.one * 2f;
                go.GetComponent<Renderer>().sharedMaterial = enemyMat;
                go.AddComponent<Enemy>();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/Enemy.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Enemy prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Update Enemy Prefab Speed")]
        public static void UpdateEnemyPrefabSpeed()
        {
            string path = PrefabFolder + "/Enemy.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Enemy enemy = root.GetComponent<Enemy>();
                if (enemy != null) enemy.moveSpeed = 4.5f;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: enemy prefab speed updated OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 3")]
        public static void SetupLevel3()
        {
            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 1's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateEnemyPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(Level3ScenePath);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level3Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(10f, 2f, 10f);

            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);
            CreateBlock(tf, "Arena", new Vector3(0.5f * L, -1f, 0f), new Vector3(0.45f * L, 2f, 0.45f * L), platformMat);
            SpawnEnemy("ArenaEnemy1", new Vector3(0.42f * L, 1f, -6f), EnemyWanderMode.WithinRadius, 10f, 1.5f);
            SpawnEnemy("ArenaEnemy2", new Vector3(0.58f * L, 1f, 6f), EnemyWanderMode.WithinRadius, 12f, 1.5f);

            CreateBlock(tf, "Hop1", new Vector3(0.95f * L, -1f, 0.1f * L), new Vector3(14f, 2f, 14f), platformMat);
            SpawnEnemy("Hop1Enemy", new Vector3(0.95f * L, 1f, 0.1f * L), EnemyWanderMode.PlatformSurface, 8f, 1.5f);
            CreateBlock(tf, "Hop2", new Vector3(1.35f * L, -1f, -0.05f * L), new Vector3(14f, 2f, 14f), platformMat);
            SpawnEnemy("Hop2Enemy", new Vector3(1.35f * L, 1f, -0.05f * L), EnemyWanderMode.PlatformSurface, 8f, 2f);

            CreateBlock(tf, "EndPlatform", new Vector3(1.7f * L, -1f, 0f), platformSize, platformMat);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(0.85f * L, -12f, 0f), new Vector3(1.7f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(1.7f * L, 2f, 0f);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.5f * L, 0f, 0f));

            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);

            SaveOpenScene(Level3ScenePath);
            Debug.Log($"KineticEnergySetup: Level 3 setup complete OK (L={L:F1}m, 4 enemies)");
        }

        [MenuItem("Tools/Kinetic Energy/Create Flying Enemy Prefab")]
        public static void CreateFlyingEnemyPrefab()
        {
            string path = PrefabFolder + "/FlyingEnemy.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            temp.name = "FlyingEnemy";
            temp.transform.localScale = Vector3.one * 1.6f;
            Material material = MakeMaterial("FlyingEnemyMaterial", new Color(0.72f, 0.2f, 0.55f));
            temp.GetComponent<Renderer>().sharedMaterial = material;
            temp.AddComponent<FlyingEnemy>();
            PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            Debug.Log("KineticEnergySetup: FlyingEnemy prefab created OK");
        }

        static T SwapForSubclass<T>(Component source) where T : Component
        {
            T replacement = source.gameObject.AddComponent<T>();
            var src = new SerializedObject(source);
            var dst = new SerializedObject(replacement);
            SerializedProperty property = src.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.propertyPath != "m_Script") dst.CopyFromSerializedProperty(property);
                } while (property.NextVisible(false));
            }
            dst.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Object.DestroyImmediate(source);
            return replacement;
        }

        [MenuItem("Tools/Kinetic Energy/Create Sized Enemy Prefab")]
        public static void CreateSizedEnemyPrefab()
        {
            string path = PrefabFolder + "/SizedEnemy.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Debug.Log("KineticEnergySetup: SizedEnemy prefab already exists OK");
                return;
            }

            GameObject baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Enemy.prefab");
            if (baseAsset == null) throw new Exception("KineticEnergySetup: Enemy.prefab missing - the sized variant inherits from it.");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "SizedEnemy";

            SwapForSubclass<SizedEnemy>(instance.GetComponent<Enemy>());

            PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
            Debug.Log("KineticEnergySetup: SizedEnemy prefab created OK (size class per instance)");
        }

        [MenuItem("Tools/Kinetic Energy/Create Weak Spot Flyer Prefab")]
        public static void CreateWeakSpotFlyerPrefab()
        {
            string path = PrefabFolder + "/WeakSpotFlyer.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Debug.Log("KineticEnergySetup: WeakSpotFlyer prefab already exists OK");
                return;
            }

            CreateFlyingEnemyPrefab();
            GameObject baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/FlyingEnemy.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "WeakSpotFlyer";

            WeakSpotFlyingEnemy weak = SwapForSubclass<WeakSpotFlyingEnemy>(instance.GetComponent<FlyingEnemy>());

            Material weakMat = MakeMaterial("WeakSpotMaterial", new Color(1f, 0.85f, 0.2f));
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "WeakSpot";
            cube.transform.SetParent(instance.transform, false);
            cube.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            cube.transform.localScale = Vector3.one * 0.42f;
            cube.GetComponent<Renderer>().sharedMaterial = weakMat;
            weak.weakSpot = cube.GetComponent<Collider>();

            PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
            Debug.Log("KineticEnergySetup: WeakSpotFlyer prefab created OK (back-cube kill spot)");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 4")]
        public static void SetupLevel4()
        {
            const string level4Path = "Assets/Scenes/Level4.unity";

            EditorSceneManager.OpenScene(Level3ScenePath, OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 3's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateFlyingEnemyPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(level4Path);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level4Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(12f, 2f, 12f);

            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);

            CreateBlock(tf, "IslandA", new Vector3(0.45f * L, -1f, 0.05f * L), new Vector3(16f, 2f, 16f), platformMat);
            SpawnFlyingEnemy("GapFlyer", new Vector3(0.22f * L, 10f, 0f), 8f, 20f);

            CreateBlock(tf, "IslandB", new Vector3(0.9f * L, -1f, -0.08f * L), new Vector3(16f, 2f, 16f), platformMat);
            SpawnFlyingEnemy("MidFlyer", new Vector3(0.68f * L, 12f, -0.02f * L), 10f, 24f);
            SpawnFlyingEnemy("HighFlyer", new Vector3(0.9f * L, 16f, -0.08f * L), 8f, 22f);

            CreateBlock(tf, "EndPlatform", new Vector3(1.3f * L, -1f, 0f), platformSize, platformMat);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(0.65f * L, -12f, 0f), new Vector3(1.3f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(1.3f * L, 2f, 0f);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.45f * L, 0f, 0f));

            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);

            GameObject pauseSystemGo = GameObject.Find("PauseSystem");
            Transform pauseCanvas = pauseSystemGo != null ? pauseSystemGo.transform.Find("PauseCanvas") : null;
            if (pauseCanvas != null)
            {
                Transform embeddedUi = pauseCanvas.Find("EnergyMeter");
                if (embeddedUi != null && !PrefabUtility.IsAnyPrefabInstanceRoot(embeddedUi.gameObject)) embeddedUi.gameObject.SetActive(false);
                Transform embeddedController = pauseSystemGo.transform.Find("EnergyMeter");
                if (embeddedController != null) embeddedController.gameObject.SetActive(false);

                GameObject energyMeter = InstantiatePrefab("EnergyMeter");
                energyMeter.transform.SetParent(pauseCanvas, false);
                rig.controller.energyMeter = energyMeter.GetComponent<EnergyMeterController>();
                GameObject slowdownMeter = InstantiatePrefab("SlowdownMeter");
                slowdownMeter.transform.SetParent(pauseCanvas, false);
                rig.controller.slowdownMeter = slowdownMeter.GetComponent<EnergyMeterController>();
                EditorUtility.SetDirty(rig.controller);
            }

            SaveOpenScene(level4Path);
            Debug.Log($"KineticEnergySetup: Level 4 setup complete OK (L={L:F1}m, 3 flying enemies)");
        }

        static void SpawnSizedEnemy(string name, Vector3 position, EnemySizeClass sizeClass, EnemyWanderMode mode, float radius)
        {
            GameObject instance = InstantiatePrefab("SizedEnemy");
            instance.name = name;
            instance.transform.position = position;
            SizedEnemy enemy = instance.GetComponent<SizedEnemy>();
            enemy.sizeClass = sizeClass;
            enemy.wanderMode = mode;
            enemy.wanderRadius = radius;
            EditorUtility.SetDirty(enemy);
        }

        static void SpawnWeakSpotFlyer(string name, Vector3 position, float radius, float detection)
        {
            GameObject instance = InstantiatePrefab("WeakSpotFlyer");
            instance.name = name;
            instance.transform.position = position;
            FlyingEnemy flyer = instance.GetComponent<FlyingEnemy>();
            flyer.flyRadius = radius;
            flyer.detectionRadius = detection;
            EditorUtility.SetDirty(flyer);
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 9")]
        public static void SetupLevel9()
        {
            const string level9Path = "Assets/Scenes/Level9.unity";

            EditorSceneManager.OpenScene("Assets/Scenes/Level7.unity", OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 7's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateSizedEnemyPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(level9Path);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level9Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(12f, 2f, 12f);

            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);

            CreateBlock(tf, "SmallArena", new Vector3(0.35f * L, -1f, 0.05f * L), new Vector3(18f, 2f, 18f), platformMat);
            SpawnSizedEnemy("SmallEnemy", new Vector3(0.35f * L, 1f, 0.05f * L), EnemySizeClass.Small, EnemyWanderMode.PlatformSurface, 10f);

            CreateBlock(tf, "MediumArena", new Vector3(0.65f * L, 2f, -0.06f * L), new Vector3(18f, 2f, 18f), platformMat);
            SpawnSizedEnemy("MediumEnemy", new Vector3(0.65f * L, 4f, -0.06f * L), EnemySizeClass.Medium, EnemyWanderMode.PlatformSurface, 10f);

            CreateBlock(tf, "LargeArena", new Vector3(0.95f * L, 5f, 0.03f * L), new Vector3(22f, 2f, 22f), platformMat);
            SpawnSizedEnemy("LargeEnemy", new Vector3(0.95f * L, 7f, 0.03f * L), EnemySizeClass.Large, EnemyWanderMode.PlatformSurface, 11f);
            SpawnSizedEnemy("PestEnemy", new Vector3(0.92f * L, 7f, -0.02f * L), EnemySizeClass.Small, EnemyWanderMode.WithinRadius, 8f);

            CreateBlock(tf, "EndPlatform", new Vector3(1.25f * L, 5f, 0f), platformSize, platformMat);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(0.62f * L, -14f, 0f), new Vector3(1.25f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(1.25f * L, 8f, 0f);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.35f * L, 0f, 0f));
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);
            WireStandaloneMeters(rig);

            SaveOpenScene(level9Path);
            Debug.Log($"KineticEnergySetup: Level 9 setup complete OK (L={L:F1}m, sized enemies small/medium/large+pest)");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 10")]
        public static void SetupLevel10()
        {
            const string level10Path = "Assets/Scenes/Level10.unity";

            EditorSceneManager.OpenScene("Assets/Scenes/Level4.unity", OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 4's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateWeakSpotFlyerPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(level10Path);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level10Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(12f, 2f, 12f);

            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);

            CreateBlock(tf, "IslandA", new Vector3(0.4f * L, 4f, 0.06f * L), new Vector3(16f, 2f, 16f), platformMat);
            SpawnWeakSpotFlyer("LowFlyer", new Vector3(0.2f * L, 6f, 0.02f * L), 7f, 20f);

            CreateBlock(tf, "IslandB", new Vector3(0.8f * L, 10f, -0.06f * L), new Vector3(16f, 2f, 16f), platformMat);
            SpawnWeakSpotFlyer("MidFlyer", new Vector3(0.6f * L, 12f, -0.02f * L), 8f, 22f);

            CreateBlock(tf, "IslandC", new Vector3(1.15f * L, 16f, 0.02f * L), new Vector3(18f, 2f, 18f), platformMat);
            SpawnWeakSpotFlyer("HighFlyer", new Vector3(0.98f * L, 18f, 0f), 8f, 22f);

            CreateBlock(tf, "EndPlatform", new Vector3(1.45f * L, 16f, 0f), platformSize, platformMat);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(0.72f * L, -12f, 0f), new Vector3(1.45f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(1.45f * L, 19f, 0f);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.4f * L, 4f, 0f));
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);
            WireStandaloneMeters(rig);

            SaveOpenScene(level10Path);
            Debug.Log($"KineticEnergySetup: Level 10 setup complete OK (L={L:F1}m, 3 weak-spot flyers)");
        }

        [MenuItem("Tools/Kinetic Energy/Create Hunter Enemy Prefab")]
        public static void CreateHunterEnemyPrefab()
        {
            string path = PrefabFolder + "/HunterEnemy.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Enemy existingHunter = root.GetComponent<Enemy>();
                    if (existingHunter != null
                        && (!existingHunter.dodgePlayerLaunches || existingHunter.killWindow != EnemyKillWindow.WhileCoolingDown))
                    {
                        existingHunter.dodgePlayerLaunches = true;
                        existingHunter.killWindow = EnemyKillWindow.WhileCoolingDown;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        Debug.Log("KineticEnergySetup: HunterEnemy prefab updated (dodge + cooldown kill window) OK");
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                return;
            }

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            temp.name = "HunterEnemy";
            temp.transform.localScale = Vector3.one * 2f;
            Material material = MakeMaterial("HunterEnemyMaterial", new Color(0.8f, 0.2f, 0.15f));
            temp.GetComponent<Renderer>().sharedMaterial = material;
            Enemy hunter = temp.AddComponent<Enemy>();
            hunter.attackAirbornePlayers = true;
            hunter.returnLaunchToPlatform = true;
            hunter.dodgePlayerLaunches = true;
            hunter.killWindow = EnemyKillWindow.WhileCoolingDown;
            hunter.detectionRadius = 20f;
            hunter.moveSpeed = 4.5f;
            PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            Debug.Log("KineticEnergySetup: HunterEnemy prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Create Stalker Enemy Prefab")]
        public static void CreateStalkerEnemyPrefab()
        {
            string path = PrefabFolder + "/StalkerEnemy.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            temp.name = "StalkerEnemy";
            temp.transform.localScale = Vector3.one * 2f;
            Material material = MakeMaterial("StalkerEnemyMaterial", new Color(0.45f, 0.12f, 0.4f));
            temp.GetComponent<Renderer>().sharedMaterial = material;
            Enemy stalker = temp.AddComponent<Enemy>();
            stalker.attackAirbornePlayers = true;
            stalker.returnLaunchToPlatform = true;
            stalker.dodgePlayerLaunches = true;
            stalker.killWindow = EnemyKillWindow.WhileWindingUp;
            stalker.detectionRadius = 20f;
            stalker.moveSpeed = 4.5f;
            PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            Debug.Log("KineticEnergySetup: StalkerEnemy prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 7")]
        public static void SetupLevel7()
        {
            const string level7Path = "Assets/Scenes/Level7.unity";

            EditorSceneManager.OpenScene("Assets/Scenes/Level6.unity", OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 6's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateHunterEnemyPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(level7Path);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level7Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(12f, 2f, 12f);

            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);

            CreateBlock(tf, "StepA", new Vector3(0.35f * L, -1f, 0.04f * L), new Vector3(18f, 2f, 18f), platformMat);
            SpawnHunter("HunterA", new Vector3(0.35f * L, 1f, 0.04f * L), EnemyWanderMode.PlatformSurface, 10f);

            CreateBlock(tf, "StepB", new Vector3(0.62f * L, 3f, -0.08f * L), new Vector3(18f, 2f, 18f), platformMat);
            SpawnHunter("HunterB", new Vector3(0.62f * L, 5f, -0.08f * L), EnemyWanderMode.PlatformSurface, 10f);

            CreateBlock(tf, "StepC", new Vector3(0.9f * L, 7f, 0.02f * L), new Vector3(20f, 2f, 20f), platformMat);
            SpawnHunter("HunterC", new Vector3(0.9f * L, 9f, 0.02f * L), EnemyWanderMode.WithinRadius, 8f);

            CreateBlock(tf, "EndPlatform", new Vector3(1.25f * L, 7f, 0f), platformSize, platformMat);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(0.62f * L, -14f, 0f), new Vector3(1.25f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(1.25f * L, 10f, 0f);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.35f * L, 0f, 0f));
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);
            WireStandaloneMeters(rig);

            SaveOpenScene(level7Path);
            Debug.Log($"KineticEnergySetup: Level 7 setup complete OK (L={L:F1}m, 3 hunters)");
        }

        static void SpawnHunter(string name, Vector3 position, EnemyWanderMode mode, float radius)
        {
            GameObject instance = InstantiatePrefab("HunterEnemy");
            instance.name = name;
            instance.transform.position = position;
            Enemy hunter = instance.GetComponent<Enemy>();
            hunter.wanderMode = mode;
            hunter.wanderRadius = radius;
            EditorUtility.SetDirty(hunter);
        }

        static Material MakeTransparentMaterial(string assetName, Color color)
        {
            Material mat = MakeMaterial(assetName, color);
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("Tools/Kinetic Energy/Create Death Wall Prefab")]
        public static void CreateDeathWallPrefab()
        {
            string path = PrefabFolder + "/DeathWall.prefab";
            Material material = MakeTransparentMaterial("DeathWallMaterial", new Color(0.55f, 0.15f, 0.85f, 0.45f));

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    root.GetComponent<Renderer>().sharedMaterial = material;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                Debug.Log("KineticEnergySetup: DeathWall prefab restyled OK");
                return;
            }

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.name = "DeathWall";
            temp.GetComponent<Renderer>().sharedMaterial = material;
            temp.GetComponent<BoxCollider>().isTrigger = true;
            Rigidbody rb = temp.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            temp.AddComponent<DeathWall>();
            PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            Debug.Log("KineticEnergySetup: DeathWall prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 8")]
        public static void SetupLevel8()
        {
            const string level8Path = "Assets/Scenes/Level8.unity";

            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 1's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateDeathWallPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(level8Path);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level8Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(10f, 2f, 10f);

            float[] gapFractions = { 0.15f, 0.25f, 0.35f, 0.5f, 0.65f, 0.8f };
            var platforms = new List<Transform>();
            platforms.Add(CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat).transform);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);
            float x = 0f;
            for (int i = 0; i < gapFractions.Length; i++)
            {
                x += platformSize.x + gapFractions[i] * L;
                string name = i == gapFractions.Length - 1 ? "EndPlatform" : "Platform" + (i + 1);
                platforms.Add(CreateBlock(tf, name, new Vector3(x, -1f, 0f), platformSize, platformMat).transform);
            }
            float endX = x;

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(endX * 0.5f, -12f, 0f), new Vector3(endX + 80f, 2f, 80f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject chaseGo = InstantiatePrefab("DeathWall");
            chaseGo.name = "ChaseWall";
            chaseGo.transform.position = new Vector3(-1.2f * L, 13f, 0f);
            chaseGo.transform.localScale = new Vector3(2f, 46f, 70f);
            DeathWall chase = chaseGo.GetComponent<DeathWall>();
            chase.moveSpeed = 4f;
            chase.moveDirection = Vector3.right;
            EditorUtility.SetDirty(chase);

            GameObject finish = new GameObject("ChallengeFinish");
            finish.transform.position = new Vector3(endX, 2f, 0f);
            BoxCollider finishBox = finish.AddComponent<BoxCollider>();
            finishBox.isTrigger = true;
            finishBox.size = new Vector3(4f, 4f, 8f);
            finish.AddComponent<ChallengeFinishTrigger>();

            GameObject stagesGo = new GameObject("ChallengeStages");
            ChallengeStageController stages = stagesGo.AddComponent<ChallengeStageController>();
            stages.chaseWall = chase;
            stages.sealWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/DeathWall.prefab");
            stages.coursePlatforms = platforms.ToArray();
            stages.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(stages);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);
            Text columnTitle = CreateText("ChallengeColumnTitle", rig.scenesPanel.transform,
                "Challenges (restarts Level 8)", font, 24, new Vector2(380f, 170f), new Vector2(380f, 40f));
            columnTitle.color = new Color(1f, 1f, 1f, 0.8f);
            string[] stageLabels = { "1 - Limited slowdown", "2 - Overcharge scatter", "3 - Chasing wall", "4 - Sealing walls" };
            UnityEngine.Events.UnityAction<string>[] stageCalls =
            {
                rig.pauseController.LoadChallengeStage1,
                rig.pauseController.LoadChallengeStage2,
                rig.pauseController.LoadChallengeStage3,
                rig.pauseController.LoadChallengeStage4,
            };
            float stageButtonY = 100f;
            for (int i = 0; i < stageLabels.Length; i++)
            {
                GameObject stageButton = CreateButton("ChallengeStage_" + (i + 1) + "Button",
                    rig.scenesPanel.transform, stageLabels[i], font, accent, new Vector2(380f, stageButtonY), new Vector2(340f, 70f));
                WireSceneButton(stageButton, stageCalls[i], "Level8");
                stageButtonY -= 90f;
            }

            PointCameraAt(rig, new Vector3(platformSize.x + gapFractions[0] * L, 0f, 0f));
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);
            WireStandaloneMeters(rig);

            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(s => s.path == level8Path))
            {
                buildScenes.Add(new EditorBuildSettingsScene(level8Path, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }

            SaveOpenScene(level8Path);
            Debug.Log($"KineticEnergySetup: Level 8 setup complete OK (L={L:F1}m, 4 challenge stages)");
        }

        [MenuItem("Tools/Kinetic Energy/Create Turret Prefab")]
        public static void CreateTurretPrefab()
        {
            string path = PrefabFolder + "/TurretEnemy.prefab";

            Material material = MakeMaterial("EnemyMaterial", new Color(0.72f, 0.15f, 0.6f));

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    root.GetComponent<Renderer>().sharedMaterial = material;
                    TurretEnemy existingTurret = root.GetComponent<TurretEnemy>();
                    if (existingTurret != null) existingTurret.windUpColor = new Color(1f, 0.35f, 0.1f);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                Debug.Log("KineticEnergySetup: TurretEnemy prefab restyled to enemy colours OK");
                return;
            }

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            temp.name = "TurretEnemy";
            temp.transform.localScale = new Vector3(1.4f, 0.9f, 1.4f);
            temp.GetComponent<Renderer>().sharedMaterial = material;
            TurretEnemy turret = temp.AddComponent<TurretEnemy>();
            turret.windUpColor = new Color(1f, 0.35f, 0.1f);
            PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            Debug.Log("KineticEnergySetup: TurretEnemy prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 5")]
        public static void SetupLevel5()
        {
            const string level5Path = "Assets/Scenes/Level5.unity";

            EditorSceneManager.OpenScene("Assets/Scenes/Level4.unity", OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 4's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            CreateTurretPrefab();
            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(level5Path);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material wallMat = MakeMaterial("GauntletWallMaterial", new Color(0.5f, 0.55f, 0.65f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level5Course");
            Transform tf = course.transform;
            Vector3 platformSize = new Vector3(12f, 2f, 12f);
            float corridorHalfWidth = 16f;

            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);

            CreateBlock(tf, "Hop1", new Vector3(0.4f * L, -1f, 0f), new Vector3(14f, 2f, 14f), platformMat);
            CreateBlock(tf, "Hop2", new Vector3(0.8f * L, -1f, 0.06f * L), new Vector3(14f, 2f, 14f), platformMat);
            CreateBlock(tf, "EndPlatform", new Vector3(1.2f * L, -1f, 0f), platformSize, platformMat);

            float wallLength = 1.3f * L;
            CreateBlock(tf, "WallLeft", new Vector3(0.6f * L, 8f, -corridorHalfWidth), new Vector3(wallLength, 20f, 2f), wallMat);
            CreateBlock(tf, "WallRight", new Vector3(0.6f * L, 8f, corridorHalfWidth), new Vector3(wallLength, 20f, 2f), wallMat);

            SpawnTurret("WallTurretLeft", new Vector3(0.35f * L, 8f, -corridorHalfWidth + 1.2f), new Vector3(-90f, 0f, 0f));
            SpawnTurret("WallTurretRight", new Vector3(0.85f * L, 9f, corridorHalfWidth - 1.2f), new Vector3(90f, 0f, 0f));

            CreateBlock(tf, "TurretPedestal", new Vector3(0.6f * L, 1f, -0.05f * L), new Vector3(3f, 6f, 3f), wallMat);
            SpawnTurret("PedestalTurret", new Vector3(0.6f * L, 4.9f, -0.05f * L), Vector3.zero);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(0.6f * L, -12f, 0f), new Vector3(1.2f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(1.2f * L, 2f, 0f);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.4f * L, 0f, 0f));
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);
            WireStandaloneMeters(rig);

            SaveOpenScene(level5Path);
            Debug.Log($"KineticEnergySetup: Level 5 setup complete OK (L={L:F1}m, 3 turrets)");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Level 6")]
        public static void SetupLevel6()
        {
            const string level6Path = "Assets/Scenes/Level6.unity";

            EditorSceneManager.OpenScene("Assets/Scenes/Level5.unity", OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 5's Player/camera to copy tuning from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            MeasureLaunchDistances(out float L, out float H);
            NewEmptyScene(level6Path);

            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material damageMat = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));

            GameObject course = new GameObject("Level6Course");
            Transform tf = course.transform;

            float runwayLength = 1.1f * L;
            CreateBlock(tf, "Runway", new Vector3(runwayLength * 0.5f, -1f, 0f), new Vector3(runwayLength + 12f, 2f, 24f), platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);

            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;

            CreateLaserGate(tf, "Gate1", new Vector3(0.3f * runwayLength, 0f, 0f), 24f, 12f, 1.5f, 1.5f, 0f, respawnPoint.transform);
            CreateLaserGate(tf, "Gate2", new Vector3(0.6f * runwayLength, 0f, 0f), 24f, 12f, 1.5f, 1.5f, 1.5f, respawnPoint.transform);
            CreateLaserGate(tf, "Gate3", new Vector3(0.85f * runwayLength, 0f, 0f), 24f, 12f, 1f, 1f, 0.75f, respawnPoint.transform);

            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(runwayLength * 0.5f, -12f, 0f), new Vector3(runwayLength + 60f, 2f, 100f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(runwayLength, 2f, 0f);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.3f * runwayLength, 0f, 0f));
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);
            WireStandaloneMeters(rig);

            SaveOpenScene(level6Path);
            Debug.Log($"KineticEnergySetup: Level 6 setup complete OK (L={L:F1}m, 3 laser gates)");
        }

        static void SpawnTurret(string name, Vector3 position, Vector3 eulerRotation)
        {
            GameObject instance = InstantiatePrefab("TurretEnemy");
            instance.name = name;
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(eulerRotation);
        }

        [MenuItem("Tools/Kinetic Energy/Create Laser Gate Prefab")]
        public static void CreateLaserGatePrefab()
        {
            string path = PrefabFolder + "/LaserGate.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            Material columnMat = MakeMaterial("LaserColumnMaterial", new Color(0.45f, 0.45f, 0.5f));
            Material beamMat = MakeMaterial("LaserBeamMaterial", new Color(0.95f, 0.08f, 0.05f));
            const float half = 12f;
            const float columnHeight = 12f;

            GameObject gate = new GameObject("LaserGate");
            CreateBlock(gate.transform, "ColumnA", new Vector3(0f, columnHeight * 0.5f, -half), new Vector3(2f, columnHeight, 2f), columnMat);
            CreateBlock(gate.transform, "ColumnB", new Vector3(0f, columnHeight * 0.5f, half), new Vector3(2f, columnHeight, 2f), columnMat);

            GameObject barsRoot = new GameObject("Beams");
            barsRoot.transform.SetParent(gate.transform, false);
            Rigidbody barsBody = barsRoot.AddComponent<Rigidbody>();
            barsBody.isKinematic = true;
            barsBody.useGravity = false;
            barsRoot.AddComponent<DamageWalls>();

            LaserWall laser = gate.AddComponent<LaserWall>();
            laser.barsRoot = barsRoot;
            laser.beamHalfLength = half - 1f;
            laser.beamMaterial = beamMat;

            PrefabUtility.SaveAsPrefabAsset(gate, path);
            UnityEngine.Object.DestroyImmediate(gate);
            Debug.Log("KineticEnergySetup: LaserGate prefab created OK");
        }

        static void CreateLaserGate(Transform parent, string name, Vector3 centre, float width, float columnHeight,
            float onSeconds, float offSeconds, float phaseOffset, Transform respawnPoint)
        {
            CreateLaserGatePrefab();
            GameObject gate = InstantiatePrefab("LaserGate");
            gate.name = name;
            gate.transform.SetParent(parent, false);
            gate.transform.position = centre;

            LaserWall laser = gate.GetComponent<LaserWall>();
            laser.onSeconds = onSeconds;
            laser.offSeconds = offSeconds;
            laser.phaseOffset = phaseOffset;
            EditorUtility.SetDirty(laser);

            DamageWalls barsDamage = gate.GetComponentInChildren<DamageWalls>(true);
            if (barsDamage != null)
            {
                barsDamage.respawnPoint = respawnPoint;
                EditorUtility.SetDirty(barsDamage);
            }
        }

        static void WireStandaloneMeters(CoreRig rig)
        {
            GameObject pauseSystemGo = GameObject.Find("PauseSystem");
            Transform pauseCanvas = pauseSystemGo != null ? pauseSystemGo.transform.Find("PauseCanvas") : null;
            if (pauseCanvas == null) return;

            Transform embeddedUi = pauseCanvas.Find("EnergyMeter");
            if (embeddedUi != null && !PrefabUtility.IsAnyPrefabInstanceRoot(embeddedUi.gameObject)) embeddedUi.gameObject.SetActive(false);
            Transform embeddedController = pauseSystemGo.transform.Find("EnergyMeter");
            if (embeddedController != null) embeddedController.gameObject.SetActive(false);

            GameObject energyMeter = InstantiatePrefab("EnergyMeter");
            energyMeter.transform.SetParent(pauseCanvas, false);
            rig.controller.energyMeter = energyMeter.GetComponent<EnergyMeterController>();
            GameObject slowdownMeter = InstantiatePrefab("SlowdownMeter");
            slowdownMeter.transform.SetParent(pauseCanvas, false);
            rig.controller.slowdownMeter = slowdownMeter.GetComponent<EnergyMeterController>();
            EditorUtility.SetDirty(rig.controller);
        }

        static void SpawnFlyingEnemy(string name, Vector3 position, float radius, float detection)
        {
            GameObject instance = InstantiatePrefab("FlyingEnemy");
            instance.name = name;
            instance.transform.position = position;
            FlyingEnemy flyer = instance.GetComponent<FlyingEnemy>();
            flyer.flyRadius = radius;
            flyer.detectionRadius = detection;
            EditorUtility.SetDirty(flyer);
        }

        static void SpawnEnemy(string name, Vector3 position, EnemyWanderMode mode, float radius, float margin)
        {
            GameObject instance = InstantiatePrefab("Enemy");
            instance.name = name;
            instance.transform.position = position;
            Enemy enemy = instance.GetComponent<Enemy>();
            enemy.wanderMode = mode;
            enemy.wanderRadius = radius;
            enemy.edgeMargin = margin;
            EditorUtility.SetDirty(enemy);
        }

        [MenuItem("Tools/Kinetic Energy/Copy Player Values Level 1 -> Level 2")]
        public static void CopyPlayerValuesLevel1ToLevel2()
        {
            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);
            KineticCubeController sourceController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove sourceMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera sourceCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (sourceController == null || sourceMove == null || sourceCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 1's Player/camera to copy from.");
            }
            string controllerJson = EditorJsonUtility.ToJson(sourceController);
            string moveJson = EditorJsonUtility.ToJson(sourceMove);
            string cameraJson = EditorJsonUtility.ToJson(sourceCamera);

            EditorSceneManager.OpenScene(Level2ScenePath, OpenSceneMode.Single);
            KineticCubeController targetController = UnityEngine.Object.FindAnyObjectByType<KineticCubeController>(FindObjectsInactive.Include);
            KineticCubeControllerFreeMove targetMove = UnityEngine.Object.FindAnyObjectByType<KineticCubeControllerFreeMove>(FindObjectsInactive.Include);
            ThirdPersonOrbitCamera targetCamera = UnityEngine.Object.FindAnyObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (targetController == null || targetMove == null || targetCamera == null)
            {
                throw new Exception("KineticEnergySetup: could not find Level 2's Player/camera to copy onto.");
            }
            OverwriteSerializedValuesKeepObjectRefs(targetController, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(targetMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(targetCamera, cameraJson);
            SaveOpenScene(Level2ScenePath);
            Debug.Log("KineticEnergySetup: player values copied Level 1 -> Level 2 OK");
        }

        static void SpawnMovingPlatform(string name, Vector3 position, Vector3 moveOffset, float lapSeconds)
        {
            GameObject instance = InstantiatePrefab("MovingPlatform");
            instance.name = name;
            instance.transform.position = position;
            MovingPlatform mover = instance.GetComponent<MovingPlatform>();
            mover.moveOffset = moveOffset;
            mover.lapSeconds = lapSeconds;
            EditorUtility.SetDirty(mover);
        }

        static void OverwriteSerializedValuesKeepObjectRefs(Component target, string sourceJson)
        {
            var savedRefs = new List<(string path, UnityEngine.Object value)>();
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.propertyPath != "m_Script")
                {
                    savedRefs.Add((property.propertyPath, property.objectReferenceValue));
                }
            }

            EditorJsonUtility.FromJsonOverwrite(sourceJson, target);

            serialized = new SerializedObject(target);
            foreach ((string path, UnityEngine.Object value) in savedRefs)
            {
                SerializedProperty restored = serialized.FindProperty(path);
                if (restored != null) restored.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        static (string label, string sceneName, int variant)[] LevelPauseButtons()
        {
            return new[]
            {
                ("The Quarry", "Quarry", 0),
                ("Gauntlet - Variant A", "Gauntlet", 1),
                ("Gauntlet - Variant B", "Gauntlet", 2),
            };
        }

        [MenuItem("Tools/Kinetic Energy/Setup Main Menu")]
        public static void SetupMainMenu()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);

            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            GameObject canvasGo = new GameObject("MenuCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject menuPanel = CreatePanel("MenuPanel", canvasGo.transform, new Color(0.08f, 0.09f, 0.11f, 1f));
            CreateText("Title", menuPanel.transform, "KINETIC ENERGY", font, 64, new Vector2(0f, 320f), new Vector2(900f, 90f));
            Text subtitle = CreateText("Subtitle", menuPanel.transform, "Two test levels", font, 30, new Vector2(0f, 250f), new Vector2(900f, 50f));
            subtitle.color = new Color(1f, 1f, 1f, 0.7f);

            GameObject quarryBtn = CreateButton("QuarryButton", menuPanel.transform, "Level 1 - The Quarry", font, accent, new Vector2(0f, 130f), new Vector2(460f, 70f));
            GameObject gauntletABtn = CreateButton("GauntletAButton", menuPanel.transform, "Level 2 - The Gauntlet (Variant A)", font, accent, new Vector2(0f, 30f), new Vector2(460f, 70f));
            GameObject gauntletBBtn = CreateButton("GauntletBButton", menuPanel.transform, "Level 2 - The Gauntlet (Variant B)", font, accent, new Vector2(0f, -70f), new Vector2(460f, 70f));
            GameObject quitBtn = CreateButton("QuitButton", menuPanel.transform, "Quit", font, accent, new Vector2(0f, -190f), new Vector2(300f, 70f));

            Text blurb = CreateText("Blurb", menuPanel.transform,
                "The Quarry - free play, infinite energy, no goals. Mess around.\n" +
                "The Gauntlet - a five-beat course; play BOTH variants back to back.\n" +
                "Variant A: aiming midair spends a separate 2s budget (refills on crash).\n" +
                "Variant B: aiming midair drains your energy tank instead.",
                font, 24, new Vector2(0f, -330f), new Vector2(1000f, 160f));
            blurb.color = new Color(1f, 1f, 1f, 0.75f);

            GameObject controllerGo = new GameObject("MainMenuUI");
            MainMenuController menu = controllerGo.AddComponent<MainMenuController>();
            menu.menuPanel = menuPanel;
            menu.firstMenuButton = quarryBtn;

            WireSceneButton(quarryBtn, menu.LoadSceneByName, "Quarry");
            WireSceneButton(gauntletABtn, menu.LoadSceneVariantA, "Gauntlet");
            WireSceneButton(gauntletBBtn, menu.LoadSceneVariantB, "Gauntlet");
            WireButton(quitBtn, menu.OnQuitClicked);
            EditorUtility.SetDirty(menu);

            SaveOpenScene(MainMenuScenePath);
            Debug.Log("KineticEnergySetup: main menu setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Add Feedback Button To Main Menu")]
        public static void AddFeedbackButtonToMainMenu()
        {
            EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            MainMenuController menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>(FindObjectsInactive.Include);
            GameObject menuPanel = menu != null ? menu.menuPanel : null;
            if (menu == null || menuPanel == null)
            {
                throw new Exception("KineticEnergySetup: MainMenu.unity is missing its MainMenuController/MenuPanel.");
            }

            Transform existing = menuPanel.transform.Find("FeedbackButton");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);
            GameObject feedbackBtn = CreateButton("FeedbackButton", menuPanel.transform, "Feedback", font, accent,
                new Vector2(340f, -190f), new Vector2(300f, 70f));
            WireButton(feedbackBtn, menu.OnFeedbackClicked);
            EditorUtility.SetDirty(menu);

            SaveOpenScene(MainMenuScenePath);
            Debug.Log("KineticEnergySetup: feedback button added to main menu OK");
        }

        static void NewEmptyScene(string path)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
            SaveOpenScene(path);
        }

        static void SaveOpenScene(string path)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new Exception($"KineticEnergySetup: failed to save scene {path}");
            }
            AssetDatabase.SaveAssets();
        }

        static GameObject CreateBlock(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static GameObject CreateRotatedBlock(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material)
        {
            GameObject go = CreateBlock(parent, name, center, size, material);
            go.transform.rotation = rotation;
            return go;
        }

        static GameObject MakeSticky(GameObject go)
        {
            go.AddComponent<StickySurface>().sticky = true;
            return go;
        }

        static GameObject CreateInvisibleBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            return go;
        }

        const float TargetSphereMaxY = 64f;

        static void CreateTargetSphere(Transform parent, string name, Vector3 position, Material material, TargetSphereCounter counter, Vector3 respawnMin, Vector3 respawnMax)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, true);
            position.y = Mathf.Min(position.y, TargetSphereMaxY);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 2.25f;
            go.GetComponent<Renderer>().sharedMaterial = material;

            TargetSphere sphere = go.AddComponent<TargetSphere>();
            sphere.counter = counter;
            sphere.respawnAreaMin = respawnMin;
            sphere.respawnAreaMax = respawnMax;
            EditorUtility.SetDirty(sphere);
        }

        static void ConfigureShadowDistance(float distance)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var pipelineAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (pipelineAsset != null && !Mathf.Approximately(pipelineAsset.shadowDistance, distance))
                {
                    pipelineAsset.shadowDistance = distance;
                    EditorUtility.SetDirty(pipelineAsset);
                }
            }
            AssetDatabase.SaveAssets();
        }

        static void BuildDirectionalLight()
        {
            GameObject lightGo = GameObject.Find("Directional Light");
            if (lightGo == null)
            {
                lightGo = new GameObject("Directional Light");
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            Light light = lightGo.GetComponent<Light>();
            if (light == null) light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;

            light.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(light);
        }

        static void BuildGlobalVolume()
        {
            if (GameObject.Find("Global Volume") != null) return;

            GameObject volumeGo = new GameObject("Global Volume");
            UnityEngine.Rendering.Volume volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;

            UnityEngine.Rendering.VolumeProfile profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumeProfilePath);
            if (profile != null) volume.sharedProfile = profile;
        }

        static void BuildPlayerShadow(Transform player)
        {
            GameObject existing = GameObject.Find("PlayerShadow");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            GameObject shadowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PlayerShadow.prefab");
            if (shadowPrefab != null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(shadowPrefab);
                instance.name = "PlayerShadow";
                PlayerShadow prefabShadow = instance.GetComponent<PlayerShadow>();
                if (prefabShadow != null)
                {
                    prefabShadow.player = player;
                    EditorUtility.SetDirty(prefabShadow);
                }
                return;
            }

            GameObject shadowGo = new GameObject("PlayerShadow");
            PlayerShadow shadowScript = shadowGo.AddComponent<PlayerShadow>();

            GameObject visualGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visualGo.name = "ShadowVisual";
            visualGo.transform.SetParent(shadowGo.transform, false);
            UnityEngine.Object.DestroyImmediate(visualGo.GetComponent<Collider>());
            visualGo.transform.localScale = new Vector3(1.6f, 0.02f, 1.6f);

            Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
            Material shadowMat = new Material(FindUnlitShader());
            shadowMat.color = shadowColor;
            MakeTransparent(shadowMat, shadowColor.a);
            shadowMat = SaveMaterialAsset(shadowMat, "PlayerShadowMaterial");
            visualGo.GetComponent<Renderer>().sharedMaterial = shadowMat;
            visualGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            shadowScript.player = player;
            shadowScript.shadowVisual = visualGo.transform;
            shadowScript.maxDistance = 500f;
            shadowScript.surfaceOffset = 0.02f;
            EditorUtility.SetDirty(shadowScript);
        }

        static Material MakeMaterial(string assetName, Color color)
        {
            Material mat = new Material(FindBestShader());
            mat.color = color;
            return SaveMaterialAsset(mat, assetName);
        }

        static Material SaveMaterialAsset(Material mat, string name)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {

                UnityEngine.Object.DestroyImmediate(mat);
                return existing;
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
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

        static Shader FindUnlitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = FindBestShader();
            return shader;
        }

        static Shader FindBestShader()
        {
            string[] candidates =
            {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Standard",
                "Diffuse"
            };

            foreach (string name in candidates)
            {
                Shader shader = Shader.Find(name);
                if (shader != null) return shader;
            }

            throw new Exception("KineticEnergySetup: no usable shader found.");
        }

        static Font FindBestFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        static GameObject CreatePanel(string name, Transform parent, Color backgroundColor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.AddComponent<Image>().color = backgroundColor;
            return go;
        }

        static GameObject InsetRect(GameObject go, float inset)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return go;
        }

        static Text CreateText(string name, Transform parent, string content, Font font, int fontSize, Vector2 anchoredPos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = content;
            return text;
        }

        static GameObject CreateButton(string name, Transform parent, string label, Font font, Color accentColor, Vector2 anchoredPos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.color = accentColor;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.selectedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;

            Text text = CreateText("Label", go.transform, label, font, 28, Vector2.zero, size);
            text.color = new Color(0.08f, 0.08f, 0.1f);
            text.fontStyle = FontStyle.Bold;
            return go;
        }

        static Image CreateFillBar(string name, Transform parent, Color color, float inset = 0f)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);

            Image image = go.AddComponent<Image>();
            image.sprite = GetSolidWhiteSprite();
            image.color = color;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;

            return image;
        }

        const string SolidWhiteSpritePath = "Assets/Editor/Generated/UISolidWhite.png";
        static Sprite cachedSolidWhiteSprite;

        static Sprite GetSolidWhiteSprite()
        {
            if (cachedSolidWhiteSprite != null) return cachedSolidWhiteSprite;

            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(SolidWhiteSpritePath);
            if (existing != null)
            {
                cachedSolidWhiteSprite = existing;
                return existing;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Editor/Generated"))
            {
                AssetDatabase.CreateFolder("Assets/Editor", "Generated");
            }

            Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(SolidWhiteSpritePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(SolidWhiteSpritePath);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SolidWhiteSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            cachedSolidWhiteSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SolidWhiteSpritePath);
            return cachedSolidWhiteSprite;
        }

        static void WireButton(GameObject buttonGo, UnityEngine.Events.UnityAction call)
        {
            Button button = buttonGo.GetComponent<Button>();
            UnityEventTools.AddPersistentListener(button.onClick, call);
        }

        static void WireSceneButton(GameObject buttonGo, UnityEngine.Events.UnityAction<string> call, string arg)
        {
            Button button = buttonGo.GetComponent<Button>();
            UnityEventTools.AddStringPersistentListener(button.onClick, call, arg);
        }

        static void DestroyDirectChildIfExists(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        static InputActionReference FindActionReference(string mapName, string actionName)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ActionsPath);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is InputActionReference iar &&
                    iar.action != null &&
                    iar.action.name == actionName &&
                    iar.action.actionMap != null &&
                    iar.action.actionMap.name == mapName)
                {
                    return iar;
                }
            }

            throw new Exception($"KineticEnergySetup: could not find InputActionReference for {mapName}/{actionName}.");
        }
    }
}

