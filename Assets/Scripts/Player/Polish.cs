using UnityEngine;

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
        }

        void OnEnable()
        {
            if (controller == null) controller = GetComponent<KineticCubeController>();
            if (controller != null) controller.CrashRegistered += OnCrash;
        }

        void OnDisable()
        {
            if (controller != null) controller.CrashRegistered -= OnCrash;
        }

        void OnCrash(Vector3 position)
        {
            // The arrival direction, read before physics wiped it - the squash flattens
            // along how the body actually came in, wall and floor hits alike.
            Vector3 approach = controller.PreCollisionVelocity;
            crashAxis = approach.sqrMagnitude > 0.01f ? approach.normalized : Vector3.down;
            crashTimer = Mathf.Max(crashRecoverSeconds, 0.01f);
        }

        // LateUpdate, so it lands after the free-move lean has written the visual's pose
        // for the frame - while a deform is active this rotation wins, and on a sphere
        // that costs nothing.
        void LateUpdate()
        {
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
