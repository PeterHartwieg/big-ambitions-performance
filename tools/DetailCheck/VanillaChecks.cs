using System.Reflection;
using HarmonyLib;

// A game with no other mods loaded: run every installer the mod entry runs, on the audited build and on an
// unaudited one (a game update), and check that nothing throws, nothing logs a failure, optional-mod
// features stay inert and an unaudited build gets timing hooks only. Managed contracts only, no Unity.
static class VanillaChecks
{
    internal static readonly List<string> Lines = new List<string>();
    public static void CaptureLog(string __0) { Lines.Add(__0); Console.WriteLine("MOD " + __0); }
    public static int Frame() => 1;
    public static IEnumerable<CodeInstruction> ReplaceClock(IEnumerable<CodeInstruction> code)
    {
        foreach (var instruction in code)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.operand is MethodInfo m && m.DeclaringType?.FullName == "UnityEngine.Time" && m.Name == "get_frameCount")
                copy.operand = AccessTools.Method(typeof(VanillaChecks), nameof(Frame));
            yield return copy;
        }
    }

    internal static void Run(Assembly mod, Assembly game)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); checks++; Console.WriteLine("PASS " + label); }
        Type Feature(string name) => mod.GetType("CityPerformance." + name, true);
        var perf = Feature("PerformanceMod");
        var owner = (Harmony)AccessTools.Field(perf, "Harmony").GetValue(null);
        var fixture = new Harmony("cityperformance.check.vanilla");
        fixture.Patch(AccessTools.Method(perf, "Log"), prefix: new HarmonyMethod(typeof(VanillaChecks), nameof(CaptureLog)));
        // Native frame counter, as in the ownership harness; the guard logic itself stays real.
        fixture.Patch(AccessTools.Method(Feature("ZoneScheduling"), "Conflict"), transpiler: new HarmonyMethod(typeof(VanillaChecks), nameof(ReplaceClock)));
        var timingTargets = new (Type, string, Type[])[]
        {
            (game.GetType("PedestrianSpawner", true), "SetActivePedestrians", new[] { typeof(int) }),
            (game.GetType("GameManager", true), "RunMainGameTick", new[] { typeof(float) }),
            (game.GetType("SaveGameManager", true), "SerializeSaveGame", new[] { typeof(object) }),
            (game.GetType("Culling.CullingGroupController", true), "Update", Type.EmptyTypes),
            (Assembly.Load("ExternalPlugins").GetType("GleyTrafficSystem.TrafficManager", true), "FixedUpdate", Type.EmptyTypes),
        };
        string[] installers = { "Refill", "ZoneScheduling", "MidnightCleanup", "BackgroundSaves", "TreeLods", "CinematicWorld", "CinematicAudioZones", "CinematicPopulation", "CinematicDecals", "FrameTrace" };
        HashSet<string> PatchedBy()
        {
            var types = new HashSet<string>();
            foreach (var method in Harmony.GetAllPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(method);
                foreach (var p in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
                    if (p.owner.StartsWith(owner.Id, StringComparison.Ordinal)) types.Add(p.PatchMethod.DeclaringType.Name);
            }
            return types;
        }
        void Load(bool verified)
        {
            Lines.Clear();
            AccessTools.Field(perf, "Verified").SetValue(null, verified);
            AccessTools.Field(perf, "Loaded").SetValue(null, true);
            AccessTools.Field(perf, "Faulted").SetValue(null, false);
            var step = AccessTools.Method(perf, "Step");
            foreach (var name in installers)
            {
                var install = AccessTools.Method(Feature(name), "Install");
                step.Invoke(null, new object[] { name, (Action)(() => install.Invoke(null, null)) });
            }
            foreach (var name in new[] { "HyperrealDiscovery", "BakeryHelp", "DetailedTrace" })
            {
                var init = AccessTools.Method(Feature(name), "Initialize");
                step.Invoke(null, new object[] { name, (Action)(() => init.Invoke(null, null)) });
            }
            var timing = AccessTools.Method(Feature("Timings"), "Install");
            foreach (var t in timingTargets) step.Invoke(null, new object[] { "timing", (Action)(() => timing.Invoke(null, new object[] { t.Item1, t.Item2, t.Item3 })) });
            // City-ready discovery of the optional mods, with neither loaded.
            AccessTools.Method(Feature("HyperrealDiscovery"), "StartCity").Invoke(null, null);
            AccessTools.Method(Feature("BakeryHelp"), "StartCity").Invoke(null, null);
        }
        bool Clean() => !Lines.Any(l => l.Contains("failed") || l.Contains("unavailable") || l.Contains("Exception") || l.Contains("error") || l.Contains("setup failed"));
        void Unload()
        {
            AccessTools.Method(Feature("HyperrealDiscovery"), "Shutdown").Invoke(null, null);
            AccessTools.Method(Feature("BakeryHelp"), "Shutdown").Invoke(null, null);
            owner.UnpatchAll(owner.Id);
        }
        try
        {
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => new[] { "PolyModsHyperreal", "Bakery", "HARTFreeCamera", "CameraTools", "FoodTrucks", "HardModePatch", "RealLifeOverhaul" }.Contains(a.GetName().Name)),
                "fixture process has no other mod assemblies loaded");

            string gameHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(game.Location))).ToLowerInvariant();
            bool audited = gameHash == (string)AccessTools.Field(perf, "AuditedGameHash").GetValue(null);
            Console.WriteLine("GAME " + gameHash + " audited=" + audited);
            if (audited) {
            Load(true);
            Check(Clean(), "audited build, no other mods: every installer runs without a failure or exception log");
            var verified = PatchedBy();
            foreach (var name in new[] { "Refill", "ZoneScheduling", "MidnightCleanup", "TreeLods", "CinematicWorld", "CinematicAudioZones", "CinematicPopulation", "CinematicTraffic", "CinematicDecals", "FrameTrace", "Timings" })
                Check(verified.Contains(name), "audited build installs vanilla feature: " + name);
            Check(!verified.Contains("BackgroundSaves"), "background autosaves install nothing while the option is off");
            Check(!verified.Contains("HyperrealDiscovery") && Lines.Any(l => l.Contains("Hyperreal discovery: Hyperreal absent")), "Hyperreal absent: no hook, one plain status line");
            Check(!verified.Contains("BakeryHelp") && Lines.Any(l => l.Contains("Bakery Help: Bakery absent")), "Bakery absent: no hook, one plain status line");
            Check(!(bool)AccessTools.PropertyGetter(Feature("CinematicWorld"), "BridgeConnected").Invoke(null, null), "HART Free Camera absent: bridge not connected, so coverage stays vanilla");
            Check(!(bool)AccessTools.PropertyGetter(Feature("CinematicWorld"), "Active").Invoke(null, null), "camera coverage inactive without the bridge");
            Check(!(bool)AccessTools.Field(perf, "Faulted").GetValue(null), "refill is not marked faulted in a vanilla game");
            Unload();
            } else Console.WriteLine("SKIP audited-build pass: this reference set is not the audited Windows build");

            Load(false);
            Check(Clean(), "unaudited build (game update): installers withdraw without a failure or exception log");
            var unverified = PatchedBy();
            Check(unverified.SetEquals(new[] { "FrameTrace", "Timings" }), "unaudited build keeps only timing/trace hooks (got: " + string.Join(",", unverified) + ")");
            Check(Lines.Any(l => l.Contains("Not active: pedestrian refill")) && Lines.Any(l => l.Contains("Not active: midnight cleanup")) && Lines.Any(l => l.Contains("Not active: tree detail")),
                "unaudited build says which behavioral features stay off");
            Unload();

            var step = AccessTools.Method(perf, "Step");
            Lines.Clear();
            step.Invoke(null, new object[] { "probe", (Action)(() => throw new TypeLoadException("missing game type fixture")) });
            Check(Lines.Count == 1 && Lines[0].StartsWith("Not active: probe", StringComparison.Ordinal), "a setup step that throws (e.g. a removed game type) is logged once and does not escape");
            var contain = AccessTools.Method(perf, "Contain");
            Lines.Clear();
            bool kept = (bool)contain.Invoke(null, new object[] { "probe", (Action)(() => throw new TypeInitializationException("Probe", null)) });
            Check(!kept && Lines.Count == 1, "a lifecycle call failing to load is contained and logged once");
            try { contain.Invoke(null, new object[] { "probe", (Action)(() => throw new InvalidOperationException("game exception fixture")) }); Check(false, "game exception escapes lifecycle guard"); }
            catch (TargetInvocationException e) { Check(e.InnerException is InvalidOperationException, "a game exception is not swallowed by the lifecycle guard"); }
            Console.WriteLine($"ALL {checks} VANILLA CHECKS PASSED (offline; Unity/Mono loading and the in-game log remain a playtest item)");
        }
        finally { owner.UnpatchAll(owner.Id); fixture.UnpatchAll(fixture.Id); AccessTools.Field(perf, "Verified").SetValue(null, false); }
    }
}
