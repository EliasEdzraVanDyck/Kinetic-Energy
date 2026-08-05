using System;
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
        const string OldScenePath = "Assets/Scenes/SampleScene.unity";
        const string ScenePath = "Assets/Scenes/Sandbox Scene.unity";
        const string Level1ScenePath = "Assets/Scenes/Level1.unity";
        const string Level2ScenePath = "Assets/Scenes/Level2.unity";
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
        };

        public static void SetupAll()
        {
            RenameSandboxSceneIfNeeded();
            Setup();
            SetupLevel1();
            SetupLevel2();
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
            // Direct request: disable every lighting-generated shadow across all levels (current
            // and future - this single method is shared by every scene's setup) - NOT the
            // separate PlayerShadow drop-shadow, which is a plain unlit mesh positioned by its
            // own script, not a real-time shadow, and is completely unaffected by this.
            light.shadows = LightShadows.None;
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
            controller.energyCostPerFullCharge = 0.1f;
            controller.energyGainPerSpeed = 0.03f;
            controller.energyGainSpeedBonus = 0.01f;
            controller.chargeAccumulationRate = 0.3f;

            // Defy Gravity scheme tuning.
            controller.minDefyGravityDuration = 0.4f;
            controller.maxDefyGravityDuration = 1.5f;
            controller.minDefyGravitySpeed = 10f;
            controller.maxDefyGravitySpeed = 35f;
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
                new EditorBuildSettingsScene(Level2ScenePath, true)
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
            orbitCam.minPitch = -20f;
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
            image.color = color;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;

            return image;
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
    }
}
