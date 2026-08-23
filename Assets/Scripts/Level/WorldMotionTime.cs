using UnityEngine;

namespace KineticEnergy.Level
{

    public static class WorldMotionTime
    {

        public static float FixedDeltaTime => Mathf.Min(Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);

        public static float DeltaTime => Mathf.Min(Time.deltaTime, Time.unscaledDeltaTime);
    }
}

