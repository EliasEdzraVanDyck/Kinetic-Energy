using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace KineticEnergy.Player
{
    public enum ControlScheme
    {
        LaunchInstantly, // West: LT aims+charges over time together, RT press = instant launch (the original system)
        HoldRelease,     // North: LT aims only, RT held charges over time, RT release = launch
        AnalogPressure   // East: LT aims only, charge directly tracks RT's analog pressure, RT release = launch
    }

    [RequireComponent(typeof(Rigidbody))]
    public class KineticCubeController : MonoBehaviour
    {
        [Header("Launch Force")]
        public float minLaunchForce = 8.6f;
        public float maxLaunchForce = 40f;
        public float maxChargeTime = 1.5f;

        // Exit speed went up (minLaunchForce/maxLaunchForce raised from the previous 6/28) for a
        // punchier-feeling launch, but a faster exit speed alone would also fly further - linear
        // drag isn't a fixed fraction of distance, it eats proportionally MORE of a slow shot's
        // range than a fast one's, so a single constant damping value can't keep both ends of the
        // charge range landing where they used to. Verified empirically (not guessed) with a
        // temporary real-physics batch simulation at a representative 30-degree launch angle:
        // matching the OLD min-force(6)/damping(0.25) baseline distance (~2.84) at the NEW,
        // scaled-up min force (~8.6) needed damping ~1.9, while matching the OLD max-force(28)
        // baseline (~46.0) at the new max force (40) needed only ~0.65. Interpolated by charge
        // fraction at launch time (same curve minLaunchForce/maxLaunchForce already use) so both
        // ends of the charge range land close to their old distances despite the higher exit speed.
        public float minLaunchDamping = 1.9f;
        public float maxLaunchDamping = 0.65f;

        [Header("Aiming")]
        [Range(0f, 1f)] public float aimDeadzone = 0.15f;
        public float aimRotationSpeed = 90f;
        public float minAimPitch = -80f;
        public float maxAimPitch = 80f;
        public float defaultAimPitch = 20f;
        public Transform cameraTransform;
        public AimArrowIndicator aimArrow;

        [Header("Landing")]
        [Range(0f, 1f)] public float groundNormalDot = 0.5f;
        public int maxPredictionSteps = 3000;
        public float previewLineHeight = 0.65f;
        public float restVelocityThreshold = 0.05f;
        public float groundCheckDistance = 0.6f;
        public LandingPreviewController landingPreview;

        [Header("Fall Reset")]
        public float fallResetY = -30f;

        [Header("Launch Grace")]
        // A large impulse applied while still technically touching the launch platform can make
        // PhysX re-report that same, continuous contact as a fresh OnCollisionEnter the instant
        // velocity changes - without this, that would immediately zero the launch it just fired,
        // reading as "moves a tiny distance, then falls". No real landing is physically possible
        // this soon after firing at any of this game's launch speeds, so any ground contact
        // inside this window is necessarily spurious and safe to ignore outright.
        public float launchGraceDuration = 0.15f;

        [Header("Input")]
        public InputActionReference moveAction;
        public InputActionReference launchAction;
        public InputActionReference fireAction;
        // Bound to the same West/North/East gamepad buttons as the old SelectGhostPreview/
        // SelectTrailPreview/SelectCrosshairPreview actions (unrenamed in the .inputactions asset
        // itself - purely a labeling mismatch, not a functional one) - repurposed here to select
        // the control scheme instead of the visual preview mode, since Ghost/Crosshair preview
        // modes are currently disabled anyway (see LandingPreviewController.ghostAndCrosshairEnabled).
        public InputActionReference selectClassicSchemeAction;
        public InputActionReference selectHoldReleaseSchemeAction;
        public InputActionReference selectAnalogSchemeAction;
        public InputActionReference selectNoneAction;

        Rigidbody rb;
        BoxCollider boxCollider;
        bool isAiming;
        bool waitingForLtRelease;
        bool hasLaunched;
        bool isGrounded;
        float chargeTime;
        float aimYaw;
        float aimPitch;
        ControlScheme controlScheme = ControlScheme.LaunchInstantly;
        float lastRtAnalogValue;

        bool launchQueued;
        Vector3 queuedDirection;
        float queuedForce;
        float queuedDamping;
        float launchGraceTimer;

        Vector3[] trajectoryBuffer;

        Vector3 lastPredictedLanding;
        bool hasPredictedLanding;

        GameObject predictionClone;
        Rigidbody predictionRb;
        Scene predictionScene;
        PhysicsScene predictionPhysicsScene;
        bool predictionSceneReady;
        static int predictionSceneCounter;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            boxCollider = GetComponent<BoxCollider>();
            trajectoryBuffer = new Vector3[Mathf.Max(maxPredictionSteps, 1)];
        }

        void Start()
        {
            UpdateSchemeLabel();
        }

        void OnDestroy()
        {
            if (predictionClone != null) Destroy(predictionClone);
            if (predictionSceneReady && predictionScene.IsValid()) SceneManager.UnloadSceneAsync(predictionScene);
        }

        void OnEnable()
        {
            moveAction?.action?.Enable();
            launchAction?.action?.Enable();
            fireAction?.action?.Enable();
            selectClassicSchemeAction?.action?.Enable();
            selectHoldReleaseSchemeAction?.action?.Enable();
            selectAnalogSchemeAction?.action?.Enable();
            selectNoneAction?.action?.Enable();
        }

        void OnDisable()
        {
            moveAction?.action?.Disable();
            launchAction?.action?.Disable();
            fireAction?.action?.Disable();
            selectClassicSchemeAction?.action?.Disable();
            selectHoldReleaseSchemeAction?.action?.Disable();
            selectAnalogSchemeAction?.action?.Disable();
            selectNoneAction?.action?.Disable();
        }

        void Update()
        {
            // Time.timeScale freezes deltaTime-scaled logic (like charge accumulation) for free,
            // but not this raw edge-detected input - without this guard, aiming/firing could
            // still start or complete while the pause menu is up.
            if (Time.timeScale <= 0f) return;

            if (transform.position.y < fallResetY)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            HandlePreviewModeSwitch();

            // Only one launch allowed per landing (hasLaunched), AND launching only ever starts
            // from a currently-grounded state (isGrounded, the same real-time raycast check
            // FixedUpdate uses) - checking both directly here, rather than trusting hasLaunched
            // alone to have been reset at the right moment, is what actually guarantees you can
            // never begin aiming/firing while airborne.
            bool ltHeld = !hasLaunched && isGrounded && launchAction != null && launchAction.action != null && launchAction.action.IsPressed();

            // One-shot-per-hold: once a launch fires, LT must be fully released before it can gate another.
            if (waitingForLtRelease)
            {
                if (!ltHeld) waitingForLtRelease = false;
                return;
            }

            if (ltHeld)
            {
                if (!isAiming)
                {
                    isAiming = true;
                    chargeTime = 0f;
                    lastRtAnalogValue = 0f;
                    SeedAimFromCamera();
                    aimArrow?.SetVisible(true);
                    landingPreview?.SetVisible(true);
                }

                bool rtHeld = fireAction != null && fireAction.action != null && fireAction.action.IsPressed();
                bool rtPressed = fireAction != null && fireAction.action != null && fireAction.action.WasPressedThisFrame();
                bool rtReleased = fireAction != null && fireAction.action != null && fireAction.action.WasReleasedThisFrame();
                float rtAnalogValue = fireAction != null && fireAction.action != null ? fireAction.action.ReadValue<float>() : 0f;

                bool launchNow;

                switch (controlScheme)
                {
                    case ControlScheme.LaunchInstantly:
                        // The original system: LT alone both aims and charges over time for as
                        // long as it's held. RT is a single instant-fire press using whatever
                        // charge has built up so far.
                        chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);
                        launchNow = rtPressed;
                        break;

                    case ControlScheme.AnalogPressure:
                        // LT only aims. Charge directly tracks how hard RT is CURRENTLY pressed
                        // (no ramp-up time) rather than building up over time, so the arrow/power
                        // preview responds live to trigger pressure. lastRtAnalogValue (this
                        // frame's reading, captured at the end of this block for use next frame)
                        // is used instead of this frame's own rtAnalogValue when computing the
                        // launch, because on the exact frame WasReleasedThisFrame fires, IsPressed
                        // has already gone false and the raw analog value can already be partway
                        // through the trigger's physical return to rest - using the prior frame's
                        // value (while it was still genuinely held) reflects the power level the
                        // player actually intended to release at.
                        chargeTime = Mathf.Clamp01(rtHeld ? rtAnalogValue : lastRtAnalogValue) * maxChargeTime;
                        launchNow = rtReleased;
                        break;

                    default: // HoldRelease
                        // LT only aims. RT is a separate hold-to-charge/release-to-launch
                        // trigger: charge builds over time only while RT is held, and firing
                        // happens on release, using whatever charge had accumulated by then.
                        if (rtHeld) chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);
                        launchNow = rtReleased;
                        break;
                }

                lastRtAnalogValue = rtAnalogValue;

                Vector2 stick = moveAction != null && moveAction.action != null
                    ? moveAction.action.ReadValue<Vector2>()
                    : Vector2.zero;

                if (stick.sqrMagnitude > aimDeadzone * aimDeadzone)
                {
                    aimYaw = Mathf.Repeat(aimYaw + stick.x * aimRotationSpeed * Time.deltaTime, 360f);
                    aimPitch = Mathf.Clamp(aimPitch - stick.y * aimRotationSpeed * Time.deltaTime, minAimPitch, maxAimPitch);
                }

                Vector3 dir = AimDirection();
                float chargeFraction = ChargeFraction();
                aimArrow?.SetAim(dir, chargeFraction);

                float previewForce = Mathf.Lerp(minLaunchForce, maxLaunchForce, chargeFraction);
                // Interpolated the same way as force - see the Launch Force header comment for
                // why a single constant damping can't keep both ends of the charge range landing
                // where they used to once exit speed went up.
                float previewDamping = Mathf.Lerp(minLaunchDamping, maxLaunchDamping, chargeFraction);

                // Computed unconditionally (not just when a visual is active) so it can always
                // be cached below and compared against where the cube actually lands - see
                // OnCollisionEnter's LandingCheck log.
                Vector3 initialVelocity = rb.linearVelocity + dir * previewForce / rb.mass;
                Vector3 lineStart = transform.position + Vector3.up * previewLineHeight;
                Vector3 landingPoint = PredictLandingPoint(transform.position, initialVelocity, previewDamping, out int stepCount, out bool didLand);
                lastPredictedLanding = landingPoint;
                hasPredictedLanding = true;

                if (landingPreview != null && landingPreview.CurrentMode != PredictionMode.None)
                {
                    landingPreview.SetLandingPoint(lineStart, landingPoint, trajectoryBuffer, stepCount, didLand);
                }

                if (launchNow)
                {
                    queuedDirection = dir;
                    queuedForce = previewForce;
                    queuedDamping = previewDamping;
                    launchQueued = true;
                    hasLaunched = true;

                    isAiming = false;
                    chargeTime = 0f;
                    aimArrow?.SetVisible(false);
                    landingPreview?.SetVisible(false);
                    waitingForLtRelease = true;
                }
            }
            else if (isAiming)
            {
                // LT released without firing - cancel, no launch.
                isAiming = false;
                chargeTime = 0f;
                aimArrow?.SetVisible(false);
                landingPreview?.SetVisible(false);
            }
        }

        void FixedUpdate()
        {
            if (launchQueued)
            {
                launchQueued = false;
                rb.linearDamping = queuedDamping;
                rb.AddForce(queuedDirection * queuedForce, ForceMode.Impulse);
                launchGraceTimer = launchGraceDuration;
            }

            if (launchGraceTimer > 0f) launchGraceTimer -= Time.fixedDeltaTime;

            // Grounded state comes from a direct downward check each step, not accumulated
            // OnCollisionEnter/Stay/Exit state - Continuous collision detection (needed so a fast
            // launch can't tunnel through the floor) can keep reporting contact slightly after the
            // cube has genuinely left the ground, which was letting hasLaunched clear near a lob
            // shot's apex (low velocity, stale "grounded") and allowing a mid-air relaunch. A fresh
            // check each step has no such lag: it's simply true or false for exactly this instant.
            //
            // A single ray from the exact center used to do this, but a landing right at a
            // platform's edge can leave the cube's CENTER hanging just past the edge while a
            // corner of its collider is still genuinely resting on the surface - the center ray
            // then misses the platform entirely, isGrounded gets stuck false forever, and since
            // both the aim-start gate and the hasLaunched re-arm above require isGrounded, this
            // permanently locked out launching, matching "land on the border and can't launch
            // anymore". A BoxCast across the cube's own footprint (slightly inset to avoid
            // catching geometry the cube isn't actually resting on) reports grounded if ANY part
            // of that footprint has support below, not just the exact center point.
            Vector3 halfExtents = boxCollider != null
                ? new Vector3(boxCollider.bounds.extents.x * 0.9f, 0.05f, boxCollider.bounds.extents.z * 0.9f)
                : new Vector3(0.4f, 0.05f, 0.4f);
            isGrounded = Physics.BoxCast(transform.position, halfExtents, Vector3.down, out _, transform.rotation, groundCheckDistance);

            // Re-arm the single launch once the cube has actually come to rest on the ground.
            // Grounded alone isn't enough (a launch fired while already touching the floor never
            // triggers a fresh OnCollisionEnter, since contact was never broken - it just slides
            // to a stop via drag/friction), so this also waits for velocity to settle rather than
            // relying only on the hard OnCollisionEnter stop below.
            if (hasLaunched && isGrounded && rb.linearVelocity.sqrMagnitude < restVelocityThreshold * restVelocityThreshold)
            {
                hasLaunched = false;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsGroundContact(collision)) return;
            if (launchGraceTimer > 0f) return;

            // TEMPORARY diagnostic: logs exactly how far off the prediction was the moment it's
            // possible to measure (right as the real landing is detected), including which axis
            // the error is on - needed real numbers rather than another guess at the cause,
            // since the last two fixes (BoxCast sizing, excluding the player's own collider from
            // the sweep) were each individually correct but reportedly didn't fully close the gap.
            if (hasPredictedLanding)
            {
                Vector3 error = transform.position - lastPredictedLanding;
                Debug.Log($"LandingCheck: predicted={lastPredictedLanding}, actual={transform.position}, error=(x:{error.x:F2}, y:{error.y:F2}, z:{error.z:F2}), distance={error.magnitude:F2}m");
                hasPredictedLanding = false;
            }

            // Stop dead the instant it lands - only on a roughly-upward contact normal, so this
            // reads as "touched the ground" and not "bumped into a wall".
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        bool IsGroundContact(Collision collision)
        {
            foreach (ContactPoint contact in collision.contacts)
            {
                if (Vector3.Dot(contact.normal, Vector3.up) > groundNormalDot) return true;
            }
            return false;
        }

        // West/North/East now pick the control scheme (see ControlScheme) rather than the
        // visual preview mode - Ghost/Crosshair preview modes are disabled anyway (see
        // LandingPreviewController.ghostAndCrosshairEnabled), so those buttons were free to
        // repurpose. South still works the way it always did: toggle the trail preview on/off,
        // just switching between Trail and None now instead of being a one-way hide, since
        // nothing else is left to bring it back otherwise.
        void HandlePreviewModeSwitch()
        {
            if (selectClassicSchemeAction != null && selectClassicSchemeAction.action != null && selectClassicSchemeAction.action.WasPressedThisFrame())
            {
                controlScheme = ControlScheme.LaunchInstantly;
                UpdateSchemeLabel();
            }
            else if (selectHoldReleaseSchemeAction != null && selectHoldReleaseSchemeAction.action != null && selectHoldReleaseSchemeAction.action.WasPressedThisFrame())
            {
                controlScheme = ControlScheme.HoldRelease;
                UpdateSchemeLabel();
            }
            else if (selectAnalogSchemeAction != null && selectAnalogSchemeAction.action != null && selectAnalogSchemeAction.action.WasPressedThisFrame())
            {
                controlScheme = ControlScheme.AnalogPressure;
                UpdateSchemeLabel();
            }
            else if (selectNoneAction != null && selectNoneAction.action != null && selectNoneAction.action.WasPressedThisFrame() && landingPreview != null)
            {
                landingPreview.SetMode(landingPreview.CurrentMode == PredictionMode.None ? PredictionMode.Trail : PredictionMode.None);
            }
        }

        void UpdateSchemeLabel()
        {
            if (landingPreview == null || landingPreview.modeLabel == null) return;
            landingPreview.modeLabel.text = $"West: Launch Instantly   North: Hold-Release   East: Analog   South: Show/Hide   (scheme: {controlScheme})";
        }

        // Runs the ACTUAL Unity physics engine on a hidden stand-in Rigidbody, fast-forwarded
        // via manual Physics.Simulate() calls, instead of approximating gravity/drag/collision
        // with hand-written math. Three attempts at a formula-based simulation (flat groundLevel,
        // then BoxCast sizing, then excluding the player's own collider) each fixed a real bug
        // but the predicted point still didn't consistently match reality - guessing at Unity's
        // exact internal drag/integration formula wasn't converging. Using the real engine for
        // both is accurate by construction: there's no formula to get subtly wrong, since it's
        // the same code path that will actually move the cube.
        Vector3 PredictLandingPoint(Vector3 startPos, Vector3 initialVelocity, float damping, out int stepCount, out bool didLand)
        {
            EnsurePredictionClone();

            // Damping now varies by charge level (see the Launch Force header comment) - set
            // fresh every call to match whatever shot is currently being aimed, rather than the
            // one-time copy EnsurePredictionClone used to take at clone-creation time, which
            // would otherwise leave every prediction using whatever damping happened to be
            // current the first time this level's clone was built.
            predictionRb.linearDamping = damping;

            // Started slightly above startPos, not exactly on it - teleporting straight onto
            // the platform's surface can register as a fresh contact the moment simulation
            // resumes, and the real cube never has this problem (it's been continuously resting
            // on its platform since it landed), but the clone, repositioned from scratch every
            // call, would otherwise immediately "land" on the platform it launches from before
            // it has moved at all.
            predictionRb.position = startPos + Vector3.up * 0.02f;
            predictionRb.rotation = transform.rotation;
            predictionRb.linearVelocity = initialVelocity;
            predictionRb.angularVelocity = Vector3.zero;
            predictionRb.Sleep();
            predictionRb.WakeUp(); predictionRb.linearVelocity = initialVelocity;
            predictionRb.angularVelocity = Vector3.zero;

            float dt = Time.fixedDeltaTime;
            Vector3 landing = startPos;
            stepCount = 0;
            didLand = false;

            for (int i = 0; i < maxPredictionSteps; i++)
            {
                predictionPhysicsScene.Simulate(dt);

                Vector3 pos = predictionClone.transform.position;
                landing = pos;
                if (stepCount < trajectoryBuffer.Length) trajectoryBuffer[stepCount++] = pos;

                // Only trusted after a couple of real steps - checking from i==0 risked reading
                // linearVelocity before this same frame's assignment had actually been picked up
                // by a just-touched physics step, which could misreport "already at rest" before
                // the clone had genuinely moved (positioned right on top of the player/arrow).
                // PredictionCloneStopper zeroes this the instant the clone lands, exactly
                // mirroring KineticCubeController's own OnCollisionEnter.
                if (i >= 2 && predictionRb.linearVelocity.sqrMagnitude < 0.0001f)
                {
                    didLand = true;
                    break;
                }

                // A shot aimed at a gap in Level1 (no floor - Sandbox Scene's flat floor always
                // catches it, which is why this only showed up in Level1) never comes to rest at
                // all, so it would otherwise run the full maxPredictionSteps of REAL physics
                // steps every single frame while aiming - this is what actually made aiming
                // unusable there. Once it's fallen past the same threshold that triggers the
                // real fall-reset, there's nothing more useful to simulate; bail out immediately
                // instead of burning the whole step budget on an already-decided miss. didLand
                // stays false - there's no real landing spot to report for Ghost/Crosshair here.
                if (pos.y < fallResetY) break;
            }

            return landing;
        }

        void EnsurePredictionClone()
        {
            if (predictionClone != null) return;

            if (!predictionSceneReady)
            {
                // A genuinely separate PhysicsScene, not just a toggled SimulationMode - manual
                // Simulate() calls on THIS scene step only its own bodies, so it is physically
                // impossible for prediction to touch the real player, the real camera, or
                // anything else in the main scene, no matter how many steps a long prediction
                // needs. Two earlier attempts at cleaning up after the fact - temporarily
                // kinematic, then snapshotting/restoring position+rotation+velocity - both still
                // let a "teleport" and a knock-on camera jump through occasionally. Isolating the
                // simulation removes the possibility outright instead of trying to undo it.
                predictionScene = SceneManager.CreateScene(
                    "KineticEnergyPredictionPhysics_" + (predictionSceneCounter++),
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                predictionPhysicsScene = predictionScene.GetPhysicsScene();
                BuildPredictionGeometryProxies();
                predictionSceneReady = true;
            }

            predictionClone = new GameObject("PredictionClone (hidden)");
            SceneManager.MoveGameObjectToScene(predictionClone, predictionScene);

            predictionRb = predictionClone.AddComponent<Rigidbody>();
            predictionRb.mass = rb.mass;
            predictionRb.linearDamping = rb.linearDamping;
            predictionRb.angularDamping = rb.angularDamping;
            predictionRb.constraints = RigidbodyConstraints.FreezeRotation;
            predictionRb.interpolation = RigidbodyInterpolation.None;
            predictionRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            BoxCollider cloneCollider = predictionClone.AddComponent<BoxCollider>();
            if (boxCollider != null) cloneCollider.size = boxCollider.size;
            // No Physics.IgnoreCollision needed anymore - a separate PhysicsScene means the
            // clone cannot physically collide with the real player's collider at all.

            PredictionCloneStopper stopper = predictionClone.AddComponent<PredictionCloneStopper>();
            stopper.groundNormalDot = groundNormalDot;

            // Left permanently active rather than toggled per prediction call - reactivating a
            // GameObject and immediately reading its Rigidbody's state in the same call is
            // exactly the kind of race that likely caused the "lands on the arrow" symptom above.
            // Between predictions it just sits wherever the last one left it (invisible, no
            // renderer) until the next call repositions and re-launches it - harmless, since
            // nothing else ever reads its state.
        }

        // Colliders can't be shared across PhysicsScenes, only duplicated - this builds
        // static-geometry stand-ins inside the prediction's own isolated scene, matching every
        // collider in the main scene that isn't a Rigidbody (platforms, floor) so the clone has
        // something to land on. Built once, lazily, on the first prediction of the level's
        // lifetime - aiming can't start before at least one real Update() frame has passed, by
        // which point every Awake()/Start() in the scene (including LevelGenerator's) has
        // already run, so the geometry being copied is guaranteed final.
        void BuildPredictionGeometryProxies()
        {
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude);

            foreach (Collider col in colliders)
            {
                if (col == boxCollider) continue;
                if (col.GetComponent<Rigidbody>() != null) continue;
                // Trigger volumes (e.g. Level1's finish line) aren't solid ground - including one
                // here would make the prediction clone incorrectly "land" on thin air.
                if (col.isTrigger) continue;

                GameObject proxy = new GameObject("PredictionGeometryProxy");
                SceneManager.MoveGameObjectToScene(proxy, predictionScene);
                proxy.transform.SetPositionAndRotation(col.transform.position, col.transform.rotation);
                proxy.transform.localScale = col.transform.lossyScale;

                if (col is BoxCollider box)
                {
                    BoxCollider proxyBox = proxy.AddComponent<BoxCollider>();
                    proxyBox.center = box.center;
                    proxyBox.size = box.size;
                }
                else if (col is MeshCollider meshCol)
                {
                    MeshCollider proxyMesh = proxy.AddComponent<MeshCollider>();
                    proxyMesh.sharedMesh = meshCol.sharedMesh;
                    proxyMesh.convex = meshCol.convex;
                }
                else
                {
                    Debug.LogWarning($"KineticCubeController: unhandled collider type {col.GetType().Name} on {col.name} - not included in landing prediction geometry.");
                    Destroy(proxy);
                }
            }
        }

        float ChargeFraction()
        {
            return maxChargeTime > 0f ? Mathf.Clamp01(chargeTime / maxChargeTime) : 1f;
        }

        Vector3 AimDirection()
        {
            return Quaternion.Euler(aimPitch, aimYaw, 0f) * Vector3.forward;
        }

        void SeedAimFromCamera()
        {
            // Only yaw comes from the camera (start aiming whichever way you're currently
            // facing) - pitch starts at a fixed, predictable default rather than whatever the
            // camera's current vertical angle happens to be, which could be anywhere from
            // looking flat to looking well upward and made the very first aim direction feel
            // random rather than a sensible, adjustable-from-there starting point.
            aimYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            aimPitch = Mathf.Clamp(defaultAimPitch, minAimPitch, maxAimPitch);
        }
    }
}
