using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Streets.Pedestrians;
using UnityEngine;
using UI.Load;

namespace CityPerformance
{
    internal static class CinematicPopulation
    {
        // These pedestrians are leased from the game's pool, but excluded from the player's requested population.
        // This avoids stealing the player's pedestrians when a remote camera opens.
        private static readonly HashSet<Pedestrian> owned = new HashSet<Pedestrian>();
        private static readonly List<Pedestrian> scratch = new List<Pedestrian>();
        private static readonly MethodInfo initial = AccessTools.DeclaredMethod(typeof(Pedestrian), "GetInitialTarget", Type.EmptyTypes);
        private static readonly MethodInfo goal = AccessTools.DeclaredMethod(typeof(Pedestrian), "GetTarget", new[] { typeof(bool), typeof(BuildingRegistration).MakeByRefType() });
        private static readonly MethodInfo range = AccessTools.DeclaredMethod(typeof(CityManager), "IsOutsidePlayerRange", new[] { typeof(Vector3) });
        private static readonly MethodInfo get = AccessTools.DeclaredMethod(typeof(PedestrianPool), "ActionOnGet", new[] { typeof(Pedestrian) });
        private static readonly MethodInfo release = AccessTools.DeclaredMethod(typeof(PedestrianPool), "ActionOnRelease", new[] { typeof(Pedestrian) });
        private static readonly MethodInfo spawn = AccessTools.DeclaredMethod(typeof(Pedestrian), "OnSpawn", Type.EmptyTypes);
        private static readonly MethodInfo change = AccessTools.DeclaredMethod(typeof(Pedestrian), "ChangeState", new[] { typeof(bool) });
        private static Pedestrian current;
        private static bool installed, creating, faulted, foundSpawn;
        private static Vector3 spawnFocus;
        private static double nextSpawn, nextConflict;
        private static int cursor, created, rejected;
        private static bool Conflict() => CinematicWorld.Foreign(initial) || CinematicWorld.Foreign(goal) || CinematicWorld.Foreign(range) || CinematicWorld.Foreign(get) || CinematicWorld.Foreign(release) || CinematicWorld.Foreign(spawn) || CinematicWorld.Foreign(change);
        internal static void Install()
        {
            if (!PerformanceMod.Verified || Conflict()) return;
            try
            {
                foreach (var method in new[] { spawn, change })
                    PerformanceMod.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(CinematicPopulation), nameof(EnterScope)), finalizer: new HarmonyMethod(typeof(CinematicPopulation), nameof(ExitScope)));
                PerformanceMod.Harmony.Patch(initial, prefix: new HarmonyMethod(typeof(CinematicPopulation), nameof(Initial)));
                PerformanceMod.Harmony.Patch(goal, prefix: new HarmonyMethod(typeof(CinematicPopulation), nameof(Goal)));
                PerformanceMod.Harmony.Patch(range, postfix: new HarmonyMethod(typeof(CinematicPopulation), nameof(Range)));
                PerformanceMod.Harmony.Patch(get, prefix: new HarmonyMethod(typeof(CinematicPopulation), nameof(Getting)), postfix: new HarmonyMethod(typeof(CinematicPopulation), nameof(Got)));
                PerformanceMod.Harmony.Patch(release, prefix: new HarmonyMethod(typeof(CinematicPopulation), nameof(Released)));
                installed = true;
                CinematicTraffic.Install();
            }
            catch (Exception e) { CinematicWorld.UnpatchFeature(typeof(CinematicPopulation)); faulted = true; PerformanceMod.Log("CINEMA population unavailable: " + e); }
        }
        private static void EnterScope(Pedestrian __instance, out Pedestrian __state) { __state = current; current = __instance; }
        private static Exception ExitScope(Pedestrian __state, Exception __exception) { current = __state; return __exception; }
        private static bool Running => installed && !faulted && CinematicWorld.Active && !BuildingManager.IsInsideBuilding;
        private static void Getting(Pedestrian pedestrian) { if (creating) owned.Add(pedestrian); }
        private static void Got(Pedestrian pedestrian)
        { if (owned.Contains(pedestrian)) InstanceBehavior<CityManager>.Instance?.pedestrianSpawner.activePedestrians.Remove(pedestrian); }
        private static void Released(Pedestrian pedestrian) { owned.Remove(pedestrian); }
        private static bool Initial(Pedestrian __instance, ref ValueTuple<Vector3, Quaternion> __result)
        {
            if (!owned.Contains(__instance)) return true;
            // Use actual, valid building-door/navmesh positions; never teleport a player or fake a gameplay collider.
            if (!Running) { __result = default; return false; }
            Vector3 focus = creating ? spawnFocus : __instance.transform.position;
            __result = default;
            for (int i = 0; i < 3; i++)
            {
                var target = PedestrianBuildingPositionProvider.GetRandomBuildingTarget(focus, 30, 90);
                if (target.Item1 == default(Vector3) || CinematicWorld.Visible(target.Item1, 2)) { rejected++; continue; }
                if (!Covered(target.Item1, 100)) continue;
                __result = target; if (creating) foundSpawn = true; break;
            }
            return false;
        }
        private static bool Goal(bool prioritizeBuilding, ref BuildingRegistration buildingRegistration, ref ValueTuple<Vector3, Quaternion> __result)
        {
            if (current == null || !owned.Contains(current)) return true;
            buildingRegistration = null;
            // Native goals were centered on the player. Remote extras walk locally instead of trying to cross the city.
            Vector3 from = current.transform.position;
            __result = PedestrianBuildingPositionProvider.GetRandomAvailableBuildingTarget(from, 0, 90, out buildingRegistration);
            // The native closed-building fallback is not distance-filtered. Never send a remote extra across the city.
            if (__result.Item1 == default(Vector3) || (__result.Item1 - from).sqrMagnitude > 90 * 90 || !Covered(__result.Item1, 100))
            {
                buildingRegistration = null;
                __result = PedestrianSpawnerPositionProvider.GetRandomSpawnerTarget(from, 0, 90);
                if (!Covered(__result.Item1, 100)) __result = default;
            }
            return false;
        }
        private static void Range(Vector3 position, ref bool __result)
        { if (__result && Running && Covered(position, 100)) __result = false; }
        internal static bool Covered(Vector3 point, float distance)
        {
            if (!Running) return false;
            var sphere = new BoundingSphere(point, 2);
            return CinematicWorld.Second.Band(sphere, distance, distance, true) == 0 || CinematicWorld.Main.Band(sphere, distance, distance, false) == 0;
        }
        internal static Vector3 Focus(CinematicWorld.View view)
        {
            if (view.Camera == null) return CinematicWorld.PlayerPosition;
            var ray = view.Camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            float length = ray.direction.y < -.05f ? (CinematicWorld.PlayerPosition.y - ray.origin.y) / ray.direction.y : 80 * CinematicCore.Scale(CinematicWorld.DistancePercent);
            length = Mathf.Clamp(length, 0, 100 * CinematicCore.Scale(CinematicWorld.DistancePercent));
            var point = ray.GetPoint(length); point.y = CinematicWorld.PlayerPosition.y;
            return point;
        }
        internal static void Tick()
        {
            if (!installed) return;
            if (!Running)
            {
                scratch.Clear(); foreach (var p in owned) { scratch.Add(p); if (scratch.Count == 2) break; }
                foreach (var p in scratch) { if (p != null) p.Release(); else owned.Remove(p); }
                CinematicTraffic.Tick(false); return;
            }
            if (Time.realtimeSinceStartupAsDouble >= nextConflict)
            { nextConflict = Time.realtimeSinceStartupAsDouble + 2; if (Conflict()) { faulted = true; PerformanceMod.Log("CINEMA pedestrians disabled: foreign hook detected."); return; } }
            CinematicTraffic.Tick(true);
            if (owned.Count >= 20 || Time.realtimeSinceStartupAsDouble < nextSpawn) return;
            nextSpawn = Time.realtimeSinceStartupAsDouble + .25;
            var spawner = InstanceBehavior<CityManager>.Instance?.pedestrianSpawner;
            if (spawner == null || !spawner.spawningActive || spawner.pedestrianPool == null) return;
            var view = (++cursor % 2 == 0 || CinematicWorld.Second.Camera == null) ? CinematicWorld.Main : CinematicWorld.Second;
            if (view.Camera == null) return;
            spawnFocus = Focus(view);
            if ((spawnFocus - CinematicWorld.PlayerPosition).sqrMagnitude < 90 * 90) return;
            try { creating = true; foundSpawn = false; spawner.pedestrianPool.GetPoolHandler().Get(); created++; }
            finally
            {
                creating = false;
                // Wide aerial views may expose every nearby door. Do not keep polling the whole provider at 4Hz.
                if (!foundSpawn) nextSpawn = Time.realtimeSinceStartupAsDouble + 1;
            }
        }
        internal static void End()
        {
            CinematicTraffic.End();
            scratch.Clear(); scratch.AddRange(owned);
            if (!GameManager.isCitySceneBeingUnloaded && !LoadScene.isLoading)
                foreach (var p in scratch) if (p != null) p.Release();
            owned.Clear(); scratch.Clear(); creating = false; current = null; nextSpawn = 0;
        }
        internal static void Report()
        {
            if ((!CinematicWorld.Enabled || !CinematicWorld.BridgeConnected) && owned.Count == 0) return;
            PerformanceMod.Log("CINEMA pedestrians extra=" + owned.Count + "/20; acquisitions=" + created + "; visible_spawn_rejections=" + rejected + "; traffic=" + CinematicTraffic.Status);
            created = rejected = 0;
        }
    }
}
