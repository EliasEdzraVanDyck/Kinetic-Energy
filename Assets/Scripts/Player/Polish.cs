using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using KineticEnergy.Level;

namespace KineticEnergy.Player
{
    // The one home for game-feel dressing - effects that read as juice, never as rules.
    // Nothing here touches physics, energy or input: it deforms the VISUAL child only,
    // while the collider and every gameplay system keep seeing the undeformed body.
    //
    // Current elements:
    //  - LAUNCH STRETCH: in flight the model elongates along its velocity and thins on
    //    the other two axes - a teardrop of speed.
    //  - CRASH SQUASH: on impact it flattens along the arrival direction and bulges on
    //    the other two, then eases back round.
    // All values are PERCENTAGES of the authored model (100 = untouched), so the
    // exaggeration is dialled in the inspector, not in code.
    //
    // The model is a sphere, which is what makes the cheap trick safe: the deform aligns
    // by ROTATING the visual so its local Z faces the effect direction, and on a sphere
    // that rotation is invisible - only the scale reads.
    // LateUpdate ordering: the orbit camera writes its pose in its own LateUpdate, and
    // every camera-space effect here (the shakes) must land AFTER it or be overwritten -
    // which is exactly what the old in-controller shake got wrong.
    [DefaultExecutionOrder(1000)]
    public class Polish : MonoBehaviour
    {
        [Tooltip("The mesh child that gets deformed. Empty = the free-move controller's visual.")]
        public Transform visual;

        [Header("Launch Stretch")]
        [Tooltip("Length of the axis pointing ALONG the flight, as % of the model (100 = no stretch).")]
        public float launchLongAxisPercent = 135f;
        [Tooltip("The other two axes while stretched, as % of the model. Below 100 thins the body into the stretch.")]
        public float launchShortAxisPercent = 80f;
        [Tooltip("Flight speed below which the stretch stands down - a drifting body is not a projectile.")]
        public float launchMinSpeed = 6f;

        [Header("Crash Squash")]
        [Tooltip("The axis pointing INTO the surface at impact, as % of the model. Well below 100 - this is the pancake.")]
        public float crashShortAxisPercent = 55f;
        [Tooltip("The other two axes at impact, as % of the model. Above 100 bulges the body outward.")]
        public float crashLongAxisPercent = 135f;
        [Tooltip("Seconds the squash takes to relax back to round, on unscaled time so it survives bullet-time.")]
        public float crashRecoverSeconds = 0.25f;

        [Header("Feel")]
        [Tooltip("How quickly the model eases toward its current deform target (bigger = snappier).")]
        public float easeSpeed = 14f;
        [Tooltip("How quickly the body returns to round when a midair aim opens - its own rate, faster than the ordinary ease so the un-deform reads as settling to attention.")]
        public float aimResetEaseSpeed = 30f;

        [Header("Crash Rumble")]
        [Tooltip("Buzz the controller on every crash - only while the controller is the CURRENT input (a keyboard player's idle pad on the desk stays silent).")]
        public bool crashRumble = true;
        [Tooltip("Low-frequency (heavy) motor strength at FULL spend - a 100%-energy launch crashes at this. The thump of the two.")]
        [Range(0f, 1f)] public float rumbleLowMotor = 1f;
        [Tooltip("High-frequency (light) motor strength at FULL spend. The sting of the two.")]
        [Range(0f, 1f)] public float rumbleHighMotor = 1f;
        [Tooltip("Fraction of the full strength a ZERO-spend arrival still buzzes at, so even a free-fall crash registers. 0 = cheap crashes are silent.")]
        [Range(0f, 1f)] public float rumbleFloorFraction = 0.08f;
        [Tooltip("Contrast on the spend-to-strength curve: 1 = linear, higher pushes cheap crashes DOWN so the expensive ones stand apart. Pads compress differences badly - 2.5 restores them.")]
        public float rumbleContrast = 2.5f;
        [Tooltip("Seconds a FULL-spend buzz lasts, unscaled. Cheap crashes buzz much shorter (down to a third of this), because duration is the difference a pad conveys best.")]
        public float rumbleSeconds = 0.4f;

        float rumbleTimer;

        [Header("Speed Blur")]
        [Tooltip("Motion blur while the launch is actually FLYING - never while aiming, charging, stuck or standing. Forward camera motion smears the periphery hardest, which is the racing-game edge blur.")]
        public bool speedBlur = true;
        [Tooltip("Blur intensity at full effect, 0-1. Racing games sit around 0.3-0.6; past that it reads as drunk, not fast.")]
        [Range(0f, 1f)] public float blurIntensity = 0.45f;
        [Tooltip("Flight speed below which the blur stands down, so the slow tail of a flight sharpens back up.")]
        public float blurMinSpeed = 10f;
        [Tooltip("How quickly the blur fades in and out (bigger = snappier).")]
        public float blurEaseSpeed = 8f;
        [Tooltip("Edge vignette at full flight, 0-1 - how far in from the edges the darkening reaches. THIS is what carries the speed read on this game's flat-colour surfaces. 0 turns it off.")]
        [Range(0f, 1f)] public float speedVignette = 0.19f;
        [Tooltip("How soft the vignette's inner edge is, 0-1. High = a long gradual falloff instead of a visible ring.")]
        [Range(0f, 1f)] public float speedVignetteSmoothness = 0.85f;

        Volume blurVolume;
        float blurWeight;
        bool isFlying; // computed once per frame by the blur gate, shared by the shakes

        [Header("Screen Shake")]
        [Tooltip("Kick the camera on every crash - both axes, camera-relative, decaying over the duration.")]
        public bool crashShake = true;
        [Tooltip("Seconds the crash kick lasts, unscaled.")]
        public float crashShakeDuration = 0.2f;
        [Tooltip("Peak crash offset in world units AT FULL SPEND - a 100%-energy launch crashes at this. Cheaper arrivals shake proportionally softer, down to the floor fraction.")]
        public float crashShakeIntensity = 0.35f;
        [Tooltip("Fraction of the full crash shake a ZERO-spend arrival still gets, so a free fall registers without reading like a slam. Same idea as the rumble floor.")]
        [Range(0f, 1f)] public float crashShakeFloorFraction = 0.25f;
        [Tooltip("Rattle the camera continuously WHILE FLYING - through the whole launch, not a kick at the button press. Both axes, camera-relative.")]
        public bool flightShake = true;
        [Tooltip("Flight rattle amplitude in world units. Small: it is engine vibration, not impact.")]
        public float flightShakeIntensity = 0.05f;

        float crashShakeTimer;
        float crashShakeWeight = 1f; // the arriving launch's spend, sampled at the crash

        [Header("Crash Decal")]
        [Tooltip("Stamp an impact mark where a crash lands on WORLD geometry - never on enemies, whose bodies move and die.")]
        public bool crashDecals = true;
        [Tooltip("The decal material (unlit transparent, wired by the setup method). Empty = no decals.")]
        public Material crashDecalMaterial;
        [Tooltip("Decal width in world units at ZERO launch spend.")]
        public float decalMinSize = 2f;
        [Tooltip("Decal width at FULL spend - kept at 1.5x the minimum per the spec (about 50% between the extremes).")]
        public float decalMaxSize = 3f;
        [Tooltip("Most decals kept alive at once - the oldest is recycled beyond this, so a long session cannot fill the scene.")]
        public int maxDecals = 40;

        readonly System.Collections.Generic.Queue<GameObject> decals = new System.Collections.Generic.Queue<GameObject>();

        [Header("Screenspace Trail")]
        [Tooltip("The fullscreen speed-lines overlay, shown only while the launch is genuinely FLYING - it drops out the moment an aim opens, with the blur and the rattle.")]
        public bool screenspaceTrail = true;
        [Tooltip("The overlay Image (the Player's 'Trails' child - wired by the setup method).")]
        public Image trailImage;

        // Same gate as the blur and the flight rattle (isFlying: launched, not aiming or
        // charging, not stuck, not grounded, above the speed floor) - the whole speed
        // package arrives and leaves as one. Aiming is the deliberate beat; speed dressing
        // during it undercut the stillness.
        void UpdateScreenspaceTrail()
        {
            if (trailImage == null) return;
            bool show = screenspaceTrail && isFlying;
            if (trailImage.enabled != show) trailImage.enabled = show;
        }

        [Header("Motion Trail")]
        [Tooltip("The world-space ribbon behind the player (the 'Trail' child - wired by the setup method).")]
        public TrailRenderer motionTrail;
        [Tooltip("Ribbon width at the player, in world units. The shape is fixed: full width here, tapering to a POINT at the far end.")]
        public float trailStartWidth = 0.5f;
        [Tooltip("Seconds a trail segment lives - the ribbon's length in time.")]
        public float trailSeconds = 0.3f;

        bool motionTrailHidden;

        // The ribbon marks MOVEMENT THE PLAYER OWNS. It stands down while aiming (the
        // deliberate beat), while stuck to any surface (a rotating wall's carry is the
        // wall's motion, not the player's), and while riding a moving platform (same
        // reason - the ribbon just traced the platform's path). Cleared on every
        // transition: re-enabling draws a segment from the LAST recorded point, which
        // after a ride or teleport is a line from somewhere else entirely.
        void UpdateMotionTrail()
        {
            if (motionTrail == null) return;
            bool hide = controller.IsAimingOrCharging
                || controller.IsStuck
                || (controller.IsGrounded && controller.GroundPlatform != null);
            if (hide == motionTrailHidden) return;
            motionTrailHidden = hide;
            motionTrail.emitting = !hide;
            motionTrail.Clear();
        }

        // The authored curve went 0.5 -> 0.23 -> 0 with the zero placed two-thirds along,
        // which left the ribbon's visible end as a blunt cut rather than a point. Rebuilt
        // at boot as a single clean taper: full width at the emitter, zero at the far tip.
        void ApplyTrailShape()
        {
            if (motionTrail == null) return;
            motionTrail.widthMultiplier = trailStartWidth;
            motionTrail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f, 0f, -1f),
                new Keyframe(1f, 0f, -1f, 0f));
            motionTrail.time = trailSeconds;
        }

        [Header("Crash Debris")]
        [Tooltip("Play the debris and dust bursts on every crash.")]
        public bool crashDebris = true;
        [Tooltip("The chunky debris system (the Player's 'Debris' child - wired by the setup method).")]
        public ParticleSystem debrisParticles;
        [Tooltip("The soft dust system (the 'Dust' child). Only played - the tuning below touches the DEBRIS alone.")]
        public ParticleSystem dustParticles;
        [Tooltip("Multiplier on the debris emission (rate and bursts alike). Above 1 = more chunks.")]
        public float debrisCountMultiplier = 2f;
        [Tooltip("Multiplier on the debris start size. Below 1 = smaller chunks.")]
        public float debrisSizeMultiplier = 0.6f;
        [Tooltip("How far the debris start colour is pulled toward the grey below (0 = authored colour, 1 = fully grey).")]
        [Range(0f, 1f)] public float debrisGreyBlend = 0.35f;
        [Tooltip("The grey the blend pulls toward.")]
        public Color debrisGrey = new Color(0.55f, 0.55f, 0.55f);
        [Tooltip("Local height the debris emitter sits at, relative to the player's centre (0 = dead centre). The authored child floats at 0.5, which read as chunks spawning ABOVE the player.")]
        public float debrisEmitterHeight = 0f;

        // The authored system was a narrow 12-degree gravityless cone: chunks rose in a
        // straight line (height untunable except via speed, which also killed the spread)
        // and always UP, even off a wall. These three take ownership of the ballistics -
        // ARCS, whose shape each field controls independently:
        [Tooltip("Launch speed of the chunks. With gravity on, more speed = higher AND further.")]
        public float debrisSpeed = 5f;
        [Tooltip("Gravity on the chunks (standard-gravity multiples). THE height dial: higher gravity = flatter, lower arcs that fall out sooner. 0 returns to the old straight-line climb.")]
        public float debrisGravity = 1.5f;
        [Tooltip("Cone half-angle in degrees - the SPREAD dial. The authored 12 was a tight fountain; 40 throws chunks visibly outward.")]
        public float debrisConeAngle = 40f;

        // The multipliers are applied ONCE at boot, on top of whatever the systems were
        // authored with - so the inspector on the particle systems stays the authority for
        // the base look, and these fields say how the crash variant differs from it.
        void ApplyDebrisTuning()
        {
            if (debrisParticles == null) return;

            // Height only - the horizontal placement (and with it the emission radius the
            // shape module spreads over) stays exactly as authored.
            Vector3 emitterLocal = debrisParticles.transform.localPosition;
            emitterLocal.y = debrisEmitterHeight;
            debrisParticles.transform.localPosition = emitterLocal;

            ParticleSystem.MainModule main = debrisParticles.main;

            // Ballistics owned outright (see the field comments). Speed spans a range so
            // the chunks don't march in lockstep; the shape's radius is left as authored.
            main.startSpeed = new ParticleSystem.MinMaxCurve(debrisSpeed * 0.55f, debrisSpeed);
            main.gravityModifier = debrisGravity;
            ParticleSystem.ShapeModule shape = debrisParticles.shape;
            shape.angle = debrisConeAngle;

            ParticleSystem.MinMaxCurve size = main.startSize;
            size.constant *= debrisSizeMultiplier;
            size.constantMin *= debrisSizeMultiplier;
            size.constantMax *= debrisSizeMultiplier;
            main.startSize = size;

            ParticleSystem.MinMaxGradient colour = main.startColor;
            colour.color = Color.Lerp(colour.color, debrisGrey, debrisGreyBlend);
            colour.colorMin = Color.Lerp(colour.colorMin, debrisGrey, debrisGreyBlend);
            colour.colorMax = Color.Lerp(colour.colorMax, debrisGrey, debrisGreyBlend);
            main.startColor = colour;
            main.maxParticles = Mathf.CeilToInt(main.maxParticles * Mathf.Max(debrisCountMultiplier, 1f));

            ParticleSystem.EmissionModule emission = debrisParticles.emission;
            ParticleSystem.MinMaxCurve rate = emission.rateOverTime;
            rate.constant *= debrisCountMultiplier;
            rate.constantMin *= debrisCountMultiplier;
            rate.constantMax *= debrisCountMultiplier;
            emission.rateOverTime = rate;
            for (int i = 0; i < emission.burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                burst.count = new ParticleSystem.MinMaxCurve(
                    burst.count.constant * debrisCountMultiplier,
                    Mathf.Max(burst.count.constantMax, burst.count.constant) * debrisCountMultiplier);
                emission.SetBurst(i, burst);
            }
        }

        // Hitting something that HURTS is not an impact worth celebrating with debris -
        // the hit's own feedback (knockback, energy loss, the hazard's colour) carries it.
        bool CrashSurfaceDealsDamage()
        {
            Collider surface = controller.LastCrashSurface;
            if (surface == null) return false;
            return surface.GetComponentInParent<Enemy>() != null
                || surface.GetComponentInParent<FlyingEnemy>() != null
                || surface.GetComponentInParent<TurretEnemy>() != null
                || surface.GetComponentInParent<LaserHazard>() != null
                || surface.GetComponentInParent<DamageWalls>() != null
                || surface.GetComponentInParent<DeathWall>() != null;
        }

        // Called by the controller's respawn reset, so a fresh spawn never opens on a
        // half-finished burst from the death that caused it.
        public void ResetCrashParticles()
        {
            if (debrisParticles != null) { debrisParticles.Stop(); debrisParticles.Clear(); }
            if (dustParticles != null) { dustParticles.Stop(); dustParticles.Clear(); }
        }

        KineticCubeController controller;
        Rigidbody body;
        Vector3 restScale;
        float crashTimer;
        Vector3 crashAxis = Vector3.down;

        void Awake()
        {
            controller = GetComponent<KineticCubeController>();
            body = GetComponent<Rigidbody>();
            if (visual == null)
            {
                KineticCubeControllerFreeMove freeMove = GetComponent<KineticCubeControllerFreeMove>();
                if (freeMove != null) visual = freeMove.visual;
            }
            if (visual != null) restScale = visual.localScale;
            ApplyDebrisTuning();
            ApplyTrailShape();

            // The blur rides its own runtime-built global Volume, so the scene's shared
            // profile asset is never written to. Weight 0 = the override does not exist;
            // easing the weight is the whole fade machinery.
            if (speedBlur)
            {
                GameObject volumeGo = new GameObject("SpeedBlurVolume");
                volumeGo.transform.SetParent(transform, false);
                blurVolume = volumeGo.AddComponent<Volume>();
                blurVolume.isGlobal = true;
                blurVolume.priority = 50f; // over the scene's authored volume
                blurVolume.weight = 0f;
                VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
                MotionBlur blur = profile.Add<MotionBlur>(true);
                blur.intensity.Override(Mathf.Clamp01(blurIntensity));
                blur.quality.Override(MotionBlurQuality.Medium);
                // The vignette rides the SAME volume weight, so the two arrive and leave
                // as one effect: edges darken and pull in while the flight is fast.
                if (speedVignette > 0.001f)
                {
                    Vignette vignette = profile.Add<Vignette>(true);
                    vignette.intensity.Override(Mathf.Clamp01(speedVignette));
                    vignette.smoothness.Override(Mathf.Clamp01(speedVignetteSmoothness));
                }
                blurVolume.profile = profile;
            }
        }

        void OnEnable()
        {
            if (controller == null) controller = GetComponent<KineticCubeController>();
            if (controller != null) controller.CrashRegistered += OnCrash;
        }

        void OnCrash(Vector3 position)
        {
            // The arrival direction, read before physics wiped it - the squash flattens
            // along how the body actually came in, wall and floor hits alike.
            Vector3 approach = controller.PreCollisionVelocity;
            crashAxis = approach.sqrMagnitude > 0.01f ? approach.normalized : Vector3.down;
            crashTimer = Mathf.Max(crashRecoverSeconds, 0.01f);
            if (crashShake)
            {
                crashShakeTimer = Mathf.Max(crashShakeDuration, 0.01f);
                // The kick measures what the arriving launch PAID - the same figure the
                // rumble and the gates read - so a full-tank slam rocks the screen and a
                // cheap hop barely nudges it.
                crashShakeWeight = Mathf.Lerp(crashShakeFloorFraction, 1f,
                    Mathf.Clamp01(controller.ArrivalEnergySpent));
            }

            SpawnCrashDecal();

            if (crashDebris && !CrashSurfaceDealsDamage())
            {
                // The burst sprays OFF the face that was hit: the emitter's cone is aimed
                // along the crash normal, so a wall crash throws chunks away from the wall
                // instead of the old always-upward fountain, and a floor crash keeps the
                // familiar upward spray.
                Vector3 sprayNormal = controller.StuckSurfaceNormal.sqrMagnitude > 0.0001f
                    ? controller.StuckSurfaceNormal.normalized
                    : Vector3.up;
                if (debrisParticles != null)
                {
                    debrisParticles.transform.rotation = Quaternion.LookRotation(sprayNormal);
                    debrisParticles.Play();
                }
                if (dustParticles != null) dustParticles.Play();
            }

            if (crashRumble && GamepadIsActiveInput())
            {
                // Scaled by what the arriving launch actually SPENT - the same figure the
                // checkpoint and kill gates read. The contrast power bends the curve so
                // cheap crashes sit near the floor and expensive ones stand clearly apart
                // (a linear map felt like no difference at all - motors compress), and the
                // DURATION scales with it too, which is the difference a pad conveys best.
                float spend = Mathf.Clamp01(controller.ArrivalEnergySpent);
                float weight = Mathf.Lerp(rumbleFloorFraction, 1f,
                    Mathf.Pow(spend, Mathf.Max(rumbleContrast, 0.01f)));
                Gamepad.current.SetMotorSpeeds(rumbleLowMotor * weight, rumbleHighMotor * weight);
                // A third to full, by weight: the wider spread is what makes the top end
                // land - a heavy slam holds the motors three times as long as a cheap tap.
                rumbleTimer = Mathf.Max(rumbleSeconds * Mathf.Lerp(0.33f, 1f, weight), 0.02f);
            }
        }

        // The impact mark: flush against the face the crash landed on, spun randomly
        // around its normal, sized by what the launch spent. Parented to the surface, so
        // a mark on a moving platform rides it instead of hanging where the platform was.
        void SpawnCrashDecal()
        {
            if (!crashDecals || crashDecalMaterial == null) return;
            Collider surface = controller.LastCrashSurface;
            if (surface == null) return;
            // World geometry only: a mark makes no sense on a body that walks off or dies.
            if (surface.GetComponentInParent<Enemy>() != null) return;
            if (surface.GetComponentInParent<FlyingEnemy>() != null) return;
            if (surface.GetComponentInParent<TurretEnemy>() != null) return;

            Vector3 normal = controller.StuckSurfaceNormal.sqrMagnitude > 0.0001f
                ? controller.StuckSurfaceNormal.normalized
                : Vector3.up;
            // The exact point ON the face, not the player's centre: the closest point the
            // hit collider offers, nudged out along the normal so the quad never z-fights.
            Vector3 point = surface.ClosestPoint(transform.position) + normal * 0.03f;

            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decal.name = "CrashDecal";
            Destroy(decal.GetComponent<Collider>()); // a mark is not geometry
            // A Unity quad FACES -Z, so looking along the reversed normal lays it flat on
            // the surface; the random spin is around the normal itself - the "y rotation"
            // of a mark that lives in the surface's plane.
            decal.transform.SetPositionAndRotation(point,
                Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Quaternion.LookRotation(-normal));
            float size = Mathf.Lerp(decalMinSize, decalMaxSize, Mathf.Clamp01(controller.ArrivalEnergySpent));
            decal.transform.localScale = new Vector3(size, size, 1f);
            decal.transform.SetParent(surface.transform, true);

            Renderer decalRenderer = decal.GetComponent<Renderer>();
            decalRenderer.sharedMaterial = crashDecalMaterial;
            decalRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            decals.Enqueue(decal);
            while (decals.Count > Mathf.Max(maxDecals, 1))
            {
                GameObject oldest = decals.Dequeue();
                if (oldest != null) Destroy(oldest);
            }
        }

        // "Current input" by recency: the pad only counts while it was touched more
        // recently than the keyboard and the mouse - the same judgment a player makes
        // about which device they are on, without any scheme bookkeeping.
        static bool GamepadIsActiveInput()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null) return false;
            double padTime = pad.lastUpdateTime;
            if (Keyboard.current != null && Keyboard.current.lastUpdateTime > padTime) return false;
            if (Mouse.current != null && Mouse.current.lastUpdateTime > padTime) return false;
            return true;
        }

        // Live only while the launch is genuinely FLYING: not aiming, not charging, not
        // stuck to a surface, not standing - and still moving fast enough to deserve it.
        void UpdateSpeedBlur()
        {
            if (controller == null || body == null) return;
            isFlying = controller.HasLaunched
                && !controller.IsAimingOrCharging
                && !controller.IsStuck
                && !controller.IsGrounded
                && body.linearVelocity.sqrMagnitude > blurMinSpeed * blurMinSpeed;
            if (blurVolume == null) return;
            blurWeight = Mathf.Lerp(blurWeight, isFlying ? 1f : 0f,
                1f - Mathf.Exp(-blurEaseSpeed * Time.unscaledDeltaTime));
            blurVolume.weight = blurWeight;
        }

        // Applied AFTER the orbit camera has written this frame's pose (the execution
        // order attribute guarantees it), as a one-frame offset in the CAMERA's own right
        // and up - both axes always, never just vertical. Nothing is accumulated and
        // nothing needs restoring: the orbit rewrites the pose from scratch next frame,
        // so each frame's shake is a fresh sample on top of a clean base.
        void ApplyScreenShake()
        {
            if (controller == null) return;
            Transform cam = controller.cameraTransform;
            if (cam == null && UnityEngine.Camera.main != null) cam = UnityEngine.Camera.main.transform;
            if (cam == null) return;

            Vector2 shake = Vector2.zero;

            if (crashShake && crashShakeTimer > 0f)
            {
                crashShakeTimer -= Time.unscaledDeltaTime;
                float strength = Mathf.Clamp01(crashShakeTimer / Mathf.Max(crashShakeDuration, 0.01f));
                shake += Random.insideUnitCircle * (crashShakeIntensity * crashShakeWeight * strength);
            }

            // The flight rattle runs the whole launch - through the air, not at the press -
            // and stands down with the same gate as the blur, so aiming is always steady.
            if (flightShake && isFlying)
            {
                shake += Random.insideUnitCircle * flightShakeIntensity;
            }

            if (shake.sqrMagnitude > 0f)
            {
                cam.position += cam.right * shake.x + cam.up * shake.y;
            }
        }

        // The buzz must END no matter what the game is doing - motors hold their last
        // speed forever otherwise. Unscaled, so pauses and bullet-time can't stretch it.
        void StopRumbleWhenDone()
        {
            if (rumbleTimer <= 0f) return;
            rumbleTimer -= Time.unscaledDeltaTime;
            if (rumbleTimer <= 0f && Gamepad.current != null) Gamepad.current.ResetHaptics();
        }

        void OnDisable()
        {
            if (controller != null) controller.CrashRegistered -= OnCrash;
            // A respawn, scene change or quit mid-buzz must not leave the motors running.
            rumbleTimer = 0f;
            if (Gamepad.current != null) Gamepad.current.ResetHaptics();
        }

        // LateUpdate, so it lands after the free-move lean has written the visual's pose
        // for the frame - while a deform is active this rotation wins, and on a sphere
        // that costs nothing.
        void LateUpdate()
        {
            StopRumbleWhenDone();
            UpdateSpeedBlur();
            ApplyScreenShake();
            if (controller != null) UpdateScreenspaceTrail();
            if (controller != null) UpdateMotionTrail();

            if (visual == null || controller == null) return;

            // Any midair aim or charge - the forward re-aim, the up-charge, the pound
            // wind-up - returns the body to its authored shape: the player is lining up a
            // shot, and a leftover teardrop would be shape noise about a flight that no
            // longer exists. Gradual, but on its own quicker ease than the ordinary one -
            // the un-deform should read as settling to attention, not as a pop.
            if (controller.IsAimingOrCharging && !controller.IsGrounded)
            {
                crashTimer = 0f;
                visual.localScale = Vector3.Lerp(visual.localScale, restScale,
                    1f - Mathf.Exp(-aimResetEaseSpeed * Time.unscaledDeltaTime));
                return;
            }

            Vector3 targetScale = restScale;
            bool orienting = false;
            Vector3 orientAxis = Vector3.forward;

            if (crashTimer > 0f)
            {
                crashTimer -= Time.unscaledDeltaTime;
                // Strongest at the instant of impact, round again by the end of the timer.
                float strength = Mathf.Clamp01(crashTimer / Mathf.Max(crashRecoverSeconds, 0.01f));
                float shortAxis = Mathf.Lerp(1f, crashShortAxisPercent * 0.01f, strength);
                float longAxis = Mathf.Lerp(1f, crashLongAxisPercent * 0.01f, strength);
                targetScale = Vector3.Scale(restScale, new Vector3(longAxis, longAxis, shortAxis));
                orienting = true;
                orientAxis = crashAxis;
            }
            else if (controller.HasLaunched && !controller.IsStuck
                && body != null && body.linearVelocity.sqrMagnitude > launchMinSpeed * launchMinSpeed)
            {
                targetScale = Vector3.Scale(restScale, new Vector3(
                    launchShortAxisPercent * 0.01f, launchShortAxisPercent * 0.01f, launchLongAxisPercent * 0.01f));
                orienting = true;
                orientAxis = body.linearVelocity.normalized;
            }

            if (orienting && orientAxis.sqrMagnitude > 0.0001f)
            {
                visual.rotation = Quaternion.LookRotation(orientAxis,
                    Mathf.Abs(Vector3.Dot(orientAxis, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            }

            // One ease for every state change - into the stretch, between poses, and back
            // to round - so the deform never pops. Unscaled, to keep its snap in slow-mo.
            visual.localScale = Vector3.Lerp(visual.localScale, targetScale,
                1f - Mathf.Exp(-easeSpeed * Time.unscaledDeltaTime));
        }
    }
}
