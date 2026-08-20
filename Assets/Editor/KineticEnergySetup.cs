using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
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
        const string OldScenePath = "Assets/Scenes/SampleScene.unity";
        const string ScenePath = "Assets/Scenes/Sandbox Scene.unity";
        const string Level1ScenePath = "Assets/Scenes/Level1.unity";
        const string Level2ScenePath = "Assets/Scenes/Level2.unity";
        const string Level3ScenePath = "Assets/Scenes/Level3.unity";
        const string FastPacedLevelScenePath = "Assets/Scenes/FastPacedLevel.unity";
        const string SlowPacedLevelScenePath = "Assets/Scenes/SlowPacedLevel.unity";
        const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
        const string Tutorial2ScenePath = "Assets/Scenes/Tutorial2.unity";
        const string TestLevel1ScenePath = "Assets/Scenes/TestLevel1.unity";
        const string Tutorial3ScenePath = "Assets/Scenes/Tutorial3.unity";
        const string TestLevel3ScenePath = "Assets/Scenes/TestLevel3.unity";
        const string TestLevel2ScenePath = "Assets/Scenes/TestLevel2.unity";
        const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

        const string CrackSourcePath = "Assets/Textures/CrackDecalSheetSource.png";
        const string CrackProcessedPath = "Assets/Textures/CrackDecalSheet.png";
        const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string PrefabFolder = "Assets/Prefabs";

        static readonly (string label, string sceneName)[] SceneMenuEntries =
        {
            ("Sandbox", "Sandbox Scene"),
            ("Level 1", "Level1"),
            ("Level 2", "Level2"),
            ("Level 3", "Level3"),
            ("Fast Paced", "FastPacedLevel"),
            ("Slow Paced", "SlowPacedLevel"),
            ("Tutorial", "Tutorial"),
            ("Tutorial 2", "Tutorial2"),
            ("Test Level 1", "TestLevel1"),
            ("Test Level 2", "TestLevel2"),
        };

        public static void SetupAll()
        {
            RenameSandboxSceneIfNeeded();
            Setup();
            SetupLevel1();
            SetupLevel2();
            SetupLevel3();
            SetupFastPacedLevel();
            SetupSlowPacedLevel();
            SetupTutorial();
            UpdateBuildSettings();

            Debug.Log("KineticEnergySetup: SetupAll complete OK");
        }

        static void RenameSandboxSceneIfNeeded()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OldScenePath) == null) return;

            string error = AssetDatabase.RenameAsset(OldScenePath, "Sandbox Scene");
            if (!string.IsNullOrEmpty(error))
            {
                throw new Exception($"KineticEnergySetup: failed to rename SampleScene.unity - {error}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void Setup()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            InputActionReference moveRef = FindActionReference("Player", "Move");
            InputActionReference launchRef = FindActionReference("Player", "Launch");
            InputActionReference fireRef = FindActionReference("Player", "Fire");
            InputActionReference lookRef = FindActionReference("Player", "Look");
            InputActionReference pauseRef = FindActionReference("Player", "Pause");
            InputActionReference selectGhostRef = FindActionReference("Player", "SelectGhostPreview");
            InputActionReference selectTrailRef = FindActionReference("Player", "SelectTrailPreview");
            InputActionReference selectCrosshairRef = FindActionReference("Player", "SelectCrosshairPreview");
            InputActionReference selectNoneRef = FindActionReference("Player", "SelectNonePreview");
            InputActionReference switchSchemeRef = FindActionReference("Player", "SwitchControlScheme");
            InputActionReference upLaunchRef = FindActionReference("Player", "LaunchUp");
            InputActionReference cancelChargeRef = FindActionReference("Player", "CancelCharge");
            InputActionReference radialMenuRef = FindActionReference("Player", "RadialMenu");

            GameObject player = GameObject.Find("Player");
            if (player == null) throw new Exception("KineticEnergySetup: could not find 'Player' GameObject in scene.");

            GameObject mainCamGo = GameObject.Find("Main Camera");
            if (mainCamGo == null) throw new Exception("KineticEnergySetup: could not find 'Main Camera' GameObject in scene.");

            BuildDirectionalLight();

            KineticCubeController controller = BuildPlayerCube(player, moveRef, launchRef, fireRef, selectGhostRef, selectTrailRef, selectCrosshairRef, selectNoneRef, switchSchemeRef, upLaunchRef, cancelChargeRef,
                out KineticCubeControllerFreeMove freeMoveController);
            ThirdPersonOrbitCamera orbitCam = BuildCameraRig(mainCamGo, lookRef);

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(player, PrefabFolder + "/Player.prefab", InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAssetAndConnect(mainCamGo, PrefabFolder + "/ThirdPersonCameraRig.prefab", InteractionMode.AutomatedAction);

            controller.cameraTransform = mainCamGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = mainCamGo.transform;
            orbitCam.target = player.transform;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);

            Text previewModeLabel = BuildPauseSystem(pauseRef, radialMenuRef, out Text controlsHint, out Text controlsBody,
                out EnergyMeterController energyMeter, out RadialMenuController radialMenu);

            controller.landingPreview.modeLabel = previewModeLabel;
            controller.controlsHintLabel = controlsHint;
            controller.controlsPanelBody = controlsBody;
            controller.energyMeter = energyMeter;
            radialMenu.controller = controller;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(controller.landingPreview);
            EditorUtility.SetDirty(radialMenu);

            BuildPlayerShadow(player.transform);
            BuildSandboxSignText();
            BuildSandboxPlatforms(player.transform.position);

            Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("KineticEnergySetup: setup complete OK");
        }

        static void SetupLevel1()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Level1ScenePath) == null)
            {

                if (!AssetDatabase.CopyAsset(ScenePath, Level1ScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Sandbox Scene to create Level1.");
                }
            }

            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);

            BuildDirectionalLight();
            BuildGlobalVolume();

            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ThirdPersonCameraRig.prefab");
            GameObject pauseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (playerAsset == null || cameraAsset == null || pauseAsset == null)
            {
                throw new Exception("KineticEnergySetup: Level1 needs Player/ThirdPersonCameraRig/PauseSystem prefabs - run Setup() (part of SetupAll) first.");
            }

            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");

            GameObject playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset);
            GameObject camGo = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset);
            GameObject pauseGo = (GameObject)PrefabUtility.InstantiatePrefab(pauseAsset);

            KineticCubeController controller = playerGo.GetComponent<KineticCubeController>();
            KineticCubeControllerFreeMove freeMoveController = playerGo.GetComponent<KineticCubeControllerFreeMove>();
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();

            controller.cameraTransform = camGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            ApplyLaunchTuning(controller);

            Text modeLabel = pauseGo.transform.Find("PauseCanvas/PreviewModeLabel")?.GetComponent<Text>();
            controller.landingPreview.modeLabel = modeLabel;
            controller.controlsHintLabel = pauseGo.transform.Find("PauseCanvas/ControlsHintLabel")?.GetComponent<Text>();
            controller.controlsPanelBody = pauseGo.transform.Find("PauseCanvas/ControlsPanel/ControlsBody")?.GetComponent<Text>();
            controller.energyMeter = pauseGo.transform.Find("EnergyMeter")?.GetComponent<EnergyMeterController>();
            RadialMenuController radialMenu = pauseGo.transform.Find("RadialMenuController")?.GetComponent<RadialMenuController>();
            if (radialMenu != null) radialMenu.controller = controller;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);
            EditorUtility.SetDirty(controller.landingPreview);
            if (radialMenu != null) EditorUtility.SetDirty(radialMenu);

            GameObject generatorGo = new GameObject("LevelGenerator");
            LevelGenerator generator = generatorGo.AddComponent<LevelGenerator>();
            generator.player = playerGo.transform;
            generator.cameraTransform = camGo.transform;
            generator.platformCount = 9;
            generator.platformSize = new Vector3(3f, 0.5f, 3f);
            generator.minHorizontalDistance = 7f;
            generator.maxHorizontalDistance = 13f;
            generator.minHeightDifference = -1.5f;
            generator.maxHeightDifference = 2f;
            generator.platformColor = new Color(0.5f, 0.5f, 0.55f);
            generator.platformMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/CheckeredFloor.mat");
            generator.finishPadColor = new Color(0.2f, 1f, 0.5f, 0.45f);
            generator.finishText = "Finish";
            generator.finishTextHeight = 2.5f;
            generator.finishTextColor = new Color(0.15f, 0.45f, 1f);
            generator.finishFontSize = 48;
            generator.finishCharacterSize = 0.2f;
            generator.safetyFloorMargin = 8f;

            generator.safetyFloorSize = 260f;

            BuildPlayerShadow(playerGo.transform);

            Scene level1Scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(level1Scene);
            EditorSceneManager.SaveScene(level1Scene);

            Debug.Log("KineticEnergySetup: Level1 setup complete OK");
        }

        static void SetupLevel2()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Level2ScenePath) == null)
            {

                if (!AssetDatabase.CopyAsset(ScenePath, Level2ScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Sandbox Scene to create Level2.");
                }
            }

            EditorSceneManager.OpenScene(Level2ScenePath, OpenSceneMode.Single);

            BuildDirectionalLight();
            BuildGlobalVolume();

            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ThirdPersonCameraRig.prefab");
            GameObject pauseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (playerAsset == null || cameraAsset == null || pauseAsset == null)
            {
                throw new Exception("KineticEnergySetup: Level2 needs Player/ThirdPersonCameraRig/PauseSystem prefabs - run Setup() (part of SetupAll) first.");
            }

            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");

            DestroyIfExists("SandboxPlatforms");

            GameObject playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset);
            GameObject camGo = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset);
            GameObject pauseGo = (GameObject)PrefabUtility.InstantiatePrefab(pauseAsset);

            KineticCubeController controller = playerGo.GetComponent<KineticCubeController>();
            KineticCubeControllerFreeMove freeMoveController = playerGo.GetComponent<KineticCubeControllerFreeMove>();
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();

            controller.cameraTransform = camGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            ApplyLaunchTuning(controller);

            Text modeLabel = pauseGo.transform.Find("PauseCanvas/PreviewModeLabel")?.GetComponent<Text>();
            controller.landingPreview.modeLabel = modeLabel;
            controller.controlsHintLabel = pauseGo.transform.Find("PauseCanvas/ControlsHintLabel")?.GetComponent<Text>();
            controller.controlsPanelBody = pauseGo.transform.Find("PauseCanvas/ControlsPanel/ControlsBody")?.GetComponent<Text>();
            controller.energyMeter = pauseGo.transform.Find("EnergyMeter")?.GetComponent<EnergyMeterController>();
            RadialMenuController radialMenu = pauseGo.transform.Find("RadialMenuController")?.GetComponent<RadialMenuController>();
            if (radialMenu != null) radialMenu.controller = controller;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);
            EditorUtility.SetDirty(controller.landingPreview);
            if (radialMenu != null) EditorUtility.SetDirty(radialMenu);

            GameObject nextPlatform = BuildLevel2Segments(playerGo.transform);
            BuildCameraStartFacing(playerGo.transform, orbitCam, nextPlatform.transform);
            BuildPlayerShadow(playerGo.transform);

            Scene level2Scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(level2Scene);
            EditorSceneManager.SaveScene(level2Scene);

            Debug.Log("KineticEnergySetup: Level2 setup complete OK");
        }

        static GameObject BuildLevel2Segments(Transform player)
        {
            GameObject container = GameObject.Find("Level2Segments");
            if (container != null) UnityEngine.Object.DestroyImmediate(container);
            container = new GameObject("Level2Segments");

            return BuildLevel2OpeningHallway(container.transform, player);
        }

        static GameObject BuildLevel2OpeningHallway(Transform parent, Transform player)
        {
            GameObject hallway = new GameObject("OpeningHallway");
            hallway.transform.SetParent(parent, true);

            Vector3 platformSize = new Vector3(6f, 0.5f, 6f);
            const float hallwayLength = 32f;
            const float corridorHalfWidth = 5f;
            const float wallThickness = 1f;
            const float wallHeight = 14f;
            const float ceilingThickness = 0.3f;

            float endMargin = platformSize.z * 0.5f;

            Vector3 startCenter = Vector3.zero;
            Vector3 endCenter = new Vector3(0f, 0f, hallwayLength);
            float corridorMinZ = startCenter.z - endMargin;
            float corridorMaxZ = endCenter.z + endMargin;
            float corridorLength = corridorMaxZ - corridorMinZ;
            float corridorCenterZ = (corridorMinZ + corridorMaxZ) * 0.5f;

            Material platformMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/CheckeredFloor.mat");
            if (platformMat == null)
            {
                platformMat = new Material(FindBestShader());
                platformMat.color = new Color(0.5f, 0.5f, 0.55f);
                platformMat = SaveMaterialAsset(platformMat, "Level2PlatformMaterial");
            }

            Material wallMat = new Material(FindBestShader());
            wallMat.color = new Color(0.32f, 0.34f, 0.4f);
            wallMat = SaveMaterialAsset(wallMat, "Level2WallMaterial");

            Color glassColor = new Color(0.75f, 0.9f, 1f, 0.35f);
            Material glassMat = new Material(FindBestShader());
            glassMat.color = glassColor;
            MakeTransparent(glassMat, glassColor.a);
            glassMat = SaveMaterialAsset(glassMat, "Level2GlassCeilingMaterial");

            CreateBlock(hallway.transform, "StartPlatform", startCenter, platformSize, platformMat);
            GameObject endPlatform = CreateBlock(hallway.transform, "EndPlatform", endCenter, platformSize, platformMat);

            Vector3 wallSize = new Vector3(wallThickness, wallHeight, corridorLength);
            Vector3 wallCenterY = new Vector3(0f, wallHeight * 0.5f, corridorCenterZ);
            CreateBlock(hallway.transform, "WallLeft", wallCenterY + new Vector3(-(corridorHalfWidth + wallThickness * 0.5f), 0f, 0f), wallSize, wallMat);
            CreateBlock(hallway.transform, "WallRight", wallCenterY + new Vector3(corridorHalfWidth + wallThickness * 0.5f, 0f, 0f), wallSize, wallMat);

            Vector3 ceilingSize = new Vector3((corridorHalfWidth + wallThickness) * 2f, ceilingThickness, corridorLength);
            Vector3 ceilingCenter = new Vector3(0f, wallHeight + ceilingThickness * 0.5f, corridorCenterZ);
            GameObject ceiling = CreateBlock(hallway.transform, "GlassCeiling", ceilingCenter, ceilingSize, glassMat);

            ceiling.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            player.position = startCenter + new Vector3(0f, platformSize.y * 0.5f + 0.5f, 0f);

            return endPlatform;
        }

        static void SetupLevel3()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Level3ScenePath) == null)
            {

                if (!AssetDatabase.CopyAsset(ScenePath, Level3ScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Sandbox Scene to create Level3.");
                }
            }

            EditorSceneManager.OpenScene(Level3ScenePath, OpenSceneMode.Single);

            BuildDirectionalLight();
            BuildGlobalVolume();

            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ThirdPersonCameraRig.prefab");
            GameObject pauseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (playerAsset == null || cameraAsset == null || pauseAsset == null)
            {
                throw new Exception("KineticEnergySetup: Level3 needs Player/ThirdPersonCameraRig/PauseSystem prefabs - run Setup() (part of SetupAll) first.");
            }

            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");

            DestroyIfExists("SandboxPlatforms");

            GameObject playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset);
            GameObject camGo = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset);
            GameObject pauseGo = (GameObject)PrefabUtility.InstantiatePrefab(pauseAsset);

            KineticCubeController controller = playerGo.GetComponent<KineticCubeController>();
            KineticCubeControllerFreeMove freeMoveController = playerGo.GetComponent<KineticCubeControllerFreeMove>();
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();

            controller.cameraTransform = camGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            ApplyLaunchTuning(controller);

            Text modeLabel = pauseGo.transform.Find("PauseCanvas/PreviewModeLabel")?.GetComponent<Text>();
            controller.landingPreview.modeLabel = modeLabel;
            controller.controlsHintLabel = pauseGo.transform.Find("PauseCanvas/ControlsHintLabel")?.GetComponent<Text>();
            controller.controlsPanelBody = pauseGo.transform.Find("PauseCanvas/ControlsPanel/ControlsBody")?.GetComponent<Text>();
            controller.energyMeter = pauseGo.transform.Find("EnergyMeter")?.GetComponent<EnergyMeterController>();
            RadialMenuController radialMenu = pauseGo.transform.Find("RadialMenuController")?.GetComponent<RadialMenuController>();
            if (radialMenu != null) radialMenu.controller = controller;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);
            EditorUtility.SetDirty(controller.landingPreview);
            if (radialMenu != null) EditorUtility.SetDirty(radialMenu);

            GameObject nextPlatform = BuildLevel3Segments(playerGo.transform, camGo.transform);
            BuildCameraStartFacing(playerGo.transform, orbitCam, nextPlatform.transform);
            BuildPlayerShadow(playerGo.transform);

            Scene level3Scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(level3Scene);
            EditorSceneManager.SaveScene(level3Scene);

            Debug.Log("KineticEnergySetup: Level3 setup complete OK");
        }

        static GameObject BuildLevel3Segments(Transform player, Transform cameraTransform)
        {
            GameObject container = GameObject.Find("Level3Segments");
            if (container != null) UnityEngine.Object.DestroyImmediate(container);
            container = new GameObject("Level3Segments");

            GameObject basicsEnd = BuildLevel3LaunchBasics(container.transform, player);
            GameObject variedPathEnd = BuildLevel3VariedPath(container.transform, basicsEnd);
            GameObject gauntletEnd = BuildLevel3Gauntlet(container.transform, variedPathEnd, cameraTransform);
            return gauntletEnd;
        }

        static GameObject BuildLevel3LaunchBasics(Transform parent, Transform player)
        {
            GameObject segment = new GameObject("LaunchBasics");
            segment.transform.SetParent(parent, true);

            Vector3 platformSize = new Vector3(5f, 0.5f, 5f);
            Material platformMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/CheckeredFloor.mat");
            if (platformMat == null)
            {
                platformMat = new Material(FindBestShader());
                platformMat.color = new Color(0.5f, 0.5f, 0.55f);
                platformMat = SaveMaterialAsset(platformMat, "Level3PlatformMaterial");
            }

            Material wallMat = new Material(FindBestShader());
            wallMat.color = new Color(0.45f, 0.45f, 0.5f);
            wallMat = SaveMaterialAsset(wallMat, "Level3BackWallMaterial");

            Vector3[] centers =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 14f),
                new Vector3(2f, 1f, 32f),
                new Vector3(-2f, 0f, 58f),
                new Vector3(1.5f, 2f, 98f),
                new Vector3(0f, 5f, 155f),
            };

            GameObject last = null;
            for (int i = 0; i < centers.Length; i++)
            {
                bool isLast = i == centers.Length - 1;
                Vector3 size = isLast ? new Vector3(6f, 0.5f, 6f) : platformSize;
                string name = i == 0 ? "StartPlatform" : $"Platform{i}";
                last = CreateBlock(segment.transform, name, centers[i], size, platformMat);

                if (i > 0)
                {
                    const float backWallHeight = 1.6f;
                    const float backWallThickness = 0.5f;
                    Vector3 backWallCenter = centers[i] + new Vector3(0f, size.y * 0.5f + backWallHeight * 0.5f, size.z * 0.5f - backWallThickness * 0.5f);
                    Vector3 backWallSize = new Vector3(size.x, backWallHeight, backWallThickness);
                    CreateBlock(segment.transform, $"Platform{i}BackWall", backWallCenter, backWallSize, wallMat);
                }
            }

            player.position = centers[0] + new Vector3(0f, platformSize.y * 0.5f + 0.5f, 0f);

            return last;
        }

        static GameObject BuildLevel3VariedPath(Transform parent, GameObject fromPlatform)
        {
            GameObject segment = new GameObject("VariedPath");
            segment.transform.SetParent(parent, true);

            Vector3 basePos = fromPlatform.transform.position;
            float x = basePos.x;
            float y = basePos.y;
            float z = basePos.z;

            Material pathMat = new Material(FindBestShader());
            pathMat.color = new Color(0.3f, 0.45f, 0.55f);
            pathMat = SaveMaterialAsset(pathMat, "Level3PathMaterial");

            Material specialMat = new Material(FindBestShader());
            specialMat.color = new Color(0.95f, 0.55f, 0.15f);
            specialMat = SaveMaterialAsset(specialMat, "Level3LedgeMaterial");

            Vector3 platformSize = new Vector3(5f, 0.5f, 5f);
            Vector3 ceilingSize = new Vector3(6f, 0.5f, 6f);
            Vector3 sideWallSize = new Vector3(1f, 5f, 5f);

            CreateBlock(segment.transform, "Normal1", new Vector3(x, y + 3f, z + 35f), platformSize, pathMat);

            CreateBlock(segment.transform, "Ceiling1", new Vector3(x, y + 13f, z + 60f), ceilingSize, pathMat);
            CreateBlock(segment.transform, "Normal2", new Vector3(x, y + 1f, z + 90f), platformSize, pathMat);

            CreateBlock(segment.transform, "SideWallLeft", new Vector3(x - 9f, y + 4f, z + 115f), sideWallSize, specialMat);
            CreateBlock(segment.transform, "Normal3", new Vector3(x, y + 2f, z + 145f), platformSize, pathMat);
            CreateBlock(segment.transform, "SideWallRight", new Vector3(x + 9f, y + 5f, z + 170f), sideWallSize, specialMat);

            GameObject last = CreateBlock(segment.transform, "Normal4", new Vector3(x, y + 3f, z + 200f), platformSize, pathMat);

            return last;
        }

        static GameObject BuildLevel3Gauntlet(Transform parent, GameObject fromPlatform, Transform cameraTransform)
        {
            GameObject segment = new GameObject("Gauntlet");
            segment.transform.SetParent(parent, true);

            Vector3 startPos = fromPlatform.transform.position;
            float x0 = startPos.x;
            float y = startPos.y;
            float z0 = startPos.z;

            Material platformMat = new Material(FindBestShader());
            platformMat.color = new Color(0.55f, 0.3f, 0.22f);
            platformMat = SaveMaterialAsset(platformMat, "Level3GauntletPlatformMaterial");

            Material wallMat = new Material(FindBestShader());
            wallMat.color = new Color(0.65f, 0.35f, 0.2f);
            wallMat = SaveMaterialAsset(wallMat, "Level3GauntletWallMaterial");

            Vector3 platformSize = new Vector3(6f, 0.5f, 6f);
            CreateBlock(segment.transform, "Waypoint1", new Vector3(x0, y, z0 + 40f), platformSize, platformMat);
            CreateBlock(segment.transform, "Waypoint2", new Vector3(x0 + 5f, y + 2f, z0 + 78f), platformSize, platformMat);

            Vector3 wallCenter = new Vector3(x0, y + 4f, z0 + 115f);
            Vector3 wallSize = new Vector3(16f, 10f, 1f);
            CreateBlock(segment.transform, "RefuelWall", wallCenter, wallSize, wallMat);

            Vector3 wallBasePos = new Vector3(x0, y, z0 + 115f);
            CreateBlock(segment.transform, "RefuelWallBase", wallBasePos, new Vector3(8f, 0.5f, 4f), platformMat);

            CreateBlock(segment.transform, "Waypoint3", new Vector3(x0 - 5f, y + 1f, z0 + 155f), platformSize, platformMat);
            CreateBlock(segment.transform, "Waypoint4", new Vector3(x0 + 3f, y - 1f, z0 + 195f), platformSize, platformMat);

            Vector3 finishPos = new Vector3(x0, y, z0 + 240f);
            Vector3 finishSize = new Vector3(7f, 0.5f, 7f);
            GameObject finishPlatform = CreateBlock(segment.transform, "FinishPlatform", finishPos, finishSize, platformMat);

            BuildLevel3FinishPad(segment.transform, finishPos, finishSize, cameraTransform);

            return finishPlatform;
        }

        static void BuildLevel3FinishPad(Transform parent, Vector3 platformPosition, Vector3 platformSize, Transform cameraTransform)
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "FinishPad";
            pad.transform.SetParent(parent, true);
            UnityEngine.Object.DestroyImmediate(pad.GetComponent<Collider>());

            const float padHeight = 0.05f;
            const float zFightGap = 0.03f;
            pad.transform.position = platformPosition + new Vector3(0f, platformSize.y * 0.5f + zFightGap + padHeight * 0.5f, 0f);
            pad.transform.localScale = new Vector3(platformSize.x, padHeight, platformSize.z);

            Color padColor = new Color(0.2f, 1f, 0.5f, 0.45f);
            Material padMat = new Material(FindBestShader());
            padMat.color = padColor;
            MakeTransparent(padMat, padColor.a);
            padMat = SaveMaterialAsset(padMat, "Level3FinishPadMaterial");
            pad.GetComponent<Renderer>().sharedMaterial = padMat;

            GameObject textGo = new GameObject("FinishText");
            textGo.transform.SetParent(parent, true);
            textGo.transform.position = platformPosition + new Vector3(0f, 2.5f, 0f);

            TextMesh textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = "Finish";
            textMesh.color = new Color(0.15f, 0.45f, 1f);
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.2f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;

            Billboard billboard = textGo.AddComponent<Billboard>();
            billboard.target = cameraTransform;

            GameObject trigger = new GameObject("FinishTrigger");
            trigger.transform.SetParent(parent, true);
            trigger.transform.position = platformPosition + new Vector3(0f, platformSize.y * 0.5f + 1f, 0f);
            BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(platformSize.x, 2f, platformSize.z);
            trigger.AddComponent<FinishLine>();
        }

        static void BuildCameraStartFacing(Transform player, ThirdPersonOrbitCamera orbitCam, Transform lookAtPoint)
        {
            GameObject existing = GameObject.Find("CameraStartFacing");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            GameObject go = new GameObject("CameraStartFacing");
            CameraStartFacing facing = go.AddComponent<CameraStartFacing>();
            facing.player = player;
            facing.cameraOrbit = orbitCam;
            facing.lookAtPoint = lookAtPoint;
        }

        static void SetupFastPacedLevel()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FastPacedLevelScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(ScenePath, FastPacedLevelScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Sandbox Scene to create FastPacedLevel.");
                }
            }

            EditorSceneManager.OpenScene(FastPacedLevelScenePath, OpenSceneMode.Single);

            BuildDirectionalLight();
            BuildGlobalVolume();

            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ThirdPersonCameraRig.prefab");
            GameObject pauseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (playerAsset == null || cameraAsset == null || pauseAsset == null)
            {
                throw new Exception("KineticEnergySetup: FastPacedLevel needs Player/ThirdPersonCameraRig/PauseSystem prefabs - run Setup() (part of SetupAll) first.");
            }

            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");

            DestroyIfExists("SandboxPlatforms");

            GameObject playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset);
            GameObject camGo = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset);
            GameObject pauseGo = (GameObject)PrefabUtility.InstantiatePrefab(pauseAsset);

            KineticCubeController controller = playerGo.GetComponent<KineticCubeController>();
            KineticCubeControllerFreeMove freeMoveController = playerGo.GetComponent<KineticCubeControllerFreeMove>();
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();

            controller.cameraTransform = camGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            ApplyLaunchTuning(controller);

            controller.SetControlScheme(ControlScheme.FastPaced);
            controller.schemeSwitchingEnabled = false;
            controller.gravity = 0f;

            controller.fallResetY = -1000f;
            freeMoveController.fallResetY = -1000f;

            controller.minLaunchForce = 25f;
            controller.maxLaunchForce = 250f;
            controller.fastPacedMinDamping = 1.0f;
            controller.fastPacedMaxDamping = 1.0f;

            controller.fastPacedRefundMultiplier = 1.2f;

            controller.fastPacedFlightTimeScale = 1.5f;
            controller.fastPacedAimAction = FindActionReference("Player", "FastPacedAim");
            controller.fastPacedLaunchAction = FindActionReference("Player", "FastPacedLaunch");

            Text modeLabel = pauseGo.transform.Find("PauseCanvas/PreviewModeLabel")?.GetComponent<Text>();
            controller.landingPreview.modeLabel = modeLabel;
            controller.controlsHintLabel = pauseGo.transform.Find("PauseCanvas/ControlsHintLabel")?.GetComponent<Text>();
            controller.controlsPanelBody = pauseGo.transform.Find("PauseCanvas/ControlsPanel/ControlsBody")?.GetComponent<Text>();
            controller.energyMeter = pauseGo.transform.Find("EnergyMeter")?.GetComponent<EnergyMeterController>();
            RadialMenuController radialMenu = pauseGo.transform.Find("RadialMenuController")?.GetComponent<RadialMenuController>();
            if (radialMenu != null) radialMenu.controller = controller;

            controller.landingPreview.ghostAndCrosshairEnabled = true;

            pauseGo.transform.Find("PauseCanvas/PausePanel/ScenesButton")?.gameObject.SetActive(false);
            pauseGo.transform.Find("PauseCanvas/ScenesPanel")?.gameObject.SetActive(false);

            PauseController pauseController = pauseGo.transform.Find("PauseController")?.GetComponent<PauseController>();

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);
            EditorUtility.SetDirty(controller.landingPreview);
            if (radialMenu != null) EditorUtility.SetDirty(radialMenu);

            GameObject firstSpiralPlatform = BuildFastPacedSpiral(playerGo.transform, camGo.transform, pauseController);
            BuildCameraStartFacing(playerGo.transform, orbitCam, firstSpiralPlatform.transform);
            BuildPlayerShadow(playerGo.transform);

            Scene fastPacedScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(fastPacedScene);
            EditorSceneManager.SaveScene(fastPacedScene);

            Debug.Log("KineticEnergySetup: FastPacedLevel setup complete OK");
        }

        static GameObject BuildFastPacedSpiral(Transform player, Transform cameraTransform, PauseController pauseController)
        {
            GameObject container = GameObject.Find("FastPacedSpiral");
            if (container != null) UnityEngine.Object.DestroyImmediate(container);
            container = new GameObject("FastPacedSpiral");

            Material startMat = new Material(FindBestShader());
            startMat.color = new Color(0.3f, 0.75f, 0.85f);
            MakeTransparent(startMat, 1f);
            startMat = SaveMaterialAsset(startMat, "FastPacedStartMaterial");

            Material platformMat = new Material(FindBestShader());
            platformMat.color = new Color(0.75f, 0.25f, 0.65f);
            MakeTransparent(platformMat, 1f);
            platformMat = SaveMaterialAsset(platformMat, "FastPacedPlatformMaterial");

            Material finishMat = new Material(FindBestShader());
            finishMat.color = new Color(0.95f, 0.75f, 0.15f);
            MakeTransparent(finishMat, 1f);
            finishMat = SaveMaterialAsset(finishMat, "FastPacedFinishMaterial");

            Vector3 startSize = new Vector3(6f, 0.5f, 6f);
            GameObject startPlatform = CreateBlock(container.transform, "StartPlatform", Vector3.zero, startSize, startMat);
            startPlatform.transform.rotation = Quaternion.identity;
            startPlatform.AddComponent<TransparentWhenOccupied>();

            player.position = new Vector3(0f, startSize.y * 0.5f + 0.5f, 0f);

            const int platformCount = 10;

            const float minAngleStepDeg = 75f;
            const float maxAngleStepDeg = 100f;

            System.Random rng = new System.Random(20260806);

            const float startRadius = 14f;
            const float radiusStep = 12f;
            const float startZ = 16f;
            const float zStep = 20f;
            Vector3 platformSize = new Vector3(4.5f, 0.5f, 4.5f);
            Vector3 finishSize = new Vector3(6f, 0.5f, 6f);

            GameObject firstSpiralPlatform = null;

            float angleDeg = 30f + (float)rng.NextDouble() * 120f;
            for (int i = 1; i <= platformCount; i++)
            {
                if (i > 1)
                {

                    bool finalStep = i == platformCount;
                    float stepMin = finalStep ? 100f : minAngleStepDeg;
                    float stepMax = finalStep ? 115f : maxAngleStepDeg;
                    angleDeg += stepMin + (float)rng.NextDouble() * (stepMax - stepMin);
                }
                float rad = angleDeg * Mathf.Deg2Rad;
                float radius = startRadius + (i - 1) * radiusStep;
                float z = startZ + (i - 1) * zStep;

                Vector3 center = new Vector3(radius * Mathf.Cos(rad), radius * Mathf.Sin(rad), z);

                Vector3 inward = new Vector3(-Mathf.Cos(rad), -Mathf.Sin(rad), 0f);
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, inward);

                bool isLast = i == platformCount;
                Vector3 size = isLast ? finishSize : platformSize;
                Material mat = isLast ? finishMat : platformMat;
                string name = isLast ? "FinishPlatform" : $"SpiralPlatform{i}";

                GameObject platform = CreateBlock(container.transform, name, center, size, mat);
                platform.transform.rotation = rotation;
                platform.AddComponent<TransparentWhenOccupied>();

                if (i == 1) firstSpiralPlatform = platform;

                if (isLast)
                {
                    BuildFastPacedFinish(container.transform, center, inward, rotation, size, cameraTransform, pauseController);
                }
            }

            return firstSpiralPlatform;
        }

        static void BuildFastPacedFinish(Transform parent, Vector3 platformCenter, Vector3 inward, Quaternion platformRotation,
            Vector3 platformSize, Transform cameraTransform, PauseController pauseController)
        {
            GameObject textGo = new GameObject("FinishText");
            textGo.transform.SetParent(parent, true);
            textGo.transform.position = platformCenter + inward * 3f;

            TextMesh textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = "Finish";
            textMesh.color = new Color(0.15f, 0.45f, 1f);
            textMesh.fontSize = 48;

            textMesh.characterSize = 0.4f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;

            Billboard billboard = textGo.AddComponent<Billboard>();
            billboard.target = cameraTransform;

            GameObject trigger = new GameObject("FinishTrigger");
            trigger.transform.SetParent(parent, true);

            trigger.transform.SetPositionAndRotation(platformCenter + inward * (platformSize.y * 0.5f + 1f), platformRotation);
            BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(platformSize.x, 2f, platformSize.z);

            FinishLineWin finishWin = trigger.AddComponent<FinishLineWin>();
            finishWin.pauseController = pauseController;
        }

        static GameObject CreateBlock(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
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

        static GameObject FindByNameIncludingInactive(string name)
        {
            foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go.name == name) return go;
            }
            return null;
        }

        static void DestroyIfExists(string name)
        {

            int destroyed = 0;
            GameObject go;
            while ((go = FindByNameIncludingInactive(name)) != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                destroyed++;
                if (destroyed > 50)
                {
                    Debug.LogError($"KineticEnergySetup: DestroyIfExists('{name}') aborted after 50 - Find() keeps returning a live object after Destroy.");
                    break;
                }
            }
            if (destroyed > 1) Debug.LogWarning($"KineticEnergySetup: DestroyIfExists('{name}') removed {destroyed} accumulated duplicates - expected at most 1.");
        }

        static void ApplyLaunchTuning(KineticCubeController controller)
        {
            controller.minLaunchForce = 45f;
            controller.maxLaunchForce = 110f;

            controller.minLaunchDamping = 2.8f;
            controller.maxLaunchDamping = 1.0f;

            controller.stickAimUpAngle = 80f;
            controller.stickAimDownAngle = 60f;
            controller.stickAimForwardAngle = 30f;
            controller.stickAimForwardNeutralAngle = 5f;

            controller.downLaunchDamping = 0.2f;

            controller.stickAimDeadzone = 0.9f;

            controller.chargeTimeScale = 0.75f;

            controller.startingEnergyFraction = 0.2f;
            controller.energyCostPerFullCharge = 1f;
            controller.energyGainPerSpeed = 0.03f;
            controller.energyGainSpeedBonus = 0.01f;
            controller.minEnergyGainPerCrash = 0.05f;
            controller.chargeAccumulationRate = 0.3f;

            controller.minDefyGravityDuration = 0.4f;
            controller.maxDefyGravityDuration = 1.5f;
            controller.maxDefyGravitySpeed = 70f;
            controller.defyGravityFallDamping = 0.2f;

            controller.defaultAimPitch = -30f;

            controller.SetControlScheme(ControlScheme.StickAim);
            controller.schemeSwitchingEnabled = true;

            controller.gravity = -30f;
        }

        static void UpdateBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(Level1ScenePath, true),
                new EditorBuildSettingsScene(Level2ScenePath, true),
                new EditorBuildSettingsScene(Level3ScenePath, true),
                new EditorBuildSettingsScene(FastPacedLevelScenePath, true),
                new EditorBuildSettingsScene(SlowPacedLevelScenePath, true),
                new EditorBuildSettingsScene(TutorialScenePath, true),
                new EditorBuildSettingsScene(Tutorial2ScenePath, true)
            };
        }

        static KineticCubeController BuildPlayerCube(GameObject player, InputActionReference moveRef, InputActionReference launchRef, InputActionReference fireRef,
            InputActionReference selectGhostRef, InputActionReference selectTrailRef, InputActionReference selectCrosshairRef, InputActionReference selectNoneRef,
            InputActionReference switchSchemeRef, InputActionReference upLaunchRef, InputActionReference cancelChargeRef,
            out KineticCubeControllerFreeMove freeMoveController)
        {
            SphereCollider oldCollider = player.GetComponent<SphereCollider>();
            if (oldCollider != null) UnityEngine.Object.DestroyImmediate(oldCollider);

            if (player.GetComponent<BoxCollider>() == null)
            {
                player.AddComponent<BoxCollider>();
            }

            MeshFilter rootMeshFilter = player.GetComponent<MeshFilter>();
            MeshRenderer rootMeshRenderer = player.GetComponent<MeshRenderer>();

            Transform visualTransform = player.transform.Find("Visual");
            GameObject visualGo = visualTransform != null ? visualTransform.gameObject : new GameObject("Visual");
            visualGo.transform.SetParent(player.transform, false);
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localRotation = Quaternion.identity;
            visualGo.transform.localScale = Vector3.one;

            MeshFilter visualMeshFilter = visualGo.GetComponent<MeshFilter>();
            if (visualMeshFilter == null) visualMeshFilter = visualGo.AddComponent<MeshFilter>();
            MeshRenderer visualMeshRenderer = visualGo.GetComponent<MeshRenderer>();
            if (visualMeshRenderer == null) visualMeshRenderer = visualGo.AddComponent<MeshRenderer>();

            if (rootMeshFilter != null)
            {
                visualMeshFilter.sharedMesh = rootMeshFilter.sharedMesh;
                UnityEngine.Object.DestroyImmediate(rootMeshFilter);
            }
            if (rootMeshRenderer != null)
            {
                visualMeshRenderer.sharedMaterial = rootMeshRenderer.sharedMaterial;
                UnityEngine.Object.DestroyImmediate(rootMeshRenderer);
            }
            if (visualMeshFilter.sharedMesh == null)
            {
                visualMeshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }

            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb == null) rb = player.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.linearDamping = 0.25f;
            rb.angularDamping = 0.05f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            KineticCubeController controller = player.GetComponent<KineticCubeController>();
            if (controller == null) controller = player.AddComponent<KineticCubeController>();

            ApplyLaunchTuning(controller);
            controller.maxChargeTime = 1.5f;
            controller.aimDeadzone = 0.15f;
            controller.aimRotationSpeed = 90f;
            controller.minAimPitch = -80f;
            controller.maxAimPitch = 80f;
            controller.maxPredictionSteps = 3000;
            controller.previewLineHeight = 0.65f;
            controller.groundCheckDistance = 0.6f;
            controller.fallResetY = -30f;
            controller.launchGraceDuration = 0.15f;
            controller.minLaunchClearDistance = 2f;
            controller.flatGroundStickThreshold = 0.9f;
            controller.slamDownwardThreshold = 0.7f;
            controller.stuckOnGroundTickThreshold = 10;
            controller.moveAction = moveRef;
            controller.launchAction = launchRef;
            controller.fireAction = fireRef;
            controller.selectClassicSchemeAction = selectGhostRef;
            controller.selectHoldReleaseSchemeAction = selectTrailRef;
            controller.selectAnalogSchemeAction = selectCrosshairRef;
            controller.selectNoneAction = selectNoneRef;
            controller.trailToggleAction = switchSchemeRef;
            controller.upLaunchAction = upLaunchRef;
            controller.cancelChargeAction = cancelChargeRef;
            controller.aimArrow = BuildAimArrow(player.transform);
            controller.landingPreview = BuildLandingPreview(player.transform);

            controller.alternateSchemesEnabled = false;
            controller.facingArrow = BuildFacingArrow(player.transform);

            controller.enabled = true;

            freeMoveController = player.GetComponent<KineticCubeControllerFreeMove>();
            if (freeMoveController == null) freeMoveController = player.AddComponent<KineticCubeControllerFreeMove>();

            freeMoveController.moveSpeed = 4f;
            freeMoveController.moveDeadzone = 0.15f;
            freeMoveController.airControlAcceleration = 7f;
            freeMoveController.airControlDeadzone = 0.1f;
            freeMoveController.maxLeanAngle = 22f;
            freeMoveController.leanSpeed = 8f;
            freeMoveController.groundCheckDistance = 0.6f;
            freeMoveController.fallResetY = -30f;
            freeMoveController.moveAction = moveRef;
            freeMoveController.visual = visualGo.transform;

            freeMoveController.enabled = true;

            foreach (Renderer childRenderer in player.GetComponentsInChildren<Renderer>(true))
            {
                childRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return controller;
        }

        static AimArrowIndicator BuildAimArrow(Transform parent)
        {
            Transform existing = parent.Find("AimArrow");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject arrowRoot = new GameObject("AimArrow");
            arrowRoot.transform.SetParent(parent, false);
            arrowRoot.transform.localPosition = Vector3.zero;

            GameObject shaftGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaftGo.name = "Shaft";
            UnityEngine.Object.DestroyImmediate(shaftGo.GetComponent<Collider>());
            shaftGo.transform.SetParent(arrowRoot.transform, false);

            GameObject headGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headGo.name = "Head";
            UnityEngine.Object.DestroyImmediate(headGo.GetComponent<Collider>());
            headGo.transform.SetParent(arrowRoot.transform, false);
            headGo.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            headGo.transform.localScale = new Vector3(0.4f, 0.12f, 0.4f);

            Material arrowMat = new Material(FindBestShader());
            Color arrowColor = new Color(1f, 0.85f, 0.1f);
            arrowMat.color = arrowColor;
            arrowMat = SaveMaterialAsset(arrowMat, "AimArrowMaterial");
            shaftGo.GetComponent<Renderer>().sharedMaterial = arrowMat;
            headGo.GetComponent<Renderer>().sharedMaterial = arrowMat;

            AimArrowIndicator indicator = arrowRoot.AddComponent<AimArrowIndicator>();
            indicator.shaft = shaftGo.transform;
            indicator.head = headGo.transform;
            indicator.arrowColor = arrowColor;
            indicator.SetAim(Vector3.forward, 0f);
            indicator.SetVisible(false);

            return indicator;
        }

        static FacingArrowIndicator BuildFacingArrow(Transform parent)
        {
            Transform existing = parent.Find("FacingArrow");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject arrowRoot = new GameObject("FacingArrow");
            arrowRoot.transform.SetParent(parent, false);
            arrowRoot.transform.localPosition = new Vector3(0f, 0.55f, 0f);

            GameObject shaftGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaftGo.name = "Shaft";
            UnityEngine.Object.DestroyImmediate(shaftGo.GetComponent<Collider>());
            shaftGo.transform.SetParent(arrowRoot.transform, false);
            shaftGo.transform.localScale = new Vector3(0.12f, 0.05f, 0.6f);
            shaftGo.transform.localPosition = new Vector3(0f, 0f, 0.3f);

            GameObject headGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headGo.name = "Head";
            UnityEngine.Object.DestroyImmediate(headGo.GetComponent<Collider>());
            headGo.transform.SetParent(arrowRoot.transform, false);
            headGo.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            headGo.transform.localScale = new Vector3(0.32f, 0.05f, 0.32f);
            headGo.transform.localPosition = new Vector3(0f, 0f, 0.62f);

            Material arrowMat = new Material(FindBestShader());
            Color arrowColor = new Color(0.9f, 0.05f, 0.05f);
            arrowMat.color = arrowColor;
            arrowMat = SaveMaterialAsset(arrowMat, "FacingArrowMaterial");
            shaftGo.GetComponent<Renderer>().sharedMaterial = arrowMat;
            headGo.GetComponent<Renderer>().sharedMaterial = arrowMat;

            FacingArrowIndicator indicator = arrowRoot.AddComponent<FacingArrowIndicator>();
            indicator.shaft = shaftGo.transform;
            indicator.head = headGo.transform;
            indicator.arrowColor = arrowColor;
            indicator.SetFacingYaw(0f);
            indicator.SetVisible(false);

            return indicator;
        }

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

            const float diameter = 1.6f;
            const float thickness = 0.02f;
            visualGo.transform.localScale = new Vector3(diameter, thickness, diameter);

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

        static void BuildSandboxSignText()
        {

            GameObject stale = GameObject.Find("SandboxSignText");
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale);

            GameObject existing = GameObject.Find("ParkourHint");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            GameObject root = new GameObject("ParkourHint");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject textGo = new GameObject("ParkourHintText", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);

            rt.anchoredPosition = new Vector2(-24f, -76f);
            rt.sizeDelta = new Vector2(460f, 140f);

            Text text = textGo.AddComponent<Text>();
            text.font = FindBestFont();
            text.fontSize = 22;
            text.alignment = TextAnchor.UpperRight;
            text.color = Color.white;
            text.text =
                "Once you're comfortable with the controls,\n" +
                "you can head to the Parkour level\n" +
                "through the Pause Menu.";

            Shadow shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            TimedMessage timed = textGo.AddComponent<TimedMessage>();
            timed.displayDuration = 3f;
        }

        const int SandboxPlatformCount = 5;

        const float SandboxPlatformRadius = 8.5f;
        const float SandboxPlatformRadiusJitter = 1.5f;
        const float SandboxPlatformAngleJitterDeg = 10f;

        const float SandboxPlatformMinGap = 0.02f;
        const float SandboxPlatformMaxGap = 0.15f;

        static void BuildSandboxPlatforms(Vector3 spawnPosition)
        {
            GameObject container = GameObject.Find("SandboxPlatforms");
            if (container != null) UnityEngine.Object.DestroyImmediate(container);
            container = new GameObject("SandboxPlatforms");

            GameObject planeGo = GameObject.Find("Plane");
            float groundY = planeGo != null ? planeGo.transform.position.y : 0f;

            Material platformMat = new Material(FindBestShader());
            platformMat.color = new Color(1f, 0.55f, 0.15f);
            platformMat = SaveMaterialAsset(platformMat, "SandboxPlatformMaterial");

            Vector3 platformSize = new Vector3(2.2f, 0.3f, 2.2f);

            for (int i = 0; i < SandboxPlatformCount; i++)
            {

                float angleDeg = i * (360f / SandboxPlatformCount) + UnityEngine.Random.Range(-SandboxPlatformAngleJitterDeg, SandboxPlatformAngleJitterDeg);
                float radius = SandboxPlatformRadius + UnityEngine.Random.Range(-SandboxPlatformRadiusJitter, SandboxPlatformRadiusJitter);
                float angleRad = angleDeg * Mathf.Deg2Rad;

                float x = spawnPosition.x + radius * Mathf.Sin(angleRad);
                float z = spawnPosition.z + radius * Mathf.Cos(angleRad);

                float gap = UnityEngine.Random.Range(SandboxPlatformMinGap, SandboxPlatformMaxGap);
                float y = groundY + gap + platformSize.y * 0.5f;

                GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                platform.name = "SandboxPlatform" + i;
                platform.transform.SetParent(container.transform, true);
                platform.transform.position = new Vector3(x, y, z);
                platform.transform.localScale = platformSize;
                platform.GetComponent<Renderer>().sharedMaterial = platformMat;
            }
        }

        static LandingPreviewController BuildLandingPreview(Transform parent)
        {
            Transform existing = parent.Find("LandingPreview");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject root = new GameObject("LandingPreview");
            root.transform.SetParent(parent, false);

            Color previewColor = new Color(0.4f, 0.9f, 1f, 0.4f);
            Material ghostMat = new Material(FindBestShader());
            ghostMat.color = previewColor;
            MakeTransparent(ghostMat, previewColor.a);
            ghostMat = SaveMaterialAsset(ghostMat, "GhostPreviewMaterial");

            Material solidMat = new Material(FindBestShader());
            solidMat.color = new Color(0.4f, 0.9f, 1f, 1f);
            solidMat = SaveMaterialAsset(solidMat, "PreviewSolidMaterial");

            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "GhostCube";
            ghost.transform.SetParent(root.transform, false);
            UnityEngine.Object.DestroyImmediate(ghost.GetComponent<Collider>());
            ghost.GetComponent<Renderer>().sharedMaterial = ghostMat;

            GameObject trail = new GameObject("Trail");
            trail.transform.SetParent(root.transform, false);

            Transform[] dots = new Transform[60];
            for (int i = 0; i < dots.Length; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dot.name = "Dot" + i;
                dot.transform.SetParent(trail.transform, false);
                dot.transform.localScale = Vector3.one * 0.15f;
                UnityEngine.Object.DestroyImmediate(dot.GetComponent<Collider>());
                dot.GetComponent<Renderer>().sharedMaterial = solidMat;
                dots[i] = dot.transform;
            }

            GameObject crosshair = new GameObject("Crosshair");
            crosshair.transform.SetParent(root.transform, false);

            GameObject barX = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barX.name = "BarX";
            barX.transform.SetParent(crosshair.transform, false);
            UnityEngine.Object.DestroyImmediate(barX.GetComponent<Collider>());
            barX.transform.localScale = new Vector3(1.2f, 0.08f, 0.15f);
            barX.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            barX.GetComponent<Renderer>().sharedMaterial = solidMat;

            GameObject barZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barZ.name = "BarZ";
            barZ.transform.SetParent(crosshair.transform, false);
            UnityEngine.Object.DestroyImmediate(barZ.GetComponent<Collider>());
            barZ.transform.localScale = new Vector3(0.15f, 0.08f, 1.2f);
            barZ.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            barZ.GetComponent<Renderer>().sharedMaterial = solidMat;

            GameObject circle = new GameObject("Circle");
            circle.transform.SetParent(crosshair.transform, false);

            const int ringSegmentCount = 16;
            const float ringRadius = 0.75f;
            float ringSegmentLength = (2f * Mathf.PI * ringRadius / ringSegmentCount) * 0.6f;
            for (int i = 0; i < ringSegmentCount; i++)
            {
                float angleDeg = i * (360f / ringSegmentCount);
                Quaternion segRotation = Quaternion.Euler(0f, angleDeg, 0f);

                GameObject ringSegment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ringSegment.name = "RingSegment" + i;
                ringSegment.transform.SetParent(circle.transform, false);
                UnityEngine.Object.DestroyImmediate(ringSegment.GetComponent<Collider>());
                ringSegment.transform.localPosition = (segRotation * Vector3.forward) * ringRadius + new Vector3(0f, 0.03f, 0f);
                ringSegment.transform.localRotation = segRotation;
                ringSegment.transform.localScale = new Vector3(ringSegmentLength, 0.08f, 0.08f);
                ringSegment.GetComponent<Renderer>().sharedMaterial = solidMat;
            }

            ghost.SetActive(false);
            trail.SetActive(false);
            crosshair.SetActive(false);

            LandingPreviewController preview = root.AddComponent<LandingPreviewController>();
            preview.ghostGroup = ghost;
            preview.ghostGroundOffset = 0f;
            preview.markerGroundOffset = -0.5f;
            preview.trailGroup = trail;
            preview.crosshairGroup = crosshair;
            preview.trailDots = dots;
            preview.maxDotSpacing = 1f;
            preview.positionSmoothTime = 0.05f;
            preview.snapDistance = 25f;
            preview.ghostAndCrosshairEnabled = false;

            return preview;
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

        static ThirdPersonOrbitCamera BuildCameraRig(GameObject camGo, InputActionReference lookRef)
        {
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();
            if (orbitCam == null) orbitCam = camGo.AddComponent<ThirdPersonOrbitCamera>();
            orbitCam.lookAction = lookRef;

            orbitCam.minPitch = -75f;
            orbitCam.maxPitch = 75f;
            orbitCam.recenterSpeed = 240f;
            return orbitCam;
        }

        static Text BuildPauseSystem(InputActionReference pauseRef, InputActionReference radialMenuRef, out Text controlsHintOut, out Text controlsBodyOut,
            out EnergyMeterController energyMeterOut, out RadialMenuController radialMenuOut)
        {
            GameObject root = GameObject.Find("PauseSystem");
            if (root == null) root = new GameObject("PauseSystem");

            GameObject eventSystemGo = FindOrCreateChild(root.transform, "EventSystem");
            if (eventSystemGo.GetComponent<EventSystem>() == null) eventSystemGo.AddComponent<EventSystem>();
            if (eventSystemGo.GetComponent<InputSystemUIInputModule>() == null) eventSystemGo.AddComponent<InputSystemUIInputModule>();

            GameObject canvasGo = FindOrCreateChild(root.transform, "PauseCanvas");
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            if (canvasGo.GetComponent<GraphicRaycaster>() == null) canvasGo.AddComponent<GraphicRaycaster>();

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);
            Color backdrop = new Color(0f, 0f, 0f, 0.75f);

            DestroyChildIfExists(canvasGo.transform, "PausePanel");
            DestroyChildIfExists(canvasGo.transform, "ControlsPanel");
            DestroyChildIfExists(canvasGo.transform, "ScenesPanel");
            DestroyChildIfExists(canvasGo.transform, "PreviewModeLabel");
            DestroyChildIfExists(canvasGo.transform, "ControlsHintLabel");
            DestroyChildIfExists(canvasGo.transform, "EnergyMeter");
            DestroyChildIfExists(canvasGo.transform, "RadialMenu");

            GameObject labelGo = new GameObject("PreviewModeLabel", typeof(RectTransform));
            labelGo.transform.SetParent(canvasGo.transform, false);
            RectTransform labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0f, 0f);
            labelRt.pivot = new Vector2(0f, 0f);
            labelRt.anchoredPosition = new Vector2(24f, 24f);
            labelRt.sizeDelta = new Vector2(900f, 50f);
            Text previewModeLabel = labelGo.AddComponent<Text>();
            previewModeLabel.font = font;
            previewModeLabel.fontSize = 30;
            previewModeLabel.alignment = TextAnchor.LowerLeft;
            previewModeLabel.color = Color.white;
            previewModeLabel.text = "";

            GameObject hintGo = new GameObject("ControlsHintLabel", typeof(RectTransform));
            hintGo.transform.SetParent(canvasGo.transform, false);
            RectTransform hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 1f);
            hintRt.anchorMax = new Vector2(0f, 1f);
            hintRt.pivot = new Vector2(0f, 1f);
            hintRt.anchoredPosition = new Vector2(24f, -24f);

            hintRt.sizeDelta = new Vector2(600f, 300f);
            Text hintText = hintGo.AddComponent<Text>();
            hintText.font = font;
            hintText.fontSize = 30;
            hintText.alignment = TextAnchor.UpperLeft;
            hintText.color = new Color(1f, 1f, 1f, 0.9f);

            hintText.text = "";

            Shadow hintShadow = hintGo.AddComponent<Shadow>();
            hintShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            hintShadow.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject pausePanel = CreatePanel("PausePanel", canvasGo.transform, backdrop);
            CreateText("Title", pausePanel.transform, "PAUSED", font, 48, new Vector2(0f, 200f), new Vector2(600f, 80f));
            GameObject restartBtn = CreateButton("RestartButton", pausePanel.transform, "Restart", font, accent, new Vector2(0f, 95f), new Vector2(300f, 70f));
            GameObject scenesBtn = CreateButton("ScenesButton", pausePanel.transform, "Scenes", font, accent, new Vector2(0f, 5f), new Vector2(300f, 70f));
            GameObject controlsBtn = CreateButton("ControlsButton", pausePanel.transform, "Controls", font, accent, new Vector2(0f, -85f), new Vector2(300f, 70f));
            GameObject quitBtn = CreateButton("QuitButton", pausePanel.transform, "Quit", font, accent, new Vector2(0f, -175f), new Vector2(300f, 70f));

            Text winLabel = CreateText("WinLabel", pausePanel.transform, "You Win!", font, 64, new Vector2(0f, 300f), new Vector2(700f, 90f));
            winLabel.color = new Color(0.3f, 1f, 0.45f);
            winLabel.gameObject.SetActive(false);

            GameObject controlsPanel = CreatePanel("ControlsPanel", canvasGo.transform, backdrop);
            CreateText("ControlsTitle", controlsPanel.transform, "CONTROLS", font, 48, new Vector2(0f, 220f), new Vector2(600f, 80f));

            Text controlsBody = CreateText("ControlsBody", controlsPanel.transform, "", font, 30, new Vector2(0f, 50f), new Vector2(900f, 300f));
            controlsBody.alignment = TextAnchor.MiddleLeft;
            GameObject backBtn = CreateButton("BackButton", controlsPanel.transform, "Back", font, accent, new Vector2(0f, -170f), new Vector2(300f, 70f));

            GameObject scenesPanel = CreatePanel("ScenesPanel", canvasGo.transform, backdrop);
            CreateText("ScenesTitle", scenesPanel.transform, "SCENES", font, 48, new Vector2(0f, 220f), new Vector2(600f, 80f));
            GameObject[] sceneButtons = new GameObject[SceneMenuEntries.Length];
            float sceneButtonY = 100f;
            for (int i = 0; i < SceneMenuEntries.Length; i++)
            {
                sceneButtons[i] = CreateButton("Scene_" + i + "Button", scenesPanel.transform, SceneMenuEntries[i].label, font, accent, new Vector2(0f, sceneButtonY), new Vector2(300f, 70f));
                sceneButtonY -= 90f;
            }
            GameObject scenesBackBtn = CreateButton("ScenesBackButton", scenesPanel.transform, "Back", font, accent, new Vector2(0f, sceneButtonY - 40f), new Vector2(300f, 70f));

            pausePanel.SetActive(false);
            controlsPanel.SetActive(false);
            scenesPanel.SetActive(false);

            GameObject controllerGo = FindOrCreateChild(root.transform, "PauseController");
            PauseController controller = controllerGo.GetComponent<PauseController>();
            if (controller == null) controller = controllerGo.AddComponent<PauseController>();

            controller.pauseAction = pauseRef;
            controller.pausePanel = pausePanel;
            controller.controlsPanel = controlsPanel;
            controller.scenesPanel = scenesPanel;
            controller.firstPauseButton = restartBtn;
            controller.firstControlsButton = backBtn;
            controller.firstScenesButton = sceneButtons.Length > 0 ? sceneButtons[0] : scenesBackBtn;
            controller.controlsBodyText = controlsBody;
            controller.winLabel = winLabel;

            WireButton(restartBtn, controller.OnRestartClicked);
            WireButton(controlsBtn, controller.OnControlsClicked);
            WireButton(quitBtn, controller.OnQuitClicked);
            WireButton(backBtn, controller.OnControlsBackClicked);
            WireButton(scenesBtn, controller.OnScenesClicked);
            WireButton(scenesBackBtn, controller.OnScenesBackClicked);
            for (int i = 0; i < SceneMenuEntries.Length; i++)
            {
                WireSceneButton(sceneButtons[i], controller.LoadSceneByName, SceneMenuEntries[i].sceneName);
            }

            GameObject energyContainer = new GameObject("EnergyMeter", typeof(RectTransform));
            energyContainer.transform.SetParent(canvasGo.transform, false);
            RectTransform energyRt = energyContainer.GetComponent<RectTransform>();
            energyRt.anchorMin = new Vector2(1f, 1f);
            energyRt.anchorMax = new Vector2(1f, 1f);
            energyRt.pivot = new Vector2(1f, 1f);
            energyRt.anchoredPosition = new Vector2(-24f, -24f);
            energyRt.sizeDelta = new Vector2(320f, 36f);

            const float meterOutlineThickness = 3f;
            CreatePanel("Outline", energyContainer.transform, new Color(1f, 1f, 1f, 0.9f));
            InsetRect(CreatePanel("Backdrop", energyContainer.transform, new Color(0f, 0f, 0f, 0.5f)), meterOutlineThickness);

            Image energyFillImage = CreateFillBar("EnergyFill", energyContainer.transform, new Color(0.95f, 0.82f, 0.15f), meterOutlineThickness);
            Image chargeFillImage = CreateFillBar("ChargeFill", energyContainer.transform, new Color(0.3f, 0.65f, 1f), meterOutlineThickness);
            chargeFillImage.gameObject.SetActive(false);

            GameObject energyMeterGo = FindOrCreateChild(root.transform, "EnergyMeter");
            EnergyMeterController energyMeter = energyMeterGo.GetComponent<EnergyMeterController>();
            if (energyMeter == null) energyMeter = energyMeterGo.AddComponent<EnergyMeterController>();
            energyMeter.energyFillImage = energyFillImage;
            energyMeter.chargeFillImage = chargeFillImage;

            GameObject radialRoot = new GameObject("RadialMenu", typeof(RectTransform));
            radialRoot.transform.SetParent(canvasGo.transform, false);
            RectTransform radialRt = radialRoot.GetComponent<RectTransform>();
            radialRt.anchorMin = new Vector2(0.5f, 0.5f);
            radialRt.anchorMax = new Vector2(0.5f, 0.5f);
            radialRt.pivot = new Vector2(0.5f, 0.5f);
            radialRt.anchoredPosition = Vector2.zero;
            radialRt.sizeDelta = new Vector2(500f, 500f);

            CreatePanel("Backdrop", radialRoot.transform, new Color(0f, 0f, 0f, 0.55f));
            Text radialUp = CreateText("RadialUp", radialRoot.transform, "Launch Instantly", font, 28, new Vector2(0f, 160f), new Vector2(320f, 60f));
            Text radialRight = CreateText("RadialRight", radialRoot.transform, "Stick Aim", font, 28, new Vector2(160f, 0f), new Vector2(280f, 60f));
            Text radialDown = CreateText("RadialDown", radialRoot.transform, "Mixed", font, 28, new Vector2(0f, -160f), new Vector2(320f, 60f));
            Text radialLeft = CreateText("RadialLeft", radialRoot.transform, "Defy Gravity", font, 28, new Vector2(-160f, 0f), new Vector2(280f, 60f));
            radialRoot.SetActive(false);

            GameObject radialMenuGo = FindOrCreateChild(root.transform, "RadialMenuController");
            RadialMenuController radialMenu = radialMenuGo.GetComponent<RadialMenuController>();
            if (radialMenu == null) radialMenu = radialMenuGo.AddComponent<RadialMenuController>();
            radialMenu.radialMenuAction = radialMenuRef;
            radialMenu.menuRoot = radialRoot;
            radialMenu.upLabel = radialUp;
            radialMenu.rightLabel = radialRight;
            radialMenu.downLabel = radialDown;
            radialMenu.leftLabel = radialLeft;

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabFolder + "/PauseSystem.prefab", InteractionMode.AutomatedAction);

            controlsHintOut = hintText;
            controlsBodyOut = controlsBody;
            energyMeterOut = energyMeter;
            radialMenuOut = radialMenu;
            return previewModeLabel;
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

        static GameObject InsetRect(GameObject go, float inset)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return go;
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

        static GameObject FindOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static void DestroyChildIfExists(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
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

            Image image = go.AddComponent<Image>();
            image.color = backgroundColor;

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

        const string MaterialFolder = "Assets/Materials";

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

        static Font FindBestFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        static Shader FindBestShader()
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

            throw new Exception("KineticEnergySetup: no usable shader found for the aim arrow material.");
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

        const float SlowPacedBlockSize = 1.5f;

        const int SlowPacedHalfInterior = 13;
        const int SlowPacedWallLayers = 14;
        const int SlowPacedHallHalfWidth = 1;
        const int SlowPacedHallLayers = 3;

        const int SlowPacedHallStartK = SlowPacedHalfInterior + 2;
        const int SlowPacedVoidStartK = SlowPacedHallStartK + 4;

        const int SlowPacedVoidBlocks = 32;
        const int SlowPacedVoidEndK = SlowPacedVoidStartK + SlowPacedVoidBlocks - 1;
        const int SlowPacedFinishStartK = SlowPacedVoidEndK + 1;
        const int SlowPacedHallEndK = SlowPacedFinishStartK + 3;

        [MenuItem("Tools/Kinetic Energy/Setup SlowPacedLevel")]
        public static void SetupSlowPacedLevel()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SlowPacedLevelScenePath) == null)
            {

                if (!AssetDatabase.CopyAsset(ScenePath, SlowPacedLevelScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Sandbox Scene to create SlowPacedLevel.");
                }
            }

            EditorSceneManager.OpenScene(SlowPacedLevelScenePath, OpenSceneMode.Single);

            BuildDirectionalLight();
            BuildGlobalVolume();

            Material crackMaterial = EnsureCrackDecalAssets();

            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ThirdPersonCameraRig.prefab");
            GameObject pauseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (playerAsset == null || cameraAsset == null || pauseAsset == null)
            {
                throw new Exception("KineticEnergySetup: SlowPacedLevel needs Player/ThirdPersonCameraRig/PauseSystem prefabs - run Setup() (part of SetupAll) first.");
            }

            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");

            DestroyIfExists("SandboxPlatforms");
            DestroyIfExists("ParkourHint");
            DestroyIfExists("SlowPacedRoom");
            DestroyIfExists("OpeningLookTarget");

            GameObject playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset);
            GameObject camGo = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset);
            GameObject pauseGo = (GameObject)PrefabUtility.InstantiatePrefab(pauseAsset);

            KineticCubeController controller = playerGo.GetComponent<KineticCubeController>();
            KineticCubeControllerFreeMove freeMoveController = playerGo.GetComponent<KineticCubeControllerFreeMove>();
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();

            controller.cameraTransform = camGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            ApplyLaunchTuning(controller);

            controller.SetControlScheme(ControlScheme.Mixed);
            controller.schemeSwitchingEnabled = false;
            controller.stickyWallsOnly = true;
            controller.nonStickyWallStickDuration = 0.3f;

            controller.minLaunchForce = 67.5f;
            controller.maxLaunchForce = 165f;
            controller.minLaunchDamping = 5.69f;
            controller.maxLaunchDamping = 2.15f;

            controller.refundSpentEnergyOnly = true;
            controller.fastPacedRefundMultiplier = 1.2f;

            controller.landingPreview.ghostAndCrosshairEnabled = true;
            controller.landingPreview.initialMode = PredictionMode.TrailAndCrosshair;

            ImpactCrackDecals crackDecals = playerGo.GetComponent<ImpactCrackDecals>();
            if (crackDecals == null) crackDecals = playerGo.AddComponent<ImpactCrackDecals>();
            crackDecals.decalMaterial = crackMaterial;
            crackDecals.sheetColumns = 3;
            crackDecals.sheetRows = 3;
            crackDecals.decalSize = 1.2f;
            crackDecals.surfaceOffset = 0.02f;
            crackDecals.holdSeconds = 3f;
            crackDecals.fadeSeconds = 1f;
            crackDecals.maxDecals = 60;
            crackDecals.minImpactSpeed = 1.5f;
            crackDecals.minSpawnInterval = 0.05f;

            Text modeLabel = pauseGo.transform.Find("PauseCanvas/PreviewModeLabel")?.GetComponent<Text>();
            controller.landingPreview.modeLabel = modeLabel;
            controller.controlsHintLabel = pauseGo.transform.Find("PauseCanvas/ControlsHintLabel")?.GetComponent<Text>();
            controller.controlsPanelBody = pauseGo.transform.Find("PauseCanvas/ControlsPanel/ControlsBody")?.GetComponent<Text>();
            controller.energyMeter = pauseGo.transform.Find("EnergyMeter")?.GetComponent<EnergyMeterController>();
            RadialMenuController radialMenu = pauseGo.transform.Find("RadialMenuController")?.GetComponent<RadialMenuController>();
            if (radialMenu != null) radialMenu.controller = controller;

            PauseController pauseController = pauseGo.transform.Find("PauseController")?.GetComponent<PauseController>();

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);
            EditorUtility.SetDirty(controller.landingPreview);
            EditorUtility.SetDirty(crackDecals);
            if (radialMenu != null) EditorUtility.SetDirty(radialMenu);

            GameObject lookTarget = BuildSlowPacedRoom(playerGo.transform, camGo.transform, pauseController);
            BuildCameraStartFacing(playerGo.transform, orbitCam, lookTarget.transform);
            BuildPlayerShadow(playerGo.transform);

            AddSceneToBuildSettings(SlowPacedLevelScenePath);

            Scene slowPacedScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(slowPacedScene);
            EditorSceneManager.SaveScene(slowPacedScene);
            AssetDatabase.SaveAssets();

            Debug.Log("KineticEnergySetup: SlowPacedLevel setup complete OK");
        }

        static GameObject BuildSlowPacedRoom(Transform player, Transform cameraTransform, PauseController pauseController)
        {
            GameObject room = new GameObject("SlowPacedRoom");

            float b = SlowPacedBlockSize;
            int ring = SlowPacedHalfInterior + 1;
            int hallWallX = SlowPacedHallHalfWidth + 1;

            Material floorA = MakeSlowPacedMaterial("SlowPacedFloorA", new Color(0.62f, 0.64f, 0.70f));
            Material floorB = MakeSlowPacedMaterial("SlowPacedFloorB", new Color(0.52f, 0.54f, 0.60f));
            Material wallA = MakeSlowPacedMaterial("SlowPacedWallA", new Color(0.42f, 0.46f, 0.56f));
            Material wallB = MakeSlowPacedMaterial("SlowPacedWallB", new Color(0.36f, 0.40f, 0.50f));
            Material ceilingA = MakeSlowPacedMaterial("SlowPacedCeilingA", new Color(0.30f, 0.32f, 0.40f));
            Material ceilingB = MakeSlowPacedMaterial("SlowPacedCeilingB", new Color(0.26f, 0.28f, 0.35f));
            Material stickyA = MakeSlowPacedMaterial("SlowPacedStickyA", new Color(0.25f, 0.85f, 0.45f));
            Material stickyB = MakeSlowPacedMaterial("SlowPacedStickyB", new Color(0.20f, 0.72f, 0.38f));
            Material finishMat = MakeSlowPacedMaterial("SlowPacedFinishBlockMaterial", new Color(0.95f, 0.75f, 0.15f));
            Material crashCubeMat = MakeSlowPacedMaterial("SlowPacedCrashCubeMaterial", new Color(0.95f, 0.55f, 0.15f));

            Transform floorGroup = NewGroup(room.transform, "Floor");
            Transform ceilingGroup = NewGroup(room.transform, "Ceiling");
            Transform frontWall = NewGroup(room.transform, "WallFront");
            Transform backWall = NewGroup(room.transform, "WallBackSticky");
            Transform leftWall = NewGroup(room.transform, "WallLeft");
            Transform rightWall = NewGroup(room.transform, "WallRight");
            Transform hallway = NewGroup(room.transform, "Hallway");
            Transform hallFloor = NewGroup(hallway, "HallwayFloor");
            Transform hallWallLeft = NewGroup(hallway, "HallwayWallLeft");
            Transform hallWallRight = NewGroup(hallway, "HallwayWallRight");
            Transform hallCeiling = NewGroup(hallway, "HallwayCeiling");
            Transform hallEndCap = NewGroup(hallway, "HallwayEndCap");
            Transform finishGroup = NewGroup(room.transform, "Finish");

            for (int i = -ring; i <= ring; i++)
            {
                for (int k = -ring; k <= ring; k++)
                {
                    CreatePbBlock(floorGroup, $"Floor_x{i}_z{k}", SlowPacedFloorCenter(i, k), SlowPacedChecker(i + k) ? floorA : floorB);
                    CreatePbBlock(ceilingGroup, $"Ceiling_x{i}_z{k}", SlowPacedBlockCenter(i, SlowPacedWallLayers, k), SlowPacedChecker(i + k) ? ceilingA : ceilingB);
                }
            }

            for (int layer = 0; layer < SlowPacedWallLayers; layer++)
            {
                for (int i = -ring; i <= ring; i++)
                {
                    for (int k = -ring; k <= ring; k++)
                    {
                        if (Mathf.Max(Mathf.Abs(i), Mathf.Abs(k)) != ring) continue;

                        bool isFront = k == ring;
                        bool isBack = k == -ring;
                        if (isFront && Mathf.Abs(i) <= SlowPacedHallHalfWidth && layer < SlowPacedHallLayers) continue;

                        Transform group = isFront ? frontWall : isBack ? backWall : i > 0 ? rightWall : leftWall;
                        bool sticky = isBack;
                        bool checker = SlowPacedChecker(i + layer + k);
                        Material mat = sticky ? (checker ? stickyA : stickyB) : (checker ? wallA : wallB);
                        CreatePbBlock(group, $"Wall_x{i}_y{layer}_z{k}", SlowPacedBlockCenter(i, layer, k), mat);
                    }
                }
            }

            for (int k = SlowPacedHallStartK; k <= SlowPacedHallEndK; k++)
            {
                bool voidRow = k >= SlowPacedVoidStartK && k <= SlowPacedVoidEndK;
                bool finishRow = k >= SlowPacedFinishStartK;

                if (!voidRow)
                {

                    for (int i = -hallWallX; i <= hallWallX; i++)
                    {
                        bool finishBlock = finishRow && Mathf.Abs(i) <= SlowPacedHallHalfWidth;
                        Transform parent = finishBlock ? finishGroup : hallFloor;
                        Material mat = finishBlock ? finishMat : (SlowPacedChecker(i + k) ? floorA : floorB);
                        string name = finishBlock ? $"Finish_x{i}_z{k}" : $"HallwayFloor_x{i}_z{k}";
                        CreatePbBlock(parent, name, SlowPacedFloorCenter(i, k), mat);
                    }
                }

                for (int layer = 0; layer < SlowPacedHallLayers; layer++)
                {
                    bool checkerLeft = SlowPacedChecker(-hallWallX + layer + k);
                    bool checkerRight = SlowPacedChecker(hallWallX + layer + k);
                    Material matLeft = voidRow ? (checkerLeft ? stickyA : stickyB) : (checkerLeft ? wallA : wallB);
                    Material matRight = voidRow ? (checkerRight ? stickyA : stickyB) : (checkerRight ? wallA : wallB);
                    CreatePbBlock(hallWallLeft, $"HallwayWallLeft_y{layer}_z{k}", SlowPacedBlockCenter(-hallWallX, layer, k), matLeft);
                    CreatePbBlock(hallWallRight, $"HallwayWallRight_y{layer}_z{k}", SlowPacedBlockCenter(hallWallX, layer, k), matRight);
                }

                for (int i = -hallWallX; i <= hallWallX; i++)
                {
                    CreatePbBlock(hallCeiling, $"HallwayCeiling_x{i}_z{k}", SlowPacedBlockCenter(i, SlowPacedHallLayers, k), SlowPacedChecker(i + k) ? ceilingA : ceilingB);
                }
            }

            for (int layer = 0; layer <= SlowPacedHallLayers; layer++)
            {
                for (int i = -hallWallX; i <= hallWallX; i++)
                {
                    CreatePbBlock(hallEndCap, $"HallwayEndCap_x{i}_y{layer}", SlowPacedBlockCenter(i, layer, SlowPacedHallEndK + 1), SlowPacedChecker(i + layer) ? wallA : wallB);
                }
            }

            float finishCenterZ = (SlowPacedFinishStartK + SlowPacedHallEndK) * 0.5f * b;
            Vector3 finishCenter = new Vector3(0f, 0f, finishCenterZ);
            float finishWidth = (SlowPacedHallHalfWidth * 2 + 1) * b;
            float finishLength = (SlowPacedHallEndK - SlowPacedFinishStartK + 1) * b;

            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "FinishPad";
            pad.transform.SetParent(finishGroup, true);
            UnityEngine.Object.DestroyImmediate(pad.GetComponent<Collider>());
            const float padHeight = 0.05f;
            const float zFightGap = 0.03f;
            pad.transform.position = finishCenter + new Vector3(0f, zFightGap + padHeight * 0.5f, 0f);
            pad.transform.localScale = new Vector3(finishWidth, padHeight, finishLength);
            Color padColor = new Color(0.2f, 1f, 0.5f, 0.45f);
            Material padMat = new Material(FindBestShader());
            padMat.color = padColor;
            MakeTransparent(padMat, padColor.a);
            padMat = SaveMaterialAsset(padMat, "SlowPacedFinishPadMaterial");
            pad.GetComponent<Renderer>().sharedMaterial = padMat;

            GameObject textGo = new GameObject("FinishText");
            textGo.transform.SetParent(finishGroup, true);
            textGo.transform.position = finishCenter + new Vector3(0f, 2.4f, 0f);
            TextMesh textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = "Finish";
            textMesh.color = new Color(0.15f, 0.45f, 1f);
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.2f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            Billboard billboard = textGo.AddComponent<Billboard>();
            billboard.target = cameraTransform;

            GameObject trigger = new GameObject("FinishTrigger");
            trigger.transform.SetParent(finishGroup, true);
            trigger.transform.position = finishCenter + new Vector3(0f, 1f, 0f);
            BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(finishWidth, 2f, finishLength);
            FinishLineWin finishWin = trigger.AddComponent<FinishLineWin>();
            finishWin.pauseController = pauseController;

            Transform crashCubes = NewGroup(room.transform, "CrashCubes");
            System.Random cubeRng = new System.Random(20260807);
            float crashCubeSize = SlowPacedBlockSize * 2f;
            List<Vector3> placedCubes = new List<Vector3>();
            for (int c = 0; c < 3; c++)
            {
                Vector3 pos;
                int guard = 0;
                do
                {
                    float x = ((float)cubeRng.NextDouble() * 2f - 1f) * SlowPacedHalfInterior * 0.6f * b;
                    float z = ((float)cubeRng.NextDouble() * 2f - 1f) * SlowPacedHalfInterior * 0.6f * b;
                    float y = 4f + (float)cubeRng.NextDouble() * (SlowPacedWallLayers * b * 0.5f);
                    pos = new Vector3(x, y, z);
                } while (guard++ < 50 && placedCubes.Exists(p => Vector3.Distance(p, pos) < crashCubeSize * 4f));
                placedCubes.Add(pos);

                ProBuilderMesh cubeMesh = ShapeGenerator.GenerateCube(PivotLocation.Center, Vector3.one * crashCubeSize);
                GameObject cube = cubeMesh.gameObject;
                cube.name = $"CrashCube{c + 1}";
                cube.transform.SetParent(crashCubes, true);
                cube.transform.position = pos;
                cube.GetComponent<MeshRenderer>().sharedMaterial = crashCubeMat;
                cubeMesh.ToMesh();
                cubeMesh.Refresh();

                BoxCollider cubeCollider = cube.AddComponent<BoxCollider>();
                cubeCollider.center = Vector3.zero;
                cubeCollider.size = Vector3.one * crashCubeSize;
            }

            BuildSlowPacedColliders(room.transform);

            player.position = new Vector3(0f, 0.5f, 0f);

            GameObject lookTarget = new GameObject("OpeningLookTarget");
            lookTarget.transform.position = new Vector3(0f, SlowPacedHallLayers * 0.5f * b, (SlowPacedHalfInterior + 0.5f) * b);
            return lookTarget;
        }

        static Vector3 SlowPacedBlockCenter(int i, int layer, int k)
        {
            float b = SlowPacedBlockSize;
            return new Vector3(i * b, (layer + 0.5f) * b, k * b);
        }

        static Vector3 SlowPacedFloorCenter(int i, int k)
        {
            float b = SlowPacedBlockSize;
            return new Vector3(i * b, -0.5f * b, k * b);
        }

        static bool SlowPacedChecker(int value)
        {
            return ((value % 2) + 2) % 2 == 0;
        }

        static GameObject CreatePbBlock(Transform parent, string name, Vector3 center, Material material)
        {
            ProBuilderMesh pbMesh = ShapeGenerator.GenerateCube(PivotLocation.Center, Vector3.one * SlowPacedBlockSize);
            GameObject go = pbMesh.gameObject;
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            pbMesh.ToMesh();
            pbMesh.Refresh();
            return go;
        }

        static GameObject CreateRoomCollider(Transform parent, string name, Vector3 center, Vector3 size, bool sticky = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            if (sticky) go.AddComponent<StickySurface>().sticky = true;
            return go;
        }

        static void BuildSlowPacedColliders(Transform room)
        {
            float b = SlowPacedBlockSize;
            int ring = SlowPacedHalfInterior + 1;
            int hallWallX = SlowPacedHallHalfWidth + 1;

            float outerHalf = (ring + 0.5f) * b;
            float outerSpan = outerHalf * 2f;
            float roomHeight = SlowPacedWallLayers * b;
            float wallCenterY = roomHeight * 0.5f;
            float wallZ = ring * b;
            float hallHeight = SlowPacedHallLayers * b;
            float hallInnerWidth = (SlowPacedHallHalfWidth * 2 + 1) * b;
            float hallOuterWidth = (hallWallX * 2 + 1) * b;
            float hallWallXPos = hallWallX * b;

            Transform colliders = NewGroup(room, "Colliders");

            CreateRoomCollider(colliders, "FloorCollider", new Vector3(0f, -b * 0.5f, 0f), new Vector3(outerSpan, b, outerSpan));
            CreateRoomCollider(colliders, "CeilingCollider", new Vector3(0f, roomHeight + b * 0.5f, 0f), new Vector3(outerSpan, b, outerSpan));
            CreateRoomCollider(colliders, "WallBackCollider", new Vector3(0f, wallCenterY, -wallZ), new Vector3(outerSpan, roomHeight, b), true);
            CreateRoomCollider(colliders, "WallLeftCollider", new Vector3(-wallZ, wallCenterY, 0f), new Vector3(b, roomHeight, outerSpan));
            CreateRoomCollider(colliders, "WallRightCollider", new Vector3(wallZ, wallCenterY, 0f), new Vector3(b, roomHeight, outerSpan));

            float openingHalf = hallInnerWidth * 0.5f;
            float sideWidth = outerHalf - openingHalf;
            float sideCenterX = openingHalf + sideWidth * 0.5f;
            CreateRoomCollider(colliders, "WallFrontLeftCollider", new Vector3(-sideCenterX, wallCenterY, wallZ), new Vector3(sideWidth, roomHeight, b));
            CreateRoomCollider(colliders, "WallFrontRightCollider", new Vector3(sideCenterX, wallCenterY, wallZ), new Vector3(sideWidth, roomHeight, b));
            float aboveHeight = roomHeight - hallHeight;
            CreateRoomCollider(colliders, "WallFrontAboveOpeningCollider", new Vector3(0f, hallHeight + aboveHeight * 0.5f, wallZ), new Vector3(hallInnerWidth, aboveHeight, b));

            float entryMinZ = SlowPacedHallStartK * b - b * 0.5f;

            float entryFloorMinZ = entryMinZ - 2f * b;
            float entryMaxZ = (SlowPacedVoidStartK - 1) * b + b * 0.5f;
            float voidMaxZ = SlowPacedVoidEndK * b + b * 0.5f;
            float hallMaxZ = SlowPacedHallEndK * b + b * 0.5f;

            CreateRoomCollider(colliders, "HallwayEntryFloorCollider",
                new Vector3(0f, -b * 0.5f, (entryFloorMinZ + entryMaxZ) * 0.5f), new Vector3(hallOuterWidth, b, entryMaxZ - entryFloorMinZ));
            CreateRoomCollider(colliders, "FinishFloorCollider",
                new Vector3(0f, -b * 0.5f, (voidMaxZ + hallMaxZ) * 0.5f), new Vector3(hallOuterWidth, b, hallMaxZ - voidMaxZ));
            CreateRoomCollider(colliders, "HallwayCeilingCollider",
                new Vector3(0f, hallHeight + b * 0.5f, (entryMinZ + hallMaxZ) * 0.5f), new Vector3(hallOuterWidth, b, hallMaxZ - entryMinZ));

            float hallWallCenterY = hallHeight * 0.5f;
            (float minZ, float maxZ, bool sticky, string label)[] wallSegments =
            {
                (entryMinZ, entryMaxZ, false, "Entry"),
                (entryMaxZ, voidMaxZ, true, "VoidSticky"),
                (voidMaxZ, hallMaxZ, false, "Finish"),
            };
            foreach ((float minZ, float maxZ, bool sticky, string label) in wallSegments)
            {
                Vector3 size = new Vector3(b, hallHeight, maxZ - minZ);
                float centerZ = (minZ + maxZ) * 0.5f;
                CreateRoomCollider(colliders, $"HallwayWallLeft{label}Collider", new Vector3(-hallWallXPos, hallWallCenterY, centerZ), size, sticky);
                CreateRoomCollider(colliders, $"HallwayWallRight{label}Collider", new Vector3(hallWallXPos, hallWallCenterY, centerZ), size, sticky);
            }

            float endCapZ = (SlowPacedHallEndK + 1) * b;
            float endCapHeight = hallHeight + b;
            CreateRoomCollider(colliders, "HallwayEndCapCollider", new Vector3(0f, endCapHeight * 0.5f, endCapZ), new Vector3(hallOuterWidth, endCapHeight, b));
        }

        static Transform NewGroup(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Material MakeSlowPacedMaterial(string name, Color color)
        {
            Material mat = new Material(FindBestShader());
            mat.color = color;
            return SaveMaterialAsset(mat, name);
        }

        static void AddSceneToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("Tools/Kinetic Energy/Setup Tutorial")]
        public static void SetupTutorial()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(ScenePath, TutorialScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Sandbox Scene to create Tutorial.");
                }
            }

            EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);

            BuildDirectionalLight();
            BuildGlobalVolume();

            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ThirdPersonCameraRig.prefab");
            GameObject pauseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (playerAsset == null || cameraAsset == null || pauseAsset == null)
            {
                throw new Exception("KineticEnergySetup: Tutorial needs Player/ThirdPersonCameraRig/PauseSystem prefabs - run Setup() (part of SetupAll) first.");
            }

            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");
            DestroyIfExists("SandboxPlatforms");
            DestroyIfExists("ParkourHint");
            DestroyIfExists("TutorialCourse");

            GameObject playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset);
            GameObject camGo = (GameObject)PrefabUtility.InstantiatePrefab(cameraAsset);
            GameObject pauseGo = (GameObject)PrefabUtility.InstantiatePrefab(pauseAsset);

            KineticCubeController controller = playerGo.GetComponent<KineticCubeController>();
            KineticCubeControllerFreeMove freeMoveController = playerGo.GetComponent<KineticCubeControllerFreeMove>();
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();

            controller.cameraTransform = camGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            ApplyLaunchTuning(controller);

            controller.SetControlScheme(ControlScheme.Mixed);
            controller.schemeSwitchingEnabled = false;

            controller.stickyWallsOnly = true;

            controller.maxLaunchesPerFlight = 2;

            Text modeLabel = pauseGo.transform.Find("PauseCanvas/PreviewModeLabel")?.GetComponent<Text>();
            controller.landingPreview.modeLabel = modeLabel;
            controller.controlsHintLabel = pauseGo.transform.Find("PauseCanvas/ControlsHintLabel")?.GetComponent<Text>();
            controller.controlsPanelBody = pauseGo.transform.Find("PauseCanvas/ControlsPanel/ControlsBody")?.GetComponent<Text>();
            controller.energyMeter = pauseGo.transform.Find("EnergyMeter")?.GetComponent<EnergyMeterController>();
            RadialMenuController radialMenu = pauseGo.transform.Find("RadialMenuController")?.GetComponent<RadialMenuController>();
            if (radialMenu != null) radialMenu.controller = controller;

            PauseController pauseController = pauseGo.transform.Find("PauseController")?.GetComponent<PauseController>();

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);
            EditorUtility.SetDirty(controller.landingPreview);
            if (radialMenu != null) EditorUtility.SetDirty(radialMenu);

            GameObject firstTarget = BuildTutorialCourse(playerGo.transform, camGo.transform, pauseController);
            BuildCameraStartFacing(playerGo.transform, orbitCam, firstTarget.transform);
            BuildPlayerShadow(playerGo.transform);

            AddSceneToBuildSettings(TutorialScenePath);

            Scene tutorialScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(tutorialScene);
            EditorSceneManager.SaveScene(tutorialScene);
            AssetDatabase.SaveAssets();

            Debug.Log("KineticEnergySetup: Tutorial setup complete OK");
        }

        static GameObject BuildTutorialCourse(Transform player, Transform cameraTransform, PauseController pauseController)
        {
            GameObject course = new GameObject("TutorialCourse");

            Material platformMat = MakeSlowPacedMaterial("TutorialPlatformMaterial", new Color(0.50f, 0.55f, 0.62f));
            Material finishMat = MakeSlowPacedMaterial("TutorialFinishWallMaterial", new Color(0.95f, 0.75f, 0.15f));

            GameObject start = CreateTutorialSlab(course.transform, "StartPlatform", new Vector3(0f, -0.75f, 0f), new Vector3(6f, 1.5f, 6f), platformMat);
            GameObject platform2 = CreateTutorialSlab(course.transform, "Platform2", new Vector3(0f, -0.75f, 16f), new Vector3(6f, 1.5f, 6f), platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, 3.2f, 0f),
                "1. Hold Left Trigger to aim, stick to adjust,\nRight Trigger to launch across the gap");

            GameObject platform3 = CreateTutorialSlab(course.transform, "Platform3", new Vector3(0f, -12.75f, 42f), new Vector3(10f, 1.5f, 10f), platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, 3.2f, 16f),
                "2. Launch forward, then hold Left Trigger in the air\nto aim, hold West and release to slam down");

            CreateTutorialSlab(course.transform, "Platform4", new Vector3(0f, -0.75f, 52f), new Vector3(8f, 1.5f, 8f), platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, -8.8f, 42f),
                "3. Hold South and tilt the stick toward the high\nplatform, release to launch up onto it");

            Vector3 finishPlatformCenter = new Vector3(0f, -12.75f, 110f);
            Vector3 finishPlatformSize = new Vector3(10f, 1.5f, 14f);
            CreateTutorialSlab(course.transform, "FinishPlatform", finishPlatformCenter, finishPlatformSize, platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, 3.2f, 52f),
                "4. Launch forward, then hold Left Trigger and Right\nTrigger in the air - release at about half charge");

            float wallZ = finishPlatformCenter.z + finishPlatformSize.z * 0.5f - 0.75f;
            Vector3 wallSize = new Vector3(12f, 9f, 1.5f);
            Vector3 wallCenter = new Vector3(0f, -12f + wallSize.y * 0.5f, wallZ);
            GameObject wall = CreateTutorialSlab(course.transform, "FinishWall", wallCenter, wallSize, finishMat);

            GameObject wallText = new GameObject("FinishWallText");
            wallText.transform.SetParent(wall.transform, true);

            wallText.transform.position = wallCenter + new Vector3(0f, 0.5f, -(wallSize.z * 0.5f + 0.05f));
            TextMesh wallTextMesh = wallText.AddComponent<TextMesh>();
            wallTextMesh.text = "FINISH";
            wallTextMesh.color = new Color(0.15f, 0.45f, 1f);
            wallTextMesh.fontSize = 48;
            wallTextMesh.characterSize = 0.5f;
            wallTextMesh.anchor = TextAnchor.MiddleCenter;
            wallTextMesh.alignment = TextAlignment.Center;

            GameObject trigger = new GameObject("FinishTrigger");
            trigger.transform.SetParent(course.transform, true);
            trigger.transform.position = wallCenter + new Vector3(0f, 0f, -(wallSize.z * 0.5f + 1f));
            BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(wallSize.x, wallSize.y, 2f);
            FinishLineWin finishWin = trigger.AddComponent<FinishLineWin>();
            finishWin.pauseController = pauseController;

            player.position = new Vector3(0f, 0.5f, 0f);
            return platform2;
        }

        static GameObject CreateTutorialSlab(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            ProBuilderMesh pbMesh = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            GameObject go = pbMesh.gameObject;
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            pbMesh.ToMesh();
            pbMesh.Refresh();
            BoxCollider collider = go.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = size;
            return go;
        }

        static void BuildTutorialSign(Transform parent, Transform cameraTransform, Vector3 position, string text)
        {
            GameObject signGo = new GameObject("TutorialSign");
            signGo.transform.SetParent(parent, true);
            signGo.transform.position = position;

            TextMesh textMesh = signGo.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.color = Color.white;
            textMesh.fontSize = 40;
            textMesh.characterSize = 0.12f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;

            Billboard billboard = signGo.AddComponent<Billboard>();
            billboard.target = cameraTransform;
        }

        [MenuItem("Tools/Kinetic Energy/Create Launch Button Prefab")]
        public static void CreateLaunchButtonPrefab()
        {
            Material socketMat = MakeSlowPacedMaterial("ButtonSocketMaterial", new Color(0.30f, 0.32f, 0.36f));
            Material capMat = MakeSlowPacedMaterial("ButtonCapMaterial", new Color(0.85f, 0.20f, 0.20f));
            Material pressedMat = MakeSlowPacedMaterial("ButtonCapPressedMaterial", new Color(0.25f, 0.80f, 0.40f));

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            GameObject root = new GameObject("LaunchButton");
            try
            {

                CreateTutorialSlab(root.transform, "Socket", new Vector3(0f, 0.2f, 0f), new Vector3(3f, 0.4f, 3f), socketMat);

                GameObject cap = CreateTutorialSlab(root.transform, "ButtonCap", new Vector3(0f, 0.7f, 0f), new Vector3(2f, 0.6f, 2f), capMat);
                cap.AddComponent<NonStickSurface>();
                LaunchButtonCap capRelay = cap.AddComponent<LaunchButtonCap>();

                LaunchButton button = root.AddComponent<LaunchButton>();
                button.buttonCap = cap.transform;
                button.capRenderer = cap.GetComponent<MeshRenderer>();
                button.pressedMaterial = pressedMat;

                button.pressDepth = 0.5f;
                button.pressSpeed = 4f;
                button.stayPressed = true;

                capRelay.button = button;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/LaunchButton.prefab");
            }
            finally
            {

                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: LaunchButton prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Tutorial3 (FastPaced Air)")]
        public static void SetupFastPacedAirTutorial()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Tutorial3ScenePath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialScenePath) == null)
                {
                    throw new Exception("KineticEnergySetup: Tutorial3 needs Tutorial.unity to duplicate - run SetupTutorial first.");
                }
                if (!AssetDatabase.CopyAsset(TutorialScenePath, Tutorial3ScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Tutorial to create Tutorial3.");
                }
            }

            EditorSceneManager.OpenScene(Tutorial3ScenePath, OpenSceneMode.Single);

            GameObject playerGo = FindByNameIncludingInactive("Player");
            KineticCubeController controller = playerGo != null ? playerGo.GetComponent<KineticCubeController>() : null;
            if (controller == null)
            {
                throw new Exception("KineticEnergySetup: Tutorial3 copy has no Player with KineticCubeController.");
            }

            controller.mixedFastPacedAir = true;
            controller.fastPacedAimAction = FindActionReference("Player", "FastPacedAim");
            controller.fastPacedLaunchAction = FindActionReference("Player", "FastPacedLaunch");
            controller.landingPreview.ghostAndCrosshairEnabled = true;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(controller.landingPreview);

            foreach (TextMesh textMesh in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (textMesh.gameObject.name != "TutorialSign") continue;
                if (textMesh.text.Contains("West and release"))
                {
                    textMesh.text = "Launch forward, then hold Right Mouse / Left Trigger\nin the air to aim, look down, hold Left Mouse / Right\nTrigger and release to launch down";
                    EditorUtility.SetDirty(textMesh);
                }
                else if (textMesh.text.Contains("half charge"))
                {
                    textMesh.text = "Launch forward, then aim ahead in the air and\nrelease at about half charge to reach the wall";
                    EditorUtility.SetDirty(textMesh);
                }
            }

            AddSceneToBuildSettings(Tutorial3ScenePath);

            Scene fastPacedTutorialScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(fastPacedTutorialScene);
            EditorSceneManager.SaveScene(fastPacedTutorialScene);
            AssetDatabase.SaveAssets();

            Debug.Log("KineticEnergySetup: Tutorial3 (fast-paced air) setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Playtest Flow")]
        public static void SetupPlaytestFlow()
        {

            EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);
            KineticCubeController tutorialController = FindPlayerController("Tutorial");
            tutorialController.mouseAirControls = true;

            tutorialController.landingPreview.ghostAndCrosshairEnabled = true;
            tutorialController.landingPreview.initialMode = PredictionMode.TrailAndCrosshair;
            EditorUtility.SetDirty(tutorialController);
            EditorUtility.SetDirty(tutorialController.landingPreview);
            ReplaceWinWithNextScene("TestLevel1");
            SaveActiveScene();

            BuildTestLevel(TutorialScenePath, TestLevel1ScenePath, "Tutorial2");

            EditorSceneManager.OpenScene(Tutorial2ScenePath, OpenSceneMode.Single);
            ReplaceWinWithNextScene("TestLevel2");
            SaveActiveScene();
            EditorSceneManager.OpenScene(TestLevel2ScenePath, OpenSceneMode.Single);
            ReplaceWinWithNextScene("Tutorial3");
            SaveActiveScene();

            EditorSceneManager.OpenScene(Tutorial3ScenePath, OpenSceneMode.Single);
            ThirdPersonOrbitCamera fastPacedCamera = UnityEngine.Object.FindFirstObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);
            if (fastPacedCamera != null)
            {
                fastPacedCamera.firstPersonMinPitch = -89f;
                fastPacedCamera.firstPersonMaxPitch = 89f;
                EditorUtility.SetDirty(fastPacedCamera);
            }
            KineticCubeController fastPacedTutorialController = FindPlayerController("Tutorial3");
            fastPacedTutorialController.landingPreview.initialMode = PredictionMode.TrailAndCrosshair;
            EditorUtility.SetDirty(fastPacedTutorialController.landingPreview);
            ReplaceWinWithNextScene("TestLevel3");
            SaveActiveScene();

            BuildTestLevel(Tutorial3ScenePath, TestLevel3ScenePath, "MainMenu");

            SetupMainMenu();
            EnsurePlaytestBuildSettings();

            Debug.Log("KineticEnergySetup: Playtest flow setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Grounded Aim Option")]
        public static void SetupGroundedAimOption()
        {
            foreach (string scenePath in new[] { TutorialScenePath, TestLevel1ScenePath, Tutorial2ScenePath, TestLevel2ScenePath, Tutorial3ScenePath, TestLevel3ScenePath })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                KineticCubeController controller = FindPlayerController(scenePath);
                GameObject pauseSystem = FindByNameIncludingInactive("PauseSystem");
                Transform pausePanel = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas/PausePanel") : null;
                if (pausePanel == null)
                {
                    throw new Exception($"KineticEnergySetup: no PauseSystem/PauseCanvas/PausePanel in {scenePath}.");
                }

                DestroyChildIfExists(pausePanel, "GroundedAimButton");

                Font font = FindBestFont();
                Color accent = new Color(1f, 0.82f, 0.2f);
                GameObject toggleBtn = CreateButton("GroundedAimButton", pausePanel, "Aim: WASD", font, accent, new Vector2(0f, -355f), new Vector2(300f, 70f));

                DestroyChildIfExists(pausePanel, "ControllerSupportWarning");
                Text warning = CreateText("ControllerSupportWarning", pausePanel, "Controller support is disabled", font, 22, new Vector2(340f, -355f), new Vector2(360f, 70f));
                warning.color = new Color(1f, 0.55f, 0.35f);
                warning.gameObject.SetActive(false);

                GroundedAimToggle toggle = toggleBtn.AddComponent<GroundedAimToggle>();
                toggle.controller = controller;
                toggle.label = toggleBtn.transform.Find("Label")?.GetComponent<Text>();
                toggle.controllerWarning = warning.gameObject;
                WireButton(toggleBtn, toggle.Toggle);
                EditorUtility.SetDirty(toggle);

                SaveActiveScene();
            }

            Debug.Log("KineticEnergySetup: grounded aim option setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Tutorial2 (Grounded Air)")]
        public static void SetupGroundedAirScenes()
        {
            (string source, string dest, string next)[] duplicates =
            {
                (TutorialScenePath, Tutorial2ScenePath, "TestLevel2"),
                (TestLevel1ScenePath, TestLevel2ScenePath, "Tutorial3"),
            };

            foreach ((string source, string dest, string next) in duplicates)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(dest) == null)
                {
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(source) == null)
                    {
                        throw new Exception($"KineticEnergySetup: {dest} needs {source} to duplicate.");
                    }
                    if (!AssetDatabase.CopyAsset(source, dest))
                    {
                        throw new Exception($"KineticEnergySetup: failed to copy {source} to {dest}.");
                    }
                }

                EditorSceneManager.OpenScene(dest, OpenSceneMode.Single);
                KineticCubeController controller = FindPlayerController(dest);
                controller.airUsesGroundedAim = true;
                EditorUtility.SetDirty(controller);
                ReplaceWinWithNextScene(next);
                SaveActiveScene();
                AddSceneToBuildSettings(dest);
            }

            Debug.Log("KineticEnergySetup: Tutorial3/TestLevel3 setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Swap Tutorial 2-3 Names")]
        public static void SwapTutorial23Names()
        {
            SwapSceneAssets(Tutorial2ScenePath, Tutorial3ScenePath);
            SwapSceneAssets(TestLevel2ScenePath, TestLevel3ScenePath);

            (string scenePath, string next)[] chain =
            {
                (Tutorial2ScenePath, "TestLevel2"),
                (TestLevel2ScenePath, "Tutorial3"),
                (Tutorial3ScenePath, "TestLevel3"),
                (TestLevel3ScenePath, "MainMenu"),
            };
            foreach ((string scenePath, string next) in chain)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                ReplaceWinWithNextScene(next);
                SaveActiveScene();
                AddSceneToBuildSettings(scenePath);
            }

            SetupMainMenu();
            Debug.Log("KineticEnergySetup: Tutorial 2-3 name swap complete OK");
        }

        static void SwapSceneAssets(string pathA, string pathB)
        {
            string nameA = Path.GetFileNameWithoutExtension(pathA);
            string nameB = Path.GetFileNameWithoutExtension(pathB);
            ThrowIfRenameFailed(AssetDatabase.RenameAsset(pathA, nameA + "_swaptmp"), pathA);
            string tmpPath = pathA.Replace(nameA + ".unity", nameA + "_swaptmp.unity");
            ThrowIfRenameFailed(AssetDatabase.RenameAsset(pathB, nameA), pathB);
            ThrowIfRenameFailed(AssetDatabase.RenameAsset(tmpPath, nameB), tmpPath);
            AssetDatabase.SaveAssets();
        }

        static void ThrowIfRenameFailed(string error, string path)
        {
            if (!string.IsNullOrEmpty(error))
            {
                throw new Exception($"KineticEnergySetup: rename of {path} failed - {error}");
            }
        }

        [MenuItem("Tools/Kinetic Energy/Setup FastPaced Two-Scene Build Menus")]
        public static void SetupFastPacedBuildMenus()
        {
            (string label, string sceneName)[] buildScenes =
            {
                ("Tutorial 3", "Tutorial3"),
                ("Test Level 3", "TestLevel3"),
            };

            ConfigurePauseMenuForBuild(Tutorial3ScenePath, buildScenes, "TestLevel3");
            ConfigurePauseMenuForBuild(TestLevel3ScenePath, buildScenes, "MainMenu");

            BuildMainMenuScene(buildScenes, "Tutorial3",
                "Playtest build - this tests the fast-paced control scheme:\n" +
                "Tutorial 3 teaches it, Test Level 3 puts it to the test.\n" +
                "Each finish line takes you straight to the next stop.\n" +
                "Note: enabling 'Aim: Always Mouse' in the pause menu DISABLES\n" +
                "all controller inputs (outside the menus) until you turn it off.\n" +
                "Please share your thoughts via the Feedback button afterwards!");

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(Tutorial3ScenePath, true),
                new EditorBuildSettingsScene(TestLevel3ScenePath, true),
            };

            Debug.Log("KineticEnergySetup: fast-paced two-scene build menus setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Energy Regulation Build Menus")]
        public static void SetupEnergyRegulationBuildMenus()
        {
            const string energyFolder = "Assets/Scenes/EnergyRegulation/";

            (string label, string sceneName)[] buildScenes =
            {
                ("Circle Cranking", "Circle Cranking"),
                ("Dedicated Buttons", "Dedicated Buttons"),
                ("Reverse Direction", "Reverse Direction"),
                ("Automatic Energy", "Automatic Energy"),
            };

            for (int i = 0; i < buildScenes.Length; i++)
            {
                string scenePath = energyFolder + buildScenes[i].sceneName + ".unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                {
                    throw new Exception($"KineticEnergySetup: energy regulation scene missing - {scenePath}");
                }

                string next = i + 1 < buildScenes.Length ? buildScenes[i + 1].sceneName : "MainMenu";
                ConfigurePauseMenuForBuild(scenePath, buildScenes, next);
            }

            BuildMainMenuScene(buildScenes, buildScenes[0].sceneName,
                "Playtest build - this tests four ways to control your launch ENERGY:\n" +
                "Circle Cranking, Dedicated Buttons, Reverse Direction, and\n" +
                "Automatic Energy - one per level, played in that order.\n" +
                "Each finish line takes you straight to the next stop.\n" +
                "Note: enabling 'Aim: Always Mouse' in the pause menu DISABLES\n" +
                "all controller inputs (outside the menus) until you turn it off.\n" +
                "Please share your thoughts via the Feedback button afterwards!");

            List<EditorBuildSettingsScene> buildList = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
            };
            foreach ((string label, string sceneName) in buildScenes)
            {
                buildList.Add(new EditorBuildSettingsScene(energyFolder + sceneName + ".unity", true));
            }
            EditorBuildSettings.scenes = buildList.ToArray();

            Debug.Log("KineticEnergySetup: energy regulation build menus setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup FastPacedLevel Restart Countdown")]
        public static void SetupFastPacedRestartCountdown()
        {
            EditorSceneManager.OpenScene(FastPacedLevelScenePath, OpenSceneMode.Single);

            KineticCubeController controller = FindPlayerController(FastPacedLevelScenePath);

            OutOfEnergyRestart restart = controller.GetComponent<OutOfEnergyRestart>();
            if (restart == null) restart = controller.gameObject.AddComponent<OutOfEnergyRestart>();
            EditorUtility.SetDirty(restart);

            controller.disableAirNudge = true;
            controller.aimWithEitherStick = true;
            EditorUtility.SetDirty(controller);

            SaveActiveScene();
            Debug.Log("KineticEnergySetup: FastPacedLevel player setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup EnergyEconomy1")]
        public static void SetupEnergyEconomy1()
        {
            const string scenePath = "Assets/Scenes/EnergyEconomy/EnergyEconomy1.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                throw new Exception($"KineticEnergySetup: scene missing - {scenePath}");
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            KineticCubeController controller = FindPlayerController(scenePath);

            controller.lastLaunchRefundEconomy = true;
            controller.westAirDownLaunch = true;

            controller.mouseAirControls = true;
            EditorUtility.SetDirty(controller);

            ReplaceWinWithNextScene("MainMenu");

            GameObject pauseSystem = FindByNameIncludingInactive("PauseSystem");
            Transform meter = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas/EnergyMeter") : null;
            if (meter == null)
            {
                throw new Exception($"KineticEnergySetup: no PauseCanvas/EnergyMeter in {scenePath}.");
            }
            DestroyChildIfExists(meter, "MeterDividers");

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

            SaveActiveScene();
            Debug.Log("KineticEnergySetup: EnergyEconomy1 setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Create Target Prefab")]
        public static void CreateTargetPrefab()
        {
            Material targetMat = new Material(FindBestShader());
            targetMat.color = new Color(0.95f, 0.35f, 0.15f);
            targetMat = SaveMaterialAsset(targetMat, "TargetMaterial");

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                root.name = "Target";
                root.transform.localScale = Vector3.one * 2f;
                root.GetComponent<Renderer>().sharedMaterial = targetMat;

                root.GetComponent<SphereCollider>().isTrigger = false;
                root.AddComponent<LaunchTarget>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/Target.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: Target prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup EnergyEconomy3")]
        public static void SetupEnergyEconomy3()
        {
            const string scenePath = "Assets/Scenes/EnergyEconomy/EnergyEconomy3.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                throw new Exception($"KineticEnergySetup: scene missing - {scenePath}");
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            KineticCubeController controller = FindPlayerController(scenePath);
            controller.chainLaunchAccumulation = true;
            EditorUtility.SetDirty(controller);

            SaveActiveScene();
            Debug.Log("KineticEnergySetup: EnergyEconomy3 setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup EnergyEconomy4")]
        public static void SetupEnergyEconomy4()
        {
            const string scenePath = "Assets/Scenes/EnergyEconomy/EnergyEconomy4.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                throw new Exception($"KineticEnergySetup: scene missing - {scenePath}");
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            KineticCubeController controller = FindPlayerController(scenePath);
            controller.groundPoundBoostEconomy = true;
            EditorUtility.SetDirty(controller);

            EnergyMeterController meter = controller.energyMeter;
            if (meter == null || meter.energyFillImage == null)
            {
                throw new Exception("KineticEnergySetup: EnergyEconomy4 has no wired energy meter to add the bonus bar to");
            }
            if (meter.bonusFillImage == null)
            {
                Transform container = meter.energyFillImage.transform.parent;
                Transform existing = container.Find("BonusFill");
                Image bonus = existing != null
                    ? existing.GetComponent<Image>()
                    : CreateFillBar("BonusFill", container, new Color(1f, 0.55f, 0.1f), 3f);
                bonus.transform.SetSiblingIndex(meter.energyFillImage.transform.GetSiblingIndex());
                bonus.fillAmount = 0f;
                bonus.gameObject.SetActive(false);
                meter.bonusFillImage = bonus;
                EditorUtility.SetDirty(meter);
            }

            SaveActiveScene();
            Debug.Log("KineticEnergySetup: EnergyEconomy4 setup complete OK");
        }

        static void ConfigurePauseMenuForBuild(string scenePath, (string label, string sceneName)[] buildScenes, string finishNextScene)
        {
            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject pauseSystem = FindByNameIncludingInactive("PauseSystem");
            Transform pausePanel = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas/PausePanel") : null;
            Transform scenesPanel = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas/ScenesPanel") : null;
            PauseController pauseController = pauseSystem != null ? pauseSystem.transform.Find("PauseController")?.GetComponent<PauseController>() : null;
            if (pausePanel == null || scenesPanel == null || pauseController == null)
            {
                throw new Exception($"KineticEnergySetup: pause menu pieces missing in {scenePath}.");
            }

            DestroyChildIfExists(pausePanel, "MainMenuButton");
            GameObject mainMenuBtn = CreateButton("MainMenuButton", pausePanel, "Main Menu", font, accent, new Vector2(0f, -265f), new Vector2(300f, 70f));
            WireSceneButton(mainMenuBtn, pauseController.LoadSceneByName, "MainMenu");

            for (int i = 0; i < 10; i++)
            {
                Transform legacyButton = scenesPanel.Find("Scene_" + i + "Button");
                if (legacyButton != null && legacyButton.gameObject.activeSelf) legacyButton.gameObject.SetActive(false);
                DestroyChildIfExists(scenesPanel, "PlaytestScene_" + i + "Button");
            }
            float buttonY = 100f;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                GameObject sceneBtn = CreateButton("PlaytestScene_" + i + "Button", scenesPanel, buildScenes[i].label, font, accent, new Vector2(0f, buttonY), new Vector2(300f, 70f));
                WireSceneButton(sceneBtn, pauseController.LoadSceneByName, buildScenes[i].sceneName);
                buttonY -= 90f;
            }

            if (!string.IsNullOrEmpty(finishNextScene))
            {
                ReplaceWinWithNextScene(finishNextScene);
            }

            SaveActiveScene();
        }

        [MenuItem("Tools/Kinetic Energy/Setup Four-Scene Build Menus")]
        public static void SetupFourSceneBuildMenus()
        {
            (string label, string sceneName)[] buildScenes =
            {
                ("Tutorial", "Tutorial"),
                ("Test Level 1", "TestLevel1"),
                ("Tutorial 2", "Tutorial2"),
                ("Test Level 2", "TestLevel2"),
            };

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);

            foreach (string scenePath in new[] { TutorialScenePath, TestLevel1ScenePath, Tutorial2ScenePath, TestLevel2ScenePath })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                GameObject pauseSystem = FindByNameIncludingInactive("PauseSystem");
                Transform pausePanel = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas/PausePanel") : null;
                Transform scenesPanel = pauseSystem != null ? pauseSystem.transform.Find("PauseCanvas/ScenesPanel") : null;
                PauseController pauseController = pauseSystem != null ? pauseSystem.transform.Find("PauseController")?.GetComponent<PauseController>() : null;
                if (pausePanel == null || scenesPanel == null || pauseController == null)
                {
                    throw new Exception($"KineticEnergySetup: pause menu pieces missing in {scenePath}.");
                }

                DestroyChildIfExists(pausePanel, "MainMenuButton");
                GameObject mainMenuBtn = CreateButton("MainMenuButton", pausePanel, "Main Menu", font, accent, new Vector2(0f, -265f), new Vector2(300f, 70f));
                WireSceneButton(mainMenuBtn, pauseController.LoadSceneByName, "MainMenu");

                for (int i = 0; i < 10; i++)
                {
                    Transform legacyButton = scenesPanel.Find("Scene_" + i + "Button");
                    if (legacyButton != null && legacyButton.gameObject.activeSelf) legacyButton.gameObject.SetActive(false);
                }
                float buttonY = 100f;
                for (int i = 0; i < buildScenes.Length; i++)
                {
                    DestroyChildIfExists(scenesPanel, "PlaytestScene_" + i + "Button");
                    GameObject sceneBtn = CreateButton("PlaytestScene_" + i + "Button", scenesPanel, buildScenes[i].label, font, accent, new Vector2(0f, buttonY), new Vector2(300f, 70f));
                    WireSceneButton(sceneBtn, pauseController.LoadSceneByName, buildScenes[i].sceneName);
                    buttonY -= 90f;
                }

                if (scenePath == TestLevel2ScenePath)
                {
                    ReplaceWinWithNextScene("MainMenu");
                }

                SaveActiveScene();
            }

            SetupMainMenu();
            EnsurePlaytestBuildSettings();
            SetupGroundedAimOption();

            Debug.Log("KineticEnergySetup: four-scene build menus setup complete OK");
        }

        [MenuItem("Tools/Kinetic Energy/Setup Energy Regulation Scenes")]
        public static void SetupEnergyRegulationScenes()
        {
            CreatePositioningObjectPrefab();

            (string path, EnergyControlMode mode)[] energyScenes =
            {
                ("Assets/Scenes/EnergyRegulation/Automatic Energy.unity", EnergyControlMode.Automatic),
                ("Assets/Scenes/EnergyRegulation/Circle Cranking.unity", EnergyControlMode.CircleCrank),
                ("Assets/Scenes/EnergyRegulation/Dedicated Buttons.unity", EnergyControlMode.DedicatedButtons),
                ("Assets/Scenes/EnergyRegulation/Reverse Direction.unity", EnergyControlMode.ReverseDirection),
            };

            foreach ((string path, EnergyControlMode mode) in energyScenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    throw new Exception($"KineticEnergySetup: energy regulation scene missing - {path}");
                }
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                KineticCubeController controller = FindPlayerController(path);
                controller.energyControlMode = mode;
                EditorUtility.SetDirty(controller);

                if (mode == EnergyControlMode.CircleCrank && controller.GetComponent<EnergyCrankUI>() == null)
                {
                    EnergyCrankUI crankUI = controller.gameObject.AddComponent<EnergyCrankUI>();
                    EditorUtility.SetDirty(crankUI);
                }

                SaveActiveScene();
            }

            Debug.Log("KineticEnergySetup: energy regulation scenes setup complete OK");
        }

        public static void CreatePositioningObjectPrefab()
        {
            Material sphereMat = new Material(FindBestShader());
            Color blue = new Color(0.25f, 0.5f, 1f, 0.4f);
            sphereMat.color = blue;
            MakeTransparent(sphereMat, blue.a);
            sphereMat = SaveMaterialAsset(sphereMat, "PositioningObjectMaterial");

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                root.name = "PositioningObject";
                root.transform.localScale = Vector3.one * 1.5f;
                root.GetComponent<Renderer>().sharedMaterial = sphereMat;
                root.GetComponent<SphereCollider>().isTrigger = true;
                root.AddComponent<PositioningTarget>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/PositioningObject.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        static KineticCubeController FindPlayerController(string sceneLabel)
        {
            GameObject playerGo = FindByNameIncludingInactive("Player");
            KineticCubeController controller = playerGo != null ? playerGo.GetComponent<KineticCubeController>() : null;
            if (controller == null)
            {
                throw new Exception($"KineticEnergySetup: no Player with KineticCubeController in {sceneLabel}.");
            }
            return controller;
        }

        static void ReplaceWinWithNextScene(string nextSceneName)
        {
            foreach (FinishLineWin win in UnityEngine.Object.FindObjectsByType<FinishLineWin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject go = win.gameObject;
                UnityEngine.Object.DestroyImmediate(win);
                go.AddComponent<FinishLineNextScene>().nextSceneName = nextSceneName;
                EditorUtility.SetDirty(go);
            }
            foreach (FinishLineNextScene next in UnityEngine.Object.FindObjectsByType<FinishLineNextScene>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                next.nextSceneName = nextSceneName;
                EditorUtility.SetDirty(next);
            }
        }

        static void SaveActiveScene()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void BuildTestLevel(string sourceScenePath, string destScenePath, string nextSceneName)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(destScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(sourceScenePath, destScenePath))
                {
                    throw new Exception($"KineticEnergySetup: failed to copy {sourceScenePath} to {destScenePath}.");
                }
            }

            EditorSceneManager.OpenScene(destScenePath, OpenSceneMode.Single);
            DestroyIfExists("TutorialCourse");
            DestroyIfExists("WallHopCourse");
            BuildWallHopCourse(nextSceneName);

            KineticCubeController testController = FindPlayerController(destScenePath);
            testController.landingPreview.ghostAndCrosshairEnabled = true;
            testController.landingPreview.initialMode = PredictionMode.TrailAndCrosshair;
            EditorUtility.SetDirty(testController.landingPreview);
            SaveActiveScene();
            AddSceneToBuildSettings(destScenePath);
        }

        static void BuildWallHopCourse(string nextSceneName)
        {
            GameObject playerGo = FindByNameIncludingInactive("Player");
            Transform player = playerGo != null ? playerGo.transform : null;
            ThirdPersonOrbitCamera orbitCam = UnityEngine.Object.FindFirstObjectByType<ThirdPersonOrbitCamera>(FindObjectsInactive.Include);

            Material platformMat = MakeSlowPacedMaterial("TutorialPlatformMaterial", new Color(0.50f, 0.55f, 0.62f));
            Material wallMat = MakeSlowPacedMaterial("TestLevelWallMaterial", new Color(0.45f, 0.55f, 0.75f));

            GameObject course = new GameObject("WallHopCourse");

            CreateTutorialSlab(course.transform, "StartPlatform", new Vector3(0f, -0.75f, 0f), new Vector3(6f, 1.5f, 6f), platformMat);

            GameObject wall1 = CreateTutorialSlab(course.transform, "FloatWall1", new Vector3(-5f, 5f, 16f), new Vector3(5f, 8f, 1f), wallMat);
            CreateTutorialSlab(course.transform, "FloatWall2", new Vector3(5f, 9f, 32f), new Vector3(5f, 8f, 1f), wallMat);
            CreateTutorialSlab(course.transform, "FloatWall3", new Vector3(-5f, 13f, 48f), new Vector3(5f, 8f, 1f), wallMat);

            Vector3 endCenter = new Vector3(0f, 13.25f, 62f);
            CreateTutorialSlab(course.transform, "EndPlatform", endCenter, new Vector3(8f, 1.5f, 8f), platformMat);

            GameObject trigger = new GameObject("FinishTrigger");
            trigger.transform.SetParent(course.transform, true);
            trigger.transform.position = endCenter + new Vector3(0f, 1.75f, 0f);
            BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(8f, 2f, 8f);
            trigger.AddComponent<FinishLineNextScene>().nextSceneName = nextSceneName;

            if (player != null)
            {
                player.position = new Vector3(0f, 0.5f, 0f);
                BuildCameraStartFacing(player, orbitCam, wall1.transform);
                BuildPlayerShadow(player);
            }
        }

        public static void SetupMainMenu()
        {

            BuildMainMenuScene(
                new (string label, string sceneName)[]
                {
                    ("Tutorial", "Tutorial"),
                    ("Test Level 1", "TestLevel1"),
                    ("Tutorial 2", "Tutorial2"),
                    ("Test Level 2", "TestLevel2"),
                },
                "Tutorial",
                "Playtest build - this tests two control schemes:\n" +
                "Tutorial + Test Level 1 use the first scheme,\n" +
                "Tutorial 2 + Test Level 2 the second.\n" +
                "Each finish line takes you straight to the next stop.\n" +
                "Note: enabling 'Aim: Always Mouse' in the pause menu DISABLES\n" +
                "all controller inputs (outside the menus) until you turn it off.\n" +
                "Please share your thoughts via the Feedback button afterwards!");
        }

        static void BuildMainMenuScene((string label, string sceneName)[] menuScenes, string startSceneName, string introText)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                Scene created = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(created, MainMenuScenePath);
            }

            EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

            string preservedFeedbackUrl = "";
            MainMenuController existingController = UnityEngine.Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (existingController != null) preservedFeedbackUrl = existingController.feedbackUrl;

            DestroyIfExists("MainMenuUI");
            DestroyIfExists("EventSystem");

            GameObject root = new GameObject("MainMenuUI");

            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.transform.SetParent(root.transform, false);
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            GameObject canvasGo = new GameObject("MenuCanvas");
            canvasGo.transform.SetParent(root.transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            Font font = FindBestFont();
            Color accent = new Color(1f, 0.82f, 0.2f);
            Color backdrop = new Color(0.06f, 0.07f, 0.1f, 1f);

            GameObject menuPanel = CreatePanel("MenuPanel", canvasGo.transform, backdrop);
            CreateText("Title", menuPanel.transform, "KINETIC ENERGY", font, 56, new Vector2(0f, 330f), new Vector2(900f, 90f));
            Text intro = CreateText("Intro", menuPanel.transform, introText,
                font, 26, new Vector2(0f, 180f), new Vector2(1000f, 200f));
            intro.color = new Color(1f, 1f, 1f, 0.9f);

            GameObject startBtn = CreateButton("StartButton", menuPanel.transform, "Start", font, accent, new Vector2(0f, 40f), new Vector2(300f, 70f));
            GameObject feedbackBtn = CreateButton("FeedbackButton", menuPanel.transform, "Feedback", font, accent, new Vector2(0f, -50f), new Vector2(300f, 70f));
            GameObject scenesBtn = CreateButton("ScenesButton", menuPanel.transform, "Scenes", font, accent, new Vector2(0f, -140f), new Vector2(300f, 70f));
            GameObject quitBtn = CreateButton("QuitButton", menuPanel.transform, "Quit", font, accent, new Vector2(0f, -230f), new Vector2(300f, 70f));

            GameObject scenesPanel = CreatePanel("ScenesPanel", canvasGo.transform, backdrop);
            CreateText("ScenesTitle", scenesPanel.transform, "SCENES", font, 48, new Vector2(0f, 240f), new Vector2(600f, 80f));
            GameObject[] sceneButtons = new GameObject[menuScenes.Length];
            float buttonY = 130f;
            for (int i = 0; i < menuScenes.Length; i++)
            {
                sceneButtons[i] = CreateButton("Scene_" + i + "Button", scenesPanel.transform, menuScenes[i].label, font, accent, new Vector2(0f, buttonY), new Vector2(300f, 70f));
                buttonY -= 90f;
            }
            GameObject scenesBackBtn = CreateButton("ScenesBackButton", scenesPanel.transform, "Back", font, accent, new Vector2(0f, buttonY - 20f), new Vector2(300f, 70f));

            scenesPanel.SetActive(false);

            MainMenuController controller = root.AddComponent<MainMenuController>();
            controller.menuPanel = menuPanel;
            controller.scenesPanel = scenesPanel;
            controller.startSceneName = startSceneName;
            controller.feedbackUrl = preservedFeedbackUrl;

            controller.firstMenuButton = startBtn;
            controller.firstScenesButton = sceneButtons.Length > 0 ? sceneButtons[0] : scenesBackBtn;

            WireButton(startBtn, controller.OnStartClicked);
            WireButton(feedbackBtn, controller.OnFeedbackClicked);
            WireButton(scenesBtn, controller.OnScenesClicked);
            WireButton(quitBtn, controller.OnQuitClicked);
            WireButton(scenesBackBtn, controller.OnScenesBackClicked);
            for (int i = 0; i < menuScenes.Length; i++)
            {
                WireSceneButton(sceneButtons[i], controller.LoadSceneByName, menuScenes[i].sceneName);
            }

            SaveActiveScene();
            AssetDatabase.SaveAssets();
        }

        static void EnsurePlaytestBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(TutorialScenePath, true),
                new EditorBuildSettingsScene(TestLevel1ScenePath, true),
                new EditorBuildSettingsScene(Tutorial2ScenePath, true),
                new EditorBuildSettingsScene(TestLevel2ScenePath, true),
            };
        }

        [MenuItem("Tools/Kinetic Energy/Create Breakable Crack Wall Prefab")]
        public static void CreateBreakableCrackWallPrefab()
        {
            Material paneMat = MakeSlowPacedMaterial("BreakableWallMaterial", new Color(0.88f, 0.82f, 0.70f));

            Material crackMat = EnsureCrackDecalAssets();
            Material bigCrackMat = new Material(crackMat);
            bigCrackMat.SetTextureScale("_BaseMap", new Vector2(1f / 3f, 1f / 3f));
            bigCrackMat.SetTextureOffset("_BaseMap", new Vector2(1f / 3f, 1f / 3f));
            bigCrackMat = SaveMaterialAsset(bigCrackMat, "BreakableCrackFaceMaterial");

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            GameObject root = new GameObject("BreakableCrackWall");
            try
            {

                CreateTutorialSlab(root.transform, "Pane", new Vector3(0f, 0.15f, 0f), new Vector3(4f, 0.3f, 4f), paneMat);
                root.AddComponent<BreakableCrackWall>();

                GameObject crackGo = new GameObject("CrackFace");
                crackGo.transform.SetParent(root.transform, false);
                crackGo.transform.localPosition = new Vector3(0f, 0.315f, 0f);
                crackGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                crackGo.transform.localScale = new Vector3(3.4f, 3.4f, 1f);
                MeshFilter crackFilter = crackGo.AddComponent<MeshFilter>();
                crackFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                MeshRenderer crackRenderer = crackGo.AddComponent<MeshRenderer>();
                crackRenderer.sharedMaterial = bigCrackMat;
                crackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/BreakableCrackWall.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: BreakableCrackWall prefab created OK");
        }

        [MenuItem("Tools/Kinetic Energy/Refresh Shadows In All Scenes")]
        public static void RefreshShadows()
        {

            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PrefabFolder + "/Player.prefab");
            foreach (Renderer childRenderer in playerRoot.GetComponentsInChildren<Renderer>(true))
            {
                childRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            PrefabUtility.SaveAsPrefabAsset(playerRoot, PrefabFolder + "/Player.prefab");
            PrefabUtility.UnloadPrefabContents(playerRoot);

            foreach (string rpPath in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var rpAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(rpPath);
                if (rpAsset != null)
                {
                    rpAsset.shadowDistance = 150f;
                    EditorUtility.SetDirty(rpAsset);
                }
            }

            string[] scenePaths =
            {
                ScenePath, Level1ScenePath, Level2ScenePath, Level3ScenePath,
                FastPacedLevelScenePath, SlowPacedLevelScenePath, TutorialScenePath,
            };

            foreach (string scenePath in scenePaths)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                GameObject lightGo = FindByNameIncludingInactive("Directional Light");
                Light light = lightGo != null ? lightGo.GetComponent<Light>() : null;
                if (light != null)
                {
                    light.shadows = LightShadows.Soft;
                    EditorUtility.SetDirty(light);
                }

                GameObject shadowGo = FindByNameIncludingInactive("PlayerShadow");
                if (shadowGo != null)
                {
                    foreach (Renderer discRenderer in shadowGo.GetComponentsInChildren<Renderer>(true))
                    {
                        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        EditorUtility.SetDirty(discRenderer);
                    }
                }

                if (scenePath == TutorialScenePath)
                {
                    RetitleTutorialSigns();
                }

                Scene scene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: RefreshShadows complete OK");
        }

        static void RetitleTutorialSigns()
        {
            foreach (TextMesh textMesh in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (textMesh.gameObject.name != "TutorialSign") continue;
                if (textMesh.text.StartsWith("2."))
                {
                    textMesh.text = "2. Launch forward, then hold Left Trigger in the air\nto aim, hold West and release to slam down";
                    EditorUtility.SetDirty(textMesh);
                }
                else if (textMesh.text.StartsWith("4."))
                {
                    textMesh.text = "4. Launch forward, then hold Left Trigger and Right\nTrigger in the air - release at about half charge";
                    EditorUtility.SetDirty(textMesh);
                }
            }
        }

        [MenuItem("Tools/Kinetic Energy/Rebuild Crack Decal Texture")]
        public static void RebuildCrackDecalTexture()
        {
            EnsureCrackDecalAssets();
            Debug.Log("KineticEnergySetup: crack decal texture rebuilt OK");
        }

        static Material EnsureCrackDecalAssets()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Textures"))
            {
                AssetDatabase.CreateFolder("Assets", "Textures");
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(CrackSourcePath) == null)
            {
                GenerateProceduralCrackSheet();
            }
            ProcessCrackSheet();

            Texture2D processed = AssetDatabase.LoadAssetAtPath<Texture2D>(CrackProcessedPath);
            Material mat = new Material(FindBestShader());
            mat.color = Color.white;
            MakeTransparent(mat, 1f);

            mat.SetFloat("_Cull", 0f);
            mat.SetTexture("_BaseMap", processed);
            return SaveMaterialAsset(mat, "CrackDecalMaterial");
        }

        static void GenerateProceduralCrackSheet()
        {
            const int cellSize = 300;
            const int sheetSize = cellSize * 3;
            Color32[] pixels = new Color32[sheetSize * sheetSize];

            System.Random rng = new System.Random(20260806);
            for (int cellY = 0; cellY < 3; cellY++)
            {
                for (int cellX = 0; cellX < 3; cellX++)
                {
                    DrawCrackCell(pixels, sheetSize, cellX * cellSize, cellY * cellSize, cellSize, rng);
                }
            }

            Texture2D sheet = new Texture2D(sheetSize, sheetSize, TextureFormat.RGBA32, false);
            sheet.SetPixels32(pixels);
            sheet.Apply();
            File.WriteAllBytes(CrackSourcePath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            AssetDatabase.ImportAsset(CrackSourcePath);
        }

        static void DrawCrackCell(Color32[] pixels, int sheetSize, int originX, int originY, int cellSize, System.Random rng)
        {
            float centerX = originX + cellSize * (0.5f + RandomSpread(rng, 0.08f));
            float centerY = originY + cellSize * (0.5f + RandomSpread(rng, 0.08f));

            int branchCount = 4 + rng.Next(3);
            float baseAngle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            for (int branch = 0; branch < branchCount; branch++)
            {
                float angle = baseAngle + branch * (Mathf.PI * 2f / branchCount) + RandomSpread(rng, 0.5f);
                float width = cellSize * (0.055f + (float)rng.NextDouble() * 0.03f);
                DrawCrackBranch(pixels, sheetSize, originX, originY, cellSize, centerX, centerY, angle, width, 3 + rng.Next(2), rng, true);
            }
        }

        static void DrawCrackBranch(Color32[] pixels, int sheetSize, int originX, int originY, int cellSize,
            float x, float y, float angle, float width, int segments, System.Random rng, bool allowSubBranches)
        {
            Color32 fill = new Color32(86, 90, 95, 255);
            Color32 core = new Color32(58, 61, 66, 255);

            for (int segment = 0; segment < segments; segment++)
            {
                float length = cellSize * (0.09f + (float)rng.NextDouble() * 0.08f);
                float endX = x + Mathf.Cos(angle) * length;
                float endY = y + Mathf.Sin(angle) * length;
                float endWidth = Mathf.Max(width * 0.62f, 2f);

                StampCrackLine(pixels, sheetSize, originX, originY, cellSize, x, y, endX, endY, width, endWidth, fill);
                StampCrackLine(pixels, sheetSize, originX, originY, cellSize, x, y, endX, endY, width * 0.45f, endWidth * 0.45f, core);

                if (allowSubBranches && segment > 0 && rng.NextDouble() < 0.55)
                {
                    float subAngle = angle + (rng.Next(2) == 0 ? 1f : -1f) * (0.6f + (float)rng.NextDouble() * 0.5f);
                    DrawCrackBranch(pixels, sheetSize, originX, originY, cellSize, x, y, subAngle, width * 0.55f, 2, rng, false);
                }

                x = endX;
                y = endY;
                angle += RandomSpread(rng, 0.7f);
                width = endWidth;
            }
        }

        static void StampCrackLine(Color32[] pixels, int sheetSize, int originX, int originY, int cellSize,
            float x0, float y0, float x1, float y1, float startWidth, float endWidth, Color32 color)
        {
            const int margin = 6;
            float length = Mathf.Max(Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)), 1f);
            int steps = Mathf.CeilToInt(length);
            for (int step = 0; step <= steps; step++)
            {
                float t = step / (float)steps;
                float px = Mathf.Lerp(x0, x1, t);
                float py = Mathf.Lerp(y0, y1, t);
                float radius = Mathf.Lerp(startWidth, endWidth, t) * 0.5f;
                int r = Mathf.Max(Mathf.CeilToInt(radius), 1);
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > radius * radius) continue;
                        int fx = Mathf.RoundToInt(px) + dx;
                        int fy = Mathf.RoundToInt(py) + dy;
                        if (fx < originX + margin || fx >= originX + cellSize - margin) continue;
                        if (fy < originY + margin || fy >= originY + cellSize - margin) continue;
                        pixels[fy * sheetSize + fx] = color;
                    }
                }
            }
        }

        static float RandomSpread(System.Random rng, float range)
        {
            return ((float)rng.NextDouble() * 2f - 1f) * range;
        }

        static void ProcessCrackSheet()
        {
            byte[] bytes = File.ReadAllBytes(Path.GetFullPath(CrackSourcePath));
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(bytes))
            {
                throw new Exception("KineticEnergySetup: could not decode " + CrackSourcePath);
            }

            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                float luminance = (pixel.r + pixel.g + pixel.b) / (3f * 255f);
                float key = Mathf.Clamp01((0.75f - luminance) / 0.15f);
                pixels[i].a = (byte)Mathf.RoundToInt(pixel.a * key);
            }

            Texture2D processed = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            processed.SetPixels32(pixels);
            processed.Apply();
            File.WriteAllBytes(CrackProcessedPath, processed.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(processed);

            AssetDatabase.ImportAsset(CrackProcessedPath);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(CrackProcessedPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;

            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
