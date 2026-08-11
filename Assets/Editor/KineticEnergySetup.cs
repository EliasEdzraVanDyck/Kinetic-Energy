using System;
using System.Collections.Generic;
using System.IO;
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
        const float UpDownChargeSpeedMultiplier = 1.5f;
        const float EnergyCostPerFullCharge = 1f;
        const float MinEnergyReserve = 0.05f;
        const float MidairRefundSpendFactor = 0.3f;
        const float GroundPoundRefundMultiplier = 1.7f;
        const float GroundPoundMinRefund = 0.1f;
        const float ChargeTimeScale = 0.2f;
        const float LaunchFlightTimeScale = 2f;
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
            controller.upDownChargeSpeedMultiplier = UpDownChargeSpeedMultiplier;
            controller.energyCostPerFullCharge = EnergyCostPerFullCharge;
            controller.minEnergyReserve = MinEnergyReserve;
            controller.midairRefundSpendFactor = MidairRefundSpendFactor;
            controller.groundPoundRefundMultiplier = GroundPoundRefundMultiplier;
            controller.groundPoundMinRefund = GroundPoundMinRefund;
            controller.chargeTimeScale = ChargeTimeScale;
            controller.launchFlightTimeScale = LaunchFlightTimeScale;
            controller.aimBudgetSeconds = AimBudgetSeconds;
            controller.tankDrainPerSecond = TankDrainPerSecond;
            controller.dialStickRate = DialStickRate;
            controller.dialWheelStep = DialWheelStep;
            controller.defaultAimPitch = -ReferenceAimPitchDegrees; // negative tilts UP
            controller.stickAimForwardAngle = 30f;
            controller.stickAimForwardNeutralAngle = 5f;
            controller.stickAimDeadzone = 0.9f;
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
            controller.groundedAimWithMouse = false;
            controller.groundedMouseAimSensitivity = 0.15f;
            controller.wasdCameraTurnMultiplier = 1.5f;

            controller.moveAction = FindActionReference("Player", "Move");
            controller.groundedAimAction = FindActionReference("Player", "Launch");
            controller.groundedLaunchAction = FindActionReference("Player", "Fire");
            controller.upLaunchAction = FindActionReference("Player", "LaunchUp");
            // West's button - the action kept its historical asset name.
            controller.groundPoundAction = FindActionReference("Player", "SelectGhostPreview");
            controller.cancelChargeAction = FindActionReference("Player", "CancelCharge");
            // Right Bumper - ditto.
            controller.trailToggleAction = FindActionReference("Player", "SwitchControlScheme");
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
                    preview.initialMode = PredictionMode.Trail;
                    controller.landingPreview = preview;
                }
                controller.aimArrow = root.GetComponentInChildren<AimArrowIndicator>(true);

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

            // Pause system wiring.
            rig.pauseCanvas = pauseSystem.transform.Find("PauseCanvas");
            rig.pausePanel = rig.pauseCanvas?.Find("PausePanel")?.gameObject;
            rig.scenesPanel = rig.pauseCanvas?.Find("ScenesPanel")?.gameObject;
            rig.pauseController = pauseSystem.GetComponentInChildren<PauseController>(true);
            if (rig.pauseCanvas == null || rig.pausePanel == null || rig.scenesPanel == null || rig.pauseController == null)
            {
                throw new Exception("KineticEnergySetup: PauseSystem prefab is missing expected children.");
            }

            Text controlsHint = rig.pauseCanvas.Find("ControlsHintLabel")?.GetComponent<Text>();
            Text controlsBody = rig.pauseCanvas.Find("ControlsPanel/ControlsBody")?.GetComponent<Text>();
            rig.controller.controlsHintLabel = controlsHint;
            rig.controller.controlsPanelBody = controlsBody;

            Transform meterControllerChild = pauseSystem.transform.Find("EnergyMeter");
            EnergyMeterController meter = meterControllerChild != null ? meterControllerChild.GetComponent<EnergyMeterController>() : null;
            if (meter == null) throw new Exception("KineticEnergySetup: PauseSystem prefab has no EnergyMeter controller child.");
            rig.controller.energyMeter = meter;
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
            DestroyDirectChildIfExists(rig.pauseCanvas, "SlowdownMeter");
            GameObject container = new GameObject("SlowdownMeter", typeof(RectTransform));
            container.transform.SetParent(rig.pauseCanvas, false);
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
            rig.controller.slowdownMeter = meter;
            EditorUtility.SetDirty(rig.controller);
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

        [MenuItem("Tools/Kinetic Energy/Setup Quarry")]
        public static void SetupQuarry()
        {
            MeasureLaunchDistances(out float L, out float H);
            Debug.Log($"KineticEnergySetup: measured launch units L={L:F1}m (max-charge grounded launch at {ReferenceAimPitchDegrees} degrees), H={H:F1}m (max-charge straight-up apex).");

            NewEmptyScene(QuarryScenePath);

            float width = 3f * L;       // x
            float depth = 3f * L;       // z
            float rimHeight = 2.5f * H; // y of the quarry rim
            float cageTop = 3f * H;     // sticky world ceiling

            Material rockFloor = MakeMaterial("QuarryFloorMaterial", new Color(0.42f, 0.52f, 0.42f));
            Material rockWall = MakeMaterial("QuarryWallMaterial", new Color(0.32f, 0.45f, 0.36f));
            Material ledgeMat = MakeMaterial("QuarryLedgeMaterial", new Color(0.30f, 0.62f, 0.40f));
            Material perchMat = MakeMaterial("QuarryPerchMaterial", new Color(0.45f, 0.55f, 0.72f));
            Material markerMat = MakeMaterial("QuarryMarkerMaterial", new Color(0.95f, 0.85f, 0.25f));

            // --- Terrain container: everything under it is sticky (GetComponentInParent). ---
            GameObject terrain = new GameObject("QuarryTerrain");
            terrain.AddComponent<StickySurface>().sticky = true;
            Transform tf = terrain.transform;

            // Zone A - the bowl floor: deliberately empty, nothing on it but scale.
            CreateBlock(tf, "QuarryFloor", new Vector3(0f, -1f, 0f), new Vector3(width, 2f, depth), rockFloor);

            // Four rim walls, sloping slightly inward (~8 degrees) so no face is a pure
            // vertical box. Oversized and sunk below the floor so the tilt leaves no seams.
            const float wallTilt = 8f;
            const float wallThickness = 8f;
            float wallHeight = rimHeight + 20f;
            float lean = Mathf.Sin(wallTilt * Mathf.Deg2Rad) * wallHeight * 0.5f;
            CreateRotatedBlock(tf, "WallSouth", new Vector3(0f, rimHeight * 0.5f, -depth * 0.5f - wallThickness * 0.5f + lean),
                new Vector3(width + wallThickness * 2f, wallHeight, wallThickness), Quaternion.Euler(-wallTilt, 0f, 0f), rockWall);
            CreateRotatedBlock(tf, "WallNorth", new Vector3(0f, rimHeight * 0.5f, depth * 0.5f + wallThickness * 0.5f - lean),
                new Vector3(width + wallThickness * 2f, wallHeight, wallThickness), Quaternion.Euler(wallTilt, 0f, 0f), rockWall);
            CreateRotatedBlock(tf, "WallWest", new Vector3(-width * 0.5f - wallThickness * 0.5f + lean, rimHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f), Quaternion.Euler(0f, 0f, -wallTilt), rockWall);
            CreateRotatedBlock(tf, "WallEast", new Vector3(width * 0.5f + wallThickness * 0.5f - lean, rimHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f), Quaternion.Euler(0f, 0f, wallTilt), rockWall);

            // Spawn ledge, mid-height on the south wall, looking in.
            float ledgeY = rimHeight * 0.5f;
            Vector3 spawnLedgeCenter = new Vector3(0f, ledgeY - 1f, -depth * 0.5f + 8f);
            CreateBlock(tf, "SpawnLedge", spawnLedgeCenter, new Vector3(16f, 2f, 16f), ledgeMat);
            Vector3 playerSpawn = spawnLedgeCenter + new Vector3(0f, 1f + 1f, 0f);

            // Zone B - the terraces (west wall): four ledges stepping up, 0.6L apart along
            // the wall and 0.4H apart vertically, progressively narrower (3, 2, 1.5, 1 cubes).
            float[] terraceDepths = { 3f, 2f, 1.5f, 1f };
            for (int i = 0; i < terraceDepths.Length; i++)
            {
                float y = 0.4f * H * (i + 1);
                float z = -0.9f * L + 0.6f * L * i;
                float terraceDepth = terraceDepths[i];
                float inset = Mathf.Sin(wallTilt * Mathf.Deg2Rad) * y; // follow the wall's lean
                CreateBlock(tf, "Terrace" + (i + 1),
                    new Vector3(-width * 0.5f + inset + terraceDepth * 0.5f, y - 0.5f, z),
                    new Vector3(terraceDepth, 1f, 8f), ledgeMat);
            }

            // Zone C - the cathedral (east wall): two overhangs and a sticky ceiling patch
            // whose underside is reachable only by a straight-up launch from the floor.
            float eastInnerX(float atHeight) => width * 0.5f - Mathf.Sin(wallTilt * Mathf.Deg2Rad) * atHeight;
            CreateBlock(tf, "CathedralOverhangLow",
                new Vector3(eastInnerX(0.8f * H) - 0.075f * L, 0.8f * H, 0.3f * L),
                new Vector3(0.15f * L, 2f, 0.2f * L), rockWall);
            CreateBlock(tf, "CathedralOverhangHigh",
                new Vector3(eastInnerX(1.5f * H) - 0.06f * L, 1.5f * H, -0.2f * L),
                new Vector3(0.12f * L, 2f, 0.16f * L), rockWall);
            // Ceiling patch: sits just under one max-charge straight-up launch from the floor.
            Vector3 ceilingPatchCenter = new Vector3(eastInnerX(0.92f * H) - 0.2f * L, 0.92f * H, 0.05f * L);
            CreateBlock(tf, "CathedralCeilingPatch", ceilingPatchCenter, new Vector3(0.3f * L, 2f, 0.3f * L), rockWall);
            // The launch spot on the floor directly beneath it, marked.
            CreateBlock(tf, "CeilingLaunchMarker", new Vector3(ceilingPatchCenter.x, 0.05f, ceilingPatchCenter.z), new Vector3(4f, 0.1f, 4f), markerMat);

            // Zone D - the chimney (north-west corner): a sticky shaft 0.8L square rising to
            // 1.6H, with a breakable crack floor halfway up. Intended discovery: launch up
            // the shaft (chaining off its sticky walls), exit the top, pound back down
            // THROUGH the crack floor into the pit below it.
            float shaft = 0.8f * L;
            float shaftTop = 1.6f * H;
            Vector3 shaftCenter = new Vector3(-width * 0.5f + shaft * 0.5f + 0.15f * L, 0f, depth * 0.5f - shaft * 0.5f - 0.15f * L);
            const float shaftWallThickness = 4f;
            CreateBlock(tf, "ChimneyWallWest", shaftCenter + new Vector3(-shaft * 0.5f - shaftWallThickness * 0.5f, shaftTop * 0.5f, 0f),
                new Vector3(shaftWallThickness, shaftTop, shaft + shaftWallThickness * 2f), rockWall);
            CreateBlock(tf, "ChimneyWallEast", shaftCenter + new Vector3(shaft * 0.5f + shaftWallThickness * 0.5f, shaftTop * 0.5f, 0f),
                new Vector3(shaftWallThickness, shaftTop, shaft + shaftWallThickness * 2f), rockWall);
            CreateBlock(tf, "ChimneyWallNorth", shaftCenter + new Vector3(0f, shaftTop * 0.5f, shaft * 0.5f + shaftWallThickness * 0.5f),
                new Vector3(shaft, shaftTop, shaftWallThickness), rockWall);
            CreateBlock(tf, "ChimneyWallSouth", shaftCenter + new Vector3(0f, shaftTop * 0.5f, -shaft * 0.5f - shaftWallThickness * 0.5f),
                new Vector3(shaft, shaftTop, shaftWallThickness), rockWall);
            // The crack floor: the breakable pane prefab scaled to span the shaft, smashable
            // only by a downward pound from above.
            GameObject crackFloor = InstantiatePrefab("BreakableCrackWall");
            crackFloor.name = "ChimneyCrackFloor";
            crackFloor.transform.position = shaftCenter + new Vector3(0f, shaftTop * 0.5f, 0f);
            crackFloor.transform.localScale = new Vector3(shaft / 4f, 1f, shaft / 4f); // the pane asset is 4x4
            // The pit below it: floor is the quarry floor, already sticky.

            // Zone E - the perches: the only NON-sticky surfaces in the level (0.3s cling).
            // A separate container without StickySurface, visually distinct.
            GameObject perches = new GameObject("QuarryPerches");
            Vector3[] perchPositions =
            {
                new Vector3(0.5f * L, 0.5f * H, -0.5f * L),
                new Vector3(-0.4f * L, 0.85f * H, 0.15f * L),
                new Vector3(0.25f * L, 1.25f * H, 0.55f * L),
                new Vector3(-0.1f * L, 1.7f * H, -0.25f * L),
            };
            for (int i = 0; i < perchPositions.Length; i++)
            {
                CreateBlock(perches.transform, "Perch" + (i + 1), perchPositions[i], new Vector3(7f, 1f, 7f), perchMat);
            }

            // Boundary cage: the rim continues upward as INVISIBLE sticky borders to a
            // ceiling at 3H. Overshooting parks you on the world's ceiling - failure becomes
            // a vantage point to pound back in from.
            GameObject cage = new GameObject("BoundaryCage");
            cage.AddComponent<StickySurface>().sticky = true;
            CreateInvisibleBox(cage.transform, "CageCeiling", new Vector3(0f, cageTop + 2f, 0f), new Vector3(width + 40f, 4f, depth + 40f));
            CreateInvisibleBox(cage.transform, "CageSouth", new Vector3(0f, (rimHeight + cageTop) * 0.5f, -depth * 0.5f - 6f), new Vector3(width + 40f, cageTop - rimHeight + 8f, 4f));
            CreateInvisibleBox(cage.transform, "CageNorth", new Vector3(0f, (rimHeight + cageTop) * 0.5f, depth * 0.5f + 6f), new Vector3(width + 40f, cageTop - rimHeight + 8f, 4f));
            CreateInvisibleBox(cage.transform, "CageWest", new Vector3(-width * 0.5f - 6f, (rimHeight + cageTop) * 0.5f, 0f), new Vector3(4f, cageTop - rimHeight + 8f, depth + 40f));
            CreateInvisibleBox(cage.transform, "CageEast", new Vector3(width * 0.5f + 6f, (rimHeight + cageTop) * 0.5f, 0f), new Vector3(4f, cageTop - rimHeight + 8f, depth + 40f));

            // The rig, configured as a toy: infinite energy, free unlimited slow-down, no
            // fail state. The only exits are the pause menu and the menu pad below.
            CoreRig rig = SpawnCoreRig(playerSpawn, LevelPauseButtons());
            rig.controller.infiniteEnergy = true;
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

            // Eight respawning target spheres with a small session counter: one per zone,
            // the rest in awkward mid-air spots. A spine for free play, not an objective.
            Text counterLabel = BuildHudLabel("TargetCounterHud", "Targets: 0", new Vector2(0.5f, 1f), new Vector2(0f, -24f), TextAnchor.UpperCenter, 30);
            TargetSphereCounter counter = counterLabel.transform.parent.gameObject.AddComponent<TargetSphereCounter>();
            counter.label = counterLabel;

            Material sphereMat = MakeMaterial("TargetSphereMaterial", new Color(1f, 0.55f, 0.1f));
            Vector3[] spherePositions =
            {
                new Vector3(0f, 0.3f * H, 0.2f * L),                                        // Zone A - over the open bowl
                new Vector3(-width * 0.5f + 12f, 0.4f * H * 4f + 3f, -0.9f * L + 1.8f * L), // Zone B - above the top terrace
                ceilingPatchCenter + new Vector3(0f, -4f, 0f),                              // Zone C - hanging under the ceiling patch
                shaftCenter + new Vector3(0f, 6f, 0f),                                      // Zone D - in the chimney's pit
                perchPositions[2] + new Vector3(0f, 4f, 0f),                                // Zone E - over a perch
                new Vector3(0.9f * L, 1.9f * H, 0.9f * L),                                  // awkward mid-air
                new Vector3(-1.1f * L, 1.1f * H, -0.9f * L),
                new Vector3(0.3f * L, 2.6f * H, -0.6f * L),                                 // up among the cage
            };
            GameObject spheres = new GameObject("TargetSpheres");
            for (int i = 0; i < spherePositions.Length; i++)
            {
                CreateTargetSphere(spheres.transform, "TargetSphere" + (i + 1), spherePositions[i], sphereMat, counter);
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

            // Everything sticky lives under this container; recovery ledges are sticky too
            // (they are safety, not a hazard).
            GameObject terrain = new GameObject("GauntletPlatforms");
            terrain.AddComponent<StickySurface>().sticky = true;
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

        static void CreateTargetSphere(Transform parent, string name, Vector3 position, Material material, TargetSphereCounter counter)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 3f;
            go.GetComponent<Renderer>().sharedMaterial = material;
            SphereCollider collider = go.GetComponent<SphereCollider>();
            collider.isTrigger = true;
            TargetSphere sphere = go.AddComponent<TargetSphere>();
            sphere.counter = counter;
            EditorUtility.SetDirty(sphere);
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
        // the player casts (its renderers don't cast real ones).
        static void BuildPlayerShadow(Transform player)
        {
            GameObject existing = GameObject.Find("PlayerShadow");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            GameObject shadowGo = new GameObject("PlayerShadow");
            PlayerShadow shadowScript = shadowGo.AddComponent<PlayerShadow>();

            GameObject visualGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visualGo.name = "ShadowVisual";
            visualGo.transform.SetParent(shadowGo.transform, false);
            UnityEngine.Object.DestroyImmediate(visualGo.GetComponent<Collider>());
            visualGo.transform.localScale = new Vector3(1.6f, 0.02f, 1.6f);

            Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
            Material shadowMat = new Material(FindBestShader());
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
