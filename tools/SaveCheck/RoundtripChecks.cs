using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using System.Collections.Concurrent;

static class RoundtripChecks
{
    private static readonly ConcurrentQueue<string> diagnostics = new();
    private static readonly List<Delegate> observers = new();
    private static Type logType;
    internal static int DiagnosticCount => diagnostics.Count;
    internal static void NoDiagnostics(int before, string phase)
    {
        var added = diagnostics.ToArray().Skip(before).ToArray();
        if (added.Length != 0) throw new Exception(phase + " emitted Unity diagnostics: " + string.Join(" | ", added));
    }
    public static void Run(Assembly game, Assembly odin, Assembly mod, string[] fixtures)
    {
        // Offline test process only: Odin's architecture initializer logs through Unity.
        // No engine is running, so replace this one logging entry point, not serialization.
        var unity = Assembly.Load("UnityEngine.CoreModule");
        logType = unity.GetType("UnityEngine.LogType");
        new Harmony("cityperformance.offline-save-log").Patch(AccessTools.Method(unity.GetType("UnityEngine.Debug"), "Log", new[] { typeof(object) }),
            prefix: new HarmonyMethod(typeof(RoundtripChecks), nameof(SkipEngineLog)));
        foreach (string name in new[] { "LogWarning", "LogError", "LogException" })
            new Harmony("cityperformance.offline-save-log").Patch(AccessTools.Method(unity.GetType("UnityEngine.Debug"), name, new[] { name == "LogException" ? typeof(Exception) : typeof(object) }),
                prefix: new HarmonyMethod(typeof(RoundtripChecks), nameof(ForwardEngineLog)));
        // Odin's shipped emitter uses .NET Framework APIs absent in this .NET test host.
        // Its built-in reflection fallback retains the format. Timings are NOT native Unity serializer timings.
        new Harmony("cityperformance.offline-emit-api").Patch(AccessTools.PropertyGetter(odin.GetType("OdinSerializer.Utilities.EmitUtilities"), "CanEmit"),
            transpiler: new HarmonyMethod(typeof(RoundtripChecks), nameof(ReflectionFallback)));
        var application = unity.GetType("UnityEngine.Application");
        new Harmony("cityperformance.offline-save-log").Patch(AccessTools.Method(application, "add_logMessageReceived"), prefix: new HarmonyMethod(typeof(RoundtripChecks), nameof(AddObserver)));
        new Harmony("cityperformance.offline-save-log").Patch(AccessTools.Method(application, "remove_logMessageReceived"), prefix: new HarmonyMethod(typeof(RoundtripChecks), nameof(RemoveObserver)));
        var adapter = mod.GetType("CityPerformance.GameSaveSnapshot", true);
        AccessTools.Method(adapter, "Initialize").Invoke(null, null);
        // .NET10-specific default comparer, absent from the audited Unity Mono corelib.
        // Notification.OnDeserialization creates six empty dictionaries with it in fixture1.
        // Warm ONLY that host formatter, and account for its exact known fallback warning.
        var hostComparer = EqualityComparer<string>.Default.GetType();
        if (hostComparer.FullName == "System.Collections.Generic.StringEqualityComparer")
        {
            int before = DiagnosticCount;
            var policy = AccessTools.Field(adapter, "policy").GetValue(null);
            var locator = odin.GetType("OdinSerializer.FormatterLocator").GetMethods().Single(m => m.Name == "GetFormatter" && !m.IsGenericMethod && m.GetParameters().Length == 3);
            locator.Invoke(null, new[] { (object)hostComparer, policy, false });
            locator.Invoke(null, new[] { (object)hostComparer, policy, true });
            string expected = "Type StringEqualityComparer implements the interface ISerializable but does not implement the required constructor with signature StringEqualityComparer(SerializationInfo info, StreamingContext context). The interface declaration will be ignored, and the formatter fallbacks to reflection.";
            var added = diagnostics.ToArray().Skip(before).ToArray();
            if (added.Length > 1 || added.Any(x => x != expected)) throw new Exception("Unexpected host-comparer warmup diagnostics");
            Console.WriteLine("HOST ADAPTATION: prewarmed stateless .NET10 default string comparer; accounted warnings=" + added.Length);
        }
        var schema = AccessTools.Field(adapter, "Schema").GetValue(null);
        var copierType = mod.GetType("CityPerformance.SaveSnapshotCopy", true);
        var serialize = AccessTools.Method(adapter, "Serialize");
        var deserialize = odin.GetType("OdinSerializer.SerializationUtility").GetMethods().Single(m => m.Name == "DeserializeValue" && m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(Stream)).MakeGenericMethod(game.GetType("GameInstance"));
        var binary = Enum.Parse(odin.GetType("OdinSerializer.DataFormat"), "Binary");
        var contextType = odin.GetType("OdinSerializer.DeserializationContext");
        object Read(Stream s)
        {
            int before = DiagnosticCount;
            var context = Activator.CreateInstance(contextType);
            var config = contextType.GetProperty("Config").GetValue(context);
            var debug = config.GetType().GetProperty("DebugContext").GetValue(config);
            debug.GetType().GetProperty("ErrorHandlingPolicy").SetValue(debug, Enum.Parse(odin.GetType("OdinSerializer.ErrorHandlingPolicy"), "ThrowOnErrors"));
            debug.GetType().GetProperty("Logger").SetValue(debug, Activator.CreateInstance(adapter.GetNestedType("WorkerLogger", BindingFlags.NonPublic), true));
            var result = deserialize.Invoke(null, new[] { s, binary, context });
            NoDiagnostics(before, "deserialize"); return result;
        }
        bool watching = false; string firstFailure = null;
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => { if (watching && firstFailure == null) firstFailure = e.Exception.ToString(); };
        byte[] Bytes(object instance)
        {
            using var stream = new MemoryStream(); firstFailure = null; watching = true;
            int before = DiagnosticCount;
            try { serialize.Invoke(null, new[] { stream, instance }); NoDiagnostics(before, "serialize"); return stream.ToArray(); }
            catch { Console.Error.WriteLine("First serializer exception: " + firstFailure); throw; }
            finally { watching = false; }
        }
        foreach (string fixture in fixtures)
        {
            using var file = File.OpenRead(fixture);
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            using var uncompressed = new MemoryStream(); zip.CopyTo(uncompressed); uncompressed.Position = 0;
            int beforeInitialRead = DiagnosticCount;
            var instance = AccessTools.Method(game.GetType("SaveGameSerializationHelper"), "DeserializeBinaryData").Invoke(null, new object[] { Path.GetFullPath(fixture) });
            NoDiagnostics(beforeInitialRead, "initial native binary loader");
            if (instance == null) throw new Exception("Native loader returned no save");
            string nativeTemp = Path.GetFullPath(fixture) + ".native-test-tmp";
            byte[] baseline;
            try
            {
                bool saved = (bool)AccessTools.Method(game.GetType("SaveGameSerializationHelper"), "SerializeBinaryData").Invoke(null, new[] { (object)nativeTemp, instance, false });
                if (!saved) throw new Exception("Native staging writer failed");
                baseline = File.ReadAllBytes(nativeTemp);
            }
            finally { File.Delete(nativeTemp); }
            if (!Bytes(instance).SequenceEqual(baseline)) throw new Exception("New serializer differs from native uncompressed writer");
            var copier = Activator.CreateInstance(copierType, schema, 5000d);
            long allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            var snapshot = copierType.GetMethod("Copy").Invoke(copier, new[] { instance });
            double capture = timer.Elapsed.TotalMilliseconds; allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            int beforeWarm = DiagnosticCount;
            AccessTools.Method(adapter, "PrepareWorker").Invoke(null, null);
            NoDiagnostics(beforeWarm, "worker formatter preparation");
            foreach (var t in (IEnumerable<Type>)schema.GetType().GetProperty("WarmTypes").GetValue(schema))
                Console.WriteLine("WARM TYPE " + t.FullName + " interfaces=" + string.Join(",", t.GetInterfaces().Select(i => i.Name)));
            foreach (var section in ((System.Collections.IEnumerable)copierType.GetField("Sections").GetValue(copier)).Cast<object>().OrderByDescending(x => (double)x.GetType().GetField("Item2").GetValue(x)).Take(6))
                Console.WriteLine("CAPTURE section=" + section);
            timer.Restart(); var copied = Task.Run(() => Bytes(snapshot)).GetAwaiter().GetResult(); double worker = timer.Elapsed.TotalMilliseconds;
            if (!baseline.SequenceEqual(copied)) throw new Exception("Byte mismatch for " + Path.GetFileName(fixture));
            using var check = new MemoryStream(copied); var reloaded = Read(check);
            if (!Bytes(reloaded).SequenceEqual(baseline)) throw new Exception("Reload mismatch for " + fixture);
            string nativeLoad = Path.GetFullPath(fixture) + ".native-load-test-tmp";
            try
            {
                using (var output = File.Create(nativeLoad)) using (var compressed = new GZipStream(output, CompressionLevel.Optimal)) compressed.Write(copied);
                int before = DiagnosticCount;
                var loaded = AccessTools.Method(game.GetType("SaveGameSerializationHelper"), "DeserializeBinaryData").Invoke(null, new object[] { nativeLoad });
                NoDiagnostics(before, "native binary loader");
                if (!Bytes(loaded).SequenceEqual(copied)) throw new Exception("Native loader mismatch");
            }
            finally { File.Delete(nativeLoad); }
            var money = game.GetType("GameInstance").GetField("Money");
            money.SetValue(instance, Convert.ChangeType(1234567, money.FieldType));
            var mods = (System.Collections.IDictionary)game.GetType("GameInstance").GetField("modData").GetValue(instance);
            mods["snapshot-test-only"] = "changed after capture";
            if (!Bytes(snapshot).SequenceEqual(copied)) throw new Exception("Live mutation escaped into snapshot");
            WorkerChecks.Run(game, mod, snapshot, copied);
            for (int warm = 0; warm < 3; warm++) {
                var next = Activator.CreateInstance(copierType, schema, 5000d); timer.Restart();
                copierType.GetMethod("Copy").Invoke(next, new[] { instance });
                Console.WriteLine($"WARM capture_ms={timer.Elapsed.TotalMilliseconds:F3}");
            }
            Console.WriteLine($"PASS fixture={Path.GetFileName(fixture)} uncompressed_bytes={baseline.Length} capture_ms={capture:F3} capture_alloc_bytes={allocated} worker_serialize_ms={worker:F3} objects={copierType.GetProperty("Objects").GetValue(copier)} elements={copierType.GetProperty("ElementCount").GetValue(copier)} exact_bytes=true reload=true native_loader=true isolated=true (offline .NET; not Unity frame timing)");
        }
        AccessTools.Field(adapter, "warmed").GetValue(null).GetType().GetMethod("Clear").Invoke(AccessTools.Field(adapter, "warmed").GetValue(null), null);
        var injection = new Harmony("cityperformance.offline-warm-warning");
        try
        {
            injection.Patch(AccessTools.Method(odin.GetType("OdinSerializer.Serializer"), "Get", new[] { typeof(Type) }),
                prefix: new HarmonyMethod(typeof(RoundtripChecks), nameof(InjectWarning)));
            bool refused = false;
            try { AccessTools.Method(adapter, "PrepareWorker").Invoke(null, null); }
            catch (TargetInvocationException e) { refused = e.InnerException is NotSupportedException; }
            if (!refused || observers.Count != 0) throw new Exception("Warmup diagnostic did not refuse capture and remove observer");
            Console.WriteLine("PASS warmup: unexpected Unity warning refuses capture and removes observer");
        }
        finally { injection.UnpatchAll(injection.Id); }
    }
    private static bool SkipEngineLog() => false;
    private static bool ForwardEngineLog(object __0)
    {
        string message = __0?.ToString() ?? "null"; diagnostics.Enqueue(message);
        foreach (var observer in observers.ToArray()) observer.DynamicInvoke(message, "", Enum.Parse(logType, "Warning"));
        Console.Error.WriteLine("Offline Unity diagnostic: " + message); return false;
    }
    private static bool AddObserver(Delegate __0) { observers.Add(__0); return false; }
    private static bool RemoveObserver(Delegate __0) { observers.Remove(__0); return false; }
    private static void InjectWarning() => ForwardEngineLog("injected unexpected formatter warning");
    private static IEnumerable<CodeInstruction> ReflectionFallback(IEnumerable<CodeInstruction> instructions) => new[] { new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Ret) };
}
