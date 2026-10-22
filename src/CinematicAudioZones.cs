using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Player.Sound;
using UnityEngine;

namespace CityPerformance
{
    // Only audio-zone callbacks are redirected. No Player collider, gameplay trigger or player transform is fabricated.
    internal static class CinematicAudioZones
    {
        private sealed class Zone { internal AudioZone Owner; internal Collider Shape; internal bool Playing; }
        private static readonly List<Zone> zones = new List<Zone>();
        private static readonly MethodInfo awake = AccessTools.DeclaredMethod(typeof(AudioZone), "Awake", Type.EmptyTypes);
        private static readonly MethodInfo destroy = AccessTools.DeclaredMethod(typeof(AudioZone), "OnDestroy", Type.EmptyTypes);
        private static readonly MethodInfo enter = AccessTools.DeclaredMethod(typeof(AudioZone), "OnTriggerEnter", new[] { typeof(Collider) });
        private static readonly MethodInfo exit = AccessTools.DeclaredMethod(typeof(AudioZone), "OnTriggerExit", new[] { typeof(Collider) });
        private static readonly MethodInfo play = AccessTools.DeclaredMethod(typeof(AudioZone), "PlaySound", Type.EmptyTypes);
        private static readonly MethodInfo stop = AccessTools.DeclaredMethod(typeof(AudioZone), "StopSound", Type.EmptyTypes);
        private static Action<AudioZone> playSound, stopSound;
        private static bool installed, active;
        private static double next, nextConflict;
        internal static void Install()
        {
            if (!PerformanceMod.Verified || Conflict()) return;
            try
            {
                playSound = AccessTools.MethodDelegate<Action<AudioZone>>(play);
                stopSound = AccessTools.MethodDelegate<Action<AudioZone>>(stop);
                PerformanceMod.Harmony.Patch(awake, postfix: new HarmonyMethod(typeof(CinematicAudioZones), nameof(Awake)));
                PerformanceMod.Harmony.Patch(destroy, prefix: new HarmonyMethod(typeof(CinematicAudioZones), nameof(Destroy)));
                PerformanceMod.Harmony.Patch(enter, prefix: new HarmonyMethod(typeof(CinematicAudioZones), nameof(Trigger)));
                PerformanceMod.Harmony.Patch(exit, prefix: new HarmonyMethod(typeof(CinematicAudioZones), nameof(Trigger)));
                installed = true;
            }
            catch (Exception e) { CinematicWorld.UnpatchFeature(typeof(CinematicAudioZones)); PerformanceMod.Log("CINEMA audio zones unavailable: " + e.Message); }
        }
        private static bool Conflict() => CinematicWorld.Foreign(enter) || CinematicWorld.Foreign(exit) || CinematicWorld.Foreign(play) || CinematicWorld.Foreign(stop) || CinematicWorld.Foreign(awake) || CinematicWorld.Foreign(destroy);
        private static bool Trigger(AudioZone __instance) => !active || !zones.Exists(z => z.Owner == __instance);
        private static void Awake(AudioZone __instance) { if (active) Register(__instance); }
        private static void Destroy(AudioZone __instance) { zones.RemoveAll(z => z.Owner == __instance); }
        private static void Register(AudioZone owner)
        {
            if (zones.Exists(z => z.Owner == owner)) return;
            var shape = owner.GetComponent<Collider>();
            if (shape == null) return;
            if (owner.isActiveAndEnabled) stopSound(owner);
            zones.Add(new Zone { Owner = owner, Shape = shape });
        }
        internal static void Tick()
        {
            if (!installed) return;
            bool requested = CinematicWorld.AudioActive;
            if (requested && !active)
            {
                if (Conflict()) return;
                zones.Clear();
                foreach (var owner in UnityEngine.Object.FindObjectsOfType<AudioZone>(true)) Register(owner);
                active = true; next = 0;
            }
            if (!requested) { if (active) End(); return; }
            if (Time.realtimeSinceStartupAsDouble < next) return;
            next = Time.realtimeSinceStartupAsDouble + .1;
            if (Time.realtimeSinceStartupAsDouble >= nextConflict)
            { nextConflict = Time.realtimeSinceStartupAsDouble + 2; if (Conflict()) { End(); installed = false; return; } }
            Vector3 position = CinematicWorld.RecordingCamera.transform.position;
            bool quiet = Helpers.FuneralHelper.IsFuneralActive;
            foreach (var zone in zones)
            {
                if (zone.Owner == null || zone.Shape == null) continue;
                bool inside = !quiet && zone.Owner.isActiveAndEnabled && zone.Shape.enabled && (zone.Shape.ClosestPoint(position) - position).sqrMagnitude < .01f;
                if (inside == zone.Playing) continue;
                if (inside) playSound(zone.Owner); else stopSound(zone.Owner);
                zone.Playing = inside;
            }
        }
        internal static void End()
        {
            bool restore = active && !Conflict(); active = false;
            if (restore) foreach (var zone in zones)
            {
                if (zone.Owner == null || zone.Shape == null) continue;
                stopSound(zone.Owner);
                if (!Helpers.FuneralHelper.IsFuneralActive && zone.Owner.isActiveAndEnabled && zone.Shape.enabled &&
                    (zone.Shape.ClosestPoint(CinematicWorld.PlayerPosition) - CinematicWorld.PlayerPosition).sqrMagnitude < .01f) playSound(zone.Owner);
            }
            zones.Clear();
        }
    }
}
