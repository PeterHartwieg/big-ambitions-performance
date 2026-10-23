using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UI.Load;
namespace CityPerformance
{
    internal static class ZoneScheduling
    {
        internal static bool Enabled = true, Faulted;
        private static readonly StepBudget budget = new StepBudget(128, 4, 2.0);
        private static MethodInfo factory, clear;
        private static MethodInfo[] guards;
        private static readonly Func<object,bool> validOwner=OwnerValid;
        private static AccessTools.FieldRef<AiSpawnerZone, bool> visible;
        private static int generation;
        internal static int Backlog => budget.Count;
        internal static double OldestAge => budget.OldestAge(Time.realtimeSinceStartupAsDouble);
        internal static void Install()
        {
            try
            {
                if (!PerformanceMod.Verified) return;
                conflictFrame=-1;
                factory = AccessTools.Method(typeof(StationaryAiSpawner), "SpawnGroupOverFrames", Type.EmptyTypes);
                clear = AccessTools.Method(typeof(StationaryAiSpawner), "ClearPedestrians", Type.EmptyTypes);
                guards=new[] {factory,clear, factory==null?null:AccessTools.EnumeratorMoveNext(factory),
                    AccessTools.Method(typeof(StationaryAiSpawner),"Spawn",new[]{typeof(Vector3)}),
                    AccessTools.Method(typeof(StationaryAiSpawner),"SpawnHomeless",new[]{typeof(Vector3)}),
                    AccessTools.Method(typeof(StationaryAiSpawner),"SpawnStreetPerformer",new[]{typeof(Vector3)})};
                if (factory == null || factory.ReturnType != typeof(IEnumerator) || clear == null || clear.ReturnType != typeof(void) || Conflict())
                { PerformanceMod.Log("Not active: stationary iterator scheduling (signature/conflict)."); return; }
                visible = AccessTools.FieldRefAccess<AiSpawnerZone, bool>("isVisible");
                PerformanceMod.Harmony.Patch(factory, postfix: new HarmonyMethod(typeof(ZoneScheduling), nameof(Wrap)));
                PerformanceMod.Harmony.Patch(clear, prefix: new HarmonyMethod(typeof(ZoneScheduling), nameof(Clear)));
                PerformanceMod.Log("Active: stationary iterator hook; shared 2ms/4-step budget, option defaults on.");
            }
            catch (Exception e) { Faulted = true; PerformanceMod.Log("Not active: stationary scheduling: " + e); }
        }
        private static int conflictFrame=-1;
        private static bool conflictResult;
        private static bool Foreign(MethodInfo method)
        {
            if(method==null)return true;
            var info=Harmony.GetPatchInfo(method);
            if(info==null)return false;
            foreach(var owner in info.Owners)if(owner!=PerformanceMod.Id)return true;
            return false;
        }
        private static bool Conflict()
        {
            int frame=Time.frameCount;
            if(frame!=conflictFrame)
            {
                conflictFrame=frame;conflictResult=false;
                if(guards==null)conflictResult=true;
                else foreach(var method in guards)if(Foreign(method)){conflictResult=true;break;}
            }
            return conflictResult;
        }
        private static void Wrap(StationaryAiSpawner __instance, ref IEnumerator __result)
        {
            if (!Enabled || Faulted || !CityEntry.Ready || __result == null) return;
            try
            {
                if (Conflict()) { Faulted = true; return; }
                var city = InstanceBehavior<CityManager>.Instance;
                if (city == null) return;
                var ticket = budget.Register(__instance, Time.realtimeSinceStartupAsDouble);
                if (ticket == null) { FrameTrace.QueueOverflow(); return; } // Bounded memory, vanilla fallback.
                __result = new GovernedIterator(__instance, city, __result, ticket, generation);
            }
            catch (Exception e) { Faulted = true; PerformanceMod.Log("Stationary wrapper disabled: " + e); }
        }
        private static void Clear(StationaryAiSpawner __instance) => budget.CancelOwner(__instance);
        private static bool OwnerValid(object value)
        {
            var owner=value as StationaryAiSpawner;
            return owner!=null && owner.isActiveAndEnabled && !owner.disabled && visible(owner);
        }
        internal static void Tick()
        {
            if(budget.Count==0)return;
            if(LoadScene.isLoading || CityMap.IsOpen){CancelAll();return;}
            budget.RemoveInvalid(validOwner);
        }
        internal static void CancelAll() { generation++; budget.Clear(); }
        private sealed class GovernedIterator : IEnumerator, IDisposable
        {
            private readonly StationaryAiSpawner owner;
            private readonly CityManager city;
            private readonly IEnumerator original;
            private readonly StepBudget.Ticket ticket;
            private readonly int epoch;
            private bool completed, released;
            private object current;
            public GovernedIterator(StationaryAiSpawner owner, CityManager city, IEnumerator original, StepBudget.Ticket ticket, int epoch)
            { this.owner=owner; this.city=city; this.original=original; this.ticket=ticket; this.epoch=epoch; }
            public object Current => current;
            public bool MoveNext()
            {
                if (completed) return false;
                // Keep pool release exclusively in the game's ClearPedestrians. Never release acquired characters here.
                if (epoch != generation || owner == null || city == null || InstanceBehavior<CityManager>.Instance != city ||
                    LoadScene.isLoading || !owner.isActiveAndEnabled || owner.disabled || !visible(owner) || CityMap.IsOpen)
                { Dispose(); return false; }
                if (!released && !ticket.Active) { Dispose(); return false; } // replacement or ClearPedestrians
                bool governed = Enabled && !Faulted && PerformanceMod.Loaded && CityEntry.Ready;
                if (!governed && !released) { budget.Cancel(ticket); released=true; }
                if (governed && !released)
                {
                    // Check new patches before continuing an owned operation.
                    if (Conflict()) { Faulted=true; budget.Cancel(ticket); released=true; }
                    else if (!budget.TryBegin(ticket, Time.frameCount)) { current=null; return true; }
                }
                long started = Stopwatch.GetTimestamp();
                try
                {
                    bool more = original.MoveNext();
                    current = more ? original.Current : null;
                    if (!more) Dispose();
                    return more;
                }
                catch { try { Dispose(); } catch { } throw; } // Preserve the exception reaching Unity's coroutine runner.
                finally
                {
                    if (governed && !released) budget.End((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency);
                }
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose()
            {
                if (completed) return;
                completed=true; current=null; budget.Cancel(ticket);
                (original as IDisposable)?.Dispose();
            }
        }
    }
}
