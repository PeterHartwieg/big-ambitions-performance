using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;

// Actual local assemblies; native engine queries are replaced only in explicit test instrumentation.
if (args.Length < 5 || args.Length > 6 || args.Length == 6 && args[5] != "--unsupported")
{ Console.Error.WriteLine("Usage: OwnershipCheck <Managed> <CityPerformance.dll> <new HMP.dll> <old HMP.dll> <targets folder> [--unsupported]"); return 2; }
string managed = Path.GetFullPath(args[0]), targets = Path.GetFullPath(args[4]);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    if (new[] { "mscorlib", "System", "System.Core", "netstandard" }.Contains(name.Name)) return null;
    foreach (string directory in new[] { managed, targets })
    {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
var cp = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
var bakeryFeature = cp.GetType("CityPerformance.BakeryHelp", true);
var lampFeature = cp.GetType("CityPerformance.HyperrealDiscovery", true);
var queries = new Harmony("ownershipcheck.queries");
var foreign = new Harmony("ownershipcheck.foreign");
int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
object Call(Type t, string m, params object[] a) => AccessTools.Method(t, m).Invoke(null, a);
object Get(Type t, string f) => AccessTools.Field(t, f).GetValue(null);
void Set(Type t, string f, object v) => AccessTools.Field(t, f).SetValue(null, v);
object B(string m, params object[] a) => Call(bakeryFeature, m, a);
bool Active(Type t) => (bool)Get(t, "active");
void Reset(Type t) { Call(t, "Initialize"); Call(t, "StartCity"); }
void Stop() { B("Shutdown"); Call(lampFeature, "Shutdown"); }
object Inert(Type t, int handle)
{
    var value = RuntimeHelpers.GetUninitializedObject(t);
    AccessTools.Field(t, "m_CachedPtr").SetValue(value, new IntPtr(handle));
    return value;
}
try
{
    Reset(bakeryFeature);
    Check(!Active(bakeryFeature) && ((string)Get(bakeryFeature, "status")).Contains("absent"), "absent Bakery is inert");
    var bakery = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(targets, "Bakery.dll"));
    var hyperreal = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(targets, "PolyModsHyperreal.dll"));
    Check((int)Get(bakeryFeature, "pending") == 1 && !Active(bakeryFeature), "late optional assembly only queues installation");
    B("Tick");
    if (args.Length == 6)
    {
        Check(!Active(bakeryFeature) && !(bool)B("HasAnyPatch") && ((string)Get(bakeryFeature, "status")).Contains("unreviewed"), "unsupported native binary set retains original Bakery Help");
        Console.WriteLine($"ALL {checks} UNSUPPORTED-PLATFORM CHECKS PASSED"); return 0;
    }
    Check(Active(bakeryFeature) && (bool)B("CanOptimize"), "exact Windows/Bakery identities install with no self-conflict");
    var tryApply = (MethodInfo)Get(bakeryFeature, "tryApply");
    var refresh = (MethodInfo)Get(bakeryFeature, "refresh");
    var build = (MethodInfo)Get(bakeryFeature, "loadCategories");
    var finder = (MethodInfo)Get(bakeryFeature, "finder");
    var rewrite = (MethodInfo)Get(bakeryFeature, "rewrite");
    var lookup = (MethodInfo)Get(bakeryFeature, "lookup");
    var owner = (Harmony)Get(bakeryFeature, "harmony");
    var helpType = build.DeclaringType;
    var original = PatchProcessor.GetOriginalInstructions(tryApply);
    var changed = ((IEnumerable<CodeInstruction>)B("Rewrite", original)).ToArray();
    Check(original.Count == changed.Length && original.Zip(changed).Count(p => p.First.opcode != p.Second.opcode || !Equals(p.First.operand, p.Second.operand)) == 1,
        "actual Bakery TryApply changes one operand only, keeping page content logic");
    Check(original.Zip(changed).All(p => p.First.labels.SequenceEqual(p.Second.labels) && p.First.blocks.SequenceEqual(p.Second.blocks)) && original.Count(i => i.Calls(finder)) == 1,
        "original IL remains untouched; branch/EH metadata retained");
    foreach (var shape in new[] { original.Where(i => !i.Calls(finder)).ToList(), original.Concat(new[] { new CodeInstruction(original.Single(i => i.Calls(finder))) }).ToList() })
    {
        var result = ((IEnumerable<CodeInstruction>)B("Rewrite", shape)).ToArray();
        Check(shape.Zip(result).All(p => ReferenceEquals(p.First, p.Second)) && !Active(bakeryFeature), "unexpected finder count retains body and withdraws"); Reset(bakeryFeature);
    }
    Call(lampFeature, "Initialize"); Call(lampFeature, "StartCity");
    Check(Active(lampFeature), "new Hyperreal adapter active before ownership checks");

    // Real pre-patch delegate reaches the patched discovery path; no Help singleton means no native search.
    AccessTools.Field(tryApply.DeclaringType, "cachedHelpSystemType").SetValue(null, helpType);
    AccessTools.Field(tryApply.DeclaringType, "helpSystemTypeResolved").SetValue(null, true);
    var actualDelegate = (Delegate)Get(bakeryFeature, "applyHelp");
    Check(!(bool)actualDelegate.DynamicInvoke(null, false), "pre-patch Bakery delegate handles absent Help without global search");
    var help = Inert(helpType, 1); var otherHelp = Inert(helpType, 2);
    Set(bakeryFeature, "buildingHelp", help);
    var one = (Array)B("FindHelp", helpType);
    Set(bakeryFeature, "buildingHelp", otherHelp);
    var two = (Array)B("FindHelp", helpType);
    Check(one.Length == 1 && ReferenceEquals(one.GetValue(0), help) && ReferenceEquals(two.GetValue(0), otherHelp) && !ReferenceEquals(one, two),
        "reentrant discovery cannot overwrite prior returned Help array");
    Set(bakeryFeature, "buildingHelp", null);
    Check(((Array)B("FindHelp", helpType)).Length == 0, "missing singleton returns empty without scene search");
    var lifecycle = helpType.BaseType.Assembly.GetType("Extensions.SingletonLifecycle", true);
    Call(lifecycle, "RegisterSingleton", helpType, help);
    try { Check(ReferenceEquals(((Array)B("FindHelp", helpType)).GetValue(0), help), "registered singleton used without a scene query"); }
    finally { Call(lifecycle, "UnregisterSingleton", helpType, help); }

    // Execute the author's actual page-insertion IL with only native scene eligibility stubbed.
    // Exempt this explicit test transpiler from boundary refusal for this fixture only.
    var savedBoundaries = Get(bakeryFeature, "boundaries");
    Set(bakeryFeature, "boundaries", ((MethodBase[])savedBoundaries).Where(m => !Equals(m, tryApply)).ToArray());
    try
    {
        var categoriesField = AccessTools.Field(helpType, "_categories");
        var categoryType = categoriesField.FieldType.GetElementType() ?? categoriesField.FieldType.GetGenericArguments()[0];
        var category = Activator.CreateInstance(categoryType);
        AccessTools.Field(categoryType, "CategoryLocalizorKey").SetValue(category, "common_sellable_products");
        var pagesField = AccessTools.Field(categoryType, "Pages");
        var pages = (IList)Activator.CreateInstance(pagesField.FieldType);
        pagesField.SetValue(category, pages);
        if (categoriesField.FieldType.IsArray)
        { var categories = Array.CreateInstance(categoryType, 1); categories.SetValue(category, 0); categoriesField.SetValue(help, categories); }
        else
        { var categories = (IList)Activator.CreateInstance(categoriesField.FieldType); categories.Add(category); categoriesField.SetValue(help, categories); }
        var patchedTryApply = queries.Patch(tryApply, transpiler: new HarmonyMethod(typeof(Probes), nameof(Probes.ReplaceSceneEligibility)));
        Set(bakeryFeature, "buildingHelp", help);
        Check((bool)patchedTryApply.Invoke(null, new object[] { help, false }) && pages.Count == 5,
            "actual Bakery prebuild succeeds with refresh suppressed and inserts all five product pages");
        Check((bool)patchedTryApply.Invoke(null, new object[] { help, false }) && pages.Count == 5,
            "actual Bakery repeated application remains successful and does not duplicate pages");
        var slugs = pages.Cast<object>().Select(p => (string)AccessTools.Field(p.GetType(), "Slug").GetValue(p)).ToArray();
        Check(slugs.Distinct().Count() == 5 && slugs.All(s => s.StartsWith("products-bakeryxx-businesstype:")),
            "author page identities preserved by prebuild");
    }
    finally
    {
        Set(bakeryFeature, "buildingHelp", null);
        queries.Unpatch(tryApply, AccessTools.Method(typeof(Probes), nameof(Probes.ReplaceSceneEligibility)));
        Set(bakeryFeature, "boundaries", savedBoundaries);
    }
    var canOptimize = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), AccessTools.Method(bakeryFeature, "CanOptimize"));
    for (int i = 0; i < 20; i++) canOptimize();
    long allocationStart = GC.GetAllocatedBytesForCurrentThread();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    for (int i = 0; i < 1000; i++) if (!canOptimize()) throw new Exception("Unexpected conflict in cost sample");
    watch.Stop();
    Console.WriteLine($"MEASUREMENT CoreCLR guard only, {Harmony.GetAllPatchedMethods().Count()} registered methods: {watch.Elapsed.TotalMilliseconds / 1000:F4} ms/call, {(GC.GetAllocatedBytesForCurrentThread() - allocationStart) / 1000} bytes/call; not a native performance result");

    var populateField = AccessTools.Field(bakeryFeature, "applyHelp");
    var populationProbe = Delegate.CreateDelegate(populateField.FieldType, AccessTools.Method(typeof(Probes), nameof(Probes.Populate)));
    populateField.SetValue(null, populationProbe);
    Probes.DuringPopulate = () =>
    {
        object[] arg = { true };
        Check(!(bool)B("RefreshPrefix", arg) && !(bool)arg[0], "only build-time nested refresh is suppressed");
        B("BuildPrefix", otherHelp);
    };
    B("BuildPrefix", help);
    Check(Probes.Populations == 1 && ReferenceEquals(Probes.Host, help) && !Probes.Force && Get(bakeryFeature, "buildingHelp") == null,
        "prebuild inserts author pages once and releases nested build scope");
    Check((bool)B("RefreshPrefix", false), "normal author refresh remains allowed outside prebuild");
    Probes.DuringPopulate = null;
    Call(lifecycle, "RegisterSingleton", helpType, help);
    try { B("PopulateExisting"); Check(Probes.Populations == 2, "already initialized Help gets catch-up population"); }
    finally { Call(lifecycle, "UnregisterSingleton", helpType, help); }
    B("SetEnabled", true); Check(Probes.Populations == 2, "unchanged enabled option never repopulates");
    Probes.ThrowPopulate = true; B("BuildPrefix", help);
    Check(Get(bakeryFeature, "buildingHelp") == null && !Active(bakeryFeature) && (bool)B("RefreshPrefix", false), "failed optional insertion clears scope and lets original Help build");
    Probes.ThrowPopulate = false; B("Tick"); Check(!(bool)B("HasAnyPatch"), "population failure removes every own hook");
    Reset(bakeryFeature);

    // Invoke actual helper IL with only its native query replaced; verify original fallback and exceptions.
    var instrumented = queries.Patch(lookup, transpiler: new HarmonyMethod(typeof(Probes), nameof(Probes.ReplaceQuery)));
    var objectType = finder.ReturnType.GetElementType();
    Probes.QueryResult = Array.CreateInstance(objectType, 0);
    object Discover(Type type) => instrumented.Invoke(null, new object[] { type });
    B("SetEnabled", false);
    Check(ReferenceEquals(Discover(helpType), Probes.QueryResult) && Probes.QueryType == helpType, "option off preserves original query/result");
    B("SetEnabled", true);
    Check(ReferenceEquals(Discover(objectType), Probes.QueryResult) && Probes.QueryType == objectType, "unexpected resolved type retains original query");
    Probes.ThrowQuery = true; int before = Probes.QueryCalls;
    try { Discover(objectType); throw new Exception("exception missing"); }
    catch (TargetInvocationException e) { Check(ReferenceEquals(e.InnerException, Probes.Error) && Probes.QueryCalls == before + 1, "native fallback exception propagates once"); }
    Probes.ThrowQuery = false;
    queries.Unpatch(lookup, AccessTools.Method(typeof(Probes), nameof(Probes.ReplaceQuery)));

    foreach (var boundary in new[] { tryApply, refresh, build, finder })
    {
        foreign.Patch(boundary, prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.Noop)));
        Check(!(bool)B("CanOptimize") && (bool)B("RefreshPrefix", false), "late boundary hook disables coupled Help behavior: " + boundary.Name);
        B("Tick"); Check(!(bool)B("HasAnyPatch") && Harmony.GetPatchInfo(boundary).Owners.Contains(foreign.Id), "withdrawal removes only own hooks: " + boundary.Name);
        Reset(bakeryFeature); Check(!Active(bakeryFeature), "pre-existing conflict prevents installation: " + boundary.Name);
        foreign.UnpatchAll(foreign.Id); Reset(bakeryFeature);
    }

    // Partial install and unpatch failures must not leave a working subset or retry every frame.
    var patchApi = typeof(Harmony).GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 5);
    foreach (var step in new[] { tryApply, refresh, build })
    {
        Probes.RefuseTarget = step;
        B("Shutdown");
        queries.Patch(patchApi, prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.FailPatch)));
        Reset(bakeryFeature);
        Check(!(bool)B("HasAnyPatch") && !Active(bakeryFeature), "partial installation fully rolls back at " + step.Name);
        queries.Unpatch(patchApi, AccessTools.Method(typeof(Probes), nameof(Probes.FailPatch))); Reset(bakeryFeature);
    }
    var unpatchApi = AccessTools.Method(typeof(Harmony), "Unpatch", new[] { typeof(MethodBase), typeof(MethodInfo) });
    Probes.RefusePatch = rewrite;
    queries.Patch(unpatchApi, prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.FailUnpatch)));
    B("SetEnabled", false);
    Check(!Active(bakeryFeature) && (bool)Get(bakeryFeature, "removeFailed") && Get(bakeryFeature, "applyHelp") == null, "failed unpatch drops active behavior and retained delegate");
    before = Probes.UnpatchCalls; for (int i = 0; i < 100; i++) B("Tick");
    Check(Probes.UnpatchCalls == before, "failed cleanup is never retried each frame");
    queries.Unpatch(unpatchApi, AccessTools.Method(typeof(Probes), nameof(Probes.FailUnpatch)));
    B("Shutdown"); Check(!(bool)B("HasAnyPatch"), "later shutdown removes residual hook"); Set(bakeryFeature, "Enabled", true); Reset(bakeryFeature);

    // Install the existing sibling hooks, without their native lifecycle/Unity entry points.
    // DetailedTrace's fixed target discovery is exercised; live subscriber discovery is a native-stage check.
    var performanceMod = cp.GetType("CityPerformance.PerformanceMod", true);
    var baseOwner = (Harmony)Get(performanceMod, "Harmony");
    Set(performanceMod, "Verified", true);
    queries.Patch(AccessTools.Method(performanceMod, "Log"), prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.CaptureLog)));
    var zoneConflict = AccessTools.Method(cp.GetType("CityPerformance.ZoneScheduling", true), "Conflict");
    queries.Patch(zoneConflict, transpiler: new HarmonyMethod(typeof(Probes), nameof(Probes.ReplaceClock)));
    try
    {
        foreach (string name in new[] { "FrameTrace", "Refill", "ZoneScheduling", "TreeLods", "CinematicWorld", "CinematicPopulation", "CinematicTraffic", "CinematicDecals", "CinematicAudioZones" })
        {
            var feature = cp.GetType("CityPerformance." + name, true);
            Call(feature, "Install");
            Check(Harmony.GetAllPatchedMethods().Any(m =>
            {
                var info = Harmony.GetPatchInfo(m);
                return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                    .Any(p => p.owner == baseOwner.Id && p.PatchMethod.DeclaringType == feature);
            }), "sibling hook fixture really installs " + name);
        }
        var detailed = cp.GetType("CityPerformance.DetailedTrace", true);
        var kind = Enum.ToObject(detailed.GetNestedType("Kind", BindingFlags.NonPublic), 0);
        var detailTargets = ((IEnumerable<MethodInfo>)Call(detailed, "HourlyTargets"))
            .Concat((IEnumerable<MethodInfo>)Call(detailed, "DailyTargets")).Distinct().ToArray();
        foreach (var target in detailTargets) Call(detailed, "Install", target, kind);
        Check(detailTargets.Length > 0 && detailTargets.All(m => Harmony.GetPatchInfo(m).Owners.Contains(baseOwner.Id)),
            "fixed hourly/daily diagnostic targets installed from actual game IL");
        Check((bool)B("CanOptimize") && (bool)Call(lampFeature, "UseUnsorted"),
            "discovery remains active alongside existing CP behavior and fixed diagnostic hooks");
    }
    finally { baseOwner.UnpatchAll(baseOwner.Id); Set(performanceMod, "Verified", false); queries.Unpatch(zoneConflict, AccessTools.Method(typeof(Probes), nameof(Probes.ReplaceClock))); queries.Unpatch(AccessTools.Method(performanceMod, "Log"), AccessTools.Method(typeof(Probes), nameof(Probes.CaptureLog))); }

    var newHmp = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
    var oldHmp = Assembly.Load(File.ReadAllBytes(Path.GetFullPath(args[3])));
    void WithHmp(Assembly assembly, bool legacy, bool hmpFirst, bool legacyOptions = true)
    {
        Stop();
        var h = new Harmony(legacy ? "ownershipcheck.oldhmp" : "ownershipcheck.newhmp");
        var instances = new List<(Type Type, object Instance)>();
        var rules = assembly.GetType("HardModePatch.Core.Rules", true);
        var priceRule = rules.GetField("BakeryVanillaMargins").GetValue(null);
        priceRule.GetType().GetField("Enabled").SetValue(priceRule, false); // No item catalogue in the offline fixture.
        if (legacy)
            foreach (string name in new[] { "BakeryHelpDiscovery", "HyperrealLightDiscovery" })
            { var rule = rules.GetField(name).GetValue(null); rule.GetType().GetField("Enabled").SetValue(rule, legacyOptions); }
        void StartCp() { Reset(bakeryFeature); Reset(lampFeature); }
        try
        {
            if (!hmpFirst) StartCp();
            foreach (var entry in new[] { ("BakeryPatch", bakery), ("HyperrealPatch", hyperreal) })
            {
                var t = assembly.GetType("HardModePatch." + entry.Item1, true); var instance = Activator.CreateInstance(t);
                var hooks = (IList<MethodBase>)t.GetMethod("ResolveHooks").Invoke(instance, new object[] { entry.Item2 });
                Check(hooks.All(x => x != null), (legacy ? "old" : "new") + " HMP hooks resolve: " + entry.Item1);
                t.GetMethod("Apply").Invoke(instance, new object[] { h, entry.Item2, hooks }); instances.Add((t, instance));
            }
            if (hmpFirst) StartCp();
            B("Tick"); Call(lampFeature, "Tick");
            Check(Active(bakeryFeature) == !legacy && Active(lampFeature) == !legacy,
                (legacy ? "old HMP takes precedence" : "new HMP coexists with CP discovery") + (hmpFirst ? " when HMP loads first" : " when CP loads first") + (legacy ? "; legacy options=" + legacyOptions : ""));
            Check(Harmony.GetPatchInfo(tryApply)?.Owners.Contains(h.Id) == legacy,
                (legacy ? "legacy" : "new") + " Bakery discovery ownership is explicit");
            var lampScan = AccessTools.Method(hyperreal.GetType("PolyModsHyperreal.StreetLights", true), "Scan");
            Check(Harmony.GetPatchInfo(lampScan)?.Owners.Contains(h.Id) == legacy,
                (legacy ? "legacy" : "new") + " lamp discovery ownership is explicit");
            var volumeApply = AccessTools.Method(hyperreal.GetType("PolyModsHyperreal.EnhancementVolume", true), "Apply");
            Check(Harmony.GetPatchInfo(volumeApply).Owners.Contains(h.Id), "HMP retains map-return hook independently");
            Stop();
            Check(Harmony.GetPatchInfo(volumeApply).Owners.Contains(h.Id), "CP unload preserves HMP compatibility hooks");
        }
        finally { Stop(); h.UnpatchAll(h.Id); foreach (var pair in instances) pair.Type.GetMethod("Revert").Invoke(pair.Instance, null); }
    }
    foreach (bool first in new[] { true, false }) { WithHmp(newHmp, false, first); WithHmp(oldHmp, true, first); WithHmp(oldHmp, true, first, false); }
    Reset(bakeryFeature); B("EndCity");
    Check(!(bool)B("HasAnyPatch") && Get(bakeryFeature, "buildingHelp") == null && Get(bakeryFeature, "applyHelp") == null, "city teardown clears hooks and references");
    B("StartCity"); Check(Active(bakeryFeature), "next city reinstalls only CP-owned optimization");
    B("Shutdown"); Check(!(bool)B("HasAnyPatch"), "mod unload removes all Help hooks");
    Console.WriteLine($"ALL {checks} OWNERSHIP CHECKS PASSED (offline; no native Help/lighting/performance claim)"); return 0;
}
finally { queries.UnpatchAll(queries.Id); foreign.UnpatchAll(foreign.Id); Stop(); }

static class Probes
{
    public static int Populations, QueryCalls, UnpatchCalls;
    public static object Host, QueryResult;
    public static Type QueryType;
    public static bool Force, ThrowPopulate, ThrowQuery;
    public static Action DuringPopulate;
    public static MethodBase RefuseTarget;
    public static MethodInfo RefusePatch;
    public static readonly Exception Error = new InvalidOperationException("query fixture");
    public static void Noop() { }
    public static void CaptureLog(string __0) => Console.WriteLine("FIXTURE LOG " + __0);
    public static bool Populate(object host, bool force)
    { Populations++; Host = host; Force = force; DuringPopulate?.Invoke(); if (ThrowPopulate) throw new InvalidOperationException("population fixture"); return true; }
    public static T[] Query<T>(Type type)
    { QueryCalls++; QueryType = type; if (ThrowQuery) throw Error; return (T[])QueryResult; }
    public static void FailPatch(MethodBase original) { if (Equals(original, RefuseTarget)) throw new InvalidOperationException("partial install fixture"); }
    public static void FailUnpatch(object[] __args) { if (Equals(__args[1], RefusePatch)) { UnpatchCalls++; throw new InvalidOperationException("unpatch fixture"); } }
    public static int Frame() => 1;
    public static IEnumerable<CodeInstruction> ReplaceClock(IEnumerable<CodeInstruction> code)
    {
        int count = 0;
        foreach (var instruction in code)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.operand is MethodInfo m && m.DeclaringType.FullName == "UnityEngine.Time" && m.Name == "get_frameCount")
            { copy.operand = AccessTools.Method(typeof(Probes), nameof(Frame)); count++; }
            yield return copy;
        }
        if (count != 1) throw new Exception("Unexpected native clock fixture shape");
    }
    public static T NativeDefault<T>(object instance) => default;
    public static bool ValidScene<T>(ref T scene) => true;
    public static IEnumerable<CodeInstruction> ReplaceSceneEligibility(IEnumerable<CodeInstruction> code)
    {
        int count = 0;
        foreach (var instruction in code)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.operand is MethodInfo m)
            {
                if ((m.DeclaringType.FullName == "UnityEngine.Component" && m.Name == "get_gameObject") ||
                    (m.DeclaringType.FullName == "UnityEngine.GameObject" && m.Name == "get_scene"))
                { copy.opcode = OpCodes.Call; copy.operand = AccessTools.Method(typeof(Probes), nameof(NativeDefault)).MakeGenericMethod(m.ReturnType); count++; }
                else if (m.DeclaringType.FullName == "UnityEngine.SceneManagement.Scene" && m.Name == "IsValid")
                { copy.opcode = OpCodes.Call; copy.operand = AccessTools.Method(typeof(Probes), nameof(ValidScene)).MakeGenericMethod(m.DeclaringType); count++; }
            }
            yield return copy;
        }
        if (count != 3) throw new Exception("Unexpected native scene eligibility shape");
    }
    public static IEnumerable<CodeInstruction> ReplaceQuery(IEnumerable<CodeInstruction> code)
    {
        foreach (var instruction in code)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.operand is MethodInfo m && m.DeclaringType.FullName == "UnityEngine.Resources" && m.Name == "FindObjectsOfTypeAll")
                copy.operand = AccessTools.Method(typeof(Probes), nameof(Query)).MakeGenericMethod(m.ReturnType.GetElementType());
            yield return copy;
        }
    }
}
