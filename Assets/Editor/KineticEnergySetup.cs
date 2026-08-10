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
        // The crack decal pipeline: SOURCE is the raw 3x3 sheet (procedurally generated if
        // missing; overwrite it with real art any time), PROCESSED is what the decal material
        // actually uses - ProcessCrackSheet keys the source's light background out into alpha.
        const string CrackSourcePath = "Assets/Textures/CrackDecalSheetSource.png";
        const string CrackProcessedPath = "Assets/Textures/CrackDecalSheet.png";
        const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string PrefabFolder = "Assets/Prefabs";

        // Label, then the actual scene name LoadSceneByName gets called with (must match a scene
        // in EditorBuildSettings.scenes - see UpdateBuildSettings). Add an entry here for any
        // future scene and BuildPauseSystem picks it up automatically, no other changes needed.
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

            // Sandbox Scene's own Directional Light predates BuildDirectionalLight (it came from
            // the original URP template scene this was renamed from) - unlike Level1/Level2,
            // nothing ever called this here before, so its shadow setting would otherwise stay
            // whatever the template shipped with regardless of this method's own logic.
            BuildDirectionalLight();

            KineticCubeController controller = BuildPlayerCube(player, moveRef, launchRef, fireRef, selectGhostRef, selectTrailRef, selectCrosshairRef, selectNoneRef, switchSchemeRef, upLaunchRef, cancelChargeRef,
                out KineticCubeControllerFreeMove freeMoveController);
            ThirdPersonOrbitCamera orbitCam = BuildCameraRig(mainCamGo, lookRef);

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            // Save each hierarchy as its own self-contained prefab BEFORE cross-wiring them -
            // a prefab asset cannot embed a reference to an object in a different hierarchy,
            // so target/cameraTransform have to be assigned after, as per-instance scene overrides.
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

            // PauseSystem is its own prefab, saved inside BuildPauseSystem - same cross-hierarchy
            // rule applies, so this wiring happens on the scene instances, after both are saved.
            controller.landingPreview.modeLabel = previewModeLabel;
            controller.controlsHintLabel = controlsHint;
            controller.controlsPanelBody = controlsBody;
            controller.energyMeter = energyMeter;
            radialMenu.controller = controller;
            // Re-marked dirty here - the earlier SetDirty(controller) above ran BEFORE these two
            // fields were assigned, which silently left them out of the saved scene's prefab
            // instance overrides entirely (confirmed by comparing against SetupLevel1, which
            // marks its own controller dirty AFTER the equivalent assignments and saves fine).
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
                // Copy Sandbox Scene as the starting point instead of NewScene(EmptyScene) - a
                // scene created empty starts with NONE of Unity's environment setup (no skybox,
                // flat default ambient), which is what "feels like different lighting"/"URP
                // assets are off" actually was. Copying guarantees identical RenderSettings,
                // LightmapSettings, etc. by construction, rather than trying to replicate every
                // relevant field by hand and risking missing one.
                if (!AssetDatabase.CopyAsset(ScenePath, Level1ScenePath))
                {
                    throw new Exception("KineticEnergySetup: failed to copy Sandbox Scene to create Level1.");
                }
            }

            EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);

            BuildDirectionalLight(); // no-ops if the copy already brought one in, which it will have
            BuildGlobalVolume();

            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Player.prefab");
            GameObject cameraAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/ThirdPersonCameraRig.prefab");
            GameObject pauseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/PauseSystem.prefab");
            if (playerAsset == null || cameraAsset == null || pauseAsset == null)
            {
                throw new Exception("KineticEnergySetup: Level1 needs Player/ThirdPersonCameraRig/PauseSystem prefabs - run Setup() (part of SetupAll) first.");
            }

            // Rebuilt fresh every run rather than patched in place, same as everything else in this
            // file. Plane is Sandbox Scene's flat floor, copied in along with everything else -
            // Level1 is platforms only, no floor.
            // Both camera names are destroyed deliberately: SaveAsPrefabAssetAndConnect
            // apparently named the saved PREFAB ASSET's root after the asset file
            // ("ThirdPersonCameraRig"), not the original scene object ("Main Camera") it was
            // saved from - Sandbox Scene's own instance kept the "Main Camera" override so
            // Setup() never noticed, but every instance freshly instantiated FROM the asset
            // (every one ever added here) comes out named "ThirdPersonCameraRig". Searching for
            // only "Main Camera" matched nothing, every single run, so old instances were never
            // actually destroyed - only ever added to. This is what actually produced the "4
            // cameras" (really "1 correctly-named ghost + N never-found ThirdPersonCameraRigs").
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

            // Same cross-hierarchy wiring as Setup() does for Sandbox Scene, but these are plain
            // prefab instances (not being re-saved as prefab assets here), so it can just be
            // assigned directly - the "save both assets first" rule only applies when the
            // instance itself is about to be captured back into a .prefab file.
            controller.cameraTransform = camGo.transform;
            controller.cameraOrbit = orbitCam;
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            // Level1's Player is a plain instance of the SAME Player.prefab BuildPlayerCube just
            // saved, not rebuilt from scratch here - which meant launch tuning silently relied on
            // instance inheritance ever actually working exactly as expected. ApplyLaunchTuning
            // makes it explicit instead (matching the anti-staleness pattern every other tunable
            // in this file already uses), so this scene can never end up quietly out of sync with
            // Sandbox Scene's numbers again, no matter how prefab-instance inheritance behaves.
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
            generator.finishTextColor = new Color(0.15f, 0.45f, 1f); // vivid blue - contrast via color, not a backing plate
            generator.finishFontSize = 48;
            generator.finishCharacterSize = 0.2f;
            generator.safetyFloorMargin = 8f;
            // Widened alongside the larger horizontal-distance range above - worst-case cumulative
            // drift over platformCount-1 steps scales with maxHorizontalDistance, and needs to
            // stay comfortably inside the floor's half-extent (safetyFloorSize / 2) or a shot far
            // out toward the edge of a heavily-drifted layout could miss the safety net entirely.
            generator.safetyFloorSize = 260f;

            BuildPlayerShadow(playerGo.transform);

            Scene level1Scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(level1Scene);
            EditorSceneManager.SaveScene(level1Scene);

            Debug.Log("KineticEnergySetup: Level1 setup complete OK");
        }

        // Level2 is hand-placed, static level geometry, not a runtime spawner like Level1's
        // LevelGenerator - direct request: "do not make them random and place them via the
        // editor". Segments get added to BuildLevel2Segments below one at a time as future
        // requests ask for them; for now it's just the opening hallway.
        static void SetupLevel2()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Level2ScenePath) == null)
            {
                // Same reasoning as SetupLevel1 - copying Sandbox Scene guarantees identical
                // RenderSettings/skybox/ambient instead of trying to replicate them by hand.
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

            // Same duplicate-camera-name trap as SetupLevel1 - both names destroyed deliberately,
            // see that method's own comment for why.
            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");
            // Level2.unity was created by copying Sandbox Scene (see the CopyAsset call above),
            // which by that point already had its own 5 circular jump platforms built (Setup()
            // runs before SetupLevel2() in SetupAll) - direct request: those don't belong here,
            // Level2 is meant to be just the hand-placed segments below.
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

            // Same single-source-of-truth reasoning as SetupLevel1 - this is a plain instance of
            // Player.prefab, not rebuilt from scratch here.
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

        // Every segment lines up back-to-back along +Z, in the order added here - "a couple of
        // level segments spawn in a straight line all after each other and connected". Only the
        // opening hallway exists so far; future segments get appended to this same method
        // (advancing z onward from wherever the previous one ended) rather than each becoming
        // its own standalone entry point. Returns the last segment's own "next platform" (right
        // now just the hallway's end platform) so the caller can point the camera at it on
        // bootup.
        static GameObject BuildLevel2Segments(Transform player)
        {
            GameObject container = GameObject.Find("Level2Segments");
            if (container != null) UnityEngine.Object.DestroyImmediate(container);
            container = new GameObject("Level2Segments");

            return BuildLevel2OpeningHallway(container.transform, player);
        }

        // The opening segment: a long straight hallway. Two platforms - start and end - float in
        // an otherwise empty void, flanked the whole way by tall walls and capped with a
        // partially-transparent "glass" ceiling that still lets light through (direct request).
        // Fully hand-placed/static (built once here, not regenerated at runtime), unlike Level1's
        // random LevelGenerator. Returns the end platform.
        static GameObject BuildLevel2OpeningHallway(Transform parent, Transform player)
        {
            GameObject hallway = new GameObject("OpeningHallway");
            hallway.transform.SetParent(parent, true);

            // Twice Level1's platform footprint (3, 0.5, 3) - direct request.
            Vector3 platformSize = new Vector3(6f, 0.5f, 6f);
            const float hallwayLength = 32f; // start-platform-center to end-platform-center, along +Z
            const float corridorHalfWidth = 5f; // interior clearance either side of center - comfortably wider than the 6-wide platforms
            const float wallThickness = 1f;
            const float wallHeight = 14f; // "high up walls"
            const float ceilingThickness = 0.3f;
            // How far past each platform's own outer edge the walls/ceiling extend, so the
            // hallway visually encloses both platforms too, not just the void between them.
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
            // Keeps its BoxCollider (unlike the purely-decorative ghost/shadow visuals elsewhere
            // in this file) - "looks like glass so the player knows they can't go through it"
            // means it has to actually BE solid, not just look that way. Shadow casting is what's
            // turned off, not collision - a real pane of glass still blocks a thrown ball, it
            // just doesn't stop the sun from lighting the room behind it.
            ceiling.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Same formula LevelGenerator uses to stand the player on a platform's surface:
            // platform center + half its thickness + the player cube's own half-height.
            player.position = startCenter + new Vector3(0f, platformSize.y * 0.5f + 0.5f, 0f);

            return endPlatform;
        }

        // Level3 is hand-placed, static level geometry, same as Level2 - direct request: "design
        // a new level yourself that would fit the launching mechanic and kinetic energy best...
        // it should have 3 distinct segments". Each segment is deliberately built around a
        // different part of the shared mechanic set instead of just repeating Level2's flat
        // hallway shape:
        //   1. Launch Basics - a straight line of flat platforms with steadily widening gaps,
        //      teaching charge-and-release distance judgment and the crash-refunds-energy loop
        //      before anything harder shows up.
        //   2. Varied Path - primarily a forward run (Z is the main axis, Y only ever drifts in a
        //      modest band) that alternates which SURFACE you stick to: normal floor platforms,
        //      a ceiling hit from below, and side-wall stubs hit with a sideways-aimed shot - same
        //      any-surface-sticks mechanic throughout, just choreographed into a rhythm.
        //   3. The Gauntlet - a fast, flat endurance run suited to Defy Gravity's Forward burst,
        //      with one deliberate wall checkpoint partway through: a wide, unmissable target
        //      worth crashing into on purpose to refuel before the final stretch, since the legs
        //      either side of it are sized to need a real charge, not a token tap.
        // Every distance below came from a real Play-mode diagnostic (TrajectoryProbeRunner, since
        // deleted) that fired actual launches at known charge levels and measured where they
        // actually landed - reasoning about force/damping/gravity numbers alone already produced
        // one wrong guess this session (see maxDefyGravitySpeed's own comment), so this level's
        // gaps are sized against measured reality, not the tuning constants directly:
        //   Old-scheme/StickAim-Forward @ 30 degrees: 12m (min charge) to 87m (max charge)
        //   StickAim Up @ tilted 80 degrees: 3m/9m (min) to 19m/61m (max) horizontal/height
        //   StickAim Down @ 60 degrees, fired from standing: 0m either way - the impulse gets
        //     absorbed by the ground it's already resting on, so it's only useful started
        //     airborne, aimed at something below - deliberately not used for traversal here
        //   Defy Gravity Forward (flat): 26m (0.4s charge) to 64m (~1s charge)
        //   Defy Gravity Up: 46m (0.4s charge) to 61m+ (0.55s charge)
        // Every gap below stays well inside its relevant range (never near the max-charge figure)
        // so no jump ever demands a pixel-perfect maximum hold.
        static void SetupLevel3()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Level3ScenePath) == null)
            {
                // Same reasoning as SetupLevel1/SetupLevel2 - copying Sandbox Scene guarantees
                // identical RenderSettings/skybox/ambient instead of trying to replicate them by hand.
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

            // Same duplicate-camera-name trap as SetupLevel1/SetupLevel2 - both names destroyed
            // deliberately, see SetupLevel1's own comment for why.
            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");
            // Level3.unity was created by copying Sandbox Scene, which already has its own 5
            // circular jump platforms - same reasoning as SetupLevel2, those don't belong here.
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

            // Same single-source-of-truth reasoning as SetupLevel1/SetupLevel2 - this is a plain
            // instance of Player.prefab, not rebuilt from scratch here.
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

        // Chains all three segments back to back, each one starting exactly where the previous one
        // ended - same "spawn in a straight line, connected" idea SetupLevel2 established, just
        // with height varying between segments (flat / climbing / flat again) instead of staying
        // level throughout. Returns the very last platform (the finish) so the caller can point
        // the camera at it.
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

        // Segment 1: "Launch Basics" - a straight line of flat platforms with steadily widening
        // gaps (14m/18m/26m/40m/57m - all well inside the ~12-87m range a 30-degree charged shot
        // actually covers, see SetupLevel3's own comment for where these numbers came from), each
        // one a genuine crash on a comfortable-to-firm charge for any grounded scheme. Every
        // landing refunds energy the same way any crash does, so by the time the gaps get serious
        // the player has already felt that loop a few times. Every platform past the start has a
        // low back wall on its far (+Z) edge - direct request: "walls on the far side to help the
        // player land on them more easily to get used to them" - a slightly-overshot landing
        // clips the wall and sticks (a genuine crash, refunding energy same as anything else)
        // instead of sailing past into open air. A little X jitter on the middle platforms is
        // small horizontal variation, not a hazard - the gaps are still overwhelmingly a Z-forward
        // judgment call. Returns the last platform.
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

        // Segment 2: "Varied Path" - reworked from an earlier straight-up shaft per direct
        // feedback: "shouldn't be as vertical, it's allowed to have some vertical difference, but
        // the main direction the player should be moving should be forward rather than vertical".
        // Z advances 200m end to end (the primary axis) while Y only ever drifts within a ~15m
        // band - a real height change, but nowhere close to a dedicated climb. The variety instead
        // comes from alternating WHICH surface you stick to, per direct request: "alternatingly
        // stick to the ceiling of a platform, and left/right turned platforms and a regular
        // platform". None of these need special-case code - a "ceiling" is just an ordinary
        // platform positioned above the approach path, hit from underneath (its bottom face has a
        // downward normal, so flatGroundStickThreshold correctly keeps it a real stick, not a
        // walk-away); a "turned" platform is a thin vertical slab exactly like the walls
        // elsewhere in this file, just placed beside the path instead of flanking it, hit with a
        // sideways-aimed shot (the stick already steers aim off the pure-forward axis on every
        // charge-based scheme, so no new input is needed) - same mechanic as any wall-stick, just
        // choreographed into a rhythm. Reached with StickAim's Up-charge tilted toward each
        // target (the same 3-19m horizontal / 9-61m vertical range already used for reaching an
        // offset target at height) or Defy Gravity, whichever the player prefers. Returns the
        // last platform.
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

            // Same accent color for BOTH special stick types (ceiling and side-wall) - a
            // deliberate, learnable visual grammar: orange means "you'll stick to this from an
            // unusual angle", blue-grey means "ordinary floor".
            Material specialMat = new Material(FindBestShader());
            specialMat.color = new Color(0.95f, 0.55f, 0.15f);
            specialMat = SaveMaterialAsset(specialMat, "Level3LedgeMaterial");

            Vector3 platformSize = new Vector3(5f, 0.5f, 5f);
            Vector3 ceilingSize = new Vector3(6f, 0.5f, 6f);
            Vector3 sideWallSize = new Vector3(1f, 5f, 5f);

            CreateBlock(segment.transform, "Normal1", new Vector3(x, y + 3f, z + 35f), platformSize, pathMat);
            // Ceiling - positioned above the path between Normal1 and Normal2, hit from below on
            // the way up from Normal1. Center height chosen so an up-tilted charge from Normal1
            // (roughly a 35-45% hold) clips its underside rather than needing a maximum charge.
            CreateBlock(segment.transform, "Ceiling1", new Vector3(x, y + 13f, z + 60f), ceilingSize, pathMat);
            CreateBlock(segment.transform, "Normal2", new Vector3(x, y + 1f, z + 90f), platformSize, pathMat);
            // Side-wall (left) - thin slab beside the path, same construction as any other wall in
            // this file, just standalone rather than flanking a corridor. Its X-facing sides are
            // what the player actually sticks to, reached with a sideways-tilted charge.
            CreateBlock(segment.transform, "SideWallLeft", new Vector3(x - 9f, y + 4f, z + 115f), sideWallSize, specialMat);
            CreateBlock(segment.transform, "Normal3", new Vector3(x, y + 2f, z + 145f), platformSize, pathMat);
            CreateBlock(segment.transform, "SideWallRight", new Vector3(x + 9f, y + 5f, z + 170f), sideWallSize, specialMat);
            // Centered (no X offset) - opens straight into segment 3's corridor along Z.
            GameObject last = CreateBlock(segment.transform, "Normal4", new Vector3(x, y + 3f, z + 200f), platformSize, pathMat);

            return last;
        }

        // Segment 3: "The Gauntlet" - a fast, flat endurance run suited to Defy Gravity's Forward
        // burst (measured 26-64m per charge), with one deliberate wall checkpoint partway through:
        // a wide target that's easy to hit ON PURPOSE, worth crashing into for the energy refund
        // before the final leg, since both legs either side of it need a real charge rather than a
        // token tap. Ends at a finish pad. Returns the finish platform.
        // Direct feedback on an earlier pass: "the final segment was really bare bones" - expanded
        // from 2 stops either side of the refuel wall to 4, with small X/Y jitter on every one
        // (direct request: "where ever you can add some small horizontal and vertical variation"),
        // stretching the total run from 150m to 240m. Still built around the same core idea - a
        // fast Defy Gravity Forward run with one deliberate, unmissable wall checkpoint worth
        // crashing into on purpose - just with more of it either side. Returns the finish platform.
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

            // Wide, unmissable wall spanning the corridor - deliberately a target you crash INTO
            // on purpose, not an obstacle you route around.
            Vector3 wallCenter = new Vector3(x0, y + 4f, z0 + 115f);
            Vector3 wallSize = new Vector3(16f, 10f, 1f);
            CreateBlock(segment.transform, "RefuelWall", wallCenter, wallSize, wallMat);

            // A small platform right at the wall's base, floor-level - so a shot that clips low
            // still has solid ground underfoot right where the wall is, rather than open air.
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

        // Static equivalent of LevelGenerator.BuildFinishPad (that one's an instance method tied
        // to LevelGenerator's own runtime fields - Level3's geometry is hand-placed at edit time,
        // not generated at runtime, so this is its own copy rather than a shared call). Same
        // visual language (translucent green pad, floating blue "Finish" text, billboard) and the
        // same FinishLine trigger component, just parameterized directly instead of reading fields.
        static void BuildLevel3FinishPad(Transform parent, Vector3 platformPosition, Vector3 platformSize, Transform cameraTransform)
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "FinishPad";
            pad.transform.SetParent(parent, true);
            UnityEngine.Object.DestroyImmediate(pad.GetComponent<Collider>());

            // Small vertical gap above the platform surface so the two faces never coincide - see
            // LevelGenerator.BuildFinishPad's own comment for the Z-fighting this avoids.
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

        // "Camera should face behind the player and look at the next platform on bootup" (direct
        // request) - same idea as LevelGenerator.FaceCameraTowardFinish for Level1, pulled out
        // into the reusable CameraStartFacing component (see its own comment) since Level2 has no
        // LevelGenerator to hang this off of. A runtime component, not a baked scene transform -
        // does NOT touch any existing object's saved position/rotation.
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

        // "Add a FastPacedLevel, with only 1 control scheme" - a new scene, mirroring
        // SetupLevel3's exact shape (copy Sandbox Scene for identical RenderSettings, instantiate
        // the 3 shared prefabs, cross-wire, then build hand-placed geometry) but with per-instance
        // overrides on the Player: FastPaced is the only reachable scheme
        // (schemeSwitchingEnabled false), gravity is off, and launch force is raised - see
        // ApplyLaunchTuning's own call below for what stays shared vs. what's overridden after it.
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

            // Same duplicate-camera-name trap as every other level scene - both names destroyed
            // deliberately, see SetupLevel1's own comment for why.
            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");
            // FastPacedLevel.unity was created by copying Sandbox Scene, which already has its
            // own 5 circular jump platforms and floor - same reasoning as SetupLevel2/SetupLevel3,
            // those don't belong here (and the floor would just get in the way of the spiral).
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

            // Same single-source-of-truth reasoning as every other level - this is a plain
            // instance of Player.prefab, not rebuilt from scratch here.
            ApplyLaunchTuning(controller);

            // FastPaced-only overrides, scoped to just this scene's Player instance (every other
            // scene's instance keeps ApplyLaunchTuning's shared defaults untouched) - direct
            // request: "only 1 control scheme", "increase the base speed of the players
            // launching in that scene for that control scheme", "gravity shouldn't affect the
            // player in this scene".
            controller.SetControlScheme(ControlScheme.FastPaced);
            controller.schemeSwitchingEnabled = false;
            controller.gravity = 0f;
            // The shared default (-30) is tuned for world-up levels where being below the floor
            // means "fell off". This level's spiral circles through the X/Y plane - platforms on
            // the lower half of each turn legitimately sit at Y down to -(startRadius + 9 *
            // radiusStep) = -122, so a shot flying toward one crossed Y=-30 mid-flight and got
            // silently scene-reset (direct bug report: "you respawn randomly sometimes while you
            // are launching in the middle of the air"). With zero gravity there's no such thing
            // as falling forever anyway - drag stops every missed shot dead in place - so this
            // only needs to be safely below anything reachable, not carefully tuned. BOTH
            // controllers need this - KineticCubeControllerFreeMove carries its own duplicate
            // fallResetY field with its own scene-reload check, and overriding only the main
            // controller's copy left the FreeMove one still firing at -30 (the exact "still
            // respawns even at -1000" bug reported after the first fix).
            controller.fallResetY = -1000f;
            freeMoveController.fallResetY = -1000f;
            // Tuned so every jump is makeable with >=20% distance margin USING ONLY THE ENERGY
            // ACTUALLY AVAILABLE at that point (direct request) - the constraint that matters is
            // the energy ECONOMY, not max charge: spending the whole tank every launch and
            // crash-refunding 1.2x, energy at launch k is 0.2 * 1.2^(k-1) (20%, 24%, 28.8%, ...,
            // 100% only by the last jump), and charge is capped by stored energy. The previous
            // curve (90-220 force over a 2.8->1.0 damping ramp) failed this from launch 3 on -
            // its distance grew too slowly at low charge fractions. CONSTANT damping makes
            // distance linear in charge (zero gravity: distance ~ 0.98 * force / damping,
            // empirically 215.6m measured vs 220 predicted at damping 1.0), so force = 25 + 225c
            // at damping 1.0 gives every launch k a reach of ~0.98*(25 + 225 * energy_k) against
            // its actual gap - worst case ratio 1.27 (final jump: ~245m reach vs 192m gap), best
            // ~2.3 (opening jumps), all comfortably past the required 1.2.
            controller.minLaunchForce = 25f;
            controller.maxLaunchForce = 250f;
            controller.fastPacedMinDamping = 1.0f;
            controller.fastPacedMaxDamping = 1.0f;
            // Crash refund = exactly 1.2x the energy the launch spent, replacing the speed-based
            // gain formula for this scheme only - see the field's own comment.
            controller.fastPacedRefundMultiplier = 1.2f;
            // 150% game speed while a FastPaced launch is in flight - see the field's own comment.
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

            // The reticle's cross+ring visual (see BuildLandingPreview's Circle) is gated behind
            // ghostAndCrosshairEnabled, which stays false for every other scene's instance - only
            // this one needs it on, for FastPaced's TrailAndCrosshair mode.
            controller.landingPreview.ghostAndCrosshairEnabled = true;

            // "This scene specifically should have no wiring to the other scenes" (direct
            // request) - deactivates the pause menu's Scenes button (and the panel it opens) on
            // just this scene's PauseSystem instance, as per-instance overrides; every other
            // scene's pause menu keeps the full scene list. Restart and Quit stay, since both
            // are self-contained.
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

        // "Are all platforms placed on the circumference of a circle, with their rotation set to
        // the centre of that circle (= z axis), for every platform the circle radius becomes
        // slightly bigger and the distance along the z axis should too, the angle of the x and y
        // coordinates between 2 subsequent platforms needs to be atleast a 55 degrees difference,
        // the starting platform should always start with world up as its rotation" (direct
        // request, implemented verbatim). Builds an expanding helix: X/Y form the circle (a fixed
        // 60-degree step per platform, safely past the 55-degree minimum), Z is the separate depth
        // axis advancing alongside the radius, so the whole thing spirals both outward and forward
        // at once. Each platform's rotation aligns local up (its flat landing face) to point
        // radially INWARD, toward the z-axis at that platform's own depth ("the centre of that
        // circle") - except the very first, which stays plain world-up (direct request), since
        // it's the flat launch pad the spiral starts from rather than a point ON the spiral
        // itself. Returns the first real spiral platform (not the start pad) so the caller can
        // point the starting camera down the spiral rather than at its own far end.
        static GameObject BuildFastPacedSpiral(Transform player, Transform cameraTransform, PauseController pauseController)
        {
            GameObject container = GameObject.Find("FastPacedSpiral");
            if (container != null) UnityEngine.Object.DestroyImmediate(container);
            container = new GameObject("FastPacedSpiral");

            // Pre-configured as Transparent-surface at full alpha (1, visually identical to
            // Opaque until faded) rather than left Opaque - direct bug report: standing on/stuck
            // to one of these tilted platforms in first person puts the camera right against its
            // surface, filling the screen with solid color. TransparentWhenOccupied (added to
            // each platform below) fades this by lowering alpha at runtime, which URP silently
            // ignores on an Opaque-surface material - see MakeTransparent's own comment for that
            // gotcha. Starting at alpha 1 keeps every platform looking exactly as before until
            // the player actually touches one.
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
            // Randomized placement along the circumference (direct request, replacing the
            // original perfectly-regular 60-degrees-every-step spiral: "the platforms shouldn't
            // form a perfect spiral, they should be positioned randomly along the circumference")
            // - each platform's angle steps from the previous by a random amount in
            // [minAngleStepDeg, maxAngleStepDeg], in a randomly chosen direction around the
            // circle. The minimum keeps the required "more than a 55 degree difference" between
            // consecutive platforms; the MAXIMUM is the reachability half of that same request
            // ("so it is always possible for the player to reach the next platform"): the
            // empirically-measured max-charge range is ~216m (temporary Play-mode diagnostic -
            // this project's own PredictLandingPoint comment distrusts hand-derived drag math),
            // and the worst-case gap at the spiral's outermost radii (~116m mean radius) with a
            // 100-degree step is chord 2*116*sin(50) ~ 178m plus the 20m Z advance - a demanding
            // but genuinely reachable max-charge shot, with real margin under 216.
            // Raised from the original 56 (which satisfied the earlier "atleast more than 55
            // degrees" requirement) - direct request: "the difference between consecutive
            // platforms should become 75 degrees now". Still random within [75, 100]: the layout
            // stays scattered rather than regular, and the 100 ceiling (the reachability bound -
            // see the comment above) is unchanged.
            const float minAngleStepDeg = 75f;
            const float maxAngleStepDeg = 100f;
            // Seeded, not UnityEngine.Random - "randomly positioned" is about the layout not
            // being a regular spiral, and a FIXED seed keeps every SetupAll re-run producing the
            // exact same layout (the same idempotency every other builder in this file already
            // has) instead of silently churning the saved scene on every rebuild.
            System.Random rng = new System.Random(20260806);
            // Empirically measured zero-gravity flight distances (same diagnostic as above):
            // ~30m at minimum charge, ~78m at 50%, ~216m at max. Radius/Z growth tuned against
            // those so gaps climb from an easy opening jump to a demanding late-spiral shot.
            const float startRadius = 14f;
            const float radiusStep = 12f;
            const float startZ = 16f;
            const float zStep = 20f;
            Vector3 platformSize = new Vector3(4.5f, 0.5f, 4.5f);
            Vector3 finishSize = new Vector3(6f, 0.5f, 6f);

            GameObject firstSpiralPlatform = null;
            // First platform: random within the UPPER two quadrants only, with a margin off the
            // horizon so it clearly reads as overhead - direct request: "the very first platform
            // from the starting platform needs to be reached by shooting up". The companion "at
            // least 75 degrees away from the starting platform" is measured against the start
            // pad's ORIENTATION (it sits at the circle's center, so there's no angular POSITION
            // to measure from): the start pad faces world-up, and any upper-half circumference
            // platform's landing face points back down toward the axis - already 90+ degrees
            // from world-up everywhere in this range, so the 75-degree minimum holds by
            // construction.
            float angleDeg = 30f + (float)rng.NextDouble() * 120f;
            for (int i = 1; i <= platformCount; i++)
            {
                if (i > 1)
                {
                    // Always advancing the SAME direction around the circle, not a random sign
                    // per step (the earlier behavior) - direct request: "make sure there is
                    // about an even distribution of the platforms along the 4 quadrants". A
                    // random sign can bounce back and forth between the same two quadrants;
                    // a one-directional sweep at 75-100 degrees per step (roughly a quadrant
                    // each) cycles all four continuously, so 10 platforms spread ~2-3 per
                    // quadrant. The step SIZE stays random, so it still reads as scattered
                    // rather than a regular spiral, and the [75, 100] bounds are unchanged.
                    //
                    // The FINAL step (onto the finish platform) uses its own raised range -
                    // direct request: "the finish platform needs to also have minimally 100
                    // degrees difference with the last normal platform". Capped at 115 rather
                    // than open-ended for the same reachability reason as maxAngleStepDeg: at
                    // the outermost radii a 115-degree chord plus the 20m Z advance is ~197m,
                    // still under the measured ~216m max-charge range.
                    bool finalStep = i == platformCount;
                    float stepMin = finalStep ? 100f : minAngleStepDeg;
                    float stepMax = finalStep ? 115f : maxAngleStepDeg;
                    angleDeg += stepMin + (float)rng.NextDouble() * (stepMax - stepMin);
                }
                float rad = angleDeg * Mathf.Deg2Rad;
                float radius = startRadius + (i - 1) * radiusStep;
                float z = startZ + (i - 1) * zStep;

                Vector3 center = new Vector3(radius * Mathf.Cos(rad), radius * Mathf.Sin(rad), z);
                // Direction from this platform back toward (0, 0, z) - the circle's centre at
                // this same depth ("= z axis"). Local up gets aligned to point this way, below.
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

        // The finish platform's extras: a billboarded "Finish" TextMesh floating off its landing
        // face, and a trigger volume hugging that face that opens the win screen (FinishLineWin ->
        // PauseController.ShowWin - see each of their own comments) instead of reloading the
        // scene the way the other levels' FinishLine does. Everything is positioned along
        // `inward` (this platform's landing-face normal - the spiral's platforms face every
        // direction, so a hardcoded world-up offset like BuildLevel3FinishPad's would bury both
        // inside or behind the platform for most of the spiral).
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
            // Doubled from the 0.2 the other levels' finish text uses (direct request: "the
            // billboard text of the finish should be twice bigger") - this level's final jump is
            // also its longest, so the label has to read from much further away.
            textMesh.characterSize = 0.4f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;

            Billboard billboard = textGo.AddComponent<Billboard>();
            billboard.target = cameraTransform;

            GameObject trigger = new GameObject("FinishTrigger");
            trigger.transform.SetParent(parent, true);
            // Rotated WITH the platform so the box's local Y is the landing-face normal - size
            // and offset then mean the same thing they do for a flat finish pad, just tilted.
            trigger.transform.SetPositionAndRotation(platformCenter + inward * (platformSize.y * 0.5f + 1f), platformRotation);
            BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(platformSize.x, 2f, platformSize.z);

            FinishLineWin finishWin = trigger.AddComponent<FinishLineWin>();
            finishWin.pauseController = pauseController;
        }

        // Plain solid cube - collider included (never destroyed here, unlike the decorative
        // ghost/preview cubes elsewhere in this file), since every use of this so far needs to
        // actually block the player.
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

        // Explicitly (re)applies every setting on every run, not just when creating the light for
        // the first time - a plain "create once, no-op if it already exists" check (the previous
        // behavior) would leave an already-saved scene's Directional Light stuck on whatever
        // shadow setting it had the very first time this ran, exactly the staleness risk every
        // other tunable in this file already guards against explicitly.
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
            // Real-time shadows back ON for everything (direct request, reversing the earlier
            // disable-all) - EXCEPT the player, whose renderers have shadow casting turned off
            // (see BuildPlayerCube / RefreshShadows): its only shadow is the PlayerShadow
            // drop-disc, per the same request.
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

        // GameObject.Find deliberately skips inactive GameObjects (see its own docs) - found via
        // direct evidence, not a guess: Level2.unity carried two leftover "SandboxPlatforms"
        // containers that GameObject.Find("SandboxPlatforms") couldn't see at all (both had
        // m_IsActive: 0 on disk), so DestroyIfExists silently did nothing to them every single
        // run. Every caller of DestroyIfExists actually wants "gone, active or not" - searching
        // inactive objects too is what makes that guarantee actually hold.
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
            // Loop, not a single Find+Destroy - a single-shot destroy silently leaves any
            // additional duplicates behind and the count never actually goes back to zero on a
            // re-run (as happened with the camera rig - 4 instances found in Level1.unity, while
            // Player/PauseSystem/LevelGenerator each stayed at a correct 1). This converges to
            // zero regardless of however many are actually present.
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

        // Single source of truth for launch tuning, called from BOTH BuildPlayerCube (Sandbox
        // Scene, rebuilding Player.prefab itself) and SetupLevel1 (which only ever instantiates
        // that already-saved prefab, never rebuilds it) - the latter previously relied on plain
        // prefab-instance inheritance to stay in sync instead of reassigning these explicitly,
        // which is exactly the kind of staleness every other tunable in this file already guards
        // against on purpose. Two separate copies of these numbers would only reintroduce the
        // same risk the next time any of them changes.
        static void ApplyLaunchTuning(KineticCubeController controller)
        {
            controller.minLaunchForce = 45f;
            controller.maxLaunchForce = 110f;
            // Halves distance vs the previous (1.3, 0.4) at the same force - see the field's own
            // comment in KineticCubeController.cs for the empirical verification.
            controller.minLaunchDamping = 2.8f;
            controller.maxLaunchDamping = 1.0f;
            // StickAim's charge (and Mixed's airborne charge) now uses this same
            // minLaunchForce/maxLaunchForce curve directly - see stickAimUpAngle etc. below for
            // the per-direction tilt angles, which are all that's still scheme-specific.
            controller.stickAimUpAngle = 80f;
            controller.stickAimDownAngle = 60f;
            controller.stickAimForwardAngle = 30f;
            controller.stickAimForwardNeutralAngle = 5f;
            // Flat, low damping so a downward launch keeps accelerating under gravity instead of
            // the arc-shaping damping curve above fighting it to a near-constant fall speed - see
            // the field's own comment in KineticCubeController.cs.
            controller.downLaunchDamping = 0.2f;
            // Only counts the stick as "held" (tilted angle vs. neutral) past 90% deflection -
            // see the field's own comment in KineticCubeController.cs.
            controller.stickAimDeadzone = 0.9f;
            // "Bullet time" while charging any launch - see the field's own comment.
            controller.chargeTimeScale = 0.75f;

            // Universal energy economy - see each field's own comment in KineticCubeController.cs.
            controller.startingEnergyFraction = 0.2f;
            controller.energyCostPerFullCharge = 1f;
            controller.energyGainPerSpeed = 0.03f;
            controller.energyGainSpeedBonus = 0.01f;
            controller.minEnergyGainPerCrash = 0.05f;
            controller.chargeAccumulationRate = 0.3f;

            // Defy Gravity scheme tuning.
            controller.minDefyGravityDuration = 0.4f;
            controller.maxDefyGravityDuration = 1.5f;
            controller.maxDefyGravitySpeed = 70f;
            controller.defyGravityFallDamping = 0.2f;
            // Moved here (was only set in BuildPlayerCube) so Level1's instance gets the same
            // explicit anti-staleness reassignment Sandbox Scene's prefab does - the exact same
            // reasoning this method already exists for. Negative tilts UP (see the field's own
            // comment) - -30 starts noticeably higher than the previous +20.
            controller.defaultAimPitch = -30f;

            // StickAim stays the default STARTING scheme, but all three (Launch Instantly/
            // StickAim/Mixed) are reachable again via the Right Bumper cycle - Hold-Release/
            // Analog stay fully in the project (nothing deleted), just still locked out via
            // alternateSchemesEnabled, unrelated to this.
            controller.SetControlScheme(ControlScheme.StickAim);
            controller.schemeSwitchingEnabled = true;
            // Matches ProjectSettings/DynamicsManager.asset's own gravity - kept in sync here too
            // since KineticCubeController.Awake/OnValidate applies this OVER the project setting
            // at runtime, and it's meant to be a fast public testing knob, not a second source of
            // truth that can quietly drift from the project value. Also live-tuned in Sandbox
            // Scene (-30, up from the previous -18) and carried forward as the new default.
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

            // The visible mesh lives on its own "Visual" child rather than directly on the
            // physics root - KineticCubeControllerFreeMove leans it while airborne, and the root
            // Rigidbody has to stay upright (FreezeRotation, set below) for its BoxCast ground
            // check to keep working exactly like KineticCubeController's does. Idempotent: only
            // moves anything the first time this runs - on a re-run the root's MeshFilter/
            // MeshRenderer are already gone, having been moved to Visual previously.
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

            // Explicitly (re)assign every tunable, not just on first creation - once a component
            // is saved once, its serialized values win over the C# field initializers on later
            // loads, so relying on the initializer alone silently keeps stale numbers on every
            // re-run of this script after the first. This intentionally means re-running Setup()
            // always resets these to the current code-defined defaults.
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

            // Hold-Release and Analog kept in the project, not removed, but not selectable for
            // now - Launch Instantly is the only reachable scheme (see HandlePreviewModeSwitch).
            controller.alternateSchemesEnabled = false;
            controller.facingArrow = BuildFacingArrow(player.transform);

            // The core launch mechanic - always enabled. Runs together with
            // KineticCubeControllerFreeMove below rather than one disabling the other; the two
            // coordinate directly (see KineticCubeController.AllowGroundedMovement /
            // AllowAirborneNudge) so free movement only ever goes passive for exactly as long as
            // each specific kind of movement is actually unsafe, instead of needing to be toggled
            // off entirely.
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

            // Also always enabled - see the comment on controller.enabled above. Free movement
            // and launching are complementary now, not alternatives to switch between.
            freeMoveController.enabled = true;

            // The player must never cast a real-time shadow (direct request - its only shadow
            // is the PlayerShadow drop-disc) - covers the Visual mesh AND every indicator
            // child (arrows, landing preview dots) in one sweep.
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
            arrowRoot.transform.localPosition = Vector3.zero; // spawns from the cube's center, pokes out through whichever face faces the aim direction

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

        // Flat, yaw-only marker shown on top of the player while StickAim is active (see
        // KineticCubeController.facingArrow) - parented under the player ROOT rather than the
        // `visual` child, same reasoning as AimArrow above but more important here: `visual`
        // leans with pitch/roll while airborne (KineticCubeControllerFreeMove), and this arrow
        // needs to stay perfectly flat regardless, which only the root (FreezeRotation) guarantees.
        static FacingArrowIndicator BuildFacingArrow(Transform parent)
        {
            Transform existing = parent.Find("FacingArrow");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject arrowRoot = new GameObject("FacingArrow");
            arrowRoot.transform.SetParent(parent, false);
            arrowRoot.transform.localPosition = new Vector3(0f, 0.55f, 0f); // just clear of the cube's top face (half-height 0.5)

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

        // Deliberately NOT parented under the player (unlike AimArrow above) - it needs to sit at
        // ground level via its own raycast, independent of however high the player currently is,
        // so a child transform (which would inherit the player's height) wouldn't work here.
        // Found/destroyed by name instead of the parent.Find(...) pattern above for the same
        // reason - there's no meaningful parent to search under.
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

            // A cylinder's flat caps already face up/down with no rotation needed - scaling its
            // height down to near-nothing turns it into a flat disc, the simplest way to get a
            // circular shadow shape out of a built-in primitive without a custom mesh or texture.
            const float diameter = 1.6f;
            const float thickness = 0.02f;
            visualGo.transform.localScale = new Vector3(diameter, thickness, diameter);

            Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
            Material shadowMat = new Material(FindBestShader());
            shadowMat.color = shadowColor;
            MakeTransparent(shadowMat, shadowColor.a);
            shadowMat = SaveMaterialAsset(shadowMat, "PlayerShadowMaterial");
            visualGo.GetComponent<Renderer>().sharedMaterial = shadowMat;
            // The fake drop-shadow disc must not itself cast a real shadow now that real-time
            // shadows are back on.
            visualGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            shadowScript.player = player;
            shadowScript.shadowVisual = visualGo.transform;
            shadowScript.maxDistance = 500f;
            shadowScript.surfaceOffset = 0.02f;

            EditorUtility.SetDirty(shadowScript);
        }

        static void BuildSandboxSignText()
        {
            // Leftover from this method's previous world-space-TextMesh version, which used to
            // idempotency-check under this name - a scene saved by that old version would
            // otherwise keep this orphaned GameObject around forever, since nothing looks for it
            // by this name anymore.
            GameObject stale = GameObject.Find("SandboxSignText");
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale);

            GameObject existing = GameObject.Find("ParkourHint");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            // Its own standalone Canvas rather than a child of PauseSystem's PauseCanvas -
            // PauseSystem is a shared prefab instantiated identically in both scenes, and this
            // message ("head to the Parkour level") only makes sense in Sandbox Scene, so it
            // needs to exist independently of that shared hierarchy rather than as an override
            // on top of it.
            GameObject root = new GameObject("ParkourHint");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // below PauseCanvas's 100, so pausing immediately still draws on top

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject textGo = new GameObject("ParkourHintText", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            // Directly beneath the energy meter (PauseSystem's EnergyMeter container: top-right,
            // anchoredPosition -24/-24, sizeDelta 320x36 - see BuildPauseSystem) with a 16px gap:
            // 24 (meter's own top offset) + 36 (meter height) + 16 (gap) = 76 - direct request:
            // "the Parkour Hint should appear underneath the energy meter". This is a separate
            // Canvas from the meter's (see this method's own comment on why), so the two can only
            // be kept aligned by hand like this rather than by actual layout parenting.
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
        // 8.5 sits in the empirically-measured gap between what a shallow shot near minimum
        // charge reaches (angle 10-25 deg, charge 0.05-0.20 -> ~2.2 to ~6.5m) and what a steep
        // shot near max charge reaches (angle 55-75 deg, charge 0.85-1.0 -> ~10.4 to ~33m) -
        // verified with a temporary real-physics batch simulation (mirroring
        // KineticCubeController's actual force/damping curve) rather than guessed, since drag
        // makes this non-linear enough that eyeballing it would likely be wrong. Both play styles
        // land within reach of this radius once the jitter below is factored in.
        const float SandboxPlatformRadius = 8.5f;
        const float SandboxPlatformRadiusJitter = 1.5f;
        const float SandboxPlatformAngleJitterDeg = 10f;
        // How far the platform's BOTTOM face sits above the ground plane - always strictly
        // positive (see the bug note on BuildSandboxPlatforms below), so this is a clearance
        // range, not a center-position jitter that could go negative.
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

            // Baked directly into the scene here, once, at edit time - deliberately NOT a
            // runtime spawner (unlike LevelGenerator, which regenerates Level1's platforms every
            // time that scene loads). These are meant to just sit in Sandbox Scene as permanent
            // fixtures the same way the Plane/Player/PauseSystem already do.
            //
            // BUG NOTE (found via the trail-flicker report - flickers only while standing on a
            // platform, never on the open plane): the original center-position formula here was
            // `groundY + protrusion - platformSize.y * 0.5f` with protrusion averaging 0.12
            // against a platform half-height of 0.15 - i.e. the platform's bottom face was BELOW
            // groundY in the typical case, genuinely overlapping the plane's collider rather than
            // just resting on it. KineticCubeController's landing-preview prediction clones every
            // static collider into an isolated PhysicsScene and simulates real physics through
            // it (PredictLandingPoint/BuildPredictionGeometryProxies) - contact resolution right
            // at an overlapping-collider seam is exactly the kind of degenerate geometry that
            // produces unstable, frame-to-frame-varying contact normals, which would show up as
            // exactly this symptom: fine on the plane (a single unambiguous surface), unstable on
            // a platform (two overlapping surfaces right underfoot). Fixed below by keeping the
            // platform's bottom face strictly above groundY at all times.
            for (int i = 0; i < SandboxPlatformCount; i++)
            {
                // Evenly spaced base angle (360/count apart) plus jitter on angle AND radius -
                // jittering angle alone would keep every platform on a perfect circle just
                // unevenly spaced around it; jittering radius too is what actually breaks the
                // circle shape itself.
                float angleDeg = i * (360f / SandboxPlatformCount) + UnityEngine.Random.Range(-SandboxPlatformAngleJitterDeg, SandboxPlatformAngleJitterDeg);
                float radius = SandboxPlatformRadius + UnityEngine.Random.Range(-SandboxPlatformRadiusJitter, SandboxPlatformRadiusJitter);
                float angleRad = angleDeg * Mathf.Deg2Rad;

                float x = spawnPosition.x + radius * Mathf.Sin(angleRad);
                float z = spawnPosition.z + radius * Mathf.Cos(angleRad);
                // Bottom face at groundY + gap (gap always > 0), NOT a center-position jitter
                // that could push the bottom face below groundY - see the bug note above.
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
            // Pool sized for the longest realistic shot (near max force/charge) at roughly
            // maxDotSpacing apart; LandingPreviewController activates only as many of these as
            // the actual predicted arc length needs each frame, so short shots just use a
            // fraction of the pool instead of stretching 14 dots across a long gap.
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

            // Ring of small dashed segments around the cross - FastPaced scheme's reticle
            // (direct request: "a cross with a circle at the end") - built from the same small-
            // block visual language as the Trail's dots rather than a custom mesh. Same angle
            // convention as FacingFlatDirection/StickWorldDirection elsewhere in this project
            // (Quaternion.Euler(0, angleDeg, 0) * Vector3.forward), so segment i's local +Z
            // points radially outward and local +X (the segment's long axis) lands tangent to
            // the ring.
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
            preview.ghostGroundOffset = 0f; // PredictLandingPoint now returns the cube's own rest-center (BoxCast-based), already correct with no offset
            preview.markerGroundOffset = -0.5f; // crosshair marks the ground surface, half a cube-height below that center
            preview.trailGroup = trail;
            preview.crosshairGroup = crosshair;
            preview.trailDots = dots;
            preview.maxDotSpacing = 1f;
            preview.positionSmoothTime = 0.05f;
            preview.snapDistance = 25f;
            preview.ghostAndCrosshairEnabled = false;

            return preview;
        }

        // URP Lit/Unlit default to an Opaque surface - alpha is ignored unless the material is
        // explicitly switched to Transparent via these properties/keywords/render queue (there's
        // no single "make transparent" call). This is the one visual detail I can't confirm
        // without Play-mode access - worth a look once this actually runs in the Editor.
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

        static ThirdPersonOrbitCamera BuildCameraRig(GameObject camGo, InputActionReference lookRef)
        {
            ThirdPersonOrbitCamera orbitCam = camGo.GetComponent<ThirdPersonOrbitCamera>();
            if (orbitCam == null) orbitCam = camGo.AddComponent<ThirdPersonOrbitCamera>();
            orbitCam.lookAction = lookRef;
            // Explicitly (re)assigned, not left to the component's own field defaults - unlike a
            // freshly-added component, GetComponent above finds the EXISTING one on every re-run
            // after the first, whose serialized values are whatever they were the very first time
            // this ran, regardless of any later change to the code-side default (the same
            // staleness risk every other tunable in this file already guards against explicitly).
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

            // Rebuild the panels fresh each run rather than patching them in place.
            DestroyChildIfExists(canvasGo.transform, "PausePanel");
            DestroyChildIfExists(canvasGo.transform, "ControlsPanel");
            DestroyChildIfExists(canvasGo.transform, "ScenesPanel");
            DestroyChildIfExists(canvasGo.transform, "PreviewModeLabel");
            DestroyChildIfExists(canvasGo.transform, "ControlsHintLabel");
            DestroyChildIfExists(canvasGo.transform, "EnergyMeter");
            DestroyChildIfExists(canvasGo.transform, "RadialMenu");

            // Created before the panels below so it's an earlier sibling and renders BEHIND
            // them - otherwise this permanent corner label would poke through the pause backdrop.
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

            // Always-on control reminder, top-left corner - unlike ControlsPanel (above) this
            // doesn't need the pause menu opened to read. Same earlier-sibling trick as
            // PreviewModeLabel so the pause backdrop still covers it while paused.
            GameObject hintGo = new GameObject("ControlsHintLabel", typeof(RectTransform));
            hintGo.transform.SetParent(canvasGo.transform, false);
            RectTransform hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 1f);
            hintRt.anchorMax = new Vector2(0f, 1f);
            hintRt.pivot = new Vector2(0f, 1f);
            hintRt.anchoredPosition = new Vector2(24f, -24f);
            // Wider/taller than before to fit fontSize 30 without wrapping/clipping.
            hintRt.sizeDelta = new Vector2(600f, 300f);
            Text hintText = hintGo.AddComponent<Text>();
            hintText.font = font;
            hintText.fontSize = 30;
            hintText.alignment = TextAnchor.UpperLeft;
            hintText.color = new Color(1f, 1f, 1f, 0.9f);
            // Content is set at runtime by KineticCubeController.UpdateControlsText, not here -
            // it needs to reflect whichever scheme is CURRENTLY active, which this Editor script
            // has no way to know.
            hintText.text = "";
            // Plain white text on top of a busy 3D scene is often unreadable depending on
            // background - a soft drop shadow keeps it legible without needing a backing panel.
            Shadow hintShadow = hintGo.AddComponent<Shadow>();
            hintShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            hintShadow.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject pausePanel = CreatePanel("PausePanel", canvasGo.transform, backdrop);
            CreateText("Title", pausePanel.transform, "PAUSED", font, 48, new Vector2(0f, 200f), new Vector2(600f, 80f));
            GameObject restartBtn = CreateButton("RestartButton", pausePanel.transform, "Restart", font, accent, new Vector2(0f, 95f), new Vector2(300f, 70f));
            GameObject scenesBtn = CreateButton("ScenesButton", pausePanel.transform, "Scenes", font, accent, new Vector2(0f, 5f), new Vector2(300f, 70f));
            GameObject controlsBtn = CreateButton("ControlsButton", pausePanel.transform, "Controls", font, accent, new Vector2(0f, -85f), new Vector2(300f, 70f));
            GameObject quitBtn = CreateButton("QuitButton", pausePanel.transform, "Quit", font, accent, new Vector2(0f, -175f), new Vector2(300f, 70f));

            // Hidden by default in every scene - only FastPacedLevel's finish (FinishLineWin ->
            // PauseController.ShowWin) ever activates it, sitting above the PAUSED title so the
            // ordinary pause layout underneath stays untouched.
            Text winLabel = CreateText("WinLabel", pausePanel.transform, "You Win!", font, 64, new Vector2(0f, 300f), new Vector2(700f, 90f));
            winLabel.color = new Color(0.3f, 1f, 0.45f);
            winLabel.gameObject.SetActive(false);

            GameObject controlsPanel = CreatePanel("ControlsPanel", canvasGo.transform, backdrop);
            CreateText("ControlsTitle", controlsPanel.transform, "CONTROLS", font, 48, new Vector2(0f, 220f), new Vector2(600f, 80f));
            // Wider/shorter-per-line than before to fit fontSize 30's longer StickAim description
            // without running into the title or Back button.
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

            // Persistent listeners only: Button.onClick.AddListener() from an Editor script
            // registers a runtime-only delegate that is NOT serialized, so it would silently
            // vanish the moment this prefab/scene is reloaded - UnityEventTools.AddPersistentListener
            // is the programmatic equivalent of dragging a method into the onClick list by hand.
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

            // Yellow energy / blue charge-preview meter, top-right corner - direct request. Both
            // fills are Image.Type.Filled/Horizontal over the SAME rect, blue layered on top (a
            // later sibling) so it always reads as "this much of my energy is about to be spent".
            // Goes up to 100% and starts filled at 20% "for free" - Image.fillAmount is already a
            // 0-1 (0-100%) range, and EnergyMeterController.SetEnergy is fed energyFraction, which
            // KineticCubeController.Awake initializes from startingEnergyFraction (0.2) - direct
            // request, no separate change needed here beyond the outline below.
            GameObject energyContainer = new GameObject("EnergyMeter", typeof(RectTransform));
            energyContainer.transform.SetParent(canvasGo.transform, false);
            RectTransform energyRt = energyContainer.GetComponent<RectTransform>();
            energyRt.anchorMin = new Vector2(1f, 1f);
            energyRt.anchorMax = new Vector2(1f, 1f);
            energyRt.pivot = new Vector2(1f, 1f);
            energyRt.anchoredPosition = new Vector2(-24f, -24f);
            energyRt.sizeDelta = new Vector2(320f, 36f);

            // Outline: a solid white panel filling the WHOLE container, sitting behind everything
            // else - Backdrop and both fill bars are inset by meterOutlineThickness so a border of
            // it always shows around the outside, reading as an outline of the entire meter
            // regardless of current fill (direct request: "show an outline of the entire meter").
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

            // Dpad-opened radial scheme-select menu, center screen, hidden until the Dpad is
            // held - direct request. Mapping (documented in the controls text too): Up = Launch
            // Instantly, Right = Stick Aim, Down = Mixed, Left = Defy Gravity.
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

        // A plain solid-color fill bar - Image.Type.Filled/Horizontal stretched over the whole
        // parent rect (minus inset, if any), so SetEnergy/SetCharge (EnergyMeterController) just
        // move fillAmount. inset lets a bar sit inside a border panel instead of covering it.
        // Image.OnPopulateMesh has an unconditional early-out - "if (sprite == null) {
        // GenerateSimpleSprite(...); return; }" - that runs BEFORE it ever looks at `type`, so a
        // Filled-type Image with no sprite assigned silently renders as a full, unfilled
        // rectangle no matter what fillAmount is set to. That's exactly the bug behind "use a
        // fill amount for the meter, it's still not normal" - fillAmount was being set correctly
        // the whole time, it just had no sprite to actually apply it to. A plain solid-white
        // sprite (not one of Unity's built-in UI sprites, which are 9-sliced/rounded and would
        // fight "the meter should remain a rectangle") fixes this without changing anything else.
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

        // A flat, un-sliced, borderless 4x4 white square saved as a real project asset (not an
        // in-memory Sprite.Create result, which wouldn't survive being referenced from a saved
        // prefab - it has no asset path to serialize against). Generated once and reused/loaded
        // on every later run, same idempotency pattern as everything else this file creates.
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

        // Shrinks a full-stretch RectTransform (as CreatePanel produces) inward by inset on all
        // four sides - used to keep a background panel from fully covering a border panel behind
        // it. Returns the same GameObject so call sites can wrap a CreatePanel call directly.
        static GameObject InsetRect(GameObject go, float inset)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return go;
        }

        static void WireButton(GameObject buttonGo, UnityEngine.Events.UnityAction call)
        {
            // buttonGo is always freshly created (panels are destroyed and rebuilt every run,
            // see DestroyChildIfExists above), so there's never an existing listener to clear first.
            Button button = buttonGo.GetComponent<Button>();
            UnityEventTools.AddPersistentListener(button.onClick, call);
        }

        // Same persistence reasoning as WireButton, but for LoadSceneByName(string) - each scene
        // button needs a different baked-in argument, which AddPersistentListener (no-arg only)
        // can't express. AddStringPersistentListener wires the call AND sets that fixed argument
        // as a serialized persistent value, equivalent to typing it into the Inspector's onClick
        // argument field by hand.
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

            // Button.Transition.ColorTint multiplies targetGraphic.color by the ColorBlock state
            // color - against a near-black base (the old 0.15 gray) even a vivid accent multiplies
            // down to a barely-different dark smear, which read as "faded". Fix: put the accent
            // color on the base image itself (normalColor stays white so it shows undistorted),
            // and use the ColorBlock states purely as a brighten/dim pulse on top of that.
            Image image = go.AddComponent<Image>();
            image.color = accentColor;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image; // not auto-wired by scripted AddComponent, unlike the Editor's "Add Component" Reset()
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.selectedColor = new Color(1.25f, 1.25f, 1.25f, 1f); // gamepad focus uses this, not highlightedColor
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;

            Text text = CreateText("Label", go.transform, label, font, 28, Vector2.zero, size);
            text.color = new Color(0.08f, 0.08f, 0.1f);
            text.fontStyle = FontStyle.Bold;

            return go;
        }

        const string MaterialFolder = "Assets/Materials";

        // The actual root cause behind "looks pink": a Material created via `new Material(...)`
        // and assigned to a renderer is a loose, non-persistent object - PrefabUtility does NOT
        // automatically embed it as a sub-asset when the GameObject is saved as a prefab (unlike
        // an already-asset-backed reference, e.g. the Player's own Yellow.mat), so the renderer's
        // material slot silently serializes as null ({fileID: 0}) and Unity renders it with its
        // built-in missing-material fallback, which IS pink. Every dynamically-created 3D material
        // in this file needs to go through this to actually survive a save. (LevelGenerator.cs's
        // materials don't have this problem - they're created at Play-mode runtime, never need to
        // survive being written to disk.)
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

        // ==================== SlowPacedLevel ====================

        // Every environment piece in this scene is a ProBuilder cube of this exact size - direct
        // request: "1 Block should be 50% bigger than the player" (the player is a 1x1x1 cube).
        const float SlowPacedBlockSize = 1.5f;
        // Interior footprint is (2*this+1) = 27x27 blocks (40.5m) - doubled from the original 13
        // per direct request ("the room should be much bigger, like twice as big"), and kept ODD
        // so a 3-block-wide opening still sits exactly centered on a wall.
        const int SlowPacedHalfInterior = 13;
        const int SlowPacedWallLayers = 14;   // interior room height in blocks (21m, also doubled)
        const int SlowPacedHallHalfWidth = 1; // interior hallway columns: i in [-1..1] (3 wide)
        const int SlowPacedHallLayers = 3;    // hallway interior height in blocks (= opening height)
        // Hallway rows, derived so resizing the room shifts the whole corridor with it.
        const int SlowPacedHallStartK = SlowPacedHalfInterior + 2;             // first row past the wall ring
        const int SlowPacedVoidStartK = SlowPacedHallStartK + 4;               // 4 rows of solid entry floor first
        // 32 rows = 48m of gap - sized from a real-physics batch simulation of the launch
        // integrator (semi-implicit Euler with Unity's 1/(1+d*dt) damping, gravity -30, force
        // 45+65c, damping 2.8-1.8c): the BEST shot that fits under the hallway's 4.5m ceiling
        // (~10 degrees) covers 49.8m at charge 0.8, only 42.2m at 0.7, and 63.4m at full charge
        // - so the direct jump genuinely requires 80%+ of the energy meter (direct request),
        // with real margin at full charge and no way to cheat it with a steeper lob (those
        // crash into the hallway ceiling first). The sticky void walls remain the alternative
        // route for anyone crossing on less energy in stick-and-relaunch hops.
        const int SlowPacedVoidBlocks = 32;
        const int SlowPacedVoidEndK = SlowPacedVoidStartK + SlowPacedVoidBlocks - 1;
        const int SlowPacedFinishStartK = SlowPacedVoidEndK + 1;               // Finish platform floor rows
        const int SlowPacedHallEndK = SlowPacedFinishStartK + 3;               // end cap sits one past this

        // "A new scene that's called SlowPacedLevel" (direct request): a big open cube-shaped
        // room (floor, walls, ceiling) built entirely from ProBuilder blocks, one opening
        // centered in the +Z wall leading into an enclosed hallway with a void gap, and a
        // platform called Finish at the far end. Mixed is the only reachable control scheme.
        // Only walls carrying StickySurface (the back wall and the hallway walls along the void,
        // tinted green) hold a crash until the next launch - everything else clings for 0.3s and
        // then drops the cube back into gravity (KineticCubeController.stickyWallsOnly). Any
        // impact stamps a random crack decal from the 3x3 sheet (ImpactCrackDecals).
        // A standalone entry point (also under Tools) rather than only part of SetupAll, so it
        // can be (re)built without touching any other scene's saved state.
        [MenuItem("Tools/Kinetic Energy/Setup SlowPacedLevel")]
        public static void SetupSlowPacedLevel()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SlowPacedLevelScenePath) == null)
            {
                // Same reasoning as every other level - copying Sandbox Scene guarantees
                // identical RenderSettings/skybox/ambient instead of replicating them by hand.
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

            // Same duplicate-camera-name trap as every other level scene - both names destroyed
            // deliberately, see SetupLevel1's own comment for why.
            DestroyIfExists("Player");
            DestroyIfExists("Main Camera");
            DestroyIfExists("ThirdPersonCameraRig");
            DestroyIfExists("PauseSystem");
            DestroyIfExists("LevelGenerator");
            DestroyIfExists("Plane");
            // Copied in from Sandbox Scene - the room supplies its own floor and layout, and the
            // "head to the Parkour level" hint only makes sense in Sandbox Scene itself.
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

            // Same single-source-of-truth reasoning as every other level - this is a plain
            // instance of Player.prefab, not rebuilt from scratch here.
            ApplyLaunchTuning(controller);

            // SlowPaced-only overrides, scoped to just this scene's Player instance - direct
            // request: "the only control scheme that should be active in that scene should be
            // Mixed", and the sticky-walls rule ("only walls that have the property sticky you
            // should be able to stick onto until launching again... if this isn't the case, the
            // player should temporarily stick to it for say 0.3 seconds and then fall down").
            controller.SetControlScheme(ControlScheme.Mixed);
            controller.schemeSwitchingEnabled = false;
            controller.stickyWallsOnly = true;
            controller.nonStickyWallStickDuration = 0.3f;

            // 1.5x launch speed at (near-)unchanged distances (direct request: "increase the
            // speed at which you launch but don't change the distance") - force scaled 1.5x and
            // damping re-solved with the same batch integrator that sized the void, calibrated
            // at the 10-degree shallow regime this level's ceiling-capped hallway shots live in
            // (a linear-drag model can't preserve every angle at once; steeper in-room arcs land
            // slightly shorter at mid charges). Verified against the 48m void: the best
            // ceiling-legal shot covers 47.0m at charge 0.8 and 55.4m at 0.9, so the direct
            // crossing still demands 80%+ of the meter. The landing preview simulates with these
            // same instance fields, so the visual stays exact by construction.
            controller.minLaunchForce = 67.5f;
            controller.maxLaunchForce = 165f;
            controller.minLaunchDamping = 5.69f;
            controller.maxLaunchDamping = 2.15f;

            // Flat energy economy (direct request: "you just gain 1.2 times the energy you put
            // in a launch, no matter the speed and all") - the same spent*1.2 refund FastPaced
            // uses, enabled for this scene's instance only.
            controller.refundSpentEnergyOnly = true;
            controller.fastPacedRefundMultiplier = 1.2f;

            // FastPaced's cross-and-ring reticle at the predicted landing spot, alongside the
            // trail dots (direct request) - unlock the crosshair visuals on this instance and
            // start in the combined mode; Right Bumper toggles it off/on as a whole.
            controller.landingPreview.ghostAndCrosshairEnabled = true;
            controller.landingPreview.initialMode = PredictionMode.TrailAndCrosshair;

            // Crack-on-impact decals - an added component on THIS scene's Player instance only
            // (a per-instance override), so Player.prefab and every other scene stay untouched.
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

            // Appended (not UpdateBuildSettings, which rewrites the whole list) so running just
            // this entry point can't clobber whatever the build list currently holds - restart
            // and the pause menu's LoadSceneByName both need the scene present here.
            AddSceneToBuildSettings(SlowPacedLevelScenePath);

            Scene slowPacedScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(slowPacedScene);
            EditorSceneManager.SaveScene(slowPacedScene);
            AssetDatabase.SaveAssets();

            Debug.Log("KineticEnergySetup: SlowPacedLevel setup complete OK");
        }

        // The whole environment, block by block. Returns the empty the boot camera should face
        // (the opening into the hallway).
        static GameObject BuildSlowPacedRoom(Transform player, Transform cameraTransform, PauseController pauseController)
        {
            GameObject room = new GameObject("SlowPacedRoom");

            float b = SlowPacedBlockSize;
            int ring = SlowPacedHalfInterior + 1;
            int hallWallX = SlowPacedHallHalfWidth + 1;

            // Two shades per surface type, checkerboarded per block - with the project's unlit
            // flat-color look, alternating shades is what keeps individual blocks readable
            // (and grabbable) instead of walls rendering as one featureless plane. Green = sticky,
            // deliberately loud so the rule is learnable at a glance.
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

            // Room floor + ceiling - covering the wall ring's footprint too, so the walls stand
            // ON floor rather than beside it.
            for (int i = -ring; i <= ring; i++)
            {
                for (int k = -ring; k <= ring; k++)
                {
                    CreatePbBlock(floorGroup, $"Floor_x{i}_z{k}", SlowPacedFloorCenter(i, k), SlowPacedChecker(i + k) ? floorA : floorB);
                    CreatePbBlock(ceilingGroup, $"Ceiling_x{i}_z{k}", SlowPacedBlockCenter(i, SlowPacedWallLayers, k), SlowPacedChecker(i + k) ? ceilingA : ceilingB);
                }
            }

            // Wall ring. The back (-Z) wall is the room's sticky practice target; the front (+Z)
            // wall carries the opening into the hallway, centered at floor level and sized to
            // the hallway's own interior (3 wide, 3 high).
            for (int layer = 0; layer < SlowPacedWallLayers; layer++)
            {
                for (int i = -ring; i <= ring; i++)
                {
                    for (int k = -ring; k <= ring; k++)
                    {
                        if (Mathf.Max(Mathf.Abs(i), Mathf.Abs(k)) != ring) continue;

                        bool isFront = k == ring;
                        bool isBack = k == -ring;
                        if (isFront && Mathf.Abs(i) <= SlowPacedHallHalfWidth && layer < SlowPacedHallLayers) continue; // the opening

                        Transform group = isFront ? frontWall : isBack ? backWall : i > 0 ? rightWall : leftWall;
                        bool sticky = isBack;
                        bool checker = SlowPacedChecker(i + layer + k);
                        Material mat = sticky ? (checker ? stickyA : stickyB) : (checker ? wallA : wallB);
                        CreatePbBlock(group, $"Wall_x{i}_y{layer}_z{k}", SlowPacedBlockCenter(i, layer, k), mat);
                    }
                }
            }

            // Hallway: an enclosed corridor off the opening - solid entry floor, then the void
            // (no floor at all; falling past fallResetY reloads the scene), then the Finish
            // platform. The side walls along the void stretch are sticky - the intended way
            // across for anything short of a full-gap shot: launch in, stick, launch again.
            for (int k = SlowPacedHallStartK; k <= SlowPacedHallEndK; k++)
            {
                bool voidRow = k >= SlowPacedVoidStartK && k <= SlowPacedVoidEndK;
                bool finishRow = k >= SlowPacedFinishStartK;

                if (!voidRow)
                {
                    // Floor spans under the side walls too (i covers the walls' columns), so the
                    // corridor reads as a solid slab from outside rather than walls on stilts.
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

            // End cap sealing the hallway just past the Finish platform (one layer taller than
            // the walls so it also closes the ceiling row).
            for (int layer = 0; layer <= SlowPacedHallLayers; layer++)
            {
                for (int i = -hallWallX; i <= hallWallX; i++)
                {
                    CreatePbBlock(hallEndCap, $"HallwayEndCap_x{i}_y{layer}", SlowPacedBlockCenter(i, layer, SlowPacedHallEndK + 1), SlowPacedChecker(i + layer) ? wallA : wallB);
                }
            }

            // Finish extras: the translucent pad, the billboard label, and the win trigger -
            // same visual language and FinishLineWin flow as FastPacedLevel's finish.
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

            // "Add 3 cubes randomly in the middle where you can crash onto" - free-floating
            // crash targets in the room's central airspace, orange (the same "special target"
            // accent language as Level3's ledges). Not sticky, so the scene rule applies: crash,
            // refund energy, cling 0.3s, drop - i.e. these are how you farm the meter up toward
            // the 80% the void demands. Seeded placement (same idempotency reasoning as the
            // FastPaced spiral) with a minimum spacing so they never clump.
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
                // Freestanding, so a single exact BoxCollider is seam-free by construction -
                // unlike the wall/floor blocks, these carry their own collision.
                BoxCollider cubeCollider = cube.AddComponent<BoxCollider>();
                cubeCollider.center = Vector3.zero;
                cubeCollider.size = Vector3.one * crashCubeSize;
            }

            BuildSlowPacedColliders(room.transform);

            // Player starts at the room's center; the boot camera faces the opening.
            player.position = new Vector3(0f, 0.5f, 0f);

            GameObject lookTarget = new GameObject("OpeningLookTarget");
            lookTarget.transform.position = new Vector3(0f, SlowPacedHallLayers * 0.5f * b, (SlowPacedHalfInterior + 0.5f) * b);
            return lookTarget;
        }

        // Grid-to-world: wall/ceiling blocks stack in layers starting at the floor's TOP surface
        // (y=0); the floor itself is the layer below that.
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

        // One environment block: a ProBuilder cube (direct request - "use ProBuilder to make all
        // environments out of blocks so I can easily adjust things"). VISUAL ONLY - deliberately
        // no collider per block: hundreds of side-by-side colliders create internal seam edges
        // that PhysX reports as real contacts, which caught the cube while walking, registered
        // phantom crashes mid-launch, and spawned crack decals from merely standing (direct bug
        // report). Collision comes from a few merged, invisible box slabs instead - see
        // BuildSlowPacedColliders.
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

        // The scene's actual physics: one seam-free box collider per flat surface region, sized
        // to exactly match the block visuals' outer faces. StickySurface lives HERE (the
        // controller reads the collider it crashed into via GetComponentInParent), so a whole
        // slab is sticky or not - the green block tint is the matching visual. If blocks get
        // rearranged in the editor, these slabs are what needs resizing to match.
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

            float outerHalf = (ring + 0.5f) * b;              // room's outer extent from center (11.25)
            float outerSpan = outerHalf * 2f;                 // full outer width (22.5)
            float roomHeight = SlowPacedWallLayers * b;       // interior height (10.5)
            float wallCenterY = roomHeight * 0.5f;
            float wallZ = ring * b;                           // wall blocks' center plane (10.5)
            float hallHeight = SlowPacedHallLayers * b;       // 4.5
            float hallInnerWidth = (SlowPacedHallHalfWidth * 2 + 1) * b; // 4.5
            float hallOuterWidth = (hallWallX * 2 + 1) * b;   // 7.5
            float hallWallXPos = hallWallX * b;               // 3

            Transform colliders = NewGroup(room, "Colliders");

            // Room shell.
            CreateRoomCollider(colliders, "FloorCollider", new Vector3(0f, -b * 0.5f, 0f), new Vector3(outerSpan, b, outerSpan));
            CreateRoomCollider(colliders, "CeilingCollider", new Vector3(0f, roomHeight + b * 0.5f, 0f), new Vector3(outerSpan, b, outerSpan));
            CreateRoomCollider(colliders, "WallBackCollider", new Vector3(0f, wallCenterY, -wallZ), new Vector3(outerSpan, roomHeight, b), true);
            CreateRoomCollider(colliders, "WallLeftCollider", new Vector3(-wallZ, wallCenterY, 0f), new Vector3(b, roomHeight, outerSpan));
            CreateRoomCollider(colliders, "WallRightCollider", new Vector3(wallZ, wallCenterY, 0f), new Vector3(b, roomHeight, outerSpan));

            // Front wall in three pieces around the opening (3 wide, hallHeight tall, centered).
            float openingHalf = hallInnerWidth * 0.5f;                 // 2.25
            float sideWidth = outerHalf - openingHalf;                 // 9
            float sideCenterX = openingHalf + sideWidth * 0.5f;        // 6.75
            CreateRoomCollider(colliders, "WallFrontLeftCollider", new Vector3(-sideCenterX, wallCenterY, wallZ), new Vector3(sideWidth, roomHeight, b));
            CreateRoomCollider(colliders, "WallFrontRightCollider", new Vector3(sideCenterX, wallCenterY, wallZ), new Vector3(sideWidth, roomHeight, b));
            float aboveHeight = roomHeight - hallHeight;               // 6
            CreateRoomCollider(colliders, "WallFrontAboveOpeningCollider", new Vector3(0f, hallHeight + aboveHeight * 0.5f, wallZ), new Vector3(hallInnerWidth, aboveHeight, b));

            // Hallway. Row k's blocks span (k*b - b/2)..(k*b + b/2).
            float entryMinZ = SlowPacedHallStartK * b - b * 0.5f;
            // The entry FLOOR alone starts two blocks early (under the doorway, overlapping the
            // room floor with both tops flush at y=0) so walking through the opening crosses
            // solid overlap instead of a collider-to-collider seam edge - the exact catch this
            // merged-collider layout exists to remove. Floor only: extending the ceiling/wall
            // segments the same way would poke invisible collider stubs into the room itself.
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

            // Side walls in three segments each: only the stretch flanking the void is sticky,
            // matching the green blocks.
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

            // End cap sealing the hallway (walls + ceiling row tall).
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

        // ==================== Tutorial ====================

        // "A level called Tutorial" (direct request): three launch lessons in a straight line,
        // each platform a single adjustable ProBuilder slab with an exact BoxCollider (seam-free
        // by construction), ending at a wall with "Finish" written on it. Mixed is the only
        // scheme, with the SHARED default tuning and the default speed-based crash refunds -
        // "positioned well enough that you get enough energy" relies on faster/longer crashes
        // refunding more, which is exactly what the default economy does (the SlowPaced flat
        // 1.2x rule stays scoped to that scene). Hop distances come from the same batch
        // integrator used for SlowPacedLevel's void (default tuning, 30-degree grounded aim:
        // charge 0.2 -> 19.7m, 0.5 -> 34.0m; air forward neutral 5 degrees at 0.5 -> ~21m at
        // level height, ~38m falling 12m):
        //   1. Start -> Platform2: a 16m center-to-center gap - the starting 20% energy covers
        //      it with margin, and the landing refund (~35%) sets up lesson 2.
        //   2. Platform2 -> Platform3, 26m ahead and 12m DOWN: forward launch, then West's
        //      down-slam mid-air. The slam crash is fast, refilling most of the tank.
        //   3. Platform3 -> Finish, 58m ahead at the same height: forward (~45%), then a second
        //      forward mid-air at ~50% - a single max-charge shot tends to overfly the wall, so
        //      the taught chain is also the reliable one.
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

            // Mixed only, same as SlowPacedLevel - this course teaches exactly that scheme's
            // grounded aim, the West down-slam, and the mid-air forward relaunch.
            controller.SetControlScheme(ControlScheme.Mixed);
            controller.schemeSwitchingEnabled = false;
            // Nothing in this scene is sticky (direct request): sticky-walls-only mode with no
            // StickySurface anywhere means a wall/ceiling crash clings only briefly and drops
            // you; flat platforms keep the normal land-refund-walk-away behavior.
            controller.stickyWallsOnly = true;
            // "Only allowed to perform 2 launches before landing" (direct request) - matches
            // the lessons exactly (each hop is at most a two-launch chain); any landing or
            // crash restores the budget.
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

        // Returns the second platform (the first launch target) so the boot camera faces it.
        static GameObject BuildTutorialCourse(Transform player, Transform cameraTransform, PauseController pauseController)
        {
            GameObject course = new GameObject("TutorialCourse");

            Material platformMat = MakeSlowPacedMaterial("TutorialPlatformMaterial", new Color(0.50f, 0.55f, 0.62f));
            Material finishMat = MakeSlowPacedMaterial("TutorialFinishWallMaterial", new Color(0.95f, 0.75f, 0.15f));

            // Lesson 1: a plain gap. 16m center to center = ~10m of open air between the edges.
            GameObject start = CreateTutorialSlab(course.transform, "StartPlatform", new Vector3(0f, -0.75f, 0f), new Vector3(6f, 1.5f, 6f), platformMat);
            GameObject platform2 = CreateTutorialSlab(course.transform, "Platform2", new Vector3(0f, -0.75f, 16f), new Vector3(6f, 1.5f, 6f), platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, 3.2f, 0f),
                "1. Hold Left Trigger to aim, stick to adjust,\nRight Trigger to launch across the gap");

            // Lesson 2: 26m ahead, 12m down - forward launch, then West's down-slam mid-air.
            // Extra-large landing pad, since two-stage launches are new at this point.
            GameObject platform3 = CreateTutorialSlab(course.transform, "Platform3", new Vector3(0f, -12.75f, 42f), new Vector3(10f, 1.5f, 10f), platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, 3.2f, 16f),
                "2. Launch forward, then hold Left Trigger in the air\nto aim, hold West and release to slam down");

            // Lesson 3: an UP launch (direct request) - Platform4 sits 10m ahead of and 12m
            // ABOVE platform3 (back at the course's starting height). South's up-charge tilted
            // toward it (80 degrees when the stick is held) covers roughly 3-19m horizontal /
            // 9-61m vertical across the charge range, so a moderate charge lands it.
            CreateTutorialSlab(course.transform, "Platform4", new Vector3(0f, -0.75f, 52f), new Vector3(8f, 1.5f, 8f), platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, -8.8f, 42f),
                "3. Hold South and tilt the stick toward the high\nplatform, release to launch up onto it");

            // Lesson 4: 58m ahead of Platform4, one story below it - forward, then forward
            // again mid-air (hold Right Trigger, release at ~50% charge). The finish platform
            // runs into the Finish wall itself.
            Vector3 finishPlatformCenter = new Vector3(0f, -12.75f, 110f);
            Vector3 finishPlatformSize = new Vector3(10f, 1.5f, 14f);
            CreateTutorialSlab(course.transform, "FinishPlatform", finishPlatformCenter, finishPlatformSize, platformMat);
            BuildTutorialSign(course.transform, cameraTransform, new Vector3(0f, 3.2f, 52f),
                "4. Launch forward, then hold Left Trigger and Right\nTrigger in the air - release at about half charge");

            // The final platform IS a wall with "Finish" on it (direct request) - crashing into
            // it sticks (default any-surface stick applies here) and the trigger opens the win
            // screen, same FinishLineWin flow as the other finishes.
            float wallZ = finishPlatformCenter.z + finishPlatformSize.z * 0.5f - 0.75f; // flush with the platform's far edge
            Vector3 wallSize = new Vector3(12f, 9f, 1.5f);
            Vector3 wallCenter = new Vector3(0f, -12f + wallSize.y * 0.5f, wallZ);
            GameObject wall = CreateTutorialSlab(course.transform, "FinishWall", wallCenter, wallSize, finishMat);

            GameObject wallText = new GameObject("FinishWallText");
            wallText.transform.SetParent(wall.transform, true);
            // On the wall's approach face, nudged off it so the letters never z-fight the slab.
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

        // A single ProBuilder cuboid with one exact BoxCollider - still fully adjustable with
        // ProBuilder's tools, but seam-free physics by construction (one collider per platform).
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

        // Floating instruction text above a lesson's launch point, always turned toward the
        // camera - same TextMesh+Billboard language as every finish label in this project.
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

        // ==================== Launch Button prefab ====================

        // Builds Assets/Prefabs/LaunchButton.prefab (direct request): a flat socket rectangle
        // with a smaller, taller cap on top - launch onto the cap and it sinks back into the
        // socket, FLIPPING the active state of every object in the button's targets list
        // (assign those per placed instance). The cap carries NonStickSurface, so touching it
        // never crash-sticks the player. Creates ONLY the prefab asset and its materials - no
        // scene is modified or saved.
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
                // Socket: the flat base rectangle. Origin sits at its BOTTOM face, so a placed
                // button rests on whatever it's dropped onto at y-position 0.
                CreateTutorialSlab(root.transform, "Socket", new Vector3(0f, 0.2f, 0f), new Vector3(3f, 0.4f, 3f), socketMat);

                // Cap: less wide/deep, taller, resting on the socket's top - the actual button.
                GameObject cap = CreateTutorialSlab(root.transform, "ButtonCap", new Vector3(0f, 0.7f, 0f), new Vector3(2f, 0.6f, 2f), capMat);
                cap.AddComponent<NonStickSurface>();
                LaunchButtonCap capRelay = cap.AddComponent<LaunchButtonCap>();

                LaunchButton button = root.AddComponent<LaunchButton>();
                button.buttonCap = cap.transform;
                button.capRenderer = cap.GetComponent<MeshRenderer>();
                button.pressedMaterial = pressedMat;
                // Sinks until the cap's top sits just proud of the socket's (0.5 of its 0.6
                // height stays out of view).
                button.pressDepth = 0.5f;
                button.pressSpeed = 4f;
                button.stayPressed = true;
                // These two are intra-prefab references (same hierarchy), which a prefab asset
                // CAN hold - only cross-hierarchy references are forbidden. targetPlatform is
                // deliberately left null here for exactly that reason.
                capRelay.button = button;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/LaunchButton.prefab");
            }
            finally
            {
                // The temp build hierarchy must not survive in whatever scene happened to be
                // open - the prefab ASSET is the only output of this method.
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("KineticEnergySetup: LaunchButton prefab created OK");
        }

        // ==================== Tutorial3 (fast-paced air) ====================

        // The fast-paced-air tutorial - NAMED Tutorial3 since the 2<->3 rename swap (it was
        // born as Tutorial2): a copy of Tutorial whose midair controls are the FastPaced
        // Level's. Deliberately NOT a rebuild: the scene is a byte-for-byte COPY of
        // Tutorial.unity exactly as it currently stands, and this method then mutates ONLY the
        // copy's Player overrides (mixedFastPacedAir + the FastPaced input actions + the
        // reticle unlock) and the air-lesson sign TEXTS - no geometry, no transforms. Re-running
        // re-applies the overrides without re-copying, so later edits survive.
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

            // The FastPaced air flow's own inputs and reticle - grounded Mixed controls and
            // every other override (sticky rules, 2-launch limit, tuning) carry over from the
            // copy untouched.
            controller.mixedFastPacedAir = true;
            controller.fastPacedAimAction = FindActionReference("Player", "FastPacedAim");
            controller.fastPacedLaunchAction = FindActionReference("Player", "FastPacedLaunch");
            controller.landingPreview.ghostAndCrosshairEnabled = true;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(controller.landingPreview);

            // Re-word the two air-lesson signs for the new controls, wherever they sit. Matched
            // by distinctive phrases of their CURRENT text (the user has hand-edited these,
            // dropping the original lesson numbering) - anything unrecognized is left alone.
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

        // ==================== Playtest flow ====================

        // One-shot wiring for the two-control-scheme playtest build (direct request):
        //   MainMenu (boot) -> Tutorial -> TestLevel1 -> Tutorial2 -> TestLevel2 -> MainMenu
        // - Tutorial gets mouse+keyboard air controls (mouseAirControls) IN PLACE.
        // - Tutorial2's camera gets the +/-89 first-person aim pitch IN PLACE.
        // - Both tutorials' finish triggers swap FinishLineWin for FinishLineNextScene.
        // - TestLevel1/TestLevel2 are COPIES of Tutorial/Tutorial2 as they currently stand, so
        //   the player carries the exact same controls/variables - only the course is replaced
        //   with the floating-wall hop (BuildWallHopCourse).
        // - MainMenu is built fresh and put first in Build Settings.
        // Tutorial/Tutorial2 are only ever touched with component/flag changes here - never
        // geometry or transforms. Signs are deliberately NOT touched (the user updates those).
        // NOTE: re-running rebuilds the WallHopCourse in both test levels from the scripted
        // layout - fine until someone hand-arranges those two scenes.
        [MenuItem("Tools/Kinetic Energy/Setup Playtest Flow")]
        public static void SetupPlaytestFlow()
        {
            // 1. Tutorial in place: mouse air controls + finish chains onward.
            EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);
            KineticCubeController tutorialController = FindPlayerController("Tutorial");
            tutorialController.mouseAirControls = true;
            // The cross-and-ring landing marker for the slow-paced (Mixed) scheme too (direct
            // request) - same combined trail+reticle mode SlowPacedLevel already uses.
            tutorialController.landingPreview.ghostAndCrosshairEnabled = true;
            tutorialController.landingPreview.initialMode = PredictionMode.TrailAndCrosshair;
            EditorUtility.SetDirty(tutorialController);
            EditorUtility.SetDirty(tutorialController.landingPreview);
            ReplaceWinWithNextScene("TestLevel1");
            SaveActiveScene();

            // 2. TestLevel1: copy of Tutorial (identical player), wall-hop course.
            BuildTestLevel(TutorialScenePath, TestLevel1ScenePath, "Tutorial2");

            // 3+4. The grounded-air pair (Tutorial2/TestLevel2 since the 2<->3 rename swap) -
            // chain onward; their overrides are maintained by SetupGroundedAirScenes.
            EditorSceneManager.OpenScene(Tutorial2ScenePath, OpenSceneMode.Single);
            ReplaceWinWithNextScene("TestLevel2");
            SaveActiveScene();
            EditorSceneManager.OpenScene(TestLevel2ScenePath, OpenSceneMode.Single);
            ReplaceWinWithNextScene("Tutorial3");
            SaveActiveScene();

            // 5. Tutorial3 (fast-paced air) in place: +/-89 first-person aim pitch + finish
            // chains onward.
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

            // 6. TestLevel3: copy of Tutorial3 (identical player + camera), same wall-hop
            // course; its finish returns to the menu.
            BuildTestLevel(Tutorial3ScenePath, TestLevel3ScenePath, "MainMenu");

            // 5. The boot menu + build order.
            SetupMainMenu();
            EnsurePlaytestBuildSettings();

            Debug.Log("KineticEnergySetup: Playtest flow setup complete OK");
        }

        // Adds the "Aim: WASD / Aim: Mouse" toggle button to the pause menus of all four
        // playtest scenes (direct request - both control schemes' grounded aim supports it) -
        // a per-instance ADDITION on each scene's PauseSystem, so the shared prefab and every
        // other scene's menu stay untouched. In-place: nothing existing is moved or modified.
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

                // Warning shown beside the button only while Always Mouse is on (direct
                // request) - GroundedAimToggle toggles its visibility.
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

        // The grounded-air pair - NAMED Tutorial2/TestLevel2 since the 2<->3 rename swap:
        // duplicates of Tutorial/TestLevel1 - courses, signs, borders, player and all - whose
        // AIR aiming is the exact same flow as the GROUNDED aiming (airUsesGroundedAim).
        // Chained Tutorial2 -> TestLevel2 -> Tutorial3, and added to the build settings.
        // Copy-once: re-running only re-applies the overrides.
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

        // One-shot for the 2<->3 rename (direct request): swaps the Tutorial2/Tutorial3 and
        // TestLevel2/TestLevel3 scene ASSETS (GUID-preserving renames via a temp name), then
        // rewires the finish chain for the six-stop loop
        //   Tutorial -> TestLevel1 -> Tutorial2 -> TestLevel2 -> Tutorial3 -> TestLevel3 -> MainMenu
        // and refreshes the main menu (new intro text; the scene buttons load by name, so they
        // pick up the swapped scenes automatically).
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

        // The fast-paced two-scene build (direct request): MainMenu + Tutorial3 + TestLevel3
        // only. Same treatment as the four-scene build - trimmed build list, main menu
        // referencing just the pair (Start boots Tutorial3), pause menus listing just the pair
        // plus the Main Menu button, chain Tutorial3 -> TestLevel3 -> MainMenu. Run "Setup
        // Four-Scene Build Menus" to switch back to the other build configuration.
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

        // Shared per-scene pause-menu configuration for a build variant: hides the shared
        // prefab's legacy scene buttons (per-instance), clears any previous variant's buttons,
        // lists exactly the given scenes, adds the Main Menu button, and retargets the finish.
        // The energy-regulation build (direct request): MainMenu + the four EnergyRegulation
        // scenes, played in sequence with AUTOMATIC ENERGY LAST -
        //   MainMenu -> Circle Cranking -> Dedicated Buttons -> Reverse Direction ->
        //   Automatic Energy -> MainMenu
        // Same treatment as the other build variants: trimmed build list, main menu listing
        // exactly these four (Start boots the first), pause menus listing the four plus the
        // Main Menu button, and each finish chaining to the next stop. Run one of the other
        // "Setup ... Build Menus" items to switch build configurations.
        [MenuItem("Tools/Kinetic Energy/Setup Energy Regulation Build Menus")]
        public static void SetupEnergyRegulationBuildMenus()
        {
            const string energyFolder = "Assets/Scenes/EnergyRegulation/";
            // Order IS the playtest sequence - Automatic Energy deliberately last.
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
                // The last stop returns to the menu; every other one leads to the next.
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

        // Adds the stranded-player restart countdown to FastPacedLevel (direct request) - a
        // per-instance component on that scene's Player only. In-place: nothing else in the
        // scene is touched, and no other scene changes.
        [MenuItem("Tools/Kinetic Energy/Setup FastPacedLevel Restart Countdown")]
        public static void SetupFastPacedRestartCountdown()
        {
            EditorSceneManager.OpenScene(FastPacedLevelScenePath, OpenSceneMode.Single);

            KineticCubeController controller = FindPlayerController(FastPacedLevelScenePath);
            // Component added if missing; its numbers are NOT (re)assigned - the user tunes
            // them in the Inspector.
            OutOfEnergyRestart restart = controller.GetComponent<OutOfEnergyRestart>();
            if (restart == null) restart = controller.gameObject.AddComponent<OutOfEnergyRestart>();
            EditorUtility.SetDirty(restart);

            // Scene-scoped control tweaks (direct request) - no midair nudging, and either
            // stick aims.
            controller.disableAirNudge = true;
            controller.aimWithEitherStick = true;
            EditorUtility.SetDirty(controller);

            SaveActiveScene();
            Debug.Log("KineticEnergySetup: FastPacedLevel player setup complete OK");
        }

        // EnergyEconomy1 (direct request): the last-launch-based refund economy + the West
        // midair down-launch on the Player, and the energy meter divided into 10 equal cells
        // by white divider lines matching the meter's 3px border. All per-instance, in place -
        // the shared PauseSystem prefab and every other scene stay untouched.
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
            // WIRING flags only - NO numeric tunables are (re)assigned here anymore (direct
            // request: "do not change any values in the editor, I set them how I want them") -
            // groundPoundRefundMultiplier, groundPoundMinRefund and friends are the user's.
            controller.lastLaunchRefundEconomy = true;
            controller.westAirDownLaunch = true;
            // Keyboard: Space = up-launch, E = ground pound (the shared flag the keyboard
            // unions already key off).
            controller.mouseAirControls = true;
            EditorUtility.SetDirty(controller);

            // The finish trigger came in broken from the scene copy - one FinishLineNextScene
            // still pointed at the source scene's chain, another had an EMPTY scene name (a
            // silent no-op). Both now return to the menu, which is in every build.
            ReplaceWinWithNextScene("MainMenu");

            // The meter's 10 divider cells: 9 white lines, 3px wide like the border, laid over
            // the fill area (inset 3px on every side, so inner width = 320 - 6 = 314). Added
            // as the container's LAST child so they render on top of both fill bars.
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

            const float inset = 3f;        // meterOutlineThickness in BuildPauseSystem
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
                lineRt.sizeDelta = new Vector2(inset, -inset * 2f); // 3px wide, inset from top/bottom
                lineRt.anchoredPosition = new Vector2(inset + innerWidth * i / 10f, 0f);
                line.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f); // the Outline's own color
            }

            SaveActiveScene();
            Debug.Log("KineticEnergySetup: EnergyEconomy1 setup complete OK");
        }

        // Builds Assets/Prefabs/Target.prefab (direct request): a solid sphere that is
        // destroyed when the player launches into it. The crash is an ordinary one - same
        // energy rules as any platform - and you're left hanging where it was, free to launch
        // again or drop after the usual wall-cling. Creates ONLY the prefab asset and its
        // material; no scene is modified or saved.
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
                // Solid, NOT a trigger - the player has to actually crash into it for the
                // normal crash/energy handling to run.
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

        // EnergyEconomy3 (direct request): chained launches - the speed already carried is
        // added to the next launch, and the crash refund is calculated from the TOTAL energy
        // spent across the flight's launches. Wiring flag only; every tunable stays as set in
        // the Inspector. No other scene is touched.
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

        // EnergyEconomy4 (direct request): flat wash refunds, a boosted ground pound that pays
        // back the whole flight's spend, and the pound's free hop + slow-mo window with its
        // instant full charge. Wiring flag only - every tunable stays as set in the Inspector.
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

        // The four-scene build's pause menus (direct request): in each playtest scene, the
        // pause menu's Scenes panel lists ONLY the four build scenes (the shared prefab's old
        // Sandbox/Level buttons are deactivated per instance - the prefab itself is untouched),
        // and the pause panel gains a "Main Menu" button back to the start screen. Also caps
        // the finish chain at TestLevel2 -> MainMenu (Tutorial3 is out of the build), rebuilds
        // the main menu, sets the four-scene build list, and repositions the aim toggle below
        // the new button.
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

                // Back to the start screen straight from the pause panel (direct request).
                DestroyChildIfExists(pausePanel, "MainMenuButton");
                GameObject mainMenuBtn = CreateButton("MainMenuButton", pausePanel, "Main Menu", font, accent, new Vector2(0f, -265f), new Vector2(300f, 70f));
                WireSceneButton(mainMenuBtn, pauseController.LoadSceneByName, "MainMenu");

                // Hide the shared prefab's legacy scene buttons (per-instance override), then
                // add the four build scenes in their slots.
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

                // The chain ends inside the four-scene build: TestLevel2 returns to the menu.
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

        // The four EnergyRegulation scenes (user-made duplicates of the fast-paced tutorial):
        // tags each scene's Player instance with its EnergyControlMode, adds the crank UI
        // component where needed, and builds the PositioningObject prefab. In-place - nothing
        // else in the scenes is touched, and no other scene changes at all (Standard mode is
        // the default everywhere).
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

        // The Automatic Energy mode's aim-target sphere (direct request): blue, a bit
        // transparent, trigger collider so the player passes straight through, and carrying
        // PositioningTarget so the auto-aim ray accepts it as a landing location. Prefab asset
        // only - place instances wherever you like.
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

        // Swaps every win-screen finish in the open scene for the next-scene chain trigger (and
        // retargets any already-swapped one), leaving the trigger object itself - position,
        // collider, everything - untouched.
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
            // Both schemes show the full trail+reticle preview (direct request) - the copies
            // predate this flag on their sources, so it's (re)applied here.
            KineticCubeController testController = FindPlayerController(destScenePath);
            testController.landingPreview.ghostAndCrosshairEnabled = true;
            testController.landingPreview.initialMode = PredictionMode.TrailAndCrosshair;
            EditorUtility.SetDirty(testController.landingPreview);
            SaveActiveScene();
            AddSceneToBuildSettings(destScenePath);
        }

        // The floating-wall level from the sketches: a safe start platform, three vertical
        // walls floating over the void (staggered left/right and climbing, so the path zigzags
        // wall to wall exactly like the arrows in the drawing), and a safe end platform with
        // the finish. Walls are thin along the travel axis so their broad face is the crash
        // target; the scene rules come from whichever tutorial the level was copied from
        // (brief cling + 2-launch budget - each wall crash refunds energy and resets the
        // budget, so every wall is a fresh start).
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

            Vector3 endCenter = new Vector3(0f, 13.25f, 62f); // top face at y=14
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

        // The boot menu scene: title, the two-control-schemes note, and Start / Feedback /
        // Scenes / Quit - the pause menu's visual language with Restart/Resume replaced by
        // Start (direct request). The Feedback button opens MainMenuController.feedbackUrl,
        // which the user fills in with their form link.
        public static void SetupMainMenu()
        {
            // Default: the four-scene build's menu.
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

            // The rebuild below replaces MainMenuUI wholesale - carry the user-entered
            // feedback link across so re-running this never silently drops it.
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
            // Gamepad navigation (direct request) - focus these when their panel opens, so
            // Dpad/stick + Submit work exactly like the pause menu's.
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

        // The build contains EXACTLY the four playtest scenes plus the boot menu (direct
        // request) - every other scene stays in the project but out of the build.
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

        // ==================== Breakable crack wall prefab ====================

        // Builds Assets/Prefabs/BreakableCrackWall.prefab (direct request): a wall rotated 90
        // degrees to lie flat, light beige, with a big version of the crash-decal crack drawn
        // across its top - smashable only by launching downwards through it (see
        // BreakableCrackWall / KineticCubeController's breakable checks). Creates ONLY the
        // prefab asset and its materials - no scene is modified or saved.
        [MenuItem("Tools/Kinetic Energy/Create Breakable Crack Wall Prefab")]
        public static void CreateBreakableCrackWallPrefab()
        {
            Material paneMat = MakeSlowPacedMaterial("BreakableWallMaterial", new Color(0.88f, 0.82f, 0.70f));

            // The big crack face: the SAME processed sheet the impact decals use, windowed to
            // its center cell via the material's own tiling/offset (a persistent material, not
            // a MaterialPropertyBlock, which wouldn't survive being saved into a prefab).
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
                // The pane itself: a thin flat slab (origin at its bottom face), solid via one
                // exact BoxCollider.
                CreateTutorialSlab(root.transform, "Pane", new Vector3(0f, 0.15f, 0f), new Vector3(4f, 0.3f, 4f), paneMat);
                root.AddComponent<BreakableCrackWall>();

                // The big crack, laid flat just above the top face (the material is
                // double-sided, so orientation can never cull it away).
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

        // ==================== In-place shadow / sign refresh ====================

        // Applies the shadows-back-on change (and the reworded tutorial signs) to the EXISTING
        // scenes and prefabs IN PLACE - deliberately NOT by re-running the scene builders, which
        // rebuild geometry at scripted positions and would wipe the user's hand-placed object
        // arrangements (explicit instruction: "Do not change any of the objects transforms in
        // the scene"). This touches only: the Player prefab's renderer shadow flags, the URP
        // assets' shadow distance, and per scene the directional light's shadow mode, the
        // PlayerShadow disc's casting flag, and TutorialSign text. No transform is ever written.
        [MenuItem("Tools/Kinetic Energy/Refresh Shadows In All Scenes")]
        public static void RefreshShadows()
        {
            // Player prefab: no real-time shadow from any of its renderers - the drop-disc is
            // the player's only shadow.
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PrefabFolder + "/Player.prefab");
            foreach (Renderer childRenderer in playerRoot.GetComponentsInChildren<Renderer>(true))
            {
                childRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            PrefabUtility.SaveAsPrefabAsset(playerRoot, PrefabFolder + "/Player.prefab");
            PrefabUtility.UnloadPrefabContents(playerRoot);

            // The default 50m shadow distance fades everything out well inside the doubled
            // room; 150 covers the biggest sightlines.
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

        // Updates the tutorial signs' TEXT in place to describe the new LT-gated air aiming,
        // matched by their lesson-number prefix so it works wherever the user has moved them.
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

        // ==================== Crack decal sheet ====================

        // Re-runnable from the menu on its own so swapping in real art is one step: overwrite
        // CrackDecalSheetSource.png with any 3x3 crack sheet (white background or transparent,
        // both fine), run this, done - the material keeps pointing at the reprocessed texture.
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
            // Double-sided - a flat decal quad must never be culled away by its winding relative
            // to the surface it was stamped on.
            mat.SetFloat("_Cull", 0f);
            mat.SetTexture("_BaseMap", processed);
            return SaveMaterialAsset(mat, "CrackDecalMaterial");
        }

        // Only used when no source sheet exists yet: draws a 3x3 sheet of angular gray cracks
        // (dark jagged branches radiating from a center, in the reference art's style) straight
        // into pixel data. ProcessCrackSheet treats this exactly like a user-supplied PNG.
        static void GenerateProceduralCrackSheet()
        {
            const int cellSize = 300;
            const int sheetSize = cellSize * 3;
            Color32[] pixels = new Color32[sheetSize * sheetSize]; // starts fully transparent

            // Seeded, same idempotency reasoning as the FastPaced spiral - re-running produces
            // the identical sheet instead of churning the texture on every rebuild.
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
            const int margin = 6; // keep every crack safely inside its own cell of the atlas
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

        // Turns whatever sheet sits at CrackSourcePath into the decal-ready texture: alpha is
        // keyed off luminance (dark pixels stay, light pixels turn transparent), which handles
        // BOTH a white-background reference PNG (background and pale watermarking key out) and
        // an already-transparent sheet (its own alpha is preserved, scaled by the same key).
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
            // Mip levels would average neighboring cells of the 3x3 atlas into each other at a
            // distance; decals are viewed close up, so no mips is both correct and artifact-free.
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
