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

namespace KineticEnergy.EditorSetup
{
    public static class KineticEnergySetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string PrefabFolder = "Assets/Prefabs";

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

            GameObject player = GameObject.Find("Player");
            if (player == null) throw new Exception("KineticEnergySetup: could not find 'Player' GameObject in scene.");

            GameObject mainCamGo = GameObject.Find("Main Camera");
            if (mainCamGo == null) throw new Exception("KineticEnergySetup: could not find 'Main Camera' GameObject in scene.");

            KineticCubeController controller = BuildPlayerCube(player, moveRef, launchRef, fireRef, selectGhostRef, selectTrailRef, selectCrosshairRef, selectNoneRef);
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
            orbitCam.target = player.transform;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(orbitCam);

            Text previewModeLabel = BuildPauseSystem(pauseRef);

            // PauseSystem is its own prefab, saved inside BuildPauseSystem - same cross-hierarchy
            // rule applies, so this wiring happens on the scene instances, after both are saved.
            controller.landingPreview.modeLabel = previewModeLabel;
            EditorUtility.SetDirty(controller.landingPreview);

            Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("KineticEnergySetup: setup complete OK");
        }

        static KineticCubeController BuildPlayerCube(GameObject player, InputActionReference moveRef, InputActionReference launchRef, InputActionReference fireRef,
            InputActionReference selectGhostRef, InputActionReference selectTrailRef, InputActionReference selectCrosshairRef, InputActionReference selectNoneRef)
        {
            SphereCollider oldCollider = player.GetComponent<SphereCollider>();
            if (oldCollider != null) UnityEngine.Object.DestroyImmediate(oldCollider);

            MeshFilter meshFilter = player.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            if (player.GetComponent<BoxCollider>() == null)
            {
                player.AddComponent<BoxCollider>();
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
            controller.minLaunchForce = 8f;
            controller.maxLaunchForce = 40f;
            controller.maxChargeTime = 1.5f;
            controller.aimDeadzone = 0.15f;
            controller.aimRotationSpeed = 90f;
            controller.minAimPitch = -80f;
            controller.maxAimPitch = 80f;
            controller.groundNormalDot = 0.5f;
            controller.groundLevel = 0f;
            controller.maxPredictionSteps = 3000;
            controller.previewLineHeight = 0.65f;
            controller.restVelocityThreshold = 0.05f;
            controller.groundCheckDistance = 0.6f;
            controller.moveAction = moveRef;
            controller.launchAction = launchRef;
            controller.fireAction = fireRef;
            controller.selectGhostAction = selectGhostRef;
            controller.selectTrailAction = selectTrailRef;
            controller.selectCrosshairAction = selectCrosshairRef;
            controller.selectNoneAction = selectNoneRef;
            controller.aimArrow = BuildAimArrow(player.transform);
            controller.landingPreview = BuildLandingPreview(player.transform);

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

            Material solidMat = new Material(FindBestShader());
            solidMat.color = new Color(0.4f, 0.9f, 1f, 1f);

            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "GhostCube";
            ghost.transform.SetParent(root.transform, false);
            UnityEngine.Object.DestroyImmediate(ghost.GetComponent<Collider>());
            ghost.GetComponent<Renderer>().sharedMaterial = ghostMat;

            GameObject trail = new GameObject("Trail");
            trail.transform.SetParent(root.transform, false);
            Transform[] dots = new Transform[14];
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
            preview.ghostGroundOffset = 0.5f; // half the Player cube's height, so it sits ON the floor, not embedded in it
            preview.trailGroup = trail;
            preview.crosshairGroup = crosshair;
            preview.trailDots = dots;

            return preview;
        }

        // URP Lit/Unlit default to an Opaque surface - alpha is ignored unless the material is
        // explicitly switched to Transparent via these properties/keywords/render queue (there's
        // no single "make transparent" call). This is the one visual detail I can't confirm
        // without Play-mode access - worth a look once this actually runs in the Editor.
        static void MakeTransparent(Material mat, float alpha)
        {
            Color c = mat.color;
            c.a = alpha;
            mat.color = c;

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
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
            DestroyChildIfExists(canvasGo.transform, "PreviewModeLabel");

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

            GameObject pausePanel = CreatePanel("PausePanel", canvasGo.transform, backdrop);
            CreateText("Title", pausePanel.transform, "PAUSED", font, 48, new Vector2(0f, 160f), new Vector2(600f, 80f));
            GameObject restartBtn = CreateButton("RestartButton", pausePanel.transform, "Restart", font, accent, new Vector2(0f, 50f), new Vector2(300f, 70f));
            GameObject controlsBtn = CreateButton("ControlsButton", pausePanel.transform, "Controls", font, accent, new Vector2(0f, -40f), new Vector2(300f, 70f));
            GameObject quitBtn = CreateButton("QuitButton", pausePanel.transform, "Quit", font, accent, new Vector2(0f, -130f), new Vector2(300f, 70f));

            GameObject controlsPanel = CreatePanel("ControlsPanel", canvasGo.transform, backdrop);
            CreateText("ControlsTitle", controlsPanel.transform, "CONTROLS", font, 48, new Vector2(0f, 220f), new Vector2(600f, 80f));
            Text controlsBody = CreateText("ControlsBody", controlsPanel.transform, "", font, 26, new Vector2(0f, 50f), new Vector2(760f, 260f));
            controlsBody.alignment = TextAnchor.MiddleLeft;
            GameObject backBtn = CreateButton("BackButton", controlsPanel.transform, "Back", font, accent, new Vector2(0f, -170f), new Vector2(300f, 70f));

            pausePanel.SetActive(false);
            controlsPanel.SetActive(false);

            GameObject controllerGo = FindOrCreateChild(root.transform, "PauseController");
            PauseController controller = controllerGo.GetComponent<PauseController>();
            if (controller == null) controller = controllerGo.AddComponent<PauseController>();

            controller.pauseAction = pauseRef;
            controller.pausePanel = pausePanel;
            controller.controlsPanel = controlsPanel;
            controller.firstPauseButton = restartBtn;
            controller.firstControlsButton = backBtn;
            controller.controlsBodyText = controlsBody;

            // Persistent listeners only: Button.onClick.AddListener() from an Editor script
            // registers a runtime-only delegate that is NOT serialized, so it would silently
            // vanish the moment this prefab/scene is reloaded - UnityEventTools.AddPersistentListener
            // is the programmatic equivalent of dragging a method into the onClick list by hand.
            WireButton(restartBtn, controller.OnRestartClicked);
            WireButton(controlsBtn, controller.OnControlsClicked);
            WireButton(quitBtn, controller.OnQuitClicked);
            WireButton(backBtn, controller.OnControlsBackClicked);

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
