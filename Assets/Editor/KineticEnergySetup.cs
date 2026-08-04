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
        };

        public static void SetupAll()
        {
            RenameSandboxSceneIfNeeded();
            Setup();
            SetupLevel1();
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

            GameObject player = GameObject.Find("Player");
            if (player == null) throw new Exception("KineticEnergySetup: could not find 'Player' GameObject in scene.");

            GameObject mainCamGo = GameObject.Find("Main Camera");
            if (mainCamGo == null) throw new Exception("KineticEnergySetup: could not find 'Main Camera' GameObject in scene.");

            KineticCubeController controller = BuildPlayerCube(player, moveRef, launchRef, fireRef, selectGhostRef, selectTrailRef, selectCrosshairRef, selectNoneRef, switchSchemeRef,
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
            freeMoveController.cameraTransform = mainCamGo.transform;
            orbitCam.target = player.transform;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);

            Text previewModeLabel = BuildPauseSystem(pauseRef);

            // PauseSystem is its own prefab, saved inside BuildPauseSystem - same cross-hierarchy
            // rule applies, so this wiring happens on the scene instances, after both are saved.
            controller.landingPreview.modeLabel = previewModeLabel;
            EditorUtility.SetDirty(controller.landingPreview);

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
            freeMoveController.cameraTransform = camGo.transform;
            orbitCam.target = playerGo.transform;

            Text modeLabel = pauseGo.transform.Find("PauseCanvas/PreviewModeLabel")?.GetComponent<Text>();
            controller.landingPreview.modeLabel = modeLabel;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(freeMoveController);
            EditorUtility.SetDirty(orbitCam);
            EditorUtility.SetDirty(controller.landingPreview);

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

        static void BuildDirectionalLight()
        {
            if (GameObject.Find("Directional Light") != null) return;

            GameObject lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.shadows = LightShadows.Soft;
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

        static void DestroyIfExists(string name)
        {
            // Loop, not a single Find+Destroy - GameObject.Find only ever returns ONE match,
            // so if duplicates ever accumulate (as happened with the camera rig - 4 instances
            // found in Level1.unity, while Player/PauseSystem/LevelGenerator each stayed at a
            // correct 1), a single-shot destroy silently leaves the rest behind and the count
            // never actually goes back to zero on a re-run. This converges to zero regardless
            // of however many are actually present.
            int destroyed = 0;
            GameObject go;
            while ((go = GameObject.Find(name)) != null)
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

        static void UpdateBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(Level1ScenePath, true)
            };
        }

        static KineticCubeController BuildPlayerCube(GameObject player, InputActionReference moveRef, InputActionReference launchRef, InputActionReference fireRef,
            InputActionReference selectGhostRef, InputActionReference selectTrailRef, InputActionReference selectCrosshairRef, InputActionReference selectNoneRef,
            InputActionReference switchSchemeRef,
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
            controller.minLaunchForce = 8.6f;
            controller.maxLaunchForce = 40f;
            controller.minLaunchDamping = 1.9f;
            controller.maxLaunchDamping = 0.65f;
            controller.maxChargeTime = 1.5f;
            controller.aimDeadzone = 0.15f;
            controller.aimRotationSpeed = 90f;
            controller.minAimPitch = -80f;
            controller.maxAimPitch = 80f;
            controller.defaultAimPitch = 20f;
            controller.groundNormalDot = 0.5f;
            controller.maxPredictionSteps = 3000;
            controller.previewLineHeight = 0.65f;
            controller.restVelocityThreshold = 0.05f;
            controller.restConfirmDuration = 0.1f;
            controller.groundCheckDistance = 0.6f;
            controller.fallResetY = -30f;
            controller.launchGraceDuration = 0.15f;
            controller.minLaunchClearDistance = 2f;
            controller.moveAction = moveRef;
            controller.launchAction = launchRef;
            controller.fireAction = fireRef;
            controller.selectClassicSchemeAction = selectGhostRef;
            controller.selectHoldReleaseSchemeAction = selectTrailRef;
            controller.selectAnalogSchemeAction = selectCrosshairRef;
            controller.selectNoneAction = selectNoneRef;
            controller.switchSchemeAction = switchSchemeRef;
            controller.aimArrow = BuildAimArrow(player.transform);
            controller.landingPreview = BuildLandingPreview(player.transform);

            // Hold-Release and Analog kept in the project, not removed, but not selectable for
            // now - Launch Instantly is the only reachable scheme (see HandlePreviewModeSwitch).
            controller.alternateSchemesEnabled = false;
            controller.stickAimForce = 24f;
            controller.stickAimDamping = 1.2f;
            controller.stickAimUpAngle = 80f;
            controller.stickAimForwardAngle = 30f;

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
            freeMoveController.airControlAcceleration = 3f;
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
            rt.anchoredPosition = new Vector2(-24f, -24f);
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
            return orbitCam;
        }

        static Text BuildPauseSystem(InputActionReference pauseRef)
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
            previewModeLabel.fontSize = 22;
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
            hintRt.sizeDelta = new Vector2(460f, 220f);
            Text hintText = hintGo.AddComponent<Text>();
            hintText.font = font;
            hintText.fontSize = 20;
            hintText.alignment = TextAnchor.UpperLeft;
            hintText.color = new Color(1f, 1f, 1f, 0.9f);
            hintText.text =
                "Move: Left Stick\n" +
                "Aim: Left Trigger (hold)\n" +
                "Adjust Aim: Left Stick (while aiming)\n" +
                "Launch: Right Trigger\n" +
                "Camera: Right Stick\n" +
                "Toggle Preview: South\n" +
                "Pause: Start / Options / Esc";
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
            Text controlsBody = CreateText("ControlsBody", controlsPanel.transform, "", font, 26, new Vector2(0f, 50f), new Vector2(760f, 260f));
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
            // Explicitly (re)assigned, same reason as every KineticCubeController tunable above -
            // a serialized value from a previous Setup() run would otherwise keep showing the old
            // charge-and-launch instructions even after this default string changes in code.
            controller.controlsText =
                "Left Stick - Move (on the ground, while not aiming)\n" +
                "Left Stick (in the air) - Nudge distance / drift sideways\n" +
                "Left Trigger - Aim (hold; the cube stays put)\n" +
                "Left Stick (while aiming) - Adjust aim direction\n" +
                "Right Trigger - Launch\n" +
                "South - Show/hide the landing preview\n" +
                "Right Stick - Camera\n" +
                "Start / Options / Esc - Pause\n\n" +
                "(Hold-Release and Analog launch schemes are still in the project, just disabled)";

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

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabFolder + "/PauseSystem.prefab", InteractionMode.AutomatedAction);

            return previewModeLabel;
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
