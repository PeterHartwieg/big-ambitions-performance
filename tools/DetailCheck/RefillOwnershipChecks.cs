using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

// Uses real CP methods and Harmony metadata; never executes native pedestrian/Unity calls.
internal static class RefillOwnershipChecks
{
    private static readonly List<string> lines = new();
    private static bool CaptureLog(string text) { lines.Add(text); return false; }
    private static void ForeignPrefix() { }
    private static bool SkipShutdown() => false;
    private static readonly Exception teardownError = new InvalidOperationException("injected early unload failure");
    private static void FailShutdown() => throw teardownError;

    internal static void Run(Assembly mod, Assembly game)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); checks++; Console.WriteLine("PASS " + label); }
        var refill = mod.GetType("CityPerformance.Refill", true);
        var perf = mod.GetType("CityPerformance.PerformanceMod", true);
        object Call(string name, params object[] args) => AccessTools.Method(refill, name).Invoke(null, args);
        var owner = (Harmony)AccessTools.Field(perf, "Harmony").GetValue(null);
        var fixture = new Harmony("cityperformance.check.refill-ownership");
        var method = AccessTools.Method(game.GetType("PedestrianSpawner", true), "SetActivePedestrians", new[] { typeof(int) });
        bool Installed() => Harmony.GetPatchInfo(method)?.Prefixes.Any(p => p.PatchMethod.DeclaringType == refill) == true;
        var plan = AccessTools.Field(refill, "plan").GetValue(null);
        void Queue() => AccessTools.Method(plan.GetType(), "Request").Invoke(plan, new object[] { 100, 0, 2 });
        bool Pending() => (bool)AccessTools.Property(plan.GetType(), "Pending").GetValue(plan);
        fixture.Patch(AccessTools.Method(perf, "Log"), prefix: new HarmonyMethod(typeof(RefillOwnershipChecks), nameof(CaptureLog)));
        try
        {
            AccessTools.Field(perf, "Verified").SetValue(null, true);
            AccessTools.Field(perf, "Faulted").SetValue(null, false);
            Call("Install");
            Check(Installed(), "without Persistent City, audited refill hook installs");
            owner.UnpatchAll(owner.Id);
            fixture.Patch(method, prefix: new HarmonyMethod(typeof(RefillOwnershipChecks), nameof(ForeignPrefix)));
            Call("Install");
            Check(!Installed(), "unrelated foreign patch still refuses installation");
            fixture.Unpatch(method, AccessTools.Method(typeof(RefillOwnershipChecks), nameof(ForeignPrefix)));
            Call("Install");
            Check(Installed(), "refill installs again before late Persistent City load");
            Queue();
            Check(Pending(), "finite CP backlog exists before ownership changes");
            AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PersistentCity"), AssemblyBuilderAccess.Run);
            Call("Tick");
            Check(!Pending(), "late assembly load cancels backlog before touching Unity owner state");
            Queue();
            Call("Flush");
            Check(!Pending(), "flush cancels rather than invoking the other owner's chain");
            Queue();
            object[] request = { null, 100, null };
            Call("Prefix", request);
            Check((int)request[1] == 100 && !Pending(), "prefix passes request unchanged and cancels pending work");
            var failure = new InvalidOperationException("original game failure");
            Check(ReferenceEquals(Call("Finalizer", null, request[2], failure), failure), "original exceptions pass through unchanged");
            // Simulate a request already in flight when the assembly loaded. A stale restore
            // must not touch even the old spawner's _lastSetAmount after ownership changes.
            var state = request[2];
            AccessTools.Field(state.GetType(), "Restore").SetValue(state, true);
            AccessTools.Field(state.GetType(), "Revision").SetValue(state, AccessTools.Field(refill, "revision").GetValue(null));
            Check(Call("Finalizer", null, state, null) == null, "in-flight request restoration stands down too");
            Check(!(bool)AccessTools.Field(perf, "Faulted").GetValue(null), "ownership surrender does not fault the experiment");
            Call("Tick"); Call("Flush");
            Check(lines.Count(l => l.Contains("Persistent City is present")) == 1, "late ownership is logged exactly once across all entry points");
            foreach (var name in new[] { "HyperrealDiscovery", "BakeryHelp" })
                fixture.Patch(AccessTools.Method(mod.GetType("CityPerformance." + name, true), "Shutdown"),
                    prefix: new HarmonyMethod(typeof(RefillOwnershipChecks), nameof(SkipShutdown)));
            fixture.Patch(AccessTools.Method(mod.GetType("CityPerformance.BackgroundSaves", true), "Stop"),
                prefix: new HarmonyMethod(typeof(RefillOwnershipChecks), nameof(FailShutdown)));
            AccessTools.Field(perf, "Loaded").SetValue(null, true);
            Queue();
            try
            {
                AccessTools.Method(perf, "OnUnloadAsync").Invoke(Activator.CreateInstance(perf), null);
                throw new Exception("FAIL early unload exception was lost");
            }
            catch (TargetInvocationException e)
            {
                Check(ReferenceEquals(e.InnerException, teardownError), "real unload preserves early shutdown exception");
            }
            Check(Installed() && (bool)AccessTools.Field(perf, "Loaded").GetValue(null), "failed unload leaves hooks and loaded flag active in regression fixture");
            Check(!Pending(), "failed unload cancels pending refill work");
            Check((bool)Call("PersistentCityOwnsRefill"), "failed unload retains Persistent City ownership after watcher disposal");
            Call("Tick"); Call("Flush");
            object[] afterUnload = { null, 400, null };
            Call("Prefix", afterUnload);
            Check((int)afterUnload[1] == 400, "requests after failed unload still pass through unchanged");
            Check(lines.Count(l => l.Contains("Persistent City is present")) == 1, "failed unload does not repeat ownership log");
            owner.UnpatchAll(owner.Id);
            lines.Clear();
            Call("Install");
            Check(!Installed(), "Persistent City loaded first prevents refill hook installation");
            Call("Tick"); Call("Flush");
            Check(lines.Count(l => l.Contains("Persistent City is present")) == 1, "early ownership is logged once per CP load");
            Call("StopOwnershipWatch");
            Check((bool)Call("PersistentCityOwnsRefill"), "detached watcher retains presence until next install");
        }
        finally
        {
            Call("StopOwnershipWatch"); Call("Cancel");
            owner.UnpatchAll(owner.Id); fixture.UnpatchAll(fixture.Id);
        }
        Console.WriteLine($"PASS: {checks} refill ownership checks (offline; native playtest still required)");
    }
}
