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
    // Builds the project's two test levels (and the menu) from scratch, entirely in code:
    //
    //   Tools > Kinetic Energy > Setup All          - prefab refresh + all three scenes + build list
    //   Tools > Kinetic Energy > Setup Quarry       - Level 1, "The Quarry" (the toy test)
    //   Tools > Kinetic Energy > Setup Gauntlet     - Level 2, "The Gauntlet" (the slowdown A/B test)
    //   Tools > Kinetic Energy > Setup Main Menu
    //
    // Batch-mode entry point:
    //   Unity.exe -batchmode -nographics -quit -projectPath <project>
    //     -executeMethod KineticEnergy.EditorSetup.KineticEnergySetup.SetupAll -logFile setup.log
    //
    // All level distances are expressed in L and H, measured from the CURRENT launch tuning
    // by simulating the launch integrator (see MeasureLaunchDistances):
    //   L = horizontal distance of a max-charge grounded launch at the default 30-degree aim
    //   H = apex height of a max-charge straight-up launch
    // Re-running the setup after a tuning change rebuilds the geometry to match.
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

        // ==================== Player tuning (single source of truth) ====================
        // The values the last playtest iteration settled on. ApplyPlayerTuning stamps them
        // onto the Player prefab and every scene instance (the anti-staleness rule this
        // project follows everywhere), and MeasureLaunchDistances derives L and H from them.

        const float MinLaunchForce = 60f;
        const float MaxLaunchForce = 130f;
        const float MaxChargeTime = 1.5f;
        const float MinLaunchDamping = 2.8f;
        const float MaxLaunchDamping = 1.0f;
        const float DownLaunchDamping = 0.2f;
        const float Gravity = -30f;
        const float ChargeAccumulationRate = 0.3f;
        // The grounded aim, forward hold-charge and midair dial accelerate: the rate grows
        // the longer the input sustains in one direction. (Up/down charges use the pound's
        // own base+growth ramp above.)
        const float ChargeAcceleration = 1f;
        // Doubled from the original 7 (direct request: "increase air control significantly")
        // - still below gravity (30), so the nudge steers a fall rather than replacing it.
        const float AirControlAcceleration = 14f;
        const float EnergyCostPerFullCharge = 1f;
        const float MinEnergyReserve = 0.05f;
        const float GroundedRefundMultiplier = 1f;
        const float MidairRefundBaseMultiplier = 1f;
        const float MidairRefundSpendFactor = 0.3f;
        const float PoundFlightRefundMultiplier = 1f;
        const float PlainFallDamping = 0.2f;
        // The EnergyEconomy4 ground pound (Final-Project branch): bounce hop, slow-mo
        // window, boosted whole-flight refund claimed by aiming inside the window, and the
        // base+growth charge ramp. Values are EnergyEconomy4's scene overrides.
        const float GroundPoundBoostMultiplier = 1.5f;
        const float GroundPoundHopHeight = 0.2f;
        const float GroundPoundSlowDuration = 0.5f;
        const float GroundPoundChargeBaseSpeed = 1.5f;
        const float GroundPoundChargeSpeedGrowth = 5f;
        const float ChargeTimeScale = 0.2f;
        // Flight game speed: 200% base, +1% per 1% of the tank spent on the launch.
        const float LaunchFlightTimeScale = 2f;
        const float FlightTimeScaleEnergyBonus = 1f;
        // Descending adds another ramp: +1% game speed on the first falling frame, growing
        // in even steps to +50% at the predicted impact.
        const float FallSpeedUpStart = 0.01f;
        const float FallSpeedUpEnd = 0.5f;
        const float AimBudgetSeconds = 2f;
        // Parity rule: a full tank must buy about the same slow-time as the aim budget, or
        // the A/B test measures generosity instead of architecture. 1 tank / 2s = 0.5/s.
        const float TankDrainPerSecond = 1f / AimBudgetSeconds;
        const float DialStickRate = 0.5f;
        const float DialWheelStep = 0.05f;
        const float ReferenceAimPitchDegrees = 30f; // the default aim pitch, and L's reference angle
        const float SimulationTimestep = 0.02f;     // matches the project's fixed timestep

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
            controller.defaultAimPitch = -ReferenceAimPitchDegrees; // negative tilts UP
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
            // Mouse aiming is the grounded default; gamepad sticks keep their normal roles
            // regardless (device checked per frame, no binding masks involved).
            controller.groundedAimWithMouse = true;
            controller.groundedMouseAimSensitivity = 0.15f;
            controller.wasdCameraTurnMultiplier = 1.5f;

            controller.moveAction = FindActionReference("Player", "Move");
            controller.groundedAimAction = FindActionReference("Player", "Launch");
            controller.groundedLaunchAction = FindActionReference("Player", "Fire");
            controller.upLaunchAction = FindActionReference("Player", "LaunchUp");
            // West's button - the action kept its historical asset name.
            controller.groundPoundAction = FindActionReference("Player", "SelectGhostPreview");
            controller.cancelChargeAction = FindActionReference("Player", "CancelCharge");
            controller.airAimAction = FindActionReference("Player", "FastPacedAim");
            controller.airLaunchAction = FindActionReference("Player", "FastPacedLaunch");
        }

        // ==================== L / H measurement ====================

        // Simulates the launch integrator the way the project has always validated tuning:
        // semi-implicit Euler with Unity's 1/(1+damping*dt) velocity decay, at the project's
        // gravity and fixed timestep. A max-charge launch uses MaxLaunchForce (the cube's
        // mass is 1, so force = exit speed) and MaxLaunchDamping.
        static void MeasureLaunchDistances(out float unitL, out float unitH)
        {
            // L: max charge at the reference 30-degree aim, distance when it returns to
            // launch height.
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

            // H: max charge straight up, apex height.
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

        // ==================== Entry points ====================

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

        // Quarry-only playtest build: the one scene, nothing else - no Gauntlet, no menu;
        // the game boots straight into the arena. The scene list is passed explicitly, so
        // EditorBuildSettings and the scenes themselves stay completely untouched. NOTE: the
        // orange menu pad and the pause menu's Main Menu / level buttons have no destination
        // in this build - pressing them logs a harmless error and nothing happens.
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

        // Builds exactly what the Build Settings window has ENABLED right now - the scene
        // list is the user's to manage there; this just executes it under the proper name.
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

        // Same scene selection as BuildFromBuildSettings, but a WEB (WebGL) build. The
        // output is a folder (index.html + Build/), servable by any static web host -
        // opening index.html straight from disk does NOT work in most browsers, it has to
        // be served (e.g. itch.io upload, or a local server for testing).
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
                locationPathName = "Builds/GD3RetakeKineticEnergyWeb", // WebGL target is a folder
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

        // ==================== Aim camera variants (depth-perception playtest) ====================

        // ADDITIVE: creates the three AimCameraPreset assets (existing assets keep their
        // tuned values), puts the variant controller + logger on the Player prefab, and adds
        // the pause menu's selector button + hint line (bottom-left corner of PausePanel, so
        // the existing button column is untouched).
        [MenuItem("Tools/Kinetic Energy/Setup Aim Camera Variants")]
        public static void SetupAimCameraVariants()
        {
            const string presetFolder = "Assets/Settings/AimCameraPresets";
            if (!AssetDatabase.IsValidFolder(presetFolder)) AssetDatabase.CreateFolder("Assets/Settings", "AimCameraPresets");

            // The dolly and standalone-parallax variants are SCRAPPED (direct request) -
            // their assets go, and their letters are reused by the PiP variants below.
            AssetDatabase.DeleteAsset(presetFolder + "/AimCameraC_OtsDriftDolly.asset");
            AssetDatabase.DeleteAsset(presetFolder + "/AimCameraD_OtsParallax.asset");

            AimCameraPreset a = LoadOrCreatePreset(presetFolder + "/AimCameraA_Baseline.asset", p =>
            {
                p.variant = AimCameraVariant.Baseline;
                p.displayName = "Frozen first person";
            });

            // B absorbs the parallax drift (subtle values - the original amplitudes read as
            // illogical rocking during bullet-time). Placement values the user may have
            // tuned are preserved; only the drift fields and name are (re)stamped.
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

            // C = baseline aim + the landing picture-in-picture window.
            AimCameraPreset c = LoadOrCreatePreset(presetFolder + "/AimCameraC_BaselinePip.asset", p =>
            {
                p.variant = AimCameraVariant.BaselinePip;
                p.displayName = "First person + landing view";
                p.pipEnabled = true;
            });

            // D = B (OTS + parallax) + the landing picture-in-picture window.
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

            // Direct request: the player should sit a bit closer to the screen in the OTS
            // variants - stamp the tighter back-distance on every OTS preset.
            b.otsBack = 1.8f;
            d.otsBack = 1.8f;
            EditorUtility.SetDirty(b);
            EditorUtility.SetDirty(d);

            // E = first person + FREE LOOK: WASD / right stick rotates the view without
            // moving the aim; energy dial on RB/LB (see the preset's UsesFreeLook).
            AimCameraPreset e = LoadOrCreatePreset(presetFolder + "/AimCameraE_FreeLookFp.asset", p =>
            {
                p.variant = AimCameraVariant.FreeLookFirstPerson;
                p.displayName = "First person + free look";
            });

            // F = the same free-look concept on the OTS camera.
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

            // Concise tester-facing names (direct request) - restamped on every run so a
            // rename here reaches existing assets too.
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
                // Long variant names must always FIT: best-fit shrinks the font before the
                // text can clip the button's edges.
                buttonLabel.resizeTextForBestFit = true;
                buttonLabel.resizeTextMinSize = 12;
                buttonLabel.resizeTextMaxSize = 26;
                pauseController.cameraVariantLabel = buttonLabel;
                EditorUtility.SetDirty(pauseController);

                // The controller-energy warning gets its OWN box directly above the variant
                // button (filled/emptied at runtime by PauseController).
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

        // Levels 2 and 3 were copied BEFORE the HUD-prefab replacement pass and never got
        // meter instances - their Player meter references point at nothing (or at the old
        // deactivated embedded UI), so the bars never update. This drops the EnergyMeter +
        // SlowdownMeter prefabs into each scene's pause canvas and wires the Player, same
        // as every other scene. Additive + wiring only.
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

                // The PauseSystem prefab's embedded meter UI (if still active here) is
                // superseded by the standalone prefabs - deactivate, never delete.
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

        // Wires the top-left ControlsHintLabel into PauseController so pausing hides it
        // (the "open the pause menu" hint is pointless while the menu is open). Pure wiring.
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

        // ADDITIVE: an Info button in the pause menu's bottom-right corner - reopens the
        // scene's intro/explainer overlay. PauseController hides it in scenes without one.
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

                // Renamed Info -> BuildInfo (direct request); the old name is cleared too
                // so re-runs on an older prefab replace it cleanly.
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

        // ADDITIVE: puts a Resume button at the TOP of the pause menu's button column and
        // makes it the gamepad's first-selected button. Wired to TogglePause, which resumes
        // when paused. Nothing existing is moved.
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
                    new Vector2(0f, 185f), new Vector2(300f, 70f)); // one column step above Restart (95)
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

        // The economy-iteration scene (a QuarryNew duplicate): locks the aim camera to
        // Variant A with all switching UI/hotkeys off, and adds the EconomyVariants harness
        // object. The scene file must already exist (copied from QuarryNew).
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

        // The merged economy's meter: a variant of the standalone EnergyMeter prefab
        // where the bar is 8 normal blocks (same 31.4px block width, dividers 8/9
        // retired) with a two-block PREMIUM segment welded flush to its right edge -
        // same block width, 30% taller, own outline/backdrop, and orange/blue fills
        // that start EMPTY (they only show actual premium energy/charge).
        [MenuItem("Tools/Kinetic Energy/Create Premium Energy Meter Prefab")]
        public static void CreatePremiumEnergyMeterPrefab()
        {
            BuildPremiumMeterVariant(PrefabFolder + "/PremiumEnergyMeter.prefab", 8);
        }

        // Shared builder: `normalBlocks` normal-height blocks, the remaining (10 - n)
        // blocks as the taller premium segment - 8+2 for the standard variant, 4+6 for
        // Level1Economy's 40% boundary.
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

                // The source layout: inset 3, block width 31.4, ten blocks. The main bar
                // keeps blocks 1-8 EXACTLY as they are (divider positions untouched);
                // the freed width becomes the premium segment.
                const float inset = 3f;
                const float blockWidth = 31.4f;
                float oldWidth = bodyRect.sizeDelta.x;                        // 323
                float newWidth = inset + normalBlocks * blockWidth + inset;   // 8 blocks: 257.2
                float zoneWidth = oldWidth - newWidth;                        // the premium blocks + border
                float bodyHeight = bodyRect.sizeDelta.y;                      // 30
                float zoneHeight = bodyHeight * 1.3f;                         // 30% taller

                // Body pivot is right-side: shrinking keeps the right edge, so the shift
                // left frees exactly the zone's strip while the total footprint stays.
                bodyRect.sizeDelta = new Vector2(newWidth, bodyHeight);
                bodyRect.anchoredPosition -= new Vector2(zoneWidth, 0f);

                Transform dividers = body.Find("MeterDividers");
                if (dividers != null)
                {
                    // The main bar keeps its first (normalBlocks - 1) internal lines;
                    // everything beyond belongs to the zone now.
                    for (int i = normalBlocks; i <= 9; i++)
                    {
                        Transform retired = dividers.Find("Divider" + i);
                        if (retired != null) retired.gameObject.SetActive(false);
                    }
                }

                // Look sampled straight from the source meter's own images.
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
                image.fillAmount = 0f; // empty until the harness feeds it real premium energy
            }
        }

        // QuarryEconomy2: a copy of the economy scene running the single MERGED design
        // (combo refunds + safety recharge + premium top 20%) instead of the five-way
        // variant harness. The copy is made from QuarryEconomy, so the locked camera and
        // all hand-tuned scene values carry over.
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

            // The five-way variant harness belongs to the FIRST economy scene only.
            var oldHarness = UnityEngine.Object.FindAnyObjectByType<EconomyVariantController>(FindObjectsInactive.Include);
            if (oldHarness != null) UnityEngine.Object.DestroyImmediate(oldHarness.gameObject);

            if (UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include) == null)
            {
                new GameObject("MergedEconomy").AddComponent<MergedEconomyController>();
            }

            // Swap the standalone meter for the premium 8+2 variant (idempotent), keeping
            // the old instance's placement and wiring the Player to the replacement.
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

        // The COMBO meter prefab (Level1Economy/Level1Challenge): a SlowdownMeter variant
        // whose bar is narrowed so that, with the harness's runtime xN circle anchored
        // 10px left of the bar, the circle's LEFT edge lines up exactly with the energy
        // meter's left edge - the bar's right edge stays where it always was.
        //   energy meter left = (-10 - 323) * 1.7376512 = -578.6 canvas
        //   bar left = -578.6 + 46 (circle) + 10 (gap) = -522.6; right edge -19
        //   => width = 503.6 (was 560)
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
                bodyRect.sizeDelta = new Vector2(503.6f, bodyRect.sizeDelta.y); // pivot (1,1): right edge stays
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("KineticEnergySetup: ComboMeter prefab created OK (width 503.6, circle aligns with the energy meter's left edge)");
        }

        // ADDITIVE: a CAMERA SETTINGS sub-screen (title, the two speed sliders, a Back
        // button) reached from a new pause-menu button - the same panel pattern the
        // Controls and Scenes screens use. Sliders run 50-150% in 5% steps, draggable
        // with the mouse or adjustable with the left stick while selected.
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

                // Clear the earlier inline block and any previous run's screen.
                DestroyDirectChildIfExists(pausePanel, "CameraSpeedSliders");
                DestroyDirectChildIfExists(pausePanel, "CameraSettingsButton");
                DestroyDirectChildIfExists(pauseCanvas, "CameraSettingsPanel");

                Font font = FindBestFont();
                Color accent = new Color(1f, 0.82f, 0.2f);

                // The sub-screen itself - same full-panel backdrop as the other screens.
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

                // ...and the way in, on the pause panel itself.
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

        // Turns Level1Challenge into the five-stage challenge cycle: the four Level 8
        // challenges plus the shrinking-platforms stage, each finish reloading the scene
        // onto the next, the LOCKED win screen after the last. Idempotent - safe to re-run.
        [MenuItem("Tools/Kinetic Energy/Setup Level1Challenge Cycle")]
        public static void SetupLevel1ChallengeCycle()
        {
            const string scenePath = "Assets/Scenes/Level1Challenge.unity";
            CreateDeathWallPrefab();
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // The finish pads advance the stage sequence now instead of showing the win
            // screen directly - the controller decides when the run is truly over.
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

            // The course in run order (ascending x - the course axis), floating walls
            // included: the sealing stage matches landings against it and the shrinking
            // stage scales along it.
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

            // The chase wall: parked behind the start, sweeping along +x. Tall and wide
            // enough to cover the course's full y spread (the elevated end pad) and its
            // z spread (platforms sit from z=-61 to z=+37).
            GameObject oldChase = GameObject.Find("ChaseWall");
            if (oldChase != null) UnityEngine.Object.DestroyImmediate(oldChase);
            GameObject chaseGo = InstantiatePrefab("DeathWall");
            chaseGo.name = "ChaseWall";
            chaseGo.transform.position = new Vector3(-60f, 13f, -12f);
            chaseGo.transform.localScale = new Vector3(2f, 46f, 140f);
            DeathWall chase = chaseGo.GetComponent<DeathWall>();
            chase.moveSpeed = 4f;
            // Gains pace as it runs: ~0.25 m/s per second, so the ~550-unit course tightens
            // from a walk into a real chase. Editable on the ChaseWall instance.
            chase.moveAcceleration = 0.25f;
            chase.maxMoveSpeed = 0f; // uncapped
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
            stages.lockedWinScreen = true; // the scene's self-contained "You win!" screen
            stages.chaseWall = chase;
            stages.sealWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/DeathWall.prefab");
            stages.coursePlatforms = course.ToArray();
            // Wider than Level 8's default - this course wanders in z, so a seal must
            // still block the whole corridor between two laterally offset platforms.
            stages.sealWallSize = new Vector3(1.5f, 40f, 90f);
            stages.respawnPoint = respawn.transform;
            EditorUtility.SetDirty(stages);

            // Stage 1 shows the aim BUDGET on its own bar underneath the combo meter -
            // previously both systems fought over the one repurposed slowdown meter. The
            // combo meter stays the economy's (via its explicit comboMeter reference);
            // the new bar becomes controller.slowdownMeter, which the controller itself
            // hides outside AimBudget mode - so it only appears in the first variation.
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
            // 45 below the combo meter's serialized spot: the economy drops the combo
            // meter 20px at runtime (comboMeterDropPixels), the body is 20 tall - this
            // lands the budget bar 5px under the dropped combo meter.
            budgetRect.anchoredPosition = comboRect.anchoredPosition + new Vector2(0f, -45f);
            EnergyMeterController budgetMeter = budgetGo.GetComponentInChildren<EnergyMeterController>(true);
            if (budgetMeter != null && budgetMeter.energyFillImage != null)
            {
                budgetMeter.energyFillImage.color = new Color(0.3f, 0.65f, 1f); // budget blue, not combo orange
                EditorUtility.SetDirty(budgetMeter.energyFillImage);
            }
            playerController.slowdownMeter = budgetMeter;
            EditorUtility.SetDirty(playerController);

            // Lethal shells on the objects you must land on PRECISELY - every face except
            // the one facing the course gets a 0.5-thick damage slab.
            Material shellMaterial = MakeMaterial("DamageWallMaterial", new Color(0.85f, 0.15f, 0.12f));
            foreach (string shellTarget in new[] { "FloatingWall1", "FloatingWall2", "FloatingWall3", "UpsidePlatform" })
            {
                GameObject shellGo = GameObject.Find(shellTarget);
                if (shellGo == null) throw new Exception("KineticEnergySetup: Level1Challenge is missing " + shellTarget);
                AddDamageShell(shellGo.transform, respawn.transform, shellMaterial);
            }

            // The explainer, shown at first boot and reopened by the pause menu's BuildInfo
            // button (both read this one string).
            economy.introText = Level1ChallengeInfoText;
            EditorUtility.SetDirty(economy);

            EnsureSceneInBuildSettings(scenePath); // the cycle reloads itself by name
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Level1Challenge five-stage cycle configured OK ("
                + course.Count + " course platforms, damage shells + build info updated)");
        }

        // ADDITIVE, SCENE-ONLY: Level1Challenge's challenge-variant picker - a "Variants"
        // button under Resume opening a screen that lists all five challenges; picking one
        // restarts the scene on it. Built on the scene's PauseSystem INSTANCE, so no other
        // scene's pause menu changes.
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

            // The screen: title, the five challenges, Back.
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

            // The way in, directly under Resume - then the whole column is re-laid so the
            // eight buttons keep the even rhythm.
            GameObject openButton = CreateButton("VariantsButton", pausePanel, "Variants",
                font, accent, new Vector2(0f, 140f), new Vector2(300f, 70f));
            WireButton(openButton, pause.OnVariantsClicked);
            LayOutPausePanelButtons(pausePanel);
            EditorUtility.SetDirty(pause);

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Level1Challenge variants screen added OK (5 variants + back)");
        }

        // Stamps the steeper grounded-aim charge ramp on the two aim-test scenes' player
        // instances - everywhere else keeps the shared default.
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

        // Drops every ground enemy straight DOWN onto the surface beneath it - x and z are
        // untouched, only the resting height is corrected. An enemy left hanging above (or
        // sunk into) its platform is what made them clip through and hurl themselves at a
        // platform on spawn.
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
                    // Probe from well ABOVE, so a body that currently sits inside the deck
                    // still finds the top face rather than missing it from within.
                    Vector3 origin = body.position + Vector3.up * 30f;
                    RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 200f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

                    float bestY = float.NegativeInfinity;
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.collider == null) continue;
                        if (hit.collider.GetComponentInParent<Enemy>() != null) continue;        // itself or a neighbour
                        if (hit.collider.GetComponentInParent<FlyingEnemy>() != null) continue;
                        if (hit.collider.GetComponentInParent<KineticCubeController>() != null) continue;
                        if (hit.collider.GetComponentInParent<DamageWalls>() != null) continue;  // never the hazard floor
                        if (hit.point.y > body.position.y + 1f) continue;                        // ceilings above it
                        if (hit.point.y > bestY) bestY = hit.point.y;
                    }
                    if (float.IsNegativeInfinity(bestY)) continue; // nothing below - leave it alone

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

        // Swaps LevelElementsTest2's grounded enemies from stalkers to HUNTERS - the same
        // aggressive cousin, but punishable AFTER its attack instead of during the windup.
        // Placement is preserved exactly; nothing else in the scene is touched.
        [MenuItem("Tools/Kinetic Energy/Use Hunter Enemies In LevelElementsTest2")]
        public static void UseHunterEnemiesInLevelElementsTest2()
        {
            const string scenePath = "Assets/Scenes/LevelElementsTest2.unity";
            CreateHunterEnemyPrefab(); // also stamps the dodge + cooldown window onto an older prefab
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject hunterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/HunterEnemy.prefab");
            if (hunterPrefab == null) throw new Exception("KineticEnergySetup: HunterEnemy.prefab is missing.");

            int swapped = 0;
            foreach (Enemy walker in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Only the ground enemies that are NOT already hunters.
                if (walker.killWindow == EnemyKillWindow.WhileCoolingDown) continue;

                GameObject old = walker.gameObject;
                GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(hunterPrefab, old.transform.parent);
                replacement.name = old.name;
                replacement.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                replacement.transform.localScale = old.transform.localScale;
                UnityEngine.Object.DestroyImmediate(old);
                swapped++;
            }

            // Swapping objects alone does not flag the scene as modified, and SaveOpenScenes
            // silently writes nothing for a clean scene - the first run reported success and
            // saved absolutely nothing.
            if (swapped > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: LevelElementsTest2 now uses hunters OK (" + swapped + " swapped)");
        }

        // SURGICAL tuning pass over both element scenes: values already serialized on scene
        // instances do not follow a code default, so they are written here. Nothing else in
        // either scene is touched.
        [MenuItem("Tools/Kinetic Energy/Tune Element Scenes")]
        public static void TuneElementScenes()
        {
            const float laserKnockback = 16.5f; // 25% under the enemy-projectile 22

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
                    // Standing still pays below the baseline even mid-chain.
                    economy.regenWhileComboRunning = true;
                    EditorUtility.SetDirty(economy);
                }

                EditorSceneManager.SaveOpenScenes();
                Debug.Log("KineticEnergySetup: tuned " + scenePath + " OK (" + beams + " laser beams at "
                    + laserKnockback + ", regen-during-combo on)");
            }
            AssetDatabase.SaveAssets();
        }

        // Copies the turret section from LevelElementsTest into LevelElementsTest2 at the
        // SAME placement it has in the source scene, so the two courses stay aligned even
        // after the source has been moved around by hand. Additive - nothing else is touched.
        [MenuItem("Tools/Kinetic Energy/Restore Turret Section In LevelElementsTest2")]
        public static void RestoreTurretSectionInTest2()
        {
            string[] copyNames = { "TurretRun1", "TurretRun2", "TurretWallLeft", "TurretWallRight" };

            // ---- Read the placement out of the source scene ----
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

            // ---- Rebuild it in the variant ----
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

        // LevelElementsTest2: the same course, harder cast. Flyers become weak-spot flyers
        // (one extra, with angled side walls), ground enemies become stalkers on a bigger
        // arena flanked by tilted non-sticky ledges, and the turret section is gone.
        // Built by COPYING the scene, then editing it - the hand-placed checkpoints, intro
        // text and section wiring all come across untouched.
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

            ReplaceLooseRotatingWalls(); // the copy inherits the loose wall too

            Material platformMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/QuarryPlatformMaterial.mat");
            Material wallMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/ElementsWallMaterial.mat");
            if (platformMat == null || wallMat == null) throw new Exception("KineticEnergySetup: course materials are missing.");

            GameObject course = GameObject.Find("ElementsCourse");
            Transform tf = course != null ? course.transform : null;
            LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);

            // ---- Ground enemies -> STALKERS, on a bigger arena ----
            foreach (string arenaName in new[] { "GroundArena1", "GroundArena2" })
            {
                GameObject arena = GameObject.Find(arenaName);
                if (arena == null) continue;
                Vector3 size = arena.transform.localScale;
                arena.transform.localScale = new Vector3(size.x * 1.25f, size.y, size.z * 1.25f);
                EditorUtility.SetDirty(arena);

                // Tilted, NON-STICKY ledges flanking the arena - one on the near side, two
                // on the far, all above the deck and at different heights, so a stalker
                // fight has vertical escapes that will not hold you if you cling.
                float deckY = arena.transform.position.y;
                float halfZ = arena.transform.localScale.z * 0.5f;
                SpawnTiltedLedge(tf, arenaName + "LedgeA", arena.transform.position + new Vector3(-4f, deckY + 9f, -halfZ - 7f), new Vector3(0f, 0f, 22f), wallMat);
                SpawnTiltedLedge(tf, arenaName + "LedgeB", arena.transform.position + new Vector3(3f, deckY + 6f, halfZ + 7f), new Vector3(0f, 0f, -18f), wallMat);
                SpawnTiltedLedge(tf, arenaName + "LedgeC", arena.transform.position + new Vector3(-6f, deckY + 14f, halfZ + 12f), new Vector3(12f, 0f, -26f), wallMat);
            }
            ReplaceEnemies("StalkerEnemy", isFlyer: false);

            // ---- Flyers -> WEAK SPOT flyers, plus one more and flanking walls ----
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

            // ---- The turret section goes entirely ----
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
                // Drop the section whose spawn just went with the turrets.
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

        // A tilted slab you can bounce off but never cling to - deliberately plain, so it
        // carries no StickySurface and lets go after the usual brief crash-cling.
        static void SpawnTiltedLedge(Transform parent, string name, Vector3 position, Vector3 eulerTilt, Material material)
        {
            GameObject ledge = CreateBlock(parent, name, position, new Vector3(12f, 1.5f, 9f), material);
            ledge.transform.rotation = Quaternion.Euler(eulerTilt);
            EditorUtility.SetDirty(ledge);
        }

        // Swaps every plain Enemy / FlyingEnemy in the scene for a harder prefab variant,
        // keeping the spot each one guarded.
        static void ReplaceEnemies(string prefabName, bool isFlyer)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + prefabName + ".prefab");
            if (prefab == null) throw new Exception("KineticEnergySetup: missing prefab " + prefabName);

            var targets = new List<GameObject>();
            if (isFlyer)
            {
                foreach (FlyingEnemy flyer in UnityEngine.Object.FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (flyer is WeakSpotFlyingEnemy) continue; // already the variant
                    targets.Add(flyer.gameObject);
                }
            }
            else
            {
                foreach (Enemy walker in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (walker.GetType() != typeof(Enemy)) continue; // leave existing variants alone
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

        // The sections screen is built from the section list, so dropping a section means
        // rebuilding its buttons - otherwise a dead button points at a missing spawn.
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

        // The weak spot sat dead on top of the sphere, so it was as reachable from the front
        // as from behind. Tilting it back makes the approach matter: you have to come at the
        // flyer from above and BEHIND to land on it.
        [MenuItem("Tools/Kinetic Energy/Angle WeakSpotFlyer Weak Spot")]
        public static void AngleWeakSpotFlyerWeakSpot()
        {
            const float tiltDegrees = 42f;   // from straight up, leaning toward the back (-z)
            const float spotDistance = 0.55f;

            string path = PrefabFolder + "/WeakSpotFlyer.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform spot = root.transform.Find("WeakSpot");
                if (spot == null) throw new Exception("KineticEnergySetup: WeakSpotFlyer has no WeakSpot child.");

                // Euler(-tilt, 0, 0) leans the local up axis toward -z, i.e. the flyer's back.
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

        // The scene held one hand-built rotating wall and one prefab instance. Swaps the
        // loose one for the prefab, keeping its placement, spin settings and damage edges.
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

        // Re-collects every respawning hazard so newly built shells follow the checkpoint.
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

        // SURGICAL: adds the damage edges to LevelElementsTest's turret and rotating walls
        // and touches NOTHING else. The full setup rebuilds the whole course, which throws
        // away hand-placed checkpoints and edited text - this is the safe way to add the
        // shells to a scene that has since been worked on by hand.
        [MenuItem("Tools/Kinetic Energy/Add Damage Edges To LevelElementsTest")]
        public static void AddElementsDamageEdges()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/LevelElementsTest.unity", OpenSceneMode.Single);

            // The existing damage material, loaded - never re-created, never restyled.
            Material damageMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/DamageWallMaterial.mat");
            if (damageMat == null) throw new Exception("KineticEnergySetup: DamageWallMaterial is missing.");

            LevelSectionController sections = UnityEngine.Object.FindAnyObjectByType<LevelSectionController>(FindObjectsInactive.Include);
            Transform fallbackSpawn = sections != null && sections.sections.Length > 0 ? sections.sections[0].spawnPoint : null;

            int shelled = 0;
            // Rotating walls: the RIM only - both broad faces must stay landable, since the
            // wall turns each of them round to meet you in turn.
            foreach (RotatingWall wall in UnityEngine.Object.FindObjectsByType<RotatingWall>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                AddEdgeDamageShell(wall.transform, fallbackSpawn, damageMat);
                shelled++;
            }

            // Turret walls: edges AND the back - only the mounted face stays landable.
            foreach (string wallName in new[] { "TurretWallLeft", "TurretWallRight" })
            {
                GameObject wallGo = GameObject.Find(wallName);
                if (wallGo == null) continue;
                AddDamageShell(wallGo.transform, fallbackSpawn, damageMat);
                shelled++;
            }

            // The new shells respawn the player, so the section index must know about them
            // or a death on one would ignore the active checkpoint.
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

        // Diagnostic: opens LevelElementsTest and reports whether a working pause menu is
        // actually present and wired (object active, panels/buttons assigned, an
        // EventSystem to drive it). Changes nothing.
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

            // Section buttons: label -> wired argument -> the section that argument selects.
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

        // ==================== LevelElementsTest ====================

        // Rebuilds LevelElementsTest as a LINEAR element showcase: one march along +x, each
        // section introducing a single new kind of platform, obstacle or enemy, with height
        // and lateral variation between them. The challenge-variation machinery the scene
        // inherited from its Level1Challenge copy is stripped out; the economy stays.
        // Idempotent - safe to re-run.
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

            // ---- Strip the inherited challenge run ----
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

            // Every section opens with a plain pad the player is teleported onto, so a jump
            // into any element starts from solid, safe ground - and a checkpoint slab
            // hovering over it, so reaching the section on foot claims it as well.
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
                // Hovers just clear of the deck, thin enough to pass through on landing.
                checkpoint.transform.position = padCentre + new Vector3(0f, pad.y * 0.5f + 0.75f, 0f);
                checkpoint.transform.localScale = new Vector3(pad.x * 0.9f, 0.5f, pad.z * 0.9f);
                checkpoint.GetComponent<Checkpoint>().respawnPoint = spawn.transform;
                EditorUtility.SetDirty(checkpoint);

                return spawn.transform;
            }

            float gap = 0.42f * L;      // a comfortable mid-charge hop
            // The section pad sits a SHORT lead-in from its own first element, and a full
            // gap from the previous section's last one. With an even spacing the pad landed
            // exactly midway between the two, so jumping to a section dropped you next to
            // the PREVIOUS section's content - it read as arriving one section early.
            float lead = gap * 0.5f;
            float x = 0f;
            float e1, e2;

            // ---- 1. Basics: plain platforms, gentle rise, slight weave ----
            OpenSection("1 - Basics", new Vector3(x, -1f, 0f));
            Vector3 playerSpawn = new Vector3(x, 1.5f, 0f);
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "BasicsHop1", new Vector3(e1, 1f, 10f), pad, platformMat);
            CreateBlock(tf, "BasicsHop2", new Vector3(e2, 4f, -8f), pad, platformMat);

            // ---- 2. Moving platforms ----
            x = e2 + gap;
            OpenSection("2 - Moving platforms", new Vector3(x, 4f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            SpawnMovingPlatform(tf, "MoverSide", new Vector3(e1, 5f, -14f), new Vector3(0f, 0f, 28f), 7f);
            SpawnMovingPlatform(tf, "MoverLift", new Vector3(e2, 2f, 6f), new Vector3(0f, 14f, 0f), 6f);

            // ---- 3. Rotating walls: sticky faces that keep turning away ----
            x = e2 + gap;
            Transform spinSpawn = OpenSection("3 - Rotating walls", new Vector3(x, 8f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            GameObject spinWall1 = SpawnRotatingWall(tf, "SpinWall1", new Vector3(e1, 10f, -10f), 28f, 0f, wallMat);
            GameObject spinWall2 = SpawnRotatingWall(tf, "SpinWall2", new Vector3(e2, 12f, 8f), -36f, 90f, wallMat);
            // Rim only - a spinning wall presents BOTH broad faces in turn, so both stay
            // landable and it is the thin edges that punish a miss.
            AddEdgeDamageShell(spinWall1.transform, spinSpawn, damageMat);
            AddEdgeDamageShell(spinWall2.transform, spinSpawn, damageMat);

            // ---- 4. Lasers: timed gates over a straight runway ----
            x = e2 + gap;
            Transform laserSpawn = OpenSection("4 - Lasers", new Vector3(x, 8f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "LaserRun1", new Vector3(e1, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            CreateBlock(tf, "LaserRun2", new Vector3(e2, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            CreateLaserGate(tf, "ElementsGate1", new Vector3(x + lead * 0.5f, 9f, 0f), 24f, 12f, 1.5f, 1.5f, 0f, laserSpawn);
            CreateLaserGate(tf, "ElementsGate2", new Vector3((e1 + e2) * 0.5f, 9f, 0f), 24f, 12f, 1.2f, 1.4f, 0.7f, laserSpawn);

            // ---- 5. Grounded enemies: wide arenas to fight across ----
            x = e2 + gap;
            OpenSection("5 - Ground enemies", new Vector3(x, 4f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "GroundArena1", new Vector3(e1, 2f, 12f), new Vector3(26f, 2f, 26f), platformMat);
            SpawnEnemy("ElementsEnemy1", new Vector3(e1, 4f, 12f), EnemyWanderMode.PlatformSurface, 10f, 2f);
            CreateBlock(tf, "GroundArena2", new Vector3(e2, 2f, -12f), new Vector3(26f, 2f, 26f), platformMat);
            SpawnEnemy("ElementsEnemy2", new Vector3(e2 - 5f, 4f, -12f), EnemyWanderMode.PlatformSurface, 10f, 2f);
            SpawnEnemy("ElementsEnemy3", new Vector3(e2 + 5f, 4f, -12f), EnemyWanderMode.WithinRadius, 8f, 2f);

            // ---- 6. Flying enemies: shooters over open gaps ----
            x = e2 + gap;
            OpenSection("6 - Flying enemies", new Vector3(x, 6f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "FlyStep1", new Vector3(e1, 9f, -10f), pad, platformMat);
            SpawnFlyingEnemy("ElementsFlyer1", new Vector3(x + lead * 0.6f, 16f, -4f), 9f, 24f);
            CreateBlock(tf, "FlyStep2", new Vector3(e2, 12f, 10f), pad, platformMat);
            SpawnFlyingEnemy("ElementsFlyer2", new Vector3(e1 + gap * 0.6f, 20f, 6f), 11f, 26f);

            // ---- 7. Turrets: fixed guns covering the final corridor ----
            x = e2 + gap;
            Transform turretSpawn = OpenSection("7 - Turrets", new Vector3(x, 10f, 0f));
            e1 = x + lead; e2 = e1 + gap;
            CreateBlock(tf, "TurretRun1", new Vector3(e1, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            GameObject turretWallLeft = CreateBlock(tf, "TurretWallLeft", new Vector3(e1, 12f, -16f), new Vector3(20f, 16f, 2f), wallMat);
            SpawnTurret("ElementsTurret1", new Vector3(e1, 13f, -14.8f), new Vector3(-90f, 0f, 0f));
            CreateBlock(tf, "TurretRun2", new Vector3(e2, 8f, 0f), new Vector3(16f, 2f, 16f), platformMat);
            GameObject turretWallRight = CreateBlock(tf, "TurretWallRight", new Vector3(e2, 12f, 16f), new Vector3(20f, 16f, 2f), wallMat);
            SpawnTurret("ElementsTurret2", new Vector3(e2, 13f, 14.8f), new Vector3(90f, 0f, 0f));
            // Edges AND the back: only the face the turret is mounted on stays landable -
            // the shell picks that face automatically as the one turned toward the course.
            AddDamageShell(turretWallLeft.transform, turretSpawn, damageMat);
            AddDamageShell(turretWallRight.transform, turretSpawn, damageMat);

            // ---- Finish ----
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

            // ---- Lasers HURT rather than kill ----
            // Their beam root's DamageWalls is swapped for the hazard component, so a
            // mistimed run costs a shove plus a chunk of tank - the same as walking into an
            // enemy - instead of ending the run. They stop being respawn sources with it.
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

            // ---- Hazard floor, well below the whole run ----
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(courseLength * 0.5f, -26f, 0f), new Vector3(courseLength + 120f, 2f, 220f), damageMat);
            DamageWalls floorDamage = damageFloor.AddComponent<DamageWalls>();
            hazards.Add(floorDamage);
            // Anything else in the course that still respawns (none today - the lasers gave
            // it up above - but a future hazard is picked up automatically).
            foreach (DamageWalls courseHazard in course.GetComponentsInChildren<DamageWalls>(true))
            {
                hazards.Add(courseHazard);
            }

            // ---- Section index (drives the pause menu's jump screen) ----
            GameObject sectionsGo = GameObject.Find("LevelSections");
            if (sectionsGo != null) UnityEngine.Object.DestroyImmediate(sectionsGo);
            sectionsGo = new GameObject("LevelSections");
            LevelSectionController sectionController = sectionsGo.AddComponent<LevelSectionController>();
            sectionController.sections = sections.ToArray();
            sectionController.hazards = hazards.ToArray();
            EditorUtility.SetDirty(sectionController);

            // Every hazard starts pointed at section 1 (the controller re-points them on
            // each jump); the player begins there too.
            foreach (DamageWalls hazard in hazards)
            {
                hazard.respawnPoint = sections[0].spawnPoint;
                EditorUtility.SetDirty(hazard);
            }

            player.transform.position = playerSpawn;
            EditorUtility.SetDirty(player);

            // The scene's own rule: a combo window that runs dry in the air drops you.
            economy.dropPlayerWhenWindowExpires = true;
            // NOTE: introText / introKey are deliberately NOT written here. They are edited
            // by hand in the scene, and stamping them on every re-run threw that work away.
            // Standing still below the 40% baseline ALWAYS refills you (while no combo
            // window is running). The recharge latches on below its trigger and fills to
            // its ceiling, so trigger == ceiling == the baseline turns it into a plain
            // "anything under 40% regenerates" rule. Set on every variant's pair so the
            // rule holds whichever one the scene is on.
            economy.safetyTriggerFraction = economy.safetyCeilingFraction;
            economy.dualSafetyTriggerFraction = economy.dualSafetyCeilingFraction;
            economy.totalLossSafetyTriggerFraction = economy.totalLossSafetyCeilingFraction;
            EditorUtility.SetDirty(economy);

            BuildSectionsScreen(pause, sectionController, sections);
        }

        // The pause menu's SECTIONS screen: a button per section that teleports the player
        // there and hands play straight back. Built on the scene's PauseSystem instance, so
        // no other scene's menu changes. The inherited Variants screen is removed.
        static void BuildSectionsScreen(PauseController pause, LevelSectionController sectionController, List<LevelSectionController.Section> sections)
        {
            Transform pauseCanvas = pause.transform.parent != null && pause.transform.parent.Find("PausePanel") != null
                ? pause.transform.parent
                : pause.transform.root.Find("PauseCanvas");
            if (pauseCanvas == null) throw new Exception("KineticEnergySetup: could not locate PauseCanvas in LevelElementsTest.");
            Transform pausePanel = pauseCanvas.Find("PausePanel");
            if (pausePanel == null) throw new Exception("KineticEnergySetup: LevelElementsTest's PauseCanvas has no PausePanel.");

            // The challenge-variant screen this scene inherited has no meaning here.
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
                // Two listeners, in order: jump, then close the menu - the teleport has
                // already put the player where they asked to be.
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

        // A floating wall on a turntable: sticky, so its face holds you and carries you
        // round once you land on it.
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

        // ORDER-ONLY swap of the sealing and chasing stages in the live scene: the two
        // sequence entries trade places, the info text renumbers, and the Variants screen
        // is rebuilt in the new order. No tuning value is touched.
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

            economy.introText = Level1ChallengeInfoText; // renumbered 3/4
            EditorUtility.SetDirty(economy);
            EditorSceneManager.SaveOpenScenes();

            // The Variants screen rebuild carries the new order/labels (tuning untouched).
            AddVariantsScreenToLevel1Challenge();
            Debug.Log("KineticEnergySetup: sealing/chasing order swapped OK");
        }

        // The checkpoint pad: a blue see-through slab that hovers over a section's platform.
        // Sized per instance (90% of its platform in x/z, 0.5 tall), so the prefab is a
        // plain unit cube trigger.
        // Converts the checkpoint from a pass-through trigger into a physical BUTTON, and
        // does it by editing the existing prefab IN PLACE. That matters: recreating the
        // asset gives every object inside it new ids, and each placed checkpoint's
        // overrides - its name, position, scale and respawn target - are bound to those
        // ids. Rebuilding the prefab silently reset all fourteen placed checkpoints to
        // defaults. Keeping the ROOT (and its Checkpoint component) untouched keeps every
        // override bound; only children are added around it.
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
                // The ROOT becomes the frame: the surround the button is set into. Solid
                // now - the assembly is landed on rather than passed through.
                Collider rootCollider = root.GetComponent<Collider>();
                if (rootCollider != null) rootCollider.isTrigger = false;
                Renderer rootRenderer = root.GetComponent<Renderer>();
                if (rootRenderer != null) rootRenderer.sharedMaterial = frameMat;

                // Child collisions report through the body on the root, which is how the
                // component hears the button being struck.
                Rigidbody body = root.GetComponent<Rigidbody>();
                if (body == null) body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;

                Transform existingButton = root.transform.Find("Button");
                if (existingButton != null) UnityEngine.Object.DestroyImmediate(existingButton.gameObject);

                // The pressable face, standing proud of the frame so its travel reads.
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
                // The button spans local y 0 -> 1.2 and the frame's top face sits at 0.5, so
                // it stands 0.7 proud. Sinking 0.45 leaves 0.25 still showing: pressed IN,
                // not swallowed - at 0.9 it dropped below the frame and disappeared.
                checkpoint.pressDepth = 0.45f;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Checkpoint button prefab updated in place OK");
        }

        // The element scenes' finish was a bare, invisible trigger built inline - nothing to
        // see and nothing to reuse. This makes it a proper prefab with a green see-through
        // body, and swaps the loose objects for instances at their existing placement.
        //
        // Deliberately its OWN prefab rather than a change to FinishTrigger.prefab, which
        // Levels 3-10 use with FinishLineNextScene - giving that one a visual would alter
        // every one of them.
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

            // Edited IN PLACE from here on, so any instances keep their overrides bound.
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();
                box.isTrigger = true; // flown THROUGH - it must never stop a launch

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

        // Diagnostic: reports the finish volume's real state in both element scenes.
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
                    if (PrefabUtility.IsPartOfPrefabInstance(finish.gameObject)) continue; // already done

                    Transform old = finish.transform;
                    BoxCollider oldBox = finish.GetComponent<BoxCollider>();
                    // The cube IS the trigger, so the old collider's box becomes the scale -
                    // the volume the player passes through stays exactly the same size.
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

        // Rewrites the intro/BuildInfo text in both element scenes. They describe DIFFERENT
        // casts now - plain enemies in one, hunters and weak-spot flyers in the other - so
        // each gets its own text rather than a shared one that would be wrong for both.
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

        // Swaps the FLYING-ENEMY and TURRET sections along the course: each section's whole
        // contents (pad, spawn, checkpoint and every element in it) is shifted by the offset
        // between the two section anchors, so their internal layout - including anything
        // moved by hand - is carried across intact. The section list is reordered and
        // renumbered to match, and the Sections screen rebuilt from it.
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

                // Measured BEFORE anything moves - the two anchors trade places exactly.
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
                        // The list is read in course order, so the entries swap places and
                        // take the OTHER one's number with them.
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
                go.transform.position += delta; // children (shells, weak spots) ride along
                EditorUtility.SetDirty(go);
            }
        }

        // The turret's flash colour is serialized on its prefab from an older build, so a
        // code default cannot reach it - written here to the ground hunter's warning yellow.
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

        // Tunes the weak-spot flyer's posture and turning. COMPONENT VALUES ONLY - the
        // model (the body, and the hand-placed weak spot on it) is not touched: the hunch
        // is a rotation the flyer holds while flying, not a change to how it is built.
        [MenuItem("Tools/Kinetic Energy/Tune WeakSpot Flyer Posture")]
        public static void TuneWeakSpotFlyerPosture()
        {
            string path = PrefabFolder + "/WeakSpotFlyer.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                FlyingEnemy flyer = root.GetComponent<FlyingEnemy>();
                if (flyer == null) throw new Exception("KineticEnergySetup: WeakSpotFlyer has no FlyingEnemy component.");

                // Nose-down, so the back-mounted weak spot rides tilted upward and can be
                // reached from above.
                flyer.hunchPitchDegrees = 22f;
                // 20% off the standard 6 - heavier, slower to bring its aim round.
                flyer.turnSpeed = 4.8f;
                // A full second sitting still after the shot: the committed, readable beat.
                flyer.postFireHoldSeconds = 1f;
                // It patrols among the section's floating walls, so it has to steer around
                // them rather than drifting through.
                flyer.avoidObstacles = true;
                flyer.obstacleClearance = 3.5f;
                EditorUtility.SetDirty(flyer);

                // A slower, steadier beacon - this tell never switches off, so a quick
                // flicker reads as noise rather than as "aim here".
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

        // The flying rotated slabs - turret walls, the flyer-section side walls and the
        // arena ledges - are LANDING TARGETS, so the aim must read them green on every face
        // rather than only where they happen to point upward. The rule for that already
        // exists: a StickySurface marks a surface safe "regardless of its value", so one
        // with sticky OFF makes the preview green while leaving the gameplay exactly as it
        // was - a brief cling, no permanent hold.
        //
        // The same pass gives the side walls and ledges their damage ridges: rim only, so
        // both broad faces stay landable and it is the thin edges that punish a miss.
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

                    // Safe to land on, from any angle - without becoming a permanent perch.
                    StickySurface marker = candidate.GetComponent<StickySurface>();
                    if (marker == null) marker = candidate.gameObject.AddComponent<StickySurface>();
                    marker.sticky = false;
                    EditorUtility.SetDirty(marker);
                    marked++;

                    // The turret walls already carry the full shell (edges AND the back, so
                    // only the mounted face is landable) - leave those exactly as they are.
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

        // Diagnostic: reports every checkpoint's wiring in both element scenes. Changes nothing.
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

        // No instance swap is needed: the prefab is edited in place, so every placed
        // checkpoint inherits the button automatically and keeps its own placement.

        const float DamageShellThickness = 0.5f;

        // Wraps a landing object in damage slabs on every face EXCEPT the one the player
        // arrives at - the face turned toward the course. Deliberately NOT a solid box
        // around the object: slabs sit just OUTSIDE each covered face, so the approach to
        // the landing face is completely clear and nothing can clip into a hazard on a
        // clean landing.
        //
        // The slabs are CHILDREN, so the shrinking-platforms variant scales them with
        // their platform for free.
        static void AddDamageShell(Transform platform, Transform respawnPoint, Material material)
        {
            for (int i = platform.childCount - 1; i >= 0; i--)
            {
                if (platform.GetChild(i).name.StartsWith("DamageShell"))
                {
                    UnityEngine.Object.DestroyImmediate(platform.GetChild(i).gameObject);
                }
            }

            // Which face stays safe: the one pointing at the course line (its own x, the
            // platform deck's height, the centre z). For a floating wall that is the side
            // turned inward; for the upside-down platform it is the underside - which
            // leaves the outward side (+z / -z respectively, +y for the ceiling) lethal.
            Vector3 courseReference = new Vector3(platform.position.x, -1f, 0f);
            Vector3 toCourseLocal = platform.InverseTransformDirection(courseReference - platform.position);
            int landingAxis = 0;
            for (int axis = 1; axis < 3; axis++)
            {
                if (Mathf.Abs(toCourseLocal[axis]) > Mathf.Abs(toCourseLocal[landingAxis])) landingAxis = axis;
            }
            float landingSign = Mathf.Sign(toCourseLocal[landingAxis]);
            float outwardSign = -landingSign;

            // Local thickness per axis - the parent's scale turns each into 0.5 world units.
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
                    // The far face - the one you would sail past the object to reach.
                    Vector3 position = Vector3.zero;
                    position[axis] = outwardSign * (0.5f + outwardThickness * 0.5f);
                    Vector3 size = Vector3.one;
                    size[axis] = outwardThickness;
                    CreateDamageSlab(platform, "DamageShell_Outward", position, size, respawnPoint, material);
                    continue;
                }

                // A side pair. Each spans from the SAFE face's plane to the outer edge of
                // the far slab, so it never overhangs the landing approach, and is widened
                // on the remaining axis to close the corners.
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

        // ONLY the narrow rim gets shells - both broad faces stay clear. A rotating wall
        // turns either face round to meet you, so neither may be lethal; it is the thin
        // edges that should punish a miss.
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

            // The THINNEST axis carries the two broad faces - those stay open.
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
                    // Flush with the broad faces (never past them, or the shell would
                    // overhang the landing face), and widened on the third axis to close
                    // the corners between the two edge pairs.
                    size[openAxis] = 1f;
                    size[otherAxis] = 1f + 2f * t[otherAxis];
                    CreateDamageSlab(platform, "DamageShell_Edge" + axis + (sign > 0 ? "P" : "N"),
                        position, size, respawnPoint, material);
                }
            }
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
            // SOLID, not a trigger: the aim preview only mirrors solid geometry, so a
            // trigger shell would be invisible to the landing prediction and the cursor
            // would read a lethal face as a safe green landing.
            slab.GetComponent<BoxCollider>().isTrigger = false;
            DamageWalls damage = slab.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint;
            EditorUtility.SetDirty(slab);
        }

        // Shown at first boot and behind the pause menu's BuildInfo button.
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

        // The pause panel's button column, top to bottom, on an even 90px rhythm. The TOP
        // is anchored (230, clearing the title at 327) rather than the centre, so a scene
        // that adds a button - Level1Challenge's Variants - grows the stack downward
        // instead of pushing the first button up into the title.
        const float PauseButtonTopY = 230f;
        const float PauseButtonSpacing = 90f;

        // VariantsButton and SectionsButton are the scene-specific slot right under Resume -
        // no scene has both, and the layout closes over whichever is absent.
        static readonly string[] PauseButtonOrder =
        {
            "ResumeButton", "VariantsButton", "SectionsButton", "RestartButton", "FeedbackButton",
            "CameraSettingsButton", "ControlsButton", "QuitButton", "MainMenuButton",
        };

        // Only the buttons that EXIST are placed, and the rhythm closes over the gaps -
        // scenes without the Variants button keep exactly the original seven positions.
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

        // One labelled slider row: name on the left, bar beneath it, percentage on the
        // right. Whole-number 10..30 range (5% per step) is configured by the component.
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

            // The slider itself: background, fill, handle - the standard Unity layout.
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
            slider.transition = Selectable.Transition.None; // the tint script owns the colours

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

        // Grows the aim trail's dot POOL to 100 (long arcs were running out of dots and
        // ending short) and widens the spacing by 20%. The pool is a fixed array of
        // Transforms on the Player prefab, so both are prefab edits; extra dots are
        // clones of the existing ones, so they inherit the exact look.
        [MenuItem("Tools/Kinetic Energy/Expand Aim Trail Dots")]
        public static void ExpandAimTrailDots()
        {
            const int wantDots = 100;
            const float wantSpacing = 1.2f; // was 1.0 - the requested +20%

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

        // Level1Aim1.1: the fully-free midair aim camera (cursor framing OFF - the view
        // follows the raw aim 1:1) plus the grounded 60-65 degree edge-follow with its
        // hard aim clamp. Additive; nothing else in the scene is touched.
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

            // The blue landing arrow lives in THIS scene only for now - the gate also
            // keeps its V / D-pad Left toggle from colliding with the variant-cycling
            // keys in the harness scenes.
            var aimPreview = UnityEngine.Object.FindAnyObjectByType<LandingPreviewController>(FindObjectsInactive.Include);
            if (aimPreview != null)
            {
                aimPreview.landingArrowAvailable = true;
                aimPreview.landingArrowEnabled = true;
                EditorUtility.SetDirty(aimPreview);
            }

            // MOMENTUM stays ON (direct request) - the carry now REDIRECTS the brought
            // speed along the aim (KineticCubeController), so the reach is uniform in
            // all 360 degrees while the momentum is kept.
            var aimMerged = UnityEngine.Object.FindAnyObjectByType<MergedEconomyController>(FindObjectsInactive.Include);
            if (aimMerged != null)
            {
                aimMerged.momentumLaunches = true;
                EditorUtility.SetDirty(aimMerged);
            }

            SaveOpenScene(aimScenePath);
            Debug.Log("KineticEnergySetup: Level1Aim1.1 camera configured OK (framing off, grounded 60-65 follow on)");
        }

        // The two Level-1-derived merged-economy test scenes (Level1Economy and the
        // user-made Level1Challenge copy): tag hidden, combo meter raised to sit ~5px
        // under the premium meter's tall blocks. Also restores Level8's challenge tag,
        // which an earlier pass disabled by mistake. Additive - nothing else touched.
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
                // Slowdown-prefab body top sits at -73; the premium meter's tall blocks
                // end at ~-77.3 canvas units. 20 = the 10px near-gap placement plus the
                // requested further 10px down.
                merged.comboMeterDropPixels = 20f;

                // These scenes carry their OWN BuildInfo text (and their own first-boot
                // key), describing the locked E economy exactly as it currently works.
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

                // The SELF-CONTAINED finish: the old next-scene trigger is neutralised
                // (empty scene name = inert, prefab-instance friendly) and the locked
                // "You win!" pause takes over.
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

                // The combo meter is its own PREFAB in these scenes - the narrowed bar
                // that aligns the xN circle with the energy meter's left edge.
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

            // Level8's tag was disabled on a wrong guess ("Level1Challenge" is its own
            // scene) - back on.
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

        // OTS copies of both economy scenes: exact duplicates whose only change is the
        // aim camera locked to variant D - over-the-shoulder plus the landing
        // picture-in-picture window when the cursor is off screen.
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

        // Level1Economy: an EXACT copy of Level 1 running QuarryEconomy2's merged economy
        // (variants A-E, the X/D-pad-Down auto-max toggle, the 8+2 premium meter, the
        // intro) with the aim camera LOCKED to QuarryNew's variant D - over-the-shoulder
        // plus the landing picture-in-picture window when the cursor is off screen.
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

            // Camera: the Player prefab carries the variant controller and presets in
            // every scene - lock it to OTS + landing window, switching off.
            var cameraVariants = UnityEngine.Object.FindAnyObjectByType<AimCameraVariantController>(FindObjectsInactive.Include);
            if (cameraVariants == null)
            {
                throw new Exception("KineticEnergySetup: no AimCameraVariantController on the Player - run Setup Aim Camera Variants first.");
            }
            cameraVariants.variantSwitchingEnabled = false;
            cameraVariants.currentVariant = AimCameraVariant.OtsParallaxPip;
            EditorUtility.SetDirty(cameraVariants);

            // The LOCKED E-only momentum test (direct request): variant E, momentum
            // launches on, nothing switchable, the boost boundary at 40% with a matching
            // 4+6 meter, and a missed window reverting to 40% instead of zero.
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
            merged.showHudTag = false; // locked scene - the tag says nothing useful
            EditorUtility.SetDirty(merged);

            GameObject pauseSystemGo = GameObject.Find("PauseSystem");
            Transform pauseCanvas = pauseSystemGo != null ? pauseSystemGo.transform.Find("PauseCanvas") : null;
            if (pauseCanvas == null) throw new Exception("KineticEnergySetup: Level1Economy has no PauseSystem/PauseCanvas.");

            // The 4+6 premium meter (boost boundary at 40%) replaces whatever meter the
            // scene carries; the embedded prefab meter is DEACTIVATED, never destroyed.
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

            // The combo-window meter (the repurposed slowdown bar) - Level 1 predates it.
            if (playerController.slowdownMeter == null)
            {
                GameObject slowdownMeter = InstantiatePrefab("SlowdownMeter");
                slowdownMeter.transform.SetParent(pauseCanvas, false);
                playerController.slowdownMeter = slowdownMeter.GetComponent<EnergyMeterController>();
            }
            EditorUtility.SetDirty(playerController);

            // The 1-key momentum toggle is RETIRED - momentum is locked ON through the
            // harness now, so the toggle object leaves the scene.
            var momentumToggle = UnityEngine.Object.FindAnyObjectByType<MomentumLaunchToggle>(FindObjectsInactive.Include);
            if (momentumToggle != null) UnityEngine.Object.DestroyImmediate(momentumToggle.gameObject);

            SaveOpenScene(scenePath);
            Debug.Log("KineticEnergySetup: Level 1 Economy scene setup complete OK (merged economy + OTS/landing-window camera)");
        }

        // ADDITIVE: the first-boot aim-variant explainer overlay for QuarryNew - shown once
        // per game process, dismissed by any input, frozen game underneath.
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

        // The challenge scene (a QuarryNew duplicate): camera locked to Variant A with the
        // switching UI off, plus the ChallengeVariants harness (overcharge scatter first).
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

            // The QuarryNew copy carries the first-boot aim intro - meaningless here.
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

        // Adds the control-scheme A/B harness to QuarryAim and locks its camera variants
        // (the V/C keys belong to the control toggle there now).
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

        // The aim-refinement lab scene (a QuarryNew duplicate): the AimRefinementSettings
        // object activates the refined input pipeline HERE ONLY - every other scene keeps
        // the exact current aim feel. The scene file must already exist (copied from
        // QuarryNew).
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
            if (preset != null) return preset; // existing asset keeps its tuned values
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

        // ==================== Prefab refresh ====================

        // Strips the leftovers of removed systems out of Player.prefab (the facing arrow,
        // the ghost landing preview) and stamps the current tuning + input wiring onto it.
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

                    // The dotted line is composed of SPHERES (direct request).
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

                // The player MODEL is a sphere (direct request) - visual only. Physics keeps
                // the BoxCollider: the footprint BoxCasts, the crash-stick alignment, and the
                // prediction clone are all built around it, and the sphere mesh's 1m diameter
                // sits fully inside the same 1m box.
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

        // Strips the removed radial scheme menu and the stale preview-mode label out of
        // PauseSystem.prefab, and clears the legacy scenes-panel buttons (each level adds
        // its own, current list as instance overrides).
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

                // The energy meter's orange BONUS bar (the ground pound's still-unclaimed
                // boost extra), drawn behind the yellow fill so only the extra pokes out.
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

        // ==================== Core rig spawn (shared by both levels) ====================

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

        // Instantiates the Player / camera rig / pause system prefabs into the open scene
        // and wires every cross-hierarchy reference (a prefab asset cannot hold a reference
        // into a different hierarchy, so all of this has to happen on the scene instances).
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
            // First person may look near-vertical - the midair aim lines up pounds this way.
            rig.orbitCamera.firstPersonMinPitch = -89f;
            rig.orbitCamera.firstPersonMaxPitch = 89f;
            rig.orbitCamera.framingMaxDeviation = 45f;

            // Pause system wiring.
            rig.pauseCanvas = pauseSystem.transform.Find("PauseCanvas");
            rig.pausePanel = rig.pauseCanvas?.Find("PausePanel")?.gameObject;
            rig.scenesPanel = rig.pauseCanvas?.Find("ScenesPanel")?.gameObject;
            rig.pauseController = pauseSystem.GetComponentInChildren<PauseController>(true);
            if (rig.pauseCanvas == null || rig.pausePanel == null || rig.scenesPanel == null || rig.pauseController == null)
            {
                throw new Exception("KineticEnergySetup: PauseSystem prefab is missing expected children.");
            }

            // The top-left ControlsHintLabel is hand-authored in the editor, not wired to
            // the controller - only the pause menu's Controls panel body is script-filled.
            Text controlsBody = rig.pauseCanvas.Find("ControlsPanel/ControlsBody")?.GetComponent<Text>();
            rig.controller.controlsPanelBody = controlsBody;

            // Older PauseSystem builds embedded a meter controller; the current prefab
            // uses the standalone EnergyMeter prefab instead, wired by each setup method
            // AFTER this rig spawns - so a missing embedded meter is expected now.
            Transform meterControllerChild = pauseSystem.transform.Find("EnergyMeter");
            EnergyMeterController meter = meterControllerChild != null ? meterControllerChild.GetComponent<EnergyMeterController>() : null;
            if (meter != null) rig.controller.energyMeter = meter;
            AddMeterDividers(rig.pauseCanvas);

            // Pause menu: a Main Menu button on the pause panel, and the current level list
            // in the Scenes panel.
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

        // The energy meter's 10 divider cells: 9 white lines, 3px wide like the border,
        // laid over the fill area, so charge amounts read in clean tenths.
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

            const float inset = 3f;        // the meter's outline thickness
            const float meterWidth = 320f; // the meter container's fixed width
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

        // A second, smaller bar under the energy meter showing the remaining aim budget
        // (Variant A). The controller hides it in every other slowdown mode.
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
            rt.anchoredPosition = new Vector2(-24f, -66f); // right under the 36px energy meter
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

        // Adds the slowdown (aim budget) meter UI to Level 1 and wires it to the Player -
        // scene ADDITIONS only, nothing existing is moved or re-valued. The controller
        // keeps it disabled while the scene's slowdown mode isn't AimBudget.
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

        // Adds the SlowdownMeter PREFAB to QuarryNew and wires it to the Player - scene
        // ADDITION only, nothing existing is moved or re-valued. The controller keeps it
        // hidden while the scene's slowdown mode isn't AimBudget.
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

        // A small always-on HUD label on its own canvas (below the pause canvas's order).
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

        // ==================== Level 1 - "The Quarry" ====================
        // The concentric-design question: is the launch fun without a game around it?
        // Economy off (infinite energy), no fail state, no finish line. Five zones, each
        // exercising one property of the launch, inside a sticky boundary cage so
        // overshooting parks you on the world's ceiling instead of killing you.

        // The level document sizes the quarry in multiples of a MAX-charge launch (L), which
        // at the current tuning is ~107m - far too sparse in practice. This densifies the
        // whole quarry uniformly (0.25 = a quarter of the distances everywhere, a bowl about
        // 80m across). The Gauntlet deliberately has no such knob, since its beat difficulty
        // depends on gaps being honest fractions of a real max launch.
        const float QuarryScale = 0.25f;

        [MenuItem("Tools/Kinetic Energy/Setup Quarry")]
        public static void SetupQuarry()
        {
            MeasureLaunchDistances(out float L, out float H);
            // The boundary must be sized against the REAL max launch, not the scaled level
            // units - a scaled-down cage would be jumpable.
            float realMaxLaunchHeight = H;
            L *= QuarryScale;
            H *= QuarryScale;
            Debug.Log($"KineticEnergySetup: measured launch units L={L:F1}m (max-charge grounded launch at {ReferenceAimPitchDegrees} degrees), H={H:F1}m (max-charge straight-up apex).");

            NewEmptyScene(QuarryScenePath);

            float width = 3f * L;       // x
            float depth = 3f * L;       // z
            float rimHeight = 2.5f * H; // y of the quarry rim

            Material rockFloor = MakeMaterial("QuarryFloorMaterial", new Color(0.42f, 0.52f, 0.42f));
            Material rockWall = MakeMaterial("QuarryWallMaterial", new Color(0.32f, 0.45f, 0.36f));
            // ONE material for every platform in the level - direct request.
            Material platformMat = MakeMaterial("QuarryPlatformMaterial", new Color(0.30f, 0.62f, 0.40f));

            // --- Terrain container. NOTHING here is sticky by default: stickiness is
            // strictly opt-in via a StickySurface component on the individual object (see
            // MakeSticky calls below - only the chimney interior and the cathedral's
            // hang-spots get one). Add or remove StickySurface on any object to change it. ---
            GameObject terrain = new GameObject("QuarryTerrain");
            Transform tf = terrain.transform;

            // Zone A - the bowl floor: deliberately empty, nothing on it but scale.
            CreateBlock(tf, "QuarryFloor", new Vector3(0f, -1f, 0f), new Vector3(width, 2f, depth), rockFloor);

            // Four vertical rim walls, sunk below the floor so there are no seams.
            // Non-sticky: their own container, no StickySurface.
            GameObject rimWalls = new GameObject("QuarryRimWalls");
            Transform wallsTf = rimWalls.transform;
            const float wallThickness = 8f;
            float wallHeight = rimHeight + 20f;
            float wallCenterY = rimHeight - wallHeight * 0.5f; // top flush with the rim, base sunk 20m under the floor
            CreateBlock(wallsTf, "WallSouth", new Vector3(0f, wallCenterY, -depth * 0.5f - wallThickness * 0.5f),
                new Vector3(width + wallThickness * 2f, wallHeight, wallThickness), rockWall);
            CreateBlock(wallsTf, "WallNorth", new Vector3(0f, wallCenterY, depth * 0.5f + wallThickness * 0.5f),
                new Vector3(width + wallThickness * 2f, wallHeight, wallThickness), rockWall);
            CreateBlock(wallsTf, "WallWest", new Vector3(-width * 0.5f - wallThickness * 0.5f, wallCenterY, 0f),
                new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f), rockWall);
            CreateBlock(wallsTf, "WallEast", new Vector3(width * 0.5f + wallThickness * 0.5f, wallCenterY, 0f),
                new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f), rockWall);

            // Spawn ledge, mid-height on the south wall, looking in.
            float ledgeY = rimHeight * 0.5f;
            Vector3 spawnLedgeCenter = new Vector3(0f, ledgeY - 1f, -depth * 0.5f + 8f);
            CreateBlock(tf, "SpawnLedge", spawnLedgeCenter, new Vector3(16f, 2f, 16f), platformMat);
            Vector3 playerSpawn = spawnLedgeCenter + new Vector3(0f, 1f + 1f, 0f);

            // Wall platforms: the SAME arrangement on every wall - three full-size green
            // platforms per wall, spread along its length at staggered heights (each wall's
            // set is offset a little so opposite walls don't mirror exactly). No wall gets
            // its own special platform type, and none of the old narrow ledges remain.
            Vector3[] wallInward = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            string[] wallNames = { "South", "North", "West", "East" };
            float[] alongOffsets = { -0.85f * L, 0.05f * L, 0.9f * L };
            float[] baseHeights = { 0.35f * H, 0.8f * H, 1.25f * H };
            for (int wall = 0; wall < 4; wall++)
            {
                Vector3 inward = wallInward[wall];
                Vector3 along = new Vector3(inward.z, 0f, inward.x); // horizontal, parallel to the wall
                Vector3 wallFaceCenter = -inward * (1.5f * L - 5f);  // just proud of the wall's inner face
                for (int i = 0; i < 3; i++)
                {
                    float height = baseHeights[(i + wall) % 3] + 0.06f * H * wall;
                    CreateBlock(tf, "Wall" + wallNames[wall] + "Platform" + (i + 1),
                        wallFaceCenter + along * alongOffsets[i] + Vector3.up * height,
                        new Vector3(8f, 1f, 8f), platformMat);
                }
            }

            // Free-floating perches: one per side, a little in from the wall platforms, at
            // staggered heights - the same green as every other platform. (Nothing in this
            // scene carries StickySurface; assign it by hand wherever a surface should hold.)
            GameObject perches = new GameObject("QuarryPerches");
            Vector3[] perchPositions =
            {
                new Vector3(1.15f * L, 0.55f * H, -0.6f * L),  // east side
                new Vector3(-1.15f * L, 0.95f * H, 0.55f * L), // west side
                new Vector3(0.55f * L, 1.35f * H, 1.15f * L),  // north side
                new Vector3(-0.55f * L, 1.7f * H, -1.15f * L), // south side
            };
            for (int i = 0; i < perchPositions.Length; i++)
            {
                CreateBlock(perches.transform, "Perch" + (i + 1), perchPositions[i], new Vector3(8f, 1f, 8f), platformMat);
            }

            // Boundary cage: the rim continues upward as INVISIBLE wall borders, tall enough
            // that even a max-charge launch from the rim can't clear them. No ceiling -
            // gravity is the ceiling. Non-sticky (a boundary is a catch-net, not a perch)
            // and ignored by the aim preview, so the trail never lands on empty sky.
            GameObject cage = new GameObject("BoundaryCage");
            cage.AddComponent<AimPreviewIgnored>();
            float cageTop = rimHeight + realMaxLaunchHeight + 15f;
            float cageWallHeight = cageTop - rimHeight + 8f;
            float cageCenterY = (rimHeight + cageTop) * 0.5f;
            // The world's ceiling: solid (nothing gets above it), non-sticky (brief cling,
            // then you fall back in), and - like the whole cage - invisible to the aim.
            CreateInvisibleBox(cage.transform, "CageCeiling", new Vector3(0f, cageTop + 2f, 0f), new Vector3(width + 40f, 4f, depth + 40f));
            CreateInvisibleBox(cage.transform, "CageSouth", new Vector3(0f, cageCenterY, -depth * 0.5f - 6f), new Vector3(width + 40f, cageWallHeight, 4f));
            CreateInvisibleBox(cage.transform, "CageNorth", new Vector3(0f, cageCenterY, depth * 0.5f + 6f), new Vector3(width + 40f, cageWallHeight, 4f));
            CreateInvisibleBox(cage.transform, "CageWest", new Vector3(-width * 0.5f - 6f, cageCenterY, 0f), new Vector3(4f, cageWallHeight, depth + 40f));
            CreateInvisibleBox(cage.transform, "CageEast", new Vector3(width * 0.5f + 6f, cageCenterY, 0f), new Vector3(4f, cageWallHeight, depth + 40f));

            // The rig, with EnergyEconomy1's exact energy balancing (20% start, last-launch
            // refunds) plus the EnergyEconomy4 ground pound - both are the tuning defaults
            // ApplyPlayerTuning already stamps. Slow-down is unmetered here; no fail state.
            // The only exits are the pause menu and the menu pad below.
            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            rig.controller.slowdownMode = SlowdownMode.Unlimited;
            // The cage means nothing can fall out of the world - park the reset far below
            // the floor so it can never fire.
            rig.controller.fallResetY = -200f;
            rig.freeMove.fallResetY = -200f;
            EditorUtility.SetDirty(rig.controller);
            EditorUtility.SetDirty(rig.freeMove);
            PointCameraAt(rig, new Vector3(0f, 0f, 0f));

            // Return-to-menu pad at the entrance (on the spawn ledge, behind the player).
            Material menuPadMat = MakeMaterial("MenuPadMaterial", new Color(0.95f, 0.6f, 0.15f));
            Vector3 padCenter = spawnLedgeCenter + new Vector3(6f, 1f + 0.1f, -5f);
            CreateBlock(tf, "MenuPadBase", padCenter, new Vector3(3f, 0.2f, 3f), menuPadMat);
            GameObject menuTrigger = new GameObject("MenuPadTrigger");
            menuTrigger.transform.position = padCenter + Vector3.up * 1.5f;
            BoxCollider menuTriggerBox = menuTrigger.AddComponent<BoxCollider>();
            menuTriggerBox.isTrigger = true;
            menuTriggerBox.size = new Vector3(3f, 3f, 3f);
            menuTrigger.AddComponent<FinishLineNextScene>().nextSceneName = "MainMenu";

            // Eight respawning target spheres with a small session counter - a spine for
            // free play, not an objective.
            Text counterLabel = BuildHudLabel("TargetCounterHud", "Targets: 0", new Vector2(0.5f, 1f), new Vector2(0f, -24f), TextAnchor.UpperCenter, 30);
            TargetSphereCounter counter = counterLabel.transform.parent.gameObject.AddComponent<TargetSphereCounter>();
            counter.label = counterLabel;

            Material sphereMat = MakeMaterial("TargetSphereMaterial", new Color(1f, 0.55f, 0.1f));
            // Half strung up the CENTRE at rising heights, one out by each wall.
            Vector3[] spherePositions =
            {
                new Vector3(0f, 0.35f * H, 0f),                // centre, low
                new Vector3(0.12f * L, 0.8f * H, -0.1f * L),   // centre, mid
                new Vector3(-0.1f * L, 1.3f * H, 0.12f * L),   // centre, high
                new Vector3(0f, 1.8f * H, 0f),                 // centre, top
                new Vector3(0f, 0.9f * H, -1.25f * L),         // by the south wall
                new Vector3(0.2f * L, 1.1f * H, 1.25f * L),    // by the north wall
                new Vector3(-1.25f * L, 0.6f * H, 0.2f * L),   // by the west wall
                new Vector3(1.25f * L, 1.5f * H, -0.2f * L),   // by the east wall
            };
            // Respawns land anywhere inside the arena interior, never above Y = 64.
            Vector3 respawnMin = new Vector3(-width * 0.5f + 10f, 4f, -depth * 0.5f + 10f);
            Vector3 respawnMax = new Vector3(width * 0.5f - 10f, Mathf.Min(TargetSphereMaxY, rimHeight - 4f), depth * 0.5f - 10f);
            GameObject spheres = new GameObject("TargetSpheres");
            for (int i = 0; i < spherePositions.Length; i++)
            {
                CreateTargetSphere(spheres.transform, "TargetSphere" + (i + 1), spherePositions[i], sphereMat, counter, respawnMin, respawnMax);
            }

            // The whole ask, on screen once: mess around, stop whenever.
            Text hint = BuildHudLabel("QuarryIntroHud", "Mess around. Stop whenever you want.\n(The orange pad by the spawn returns to the menu.)", new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), TextAnchor.MiddleCenter, 34);
            hint.gameObject.AddComponent<TimedMessage>().displayDuration = 6f;

            SaveOpenScene(QuarryScenePath);
            Debug.Log("KineticEnergySetup: Quarry setup complete OK");
        }

        // ==================== Level 2 - "The Gauntlet" ====================
        // Compares two architectures for the slowdown resource under identical conditions:
        // Variant A (separate aim budget, refills on crash) vs Variant B (bullet time drains
        // the energy tank). Same scene, one flag - the menu buttons pick the variant. A
        // linear corridor of five beats; beat 5 is gated by an energy clamp so the final
        // stretch is always played on a low tank, where the two variants actually diverge.

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

            // Corridor along +x. Platform tops sit near y=0; recovery ledges below; the void
            // past them ends at the fall reset.
            float corridorHalfWidth = 0.75f * L;
            float recoveryY = -0.35f * H;
            float fallReset = -0.6f * H;

            // NOTHING here is sticky by default - stickiness is strictly opt-in via a
            // StickySurface component on the individual object. In this level only beat 3's
            // strip carries one (plus the beat-5 panel's own TimedStickyPanel); every
            // platform top is flat and walkable, so nothing else needs to hold.
            GameObject terrain = new GameObject("GauntletPlatforms");
            Transform tf = terrain.transform;

            var beatRegions = new List<(int beat, Vector3 center, Vector3 size)>();

            // ---- Beat 1 - Baseline: platform, 0.5L gap, platform. One grounded launch. ----
            Vector3 startTop = new Vector3(0.1f * L, 0f, 0f);
            CreateBlock(tf, "Beat1_Start", new Vector3(0.1f * L, -1f, 0f), new Vector3(0.25f * L, 2f, 0.3f * L), platformMat);
            CreateBlock(tf, "Beat1_Landing", new Vector3(0.85f * L, -1f, 0f), new Vector3(0.25f * L, 2f, 0.3f * L), platformMat);
            beatRegions.Add((1, new Vector3(0.1f * L, 4f, 0f), new Vector3(0.25f * L, 10f, 0.3f * L)));
            Vector3 playerSpawn = startTop + new Vector3(-0.05f * L, 1.5f, 0f);

            // ---- Beat 2 - The Fork: one long flight, two valid landings at different
            // heights. Wide-easy low-left, narrow high-right that skips ahead. ----
            beatRegions.Add((2, new Vector3(0.85f * L, 4f, 0f), new Vector3(0.25f * L, 10f, 0.3f * L)));
            CreateBlock(tf, "Beat2_LowLedge", new Vector3(1.6f * L, -0.15f * H - 1f, -0.35f * L), new Vector3(0.25f * L, 2f, 0.25f * L), platformMat);
            CreateBlock(tf, "Beat2_HighLedge", new Vector3(1.75f * L, 0.12f * H - 1f, 0.45f * L), new Vector3(8f, 2f, 8f), platformMat);

            // ---- Beat 3 - The Correction: a launch toward a mostly non-sticky wall with
            // one sticky strip. Missing the strip = 0.3s cling, drop to a recovery ledge. ----
            Vector3 beat3Start = new Vector3(2.3f * L, 0f, 0f);
            CreateBlock(tf, "Beat3_Start", new Vector3(2.3f * L, -1f, 0f), new Vector3(0.2f * L, 2f, 0.3f * L), platformMat);
            beatRegions.Add((3, new Vector3(2.3f * L, 4f, 0f), new Vector3(0.2f * L, 10f, 0.3f * L)));

            // The wall: NOT under the sticky container, so its face clings-and-drops.
            GameObject correctionWall = new GameObject("Beat3_Wall");
            float wallX = 2.9f * L;
            float wallHeight = 0.35f * H;
            CreateBlock(correctionWall.transform, "WallFace", new Vector3(wallX, wallHeight * 0.5f - 0.05f * H, 0f), new Vector3(4f, wallHeight, corridorHalfWidth * 2f), wallMat);
            // The one sticky strip, about a cube-width wide, at three-quarters height.
            GameObject strip = CreateBlock(null, "Beat3_StickyStrip",
                new Vector3(wallX - 2.05f, wallHeight * 0.75f - 0.05f * H, 0f), new Vector3(0.3f, 6f, 1.5f), stripMat);
            strip.AddComponent<StickySurface>().sticky = true;
            // Recovery ledge at the wall's base, catching the cling-drop.
            CreateBlock(tf, "Beat3_Recovery", new Vector3(wallX - 0.06f * L, recoveryY, 0f), new Vector3(0.15f * L, 2f, 0.3f * L), recoveryMat);
            // Beat 4's start sits past the wall - from the strip, hop over the top.
            CreateBlock(tf, "Beat4_Start", new Vector3(3.1f * L, -1f, 0f), new Vector3(0.2f * L, 2f, 0.3f * L), platformMat);

            // ---- Beat 4 - The Splitter (the crux): a crossing too wide for one launch,
            // demanding TWO separate midair aims. With a 2-second budget, doing both
            // carefully overruns - one of them has to happen at full speed. ----
            beatRegions.Add((4, new Vector3(3.1f * L, 4f, 0f), new Vector3(0.2f * L, 10f, 0.3f * L)));
            CreateBlock(tf, "Beat4_LandingPad", new Vector3(4.7f * L, -1f, 0f), new Vector3(8f, 2f, 8f), platformMat);
            // The generous recovery ledge under the whole crossing - failable repeatedly
            // without a restart. From it, launch back up to the beat 4 start.
            CreateBlock(tf, "Beat4_Recovery", new Vector3(3.95f * L, recoveryY, 0f), new Vector3(1.4f * L, 2f, 0.5f * L), recoveryMat);

            // ---- Beat 5 - The Dry Run: the energy clamp guarantees a low tank, then a
            // small target pad across a gap with a timed sticky panel as the only staging
            // point. Under Variant A thinking is free and moving is expensive; under
            // Variant B they compete for the same nearly-empty tank. ----
            beatRegions.Add((5, new Vector3(4.7f * L, 4f, 0f), new Vector3(8f, 10f, 8f)));
            // The clamp, wrapped around the beat-4 landing pad so every arrival (and every
            // recovery re-entry) replays the beat on ~25%.
            GameObject clamp = new GameObject("Beat5_EnergyClamp");
            clamp.transform.position = new Vector3(4.7f * L, 4f, 0f);
            BoxCollider clampBox = clamp.AddComponent<BoxCollider>();
            clampBox.isTrigger = true;
            clampBox.size = new Vector3(10f, 10f, 10f);
            clamp.AddComponent<EnergyClampTrigger>().clampFraction = 0.25f;

            // The timed sticky panel mid-gap (2-second hold), and the small final pad.
            GameObject panel = CreateBlock(null, "Beat5_TimedPanel", new Vector3(5.0f * L, -0.05f * H, 0f), new Vector3(6f, 1f, 6f), panelMat);
            TimedStickyPanel timedPanel = panel.AddComponent<TimedStickyPanel>();
            timedPanel.holdSeconds = 2f;
            CreateBlock(tf, "Beat5_TargetPad", new Vector3(5.35f * L, -1f, 0f), new Vector3(6f, 2f, 6f), platformMat);
            // Recovery under the final gap, with enough room to relaunch back to the pad.
            CreateBlock(tf, "Beat5_Recovery", new Vector3(5.05f * L, recoveryY, 0f), new Vector3(0.6f * L, 2f, 0.4f * L), recoveryMat);

            // ---- Rig, instrumentation, finish. ----
            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            rig.controller.startingEnergyFraction = 1f; // the corridor is tuned around a full-tank start
            rig.controller.slowdownMode = SlowdownMode.AimBudget; // Variant A unless the menu picked B
            rig.controller.maxLaunchesPerFlight = 3; // beat 4 needs a grounded launch plus two midair redirects
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

            // Finish-line trigger immediately after the target pad, with a visible marker.
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

        // ==================== Level 1 - platform run into wall hops ====================
        // A series of platforms whose gaps grow, demanding increasingly more launch energy,
        // then a few sticky floating walls to hop between, over a red DamageWalls floor that
        // instantly respawns the player at the start. The Player/camera tuning is COPIED
        // from the Quarry scene's current instances (the values the user hand-tuned), so
        // this level plays identically - nothing existing is rebuilt or re-valued.
        [MenuItem("Tools/Kinetic Energy/Setup Level 1")]
        public static void SetupLevel1()
        {
            // Capture the hand-tuned Player/camera state from the Quarry first.
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

            // The platform run: gaps grow with every jump, so each one needs more energy.
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

            // The wall hops: a few sticky floating walls to jump between, then the end pad.
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

            // The hazard: a red DamageWalls floor under the whole course - touch it and you
            // respawn instantly at the start.
            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(endX * 0.5f, -12f, 0f), new Vector3(endX + 60f, 2f, 80f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            // Finish on the end pad returns to the menu.
            GameObject finish = new GameObject("FinishTrigger");
            finish.transform.position = new Vector3(endX, 2f, 0f);
            BoxCollider finishBox = finish.AddComponent<BoxCollider>();
            finishBox.isTrigger = true;
            finishBox.size = new Vector3(4f, 4f, 8f);
            finish.AddComponent<FinishLineNextScene>().nextSceneName = "MainMenu";

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(platformSize.x + gapFractions[0] * L, 0f, 0f));

            // Stamp the Quarry's hand-tuned values over the fresh instances, keeping this
            // scene's own object wiring (meter, camera, input references) intact.
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);

            SaveOpenScene(Level1ScenePath);
            Debug.Log($"KineticEnergySetup: Level 1 setup complete OK (L={L:F1}m; gaps "
                + string.Join(", ", Array.ConvertAll(gapFractions, g => (g * L).ToString("F0"))) + "m)");
        }

        // Level 1's gradual-drain test: sets ONLY the gradualLaunchDrain wiring flag on the
        // scene's Player instance - no other value is read or written (the user tunes
        // everything else in the Inspector).
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

        // Level 1's wall-crash launch limit: sets ONLY the wallCrashLaunchAllowance wiring
        // value (1 launch per non-grounding crash) on the scene's Player instance - no
        // positions and no other values are touched.
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

        // Turns the reusable pieces into prefab ASSETS (direct request):
        //  - FinishTrigger, PlayerShadow and SlowdownMeter are converted from Level 1's
        //    existing instances (SaveAsPrefabAssetAndConnect keeps their positions and
        //    values; PlayerShadow's cross-hierarchy player reference is re-wired on the
        //    scene instance afterwards, since a prefab asset cannot hold it).
        //  - EnergyMeter is built fresh as a standalone prefab (the live meters sit inside
        //    the PauseSystem prefab and stay untouched) - drop it on any canvas and wire
        //    the Player's energyMeter field to it.
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
                // Cross-hierarchy wiring must be restored on the instance after the save.
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

        // A standalone, self-contained energy meter prefab: the full bar stack (outline,
        // backdrop, orange bonus, yellow energy, blue charge, 10-cell dividers) with the
        // EnergyMeterController ON the container. Anchored top-right like the live meters.
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

                // The 10-cell dividers, matching the live meters.
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

        // Swaps every plain (non-prefab) instance of the HUD pieces for an instance of the
        // corresponding prefab, carrying position/values over as instance overrides and
        // re-wiring the Player's references. Objects already connected to the prefabs
        // (Level 1's) are left alone; the PauseSystem's built-in meter UI is deactivated
        // per instance and replaced by the standalone EnergyMeter prefab.
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

                // --- PlayerShadow ---
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

                // --- Energy meter: deactivate the PauseSystem's built-in UI, drop in the
                // standalone prefab at the same canvas slot, re-wire the Player. ---
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

                    // --- Slowdown meter ---
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

                // --- Finish triggers of the FinishLineNextScene kind (Level 1's finish, the
                // Quarry's menu pad) - the Gauntlet's own GauntletFinishLine stays as it is. ---
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

        // Unity never propagates a prefab ROOT's transform to its instances (each placement
        // owns its root position/size by design), and the meters' whole layout sat on the
        // root RectTransform - which is why asset edits didn't show up in scenes. This
        // moves each meter's layout onto an inner "Body" child (prefab-driven, so edits DO
        // propagate) and zeroes the existing instances' roots once so nothing shifts.
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
                if (root.transform.Find("Body") != null) return; // already restructured

                RectTransform rootRt = root.GetComponent<RectTransform>();
                GameObject body = new GameObject("Body", typeof(RectTransform));
                RectTransform bodyRt = body.GetComponent<RectTransform>();
                body.transform.SetParent(root.transform, false);

                // The Body inherits the whole layout the root used to carry...
                bodyRt.anchorMin = rootRt.anchorMin;
                bodyRt.anchorMax = rootRt.anchorMax;
                bodyRt.pivot = rootRt.pivot;
                bodyRt.anchoredPosition = rootRt.anchoredPosition;
                bodyRt.sizeDelta = rootRt.sizeDelta;

                // ...and every visual moves under it (Body itself stays the last-created
                // child until the loop empties the root, so ordering is preserved).
                var toMove = new List<Transform>();
                foreach (Transform child in root.transform)
                {
                    if (child != body.transform) toMove.Add(child);
                }
                foreach (Transform child in toMove)
                {
                    child.SetParent(body.transform, false);
                }

                // The root becomes a pure zero-size anchor point.
                rootRt.anchoredPosition = Vector2.zero;
                rootRt.sizeDelta = Vector2.zero;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // The moving platform as a drag-and-drop prefab: green 8x8 block + MovingPlatform
        // (public moveOffset, lapSeconds, arrow settings). The lead arrow builds itself at
        // runtime, so the prefab stays one self-contained piece.
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

        // Gives the MovingPlatform prefab its own blueish-green (teal) material so movers
        // read differently from static platforms at a glance - applied on the prefab
        // asset, so every placed instance updates automatically.
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

        // ==================== Level 2 - moving platforms ====================
        // Static platforms interleaved with MovingPlatform prefab instances: a sideways
        // ferry, an along-the-path shuttle and a vertical lift, over a DamageWalls floor.
        // Player/camera tuning is copied from the Quarry's current hand-tuned instances,
        // exactly like Level 1 - nothing existing is rebuilt or re-valued.
        [MenuItem("Tools/Kinetic Energy/Setup Level 2")]
        public static void SetupLevel2()
        {
            // Capture the hand-tuned Player/camera state from the Quarry first.
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

            // Static stepping stones...
            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);
            CreateBlock(tf, "Static1", new Vector3(0.3f * L, -1f, 0f), platformSize, platformMat);
            CreateBlock(tf, "Static2", new Vector3(0.95f * L, -1f, 0.3f * L), platformSize, platformMat);
            CreateBlock(tf, "Static3", new Vector3(1.75f * L, -1f, 0.3f * L), platformSize, platformMat);
            CreateBlock(tf, "EndPlatform", new Vector3(2.3f * L, -1f, 0.3f * L), platformSize, platformMat);

            // ...interleaved with movers. The blue lead arrow appears on each while aiming
            // midair, its tip at the centre's position when the previewed shot lands.
            SpawnMovingPlatform("Mover1_SidewaysFerry", new Vector3(0.62f * L, -1f, 0f), new Vector3(0f, 0f, 0.3f * L), 7f);
            SpawnMovingPlatform("Mover2_PathShuttle", new Vector3(1.25f * L, -1f, 0.3f * L), new Vector3(0.25f * L, 0f, 0f), 5f);
            SpawnMovingPlatform("Mover3_Lift", new Vector3(2.05f * L, -1f, 0.3f * L), new Vector3(0f, 14f, 0f), 6f);

            // The hazard floor: touch it and you respawn at the start instantly.
            GameObject respawnPoint = new GameObject("RespawnPoint");
            respawnPoint.transform.position = playerSpawn;
            GameObject damageFloor = CreateBlock(null, "DamageFloor",
                new Vector3(1.15f * L, -12f, 0.15f * L), new Vector3(2.3f * L + 60f, 2f, L + 60f), damageMat);
            DamageWalls damage = damageFloor.AddComponent<DamageWalls>();
            damage.respawnPoint = respawnPoint.transform;
            EditorUtility.SetDirty(damage);

            // The finish, as the FinishTrigger prefab.
            GameObject finish = InstantiatePrefab("FinishTrigger");
            finish.transform.position = new Vector3(2.3f * L, 2f, 0.3f * L);
            FinishLineNextScene finishComp = finish.GetComponent<FinishLineNextScene>();
            if (finishComp != null) finishComp.nextSceneName = "MainMenu";
            EditorUtility.SetDirty(finish);

            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            PointCameraAt(rig, new Vector3(0.3f * L, 0f, 0f));

            // Stamp the Quarry's hand-tuned values over the fresh instances, keeping this
            // scene's own object wiring intact.
            OverwriteSerializedValuesKeepObjectRefs(rig.controller, controllerJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.freeMove, moveJson);
            OverwriteSerializedValuesKeepObjectRefs(rig.orbitCamera, cameraJson);

            SaveOpenScene(Level2ScenePath);
            Debug.Log($"KineticEnergySetup: Level 2 setup complete OK (L={L:F1}m, 3 movers)");
        }

        // The wandering ground enemy as a drag-and-drop prefab: magenta sphere + Enemy
        // (wander mode dropdown, radius, edge margin, speed - all public per instance).
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

        // Updates the Enemy prefab's stored walking speed to the new default (4.5 - 50%
        // faster) - instances without their own speed override follow automatically.
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

        // ==================== Level 3 - enemies ====================
        // Wandering enemies on an open arena and on platforms - launch through them to
        // clear the way. Player/camera tuning is copied from LEVEL 1's current instances,
        // so all three levels share the exact same values.
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

            // Start pad, then an open arena patrolled by radius-mode enemies.
            CreateBlock(tf, "StartPlatform", new Vector3(0f, -1f, 0f), platformSize, platformMat);
            Vector3 playerSpawn = new Vector3(0f, 1.5f, 0f);
            CreateBlock(tf, "Arena", new Vector3(0.5f * L, -1f, 0f), new Vector3(0.45f * L, 2f, 0.45f * L), platformMat);
            SpawnEnemy("ArenaEnemy1", new Vector3(0.42f * L, 1f, -6f), EnemyWanderMode.WithinRadius, 10f, 1.5f);
            SpawnEnemy("ArenaEnemy2", new Vector3(0.58f * L, 1f, 6f), EnemyWanderMode.WithinRadius, 12f, 1.5f);

            // Two platform hops, each patrolled edge-to-edge by a platform-surface enemy.
            CreateBlock(tf, "Hop1", new Vector3(0.95f * L, -1f, 0.1f * L), new Vector3(14f, 2f, 14f), platformMat);
            SpawnEnemy("Hop1Enemy", new Vector3(0.95f * L, 1f, 0.1f * L), EnemyWanderMode.PlatformSurface, 8f, 1.5f);
            CreateBlock(tf, "Hop2", new Vector3(1.35f * L, -1f, -0.05f * L), new Vector3(14f, 2f, 14f), platformMat);
            SpawnEnemy("Hop2Enemy", new Vector3(1.35f * L, 1f, -0.05f * L), EnemyWanderMode.PlatformSurface, 8f, 2f);

            CreateBlock(tf, "EndPlatform", new Vector3(1.7f * L, -1f, 0f), platformSize, platformMat);

            // Hazard floor + respawn + finish, as in the other levels.
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

        // ==================== Level 4 - flying enemies ====================

        // The flying enemy prefab: a magenta sphere with the FlyingEnemy component - every
        // tunable public on it. Idempotent; an existing prefab keeps its tuned values.
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

        // Swaps a component for a SUBCLASS in place, carrying every serialized value over
        // (m_Script excluded, so the new type sticks) - the literal "inherits all their
        // behaviour and values" for the enemy variant prefabs.
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

        // The sized ground enemy: one prefab, Small/Medium/Large picked per placed
        // instance on the SizedEnemy component. Values inherited from Enemy.prefab.
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

        // The armoured flyer: FlyingEnemy plus a golden back cube - the only killable
        // spot. Values inherited from FlyingEnemy.prefab.
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

            // The back cube: parked on top of the sphere, slightly sticking out. Its own
            // collider is what the crash pipeline must report for a kill.
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

        // A SMALL flying-gauntlet: islands over a hazard floor, guarded by projectile-
        // shooting flyers. Player tuning copied from Level 3 (the previous reference).
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

            // Small island run over the void: every crossing is covered by a flyer's
            // firing lane, so the route is dodge-or-be-swatted.
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

            // Standalone HUD meter prefabs, wired like every other level.
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

        // ==================== Level 9 - sized enemies / Level 10 - weak-spot flyers ====================

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

        // A stepped run of arenas, one SIZE CLASS per step: the small (20% kill, fast)
        // greets first, the medium (40%) guards the climb, the large (60%, hard-hitting)
        // holds the last arena - the billboard percentages teach the escalation. Player
        // tuning copied from Level 7 (the ground-enemy reference).
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

            // The final arena pairs the LARGE with a second small - the player has to
            // budget a 60% launch while a fast 20% pest is on the same floor.
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

        // A CLIMBING island run: every weak-spot flyer hovers just below the next island,
        // so the route above them - the back cube is the only kill spot - is always
        // there, and every crossing passes over a flyer's patrol. Player tuning copied
        // from Level 4 (the flyer reference).
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

        // ==================== Level 7 - hunter enemies ====================

        // The HUNTER prefab: the ground enemy with its variant flags on - attacks airborne
        // players, launches back to the nearest platform instead of falling, and sees
        // further. Crimson so it reads as the dangerous cousin.
        [MenuItem("Tools/Kinetic Energy/Create Hunter Enemy Prefab")]
        public static void CreateHunterEnemyPrefab()
        {
            string path = PrefabFolder + "/HunterEnemy.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                // Prefab exists - stamp the newer hunter capabilities onto it (dodging),
                // leaving every user-tuned value alone.
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

        // Hunter variant B: the STALKER - killable only during its attack TELEGRAPH (it is
        // committed then and cannot dodge), untouchable the rest of the time.
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

        // A stepped platform cluster over the void - hunters roam it, punish airborne
        // crossings, and hop back when baited off edges. Tuning copied from Level 6.
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

            // A stepped cluster: heights vary, so airborne crossings are constant - which
            // is exactly what hunters punish.
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

        // ==================== Level 8 - the challenge gauntlet ====================

        // The translucent hazard look - a MakeMaterial with the URP Lit transparent
        // surface switched on, so the purple walls read as a barrier without hiding the
        // level behind them.
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

            // A unit cube scaled per use: the chase wall stretches its scene instance, the
            // seal walls get sealWallSize at spawn. Trigger collider - death on touch, no
            // physical shove - and a kinematic body so the moving variant sweeps properly.
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

        // Level 1's challenge twin: the same growing-gap platform run, but played FOUR
        // times in sequence - limited slowdown, overcharge scatter, the chasing wall, and
        // the sealing walls - advancing at the end pad. The pause Scenes panel gets a
        // second column that jumps straight to a stage (always a restart of the level).
        [MenuItem("Tools/Kinetic Energy/Setup Level 8")]
        public static void SetupLevel8()
        {
            const string level8Path = "Assets/Scenes/Level8.unity";

            // Tuning copied from Level 1 - this level should feel identical to play.
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

            // Level 1's run: gaps grow with every jump. The last platform IS the end pad.
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

            // The chase wall: one stretched DeathWall parked behind the start, sweeping
            // toward the end. Speed and start position are edited on this instance.
            GameObject chaseGo = InstantiatePrefab("DeathWall");
            chaseGo.name = "ChaseWall";
            chaseGo.transform.position = new Vector3(-1.2f * L, 13f, 0f);
            chaseGo.transform.localScale = new Vector3(2f, 46f, 70f);
            DeathWall chase = chaseGo.GetComponent<DeathWall>();
            chase.moveSpeed = 4f;
            chase.moveDirection = Vector3.right;
            EditorUtility.SetDirty(chase);

            // The end pad advances the stage sequence instead of loading another scene.
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

            // The challenge column in the Scenes panel: four direct-to-stage buttons next
            // to the ordinary scene list, wired to the PauseController stage loaders.
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

            // Level 8 reloads ITSELF by name (stage advance + the pause stage buttons), so
            // it must sit in Build Settings - appended once, existing entries untouched.
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(s => s.path == level8Path))
            {
                buildScenes.Add(new EditorBuildSettingsScene(level8Path, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }

            SaveOpenScene(level8Path);
            Debug.Log($"KineticEnergySetup: Level 8 setup complete OK (L={L:F1}m, 4 challenge stages)");
        }

        // ==================== Level 5 - turrets / Level 6 - laser walls ====================

        [MenuItem("Tools/Kinetic Energy/Create Turret Prefab")]
        public static void CreateTurretPrefab()
        {
            string path = PrefabFolder + "/TurretEnemy.prefab";
            // Turrets are ENEMIES, so they wear the shared enemy colour (direct request) -
            // the same EnemyMaterial the ground enemy uses, and the same windup flash.
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
            temp.transform.localScale = new Vector3(1.4f, 0.9f, 1.4f); // squat cylinder
            temp.GetComponent<Renderer>().sharedMaterial = material;
            TurretEnemy turret = temp.AddComponent<TurretEnemy>();
            turret.windUpColor = new Color(1f, 0.35f, 0.1f);
            PrefabUtility.SaveAsPrefabAsset(temp, path);
            UnityEngine.Object.DestroyImmediate(temp);
            Debug.Log("KineticEnergySetup: TurretEnemy prefab created OK");
        }

        // A walled corridor watched by fixed turrets - two on the flanking walls, one on a
        // pedestal mid-course. Player tuning copied from Level 4.
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

            // The corridor: hop platforms between two tall flanking walls.
            CreateBlock(tf, "Hop1", new Vector3(0.4f * L, -1f, 0f), new Vector3(14f, 2f, 14f), platformMat);
            CreateBlock(tf, "Hop2", new Vector3(0.8f * L, -1f, 0.06f * L), new Vector3(14f, 2f, 14f), platformMat);
            CreateBlock(tf, "EndPlatform", new Vector3(1.2f * L, -1f, 0f), platformSize, platformMat);

            float wallLength = 1.3f * L;
            CreateBlock(tf, "WallLeft", new Vector3(0.6f * L, 8f, -corridorHalfWidth), new Vector3(wallLength, 20f, 2f), wallMat);
            CreateBlock(tf, "WallRight", new Vector3(0.6f * L, 8f, corridorHalfWidth), new Vector3(wallLength, 20f, 2f), wallMat);

            // Wall turrets: cylinder axis pointing INTO the corridor (half-embedded).
            SpawnTurret("WallTurretLeft", new Vector3(0.35f * L, 8f, -corridorHalfWidth + 1.2f), new Vector3(-90f, 0f, 0f));
            SpawnTurret("WallTurretRight", new Vector3(0.85f * L, 9f, corridorHalfWidth - 1.2f), new Vector3(90f, 0f, 0f));
            // Pedestal turret guarding the middle hop, upright on its column.
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

        // A runway crossed by blinking laser gates - staggered phases, so the route is a
        // rhythm read. Player tuning copied from Level 5.
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

            // One long runway; gates cross it at intervals with alternating phases.
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

        // The laser gate PREFAB: two grey columns (24 apart, 12 high) + a Beams root
        // (kinematic rigidbody + DamageWalls) that LaserWall fills with red beam cylinders
        // at runtime from its public fields. The DamageWalls respawn point CANNOT live in
        // the prefab (cross-hierarchy scene reference) - wired per instance.
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

        // Instantiates the LaserGate prefab and wires the per-instance bits: position,
        // timing overrides, and the scene's respawn point onto the beams' DamageWalls.
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

        // Standalone HUD meter prefabs, wired the way every level does it now.
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

        // Copies EVERY serialized value on Level 1's Player/free-move/camera onto Level 2's
        // instances (keeping Level 2's own object wiring) - the two levels then play with
        // exactly the same tuning, flags included.
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

        // Stamps another component's serialized state (as EditorJsonUtility JSON) onto
        // target, then restores every UnityEngine.Object reference target had before - the
        // JSON's refs are instance IDs from a scene no longer loaded, while the target's own
        // wiring must stay this scene's.
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

        // ==================== Main menu ====================

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

        // ADDITIVE: drops a Feedback button (opens the playtest form URL) into the existing
        // main menu, next to Quit - nothing existing is moved or re-valued. The playable
        // scenes get theirs through the PauseSystem prefab's converted Scenes button.
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
                new Vector2(340f, -190f), new Vector2(300f, 70f)); // beside Quit, same row
            WireButton(feedbackBtn, menu.OnFeedbackClicked);
            EditorUtility.SetDirty(menu);

            SaveOpenScene(MainMenuScenePath);
            Debug.Log("KineticEnergySetup: feedback button added to main menu OK");
        }

        // ==================== Scene / geometry helpers ====================

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

        // Stickiness is strictly opt-in, per object - a surface only holds a crash if it
        // itself carries a StickySurface component, visible in the Inspector.
        static GameObject MakeSticky(GameObject go)
        {
            go.AddComponent<StickySurface>().sticky = true;
            return go;
        }

        // Solid and sticky-taggable but never rendered - the boundary cage. A plain
        // GameObject with only a BoxCollider: the landing prediction picks it up like any
        // other static collider, so the trail honestly shows a landing on the world's edge.
        static GameObject CreateInvisibleBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            return go;
        }

        // Targets never sit (or respawn) above this height - direct request.
        const float TargetSphereMaxY = 64f;

        static void CreateTargetSphere(Transform parent, string name, Vector3 position, Material material, TargetSphereCounter counter, Vector3 respawnMin, Vector3 respawnMax)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, true);
            position.y = Mathf.Min(position.y, TargetSphereMaxY);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 2.25f; // 75% of the original 3m diameter
            go.GetComponent<Renderer>().sharedMaterial = material;
            // SOLID on purpose: the player crash-lands on the sphere (normal refund), it
            // vanishes, and the aim preview treats it as a genuine landing target.
            TargetSphere sphere = go.AddComponent<TargetSphere>();
            sphere.counter = counter;
            sphere.respawnAreaMin = respawnMin;
            sphere.respawnAreaMax = respawnMax;
            EditorUtility.SetDirty(sphere);
        }

        // URP ships with a ~50m max shadow distance - at this project's arena sizes that
        // leaves most of the level shadowless while nearby blocks are shadowed, which reads
        // as broken. Raised on every URP quality asset in Assets/Settings.
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
            // Real-time shadows for the world; the player's own renderers have casting off
            // and use the PlayerShadow drop-disc instead.
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

        // A flat dark disc kept directly under the player by PlayerShadow - the only shadow
        // the player casts (its renderers don't cast real ones). Instantiates the
        // PlayerShadow prefab when it exists (and wires the cross-hierarchy player ref);
        // the from-scratch construction below is the fallback for before the prefab existed.
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

            // UNLIT on purpose - a lit black disc picks up lighting/shadowing and stops
            // reading as a shadow at all.
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

        // ==================== Materials ====================

        static Material MakeMaterial(string assetName, Color color)
        {
            Material mat = new Material(FindBestShader());
            mat.color = color;
            return SaveMaterialAsset(mat, assetName);
        }

        // A Material created via `new Material(...)` is a loose object - it must be saved as
        // a real asset or the renderer's slot serializes as null and renders pink.
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
                // NEVER overwrite a material that already exists. These are hand-tuned in
                // the editor, and this used to stamp the setup script's own colour and
                // smoothness back over them on every re-run - silently undoing that work.
                // A setup method asks for a material by NAME; the asset on disk is the
                // authority on what it looks like.
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

        // ==================== UI helpers ====================

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

            // The accent lives on the base image (normalColor stays white so it shows
            // undistorted); the ColorBlock states are purely a brighten/dim pulse on top.
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

        // A plain solid-color fill bar - Image.Type.Filled/Horizontal stretched over the
        // parent rect (minus inset). Needs a real sprite: a Filled Image with no sprite
        // silently renders as a full rectangle regardless of fillAmount.
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

        // A flat, un-sliced 4x4 white square saved as a real project asset (an in-memory
        // Sprite.Create result has no asset path and wouldn't survive being referenced from
        // a saved scene). Generated once and reloaded on every later run.
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

        // Persistent listeners only: a runtime AddListener from an Editor script is not
        // serialized and silently vanishes on reload - these are the programmatic
        // equivalent of wiring onClick in the Inspector by hand.
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

        // ==================== Asset lookups ====================

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
