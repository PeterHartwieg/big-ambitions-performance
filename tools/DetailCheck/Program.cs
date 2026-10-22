using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using System.Runtime.Loader;
using HarmonyLib;

// Metadata and managed hooks only: never starts Unity or invokes a game method.
if(args.Length<3 || args.Length>4 || args.Length==4 && args[3]!="--trees-only")
{Console.Error.WriteLine("Usage: DetailCheck <Managed> <CityPerformance.dll> <CameraTools.dll> [--trees-only] OR DetailCheck <Managed> <CityPerformance.dll> --cinematic|--vanilla");return 2;}
string managed=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(context,name)=>
{
    if(new[]{"mscorlib","System","System.Core","netstandard"}.Contains(name.Name))return null;
    string path=Path.Combine(managed,name.Name+".dll");
    return File.Exists(path)?context.LoadFromAssemblyPath(path):null;
};
var mod=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
var game=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,"BigAmbitions.dll"));
if(args[2]=="--cinematic"){CinematicHookChecks.Run(mod,game);return 0;}
if(args[2]=="--vanilla"){VanillaChecks.Run(mod,game);return 0;}
var camera=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
var detail=mod.GetType("CityPerformance.DetailedTrace",true);
var performance=mod.GetType("CityPerformance.PerformanceMod",true);
var kind=detail.GetNestedType("Kind",BindingFlags.NonPublic);
var owner=(Harmony)AccessTools.Field(performance,"Harmony").GetValue(null);
int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
object Call(string name,params object[] values)=>AccessTools.Method(detail,name).Invoke(null,values);
void Install(MethodInfo method,string category)
{
    Call("Install",method,Enum.Parse(kind,category));
    var patch=Harmony.GetPatchInfo(method);
    Check(patch?.Prefixes.Any(p=>p.owner==owner.Id && p.PatchMethod.DeclaringType==detail)==true
        && patch.Finalizers.Any(p=>p.owner==owner.Id && p.PatchMethod.DeclaringType==detail),
        category+" timing hook applied: "+method.DeclaringType.FullName+"."+method.Name);
}
try
{
    TreeHookChecks.Run(mod,game,owner,Check);
    if(args.Length==4){Console.WriteLine($"ALL {checks} TREE CHECKS PASSED (offline; visual/lifecycle behavior requires Unity playtest)");return 0;}
    var targets=(IEnumerable<MethodInfo>)Call("HourlyTargets");
    var methods=targets.Distinct().ToArray();
    var tick=AccessTools.Method(game.GetType("GameManager",true),"RunMainGameTick",new[]{typeof(float)});
    var calls=PatchProcessor.GetOriginalInstructions(tick).Select(i=>i.operand).OfType<MethodInfo>().ToArray();
    Check(methods.Length>=24 && methods.All(calls.Contains),"hourly targets resolve to original tick call sites");
    foreach(var method in methods)Install(method,"Hourly");
    var daily=(IEnumerable<MethodInfo>)Call("DailyTargets");
    var newDay=AccessTools.DeclaredMethod(game.GetType("GameManager",true),"NewDay",Type.EmptyTypes);
    var dailyCalls=PatchProcessor.GetOriginalInstructions(newDay).Select(i=>i.operand).OfType<MethodInfo>().ToArray();
    Check(daily.Count()>=12 && daily.All(dailyCalls.Contains),"daily timings resolve only to audited daily call sites");
    foreach(var method in daily.Distinct())Install(method,"Hourly");
    var cleanup=mod.GetType("CityPerformance.MidnightCleanup",true);
    var rewrite=AccessTools.Method(cleanup,"Rewrite");
    var source=PatchProcessor.GetOriginalInstructions(newDay);
    var changed=((IEnumerable<CodeInstruction>)rewrite.Invoke(null,new object[]{source})).ToArray();
    var differences=Enumerable.Range(0,source.Count).Where(i=>source[i].opcode!=changed[i].opcode || !Equals(source[i].operand,changed[i].operand)).ToArray();
    Check(source.Count==changed.Length && differences.Length==1,"cleanup changes exactly one operand, preserving every daily instruction");
    int callIndex=differences.Single();
    Check(changed[callIndex].Calls(AccessTools.Method(cleanup,"Request")) && changed[callIndex+1].opcode==OpCodes.Pop,"cleanup replacement result is discarded at the exact call site");
    Check(Enumerable.Range(0,source.Count).All(i=>source[i].labels.SequenceEqual(changed[i].labels) && source[i].blocks.SequenceEqual(changed[i].blocks)),"cleanup preserves branch labels and exception blocks");
    Check(!ReferenceEquals(source[callIndex],changed[callIndex]),"rewrite never mutates another transpiler's instructions");
    void Refuses(List<CodeInstruction> candidate,string label)
    {
        try { rewrite.Invoke(null,new object[]{candidate});throw new Exception("unexpected accepted rewrite"); }
        catch(TargetInvocationException e){Check(e.InnerException is InvalidOperationException,label);}
    }
    Refuses(source.Where((x,i)=>i!=callIndex).ToList(),"no cleanup call is rejected");
    var used=source.Select(x=>new CodeInstruction(x)).ToList();used[callIndex+1].opcode=OpCodes.Stloc_0;
    Refuses(used,"a consumed operation return is rejected");
    var duplicate=source.Select(x=>new CodeInstruction(x)).ToList();duplicate.Insert(callIndex,new CodeInstruction(source[callIndex]));
    Refuses(duplicate,"multiple cleanup calls are rejected");
    owner.Patch(newDay,transpiler:new HarmonyMethod(rewrite));
    Check(Harmony.GetPatchInfo(newDay).Transpilers.Any(p=>p.PatchMethod==rewrite),"cleanup transpiler applies beside our daily diagnostic hooks");
    bool Multiplayer(string name)=>(bool)AccessTools.Method(cleanup,"IsMultiplayerAssembly").Invoke(null,new object[]{name});
    Check(Multiplayer("BigAmbitionsMP") && Multiplayer("BigAmbitionsMP.Updated") && Multiplayer("GoingPublic") && !Multiplayer("RealLifeOverhaul"),"actual Going Public assembly name is guarded");
    var foreign=new Harmony("cityperformance.check.foreign");
    try
    {
        foreign.Patch(newDay,prefix:new HarmonyMethod(typeof(Probes),nameof(Probes.Noop)));
        Check((bool)AccessTools.Method(cleanup,"Conflict").Invoke(null,null),"later foreign NewDay patch disables deferral");
    }
    finally{foreign.UnpatchAll(foreign.Id);}
    Check(!(bool)AccessTools.Method(cleanup,"Conflict").Invoke(null,null),"our own timing hooks do not cause a conflict");
    owner.Unpatch(newDay,rewrite);
    AccessTools.Field(performance,"Verified").SetValue(null,false);
    AccessTools.Method(cleanup,"Install").Invoke(null,null);
    Check(!Harmony.GetPatchInfo(newDay).Transpilers.Any(p=>p.PatchMethod==rewrite),"unverified build refuses cleanup installation");
    AccessTools.Field(performance,"Verified").SetValue(null,true);
    try
    {
        foreign.Patch(newDay,prefix:new HarmonyMethod(typeof(Probes),nameof(Probes.Noop)));
        AccessTools.Method(cleanup,"Install").Invoke(null,null);
        Check(!Harmony.GetPatchInfo(newDay).Transpilers.Any(p=>p.PatchMethod==rewrite),"pre-existing foreign patch refuses cleanup installation");
    }
    finally{foreign.UnpatchAll(foreign.Id);}
    AccessTools.Method(cleanup,"Install").Invoke(null,null);
    Check((bool)AccessTools.Field(cleanup,"installed").GetValue(null) && Harmony.GetPatchInfo(newDay).Transpilers.Any(p=>p.PatchMethod==rewrite),"verified initialization installs the audited cleanup hook");
    AccessTools.Method(cleanup,"Shutdown").Invoke(null,null);
    var cameraType=camera.GetType("CameraTools.CameraToolsRuntime",true);
    foreach(string name in new[]{"ResolveVehicleTarget","ResolveActiveCarControllerCandidate","ResolveVehicleControllerCandidate","FindFirstActiveController","TryResolveWorldTextOverlay","ResolveHiddenUiTargets"})
        Install(AccessTools.DeclaredMethod(cameraType,name),"Discovery");
    var action=typeof(Probes).GetMethod(nameof(Probes.Hour));
    var eventMethod=typeof(Probes).GetMethod(nameof(Probes.Event));
    Install(action,"Hourly");Install(eventMethod,"GameEvent");
    var hookMap=(IDictionary)AccessTools.Field(detail,"hooks").GetValue(null);
    bool Retains(MethodInfo method)
    {
        object hook=hookMap[method];return (bool)hook.GetType().GetField("RetainAll").GetValue(hook);
    }
    Check(Retains(methods.First(m=>m.Name=="RunHourly")),"known game hourly helpers retain even fast call records");
    Check(!Retains(action),"subscriber provenance alone cannot flood the normal ring");
    Check(Probes.Hour(7)==8,"disabled diagnostic hook preserves managed return value");
    Probes.Event("unrelated");Check(Probes.Calls==1,"unrelated event still invokes original callback");
    try{Probes.Event("throw");throw new Exception("Original exception lost");}
    catch(InvalidOperationException e){Check(ReferenceEquals(e,Probes.Error),"finalizer preserves original exception identity");}

    var spanType=detail.GetNestedType("Span",BindingFlags.NonPublic);
    object Span(int epoch)
    {
        object span=Activator.CreateInstance(spanType);
        foreach(var p in new Dictionary<string,object>{{"Start",Stopwatch.GetTimestamp()-Stopwatch.Frequency/100},{"Frame",42},{"Day",7},{"Hour",3},{"Id",0},{"Epoch",epoch},{"Retain",true}})
            spanType.GetField(p.Key).SetValue(span,p.Value);
        return span;
    }
    int epoch=(int)AccessTools.Field(detail,"epoch").GetValue(null);
    Check(ReferenceEquals(Call("End",Span(epoch),Probes.Error),Probes.Error),"active span preserves exception identity");
    var samples=(Array)Call("Snapshot");
    Check(samples.Length==1 && (int)samples.GetValue(0).GetType().GetField("Failed").GetValue(samples.GetValue(0))==1,"active span records failure exactly once");
    object dailySpan=Span(epoch);
    spanType.GetField("DayRoot").SetValue(dailySpan,true);
    spanType.GetField("DayBoundary").SetValue(dailySpan,true);
    AccessTools.Field(detail,"dayDepth").SetValue(null,1);
    Call("End",dailySpan,Probes.Error);
    Check((int)AccessTools.Field(detail,"dayDepth").GetValue(null)==0,"throwing daily call unwinds diagnostic context");
    var protectedSamples=(Array)Call("Snapshot");
    Check(protectedSamples.Cast<object>().Any(s=>(int)s.GetType().GetField("DayBoundary").GetValue(s)==1),"daily timing goes into protected context storage");
    object stale=Span(epoch);Call("Reset");Call("End",stale,null);
    Check(((Array)Call("Snapshot")).Length==0,"reset rejects in-flight old-city span");
    var notes=new List<string>();Call("Describe",notes);
    Check(!notes.Any(n=>n.StartsWith("Unavailable timing:")),"no silently skipped timing hooks");
    Console.WriteLine($"ALL {checks} DETAIL CHECKS PASSED (offline; Unity callbacks and counters require playtest)");
    return 0;
}
finally{owner.UnpatchAll(owner.Id);}

public static class Probes
{
    public static int Calls;
    public static void Noop(){}
    public static readonly InvalidOperationException Error=new("original probe error");
    [MethodImpl(MethodImplOptions.NoInlining)]public static int Hour(int value)=>value+1;
    [MethodImpl(MethodImplOptions.NoInlining)]public static void Event(string value){if(value=="throw")throw Error;Calls++;}
}
