using System;

namespace CityPerformance
{
    internal static class CinematicCore
    {
        internal static float Scale(float percent) => float.IsNaN(percent) || float.IsInfinity(percent) ? 1f : Math.Max(1f, Math.Min(2f, percent / 100f));
        internal static int Band(float distance, float first, float second) => distance < first ? 0 : distance < second ? 1 : 2;
        internal static int Combine(int vanilla, int nearbyCamera, int directionalCamera) => Math.Min(vanilla, Math.Min(nearbyCamera, directionalCamera));
        // A smaller band means more detail. Upgrades are immediate; downgrades have a grace period.
        internal static int Hold(int wanted, int applied, ref double lastNeeded, double now, bool active)
        {
            if (!active) return wanted;
            if (wanted <= applied) { lastNeeded = now; return wanted; }
            return now - lastNeeded < 2 ? applied : wanted;
        }
        internal static bool SphereInside(float signedPlaneDistance, float radius) => signedPlaneDistance >= -radius;
    }
}
