using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;
using UI;
using UI.Load;

namespace CityPerformance
{
    // Only the discarded UnloadUnusedAssets return in the audited NewDay is replaced.
    // Daily simulation, events, autosaves and every other cleanup caller stay in place.
    internal static class MidnightCleanup
    {
        internal static bool Enabled, Faulted;
        private static readonly CleanupPlan plan = new CleanupPlan();
        private static readonly MethodInfo target = AccessTools.DeclaredMethod(typeof(GameManager), "NewDay", Type.EmptyTypes);
        private static readonly MethodInfo unload = AccessTools.DeclaredMethod(typeof(Resources), nameof(Resources.UnloadUnusedAssets), Type.EmptyTypes);
        private static bool installed;
        private static volatile bool multiplayer;
        private static volatile bool pressure;
        private static AsyncOperation operation;
        private static double nextPoll;
        private static long startedTicks, requests, deferred, coalesced, executions;
        private static long memoryBefore = -1, memoryAfter = -1, lastBatch;
        private static double callMs;
        private static string lastReason = "none";
        private static int requestFrame = -1, executionFrame = -1;
        private static double lastWait, lastLatency;

        internal static void Install()
        {
            installed = false; Faulted = false;
            Application.lowMemory -= LowMemory;
            Application.lowMemory += LowMemory;
            Application.quitting -= Quit;
            Application.quitting += Quit;
            AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
            try
            {
                // Conservative: no schedule change with the multiplayer mod loaded, even in a solo city.
                multiplayer = AppDomain.CurrentDomain.GetAssemblies().Any(a => IsMultiplayerAssembly(a.GetName().Name));
                if (!PerformanceMod.Verified || multiplayer || target == null || target.ReturnType != typeof(void) || target.IsStatic || Conflict())
                { PerformanceMod.Log("Not active: midnight cleanup deferral (build, signature, multiplayer or conflict)."); return; }
                // Refuse the hook entirely if the return is used or the call count changed.
                Rewrite(DetailedTrace.Instructions(target)).ToArray();
                PerformanceMod.Harmony.Patch(target, transpiler: new HarmonyMethod(typeof(MidnightCleanup), nameof(Rewrite)));
                installed = true;
                PerformanceMod.Log("Active: audited NewDay cleanup batching; defaults off; maximum wait " + F(plan.DelaySeconds) + "s excluding active saves/cleanup.");
            }
            catch (Exception e)
            {
                Faulted = true;
                if (target != null) PerformanceMod.Harmony.Unpatch(target, AccessTools.Method(typeof(MidnightCleanup), nameof(Rewrite)));
                PerformanceMod.Log("Not active: midnight cleanup: " + e.Message);
            }
        }

        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            var matches = new List<int>();
            for (int i = 0; i < code.Count; i++) if (code[i].opcode == OpCodes.Call && Equals(code[i].operand, unload)) matches.Add(i);
            if (matches.Count != 1 || matches[0] + 1 >= code.Count || code[matches[0] + 1].opcode != OpCodes.Pop)
                throw new InvalidOperationException("NewDay cleanup must have exactly one call with an immediately discarded result.");
            code[matches[0]].operand = AccessTools.Method(typeof(MidnightCleanup), nameof(Request));
            return code;
        }

        private static bool Conflict()
        {
            if (target == null) return true;
            var info = Harmony.GetPatchInfo(target);
            if (info != null) foreach (string owner in info.Owners) if (owner != PerformanceMod.Id) return true;
            return false;
        }
        private static void LowMemory() { pressure = true; }
        internal static bool IsMultiplayerAssembly(string name) => name != null &&
            (name.StartsWith("BigAmbitionsMP", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GoingPublic", StringComparison.OrdinalIgnoreCase));
        private static void AssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            if (IsMultiplayerAssembly(args.LoadedAssembly.GetName().Name)) multiplayer = true;
        }
        private static bool Hidden()
        {
            var ui = InstanceBehavior<UIs>.Instance;
            // A fade merely starting is still visible. Require an active, fully opaque overlay.
            bool mainHidden = LoadScene.isLoading || ui != null && ui.blackOverlay != null && ui.blackOverlay.isActiveAndEnabled &&
                ui.blackOverlay.gameObject.activeInHierarchy && ui.blackOverlay.color.a >= .999f;
            // Refresh the optional bridge: the cached LateUpdate camera may be one frame old,
            // and coverage/audio can be disabled while HART still records.
            return mainHidden && !CinematicWorld.HasRecordingView();
        }
        private static long Memory() => Profiler.GetTotalAllocatedMemoryLong();
        private static bool MemoryPressure(long memory)
        {
            long physical = (long)SystemInfo.systemMemorySize * 1024 * 1024;
            return pressure || physical > 0 && memory >= physical * .8;
        }

        // The caller discards this return. A deferred request therefore needs no fake Unity operation.
        private static AsyncOperation Request()
        {
            requests++;
            bool defer = false;
            try
            {
                if (installed && Enabled && !Faulted && PerformanceMod.Loaded && CityEntry.Ready && !multiplayer)
                {
                    if (Conflict()) { Faulted = true; PerformanceMod.Log("Midnight cleanup deferral disabled: foreign patch detected."); }
                    else
                    {
                        long memory = Memory();
                        bool first = plan.Request(Time.realtimeSinceStartupAsDouble, memory);
                        if (first) { requestFrame = Time.frameCount; deferred++; } else coalesced++;
                        defer = true;
                        // Even an immediately due request goes through Tick so an active
                        // save or previous cleanup cannot be overlapped or lose its handle.
                        PerformanceMod.Log("CLEANUP queued frame=" + Time.frameCount + "; first_frame=" + requestFrame +
                            "; coalesced=" + !first + "; batch_requests=" + plan.RequestsInBatch +
                            "; deadline_seconds=" + F(plan.DeadlineAt) + "; memory_at_request_bytes=" + plan.MemoryAtRequest);
                    }
                }
            }
            catch (Exception e) { Faulted = true; PerformanceMod.Log("Midnight cleanup deferral disabled: " + e.Message); }
            if (defer) return null;
            // Outside the guard: an exception from Unity's original call must propagate unchanged.
            return Execute("original-call");
        }

        private static AsyncOperation Execute(string reason)
        {
            lastWait = plan.Pending ? Math.Max(0, Time.realtimeSinceStartupAsDouble - plan.RequestedAt) : 0;
            lastBatch = plan.RequestsInBatch;
            plan.Clear(); pressure = false; lastReason = reason; executionFrame = Time.frameCount;
            memoryBefore = ReadMemoryForReport(); memoryAfter = -1;
            executions++; startedTicks = Stopwatch.GetTimestamp();
            try { operation = Resources.UnloadUnusedAssets(); }
            catch { Faulted = true; throw; }
            callMs = (Stopwatch.GetTimestamp() - startedTicks) * 1000.0 / Stopwatch.Frequency;
            PerformanceMod.Log("CLEANUP started reason=" + reason + "; frame=" + executionFrame + "; wait_seconds=" + F(lastWait) +
                "; batch_requests=" + lastBatch + "; memory_before_bytes=" + memoryBefore + "; call_ms=" + F(callMs) +
                " (call return only; Unity may continue the sweep later).");
            return operation;
        }
        private static long ReadMemoryForReport()
        {
            try { long value = Memory(); return value > 0 ? value : -1; }
            catch { return -1; } // Optional telemetry must never replace or retry a native operation.
        }
        internal static void Tick()
        {
            if (!plan.Pending && operation == null) return;
            string executeReason = null;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < nextPoll && nextPoll - now <= .25) return;
                nextPoll = now + .25;
                if (operation != null && operation.isDone)
                {
                    lastLatency = (Stopwatch.GetTimestamp() - startedTicks) * 1000.0 / Stopwatch.Frequency;
                    memoryAfter = ReadMemoryForReport();
                    operation = null;
                    PerformanceMod.Log("CLEANUP completed frame=" + Time.frameCount + "; execution_frame=" + executionFrame +
                        "; completion_latency_ms=" + F(lastLatency) + "; memory_after_bytes=" + memoryAfter +
                        "; observed_memory_drop_bytes=" + MemoryDrop() +
                        " (latency includes polling, not CPU time; memory delta includes intervening allocations).");
                }
                if (!plan.Pending || operation != null || SaveGameManager.SavingGameInProgress) return;
                if (!Enabled || Faulted || multiplayer || Conflict())
                {
                    if (Enabled && !Faulted) { Faulted = true; PerformanceMod.Log("Midnight cleanup deferral disabled: multiplayer or foreign patch detected."); }
                    executeReason = "disabled-or-conflict";
                }
                else
                {
                    long memory = Memory();
                    var reason = plan.Due(now, memory, Hidden(), MemoryPressure(memory), false);
                    if (reason == CleanupReason.MemoryUnavailable)
                    {
                        Faulted = true;
                        PerformanceMod.Log("Midnight cleanup batching disabled: allocated-memory telemetry unavailable.");
                    }
                    if (reason != CleanupReason.None) executeReason = reason.ToString();
                }
            }
            catch (Exception e)
            {
                Faulted = true;
                PerformanceMod.Log("Midnight cleanup scheduler disabled: " + e);
                if (plan.Pending) executeReason = "scheduler-fallback";
            }
            // Unity exceptions propagate to its Update loop; the request is already consumed, never retried.
            if (executeReason != null) Execute(executeReason);
        }
        internal static void SetEnabled(bool value)
        {
            Enabled = value;
            PerformanceMod.Log("Midnight cleanup deferral requested=" + value + "; installed=" + installed + "; faulted=" + Faulted);
            // Tick drains a pending request after any active save or previous operation finishes.
        }
        internal static void SetDelayMinutes(int value)
        {
            plan.SetDelaySeconds((double)value * 60);
            PerformanceMod.Log("Cleanup batching maximum_wait_seconds=" + F(plan.DelaySeconds) +
                "; pending_deadline_seconds=" + (plan.Pending ? F(plan.DeadlineAt) : "none"));
        }
        internal static void EndCity()
        {
            if (plan.Pending) Execute("city-or-mod-unload");
        }
        internal static void StartCity()
        {
            plan.Clear(); pressure = false; nextPoll = 0;
            requests = deferred = coalesced = executions = 0; requestFrame = executionFrame = -1;
            lastReason = "none"; lastWait = lastLatency = 0;
            memoryBefore = memoryAfter = -1; lastBatch = 0; callMs = 0;
        }
        private static void Quit()
        {
            // Process exit releases memory; do not add a cleanup stall or retain a city after quitting.
            if (plan.Pending) PerformanceMod.Log("CLEANUP pending request retired at application quit.");
            plan.Clear();
        }
        internal static void Shutdown()
        {
            try { EndCity(); }
            finally
            {
                Application.lowMemory -= LowMemory; Application.quitting -= Quit;
                AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            }
        }
        internal static void Describe(List<string> output)
        {
            output.Add("cleanup_requested=" + Enabled + "; cleanup_installed=" + installed + "; cleanup_faulted=" + Faulted +
                "; cleanup_pending=" + plan.Pending + "; cleanup_requests=" + requests + "; cleanup_deferred=" + deferred +
                "; cleanup_coalesced=" + coalesced + "; cleanup_executions=" + executions + "; cleanup_last_reason=" + lastReason +
                "; cleanup_request_frame=" + requestFrame + "; cleanup_execution_frame=" + executionFrame +
                "; cleanup_wait_seconds=" + F(lastWait) + "; cleanup_completion_latency_ms=" + F(lastLatency) +
                "; cleanup_maximum_wait_seconds=" + F(plan.DelaySeconds) + "; cleanup_pending_batch=" + plan.RequestsInBatch +
                "; cleanup_pending_deadline_seconds=" + F(plan.DeadlineAt) + "; cleanup_last_batch=" + lastBatch +
                "; cleanup_memory_before_bytes=" + memoryBefore + "; cleanup_memory_after_bytes=" + memoryAfter +
                "; cleanup_observed_memory_drop_bytes=" + MemoryDrop() + "; cleanup_call_ms=" + F(callMs));
        }
        private static string MemoryDrop() => memoryBefore < 0 || memoryAfter < 0 ? "NA" :
            (memoryBefore - memoryAfter).ToString(CultureInfo.InvariantCulture);
        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
