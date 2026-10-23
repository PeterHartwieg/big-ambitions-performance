using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

static class SaveHookChecks
{
    public static void Run(Assembly game, Assembly mod)
    {
        var type = mod.GetType("CityPerformance.BackgroundSaves", true);
        var performance = mod.GetType("CityPerformance.PerformanceMod", true);
        var manager = game.GetType("SaveGameManager", true);
        var save = AccessTools.Method(manager, "Save");
        var rewrite = AccessTools.Method(type, "Rewrite");
        var owner = (Harmony)AccessTools.Field(performance, "Harmony").GetValue(null);
        var foreign = new Harmony("cityperformance.offline-save-conflict");
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        object Call(string name, params object[] args) => AccessTools.Method(type, name).Invoke(null, args);
        Check(AccessTools.Field(type, "save").GetValue(null) == null, "option state initializes without game reflection");
        AccessTools.Field(type, "Enabled").SetValue(null, false);
        Call("Install");
        Check(AccessTools.Field(type, "save").GetValue(null) == null && Harmony.GetPatchInfo(save) == null, "explicit off performs no save lookup or patch");
        Call("Bind");
        var source = PatchProcessor.GetOriginalInstructions(save);
        var rewritten = ((IEnumerable<CodeInstruction>)rewrite.Invoke(null, new object[] { source })).ToArray();
        var differences = Enumerable.Range(0, source.Count).Where(i => source[i].opcode != rewritten[i].opcode || !Equals(source[i].operand, rewritten[i].operand)).ToArray();
        Check(source.Count == rewritten.Length && differences.Length == 4, "save changes exactly four call operands/opcodes");
        Check(differences.Select(i => ((MethodInfo)rewritten[i].operand).Name).SequenceEqual(new[] { "Capture", "Worker", "Priority", "Metadata" }), "native save order retained");
        Check(Enumerable.Range(0, source.Count).All(i => source[i].labels.SequenceEqual(rewritten[i].labels) && source[i].blocks.SequenceEqual(rewritten[i].blocks)), "labels and exception blocks preserved");
        Check(differences.All(i => !ReferenceEquals(source[i], rewritten[i])), "rewrite never modifies another patch's instructions");
        foreach (int i in differences)
        {
            var missing = source.Where((_, index) => index != i).ToArray();
            var unchanged = ((IEnumerable<CodeInstruction>)rewrite.Invoke(null, new object[] { missing })).ToArray();
            Check(unchanged.Length == missing.Length && unchanged.Zip(missing).All(x => x.First.opcode == x.Second.opcode && Equals(x.First.operand, x.Second.operand)) &&
                (bool)AccessTools.Field(type, "Faulted").GetValue(null), "missing site retains incoming instructions and disables feature: " + ((MethodInfo)source[i].operand).Name);
            var duplicate = source.Select(x => new CodeInstruction(x)).ToList(); duplicate.Insert(i, new CodeInstruction(source[i]));
            unchanged = ((IEnumerable<CodeInstruction>)rewrite.Invoke(null, new object[] { duplicate })).ToArray();
            Check(unchanged.Length == duplicate.Count && unchanged.Zip(duplicate).All(x => x.First.opcode == x.Second.opcode && Equals(x.First.operand, x.Second.operand)), "duplicate site retains incoming instructions: " + ((MethodInfo)source[i].operand).Name);
        }
        try
        {
            AccessTools.Field(type, "Enabled").SetValue(null, true);
            AccessTools.Field(performance, "Verified").SetValue(null, false); Call("Install");
            Check(!(bool)AccessTools.Field(type, "installed").GetValue(null), "unknown build refuses save hooks");
            AccessTools.Field(performance, "Verified").SetValue(null, true);
            foreign.Patch(save, prefix: new HarmonyMethod(typeof(SaveHookChecks), nameof(Noop)));
            Call("Install"); Check(!(bool)AccessTools.Field(type, "installed").GetValue(null), "foreign save patch refuses installation");
            foreign.UnpatchAll(foreign.Id);
            var snapshot = mod.GetType("CityPerformance.GameSaveSnapshot", true);
            var serializerGuard = AccessTools.Method(snapshot, "ForeignFormatterOrMultiplayer");
            var policyType = Assembly.Load("OdinSerializer").GetType("OdinSerializer.SerializationPolicies");
            foreign.Patch(AccessTools.PropertyGetter(policyType, "Unity"), prefix: new HarmonyMethod(typeof(SaveHookChecks), nameof(Noop)));
            Check((bool)serializerGuard.Invoke(null, null), "foreign Odin hook is detected");
            Call("Install"); Check(!(bool)AccessTools.Field(type, "installed").GetValue(null), "foreign Odin hook refuses installation");
            foreign.UnpatchAll(foreign.Id);
            Check(!(bool)serializerGuard.Invoke(null, null), "unpatched audited serializer accepted");
            Call("Install"); Check((bool)AccessTools.Field(type, "installed").GetValue(null), "audited native save hooks apply");
            Check(Harmony.GetPatchInfo(save).Transpilers.Any(p => p.PatchMethod == rewrite), "save transpiler installed");
            Check(Harmony.GetPatchInfo(AccessTools.Method(manager, "JoinSaveGameThreads")).Prefixes.Any(p => p.PatchMethod.DeclaringType == type), "join lifetime hook installed");
            Check(Harmony.GetPatchInfo(AccessTools.PropertyGetter(manager, "SavingGameInProgress")).Postfixes.Any(p => p.PatchMethod.DeclaringType == type), "progress completion hook installed");
            Check(!(bool)Call("Conflict"), "own save hooks do not conflict");
            foreach (var native in (MethodInfo[])AccessTools.Field(type, "nativeSync").GetValue(null))
                Check(Harmony.GetPatchInfo(native).Prefixes.Any(p => p.PatchMethod.DeclaringType.Name == "SaveSerializationGate"), "native Odin gate installed: " + native.Name);
            var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CitySaveEmptyDynamic"), AssemblyBuilderAccess.Run);
            dynamicAssembly.DefineDynamicModule("empty");
            Check(!(bool)serializerGuard.Invoke(null, null), "empty non-Odin dynamic assembly attributes scan on the offline host");
            var emitter = Assembly.Load("OdinSerializer").GetType("OdinSerializer.Utilities.FormatterEmitter") ?? Assembly.Load("OdinSerializer").GetType("OdinSerializer.FormatterEmitter", true);
            var emittedAssembly = AccessTools.Field(emitter, "runtimeEmittedAssembly");
            Check(emittedAssembly != null && emittedAssembly.IsStatic && typeof(Assembly).IsAssignableFrom(emittedAssembly.FieldType), "audited Odin emitted-assembly identity field exists and is static Assembly");
            foreign.Patch(serializerGuard, prefix: new HarmonyMethod(typeof(SaveHookChecks), nameof(ThrowInspection)));
            Check(!(bool)Call("CheckCompatibility") && (bool)AccessTools.Field(type, "Faulted").GetValue(null), "inspection exception disables optimization without escaping native save boundary");
            foreign.UnpatchAll(foreign.Id);
            AccessTools.Field(type, "Faulted").SetValue(null, false);
            foreign.Patch(save, transpiler: new HarmonyMethod(typeof(SaveHookChecks), nameof(ChangeSave)) { priority = Priority.First });
            Check((bool)AccessTools.Field(type, "Faulted").GetValue(null), "later foreign save transpiler succeeds and disables optimization");
            foreign.UnpatchAll(foreign.Id);
            foreign.Patch(AccessTools.Method(game.GetType("SaveGameSerializationHelper"), "SerializeBinaryData"), prefix: new HarmonyMethod(typeof(SaveHookChecks), nameof(Noop)));
            Check((bool)Call("Conflict"), "later foreign serialization patch is detected");
            foreign.UnpatchAll(foreign.Id);
            var call = Activator.CreateInstance(type.GetNestedType("Call", BindingFlags.NonPublic), true);
            var exception = new IOException("native exception");
            Check(ReferenceEquals(Call("End", call, false, exception), exception), "native exception identity preserved");
            var processes = AccessTools.Field(manager, "_saveProcessesRunning");
            processes.SetValue(null, 1);
            Check((bool)AccessTools.PropertyGetter(manager, "SavingGameInProgress").Invoke(null, null), "native screenshot remains in progress without custom job");
            processes.SetValue(null, 0);
            Check(!(bool)AccessTools.PropertyGetter(manager, "SavingGameInProgress").Invoke(null, null), "native completed progress unchanged without custom job");
            Call("Drain"); Check(true, "idle lifecycle drain is harmless");
            var allocation = AccessTools.Method(type, "CaptureAllocatedBytes");
            Check((allocation.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0, "optional Mono allocation API isolated from Capture JIT by NoInlining");
            var callType = type.GetNestedType("Call", BindingFlags.NonPublic);
            var captureCall = Activator.CreateInstance(callType, true);
            AccessTools.Field(callType, "Eligible").SetValue(captureCall, true);
            var wrapper = Activator.CreateInstance(manager.GetNestedType("SaveGameThreadWrapper", BindingFlags.NonPublic));
            var nativeSerializeField = AccessTools.Field(type, "serialize"); var originalSerialize = nativeSerializeField.GetValue(null);
            AccessTools.Field(type, "current").SetValue(null, captureCall);
            nativeSerializeField.SetValue(null, AccessTools.Method(typeof(SaveHookChecks), nameof(NativeSerializeFallback)));
            foreign.Patch(allocation, prefix: new HarmonyMethod(typeof(SaveHookChecks), nameof(ThrowAllocation)));
            try
            {
                foreach (bool missing in new[] { false, true })
                {
                    missingAllocationApi = missing; fallbackCalls = 0; AccessTools.Field(type, "Faulted").SetValue(null, false);
                    Call("Capture", wrapper);
                    Check(fallbackCalls == 1 && (bool)AccessTools.Field(type, "Faulted").GetValue(null) && AccessTools.Field(type, "pending").GetValue(null) == null,
                        "allocation diagnostic " + (missing ? "missing API" : "runtime failure") + " falls back to native serializer and faults only optimization");
                }
            }
            finally { foreign.UnpatchAll(foreign.Id); nativeSerializeField.SetValue(null, originalSerialize); AccessTools.Field(type, "current").SetValue(null, null); }
            foreach (bool validating in new[] { true, false })
            {
                var jobType = type.GetNestedType("Job", BindingFlags.NonPublic);
                var priorityJob = Activator.CreateInstance(jobType, true);
                AccessTools.Field(jobType, "Validate").SetValue(priorityJob, validating);
                AccessTools.Field(callType, "Job").SetValue(captureCall, priorityJob);
                AccessTools.Field(type, "current").SetValue(null, captureCall);
                try
                {
                    var thread = new Thread(() => { }); Call("Priority", thread, ThreadPriority.Highest);
                    Check(thread.Priority == (validating ? ThreadPriority.Highest : ThreadPriority.BelowNormal), validating ?
                        "validation compression retains requested native priority" : "optimized save worker starts at background priority");
                }
                finally { AccessTools.Field(type, "current").SetValue(null, null); }
            }
            AccessTools.Field(type, "validated").SetValue(null, true); AccessTools.Field(type, "validatedRevision").SetValue(null, 7);
            Check(!(bool)Call("NeedsValidation", 7) && (bool)Call("NeedsValidation", 8), "new runtime/comparer schema revision requires native comparison");
            Call("OnApplicationQuit"); Check((bool)AccessTools.Field(type, "quitting").GetValue(null), "quit prevents new background jobs");
            GateChecks.Run(mod);
        }
        finally { foreign.UnpatchAll(foreign.Id); owner.UnpatchAll(owner.Id); }
        Console.WriteLine($"PASS {checks} offline save hook checks; no Unity execution");
    }
    private static void Noop() { }
    private static bool missingAllocationApi;
    private static int fallbackCalls;
    private static void NativeSerializeFallback(object data) { fallbackCalls++; }
    private static void ThrowAllocation() { if (missingAllocationApi) throw new MissingMethodException("injected missing Mono allocation API"); throw new NotSupportedException("injected allocation diagnostic failure"); }
    private static void ThrowInspection() => throw new TypeLoadException("injected unresolvable attribute");
    private static void SaveWrapper(object value) { }
    private static IEnumerable<CodeInstruction> ChangeSave(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.operand is MethodInfo m && m.Name == "SerializeSaveGame") copy.operand = AccessTools.Method(typeof(SaveHookChecks), nameof(SaveWrapper));
            yield return copy;
        }
    }
}
