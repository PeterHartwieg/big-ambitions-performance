using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;

namespace CityPerformance
{
    // Only the audited Save call sites change. The native event, slot selection, thumbnail,
    // metadata construction, thread field and save-in-progress lifecycle remain in use.
    internal static class BackgroundSaves
    {
        internal static volatile bool Enabled = true, Faulted;
        private static bool installed, validated, quitting;
        private static int mainThread, validatedRevision;
        [ThreadStatic] private static Call current;
        private static volatile Job pending;
        private static readonly Queue<string> reports = new Queue<string>();
        // No game reflection in type initialization: an unknown build must still load
        // the mod's settings and diagnostics. Resolve only inside guarded installation.
        private static MethodInfo save, serialize, compress, join, saving, writeMeta, priority;
        private static FieldInfo processes, gameField, pathField, jsonField, inputField, outputField;
        private static MethodBase[] guarded;
        private static MethodInfo[] nativeSync, loadBoundaries;
        private static MethodInfo nativeAsync;
        internal static void Bind()
        {
            var manager = typeof(SaveGameManager);
            save = AccessTools.DeclaredMethod(manager, "Save", new[] { typeof(SaveGameManager.SaveType), typeof(string), typeof(string) });
            serialize = AccessTools.DeclaredMethod(manager, "SerializeSaveGame", new[] { typeof(object) });
            compress = AccessTools.DeclaredMethod(manager, "CompressSaveGame", new[] { typeof(object) });
            join = AccessTools.DeclaredMethod(manager, "JoinSaveGameThreads", Type.EmptyTypes);
            saving = AccessTools.PropertyGetter(manager, "SavingGameInProgress");
            writeMeta = AccessTools.DeclaredMethod(typeof(File), "WriteAllText", new[] { typeof(string), typeof(string) });
            priority = AccessTools.PropertySetter(typeof(Thread), "Priority");
            processes = AccessTools.Field(manager, "_saveProcessesRunning");
            var wrapper = manager.GetNestedType("SaveGameThreadWrapper", BindingFlags.NonPublic);
            var compressedWrapper = manager.GetNestedType("CompressionThreadWrapper", BindingFlags.NonPublic);
            if (wrapper == null || compressedWrapper == null) throw new MissingMemberException("Native save wrappers changed");
            gameField = AccessTools.Field(wrapper, "SaveGame"); pathField = AccessTools.Field(wrapper, "Path"); jsonField = AccessTools.Field(wrapper, "SaveAsJson");
            inputField = AccessTools.Field(compressedWrapper, "inputPath"); outputField = AccessTools.Field(compressedWrapper, "outputPath");
            nativeSync = new[] {
                AccessTools.DeclaredMethod(typeof(SaveGameSerializationHelper), "SerializeBinaryData", new[] { typeof(string), typeof(GameInstance), typeof(bool) }),
                AccessTools.DeclaredMethod(typeof(SaveGameSerializationHelper), "DeserializeBinaryData", new[] { typeof(string) }),
                AccessTools.DeclaredMethod(manager, "CreateSaveSnapshot", new[] { typeof(GameInstance) }) };
            nativeAsync = AccessTools.DeclaredMethod(typeof(SaveGameSerializationHelper), "DeserializeBinaryDataAsync", new[] { typeof(string), typeof(IProgress<(float, string)>) });
            loadBoundaries = new[] {
                AccessTools.DeclaredMethod(manager, "Load", new[] { typeof(SaveGameManager.SaveGameStruct), typeof(bool) }),
                AccessTools.DeclaredMethod(manager, "LoadAsync", new[] { typeof(SaveGameManager.SaveGameStruct), typeof(bool) }) };
            guarded = new MethodBase[] { save, serialize, compress, join, saving,
                AccessTools.DeclaredMethod(typeof(SaveGameSerializationHelper), "SerializeBinaryData", new[] { typeof(string), typeof(GameInstance), typeof(bool) }),
                AccessTools.DeclaredMethod(typeof(SaveGameSerializationHelper), "CompressBinaryData", new[] { typeof(string), typeof(string) }),
                AccessTools.DeclaredMethod(typeof(Player.SaveSystem.SaveGameSerializationPolicy), "ShouldSerializeMember", new[] { typeof(MemberInfo) }) };
            guarded = guarded.Concat(nativeSync).Concat(loadBoundaries).Concat(new[] { nativeAsync }).ToArray();
            if (guarded.Any(m => m == null) || writeMeta == null || priority == null ||
                new[] { processes, gameField, pathField, jsonField, inputField, outputField }.Any(f => f == null))
                throw new MissingMemberException("Native save bindings changed");
        }

        internal sealed class Call
        {
            internal Call Previous;
            internal bool Eligible;
            internal long Start;
            internal Job Job;
        }
        internal sealed class Job
        {
            internal GameInstance Snapshot;
            internal WeakReference<GameInstance> Source;
            internal Thread Thread;
            internal readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            internal readonly object PriorityLock = new object();
            internal bool Draining;
            internal volatile bool Done;
            internal bool Abort, Validate, Matched, OwnsCounter;
            internal string NativeTemp, MetadataPath, Metadata, Error, TempPrefix, Sections;
            internal int Frame, Day, Objects, Plans, SchemaRevision, CaptureGc0, CaptureGc1, CaptureGc2;
            internal long Elements, CaptureBytes, RawBytes, DiskBytes;
            internal double PrepareMs, CaptureMs, MainMs, SerializeMs, CompressMs, CommitMs, WorkerMs;
        }
        internal static void Install()
        {
            if (installed) return;
            installed = false; Faulted = false; validated = false; mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                if (!Enabled || quitting || !PerformanceMod.Verified)
                { PerformanceMod.Log("AUTOSAVE inactive: option off, quitting, or unverified build."); return; }
                Bind();
                if (!CheckCompatibility()) return;
                GameSaveSnapshot.Initialize();
                Rewrite(DetailedTrace.Instructions(save)).ToArray();
                if (Faulted) return;
                PerformanceMod.Harmony.Patch(save, prefix: new HarmonyMethod(typeof(BackgroundSaves), nameof(Begin)),
                    transpiler: new HarmonyMethod(typeof(BackgroundSaves), nameof(Rewrite)) { priority = HarmonyLib.Priority.Last }, finalizer: new HarmonyMethod(typeof(BackgroundSaves), nameof(End)));
                PerformanceMod.Harmony.Patch(join, prefix: new HarmonyMethod(typeof(BackgroundSaves), nameof(Drain)));
                PerformanceMod.Harmony.Patch(saving, postfix: new HarmonyMethod(typeof(BackgroundSaves), nameof(InProgress)));
                foreach (var method in nativeSync)
                    PerformanceMod.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(SaveSerializationGate), nameof(SaveSerializationGate.Enter)),
                        finalizer: new HarmonyMethod(typeof(SaveSerializationGate), nameof(SaveSerializationGate.Exit)));
                PerformanceMod.Harmony.Patch(nativeAsync, prefix: new HarmonyMethod(typeof(SaveSerializationGate), nameof(SaveSerializationGate.EnterAsync)),
                    finalizer: new HarmonyMethod(typeof(SaveSerializationGate), nameof(SaveSerializationGate.ExitAsync)));
                foreach (var method in loadBoundaries)
                    PerformanceMod.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(BackgroundSaves), nameof(Drain)));
                installed = true;
                PerformanceMod.Log("AUTOSAVE installed; defaults on; first eligible save validates snapshot bytes; native format and save events retained.");
            }
            catch (Exception e)
            {
                Faulted = true;
                foreach (var method in new[] { save, join, saving }.Concat(loadBoundaries ?? Array.Empty<MethodInfo>()))
                    foreach (string hook in new[] { nameof(Begin), nameof(Rewrite), nameof(End), nameof(Drain), nameof(InProgress) })
                        if (method != null) PerformanceMod.Harmony.Unpatch(method, AccessTools.Method(typeof(BackgroundSaves), hook));
                foreach (var method in (nativeSync ?? Array.Empty<MethodInfo>()).Concat(new[] { nativeAsync }))
                    if (method != null) foreach (string hook in new[] { nameof(SaveSerializationGate.Enter), nameof(SaveSerializationGate.Exit), nameof(SaveSerializationGate.EnterAsync), nameof(SaveSerializationGate.ExitAsync) })
                        PerformanceMod.Harmony.Unpatch(method, AccessTools.Method(typeof(SaveSerializationGate), hook));
                PerformanceMod.Log("AUTOSAVE unavailable: " + e.Message);
            }
        }
        internal static bool Conflict() => guarded == null || guarded.Any(m => m == null || Harmony.GetPatchInfo(m)?.Owners.Any(o => o != PerformanceMod.Id) == true);
        internal static bool CheckCompatibility()
        {
            try
            {
                if (!Conflict() && !GameSaveSnapshot.ForeignFormatterOrMultiplayer()) return true;
                Faulted = true; PerformanceMod.Log("AUTOSAVE disabled: foreign save patch/formatter or multiplayer.");
            }
            catch (Exception e)
            { Faulted = true; PerformanceMod.Log("AUTOSAVE compatibility inspection failed; using native saving: " + e.Message); }
            return false;
        }
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        {
            var code = source.Select(i => new CodeInstruction(i)).ToList();
            var sites = new[] { (serialize, nameof(Capture), OpCodes.Call), (compress, nameof(Worker), OpCodes.Ldftn),
                (writeMeta, nameof(Metadata), OpCodes.Call), (priority, nameof(Priority), OpCodes.Callvirt) };
            if (sites.Any(s => s.Item1 == null || code.Count(i => i.opcode == s.Item3 && Equals(i.operand, s.Item1)) != 1))
            {
                Faulted = true;
                PerformanceMod.Log("AUTOSAVE disabled: changed save call sites; retaining incoming instructions.");
                return code;
            }
            foreach (var site in sites)
            {
                var instruction = code.Single(i => i.opcode == site.Item3 && Equals(i.operand, site.Item1));
                instruction.operand = AccessTools.Method(typeof(BackgroundSaves), site.Item2);
                if (site.Item3 == OpCodes.Callvirt) instruction.opcode = OpCodes.Call;
            }
            return code;
        }
        private static void Begin(SaveGameManager.SaveType saveType, out Call __state)
        {
            if (current == null) Drain();
            __state = new Call { Previous = current, Start = Stopwatch.GetTimestamp() };
            current = __state;
            if (!installed || !Enabled || Faulted || quitting || SaveSerializationGate.Busy || current.Previous != null || Thread.CurrentThread.ManagedThreadId != mainThread || !PerformanceMod.Loaded || !CityEntry.Ready) return;
            // Manual saves and old-age backups retain the native synchronous serialization path.
            if (saveType != SaveGameManager.SaveType.RecoverSave && saveType != SaveGameManager.SaveType.MidnightSave) return;
            if (!CheckCompatibility()) return;
            __state.Eligible = !(bool)saving.Invoke(null, null);
        }
        private static void Capture(object data)
        {
            if (current?.Eligible == true && Enabled && !Faulted && !quitting && !SaveSerializationGate.Busy && !(bool)jsonField.GetValue(data))
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    long allocated = CaptureAllocatedBytes();
                    int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                    var source = (GameInstance)gameField.GetValue(data);
                    // The first comparison includes cold JIT work. These are abort budgets,
                    // not frame-time promises; a refused capture adds to the subsequent native save.
                    var copier = new SaveSnapshotCopy(GameSaveSnapshot.Schema, validated ? 500 : 1500);
                    var snapshot = (GameInstance)copier.Copy(source);
                    GameSaveSnapshot.PrepareWorker();
                    var job = new Job { Snapshot = snapshot, Source = new WeakReference<GameInstance>(source),
                        NativeTemp = (string)pathField.GetValue(data), Validate = NeedsValidation(GameSaveSnapshot.Schema.Revision), OwnsCounter = true,
                        Frame = UnityEngine.Time.frameCount, Day = snapshot.Day, Objects = copier.Objects,
                        Elements = copier.ElementCount, Plans = GameSaveSnapshot.Schema.PlanCount, SchemaRevision = GameSaveSnapshot.Schema.Revision,
                        PrepareMs = Ms(start - current.Start), CaptureMs = Ms(Stopwatch.GetTimestamp() - start),
                        CaptureBytes = CaptureAllocatedBytes() - allocated,
                        CaptureGc0 = GC.CollectionCount(0) - gc0, CaptureGc1 = GC.CollectionCount(1) - gc1, CaptureGc2 = GC.CollectionCount(2) - gc2 };
                    // Some Mono GC implementations expose the API but report zero.
                    // Do not present an unavailable counter as a zero-allocation capture.
                    if (job.CaptureBytes <= 0) job.CaptureBytes = -1;
                    job.Sections = string.Join(", ", copier.Sections.OrderByDescending(x => x.Milliseconds).Take(6).Select(x => x.Name + ":" + x.Milliseconds.ToString("F3", CultureInfo.InvariantCulture) + "ms/" + x.Objects + "objects"));
                    current.Job = pending = job;
                    if (!job.Validate) return;
                }
                catch (Exception e)
                {
                    Faulted = true;
                    PerformanceMod.Log("AUTOSAVE capture refused; using native save; capture_ms=" + Ms(Stopwatch.GetTimestamp() - start).ToString("F3", CultureInfo.InvariantCulture) + "; " + e.Message);
                }
            }
            Invoke(serialize, data);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long CaptureAllocatedBytes() => GC.GetAllocatedBytesForCurrentThread();
        private static void Priority(Thread thread, ThreadPriority requested)
        {
            if (current?.Job != null) { current.Job.Thread = thread; thread.Priority = current.Job.Validate ? requested : ThreadPriority.BelowNormal; }
            else thread.Priority = requested;
        }
        private static void Metadata(string path, string text)
        {
            var job = current?.Job;
            if (job != null) { job.MetadataPath = path; job.Metadata = text; }
            if (job == null || job.Validate) File.WriteAllText(path, text);
        }
        private static Exception End(Call __state, bool __result, Exception __exception)
        {
            if (__state == null) return __exception;
            try
            {
                var job = __state.Job;
                if (job != null)
                {
                    job.MainMs = Ms(Stopwatch.GetTimestamp() - __state.Start);
                    job.Abort = __exception != null || !__result || job.Metadata == null;
                    job.Ready.Set();
                    if (job.Thread == null || (job.Thread.ThreadState & System.Threading.ThreadState.Unstarted) != 0)
                    {
                        job.Error = "Native save thread did not start";
                        job.Done = true;
                    }
                }
            }
            finally { current = __state.Previous; }
            if (quitting && current == null)
                try { Drain(); } catch (Exception e) { Faulted = true; PerformanceMod.Log("AUTOSAVE quit drain failed: " + e.Message); }
            return __exception;
        }
        private static void Worker(object data)
        {
            var job = pending;
            if (job == null || !ReferenceEquals(job.Thread, Thread.CurrentThread)) { Invoke(compress, data); return; }
            long started = Stopwatch.GetTimestamp();
            try
            {
                job.Ready.Wait();
                if (job.Validate)
                {
                    Invoke(compress, data); job.OwnsCounter = false;
                    // Retain native compression priority; only the additional byte
                    // comparison work belongs at background priority.
                    lock (job.PriorityLock)
                        if (!job.Draining) Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                }
                if (job.Abort)
                {
                    throw new InvalidOperationException("Native save preparation failed; background publication cancelled");
                }
                string destination = (string)outputField.GetValue(data);
                if ((string)inputField.GetValue(data) != job.NativeTemp || job.MetadataPath != destination + ".meta")
                    throw new InvalidOperationException("Native save paths changed");
                string expected = null;
                if (job.Validate)
                {
                    expected = Digest(job.NativeTemp);
                }
                job.TempPrefix = destination + ".cityperformance-" + Guid.NewGuid().ToString("N");
                long phase = Stopwatch.GetTimestamp();
                using (var raw = new FileStream(job.TempPrefix + ".raw", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    SaveSerializationGate.Run(() => GameSaveSnapshot.Serialize(raw, job.Snapshot));
                    job.RawBytes = raw.Length;
                }
                job.SerializeMs = Ms(Stopwatch.GetTimestamp() - phase);
                job.Snapshot = null;
                if (job.Validate)
                {
                    job.Matched = expected == Digest(job.TempPrefix + ".raw");
                    if (!job.Matched) throw new InvalidOperationException("Snapshot/native serialized bytes differ; background saves remain disabled");
                    return;
                }
                phase = Stopwatch.GetTimestamp();
                using (var raw = File.OpenRead(job.TempPrefix + ".raw"))
                using (var file = new FileStream(job.TempPrefix + ".data-tmp", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var gzip = new GZipStream(file, CompressionLevel.Optimal, true)) raw.CopyTo(gzip);
                    file.Flush(true); job.DiskBytes = file.Length;
                }
                job.CompressMs = Ms(Stopwatch.GetTimestamp() - phase);
                phase = Stopwatch.GetTimestamp();
                File.WriteAllText(job.TempPrefix + ".meta-tmp", job.Metadata);
                using (var meta = new FileStream(job.TempPrefix + ".meta-tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) meta.Flush(true);
                SaveFileCommit.Publish(job.TempPrefix + ".data-tmp", job.TempPrefix + ".meta-tmp", destination);
                job.CommitMs = Ms(Stopwatch.GetTimestamp() - phase);
            }
            catch (Exception e) { job.Error = e.ToString(); }
            finally
            {
                job.Snapshot = null;
                if (job.TempPrefix != null) foreach (string ext in new[] { ".raw", ".data-tmp", ".meta-tmp" })
                    try { File.Delete(job.TempPrefix + ext); } catch { /* Cleanup cannot hide the save outcome. */ }
                job.WorkerMs = Ms(Stopwatch.GetTimestamp() - started);
                job.Done = true;
            }
        }
        internal static void Tick()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread) return;
            var job = pending;
            if (job == null || !job.Done) return;
            pending = null;
            // Native thumbnail callbacks also decrement this field on the main thread. Never race them from our worker.
            if (job.OwnsCounter) processes.SetValue(null, Math.Max(0, (int)processes.GetValue(null) - 1));
            if (job.Error != null)
            {
                Faulted = true;
                if (job.OwnsCounter)
                {
                    if (job.Source.TryGetTarget(out var source) && ReferenceEquals(source, SaveGameManager.Current)) SaveGameManager.MarkChange();
                    try { UI.Notification.Notifications.ShowError("cityperformance:savefailed"); }
                    catch (Exception e) { PerformanceMod.Log("AUTOSAVE failure notification unavailable: " + e.Message); }
                }
            }
            else if (job.Validate && job.Matched) { validated = true; validatedRevision = job.SchemaRevision; }
            string report = string.Format(CultureInfo.InvariantCulture,
                "AUTOSAVE mode={0}; frame={1}; day={2}; prepare_ms={3:F3}; capture_ms={4:F3}; capture_alloc_bytes={5}; objects={6}; elements={7}; plans={8}; main_call_ms={9:F3}; worker_serialize_ms={10:F3}; worker_compress_ms={11:F3}; commit_ms={12:F3}; worker_elapsed_ms={13:F3}; raw_bytes={14}; compressed_bytes={15}; byte_match={16}; error={17}",
                job.Validate ? "validation" : "background", job.Frame, job.Day, job.PrepareMs, job.CaptureMs, job.CaptureBytes, job.Objects, job.Elements, job.Plans,
                job.MainMs, job.SerializeMs, job.CompressMs, job.CommitMs, job.WorkerMs, job.RawBytes, job.DiskBytes, job.Matched, job.Error ?? "none");
            report += "; capture_gc_gen0=" + job.CaptureGc0 + "; capture_gc_gen1=" + job.CaptureGc1 +
                "; capture_gc_gen2=" + job.CaptureGc2 + "; capture_sections=" + job.Sections;
            reports.Enqueue(report); while (reports.Count > 32) reports.Dequeue();
            PerformanceMod.Log(report); lock (job.Ready) job.Ready.Dispose();
        }
        private static void InProgress(ref bool __result)
        {
            if (pending == null) return;
            Tick(); __result = (int)processes.GetValue(null) > 0 || pending != null;
        }
        internal static void Drain()
        {
            var job = pending;
            if (job == null) return;
            // Called only at native lifecycle boundaries, subsequent saves or mod unload.
            // Never wait inside a reentrant save callback before its outer call has published metadata.
            if (current != null) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                lock (job.Ready) { if (!job.Done) job.Ready.Wait(); }
            if (job.Thread != null && job.Thread.IsAlive && job.Thread != Thread.CurrentThread)
            {
                try
                {
                    lock (job.PriorityLock)
                    {
                        job.Draining = true;
                        if (job.Thread.IsAlive && job.Thread.Priority < ThreadPriority.Normal) job.Thread.Priority = ThreadPriority.Normal;
                    }
                }
                catch (Exception e) { PerformanceMod.Log("AUTOSAVE worker priority unavailable while draining: " + e.Message); }
                job.Thread.Join();
            }
            Tick();
        }
        internal static bool NeedsValidation(int revision) => !validated || revision != validatedRevision;
        internal static void OnApplicationQuit() { quitting = true; Drain(); }
        internal static void Stop() { Drain(); installed = false; validated = false; }
        internal static void StartCity() { Drain(); validated = false; validatedRevision = 0; reports.Clear(); }
        internal static void SetEnabled(bool value)
        {
            Enabled = value;
            if (value && !installed && !Faulted) Install();
            PerformanceMod.Log("AUTOSAVE requested=" + value + "; validated=" + validated + "; faulted=" + Faulted);
        }
        internal static void Describe(List<string> output)
        {
            output.Add("autosave_requested=" + Enabled + "; installed=" + installed + "; faulted=" + Faulted + "; validated=" + validated + "; pending=" + (pending != null));
            output.AddRange(reports);
        }
        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        private static string Digest(string path)
        {
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file));
        }
        private static void Invoke(MethodInfo method, object data)
        {
            try { method.Invoke(null, new[] { data }); }
            catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
        }
    }
}
