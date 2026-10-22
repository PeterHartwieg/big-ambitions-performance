using System;
namespace CityPerformance
{
    public static class TreeLodPolicy
    {
        // Smaller screen-height thresholds retain the detailed mesh for longer.
        public const float MaximumTransition = .15f;
        public static bool TryThresholds(float detailed, float culled, out float transition)
        {
            transition = 0;
            if (float.IsNaN(detailed) || float.IsInfinity(detailed) || detailed <= 0 || detailed > 1 ||
                float.IsNaN(culled) || float.IsInfinity(culled) || culled < 0 || culled >= detailed) return false;
            transition = Math.Min(detailed, MaximumTransition);
            return true;
        }
        public static int AfterMap(int previous, bool active, bool lowDetailMap, bool open) =>
            active && !lowDetailMap ? (open ? 1 : 0) : previous;
    }
}
