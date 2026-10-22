using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Culling;
using HarmonyLib;
using UnityEngine;
using UI.Load;

namespace CityPerformance
{
    internal static class CinematicWorld
    {
        internal static bool Enabled = true, AudioEnabled = true, Faulted;
        internal static float DistancePercent = 150;
        internal static Camera RecordingCamera { get; private set; }
        // Coverage is a HART Free Camera feature: without its bridge it changes nothing, including the main view.
        internal static bool BridgeConnected => cameraGetter != null;
        internal static bool Active => CinematicCore.CoverageAllowed(installed, Faulted, Enabled, BridgeConnected) && CityEntry.Ready && !LoadScene.isLoading;
        internal static bool AudioActive => installed && !Faulted && AudioEnabled && CityEntry.Ready && !LoadScene.isLoading && RecordingCamera != null;
        internal static readonly View Main = new View(), Second = new View();
        internal static Vector3 PlayerPosition;
        private static bool installed, audioWasActive, announcedMissing, bridgeBroken;
        private static Func<Camera> cameraGetter;
        private static double nextDiscovery, nextConflict;
        private static int groupCursor, frame = -1, dispatched, scans, transitions, deferred;
        private static double maximumMs;
        private static readonly Dictionary<CullingGroupController, Group> groups = new Dictionary<CullingGroupController, Group>();
        private static readonly MethodInfo dispatch = AccessTools.DeclaredMethod(typeof(CullingGroupController), "DispatchLod", new[] { typeof(ICullable) });
        private static readonly MethodInfo unregister = AccessTools.DeclaredMethod(typeof(CullingGroupController), "UnregisterCullable", new[] { typeof(ICullable) });
        private static readonly MethodInfo dispose = AccessTools.DeclaredMethod(typeof(CullingGroupController), "Dispose", Type.EmptyTypes);
        private static readonly MethodInfo audioPosition = AccessTools.DeclaredMethod(typeof(AudioListenerPositioner), "GetAudioListenerPosition", Type.EmptyTypes);
        private static readonly MethodInfo audioLate = AccessTools.DeclaredMethod(typeof(AudioListenerPositioner), "LateUpdate", Type.EmptyTypes);
        private static AccessTools.FieldRef<CullingGroupController, List<ICullable>> items;
        private static AccessTools.FieldRef<CullingGroupController, Dictionary<ICullable, int>> pending;
        private static AccessTools.FieldRef<CullingGroupController, float> first, second;
        private static AccessTools.FieldRef<CullingGroupController, Helpers.DistributedWork<ICullable>> work;
        private sealed class Entry
        {
            internal int Applied = -1;
            internal double LastNeeded;
            internal bool Owned;
        }
        private sealed class Group
        {
            internal CullingGroupController Owner;
            internal int Cursor;
            internal readonly Dictionary<ICullable, Entry> Entries = new Dictionary<ICullable, Entry>();
        }
        internal sealed class View
        {
            internal Camera Camera;
            internal Vector3 Position;
            internal readonly Plane[] Planes = new Plane[6];
            private float originalFar, appliedFar;
            private bool ownsFar;
            internal void Capture(Camera camera, bool active)
            {
                if (!active) camera = null;
                if (Camera != camera) { Restore(); Camera = camera; }
                if (Camera == null) return;
                if (ownsFar && !Mathf.Approximately(Camera.farClipPlane, appliedFar))
                { ownsFar = false; originalFar = Camera.farClipPlane; }
                float desired = Math.Max(200 * CinematicCore.Scale(DistancePercent), ownsFar ? originalFar : Camera.farClipPlane);
                if (ownsFar || desired > Camera.farClipPlane)
                {
                    if (!ownsFar) originalFar = Camera.farClipPlane;
                    Camera.farClipPlane = appliedFar = desired; ownsFar = true;
                }
                Position = Camera.transform.position;
                // Expand the actual projection, retaining lens shift and orthographic cameras. The cached six planes allocate nothing per frame.
                var projection = Camera.projectionMatrix;
                projection.m00 /= 1.2f; projection.m11 /= 1.2f;
                GeometryUtility.CalculateFrustumPlanes(projection * Camera.worldToCameraMatrix, Planes);
            }
            internal bool Contains(Vector3 position, float radius)
            {
                if (Camera == null) return false;
                for (int i = 0; i < 6; i++) if (!CinematicCore.SphereInside(Planes[i].GetDistanceToPoint(position), radius)) return false;
                return true;
            }
            internal int Band(BoundingSphere sphere, float near, float far, bool nearby)
            {
                if (Camera == null) return 2;
                // Ground coverage remains useful for drone shots; the 3D frustum still rejects scenery outside the shot.
                float dx = sphere.position.x - Position.x, dz = sphere.position.z - Position.z;
                float d = Math.Max(0, Mathf.Sqrt(dx * dx + dz * dz) - sphere.radius);
                int local = nearby ? CinematicCore.Band(d, near, far) : 2;
                int visible = Contains(sphere.position, sphere.radius) ? CinematicCore.Band(d, near * CinematicCore.Scale(DistancePercent), far * CinematicCore.Scale(DistancePercent)) : 2;
                return Math.Min(local, visible);
            }
            internal void Restore()
            {
                if (Camera != null && ownsFar && Mathf.Approximately(Camera.farClipPlane, appliedFar)) Camera.farClipPlane = originalFar;
                ownsFar = false; Camera = null;
            }
        }
        internal static void UnpatchFeature(Type feature)
        {
            var targets = new List<MethodBase>(Harmony.GetAllPatchedMethods());
            foreach (var target in targets)
            {
                var info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                var hooks = new List<MethodInfo>();
                foreach (var patch in info.Prefixes) if (patch.owner == PerformanceMod.Id && patch.PatchMethod.DeclaringType == feature) hooks.Add(patch.PatchMethod);
                foreach (var patch in info.Postfixes) if (patch.owner == PerformanceMod.Id && patch.PatchMethod.DeclaringType == feature) hooks.Add(patch.PatchMethod);
                foreach (var patch in info.Finalizers) if (patch.owner == PerformanceMod.Id && patch.PatchMethod.DeclaringType == feature) hooks.Add(patch.PatchMethod);
                foreach (var hook in hooks) PerformanceMod.Harmony.Unpatch(target, hook);
            }
        }
        internal static bool Foreign(MethodBase method)
        {
            if (method == null) return true;
            var info = Harmony.GetPatchInfo(method);
            if (info != null) foreach (string owner in info.Owners) if (owner != PerformanceMod.Id) return true;
            return false;
        }
        internal static void Install()
        {
            installed = false; Faulted = false;
            try
            {
                if (!PerformanceMod.Verified || Conflict()) { PerformanceMod.Log("Not active: cinematic coverage (unverified build or conflicting hooks)."); return; }
                work = AccessTools.FieldRefAccess<CullingGroupController, Helpers.DistributedWork<ICullable>>("_lodDispatches");
                items = AccessTools.FieldRefAccess<CullingGroupController, List<ICullable>>("_cullables");
                pending = AccessTools.FieldRefAccess<CullingGroupController, Dictionary<ICullable, int>>("_pendingBands");
                first = AccessTools.FieldRefAccess<CullingGroupController, float>("_firstBand"); second = AccessTools.FieldRefAccess<CullingGroupController, float>("_secondBand");
                PerformanceMod.Harmony.Patch(dispatch, prefix: new HarmonyMethod(typeof(CinematicWorld), nameof(BeforeDispatch)));
                PerformanceMod.Harmony.Patch(unregister, postfix: new HarmonyMethod(typeof(CinematicWorld), nameof(Unregistered)));
                PerformanceMod.Harmony.Patch(dispose, prefix: new HarmonyMethod(typeof(CinematicWorld), nameof(Disposed)));
                PerformanceMod.Harmony.Patch(audioPosition, prefix: new HarmonyMethod(typeof(CinematicWorld), nameof(AudioPosition)));
                PerformanceMod.Harmony.Patch(audioLate, postfix: new HarmonyMethod(typeof(CinematicWorld), nameof(AudioLate)));
                installed = true;
                PerformanceMod.Log("Active: cinematic world hooks; coverage and recording-camera audio start only when the HART Free Camera bridge (protocol 1) is loaded.");
            }
            catch (Exception e) { UnpatchFeature(typeof(CinematicWorld)); Fail(e); }
        }
        internal static bool Conflict() => Foreign(dispatch) || Foreign(unregister) || Foreign(dispose) || Foreign(audioPosition) || Foreign(audioLate);
        internal static void SetEnabled(bool value) { Enabled = value; PerformanceMod.Log("CINEMA coverage requested=" + value); }
        internal static bool HasRecordingView() { Discover(); return RecordingCamera != null; }
        private static void Discover()
        {
            if (cameraGetter != null)
            {
                try { RecordingCamera = cameraGetter(); return; }
                catch (Exception e)
                {
                    // A broken bridge counts as absent: coverage and audio fall back to vanilla through the normal off path.
                    cameraGetter = null; RecordingCamera = null; bridgeBroken = true;
                    PerformanceMod.Log("CINEMA HART bridge failed and was dropped for this session; coverage returns to vanilla: " + e.Message);
                    return;
                }
            }
            if (bridgeBroken) { RecordingCamera = null; return; }
            RecordingCamera = null;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextDiscovery) return;
            nextDiscovery = now + 5;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("Hart.FreeCamera.CinematicCameraBridge", false);
                if (type == null || !Equals(type.GetField("ProtocolVersion")?.GetRawConstantValue(), 1)) continue;
                var getter = type.GetProperty("RecordingCamera", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();
                if (getter == null || getter.ReturnType != typeof(Camera)) continue;
                cameraGetter = (Func<Camera>)Delegate.CreateDelegate(typeof(Func<Camera>), getter);
                RecordingCamera = cameraGetter();
                PerformanceMod.Log("CINEMA connected HART recording-camera bridge 1."); break;
            }
            if (cameraGetter == null && !announcedMissing)
            {
                announcedMissing = true;
                PerformanceMod.Log("CINEMA HART Free Camera bridge not loaded; coverage, decal distance, traffic observers and camera audio stay vanilla.");
            }
        }
        private static Group GetGroup(CullingGroupController owner)
        {
            if (!groups.TryGetValue(owner, out var group)) { group = new Group { Owner = owner }; groups.Add(owner, group); }
            return group;
        }
        private static bool Supported(ICullable item) => item != null && item.GetType().Assembly == typeof(CullingManager).Assembly;
        private static bool SoundOnly(ICullable item) => item is RandomClipOnSourceLooped || item is Buildings.Outdoors.BuildingOutsideMusicController;
        private static int Vanilla(Group group, BoundingSphere sphere) => CinematicCore.Band(Math.Max(0, Vector3.Distance(PlayerPosition, sphere.position) - sphere.radius), first(group.Owner), second(group.Owner));
        private static int Desired(Group group, ICullable item, BoundingSphere sphere, int vanilla)
        {
            float near = first(group.Owner), far = second(group.Owner);
            if (SoundOnly(item))
                return AudioActive ? CinematicCore.Band(Math.Max(0, Vector3.Distance(RecordingCamera.transform.position, sphere.position) - sphere.radius), near, far) : vanilla;
            if (!Active) return vanilla;
            return CinematicCore.Combine(vanilla, Second.Band(sphere, near, far, true), Main.Band(sphere, near, far, false));
        }
        private static bool TakeQuota()
        {
            if (frame != Time.frameCount) { frame = Time.frameCount; dispatched = 0; }
            if (dispatched >= 2) { deferred++; return false; }
            dispatched++; return true;
        }
        private static void BeforeDispatch(CullingGroupController __instance, ICullable cullable)
        {
            if (!installed || Faulted || !CityEntry.Ready || LoadScene.isLoading || !Supported(cullable) || (!Active && !AudioActive && !groups.ContainsKey(__instance))) return;
            try
            {
                if (!pending(__instance).TryGetValue(cullable, out int vanilla)) return;
                var group = GetGroup(__instance);
                if (!group.Entries.TryGetValue(cullable, out var entry)) { entry = new Entry(); group.Entries.Add(cullable, entry); }
                int wanted = Desired(group, cullable, cullable.GetCullingBoundingSphere(), vanilla);
                wanted = CinematicCore.Hold(wanted, entry.Applied < 0 ? vanilla : entry.Applied, ref entry.LastNeeded, Time.realtimeSinceStartupAsDouble, Active || AudioActive);
                if (wanted != vanilla && wanted != entry.Applied && !TakeQuota()) wanted = entry.Applied >= 0 ? entry.Applied : vanilla;
                pending(__instance)[cullable] = wanted;
                entry.Applied = wanted; entry.Owned = wanted != vanilla;
            }
            catch (Exception e) { Fail(e); }
        }
        private static void Unregistered(CullingGroupController __instance, ICullable cullable)
        { if (groups.TryGetValue(__instance, out var g)) g.Entries.Remove(cullable); }
        private static void Disposed(CullingGroupController __instance) { groups.Remove(__instance); }
        private static void QueueVanilla(Group group, ICullable item, int band)
        {
            if (pending(group.Owner).ContainsKey(item)) return;
            pending(group.Owner)[item] = band;
            work(group.Owner).Enqueue(item);
        }
        private static void Apply(ICullable item, int band)
        { if (band == 0) item.OnLod0(); else if (band == 1) item.OnLod1(); else item.OnLod2(); }
        internal static void Tick()
        {
            if (!installed || !CityEntry.Ready || LoadScene.isLoading) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                Discover();
                var player = GameManager.Instance?.playerController;
                if (player == null) return;
                PlayerPosition = player.transform.position;
                if (!Faulted && Time.realtimeSinceStartupAsDouble >= nextConflict)
                {
                    nextConflict = Time.realtimeSinceStartupAsDouble + 2;
                    if (Conflict()) { Faulted = true; PerformanceMod.Log("CINEMA disabled: foreign hook detected; yielding to its owner."); }
                }
                Main.Capture(GameManager.GetMainCamera(), Active && !CityMap.IsOpen);
                Second.Capture(RecordingCamera, Active);
                ApplyAudio();
                CinematicAudioZones.Tick();
                CinematicPopulation.Tick();
                if (!Active && !AudioActive)
                {
                    bool remaining = false;
                    foreach (var g in groups.Values) foreach (var e in g.Entries.Values) if (e.Owned) { remaining = true; break; }
                    if (!remaining) { groups.Clear(); return; }
                }
                var manager = InstanceBehavior<CullingManager>.Instance;
                if (manager == null) return;
                var a = manager.generalCullingGroupController; var b = manager.hamptonsHousesCullingGroupController;
                for (int i = 0; i < 128; i++)
                {
                    if ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= .75) break;
                    var owner = ((groupCursor++ & 1) == 0 || b == null) ? a : b;
                    if (owner == null) continue;
                    var group = GetGroup(owner); var list = items(owner);
                    if (list.Count == 0) continue;
                    if (group.Cursor >= list.Count) group.Cursor = 0;
                    var item = list[group.Cursor++]; scans++;
                    if (!Supported(item) || item is UnityEngine.Object u && u == null) continue;
                    if (!Active && !SoundOnly(item) && !group.Entries.ContainsKey(item)) continue;
                    var sphere = item.GetCullingBoundingSphere();
                    int vanilla = Vanilla(group, sphere);
                    if (Faulted)
                    {
                        if (group.Entries.TryGetValue(item, out var old) && old.Owned && TakeQuota()) { QueueVanilla(group, item, vanilla); old.Owned = false; old.Applied = vanilla; }
                        continue;
                    }
                    if (!group.Entries.TryGetValue(item, out var entry)) { entry = new Entry { Applied = vanilla }; group.Entries.Add(item, entry); }
                    int desired = Desired(group, item, sphere, vanilla);
                    desired = CinematicCore.Hold(desired, entry.Applied, ref entry.LastNeeded, Time.realtimeSinceStartupAsDouble, Active || AudioActive);
                    if (desired == entry.Applied || pending(owner).ContainsKey(item)) continue;
                    if (!TakeQuota()) continue;
                    Apply(item, desired); entry.Applied = desired; entry.Owned = desired != vanilla; transitions++;
                }
            }
            catch (Exception e) { Fail(e); try { CinematicPopulation.Tick(); } catch (Exception cleanup) { PerformanceMod.Log("CINEMA population recovery: " + cleanup.Message); } }
            finally { maximumMs = Math.Max(maximumMs, (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); }
        }
        private static bool AudioPosition(ref Vector3 __result)
        {
            if (!AudioActive) return true;
            __result = RecordingCamera.transform.position; return false;
        }
        private static void AudioLate(AudioListenerPositioner __instance)
        { if (AudioActive && __instance != null) __instance.transform.SetPositionAndRotation(RecordingCamera.transform.position, RecordingCamera.transform.rotation); }
        private static void ApplyAudio()
        {
            var listener = AudioListenerPositioner.Instance;
            if (listener != null)
            {
                if (AudioActive) AudioLate(listener);
                else if (audioWasActive)
                {
                    var main = GameManager.GetMainCamera();
                    listener.transform.position = AudioListenerPositioner.GetAudioListenerPosition();
                    if (main != null) listener.transform.rotation = main.transform.rotation;
                }
            }
            if (audioWasActive != AudioActive) PerformanceMod.Log("CINEMA audio=" + (AudioActive ? "recording camera" : "player"));
            audioWasActive = AudioActive;
        }
        private static void Fail(Exception e) { Faulted = true; PerformanceMod.Log("CINEMA disabled after error: " + e); }
        private static void Cleanup(Action action) { try { action(); } catch (Exception e) { PerformanceMod.Log("CINEMA cleanup: " + e); } }
        internal static void EndCity(bool restore = false)
        {
            bool wasEnabled = Enabled, wasAudio = AudioEnabled;
            Enabled = false; AudioEnabled = false;
            try
            {
                Cleanup(ApplyAudio); Cleanup(CinematicAudioZones.End); Cleanup(CinematicPopulation.End);
                Cleanup(Main.Restore); Cleanup(Second.Restore);
                if (restore) foreach (var g in groups.Values) foreach (var pair in g.Entries)
                    if (pair.Value.Owned && !(pair.Key is UnityEngine.Object u && u == null))
                    { try { QueueVanilla(g, pair.Key, Vanilla(g, pair.Key.GetCullingBoundingSphere())); } catch (Exception e) { PerformanceMod.Log("CINEMA restore: " + e.Message); } }
            }
            finally { Enabled = wasEnabled; AudioEnabled = wasAudio; groups.Clear(); RecordingCamera = null; scans = transitions = deferred = 0; maximumMs = 0; }
        }
        internal static bool Visible(Vector3 point, float radius) => Main.Contains(point, radius) || Second.Contains(point, radius);
        internal static void Describe(List<string> output)
        {
            output.Add("cinematic installed=" + installed + "; requested=" + Enabled + "; faulted=" + Faulted + "; active=" + Active + "; distance_pct=" + DistancePercent + "; bridge=" + (cameraGetter != null) + "; recording=" + (RecordingCamera != null) + "; audio_requested=" + AudioEnabled + "; audio_active=" + AudioActive);
            output.Add("cinematic traffic=" + CinematicTraffic.Status + "; extra_pedestrian_cap=20; lod_callbacks_per_frame=2; scan_limit=128; world_tick_max_ms_since_summary=" + maximumMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
        internal static void Report()
        {
            if (!BridgeConnected || !Enabled && !audioWasActive) return;
            PerformanceMod.Log("CINEMA active=" + Active + "; recording=" + (RecordingCamera != null) + "; distance_pct=" + DistancePercent + "; audio=" + AudioActive + "; scans=" + scans + "; transitions=" + transitions + "; deferred=" + deferred + "; max_ms=" + maximumMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            scans = transitions = deferred = 0; maximumMs = 0;
        }
    }
}
