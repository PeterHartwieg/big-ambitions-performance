using System.IO.Compression;
using System.Reflection;
using HarmonyLib;

static class WorkerChecks
{
    public static void Run(Assembly game, Assembly mod, object snapshot, byte[] serialized)
    {
        var coordinator = mod.GetType("CityPerformance.BackgroundSaves");
        AccessTools.Method(coordinator, "Bind").Invoke(null, null);
        var jobType = coordinator.GetNestedType("Job", BindingFlags.NonPublic);
        var manager = game.GetType("SaveGameManager");
        var compressionType = manager.GetNestedType("CompressionThreadWrapper", BindingFlags.NonPublic);
        var worker = AccessTools.Method(coordinator, "Worker");
        var processes = AccessTools.Field(manager, "_saveProcessesRunning");
        AccessTools.Field(coordinator, "mainThread").SetValue(null, Thread.CurrentThread.ManagedThreadId);
        string folder = Path.Combine(Path.GetTempPath(), "city-save-worker-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string destination = Path.Combine(folder, "test.hsg"), raw = Path.Combine(folder, "native.raw");
            void Check(bool okay, string label) { if (!okay) throw new Exception(label); Console.WriteLine("PASS worker: " + label); }
            foreach (string scenario in new[] { "validation", "background", "abort", "write-failure", "mismatch" })
            {
                File.WriteAllText(destination, "old-save"); File.WriteAllText(destination + ".meta", "old-meta");
                File.WriteAllBytes(raw, scenario == "mismatch" ? new byte[] { 0, 1 } : serialized);
                var job = Activator.CreateInstance(jobType, true);
                void Set(string name, object value) => AccessTools.Field(jobType, name).SetValue(job, value);
                bool validation = scenario == "validation" || scenario == "mismatch";
                string output = scenario == "write-failure" ? Path.Combine(folder, "missing", "test.hsg") : destination;
                Set("Snapshot", snapshot); Set("Source", Activator.CreateInstance(typeof(WeakReference<>).MakeGenericType(game.GetType("GameInstance")), snapshot));
                Set("NativeTemp", raw); Set("Validate", validation); Set("OwnsCounter", true);
                Set("MetadataPath", output + ".meta"); Set("Metadata", "new-meta"); Set("Abort", scenario == "abort");
                var data = Activator.CreateInstance(compressionType); AccessTools.Field(compressionType, "inputPath").SetValue(data, raw); AccessTools.Field(compressionType, "outputPath").SetValue(data, output);
                var thread = new Thread(() => worker.Invoke(null, new[] { data })); Set("Thread", thread);
                AccessTools.Field(coordinator, "pending").SetValue(null, job); processes.SetValue(null, 2);
                int beforeWorker = RoundtripChecks.DiagnosticCount;
                thread.Start();
                bool waiting = SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0 || !thread.IsAlive, 2000);
                Check(waiting && thread.IsAlive && (thread.ThreadState & ThreadState.WaitSleepJoin) != 0 &&
                    AccessTools.Field(jobType, "TempPrefix").GetValue(job) == null && File.ReadAllText(destination) == "old-save",
                    scenario + " waits for main-thread metadata publication");
                ((ManualResetEventSlim)AccessTools.Field(jobType, "Ready").GetValue(job)).Set();
                if (!thread.Join(30000)) throw new Exception("worker timeout");
                RoundtripChecks.NoDiagnostics(beforeWorker, "worker " + scenario);
                string error = (string)AccessTools.Field(jobType, "Error").GetValue(job);
                Check((bool)AccessTools.Field(jobType, "Done").GetValue(job), scenario + " always completes");
                bool expectedSuccess = scenario == "validation" || scenario == "background";
                Check(expectedSuccess ? error == null : error != null, scenario + " reports correct outcome");
                if (scenario == "background")
                {
                    using var file = File.OpenRead(destination); using var zip = new GZipStream(file, CompressionMode.Decompress); using var buffer = new MemoryStream(); zip.CopyTo(buffer);
                    Check(buffer.ToArray().SequenceEqual(serialized) && File.ReadAllText(destination + ".meta") == "new-meta", "published native bytes and staged metadata");
                    Check(File.ReadAllText(destination + ".cityperformance-previous") == "old-save" && File.ReadAllText(destination + ".cityperformance-previous-metadata") == "old-meta", "previous complete pair retained");
                    Check((int)processes.GetValue(null) == 2, "background worker never decrements main-thread screenshot counter");
                }
                if (scenario == "validation")
                {
                    Check((bool)AccessTools.Field(jobType, "Matched").GetValue(job), "native and snapshot staging digests match");
                    using var file = File.OpenRead(destination); using var zip = new GZipStream(file, CompressionMode.Decompress); using var buffer = new MemoryStream(); zip.CopyTo(buffer);
                    Check(buffer.ToArray().SequenceEqual(serialized), "validation retains correct native compressed save output");
                }
                if (scenario == "abort" || scenario == "write-failure") Check(File.ReadAllText(destination) == "old-save" && File.ReadAllText(destination + ".meta") == "old-meta", scenario + " preserves previous files");
                AccessTools.Method(coordinator, "Tick").Invoke(null, null);
                Check((int)processes.GetValue(null) == 1 && AccessTools.Field(coordinator, "pending").GetValue(null) == null, scenario + " releases exactly one save unit on completion");
                AccessTools.Method(coordinator, "Tick").Invoke(null, null);
                Check((int)processes.GetValue(null) == 1, scenario + " repeated pump does not double-complete");
                Check(!Directory.GetFiles(folder).Any(p => p.EndsWith(".data-tmp") || p.EndsWith(".meta-tmp") || p.EndsWith(".raw") && p != raw), scenario + " cleans staging files");
            }
            var unstarted = Activator.CreateInstance(jobType, true);
            AccessTools.Field(jobType, "Thread").SetValue(unstarted, new Thread(() => throw new Exception("must not run")));
            AccessTools.Field(jobType, "OwnsCounter").SetValue(unstarted, true);
            AccessTools.Field(jobType, "Source").SetValue(unstarted, Activator.CreateInstance(typeof(WeakReference<>).MakeGenericType(game.GetType("GameInstance")), snapshot));
            var callType = coordinator.GetNestedType("Call", BindingFlags.NonPublic);
            var call = Activator.CreateInstance(callType, true);
            AccessTools.Field(callType, "Job").SetValue(call, unstarted);
            AccessTools.Field(coordinator, "pending").SetValue(null, unstarted); processes.SetValue(null, 2);
            var startupError = new IOException("injected thread startup failure");
            var returned = AccessTools.Method(coordinator, "End").Invoke(null, new object[] { call, false, startupError });
            Check(ReferenceEquals(returned, startupError), "thread startup failure preserves original exception");
            Check((bool)AccessTools.Field(jobType, "Done").GetValue(unstarted) && AccessTools.Field(jobType, "Error").GetValue(unstarted) != null, "unstarted worker becomes failed completion");
            AccessTools.Method(coordinator, "Drain").Invoke(null, null);
            Check((int)processes.GetValue(null) == 1 && AccessTools.Field(coordinator, "pending").GetValue(null) == null, "unstarted worker drains without waiting or leaking save counter");
        }
        finally { Directory.Delete(folder, true); }
    }
}
