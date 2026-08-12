using UnityEngine;

namespace KineticEnergy.Level
{
    // THE project-wide time rule for every moving world object that is not the player
    // (platforms, hazards, future interactables): advance motion by the SMALLER of game
    // time and real time each step. One rule covers every situation:
    //  - midair aim (bullet-time): game time is smaller -> the world follows the slow-mo;
    //  - a launch's speed-up: real time is smaller -> the world does NOT accelerate with
    //    the flight;
    //  - normal play: both equal -> plain speed;
    //  - pause: no ticks -> everything freezes, no jump on resume.
    // Accumulate these deltas into your own clock rather than reading Time.time, so pauses
    // never cause teleports.
    public static class WorldMotionTime
    {
        // For motion driven from FixedUpdate (physics-moved objects).
        public static float FixedDeltaTime => Mathf.Min(Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);

        // For motion driven from Update (purely visual movers).
        public static float DeltaTime => Mathf.Min(Time.deltaTime, Time.unscaledDeltaTime);
    }
}
