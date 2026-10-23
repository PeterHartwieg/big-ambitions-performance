using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UI.Load;

namespace CityPerformance
{
    // Restore native, camera-specific LOD selection only for validated TreeController mesh pairs.
    // Never use the player's distance: the recording camera can be close to a distant tree.
    internal static class TreeLods
    {
        internal static bool Enabled = true, Faulted;
        private static bool installed, inCity, mapHold, announced;
        private static int scan, refresh, skipped, changedElsewhere, setupFrame = -1, earliestFrame;
        private static double nextConflictCheck;
        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly HashSet<LODGroup> seen = new HashSet<LODGroup>();
        private static readonly Dictionary<string, int> skipReasons = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> skipExamples = new Dictionary<string, string>();
        private static readonly MethodInfo start = AccessTools.DeclaredMethod(typeof(TreeController), "Start", Type.EmptyTypes);
        private static readonly MethodInfo map = AccessTools.DeclaredMethod(typeof(TreeController), "ToggleMapLod", new[] { typeof(bool) });
        private static AccessTools.FieldRef<TreeController, LODGroup> groupOf;
        private sealed class Entry
        {
            public LODGroup Group;
            public LOD[] Original, Applied;
            public int VanillaForce;
            public bool Released;
        }

        internal static void Install()
        {
            installed = false; Faulted = false;
            try
            {
                if (!PerformanceMod.Verified || start == null || start.IsStatic || start.ReturnType != typeof(void) ||
                    map == null || !map.IsStatic || map.ReturnType != typeof(void) || Conflict())
                { PerformanceMod.Log("Not active: tree detail (unverified build, signature or conflicting patch)."); return; }
                groupOf = AccessTools.FieldRefAccess<TreeController, LODGroup>("lodGroup");
                PerformanceMod.Harmony.Patch(map, postfix: new HarmonyMethod(typeof(TreeLods), nameof(MapChanged)));
                installed = true;
                PerformanceMod.Log("Active: tree-detail hook; option defaults on; validated two-mesh trees only, no distance disappearance.");
            }
            catch (Exception e)
            {
                Faulted = true;
                if (map != null) PerformanceMod.Harmony.Unpatch(map, AccessTools.Method(typeof(TreeLods), nameof(MapChanged)));
                PerformanceMod.Log("Not active: tree detail: " + e.Message);
            }
        }
        internal static bool Conflict()
        {
            return Foreign(start) || Foreign(map);
        }
        private static bool Foreign(MethodInfo method)
        {
            if (method == null) return true;
            var info = Harmony.GetPatchInfo(method);
            if (info != null) foreach (string owner in info.Owners) if (owner != PerformanceMod.Id) return true;
            return false;
        }
        internal static void StartCity()
        {
            inCity = true; mapHold = CityMap.IsOpen; earliestFrame = Time.frameCount + 1;
            nextConflictCheck = 0; setupFrame = -1;
            GlobalEvents.onCityMapClosed -= MapClosed;
            GlobalEvents.onCityMapClosed += MapClosed;
        }
        internal static void EndCity()
        {
            inCity = false;
            GlobalEvents.onCityMapClosed -= MapClosed;
            ReleaseAll(!Conflict());
            scan = refresh = skipped = changedElsewhere = 0; setupFrame = -1;
            skipReasons.Clear(); skipExamples.Clear(); mapHold = false; announced = false;
        }
        internal static void SetEnabled(bool value)
        {
            Enabled = value;
            if (!value) { ReleaseAll(!Conflict()); scan = refresh = 0; }
            if (inCity) setupFrame = Time.frameCount;
            announced = false;
            PerformanceMod.Log("TREE requested=" + value + "; installed=" + installed + "; faulted=" + Faulted);
        }
        private static void MapChanged(bool open)
        {
            if (!inCity) return;
            try
            {
                if (entries.Count != 0 && Conflict()) { Fail("foreign patch during map transition", false); return; }
                bool lowDetailMap = PlayerPrefSettings.LowDetailCityMap;
                // Record exactly the vanilla writes, including inactive trees and LowDetailCityMap's early return.
                foreach (var entry in entries)
                    if (!entry.Released && entry.Group != null)
                    {
                        entry.VanillaForce = TreeLodPolicy.AfterMap(entry.VanillaForce, entry.Group.gameObject.activeInHierarchy, lowDetailMap, open);
                        // Low-detail map mode returns before vanilla writes anything. Suspend our automatic
                        // selection there too, retaining the previously forced vanilla level. Normal map
                        // mode already made the writes: avoid a second set of calls/LOD-array allocations.
                        if (lowDetailMap)
                        {
                            if (!Same(entry.Group.GetLODs(), entry.Applied)) { entry.Released = true; changedElsewhere++; continue; }
                            entry.Group.ForceLOD(entry.VanillaForce);
                        }
                    }
                // The false toggle is the start of the return animation. Wait for the closed event as well.
                mapHold = true; refresh = 0; announced = false;
            }
            catch (Exception e) { Fail("map transition: " + e.Message, true); }
        }
        private static void MapClosed()
        {
            if (!inCity) return;
            mapHold = false; refresh = 0; announced = false;
        }
        internal static void Tick()
        {
            if (!inCity || !installed || !Enabled || Faulted || !CityEntry.Ready || LoadScene.isLoading || Time.frameCount < earliestFrame) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now >= nextConflictCheck)
                {
                    nextConflictCheck = now + 2;
                    if (Conflict()) { Fail("foreign TreeController patch detected; leaving forced selection to its owner", false); return; }
                    var manager = InstanceBehavior<GameManager>.Instance;
                    if (manager != null && manager.ForceLOD0) { Fail("game's global ForceLOD0 override is enabled", true); return; }
                }
                if (mapHold || CityMap.IsOpen) return;
                long begin = Stopwatch.GetTimestamp();
                // Bounded setup/reapplication, not a per-frame traversal of every tree or every camera.
                int work = 0;
                while (work < 32 && (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency < .75)
                {
                    if (refresh < entries.Count)
                    {
                        var entry = entries[refresh++];
                        if (!entry.Released && entry.Group != null)
                        {
                            if (!Same(entry.Group.GetLODs(), entry.Applied))
                            { entry.Released = true; changedElsewhere++; }
                            else entry.Group.ForceLOD(-1);
                        }
                    }
                    else if (scan < TreeController.AllTrees.Count)
                    {
                        if (scan >= 20000) { Fail("tree registry exceeded the audited safety bound", true); return; }
                        Add(TreeController.AllTrees[scan++]);
                        refresh = entries.Count; // Add already applies its new group.
                    }
                    else break;
                    work++; setupFrame = Time.frameCount;
                }
                if (!announced && refresh == entries.Count && scan == TreeController.AllTrees.Count)
                { announced = true; PerformanceMod.Log(Summary()); }
            }
            catch (Exception e) { Fail(e.Message, true); }
        }
        private static void Add(TreeController tree)
        {
            if (tree == null) { Skip("destroyed"); return; }
            if (tree.GetType() != typeof(TreeController)) { Skip("other-controller", tree.GetType().FullName); return; }
            var group = groupOf(tree);
            if (group == null || !seen.Add(group)) { Skip("missing-or-duplicate-group"); return; }
            if (group.gameObject != tree.gameObject || group.GetComponent<LODCityMapSwitcher>() != null)
            { Skip("other-controller"); return; }
            var original = group.GetLODs();
            if (original.Length != 2 || !TreeLodPolicy.TryThresholds(original[0].screenRelativeTransitionHeight, original[1].screenRelativeTransitionHeight, out float transition))
            { Skip("unsupported-lod-layout"); return; }
            var meshes = new Mesh[2];
            for (int i = 0; i < 2; i++)
            {
                var renderers = original[i].renderers;
                if (renderers == null || renderers.Length != 1 || !(renderers[0] is MeshRenderer renderer) ||
                    !renderer.transform.IsChildOf(tree.transform) || tree.renderersToFade == null || !tree.renderersToFade.Contains(renderer))
                { Skip("renderer-or-fade-layout", tree.name); return; }
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) { Skip("missing-mesh"); return; }
                meshes[i] = filter.sharedMesh;
            }
            if (original[0].renderers[0] == original[1].renderers[0] || !KnownPair(meshes[0], meshes[1]))
            { Skip("unrecognized-mesh-pair", meshes[0].name + " -> " + meshes[1].name); return; }
            var applied = (LOD[])original.Clone();
            applied[0].screenRelativeTransitionHeight = transition;
            applied[1].screenRelativeTransitionHeight = 0; // Keep the last real mesh; never add a new distance-cull boundary.
            var entry = new Entry { Group = group, Original = original, Applied = applied, VanillaForce = 0 };
            entries.Add(entry); // Retain restoration data before the first Unity mutation.
            group.SetLODs(applied);
            group.ForceLOD(-1);
        }
        private static bool KnownPair(Mesh high, Mesh low)
        {
            if (!TreeMeshCatalog.Matches(high.name, high.vertexCount, Indices(high), low.name, low.vertexCount, Indices(low))) return false;
            return true;
        }
        private static long Indices(Mesh mesh)
        {
            long result = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles) return -1;
                result += mesh.GetIndexCount(i);
            }
            return result;
        }
        private static bool Same(LOD[] a, LOD[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].screenRelativeTransitionHeight != b[i].screenRelativeTransitionHeight || a[i].fadeTransitionWidth != b[i].fadeTransitionWidth || a[i].renderers.Length != b[i].renderers.Length) return false;
                for (int j = 0; j < a[i].renderers.Length; j++) if (a[i].renderers[j] != b[i].renderers[j]) return false;
            }
            return true;
        }
        private static void ReleaseAll(bool restoreForce)
        {
            foreach (var entry in entries)
            {
                try
                {
                    if (entry.Released || entry.Group == null) continue;
                    if (!Same(entry.Group.GetLODs(), entry.Applied)) { changedElsewhere++; continue; }
                    entry.Group.SetLODs(entry.Original);
                    if (restoreForce) entry.Group.ForceLOD(entry.VanillaForce);
                }
                catch (Exception e) { Faulted = true; PerformanceMod.Log("TREE restore failed: " + e.Message); }
            }
            entries.Clear(); seen.Clear();
        }
        private static void Fail(string reason, bool restoreForce)
        {
            Faulted = true; ReleaseAll(restoreForce);
            PerformanceMod.Log("TREE disabled: " + reason);
        }
        private static void Skip(string reason, string example = null)
        {
            skipped++; skipReasons.TryGetValue(reason, out int count); skipReasons[reason] = count + 1;
            if (example != null && !skipExamples.ContainsKey(reason)) skipExamples[reason] = example.Length > 160 ? example.Substring(0, 160) : example;
        }
        private static string Summary() => "TREE requested=" + Enabled + "; installed=" + installed + "; faulted=" + Faulted +
            "; registered_seen=" + scan + "; configured_records=" + entries.Count + "; skipped=" + skipped +
            "; changed_elsewhere=" + changedElsewhere + "; map_hold=" + mapHold + "; lod_bias=" + QualitySettings.lodBias +
            "; maximum_lod=" + QualitySettings.maximumLODLevel + "; configured records are not visible-tree or selected-LOD counts.";
        internal static void Report() { if (inCity && Enabled) PerformanceMod.Log(Summary()); }
        internal static void Describe(List<string> output)
        {
            output.Add(Summary());
            foreach (var pair in skipReasons) output.Add("tree_skip=" + pair.Key + "; count=" + pair.Value);
            foreach (var pair in skipExamples) output.Add("tree_skip_example=" + pair.Key + "; example=" + pair.Value);
        }
        internal static void Describe(ref TraceRow row)
        {
            row.TreeRequested = Enabled ? 1 : 0; row.TreeFaulted = Faulted ? 1 : 0;
            row.TreeConfigured = entries.Count; row.TreeMapHold = mapHold ? 1 : 0;
            row.TreeSetup = row.Frame == setupFrame ? 1 : 0;
        }
    }
}
