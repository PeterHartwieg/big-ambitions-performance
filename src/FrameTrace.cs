using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using GleyTrafficSystem;
using UI.Load;
namespace CityPerformance
{
    internal static class FrameTrace
    {
        internal static bool Recording, AutoExport;
        private static readonly TraceRing ring = new TraceRing(8192);
        private static readonly HitchArchive hitches = new HitchArchive(128);
        private static readonly FrameHistogram focusedFrames = new FrameHistogram();
        private static readonly Dictionary<MethodBase, TraceScope> scopes = new Dictionary<MethodBase, TraceScope>();
        private static readonly List<string> capabilities = new List<string>();
        private static readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        private static int mainThread, exporting;
        private static bool exportRequested, wasRecording, discovered;
        private static int hookCapabilities;
        private static double lastEnd;
        private static bool lastFocused;
        private static string sessionStartedUtc;
        private static ProfilerRecorder gpu, mainCpu, gcAlloc;
        private static ProfilerRecorder gcTime,gcUsed,gcReserved,renderThread,playerLoop,scripts,lateScripts,physics,ui,presentWait;
        private static bool recorderDetails;
        private static int recorderSetupFrame=-1;
        private static int trafficActive=-1, trafficCapacity=-1, trafficSampleFrame=-1;
        private static AccessTools.FieldRef<TrafficManager, TrafficVehicles> trafficField;
        private struct Span { public long Start; public int Frame; public TraceScope Scope; }
        private static bool Active => Thread.CurrentThread.ManagedThreadId==mainThread && Recording && PerformanceMod.Loaded && CityEntry.Ready;
        internal static bool IsActive=>Active && !LoadScene.isLoading;
        internal static void Install()
        {
            capabilities.Clear(); scopes.Clear(); ring.AvailableScopes=0;
            mainThread=Thread.CurrentThread.ManagedThreadId;
            Hook(AccessTools.Method(typeof(PedestrianSpawner),"SetActivePedestrians",new[]{typeof(int)}),TraceScope.Refill);
            var factory=AccessTools.Method(typeof(StationaryAiSpawner),"SpawnGroupOverFrames",Type.EmptyTypes);
            if(PerformanceMod.Verified && factory!=null) Hook(AccessTools.EnumeratorMoveNext(factory),TraceScope.Stationary);
            Hook(AccessTools.Method(typeof(Entities.PedestrianSpawnerForOutsideInteractableItems),"SpawnPedestrians",Type.EmptyTypes),TraceScope.Outside);
            Hook(AccessTools.Method(typeof(Culling.CullingGroupController),"Update",Type.EmptyTypes),TraceScope.Culling);
            Hook(AccessTools.Method(typeof(TrafficManager),"FixedUpdate",Type.EmptyTypes),TraceScope.Traffic);
            Hook(AccessTools.Method(typeof(GameManager),"RunMainGameTick",new[]{typeof(float)}),TraceScope.Tick);
            Hook(AccessTools.Method(typeof(SaveGameManager),"SerializeSaveGame",new[]{typeof(object)}),TraceScope.Save);
            Hook(AccessTools.Method(typeof(Buildings.ClosestBuildingFromPlayer),"TryGet",new[]{typeof(Buildings.Building).MakeByRefType()}),TraceScope.Nearest);
            try
            {
                trafficField=AccessTools.FieldRefAccess<TrafficManager,TrafficVehicles>("trafficVehicles");
                PerformanceMod.Harmony.Patch(AccessTools.Method(typeof(TrafficManager),"FixedUpdate",Type.EmptyTypes),
                    postfix:new HarmonyMethod(typeof(FrameTrace),nameof(TrafficSample)));
            }
            catch(Exception e){PerformanceMod.Log("Trace traffic occupancy unavailable: "+e.Message);}
            hookCapabilities=capabilities.Count;
        }
        private static void Hook(MethodInfo method,TraceScope scope)
        {
            try
            {
                if(method==null) {capabilities.Add(scope+": unavailable method");return;}
                scopes[method]=scope;
                PerformanceMod.Harmony.Patch(method,prefix:new HarmonyMethod(typeof(FrameTrace),nameof(Begin)),finalizer:new HarmonyMethod(typeof(FrameTrace),nameof(End)));
                ring.AvailableScopes |= 1 << (int)scope;
                capabilities.Add(scope+": "+method.DeclaringType.FullName+"."+method.Name);
            }
            catch(Exception e){capabilities.Add(scope+": unavailable "+e.Message);}
        }
        private static void Begin(MethodBase __originalMethod,out Span __state)
        {
            __state=default;
            if(!Active || !scopes.TryGetValue(__originalMethod,out var scope))return;
            __state=new Span{Start=Stopwatch.GetTimestamp(),Frame=Time.frameCount,Scope=scope};
        }
        private static Exception End(Span __state,Exception __exception)
        {
            if(__state.Start!=0)
                ring.AddCost(__state.Frame,__state.Scope,(Stopwatch.GetTimestamp()-__state.Start)*1000.0/Stopwatch.Frequency,__exception!=null);
            return __exception;
        }
        private static void TrafficSample(TrafficManager __instance)
        {
            if(!Active || Time.frameCount-trafficSampleFrame<30)return;
            try
            {
                var storage=trafficField(__instance);
                var vehicles=storage==null?null:storage.GetVehicleList();
                trafficSampleFrame=Time.frameCount;trafficCapacity=vehicles==null?-1:vehicles.Count;trafficActive=-1;
                if(vehicles==null || vehicles.Count>4096)return;
                int n=0;for(int i=0;i<vehicles.Count;i++)if(vehicles[i]!=null && vehicles[i].gameObject.activeInHierarchy)n++;
                trafficActive=n;
            }
            catch {trafficActive=trafficCapacity=-1;}
        }
        internal static void FixedStep(){if(Active)ring.At(Time.frameCount).FixedSteps++;}
        internal static void Hour(){if(Active)ring.At(Time.frameCount).HourEvents++;}
        internal static void Save(){if(Active)ring.At(Time.frameCount).SaveEvents++;}
        internal static void QueueOverflow(){if(Active)ring.At(Time.frameCount).QueueOverflow++;}
        internal static void RequestExport()=>exportRequested=true;
        internal static void Reset()
        {
            ring.Clear();hitches.Clear();focusedFrames.Clear();sessionStartedUtc=null;
            wasRecording=false;lastEnd=0;trafficActive=trafficCapacity=trafficSampleFrame=-1;
            DetailedTrace.Reset();recorderSetupFrame=-1;
            DisposeRecorders();
        }
        private static void DisposeRecorders()
        {
            gpu.Dispose();mainCpu.Dispose();gcAlloc.Dispose();gpu=mainCpu=gcAlloc=default;discovered=false;
            gcTime.Dispose();gcUsed.Dispose();gcReserved.Dispose();renderThread.Dispose();playerLoop.Dispose();scripts.Dispose();lateScripts.Dispose();physics.Dispose();ui.Dispose();presentWait.Dispose();
            gcTime=gcUsed=gcReserved=renderThread=playerLoop=scripts=lateScripts=physics=ui=presentWait=default;
        }
        internal static void FrameEnd()
        {
            if(!Active || LoadScene.isLoading){wasRecording=false;return;}
            if(!discovered)Discover();
            double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
            ref var row=ref ring.At(Time.frameCount);
            bool focused=Application.isFocused;
            row.Focused=focused?1:0;row.MonotonicSeconds=now;
            row.WallMs=wasRecording && focused==lastFocused?(now-lastEnd)*1000:double.NaN;
            lastEnd=now;wasRecording=true;lastFocused=focused;
            if(sessionStartedUtc==null)sessionStartedUtc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
            row.Gc0=GC.CollectionCount(0);row.Gc1=GC.CollectionCount(1);row.Gc2=GC.CollectionCount(2);
            row.Backlog=ZoneScheduling.Backlog;row.OldestSeconds=ZoneScheduling.OldestAge;
            row.TrafficActive=trafficActive;row.TrafficCapacity=trafficCapacity;row.TrafficSampleFrame=trafficSampleFrame;
            try
            {
                row.TextureCurrentBytes=Texture.currentTextureMemory;row.TextureDesiredBytes=Texture.desiredTextureMemory;
                row.TexturePending=Texture.streamingTexturePendingLoadCount;row.TextureLoading=Texture.streamingTextureLoadingCount;
            }
            catch { /* NaN denotes unavailable. */ }
            row.GpuLastNs=Value(gpu);row.MainThreadLastNs=Value(mainCpu);row.GcAllocLastBytes=Value(gcAlloc);
            row.GcLastNs=Value(gcTime);row.GcUsedBytes=Value(gcUsed);row.GcReservedBytes=Value(gcReserved);row.RenderThreadLastNs=Value(renderThread);
            row.PlayerLoopLastNs=Value(playerLoop);row.ScriptsLastNs=Value(scripts);row.LateScriptsLastNs=Value(lateScripts);row.PhysicsLastNs=Value(physics);row.UiLastNs=Value(ui);row.PresentWaitLastNs=Value(presentWait);
            DetailedTrace.Frame(ref row);if(recorderSetupFrame==row.Frame)row.DiagnosticSetup=1;
            Refill.Describe(ref row);
            TreeLods.Describe(ref row);
            row.ZonesRequested=ZoneScheduling.Enabled?1:0;row.ZonesFaulted=ZoneScheduling.Faulted?1:0;
            row.RefillRequested=PerformanceMod.Smoothing?1:0;row.Verified=PerformanceMod.Verified?1:0;
            if(focused)focusedFrames.Add(row.WallMs);
            hitches.Add(row);
        }
        private static double Value(ProfilerRecorder r)=>r.Valid && r.Count>0?r.LastValue:double.NaN;
        private static void Discover()
        {
            discovered=true;
            recorderDetails=DetailedTrace.Enabled;recorderSetupFrame=Time.frameCount;
            if(capabilities.Count>hookCapabilities)capabilities.RemoveRange(hookCapabilities,capabilities.Count-hookCapabilities);
            try
            {
                var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
                foreach(var h in handles)
                {
                    var d=ProfilerRecorderHandle.GetDescription(h);
                    string unit=d.UnitType.ToString();
                    capabilities.Add("recorder: "+d.Name+" ["+unit+"]");
                    if(d.Name=="GPU Frame Time" && unit=="TimeNanoseconds")gpu=StartRecorder(d.Category,d.Name);
                    if(d.Name=="Main Thread" && unit=="TimeNanoseconds")mainCpu=StartRecorder(d.Category,d.Name);
                    if(d.Name=="GC Allocated In Frame" && unit=="Bytes")gcAlloc=StartRecorder(d.Category,d.Name);
                    if(!recorderDetails)continue;
                    if(unit=="Bytes")
                    {
                        if(d.Name=="GC Used Memory")gcUsed=StartRecorder(d.Category,d.Name);
                        if(d.Name=="GC Reserved Memory")gcReserved=StartRecorder(d.Category,d.Name);
                    }
                    if(unit!="TimeNanoseconds")continue;
                    switch(d.Name)
                    {
                        case "GC.Collect":gcTime=StartRecorder(d.Category,d.Name);break;
                        case "CPU Render Thread Frame Time":renderThread=StartRecorder(d.Category,d.Name);break;
                        case "PlayerLoop":playerLoop=StartRecorder(d.Category,d.Name);break;
                        case "Update.ScriptRunBehaviourUpdate":scripts=StartRecorder(d.Category,d.Name);break;
                        case "PreLateUpdate.ScriptRunBehaviourLateUpdate":lateScripts=StartRecorder(d.Category,d.Name);break;
                        case "Physics.Simulate":physics=StartRecorder(d.Category,d.Name);break;
                        case "Canvas.SendWillRenderCanvases":ui=StartRecorder(d.Category,d.Name);break;
                        case "Gfx.WaitForPresentOnGfxThread":presentWait=StartRecorder(d.Category,d.Name);break;
                    }
                }
            }
            catch(Exception e){capabilities.Add("recorder discovery unavailable: "+e.Message);}
            capabilities.Add("active_recorders: GC="+gcTime.Valid+"; heapUsed="+gcUsed.Valid+"; heapReserved="+gcReserved.Valid+"; renderThread="+renderThread.Valid+"; playerLoop="+playerLoop.Valid+"; scripts="+scripts.Valid+"; lateScripts="+lateScripts.Valid+"; physics="+physics.Valid+"; ui="+ui.Valid+"; presentWait="+presentWait.Valid);
        }
        private static ProfilerRecorder StartRecorder(ProfilerCategory category,string name)
        {
            try{return ProfilerRecorder.StartNew(category,name,1);}
            catch(Exception e){capabilities.Add("recorder start failed: "+name+": "+e.Message);return default;}
        }
        internal static void Tick()
        {
            if(!Recording){wasRecording=false;if(discovered)DisposeRecorders();}
            if(discovered && recorderDetails!=DetailedTrace.Enabled)DisposeRecorders();
            DetailedTrace.Tick();
            while(messages.TryDequeue(out var message))PerformanceMod.Log(message);
            if(!exportRequested)return;exportRequested=false;
            if(Interlocked.CompareExchange(ref exporting,1,0)!=0){PerformanceMod.Log("Trace export already running.");return;}
            try
            {
                var report=Capture("manual");
                Task.Run(()=>
                {
                    try
                    {
                        Write(report);messages.Enqueue("Trace exported: "+report.Path+".csv");
                    }
                    catch(Exception e){messages.Enqueue("Trace export failed: "+e.Message);}
                    finally{Interlocked.Exchange(ref exporting,0);}
                });
            }
            catch(Exception e){Interlocked.Exchange(ref exporting,0);PerformanceMod.Log("Trace snapshot failed: "+e.Message);}
        }
        internal static void OnApplicationQuit()=>FinishSession("application-quit");
        internal static void FinishSession(string reason)
        {
            if(!AutoExport || ring.Count==0)return;
            try
            {
                // Lifecycle boundary only. Finish the write before normal quit can terminate background tasks.
                // A manual export may still be finishing; each report has independent data and a unique path.
                var report=Capture(reason);Write(report);
                PerformanceMod.Log("Trace automatically saved: "+report.Path+".csv; worst focused hitches: "+report.Hitches.Length);
                ring.Clear();hitches.Clear();focusedFrames.Clear();sessionStartedUtc=null;
            }
            catch(Exception e){PerformanceMod.Log("Automatic trace export failed: "+e.Message);}
        }
        private sealed class Report
        {
            public string Path;
            public TraceRow[] Frames,Hitches;
            public string[] Metadata;
            public DetailSample[] Methods;
        }
        private static Report Capture(string reason)
        {
            // All Unity access and array copies stay on the main thread, outside automatic per-frame capture.
            var metadata=new List<string>(capabilities);
            metadata.Add("version=0.4.2; export_reason="+reason+"; session_started_utc="+sessionStartedUtc);
            BackgroundSaves.Describe(metadata);
            DetailedTrace.Describe(metadata);
            MidnightCleanup.Describe(metadata);
            TreeLods.Describe(metadata);
            CinematicWorld.Describe(metadata);
            metadata.Add("utc="+DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
            metadata.Add("verified_build="+PerformanceMod.Verified+"; zones_requested="+ZoneScheduling.Enabled+"; zones_faulted="+ZoneScheduling.Faulted+"; refill_requested="+PerformanceMod.Smoothing);
            metadata.Add("focused_frames="+focusedFrames.Count+"; mean_ms="+(focusedFrames.Count==0?"NA":(focusedFrames.Sum/focusedFrames.Count).ToString("R",CultureInfo.InvariantCulture))+"; p95_le_ms="+focusedFrames.Percentile(.95)+"; p99_le_ms="+focusedFrames.Percentile(.99)+"; max_ms="+focusedFrames.Max.ToString("R",CultureInfo.InvariantCulture)+"; over50="+focusedFrames.Over50);
            metadata.Add("Hitch file: slowest 128 focused frames over 50ms across this session; selected outliers, not a representative frame distribution. Recorder samples can be delayed. Scope costs are inclusive.");
            try
            {
                metadata.Add("unity="+Application.unityVersion+"; game="+Application.version+"; os="+SystemInfo.operatingSystem);
                metadata.Add("cpu="+SystemInfo.processorType+"; gpu="+SystemInfo.graphicsDeviceName+"; api="+SystemInfo.graphicsDeviceType+"; gpu_memory_mb="+SystemInfo.graphicsMemorySize);
                metadata.Add("screen="+Screen.width+"x"+Screen.height+"; quality="+QualitySettings.GetQualityLevel()+"; vsync="+QualitySettings.vSyncCount+"; target_fps="+Application.targetFrameRate);
                metadata.Add("gc_incremental="+UnityEngine.Scripting.GarbageCollector.isIncremental+"; gc_mode="+UnityEngine.Scripting.GarbageCollector.GCMode);
                foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())metadata.Add("assembly="+assembly.GetName().FullName);
            }
            catch(Exception e){metadata.Add("runtime_metadata_unavailable="+e.Message);}
            string folder=Path.Combine(Application.persistentDataPath,"CityPerformanceTraces");
            string name="trace-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
            return new Report{Path=Path.Combine(folder,name),Frames=ring.Snapshot(),Hitches=hitches.Snapshot(),Metadata=metadata.ToArray(),Methods=DetailedTrace.Snapshot()};
        }
        private static void Write(Report report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report.Path));
            using(var writer=new StreamWriter(report.Path+".csv",false))TraceCsv.Write(writer,report.Frames);
            using(var writer=new StreamWriter(report.Path+"-hitches.csv",false))TraceCsv.Write(writer,report.Hitches);
            using(var writer=new StreamWriter(report.Path+"-methods.csv",false))DetailCsv.Write(writer,report.Methods);
            File.WriteAllLines(report.Path+"-capabilities.txt",report.Metadata);
        }
    }
}
