using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Mods;
using HarmonyLib;
using UnityEngine;
using UI.Load;
using Object = UnityEngine.Object;

[assembly: RegisterModClass(typeof(CityPerformance.PerformanceMod))]
[assembly: RegisterModClass(typeof(CityPerformance.CityEntry))]
namespace CityPerformance
{
    [ModEntryOnInitializationLoad]
    public sealed class PerformanceMod : IModBigAmbitions
    {
        internal const string Id = "cityperformance.mod";
        internal const string Version = "0.5.0";
        // Windows build 3682. Do not add another hash without re-auditing every behavioral hook against it.
        internal const string AuditedGameHash = "9427561b81aaab2ada09c22a3c98dd9a4017a65225b389513885bf336852e911";
        internal static readonly Harmony Harmony = new Harmony(Id);
        internal static IModLogger Logger;
        internal static volatile bool Diagnostics = false, Smoothing = true, Verified, Faulted, Loaded;
        private string modId;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();
        public Task OnLoadAsync(ModContext context)
        {
            Logger = context.Logger; modId = context.ModId;
            Loaded = true; Faulted = false; Verified = false;
            // Saved choices, read per feature so one broken feature cannot block the others' settings.
            Step("preferences", () => { Diagnostics = Pref("diagnostics", 0); Smoothing = Pref("refill", 1); });
            Step("preferences", () => { ZoneScheduling.Enabled = Pref("zones", 1); ZoneScheduling.Faulted = false; });
            Step("preferences", () => { FrameTrace.Recording = Pref("trace", 0); FrameTrace.AutoExport = Pref("autoexport", 0); });
            Step("preferences", () => DetailedTrace.Enabled = Pref("details", 0));
            Step("preferences", () =>
            {
                CinematicWorld.Enabled = Pref("cinematic", 1);
                CinematicWorld.AudioEnabled = Pref("cameraaudio", 1);
                CinematicWorld.DistancePercent = UnityEngine.PlayerPrefs.GetInt(Key("cinematicdistance"), 150);
            });
            Step("preferences", () => TreeLods.Enabled = Pref("trees", 1));
            Step("preferences", () => HyperrealDiscovery.Enabled = Pref("hyperrealdiscovery", 1));
            Step("preferences", () => BakeryHelp.Enabled = Pref("bakeryhelp", 1));
            Step("preferences", () =>
            {
                MidnightCleanup.Enabled = Pref("cleanup", 1);
                MidnightCleanup.SetDelayMinutes(UnityEngine.PlayerPrefs.GetInt(Key("cleanupminutes"), 5));
            });
            Step("preferences", () => BackgroundSaves.Enabled = Pref("backgroundsaves", 1));
            Step("quit hooks", () =>
            {
                Application.quitting -= BackgroundSaves.OnApplicationQuit;
                Application.quitting += BackgroundSaves.OnApplicationQuit;
                Application.quitting -= FrameTrace.OnApplicationQuit;
                Application.quitting += FrameTrace.OnApplicationQuit;
            });
            // Each step is isolated: a game update that breaks one feature must not take the others down,
            // and a missing game type surfaces as one logged exception, not a failed mod load.
            // Presentation-only and fail-open; deliberately independent of the behavioral hash gate.
            Step("option button captions", OptionButtonCaptions.Start);
            Step("options", RegisterOptions);
            Step("game hash", ReadGameHash);
            Step("pedestrian refill", () => { try { Refill.Install(); } catch { Faulted = true; throw; } });
            Step("stationary scheduling", () => ZoneScheduling.Install());
            Step("midnight cleanup", () => MidnightCleanup.Install());
            Step("background autosaves", () => BackgroundSaves.Install());
            Step("tree detail", () => TreeLods.Install());
            Step("Hyperreal discovery", () => HyperrealDiscovery.Initialize());
            Step("Bakery Help", () => BakeryHelp.Initialize());
            Step("cinematic world", () => CinematicWorld.Install());
            Step("cinematic audio zones", () => CinematicAudioZones.Install());
            Step("cinematic population", () => CinematicPopulation.Install());
            Step("cinematic decals", () => CinematicDecals.Install());
            Step("frame trace", () => FrameTrace.Install());
            Step("detailed diagnostics", () => DetailedTrace.Initialize());
            Step("timing", () => Timings.Install(typeof(PedestrianSpawner), "SetActivePedestrians", new[] { typeof(int) }));
            Step("timing", () => Timings.Install(typeof(GameManager), "RunMainGameTick", new[] { typeof(float) }));
            Step("timing", () => Timings.Install(typeof(SaveGameManager), "SerializeSaveGame", new[] { typeof(object) }));
            Step("timing", () => Timings.Install(typeof(Culling.CullingGroupController), "Update", Type.EmptyTypes));
            Step("timing", () => Timings.Install(typeof(GleyTrafficSystem.TrafficManager), "FixedUpdate", Type.EmptyTypes));
            return Task.CompletedTask;
        }
        private string Key(string option) => "m:" + modId + ":" + option;
        private bool Pref(string option, int fallback) => UnityEngine.PlayerPrefs.GetInt(Key(option), fallback) != 0;
        // A missing or changed game type after an update: the feature is gone, the rest must carry on.
        internal static bool LoadFailure(Exception e) => e is TypeInitializationException || e is TypeLoadException ||
            e is MissingMemberException || e is FileNotFoundException || e is BadImageFormatException;
        // Lifecycle and per-frame calls: contain only load failures. Game exceptions keep propagating as before.
        internal static bool Contain(string name, Action action)
        {
            try { action(); return true; }
            catch (Exception e) when (LoadFailure(e)) { Log("Not active: " + name + " (game build changed; original game behavior kept): " + e); return false; }
        }
        internal static void Step(string name, Action action)
        {
            try { action(); }
            catch (Exception e) { Log("Not active: " + name + " (setup failed; original game behavior kept): " + e); }
        }
        private static void ReadGameHash()
        {
            using (var hash = SHA256.Create())
            using (var file = File.OpenRead(typeof(PedestrianSpawner).Assembly.Location))
            {
                var actual = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                Verified = actual == AuditedGameHash;
                Log("Version " + Version + "; BigAmbitions SHA256=" + actual + "; verified=" + Verified +
                    (Verified ? "" : "; game build not audited, so behavioral changes stay off and only timing/trace hooks and the presentation-only button-caption hook run"));
            }
        }
        private void RegisterOptions()
        {
                OptionsService.Register(modId, new ModOptions()
                    .AddHeader("cityperformance:header")
                    .AddToggle("diagnostics", "cityperformance:diagnostics", false, x => Diagnostics = x)
                    .AddToggle("refill", "cityperformance:refill", true, x => { Smoothing = x; Log("Pedestrian-refill smoothing requested="+x+"; verified="+Verified+"; faulted="+Faulted); })
                    .AddToggle("zones", "cityperformance:zones", true, x => ZoneScheduling.Enabled = x)
                    .AddToggle("trees", "cityperformance:trees", true, TreeLods.SetEnabled)
                    .AddToggle("hyperrealdiscovery", "cityperformance:hyperrealdiscovery", true, HyperrealDiscovery.SetEnabled)
                    .AddToggle("bakeryhelp", "cityperformance:bakeryhelp", true, BakeryHelp.SetEnabled)
                    .AddToggle("cinematic", "cityperformance:cinematic", true, CinematicWorld.SetEnabled)
                    .AddSlider("cinematicdistance", "cityperformance:cinematicdistance", 100, 200, 150, x => CinematicWorld.DistancePercent = x, "cityperformance:percent")
                    .AddToggle("cameraaudio", "cityperformance:cameraaudio", true, x => CinematicWorld.AudioEnabled = x)
                    .AddToggle("cleanup", "cityperformance:cleanup", true, MidnightCleanup.SetEnabled)
                    .AddSlider("cleanupminutes", "cityperformance:cleanupminutes", 1, 10, 5, MidnightCleanup.SetDelayMinutes, "cityperformance:minutes")
                    .AddToggle("backgroundsaves", "cityperformance:backgroundsaves", true, BackgroundSaves.SetEnabled)
                    .AddToggle("trace", "cityperformance:trace", false, x => FrameTrace.Recording = x)
                    .AddToggle("autoexport", "cityperformance:autoexport", false, x => FrameTrace.AutoExport = x)
                    .AddToggle("details", "cityperformance:details", false, DetailedTrace.SetEnabled)
                    .AddButton(OptionButtonCaptions.ExportLabelKey, FrameTrace.RequestExport));
        }
        public Task OnUnloadAsync()
        {
            try
            {
                // This adapter owns a separate Harmony id; deactivate even if another feature's drain fails.
                Contain("Hyperreal discovery", () => HyperrealDiscovery.Shutdown());
                Contain("Bakery Help", () => BakeryHelp.Shutdown());
                Contain("background autosaves", () => BackgroundSaves.Stop());
                Loaded = false;
                try { Contain("pedestrian refill", () => Refill.Flush()); CityEntry.Stop(false); }
                finally
                {
                    try { Contain("midnight cleanup", () => MidnightCleanup.Shutdown()); }
                    finally
                    {
                        Contain("detailed diagnostics", () => DetailedTrace.Shutdown());
                        Contain("quit hooks", () =>
                        {
                            Application.quitting -= FrameTrace.OnApplicationQuit;
                            Application.quitting -= BackgroundSaves.OnApplicationQuit;
                        });
                        try { Harmony.UnpatchAll(Id); } catch (Exception e) { Log("Unpatch failed: " + e); }
                        try { if (modId != null) OptionsService.RemoveModOptions(modId); }
                        catch (Exception e) { Log("Options removal failed: " + e.Message); }
                    }
                }
            }
            finally
            {
                // Keep captions installed through options removal, including its native page rebuild.
                // Even an earlier teardown exception must withdraw this separate presentation owner.
                try { OptionButtonCaptions.Stop(); } catch (Exception e) { Log("Button caption cleanup failed: " + e); }
            }
            return Task.CompletedTask;
        }
        internal static void Log(string text) { try { Logger?.Info(text); } catch { /* Diagnostics cannot break gameplay. */ } }
    }

    [ModEntryOnCityLoad]
    public sealed class CityEntry : IModBigAmbitions
    {
        private static GameObject host;
        private static PerformanceHost runtime;
        private static readonly SessionLifetime lifetime = new SessionLifetime();
        private static int startedTicket;
        private int ticket;
        private Action unloadCallback;
        internal static bool Ready => runtime != null && runtime.isActiveAndEnabled && PerformanceMod.Loaded;
        internal static bool IsCurrent(PerformanceHost candidate) => Ready && runtime == candidate;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();
        public Task OnLoadAsync(ModContext context)
        {
            Stop();
            ticket = lifetime.Begin();
            int capturedTicket = ticket;
            unloadCallback = () => { if (lifetime.IsCurrent(capturedTicket)) Stop(); };
            GlobalEvents.onGameUnloaded += unloadCallback;
            GlobalEvents.onNewHour += FrameTrace.Hour;
            GlobalEvents.onSaveGame += FrameTrace.Save;
            // This API may invoke synchronously, or retain the callback until later. It has no unregister API.
            GlobalEvents.RegisterOnGameLoadedLateCallback(() => Start(capturedTicket));
            return Task.CompletedTask;
        }
        private static void Start(int expectedTicket)
        {
            // Kept free of game types so a changed game build fails inside the guard, not in the game's callback.
            try { StartCity(expectedTicket); }
            catch (Exception e) { PerformanceMod.Log("City host unavailable: " + e); try { Stop(); } catch { /* Already logged. */ } }
        }
        private static void StartCity(int expectedTicket)
        {
            if (!PerformanceMod.Loaded || !lifetime.IsCurrent(expectedTicket) || startedTicket == expectedTicket ||
                LoadScene.isLoading || SaveGameManager.Current == null) return;
            try
            {
                host = new GameObject("CityPerformance.Runtime");
                Object.DontDestroyOnLoad(host); runtime = host.AddComponent<PerformanceHost>();
                startedTicket = expectedTicket;
                PerformanceMod.Step("midnight cleanup", () => MidnightCleanup.StartCity());
                PerformanceMod.Step("background autosaves", () => BackgroundSaves.StartCity());
                PerformanceMod.Step("detailed diagnostics", () => DetailedTrace.StartCity());
                PerformanceMod.Step("tree detail", () => TreeLods.StartCity());
                PerformanceMod.Step("Hyperreal discovery", () => HyperrealDiscovery.StartCity());
                PerformanceMod.Step("Bakery Help", () => BakeryHelp.StartCity());
                PerformanceMod.Contain("city-ready status", () =>
                    PerformanceMod.Log("City ready; diagnostics=" + PerformanceMod.Diagnostics + "; trace=" + FrameTrace.Recording + "; autoexport=" + FrameTrace.AutoExport + "; zones requested=" + ZoneScheduling.Enabled + "; refill requested=" + PerformanceMod.Smoothing + "; trace directory=" + System.IO.Path.Combine(Application.persistentDataPath,"CityPerformanceTraces")));
            }
            catch (Exception e) { Stop(); PerformanceMod.Log("City host failed: " + e); }
        }
        internal static void Stop(bool cancelZones = true)
        {
            PerformanceMod.Contain("Hyperreal discovery", () => HyperrealDiscovery.EndCity());
            PerformanceMod.Contain("Bakery Help", () => BakeryHelp.EndCity());
            try { PerformanceMod.Contain("midnight cleanup", () => MidnightCleanup.EndCity()); }
            finally { StopAfterCleanup(cancelZones); }
        }
        private static void StopAfterCleanup(bool cancelZones)
        {
            PerformanceMod.Contain("background autosaves", () => BackgroundSaves.Drain());
            PerformanceMod.Contain("frame trace", () => FrameTrace.FinishSession("city-or-mod-unload"));
            PerformanceMod.Contain("tree detail", () => TreeLods.EndCity());
            PerformanceMod.Contain("cinematic world", () => CinematicWorld.EndCity(cancelZones == false));
            lifetime.End();
            PerformanceMod.Contain("pedestrian refill", () => Refill.Cancel());
            if(cancelZones) PerformanceMod.Contain("stationary scheduling", () => ZoneScheduling.CancelAll());
            PerformanceMod.Contain("frame trace", () => FrameTrace.Reset());
            // Unity Destroy is deferred: disable and detach first so an old host cannot tick a new city's work.
            var oldHost = host; var oldRuntime = runtime;
            host = null; runtime = null;
            if (oldRuntime != null) oldRuntime.enabled = false;
            if (oldHost != null) Object.Destroy(oldHost);
            Timings.Reset();
        }
        public Task OnUnloadAsync()
        {
            if (unloadCallback != null) GlobalEvents.onGameUnloaded -= unloadCallback;
            GlobalEvents.onNewHour -= FrameTrace.Hour;
            GlobalEvents.onSaveGame -= FrameTrace.Save;
            if (lifetime.IsCurrent(ticket))
            {
                // Entry-unload order is unspecified. Flush here too when a mod is removed in an active city.
                // Actual city teardown uses the game-unloaded callback above and cancels instead.
                PerformanceMod.Contain("pedestrian refill", () => Refill.Flush());
                Stop(false);
            }
            return Task.CompletedTask;
        }
    }

    internal static class Refill
    {
        private static readonly RefillPlan plan = new RefillPlan();
        private static PedestrianSpawner owner;
        private static bool bypass;
        private static long revision;
        private struct RequestState { public int Target; public bool Restore; public long Revision; }
        private static MethodInfo method;
        private static AccessTools.FieldRef<PedestrianSpawner, int> lastTarget;
        private static readonly FrameQuota quota = new FrameQuota(2);
        internal static void Install()
        {
            method = AccessTools.Method(typeof(PedestrianSpawner), "SetActivePedestrians", new[] { typeof(int) });
            if (!PerformanceMod.Verified || method == null || method.IsStatic || method.ReturnType != typeof(void) || method.DeclaringType != typeof(PedestrianSpawner) || ForeignPatch())
            { PerformanceMod.Log("Not active: pedestrian refill (unverified build or conflicting patch)."); return; }
            lastTarget = AccessTools.FieldRefAccess<PedestrianSpawner, int>("_lastSetAmount");
            PerformanceMod.Harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(Refill), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(Refill), nameof(Finalizer)));
            PerformanceMod.Log("Active: PedestrianSpawner.SetActivePedestrians refill hook; optimization defaults on.");
        }
        private static bool ForeignPatch()
        {
            var info = Harmony.GetPatchInfo(method);
            return info != null && info.Owners.Any(x => x != PerformanceMod.Id);
        }
        private static void Prefix(PedestrianSpawner __instance, ref int amount, out RequestState __state)
        {
            __state = default;
            if (bypass) { bypass = false; return; }
            __state.Target = amount; __state.Revision = ++revision;
            try
            {
                // Any newer request replaces the old target, including decreases and indoor calls.
                if (owner == __instance) Cancel();
                if (!CityEntry.Ready || !PerformanceMod.Smoothing || PerformanceMod.Faulted ||
                    BuildingManager.IsInsideBuilding || LoadScene.isLoading || !__instance.isActiveAndEnabled || __instance.activePedestrians == null) return;
                if (InstanceBehavior<CityManager>.Instance == null || InstanceBehavior<CityManager>.Instance.pedestrianSpawner != __instance) return;
                if (ForeignPatch()) { PerformanceMod.Faulted = true; PerformanceMod.Log("Refill disabled: foreign patch detected."); return; }
                if (owner != null && owner != __instance) return; // Never maintain an unbounded set of scene objects.
                int count = __instance.activePedestrians.Count;
                if (amount <= count) return;
                owner = __instance;
                int allowed = quota.Take(Time.frameCount, Math.Min(2, amount - count));
                int next = plan.Request(amount, count, 2);
                amount = count + Math.Min(allowed, next - count);
                plan.Consume(amount - count);
                __state.Restore = amount != __state.Target;
            }
            catch (Exception e) { PerformanceMod.Faulted = true; Cancel(); amount = __state.Target; __state.Restore = false; PerformanceMod.Log("Refill prefix disabled: " + e.Message); }
        }
        private static Exception Finalizer(PedestrianSpawner __instance, RequestState __state, Exception __exception)
        {
            // Restore the full requested target even if the vanilla call throws. Never swallow its exception.
            try
            {
                if (__state.Restore && __state.Revision == revision && lastTarget != null) lastTarget(__instance) = __state.Target;
            }
            catch (Exception e) { Cancel(); PerformanceMod.Faulted = true; PerformanceMod.Log("Refill target restore failed: " + e); }
            if (__exception != null) { Cancel(); PerformanceMod.Faulted = true; }
            return __exception;
        }
        internal static void Tick()
        {
            if (!OwnerIsCurrent()) { Cancel(); return; }
            if (BuildingManager.IsInsideBuilding || LoadScene.isLoading) { Cancel(); return; }
            if (ForeignPatch()) { DisableForConflict(); return; }
            if (!PerformanceMod.Smoothing || PerformanceMod.Faulted) { Flush(); return; }
            int next = plan.Next(owner.activePedestrians.Count, 2);
            if (!plan.Pending) { Cancel(); return; }
            int allowed = quota.Take(Time.frameCount, next - owner.activePedestrians.Count);
            if (allowed > 0) { plan.Consume(allowed); Apply(owner.activePedestrians.Count + allowed); }
        }
        private static void Apply(int amount)
        {
            var spawner = owner; int target = plan.Target; long requestRevision = revision;
            try { bypass = true; spawner.SetActivePedestrians(amount); }
            catch (Exception e) { PerformanceMod.Faulted = true; Cancel(); PerformanceMod.Log("Refill continuation failed; future requests use vanilla: " + e); }
            finally
            {
                bypass = false;
                try { if (spawner != null && revision == requestRevision) lastTarget(spawner) = target; }
                catch (Exception e) { Cancel(); PerformanceMod.Faulted = true; PerformanceMod.Log("Deferred target restore failed: " + e); }
            }
        }
        internal static void Flush()
        {
            try
            {
                if (OwnerIsCurrent() && plan.Pending && !BuildingManager.IsInsideBuilding && !LoadScene.isLoading)
                {
                    if (ForeignPatch()) { DisableForConflict(); return; }
                    int count = owner.activePedestrians.Count;
                    int next = plan.Next(count, int.MaxValue);
                    if (next > count) Apply(next);
                }
            }
            catch (Exception e) { PerformanceMod.Faulted = true; PerformanceMod.Log("Refill flush failed: " + e); }
            finally { Cancel(); }
        }
        private static bool OwnerIsCurrent()
        {
            var city=InstanceBehavior<CityManager>.Instance;
            return owner!=null && city!=null && city.pedestrianSpawner==owner && owner.isActiveAndEnabled;
        }
        private static void DisableForConflict()
        {
            PerformanceMod.Faulted = true;
            Cancel();
            PerformanceMod.Log("Refill disabled: foreign patch detected; pending work cancelled without invoking the conflicting chain.");
        }
        internal static void Cancel() { owner = null; plan.Cancel(); }
        internal static void Describe(ref TraceRow row)
        {
            row.RefillFaulted=PerformanceMod.Faulted?1:0;
            row.RefillPending=plan.Pending?1:0;row.RefillRemaining=plan.Remaining;
            if(owner!=null){row.RefillTarget=plan.Target;row.RefillActive=owner.activePedestrians==null?-1:owner.activePedestrians.Count;}
        }
    }

    [DefaultExecutionOrder(20000)]
    internal sealed class PerformanceHost : MonoBehaviour
    {
        private readonly FrameWindow window = new FrameWindow(30);
        private System.Collections.IEnumerator Start()
        {
            var endOfFrame=new WaitForEndOfFrame();
            while(true)
            {
                yield return endOfFrame;
                if(!CityEntry.IsCurrent(this))continue;
                try{FrameTrace.FrameEnd();}
                catch(Exception e){FrameTrace.Recording=false;PerformanceMod.Log("Trace capture disabled: "+e);}
            }
        }
        // One slot per per-frame feature tick. A tick that throws is stopped for this session and logged once,
        // so a game update cannot turn one broken feature into an exception every frame.
        // Only load failures stop a slot; game exceptions propagate to Unity exactly as before.
        private static readonly string[] tickNames = { "tree detail", "cinematic world", "frame trace fixed step", "background autosaves", "Hyperreal discovery", "Bakery Help", "midnight cleanup", "frame trace", "stationary scheduling", "pedestrian refill" };
        private static readonly bool[] stopped = new bool[10];
        private static readonly Action[] ticks =
        {
            () => TreeLods.Tick(), () => CinematicWorld.Tick(), () => FrameTrace.FixedStep(), () => BackgroundSaves.Tick(),
            () => HyperrealDiscovery.Tick(), () => BakeryHelp.Tick(), () => MidnightCleanup.Tick(),
            () => { try { FrameTrace.Tick(); } catch (Exception e) when (!PerformanceMod.LoadFailure(e)) { FrameTrace.Recording = false; PerformanceMod.Log("Trace update disabled: " + e); } },
            () => { try { ZoneScheduling.Tick(); } catch (Exception e) when (!PerformanceMod.LoadFailure(e)) { ZoneScheduling.Faulted = true; PerformanceMod.Log("Zone scheduler disabled: " + e); } },
            () => { try { Refill.Tick(); } catch (Exception e) when (!PerformanceMod.LoadFailure(e)) { Refill.Cancel(); PerformanceMod.Faulted = true; PerformanceMod.Log("Refill disabled: " + e); } },
        };
        private static void Run(int slot)
        {
            if (stopped[slot]) return;
            try { ticks[slot](); }
            catch (Exception e) when (PerformanceMod.LoadFailure(e))
            {
                stopped[slot] = true;
                if (slot == 9) PerformanceMod.Faulted = true;
                PerformanceMod.Log("Stopped " + tickNames[slot] + " for this session (game build changed); original game behavior continues: " + e);
            }
        }
        private void LateUpdate(){if(CityEntry.IsCurrent(this)){Run(0);Run(1);}}
        private void FixedUpdate(){if(CityEntry.IsCurrent(this))Run(2);}
        private void Update()
        {
            if (!CityEntry.IsCurrent(this)) return;
            Run(3);
            Run(4);
            Run(5);
            double now = Time.realtimeSinceStartupAsDouble;
            Run(7);
            Run(8);
            Run(6);
            Run(9);
            bool recording = PerformanceMod.Diagnostics && Application.isFocused && !LoadScene.isLoading;
            if (!window.Observe(now, recording)) return;
            var frames = window.Frames;
            PerformanceMod.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "PERF frames n={0} mean_ms={1:F2} p50_le_ms={2} p95_le_ms={3} p99_le_ms={4} max_ms={5:F2} over50={6} bin250plus={7} refill={8}",
                frames.Count, frames.Count == 0 ? 0 : frames.Sum / frames.Count, frames.Percentile(.5), frames.Percentile(.95), frames.Percentile(.99), frames.Max, frames.Over50, frames.Overflow, PerformanceMod.Smoothing && PerformanceMod.Verified && !PerformanceMod.Faulted));
            frames.Clear();
            try { Timings.Report(); DetailedTrace.Report(); TreeLods.Report(); CinematicWorld.Report(); CinematicPopulation.Report(); }
            catch (Exception e) { PerformanceMod.Diagnostics = false; PerformanceMod.Log("Performance summaries stopped after an error: " + e); }
            // The next frame includes summary formatting/logging overhead in measured cadence.
        }
    }

    internal static class Timings
    {
        private sealed class Counter { public string Name; public long Calls, Ticks, Max, Errors; }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<MethodBase, Counter> counters = new System.Collections.Concurrent.ConcurrentDictionary<MethodBase, Counter>();
        internal static void Install(Type type, string name, Type[] args)
        {
            try
            {
                var method = AccessTools.Method(type, name, args);
                if (method == null || method.DeclaringType != type || method.ReturnType != typeof(void)) { PerformanceMod.Log("Not active: timing " + type.Name + "." + name); return; }
                counters[method] = new Counter { Name = type.Name + "." + name };
                PerformanceMod.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(Timings), nameof(Begin)), finalizer: new HarmonyMethod(typeof(Timings), nameof(End)));
                PerformanceMod.Log("Active: timing " + type.Name + "." + name);
            }
            catch (Exception e) { PerformanceMod.Log("Not active: timing " + type.Name + "." + name + ": " + e.Message); }
        }
        private static void Begin(out long __state) => __state = PerformanceMod.Loaded && PerformanceMod.Diagnostics ? Stopwatch.GetTimestamp() : 0;
        private static Exception End(MethodBase __originalMethod, long __state, Exception __exception)
        {
            if (__state == 0) return __exception;
            if (counters.TryGetValue(__originalMethod, out var c))
            {
                long ticks = Stopwatch.GetTimestamp() - __state;
                Interlocked.Increment(ref c.Calls); Interlocked.Add(ref c.Ticks, ticks);
                if (__exception != null) Interlocked.Increment(ref c.Errors);
                long old;
                do { old = Interlocked.Read(ref c.Max); if (old >= ticks) break; } while (Interlocked.CompareExchange(ref c.Max, ticks, old) != old);
            }
            return __exception;
        }
        internal static void Reset()
        {
            foreach (var c in counters.Values)
            {
                Interlocked.Exchange(ref c.Calls, 0); Interlocked.Exchange(ref c.Ticks, 0);
                Interlocked.Exchange(ref c.Max, 0); Interlocked.Exchange(ref c.Errors, 0);
            }
        }
        internal static void Report()
        {
            foreach (var c in counters.Values)
            {
                long calls = Interlocked.Exchange(ref c.Calls, 0), ticks = Interlocked.Exchange(ref c.Ticks, 0), max = Interlocked.Exchange(ref c.Max, 0), errors = Interlocked.Exchange(ref c.Errors, 0);
                PerformanceMod.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "PERF scope={0} calls={1} total_ms={2:F3} max_ms={3:F3} exceptions={4}", c.Name, calls, ticks * 1000.0 / Stopwatch.Frequency, max * 1000.0 / Stopwatch.Frequency, errors));
            }
        }
    }
}
