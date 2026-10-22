using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
namespace CityPerformance
{
    // Timers only: no method bodies, delegates, game state or callback order are replaced.
    internal static class DetailedTrace
    {
        internal static bool Enabled, Faulted;
        internal static int SetupFrame=-1;
        private const int Limit=192;
        private static readonly DetailStore store=new DetailStore(4096,Limit,2048);
        private enum Kind { Hourly, GameEvent, Discovery }
        private sealed class Hook { public int Id; public string Name; public Kind Kind; public bool RetainAll; }
        private static readonly Dictionary<MethodBase,Hook> hooks=new Dictionary<MethodBase,Hook>();
        private static readonly List<Hook> ordered=new List<Hook>();
        private static readonly List<string> notes=new List<string>();
        private static int epoch;
        private static int dayDepth;
        private static readonly MethodInfo newDay=AccessTools.DeclaredMethod(typeof(GameManager),"NewDay",Type.EmptyTypes);
        private static bool pendingSetup, callbacksAttached;
        private struct Span { public long Start; public int Frame,Day,Hour,Id,Epoch; public bool Retain,DayBoundary,DayRoot; }
        private struct CameraSlot { public int Id,StartFrame; public string Name; public long Start,Calls; public double TotalMs,MaxMs; }
        private static readonly CameraSlot[] cameras=new CameraSlot[16];
        private static int cameraCount, cameraOverflow, cameraFrame=-1, renders, biggestCamera;
        private static double cameraMs, biggestCameraMs;
        private static bool Active=>Enabled && !Faulted && FrameTrace.IsActive;
        internal static void Initialize()
        {
            hooks.Clear();ordered.Clear();notes.Clear();Faulted=false;
            Reset();
            if(callbacksAttached)return;
            RenderPipelineManager.beginCameraRendering+=CameraBegin;
            RenderPipelineManager.endCameraRendering+=CameraEnd;
            callbacksAttached=true;
        }
        internal static void StartCity(){pendingSetup=true;}
        internal static void SetEnabled(bool value)
        {
            Enabled=value;if(value){Faulted=false;pendingSetup=true;}
            PerformanceMod.Log("Detailed diagnostics requested="+value+". New hooks are discovered outside hourly callbacks; setup frames are marked in the trace.");
        }
        internal static void Tick()
        {
            if(!pendingSetup || !Active)return;
            pendingSetup=false;SetupFrame=Time.frameCount;
            try
            {
                if(!PerformanceMod.Verified){notes.Add("Hourly/discovery hooks skipped: unreviewed game binary.");return;}
                foreach(var method in HourlyTargets())Install(method,Kind.Hourly);
                foreach(var method in DailyTargets())Install(method,Kind.Hourly);
                Subscribers(GlobalEvents.onNewHour,Kind.Hourly);
                Subscribers(GlobalEvents.onNewDay,Kind.Hourly);
                Subscribers(GameEvent.onGameEventTriggered,Kind.GameEvent);
                // Read only the loaded mod. Never force-load an absent Workshop assembly.
                var cameraTools=AccessTools.TypeByName("CameraTools.CameraToolsRuntime");
                if(cameraTools!=null)
                    foreach(string name in new[]{"ResolveVehicleTarget","ResolveActiveCarControllerCandidate","ResolveVehicleControllerCandidate","FindFirstActiveController","TryResolveWorldTextOverlay","ResolveHiddenUiTargets"})
                    {
                        var method=AccessTools.DeclaredMethod(cameraTools,name);
                        if(method!=null)Install(method,Kind.Discovery);
                    }
                var catering=AccessTools.TypeByName("FoodTrucks.Catering");
                if(catering!=null)
                    foreach(string name in new[]{"OnNewDay","Offer","Places","Caterers","CategoryUnits"})
                    {
                        var method=AccessTools.DeclaredMethod(catering,name);
                        if(method!=null)Install(method,Kind.Discovery);
                    }
                PerformanceMod.Log("Detailed diagnostics: "+hooks.Count+" timing-only hooks; hourly/event subscribers captured at setup. Later registrations require toggling detailed diagnostics off/on. Setup frame="+SetupFrame+".");
            }
            catch(Exception e){Fail("setup",e);}
        }
        internal static List<MethodInfo> HourlyTargets()
        {
            var result=new List<MethodInfo>();
            var tick=AccessTools.Method(typeof(GameManager),"RunMainGameTick",new[]{typeof(float)});
            foreach(var instruction in Instructions(tick))
            {
                var method=instruction.operand as MethodInfo;
                if(method==null)continue;
                if(method.Name=="RunHourly" || method.Name=="NewDay" || method.Name=="UpdateAllSecurityLevels"
                    || method.Name=="HandleWholesaleDeliveries" || method.Name=="UpdateDynamicComponents"
                    || method.Name=="GenerateShortagesAndBackorders" || method.Name=="RunMidNightAutoSave"
                    || method.Name.StartsWith("DoAll",StringComparison.Ordinal) && method.Name.EndsWith("Deliveries",StringComparison.Ordinal))result.Add(method);
            }
            return result;
        }
        internal static List<MethodInfo> DailyTargets()
        {
            var result=new List<MethodInfo>();
            foreach(var instruction in Instructions(newDay))
                if(instruction.operand is MethodInfo method && method.DeclaringType!=null &&
                    (method.Name=="RunDaily" || method.Name=="PayDailyWages" || method.Name=="WorkDaily" ||
                     method.Name=="RecordMidnightBankBalance" || method.DeclaringType.Name=="EnsureAvailableCinemaTheater"))result.Add(method);
            return result;
        }
        internal static IEnumerable<CodeInstruction> Instructions(MethodInfo target)
        {
            // The pinned Unity/Mono Harmony build exposes ILGenerator from mscorlib.
            // Resolve that overload once here so our netstandard assembly needs no
            // compile-time reference to that framework-specific parameter type.
            MethodInfo reader=null;
            foreach(var candidate in typeof(PatchProcessor).GetMethods(BindingFlags.Public|BindingFlags.Static))
            {
                var parameters=candidate.GetParameters();
                if(candidate.Name=="GetOriginalInstructions" && parameters.Length==2 && !parameters[1].ParameterType.IsByRef){reader=candidate;break;}
            }
            if(reader==null)throw new MissingMethodException("Harmony original-instruction reader unavailable");
            return (IEnumerable<CodeInstruction>)reader.Invoke(null,new object[]{target,null});
        }
        private static void Subscribers(Delegate callbacks,Kind kind)
        {
            if(callbacks==null)return;
            foreach(var callback in callbacks.GetInvocationList())
            {
                var method=callback.Method;
                if(method.DeclaringType==null || method.DeclaringType.Assembly==typeof(DetailedTrace).Assembly)continue;
                Install(method,kind);
            }
        }
        private static void Install(MethodInfo method,Kind kind)
        {
            if(hooks.ContainsKey(method))return;
            if(hooks.Count>=Limit){notes.Add("Hook capacity reached: "+method.DeclaringType.FullName+"."+method.Name);return;}
            try
            {
                if(method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody()==null)return;
                if(kind==Kind.GameEvent && (method.GetParameters().Length!=1 || method.GetParameters()[0].ParameterType!=typeof(string)))return;
                var hook=new Hook{Id=ordered.Count,Name=method.DeclaringType.Assembly.GetName().Name+":"+method.DeclaringType.FullName+"."+method.Name,Kind=kind,
                    RetainAll=kind==Kind.GameEvent || method.DeclaringType.Assembly==typeof(GameManager).Assembly && method.Name=="RunHourly"};
                hooks.Add(method,hook);ordered.Add(hook);
                try
                {
                    PerformanceMod.Harmony.Patch(method,prefix:new HarmonyMethod(typeof(DetailedTrace),kind==Kind.GameEvent?nameof(BeginEvent):nameof(Begin)),
                        finalizer:new HarmonyMethod(typeof(DetailedTrace),nameof(End)));
                }
                catch
                {
                    // Remove only our two diagnostic functions, never another City Performance hook.
                    PerformanceMod.Harmony.Unpatch(method,AccessTools.Method(typeof(DetailedTrace),kind==Kind.GameEvent?nameof(BeginEvent):nameof(Begin)));
                    PerformanceMod.Harmony.Unpatch(method,AccessTools.Method(typeof(DetailedTrace),nameof(End)));
                    hooks.Remove(method);ordered.RemoveAt(ordered.Count-1);throw;
                }
            }
            catch(Exception e){notes.Add("Unavailable timing: "+method.DeclaringType.FullName+"."+method.Name+": "+e.Message);}
        }
        private static void BeginEvent(MethodBase __originalMethod,string __0,out Span __state)
        {
            __state=default;
            if(__0=="ba:gameevent_newhour" || __0=="ba:gameevent_newday")Begin(__originalMethod,out __state);
        }
        private static void Begin(MethodBase __originalMethod,out Span __state)
        {
            __state=default;
            if(!Active)return;
            try
            {
                if(!hooks.TryGetValue(__originalMethod,out var hook))return;
                var game=SaveGameManager.Current;
                bool root=__originalMethod==newDay;
                __state=new Span{Start=Stopwatch.GetTimestamp(),Frame=Time.frameCount,Day=game==null?-1:game.Day,Hour=game==null?-1:game.Hour,
                    Id=hook.Id,Epoch=epoch,Retain=hook.RetainAll,DayRoot=root,DayBoundary=root || dayDepth>0};
                if(root)dayDepth++;
            }
            catch(Exception e){Fail("begin",e);}
        }
        private static Exception End(Span __state,Exception __exception)
        {
            if(__state.Start==0 || __state.Epoch!=epoch)return __exception;
            if(__state.DayRoot)dayDepth=Math.Max(0,dayDepth-1);
            try
            {
                double ms=(Stopwatch.GetTimestamp()-__state.Start)*1000.0/Stopwatch.Frequency;
                store.Add(new DetailSample{Method=__state.Id,Frame=__state.Frame,Day=__state.Day,Hour=__state.Hour,
                    StartSeconds=__state.Start/(double)Stopwatch.Frequency,Ms=ms,Failed=__exception==null?0:1,DayBoundary=__state.DayBoundary?1:0},__state.Retain);
            }
            catch(Exception e){Fail("end",e);}
            return __exception;
        }
        private static void Fail(string where,Exception e)
        {
            if(Faulted)return;Faulted=true;PerformanceMod.Log("Detailed diagnostics stopped at "+where+"; original game callbacks continue: "+e.Message);
        }
        private static int CameraIndex(Camera camera)
        {
            int id=camera.GetInstanceID();
            for(int i=0;i<cameraCount;i++)if(cameras[i].Id==id)return i;
            if(cameraCount==cameras.Length){cameraOverflow++;return -1;}
            int index=cameraCount++;cameras[index]=new CameraSlot{Id=id,Name=camera.name};return index;
        }
        private static void CameraBegin(ScriptableRenderContext context,Camera camera)
        {
            if(!Active || camera==null)return;
            try
            {
                int i=CameraIndex(camera);if(i<0)return;
                cameras[i].Start=Stopwatch.GetTimestamp();cameras[i].StartFrame=Time.frameCount;
            }
            catch(Exception e){Fail("camera begin",e);}
        }
        private static void CameraEnd(ScriptableRenderContext context,Camera camera)
        {
            if(!Active || camera==null)return;
            try
            {
                int id=camera.GetInstanceID(),i=-1;for(int n=0;n<cameraCount;n++)if(cameras[n].Id==id){i=n;break;}
                if(i<0 || cameras[i].Start==0 || cameras[i].StartFrame!=Time.frameCount)return;
                double ms=(Stopwatch.GetTimestamp()-cameras[i].Start)*1000.0/Stopwatch.Frequency;cameras[i].Start=0;
                cameras[i].Calls++;cameras[i].TotalMs+=ms;cameras[i].MaxMs=Math.Max(cameras[i].MaxMs,ms);
                if(cameraFrame!=Time.frameCount){cameraFrame=Time.frameCount;renders=0;cameraMs=biggestCameraMs=0;biggestCamera=0;}
                renders++;cameraMs+=ms;if(ms>biggestCameraMs){biggestCameraMs=ms;biggestCamera=id;}
            }
            catch(Exception e){Fail("camera end",e);}
        }
        internal static void Frame(ref TraceRow row)
        {
            row.DetailRequested=Enabled?1:0;row.DetailFaulted=Faulted?1:0;row.DiagnosticSetup=SetupFrame==row.Frame?1:0;
            if(!Active)return;
            row.CameraRenderCount=cameraFrame==row.Frame?renders:0;row.CameraCpuMs=cameraFrame==row.Frame?cameraMs:0;
            row.BiggestCameraId=cameraFrame==row.Frame?biggestCamera:0;row.BiggestCameraCpuMs=cameraFrame==row.Frame?biggestCameraMs:0;
        }
        internal static DetailSample[] Snapshot()=>store.Snapshot();
        internal static void Describe(List<string> output)
        {
            output.Add("details_requested="+Enabled+"; details_faulted="+Faulted+"; detail_samples_overwritten="+store.Overwritten+"; daily_samples_retained="+store.DailyCount+"; daily_samples_overwritten="+store.DailyOverwritten+"; camera_capacity_overflows="+cameraOverflow);
            output.Add("Method retention: 4096 recent slow (>=1ms), failed, game RunHourly or selected game-event calls; 2048 separate calls inside GameManager.NewDay. day_boundary=1 marks that context. All calls contribute aggregate counters; Hourly is hook provenance, not proof of hourly-only invocation.");
            output.Add("Method samples are inclusive CPU wall time, keyed by frame/start clock. Hourly methods always retained; discovery calls >=1ms or exceptions retained. Counters include smaller calls. Same callback method on multiple instances is aggregated. Late subscribers require an off/on refresh. Camera times are CPU pipeline callback spans, NOT GPU time; nested render callbacks can overlap.");
            output.AddRange(notes);
            foreach(var hook in ordered)
            {
                var c=store.Counter(hook.Id);
                output.Add("method_id="+hook.Id+"; kind="+hook.Kind+"; name="+hook.Name+"; calls="+c.Calls+"; inclusive_ms="+F(c.TotalMs)+"; max_ms="+F(c.MaxMs)+"; exceptions="+c.Errors);
            }
            for(int i=0;i<cameraCount;i++)
            {
                var c=cameras[i];output.Add("camera_id="+c.Id+"; name="+c.Name+"; renders="+c.Calls+"; cpu_inclusive_ms="+F(c.TotalMs)+"; cpu_max_ms="+F(c.MaxMs));
            }
        }
        private static string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        internal static void Report()
        {
            if(!Active)return;
            Hook biggest=null;double max=0;
            foreach(var hook in ordered){var c=store.Counter(hook.Id);if(c.MaxMs>max){max=c.MaxMs;biggest=hook;}}
            PerformanceMod.Log("DETAIL hooks="+hooks.Count+"; retained="+store.Count+"; overwritten="+store.Overwritten+"; daily_retained="+store.DailyCount+"; daily_overwritten="+store.DailyOverwritten+"; session_worst_method="+(biggest==null?"none":biggest.Name)+"; max_ms="+F(max)+"; full breakdown in exported methods CSV and capabilities.");
        }
        internal static void Reset()
        {
            epoch++;dayDepth=0;store.Clear();Array.Clear(cameras,0,cameras.Length);cameraCount=cameraOverflow=0;cameraFrame=-1;
            cameraMs=biggestCameraMs=0;renders=biggestCamera=0;SetupFrame=-1;pendingSetup=false;
        }
        internal static void Shutdown()
        {
            if(callbacksAttached){RenderPipelineManager.beginCameraRendering-=CameraBegin;RenderPipelineManager.endCameraRendering-=CameraEnd;callbacksAttached=false;}
            Reset();
        }
    }
}
