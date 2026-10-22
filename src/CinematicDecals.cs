using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace CityPerformance
{
    internal static class CinematicDecals
    {
        private static MethodInfo getter;
        private static bool faulted;
        private static double nextConflict;
        internal static void Install()
        {
            try
            {
                var type = Type.GetType("UnityEngine.Rendering.HighDefinition.DecalSystem, Unity.RenderPipelines.HighDefinition.Runtime", false);
                getter = type == null ? null : AccessTools.PropertyGetter(type, "DrawDistance");
                if (!PerformanceMod.Verified || getter == null || getter.ReturnType != typeof(int) || CinematicWorld.Foreign(getter)) return;
                using (var hash = SHA256.Create()) using (var file = File.OpenRead(type.Assembly.Location))
                    if (BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != "d0f869303996388151aa45aefb90c33147f0ae7961a1d0e0b406015ad5a8dfc6")
                    { PerformanceMod.Log("CINEMA decals unavailable: HDRP assembly not audited."); return; }
                PerformanceMod.Harmony.Patch(getter, postfix: new HarmonyMethod(typeof(CinematicDecals), nameof(Distance)));
                PerformanceMod.Log("CINEMA decal distance hook active; authored projector limits retained, global extension capped at 400m.");
            }
            catch (Exception e) { faulted = true; PerformanceMod.Log("CINEMA decals unavailable: " + e.Message); }
        }
        private static void Distance(ref int __result)
        {
            if (faulted || !CinematicWorld.Active) return;
            if (UnityEngine.Time.realtimeSinceStartupAsDouble >= nextConflict)
            { nextConflict = UnityEngine.Time.realtimeSinceStartupAsDouble + 2; if (CinematicWorld.Foreign(getter)) { faulted = true; return; } }
            __result = Math.Max(__result, Math.Min(400, (int)Math.Ceiling(__result * CinematicCore.Scale(CinematicWorld.DistancePercent))));
        }
    }
}
