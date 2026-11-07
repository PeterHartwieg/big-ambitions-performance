using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

// Composition of CP's BackgroundSaves hooks with the agent rig's Cecil save guard. Run against a scratch
// Managed copy holding the rig's patched BigAmbitions.dll and its AgentTestRuntime.dll; never the rig itself.
// Only refused saves execute: the guard returns before any Unity call, so an allowed save cannot run offline.
static class RigGuardChecks
{
    public static void Run(Assembly game, Assembly mod)
    {
        var type = mod.GetType("CityPerformance.BackgroundSaves", true);
        var performance = mod.GetType("CityPerformance.PerformanceMod", true);
        var build = mod.GetType("CityPerformance.GameBuild", true);
        var manager = game.GetType("SaveGameManager", true);
        var saveType = manager.GetNestedType("SaveType", BindingFlags.Public | BindingFlags.NonPublic);
        var save = AccessTools.DeclaredMethod(manager, "Save", new[] { saveType, typeof(string), typeof(string) });
        var host = Assembly.Load("AgentTestRuntime").GetType("AgentTests.Host", true);
        // Newer runtimes route AllowSave through SaveGate; the deployed one inlines `!rigTest || manual`.
        var gate = host.Assembly.GetType("AgentTests.SaveGate", false);
        var owner = (Harmony)AccessTools.Field(performance, "Harmony").GetValue(null);
        var probe = new Harmony("cityperformance.offline-rig-probe");
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        object Call(string name, params object[] args) => AccessTools.Method(type, name).Invoke(null, args);
        object Get(string name) => AccessTools.Field(type, name).GetValue(null);

        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(game.Location))).ToLowerInvariant();
        Check(digest == (string)AccessTools.Field(build, "AgentRig3682").GetValue(null), "game is the agent-rig copy of build 3682: " + digest);

        // The rig guard: ldarg.0; ldc.i4 Default; ceq; call Host.AllowSave(bool); brtrue original; ldc.i4.0; ret.
        var source = PatchProcessor.GetOriginalInstructions(save);
        var allowSave = AccessTools.DeclaredMethod(host, "AllowSave", new[] { typeof(bool) });
        int manual = Convert.ToInt32(Enum.Parse(saveType, "Default"));
        Check(source[0].opcode == OpCodes.Ldarg_0 && source[1].opcode == OpCodes.Ldc_I4 && Equals(source[1].operand, manual) &&
            source[2].opcode == OpCodes.Ceq && source[3].opcode == OpCodes.Call && Equals(source[3].operand, allowSave) &&
            source[4].opcode == OpCodes.Brtrue && source[7].labels.Contains((Label)source[4].operand) &&
            source[5].opcode == OpCodes.Ldc_I4_0 && source[6].opcode == OpCodes.Ret, "rig Save starts with the seven-instruction guard");

        AccessTools.Method(type, "Bind").Invoke(null, null);
        var rewritten = ((IEnumerable<CodeInstruction>)AccessTools.Method(type, "Rewrite").Invoke(null, new object[] { source })).ToArray();
        var differences = Enumerable.Range(0, source.Count).Where(i => source[i].opcode != rewritten[i].opcode || !Equals(source[i].operand, rewritten[i].operand)).ToArray();
        Check(!(bool)Get("Faulted") && rewritten.Length == source.Count && differences.Length == 4 && differences.All(i => i > 6),
            "Rewrite still finds exactly one of each save site, all after the guard");
        Check(differences.Select(i => ((MethodInfo)rewritten[i].operand).Name).SequenceEqual(new[] { "Capture", "Worker", "Priority", "Metadata" }), "native save order retained after the guard");
        Check(Enumerable.Range(0, 7).All(i => rewritten[i].opcode == source[i].opcode && Equals(rewritten[i].operand, source[i].operand)) &&
            Enumerable.Range(0, source.Count).All(i => source[i].labels.SequenceEqual(rewritten[i].labels) && source[i].blocks.SequenceEqual(rewritten[i].blocks)),
            "Rewrite leaves the guard IL, its branch label and exception blocks intact");

        // The real hash read, not a forced flag: this digest alone turns every behavioral feature on.
        AccessTools.Field(performance, "Verified").SetValue(null, false);
        AccessTools.Method(performance, "ReadGameHash").Invoke(null, null);
        Check((bool)AccessTools.Field(performance, "Verified").GetValue(null), "rig digest is verified by the shipped hash read");

        // -rig-test with no save-specific mode: only SaveType.Default passes.
        if (gate != null) AccessTools.Method(gate, "Configure").Invoke(null, new object[] { true, null, null });
        else AccessTools.Field(host, "rigTest").SetValue(null, true);
        long[] Counters() => gate == null ? null : (long[])AccessTools.Method(gate, "Counters").Invoke(null, null);
        AccessTools.Field(performance, "Loaded").SetValue(null, true);
        AccessTools.Field(type, "Enabled").SetValue(null, true);
        var ready = AccessTools.PropertyGetter(mod.GetType("CityPerformance.CityEntry", true), "Ready");
        string scratch = Path.Combine(Path.GetTempPath(), "cityperformance-rig-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            // Begin needs a loaded city host; the Unity component cannot exist offline.
            probe.Patch(ready, prefix: new HarmonyMethod(typeof(RigGuardChecks), nameof(CityReady)));
            Call("Install");
            Check((bool)Get("installed") && !(bool)Get("Faulted"), "verified rig installs background autosaves alongside the Cecil guard");
            var info = Harmony.GetPatchInfo(save);
            Check(info.Owners.All(o => o == owner.Id) && !(bool)Call("Conflict"), "the Cecil guard is not a Harmony owner, so it is not a conflict");
            currentCall = AccessTools.Field(type, "current");
            probe.Patch(AccessTools.Method(type, "CheckCompatibility"), postfix: new HarmonyMethod(typeof(RigGuardChecks), nameof(Observe)));
            foreach (string name in new[] { "MidnightSave", "RecoverSave" })
            {
                observed = null; var before = Counters();
                object result = save.Invoke(null, new[] { Enum.Parse(saveType, name), Path.Combine(scratch, name), Path.Combine(scratch, name + ".png") });
                var after = Counters();
                Check(observed != null && (bool)AccessTools.Field(observed.GetType(), "Eligible").GetValue(observed),
                    name + ": Harmony prefix Begin runs before the guard and marks the save eligible");
                Check(Equals(result, false) && (gate == null || after[0] - before[0] == 1 && after[3] - before[3] == 1), name + ": guard then refuses it and Save returns false");
                Check(AccessTools.Field(observed.GetType(), "Job").GetValue(observed) == null && Get("pending") == null && Get("current") == null,
                    name + ": End sees __result false with no job; no capture, no pending worker, call stack restored");
                Check(Directory.GetFileSystemEntries(scratch).Length == 0, name + ": nothing serialized or written");
                Check((bool)Get("installed") && !(bool)Get("Faulted"), name + ": refusal leaves background autosaves healthy");
            }
            // A job can only exist after native Save reached SerializeSaveGame. Cover End's false-result path anyway.
            var callType = type.GetNestedType("Call", BindingFlags.NonPublic);
            var jobType = type.GetNestedType("Job", BindingFlags.NonPublic);
            var call = Activator.CreateInstance(callType, true); var job = Activator.CreateInstance(jobType, true);
            AccessTools.Field(jobType, "Metadata").SetValue(job, "{}");
            AccessTools.Field(callType, "Job").SetValue(call, job);
            Check(Call("End", call, false, null) == null && (bool)AccessTools.Field(jobType, "Abort").GetValue(job) &&
                (bool)AccessTools.Field(jobType, "Done").GetValue(job) && AccessTools.Field(jobType, "Error").GetValue(job) != null,
                "End with __result false aborts a job before any publication");
        }
        finally
        {
            probe.UnpatchAll(probe.Id); owner.UnpatchAll(owner.Id);
            AccessTools.Field(performance, "Verified").SetValue(null, false);
            Directory.Delete(scratch, true);
        }
        Console.WriteLine($"PASS {checks} offline agent-rig save guard composition checks; no Unity execution");
    }
    private static object observed;
    private static bool CityReady(ref bool __result) { __result = true; return false; }
    private static FieldInfo currentCall;
    // Begin calls CheckCompatibility after every other gate and just before it sets Eligible.
    private static void Observe(bool __result) { if (__result) observed = currentCall.GetValue(null); }
}
