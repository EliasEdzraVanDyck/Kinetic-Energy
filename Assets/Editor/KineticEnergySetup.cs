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
                existing.shader = mat.shader;
                existing.CopyPropertiesFromMaterial(mat);
                EditorUtility.SetDirty(existing);
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
