using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

static class TreeHookChecks
{
    internal static void Run(Assembly mod, Assembly game, Harmony owner, Action<bool,string> check)
    {
        var feature=mod.GetType("CityPerformance.TreeLods",true);
        var performance=mod.GetType("CityPerformance.PerformanceMod",true);
        var tree=game.GetType("TreeController",true);
        var start=AccessTools.DeclaredMethod(tree,"Start",Type.EmptyTypes);
        var map=AccessTools.DeclaredMethod(tree,"ToggleMapLod",new[]{typeof(bool)});
        var postfix=AccessTools.Method(feature,"MapChanged");
        var install=AccessTools.Method(feature,"Install");
        var verified=AccessTools.Field(performance,"Verified");
        bool Installed()=>Harmony.GetPatchInfo(map)?.Postfixes.Any(p=>p.PatchMethod==postfix)==true;
        bool Conflict()=>(bool)AccessTools.Method(feature,"Conflict").Invoke(null,null);
        check(AccessTools.Field(tree,"lodGroup")?.FieldType.FullName=="UnityEngine.LODGroup","tree group field matches the audited private-field accessor");
        var instructions=PatchProcessor.GetOriginalInstructions(start);
        int[] forced=instructions.Select((i,n)=>(i,n)).Where(x=>x.i.operand is MethodInfo m && m.Name=="ForceLOD").Select(x=>x.n).ToArray();
        check(forced.Length==1 && instructions[forced[0]-1].opcode==OpCodes.Ldc_I4_0,"tree Start contains exactly one original forced-LOD0 call");
        check(PatchProcessor.GetOriginalInstructions(map).Count(i=>i.operand is MethodInfo m && m.Name=="ForceLOD")==1,"map contains its audited forced-LOD call");
        foreach(string name in new[]{"OnLod0","OnLod1","OnLod2"})
            check(!PatchProcessor.GetOriginalInstructions(AccessTools.DeclaredMethod(tree,name)).Any(i=>i.operand is MethodInfo m && m.Name=="ForceLOD"),name+" is not a competing mesh LOD controller");
        verified.SetValue(null,false);install.Invoke(null,null);
        check(!Installed(),"unverified game cannot install tree optimization");
        verified.SetValue(null,true);
        var foreign=new Harmony("cityperformance.check.tree.foreign");
        try
        {
            foreign.Patch(start,prefix:new HarmonyMethod(typeof(Probes),nameof(Probes.Noop)));
            install.Invoke(null,null);
            check(!Installed() && Conflict(),"pre-existing foreign tree initialization patch refuses installation");
        }
        finally{foreign.UnpatchAll(foreign.Id);}
        install.Invoke(null,null);
        check(Installed() && !Conflict(),"verified tree map hook installs without treating our owner as a conflict");
        check(Harmony.GetPatchInfo(map).Prefixes.All(p=>p.PatchMethod.DeclaringType!=feature) && Harmony.GetPatchInfo(map).Transpilers.All(p=>p.PatchMethod.DeclaringType!=feature),"tree option never skips or rewrites the original map method");
        check(Harmony.GetPatchInfo(start)?.Owners.Contains(owner.Id)!=true,"tree setup keeps original Start and its particle/instancing behavior intact");
        // No current city: this must return before accessing any Unity native API.
        postfix.Invoke(null,new object[]{true});postfix.Invoke(null,new object[]{false});
        check(true,"map hook is inert outside an active city");
        try
        {
            foreign.Patch(map,prefix:new HarmonyMethod(typeof(Probes),nameof(Probes.Noop)));
            check(Conflict(),"later foreign map patch is detected");
        }
        finally{foreign.UnpatchAll(foreign.Id);}
        owner.Unpatch(map,postfix);
        check(!Installed(),"tree hook can be removed independently");
    }
}
