using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

static class CinematicHookChecks
{
    internal static void Run(Assembly mod, Assembly game)
    {
        int checks=0;
        void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);checks++;Console.WriteLine("PASS "+label);}
        var perf=mod.GetType("CityPerformance.PerformanceMod",true);
        var logger=AccessTools.Field(perf,"Logger");
        logger.SetValue(null,DispatchProxy.Create(logger.FieldType,typeof(CinematicLogProxy)));
        var harmony=(Harmony)AccessTools.Field(perf,"Harmony").GetValue(null);
        var verified=AccessTools.Field(perf,"Verified");
        Type Feature(string name)=>mod.GetType("CityPerformance."+name,true);
        void Install(string name)=>AccessTools.Method(Feature(name),"Install").Invoke(null,null);
        bool Patched(MethodBase target,Type feature)=>Harmony.GetPatchInfo(target)?.Owners.Contains(harmony.Id)==true &&
            Harmony.GetPatchInfo(target).Prefixes.Concat(Harmony.GetPatchInfo(target).Postfixes).Concat(Harmony.GetPatchInfo(target).Finalizers).Any(p=>p.PatchMethod.DeclaringType==feature);
        var culling=game.GetType("Culling.CullingGroupController",true);
        var dispatch=AccessTools.DeclaredMethod(culling,"DispatchLod");
        var pedestrian=game.GetType("Streets.Pedestrians.Pedestrian",true);
        var goal=AccessTools.DeclaredMethod(pedestrian,"GetTarget");
        var initial=AccessTools.DeclaredMethod(pedestrian,"GetInitialTarget");
        Check(goal.IsStatic && !initial.IsStatic,"audited goal is static and initial spawn is an instance call");
        Check(!AccessTools.Method(Feature("CinematicPopulation"),"Goal").GetParameters().Any(p=>p.Name=="__instance"),"static goal hook never requests an instance");
        try
        {
            verified.SetValue(null,false);
            foreach(string f in new[]{"CinematicWorld","CinematicAudioZones","CinematicPopulation","CinematicDecals"})Install(f);
            Check(!Patched(dispatch,Feature("CinematicWorld")) && !Patched(goal,Feature("CinematicPopulation")),"unverified build cannot install behavioral camera hooks");
            verified.SetValue(null,true);
            var foreign=new Harmony("cityperformance.check.cinematic.foreign");
            try
            {
                foreign.Patch(dispatch,prefix:new HarmonyMethod(typeof(Probes),nameof(Probes.Noop)));
                Install("CinematicWorld");
                Check(!Patched(dispatch,Feature("CinematicWorld")),"foreign culling patch refuses world installation");
            }
            finally{foreign.UnpatchAll(foreign.Id);}
            foreach(string f in new[]{"CinematicWorld","CinematicAudioZones","CinematicPopulation","CinematicDecals"})Install(f);
            foreach(var pair in new[]{("CinematicWorld",dispatch),("CinematicPopulation",goal),("CinematicPopulation",initial),
                ("CinematicPopulation",AccessTools.DeclaredMethod(pedestrian,"OnSpawn",Type.EmptyTypes)),
                ("CinematicPopulation",AccessTools.DeclaredMethod(pedestrian,"ChangeState")),
                ("CinematicWorld",AccessTools.DeclaredMethod(game.GetType("AudioListenerPositioner",true),"LateUpdate")),
                ("CinematicWorld",AccessTools.DeclaredMethod(game.GetType("AudioListenerPositioner",true),"GetAudioListenerPosition")),
                ("CinematicAudioZones",AccessTools.DeclaredMethod(game.GetType("Player.Sound.AudioZone",true),"Awake")),
                ("CinematicAudioZones",AccessTools.DeclaredMethod(game.GetType("Player.Sound.AudioZone",true),"OnTriggerEnter"))})
                Check(Patched(pair.Item2,Feature(pair.Item1)),"actual Harmony binding: "+pair.Item1+" / "+pair.Item2.Name);
            var external=Assembly.Load("ExternalPlugins");
            foreach(var pair in new[]{("GleyTrafficSystem.TrafficManager","UpdateCamera"),("GleyTrafficSystem.DensityManager","IsPositionOnScreen"),("GleyTrafficSystem.DensityManager","UpdateVehicleDensity")})
                Check(Patched(AccessTools.DeclaredMethod(external.GetType(pair.Item1,true),pair.Item2),Feature("CinematicTraffic")),"traffic hook and audited field/delegate resolution: "+pair.Item2);
            var hdrp=Assembly.Load("Unity.RenderPipelines.HighDefinition.Runtime");
            var decalDistance=AccessTools.PropertyGetter(hdrp.GetType("UnityEngine.Rendering.HighDefinition.DecalSystem",true),"DrawDistance");
            Check(Patched(decalDistance,Feature("CinematicDecals")),"exact Windows HDRP decal hook binds");
            var pop=Feature("CinematicPopulation");
            var a=RuntimeHelpers.GetUninitializedObject(pedestrian);var b=RuntimeHelpers.GetUninitializedObject(pedestrian);
            object[] argsA={a,null},argsB={b,null};
            var enter=AccessTools.Method(pop,"EnterScope");var exit=AccessTools.Method(pop,"ExitScope");var current=AccessTools.Field(pop,"current");
            enter.Invoke(null,argsA);enter.Invoke(null,argsB);
            Check(ReferenceEquals(current.GetValue(null),b) && ReferenceEquals(argsB[1],a),"nested pedestrian callbacks retain the outer owner");
            var error=new InvalidOperationException("original");
            Check(ReferenceEquals(exit.Invoke(null,new[]{argsB[1],error}),error) && ReferenceEquals(current.GetValue(null),a),"scope finalizer restores owner without swallowing native errors");
            exit.Invoke(null,new[]{argsA[1],null});Check(current.GetValue(null)==null,"scope clears after outer callback");
            var audioArgs=new object[]{Activator.CreateInstance(AccessTools.Method(Feature("CinematicWorld"),"AudioPosition").GetParameters()[0].ParameterType.GetElementType())};
            Check((bool)AccessTools.Method(Feature("CinematicWorld"),"AudioPosition").Invoke(null,audioArgs),"audio hook is inert outside a city");
            Check(!Harmony.GetPatchInfo(goal).Transpilers.Any(p=>p.owner==harmony.Id),"goal hook does not rewrite the native body");
            try
            {
                foreach(var pair in new[]{("CinematicWorld",dispatch),("CinematicPopulation",goal),
                    ("CinematicTraffic",AccessTools.DeclaredMethod(external.GetType("GleyTrafficSystem.TrafficManager",true),"FixedUpdate")),
                    ("CinematicAudioZones",AccessTools.DeclaredMethod(game.GetType("Player.Sound.AudioZone",true),"OnTriggerEnter"))})
                {
                    foreign.Patch(pair.Item2,prefix:new HarmonyMethod(typeof(Probes),nameof(Probes.Noop)));
                    Check((bool)AccessTools.Method(Feature(pair.Item1),"Conflict").Invoke(null,null),"later foreign patch detected: "+pair.Item1);
                }
                AccessTools.Method(Feature("CinematicWorld"),"UnpatchFeature").Invoke(null,new object[]{Feature("CinematicWorld")});
                Check(!Patched(dispatch,Feature("CinematicWorld")) && Harmony.GetPatchInfo(dispatch).Owners.Contains(foreign.Id),"partial-install rollback removes only this feature's hooks");
                Check(Patched(goal,Feature("CinematicPopulation")) && Patched(decalDistance,Feature("CinematicDecals")),"feature rollback preserves other City Performance hooks");
            }
            finally{foreign.UnpatchAll(foreign.Id);}
            Console.WriteLine("REFERENCES "+string.Join(",",mod.GetReferencedAssemblies().Select(a=>a.Name)));
            Console.WriteLine($"ALL {checks} CINEMATIC CHECKS PASSED (offline; Unity playtest still required)");
        }
        finally{harmony.UnpatchAll(harmony.Id);}
    }
}

public class CinematicLogProxy : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
    {
        Console.WriteLine("MOD " + string.Join(" ", args ?? Array.Empty<object>()));
        return method.ReturnType == typeof(void) || !method.ReturnType.IsValueType ? null : Activator.CreateInstance(method.ReturnType);
    }
}
