using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using KineticEnergy.Level;

namespace KineticEnergy.Player
{

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
        bool isFlying;

        [Header("Outline Fade")]
        [Tooltip("Outline strength WHILE FLYING (1 = full). With the camera trailing a launch the player shrinks to a handful of pixels, and even a thin outset ring dwarfs a ball that small - the whole outline dims for the flight and returns on arrival.")]
        [Range(0f, 1f)] public float launchOutlineFade = 0.2f;
        [Tooltip("How quickly the outline dims and returns (bigger = snappier).")]
        public float outlineFadeEaseSpeed = 6f;
        float outlineFade = 1f;

        [Tooltip("Metres around the player's own depth the fade covers - keeps the dim scoped to the ball's rim while every other outline in the frame stays full.")]
        public float outlineFadeWindow = 1.5f;

        void UpdateOutlineFade()
        {
            outlineFade = Mathf.Lerp(outlineFade, isFlying ? launchOutlineFade : 1f,
                1f - Mathf.Exp(-outlineFadeEaseSpeed * Time.unscaledDeltaTime));
            Shader.SetGlobalFloat("_OutlineGlobalFade", outlineFade);
            Shader.SetGlobalFloat("_OutlineFadeWindow", outlineFadeWindow);

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                float depth = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
                Shader.SetGlobalFloat("_OutlinePlayerDepth", depth);
            }
        }

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
        float crashShakeWeight = 1f;

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

        void UpdateScreenspaceTrail()
        {
            if (trailImage == null) return;
            bool show = screenspaceTrail && isFlying;
            if (trailImage.enabled != show) trailImage.enabled = show;
        }

        [Header("Audio")]
        [Tooltip("The Player's AudioSource for the LOOPED sounds (charging, flying). The crash plays through its own runtime source, so its heavy pitch never bleeds into the loops.")]
        public AudioSource playerSounds;
        [Tooltip("Whoosh loop while airborne after a launch.")]
        public bool flyingSoundEnabled = true;
        public AudioClip flyingSound;
        [Tooltip("The charging loop, playing the whole time an aim is open - grounded or midair, dial or no dial. (The old intro chirp is gone; this carries the identity alone.)")]
        public bool chargeLoopSoundEnabled = true;
        public AudioClip chargingLoopSound;
        [Tooltip("The impact thud on landings and crashes.")]
        public bool crashSoundEnabled = true;
        public AudioClip crashSound;
        [Tooltip("Crash pitch AT FULL SPEND - well below 1: pitching down is what makes it heavy.")]
        public float crashPitch = 0.72f;
        [Tooltip("Crash volume AT FULL SPEND, 0-1.")]
        [Range(0f, 1f)] public float crashVolume = 1f;
        [Tooltip("Pitch of a ZERO-spend arrival - slightly above 1, so a cheap crash is a light tap and the ramp down to the heavy pitch carries the spend.")]
        public float crashLightPitch = 1.05f;
        [Tooltip("Volume of a ZERO-spend arrival.")]
        [Range(0f, 1f)] public float crashLightVolume = 0.35f;
        [Tooltip("The SUB-THUMP: the same thud layered an octave down, fading in above ~35% spend - the chest hit that makes a committed slam sound like one. 0 disables the layer.")]
        [Range(0f, 1f)] public float crashSubThumpVolume = 0.8f;
        [Tooltip("Contrast on the spend-to-sound curve, like the rumble's: higher pushes cheap crashes toward the light end so the heavy end stands apart.")]
        public float crashSoundContrast = 1.5f;

        [Tooltip("Flight whoosh pitch at FULL spend - below 1, the deeper roar of a big launch.")]
        public float launchFullPitch = 0.85f;
        [Tooltip("Flight whoosh pitch at ZERO spend - a light, quick swish.")]
        public float launchLightPitch = 1.1f;
        [Tooltip("Whoosh volume at zero spend.")]
        [Range(0f, 1f)] public float launchLightVolume = 0.45f;
        [Tooltip("Whoosh volume at full spend.")]
        [Range(0f, 1f)] public float launchFullVolume = 1f;
        [Tooltip("The launch BOOM: the thud sample fired as a one-shot cannon shot under the whoosh. 0 disables it.")]
        [Range(0f, 1f)] public float launchBoomVolume = 0.9f;
        [Tooltip("Spend at which the boom starts fading in. High on purpose: it reuses the CRASH sample, and below ~85% it read as the crash SFX firing midair at launch (direct report at >50%). The boom belongs to the big-launch identity, not to every decent shot.")]
        [Range(0f, 1f)] public float launchBoomStartSpend = 0.85f;
        [Tooltip("The 100% EXTRA: at a genuinely full-tank launch, a second boom lands two octaves down on top of everything - the overcharge tell. 0 disables it.")]
        [Range(0f, 1f)] public float launchOverchargeVolume = 1f;

        [Tooltip("Seconds the charge loop takes to swell from its quiet start to its maximum.")]
        public float chargeLoopRampSeconds = 2f;
        [Tooltip("Fraction of the loop's MAXIMUM the swell starts at - the rise from here is the charge audibly building.")]
        [Range(0f, 1f)] public float chargeLoopStartVolume = 0.25f;
        [Tooltip("The loop's MAXIMUM volume, as a fraction of the audio source's own - the ceiling the swell rises to. 0.7 = 30% under the source's authored level.")]
        [Range(0f, 1f)] public float chargeLoopMaxVolume = 0.7f;
        [Tooltip("The tick when the charge crosses a 20% band boundary - the audible counterpart of the meter's colour change.")]
        public bool energyClickEnabled = true;
        public AudioClip energyClickSound;
        [Tooltip("The click's loudness relative to the charge loop's volume AT THAT MOMENT. 1.5 = half again as loud as the hum it interrupts.")]
        public float energyClickVolumeScale = 1.5f;
        [Tooltip("The crate-break when a launch KILLS an enemy - ground, flyer and turret alike.")]
        public bool enemyKillSoundEnabled = true;
        public AudioClip enemyKillSound;
        [Range(0f, 1f)] public float enemyKillVolume = 1f;
        [Tooltip("The bass impact when something HURTS the player - enemy hits, lasers, damage shells, projectiles: everything that shoves and drains.")]
        public bool playerHurtSoundEnabled = true;
        public AudioClip playerHurtSound;
        [Tooltip("Above 1 the clip is LAYERED onto itself for the extra gain (a source cannot exceed 1 on its own) - 2 plays it twice at once, roughly +6dB.")]
        [Range(0f, 3f)] public float playerHurtVolume = 1.5f;

        void OnEnemyKilled()
        {
            if (!enemyKillSoundEnabled || enemyKillSound == null || crashSource == null) return;
            crashSource.pitch = 1f;
            crashSource.PlayOneShot(enemyKillSound, enemyKillVolume);
        }

        AudioSource hurtSource;

        void OnPlayerHurt()
        {
            if (!playerHurtSoundEnabled || playerHurtSound == null) return;

            if (hurtSource == null)
            {
                hurtSource = gameObject.AddComponent<AudioSource>();
                hurtSource.playOnAwake = false;
                hurtSource.spatialBlend = 0f;
            }
            hurtSource.pitch = 1f;

            float remaining = Mathf.Clamp(playerHurtVolume, 0f, 3f);
            while (remaining > 0.01f)
            {
                hurtSource.PlayOneShot(playerHurtSound, Mathf.Min(remaining, 1f));
                remaining -= 1f;
            }
        }

        AudioSource crashSource;
        AudioSource crashSubSource;
        float chargeLoopStartTime;
        int lastChargeBand = -1;
        float airborneSeconds;
        float loopSourcePitch = 1f;
        float loopSourceVolume = 1f;
        bool audioWasGrounded;
        float nextCrashSoundTime;

        void OnLaunchFired()
        {
            if (playerSounds == null) return;

            float spend = Mathf.Clamp01(controller.LastLaunchEnergySpent);
            float weight = Mathf.Pow(spend, Mathf.Max(crashSoundContrast, 0.01f));

            if (flyingSoundEnabled && flyingSound != null)
            {
                playerSounds.Stop();
                playerSounds.loop = true;
                playerSounds.clip = flyingSound;
                playerSounds.pitch = Mathf.Lerp(launchLightPitch, launchFullPitch, weight);
                playerSounds.volume = Mathf.Lerp(launchLightVolume, launchFullVolume, weight);
                playerSounds.Play();
            }

            if (crashSource != null && crashSound != null)
            {
                float boom = launchBoomVolume
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(launchBoomStartSpend, 1f, spend));
                if (boom > 0.02f)
                {

                    crashSource.pitch = Mathf.Lerp(0.55f, 0.4f, weight);
                    crashSource.PlayOneShot(crashSound, boom);
                }
                if (spend >= 0.999f && launchOverchargeVolume > 0.02f && crashSubSource != null)
                {
                    crashSubSource.pitch = 0.45f;
                    crashSubSource.PlayOneShot(crashSound, launchOverchargeVolume);
                }
            }
        }

        void PlayCrashSound()
        {
            if (!crashSoundEnabled || crashSound == null || crashSource == null) return;

            if (Time.unscaledTime < nextCrashSoundTime) return;
            nextCrashSoundTime = Time.unscaledTime + 0.1f;

            if (playerSounds != null && playerSounds.isPlaying && playerSounds.clip == flyingSound)
            {
                playerSounds.Stop();
            }

            float spend = Mathf.Clamp01(controller.ArrivalEnergySpent);
            float weight = Mathf.Pow(spend, Mathf.Max(crashSoundContrast, 0.01f));
            crashSource.pitch = Mathf.Lerp(crashLightPitch, crashPitch, weight);
            crashSource.PlayOneShot(crashSound, Mathf.Lerp(crashLightVolume, crashVolume, weight));

            float subVolume = crashSubThumpVolume
                * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, spend));
            if (subVolume > 0.02f && crashSubSource != null)
            {
                crashSubSource.pitch = crashSource.pitch * 0.5f;
                crashSubSource.PlayOneShot(crashSound, subVolume);
            }
        }

        void UpdateAudio()
        {
            if (playerSounds == null) return;

            if (Time.timeScale <= 0f)
            {
                if (playerSounds.isPlaying) playerSounds.Stop();
                return;
            }

            bool grounded = controller.IsGrounded;
            if (grounded && !audioWasGrounded && airborneSeconds > 0.25f) PlayCrashSound();
            airborneSeconds = grounded ? 0f : airborneSeconds + Time.deltaTime;
            audioWasGrounded = grounded;

            if (controller.IsAimingOrCharging && chargeLoopSoundEnabled && chargingLoopSound != null)
            {
                if (playerSounds.clip != chargingLoopSound || !playerSounds.isPlaying)
                {
                    playerSounds.Stop();

                    playerSounds.pitch = loopSourcePitch;
                    playerSounds.clip = chargingLoopSound;
                    playerSounds.loop = true;
                    playerSounds.volume = loopSourceVolume * chargeLoopMaxVolume * chargeLoopStartVolume;
                    playerSounds.Play();
                    chargeLoopStartTime = Time.unscaledTime;
                }
                float swell = Mathf.Clamp01((Time.unscaledTime - chargeLoopStartTime)
                    / Mathf.Max(chargeLoopRampSeconds, 0.01f));
                playerSounds.volume = loopSourceVolume * chargeLoopMaxVolume
                    * Mathf.Lerp(chargeLoopStartVolume, 1f, swell);

                int band = Mathf.Clamp(Mathf.FloorToInt(controller.ProjectedLaunchSpend / 0.2f), 0, 5);
                if (lastChargeBand >= 0 && band != lastChargeBand
                    && energyClickEnabled && energyClickSound != null && crashSource != null)
                {
                    crashSource.pitch = 1f;
                    crashSource.PlayOneShot(energyClickSound,
                        Mathf.Clamp01(playerSounds.volume * energyClickVolumeScale));
                }
                lastChargeBand = band;
            }
            else
            {
                lastChargeBand = -1;
                if (playerSounds.isPlaying && playerSounds.clip == chargingLoopSound)
                {
                    playerSounds.Stop();
                }
            }
        }

        [Header("Motion Trail")]
        [Tooltip("The world-space ribbon behind the player (the 'Trail' child - wired by the setup method).")]
        public TrailRenderer motionTrail;
        [Tooltip("Ribbon width at the player, in world units. The shape is fixed: full width here, tapering to a POINT at the far end.")]
        public float trailStartWidth = 0.5f;
        [Tooltip("Seconds a trail segment lives - the ribbon's length in time.")]
        public float trailSeconds = 0.3f;

        bool motionTrailHidden;

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

        [Tooltip("Launch speed of the chunks. With gravity on, more speed = higher AND further.")]
        public float debrisSpeed = 5f;
        [Tooltip("Gravity on the chunks (standard-gravity multiples). THE height dial: higher gravity = flatter, lower arcs that fall out sooner. 0 returns to the old straight-line climb.")]
        public float debrisGravity = 1.5f;
        [Tooltip("Cone half-angle in degrees - the SPREAD dial. The authored 12 was a tight fountain; 40 throws chunks visibly outward.")]
        public float debrisConeAngle = 40f;

        void ApplyDebrisTuning()
        {
            if (debrisParticles == null) return;

            Vector3 emitterLocal = debrisParticles.transform.localPosition;
            emitterLocal.y = debrisEmitterHeight;
            debrisParticles.transform.localPosition = emitterLocal;

            ParticleSystem.MainModule main = debrisParticles.main;

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

        public class DecalFollower : MonoBehaviour
        {
            Transform target;
            Vector3 localPoint;
            Quaternion targetStartRotation;
            Quaternion startRotation;

            public void Bind(Transform surface)
            {
                target = surface;
                localPoint = surface.InverseTransformPoint(transform.position);
                targetStartRotation = surface.rotation;
                startRotation = transform.rotation;
            }

            void LateUpdate()
            {
                if (target == null) { Destroy(gameObject); return; }
                transform.position = target.TransformPoint(localPoint);
                transform.rotation = target.rotation * Quaternion.Inverse(targetStartRotation) * startRotation;
            }
        }

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

            if (playerSounds != null)
            {
                loopSourcePitch = playerSounds.pitch;
                loopSourceVolume = playerSounds.volume;
            }
            crashSource = gameObject.AddComponent<AudioSource>();
            crashSource.playOnAwake = false;
            if (playerSounds != null) crashSource.spatialBlend = playerSounds.spatialBlend;
            crashSubSource = gameObject.AddComponent<AudioSource>();
            crashSubSource.playOnAwake = false;
            crashSubSource.spatialBlend = crashSource.spatialBlend;

            if (speedBlur)
            {
                GameObject volumeGo = new GameObject("SpeedBlurVolume");
                volumeGo.transform.SetParent(transform, false);
                blurVolume = volumeGo.AddComponent<Volume>();
                blurVolume.isGlobal = true;
                blurVolume.priority = 50f;
                blurVolume.weight = 0f;
                VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
#if !UNITY_WEBGL || UNITY_EDITOR

                MotionBlur blur = profile.Add<MotionBlur>(true);
                blur.intensity.Override(Mathf.Clamp01(blurIntensity));
                blur.quality.Override(MotionBlurQuality.Medium);
#endif

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
            if (controller != null)
            {
                controller.CrashRegistered += OnCrash;
                controller.LaunchFired += OnLaunchFired;
                controller.EnemyKilled += OnEnemyKilled;
                controller.PlayerHurt += OnPlayerHurt;
            }
        }

        void OnCrash(Vector3 position)
        {

            Vector3 approach = controller.PreCollisionVelocity;
            crashAxis = approach.sqrMagnitude > 0.01f ? approach.normalized : Vector3.down;
            crashTimer = Mathf.Max(crashRecoverSeconds, 0.01f);
            if (crashShake)
            {
                crashShakeTimer = Mathf.Max(crashShakeDuration, 0.01f);

                crashShakeWeight = Mathf.Lerp(crashShakeFloorFraction, 1f,
                    Mathf.Clamp01(controller.ArrivalEnergySpent));
            }

            SpawnCrashDecal();

            if (!CrashSurfaceDealsDamage()) PlayCrashSound();

            if (crashDebris && !CrashSurfaceDealsDamage())
            {

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

                float spend = Mathf.Clamp01(controller.ArrivalEnergySpent);
                float weight = Mathf.Lerp(rumbleFloorFraction, 1f,
                    Mathf.Pow(spend, Mathf.Max(rumbleContrast, 0.01f)));
                Gamepad.current.SetMotorSpeeds(rumbleLowMotor * weight, rumbleHighMotor * weight);

                rumbleTimer = Mathf.Max(rumbleSeconds * Mathf.Lerp(0.33f, 1f, weight), 0.02f);
            }
        }

        void SpawnCrashDecal()
        {
            if (!crashDecals || crashDecalMaterial == null) return;
            Collider surface = controller.LastCrashSurface;
            if (surface == null) return;

            if (CrashSurfaceDealsDamage()) return;

            Vector3 normal = controller.StuckSurfaceNormal.sqrMagnitude > 0.0001f
                ? controller.StuckSurfaceNormal.normalized
                : Vector3.up;

            Vector3 point = surface.ClosestPoint(transform.position) + normal * 0.03f;

            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decal.name = "CrashDecal";
            Destroy(decal.GetComponent<Collider>());

            decal.transform.SetPositionAndRotation(point,
                Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Quaternion.LookRotation(-normal));
            float size = Mathf.Lerp(decalMinSize, decalMaxSize, Mathf.Clamp01(controller.ArrivalEnergySpent));
            decal.transform.localScale = new Vector3(size, size, 1f);

            DecalFollower follower = decal.AddComponent<DecalFollower>();
            follower.Bind(surface.transform);

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

        static bool GamepadIsActiveInput()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null) return false;
            double padTime = pad.lastUpdateTime;
            if (Keyboard.current != null && Keyboard.current.lastUpdateTime > padTime) return false;
            if (Mouse.current != null && Mouse.current.lastUpdateTime > padTime) return false;
            return true;
        }

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

            if (flightShake && isFlying)
            {
                shake += Random.insideUnitCircle * flightShakeIntensity;
            }

            if (shake.sqrMagnitude > 0f)
            {
                cam.position += cam.right * shake.x + cam.up * shake.y;
            }
        }

        void StopRumbleWhenDone()
        {
            if (rumbleTimer <= 0f) return;
            rumbleTimer -= Time.unscaledDeltaTime;
            if (rumbleTimer <= 0f && Gamepad.current != null) Gamepad.current.ResetHaptics();
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.CrashRegistered -= OnCrash;
                controller.LaunchFired -= OnLaunchFired;
                controller.EnemyKilled -= OnEnemyKilled;
                controller.PlayerHurt -= OnPlayerHurt;
            }

            rumbleTimer = 0f;
            if (Gamepad.current != null) Gamepad.current.ResetHaptics();
        }

        void LateUpdate()
        {
            StopRumbleWhenDone();
            UpdateSpeedBlur();
            UpdateOutlineFade();
            ApplyScreenShake();
            if (controller != null) UpdateScreenspaceTrail();
            if (controller != null) UpdateMotionTrail();
            if (controller != null) UpdateAudio();

            if (visual == null || controller == null) return;

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

            visual.localScale = Vector3.Lerp(visual.localScale, targetScale,
                1f - Mathf.Exp(-easeSpeed * Time.unscaledDeltaTime));
        }
    }
}

