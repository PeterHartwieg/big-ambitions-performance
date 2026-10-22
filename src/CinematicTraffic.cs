using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using GleyTrafficSystem;
using GleyUrbanAssets;
using HarmonyLib;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CityPerformance
{
    internal static class CinematicTraffic
    {
        internal static string Status => faulted ? "disabled by guard" : owner != null ? "camera coverage; native vehicle cap" : "vanilla";
        private static readonly MethodInfo update = AccessTools.DeclaredMethod(typeof(TrafficManager), "UpdateCamera", new[] { typeof(Transform[]) });
        private static readonly MethodInfo density = AccessTools.DeclaredMethod(typeof(DensityManager), "UpdateVehicleDensity", new[] { typeof(Vector3), typeof(Vector3), typeof(int) });
        private static readonly MethodInfo visible = AccessTools.DeclaredMethod(typeof(DensityManager), "IsPositionOnScreen", new[] { typeof(Vector3), typeof(float) });
        private static readonly MethodInfo fixedUpdate = AccessTools.DeclaredMethod(typeof(TrafficManager), "FixedUpdate", Type.EmptyTypes);
        private static AccessTools.FieldRef<TrafficManager, Transform[]> cameras;
        private static AccessTools.FieldRef<TrafficManager, NativeArray<float3>> positions;
        private static AccessTools.FieldRef<TrafficManager, GridManager> grid;
        private static AccessTools.FieldRef<TrafficManager, DensityManager> densityOf;
        private static AccessTools.FieldRef<TrafficManager, int> level;
        private static AccessTools.FieldRef<TrafficManager, bool> initialized;
        private static Action<GridManager, int, NativeArray<float3>> updateGrid;
        private static Func<GridManager, Vector3, Vector2Int> getCellIndex;
        private static AccessTools.FieldRef<GridManager, NativeArray<float3>> gridPositions;
        private static AccessTools.FieldRef<DensityManager, PositionValidator> validator;
        private static AccessTools.FieldRef<PositionValidator, Transform[]> validatorCameras;
        private static TrafficManager owner;
        private static Transform[] original, applied;
        private static GameObject pivotRoot;
        private static readonly Transform[] pivots = new Transform[6];
        private static bool installed, faulted;
        private static double nextConflict;
        private static bool Conflict() => CinematicWorld.Foreign(update) || CinematicWorld.Foreign(density) || CinematicWorld.Foreign(visible) || CinematicWorld.Foreign(fixedUpdate);
        internal static void Install()
        {
            try
            {
                if (!PerformanceMod.Verified || Conflict()) return;
                using (var hash = SHA256.Create()) using (var file = File.OpenRead(typeof(TrafficManager).Assembly.Location))
                    if (BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != "7d43e9d42d2f464674cff045bcb048c48e0f87aec0a987baa34122ac6e3415d1") return;
                gridPositions = AccessTools.FieldRefAccess<GridManager, NativeArray<float3>>("activeCameraPositions");
                validator = AccessTools.FieldRefAccess<DensityManager, PositionValidator>("positionValidator");
                validatorCameras = AccessTools.FieldRefAccess<PositionValidator, Transform[]>("activeCameras");
                cameras = AccessTools.FieldRefAccess<TrafficManager, Transform[]>("activeCameras");
                positions = AccessTools.FieldRefAccess<TrafficManager, NativeArray<float3>>("activeCameraPositions");
                densityOf = AccessTools.FieldRefAccess<TrafficManager, DensityManager>("densityManager");
                grid = AccessTools.FieldRefAccess<TrafficManager, GridManager>("gridManager");
                level = AccessTools.FieldRefAccess<TrafficManager, int>("activeSquaresLevel");
                initialized = AccessTools.FieldRefAccess<TrafficManager, bool>("initialized");
                updateGrid = AccessTools.MethodDelegate<Action<GridManager, int, NativeArray<float3>>>(AccessTools.DeclaredMethod(typeof(GridManager), "UpdateGrid", new[] { typeof(int), typeof(NativeArray<float3>) }));
                getCellIndex = AccessTools.MethodDelegate<Func<GridManager, Vector3, Vector2Int>>(AccessTools.DeclaredMethod(typeof(GridManager), "GetCellIndex", new[] { typeof(Vector3) }));
                PerformanceMod.Harmony.Patch(update, prefix: new HarmonyMethod(typeof(CinematicTraffic), nameof(BeforeCamera)));
                PerformanceMod.Harmony.Patch(density, prefix: new HarmonyMethod(typeof(CinematicTraffic), nameof(BeforeDensity)));
                PerformanceMod.Harmony.Patch(visible, postfix: new HarmonyMethod(typeof(CinematicTraffic), nameof(OnScreen)));
                installed = true;
            }
            catch (Exception e) { CinematicWorld.UnpatchFeature(typeof(CinematicTraffic)); faulted = true; PerformanceMod.Log("CINEMA traffic unavailable: " + e); }
        }
        private static bool BeforeCamera(TrafficManager __instance, Transform[] activeCameras)
        {
            if (__instance != owner || !initialized(__instance)) return true;
            if (activeCameras == null || activeCameras.Length == 0) return true;
            if (faulted) { original = activeCameras; End(); return false; }
            var next = Combine(activeCameras);
            if (SwapCameras(next)) { original = activeCameras; applied = next; return false; }
            // Retain this request as the restoration target, without invoking the unsafe native resize.
            original = activeCameras; End(); return false;
        }
        private static bool SwapCameras(Transform[] targets)
        {
            var previous = positions(owner); var previousCameras = cameras(owner);
            var gridOwner = grid(owner); var validatorOwner = validator(densityOf(owner));
            var previousValidation = validatorCameras(validatorOwner);
            var next = default(NativeArray<float3>);
            bool assigned = false;
            try
            {
                foreach (var target in targets)
                {
                    if (target == null) throw new InvalidOperationException("Missing traffic observer");
                    getCellIndex(gridOwner, target.position);
                }
                next = new NativeArray<float3>(targets.Length, Allocator.Persistent);
                for (int i = 0; i < next.Length; i++) next[i] = targets[i].position;
                // These fields are the complete native UpdateCamera/PositionValidator handoff. No callback can observe mixed lengths.
                positions(owner) = next; cameras(owner) = targets;
                validatorCameras(validatorOwner) = targets; gridPositions(gridOwner) = next; assigned = true;
                updateGrid(gridOwner, level(owner), next);
            }
            catch (Exception e)
            {
                faulted = true;
                if (assigned)
                {
                    // Restore buffer references first, independently of grid callbacks that can themselves throw.
                    positions(owner) = previous; cameras(owner) = previousCameras;
                    validatorCameras(validatorOwner) = previousValidation; gridPositions(gridOwner) = previous;
                    try { updateGrid(gridOwner, level(owner), previous); }
                    catch (Exception rollback) { PerformanceMod.Log("CINEMA grid-cell refresh failed after coherent buffer rollback: " + rollback.Message); }
                }
                DisposeBuffer(next);
                PerformanceMod.Log("CINEMA traffic swap disabled: " + e.Message);
                return false;
            }
            // Commit has completed. Disposal errors must never roll back to a buffer that may already have been freed.
            DisposeBuffer(previous);
            return true;
        }
        private static void DisposeBuffer(NativeArray<float3> buffer)
        {
            if (!buffer.IsCreated) return;
            try { buffer.Dispose(); }
            catch (Exception e) { faulted = true; PerformanceMod.Log("CINEMA traffic buffer disposal failed: " + e.Message); }
        }
        private static Transform[] Combine(Transform[] baseline)
        {
            var all = new Transform[baseline.Length + pivots.Length];
            Array.Copy(baseline, all, baseline.Length); Array.Copy(pivots, 0, all, baseline.Length, pivots.Length);
            return all;
        }
        private static void BeforeDensity(DensityManager __instance, ref Vector3 playerPosition, ref Vector3 playerDirection, int activeCameraIndex)
        {
            if (owner == null || __instance != densityOf(owner) || !CinematicWorld.Active) return;
            var all = cameras(owner);
            if (activeCameraIndex < 0 || activeCameraIndex >= all.Length || all[activeCameraIndex] == null) return;
            playerPosition = all[activeCameraIndex].position; playerDirection = all[activeCameraIndex].forward;
        }
        private static void OnScreen(DensityManager __instance, Vector3 position, ref bool __result)
        { if (owner != null && __instance == densityOf(owner) && CinematicWorld.Active) __result |= CinematicWorld.Visible(position, 8); }
        private static Vector3 SafePoint(Vector3 point)
        {
            // Native grid access is not clamped at the world border. Invalid camera positions fall back to the player.
            try { getCellIndex(grid(owner), point); return point; }
            catch (IndexOutOfRangeException) { return CinematicWorld.PlayerPosition; }
        }
        private static void PositionPivots()
        {
            PositionView(CinematicWorld.Main, 0);
            PositionView(CinematicWorld.Second, 3);
        }
        private static void PositionView(CinematicWorld.View view, int offset)
        {
            var near = view.Camera == null ? CinematicWorld.PlayerPosition : view.Position;
            near.y = CinematicWorld.PlayerPosition.y;
            var far = CinematicPopulation.Focus(view);
            // At 200% the ground focus can be 200m away. A midpoint keeps the native 80m removal circles overlapping.
            pivots[offset].position = SafePoint(near);
            pivots[offset + 1].position = SafePoint(Vector3.Lerp(near, far, .5f));
            pivots[offset + 2].position = SafePoint(far);
            for (int i = 0; i < 3; i++) pivots[offset + i].rotation = view.Camera == null ? Quaternion.identity : view.Camera.transform.rotation;
        }
        internal static void Tick(bool active)
        {
            if (!installed) return;
            if (!active || faulted) { if (owner != null) End(); return; }
            if (Time.realtimeSinceStartupAsDouble >= nextConflict)
            { nextConflict = Time.realtimeSinceStartupAsDouble + 2; if (Conflict()) { faulted = true; End(); PerformanceMod.Log("CINEMA traffic disabled: foreign patch detected."); return; } }
            if (owner == null)
            {
                if (!TrafficManager.HasInstance) return;
                var candidate = TrafficManager.Instance;
                if (!initialized(candidate)) return;
                var baseline = cameras(candidate);
                if (baseline == null || baseline.Length != 1 || baseline[0] == null) return;
                owner = candidate; original = baseline;
                pivotRoot = new GameObject("CityPerformance.TrafficObservers");
                for (int i = 0; i < pivots.Length; i++)
                {
                    var child = new GameObject("Observer" + i); child.transform.SetParent(pivotRoot.transform, false); pivots[i] = child.transform;
                }
                PositionPivots(); applied = Combine(original);
                if (!SwapCameras(applied)) { End(); return; }
                PerformanceMod.Log("CINEMA traffic observers attached; native population/removal radius unchanged; grid and buffer ownership repaired.");
            }
            PositionPivots();
        }
        internal static void End()
        {
            // All jobs in the audited FixedUpdate complete synchronously. Keep pivots alive until no native camera array references them.
            if (owner != null && initialized(owner))
            {
                var current = cameras(owner);
                Transform[] target = original;
                if (!ReferenceEquals(current, applied))
                {
                    var kept = new List<Transform>();
                    foreach (var camera in current) if (Array.IndexOf(pivots, camera) < 0) kept.Add(camera);
                    target = kept.Count > 0 ? kept.ToArray() : original;
                }
                if (target != null && !SwapCameras(target)) return;
            }
            owner = null; original = applied = null;
            if (pivotRoot != null) UnityEngine.Object.Destroy(pivotRoot);
            pivotRoot = null; for (int i = 0; i < pivots.Length; i++) pivots[i] = null;
        }
    }
}
