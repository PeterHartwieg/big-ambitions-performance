using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;

// Actual locally supplied assemblies + production hooks; never launches Unity or invokes native APIs.
// Query substitution below is test instrumentation, not shipped in the mod.
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: HyperrealCheck <Managed> <CityPerformance.dll> <PolyModsHyperreal.dll>");
    return 2;
}
string managed = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    if (new[] { "mscorlib", "System", "System.Core", "netstandard" }.Contains(name.Name)) return null;
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
var feature = mod.GetType("CityPerformance.HyperrealDiscovery", true);
var owner = (Harmony)AccessTools.Field(feature, "harmony").GetValue(null);
var instrument = new Harmony("cityperformance.hyperrealcheck.queries");
var foreign = new Harmony("cityperformance.hyperrealcheck.foreign");
var rewrite = AccessTools.Method(feature, "Rewrite");
int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
object Call(string name, params object[] values) => AccessTools.Method(feature, name).Invoke(null, values);
object Get(string name) => AccessTools.Field(feature, name).GetValue(null);
void Set(string name, object value) => AccessTools.Field(feature, name).SetValue(null, value);
bool Patched() => (bool)Call("HasPatch");
void Reset() { Call("Initialize"); Call("StartCity"); }
try
{
    Call("Initialize"); Call("StartCity");
    Check(!Patched() && ((string)Get("status")).Contains("absent"), "missing optional mod leaves City Performance active and Hyperreal hook absent");
    var hyperreal = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
    Check(!Patched() && (int)Get("pending") == 1, "late assembly notification only queues main-thread installation");
    Call("Tick");
    Check(Patched() && (bool)Get("active"), "late Hyperreal installs after main-thread tick with audited binary pair");
    Check((bool)Call("UseUnsorted") && !(bool)Get("blocked"), "clean installed hook is not mistaken for a conflict before first query");
    var scan = (MethodInfo)Get("scan");
    var finder = (MethodInfo)Get("originalFinder");
    var helper = (MethodInfo)Get("replacement");
    var light = finder.GetGenericArguments()[0];
    var street = scan.DeclaringType;
    Check(AccessTools.Field(street, "_lights").FieldType.GetGenericArguments()[0] == light,
        "audited baseline dictionary is keyed by light instance, not shared prefab name");
    Check(scan.IsPrivate && !scan.IsStatic && scan.ReturnType == typeof(void) && scan.GetParameters().Length == 0,
        "actual Hyperreal target is private instance void Scan()");
    Check((string)Call("Digest", hyperreal) == (string)AccessTools.Field(feature, "HyperrealHash").GetRawConstantValue(), "exact Hyperreal SHA accepted");
    Check(!(bool)Call("ReviewedPair", "unknown", (string)Call("Digest", light.Assembly)), "unknown Core binary rejected");
    Check(!(bool)Call("ReviewedPair", (string)Call("Digest", finder.DeclaringType.Assembly), "unknown"), "unknown HDRP binary rejected");
    Check(!(bool)Call("ReviewedPair", "5afc743d350e95dc30379a6d2bafa2293b63259acf196fe3f7370f03644ef6f4", "acedb057709b46696572e8a6874e97d8525c1b900db95be2f9d314257e512450"), "mixed platform pair rejected");
    Check((bool)Call("ReviewedPair", (string)Call("Digest", finder.DeclaringType.Assembly), (string)Call("Digest", light.Assembly)), "actual supplied Unity/HDRP pair matches audit");

    var source = PatchProcessor.GetOriginalInstructions(scan);
    var changed = ((IEnumerable<CodeInstruction>)Call("Rewrite", source)).ToArray();
    int index = source.FindIndex(i => i.Calls(finder));
    Check(index > 0 && source.Count == changed.Length && source.Zip(changed).Count(p => p.First.opcode != p.Second.opcode || !Equals(p.First.operand, p.Second.operand)) == 1,
        "actual Scan changes exactly one operand and preserves all baseline/foliage instructions");
    Check(changed[index].Calls(helper) && source[index].Calls(finder) && !ReferenceEquals(changed[index], source[index]), "correct closed generic helper replaces finder without mutating input");
    Check(source.Zip(changed).All(p => p.First.labels.SequenceEqual(p.Second.labels) && p.First.blocks.SequenceEqual(p.Second.blocks)), "actual branch and exception metadata preserved");
    var labeled = source.Select(i => new CodeInstruction(i)).ToList();
    var label = new DynamicMethod("fixture", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
    labeled[index - 1].labels.Add(label);
    labeled[index].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
    var labeledOutput = ((IEnumerable<CodeInstruction>)Call("Rewrite", labeled)).ToArray();
    Check(labeledOutput[index - 1].labels.Contains(label) && labeledOutput[index].blocks.Count == labeled[index].blocks.Count,
        "synthetic label and exception marker survive one-call replacement");
    void Refuse(List<CodeInstruction> input, string name)
    {
        var output = ((IEnumerable<CodeInstruction>)Call("Rewrite", input)).ToArray();
        Check(output.Length == input.Count && input.Zip(output).All(p => ReferenceEquals(p.First, p.Second)) && (bool)Get("blocked"), name);
        Reset();
    }
    Refuse(source.Where((_, i) => i != index).ToList(), "missing finder refuses rewrite without removing original body");
    var twice = source.Select(i => new CodeInstruction(i)).ToList(); twice.Insert(index, new CodeInstruction(source[index]));
    Refuse(twice, "duplicate finder refuses rewrite");
    var falseArg = source.Select(i => new CodeInstruction(i)).ToList(); falseArg[index - 1].opcode = OpCodes.Ldc_I4_0;
    Refuse(falseArg, "unexpected false argument refuses rewrite");
    var wrongType = source.Select(i => new CodeInstruction(i)).ToList(); wrongType[index].operand = finder.GetGenericMethodDefinition().MakeGenericMethod(finder.DeclaringType);
    Refuse(wrongType, "different generic element type refuses rewrite");
    var branchToCall = source.Select(i => new CodeInstruction(i)).ToList(); branchToCall[index].labels.Add(label);
    Refuse(branchToCall, "branch bypassing expected true constant refuses rewrite");

    Call("StartCity"); Call("Tick");
    Check(Harmony.GetPatchInfo(scan).Transpilers.Count(p => p.owner == owner.Id) == 1, "repeated lifecycle callbacks never double-install");
    Call("SetEnabled", false);
    Check(!Patched() && !(bool)Get("active"), "option off removes exactly the feature transpiler");
    Call("SetEnabled", true);
    Check(Patched() && (bool)Get("active"), "option re-enable reinstalls validated target");
    Call("EndCity"); Check(!Patched(), "city teardown removes own transpiler");
    Call("StartCity"); Check(Patched(), "new city reinstalls with no scene object state retained");

    var boundaries = (MethodBase[])Get("boundaries");
    Check(boundaries.Contains(finder) && boundaries.Contains(finder.GetGenericMethodDefinition()) && boundaries.Contains(scan), "closed and generic finder boundaries plus target are guarded");
    Check(boundaries.OfType<MethodInfo>().Any(m => m.Name == "FindObjectsByType" && !m.IsGenericMethod) &&
        boundaries.OfType<MethodInfo>().Any(m => m.Name == "ConvertObjects"), "native query overloads and shared array conversion are guarded");

    // Execute the REAL production helper while replacing ONLY its two engine call operands.
    // This tests selection/arguments/result identity/exception behavior without a Unity runtime.
    var instrumentedHelper = instrument.Patch(helper, transpiler: new HarmonyMethod(typeof(Probes), nameof(Probes.ReplaceQueries)));
    var first = RuntimeHelpers.GetUninitializedObject(light);
    var second = RuntimeHelpers.GetUninitializedObject(light);
    Array inventory = Array.CreateInstance(light, 2); inventory.SetValue(first, 0); inventory.SetValue(second, 1);
    Array unsorted = Array.CreateInstance(light, 2); unsorted.SetValue(second, 0); unsorted.SetValue(first, 1);
    Probes.OriginalResult = inventory; Probes.UnsortedResult = unsorted;
    object Discover(bool includeInactive) => instrumentedHelper.Invoke(null, new object[] { includeInactive });
    Check(ReferenceEquals(Discover(true), unsorted) && Probes.Inactive == 1 && Probes.Sort == 0 && Probes.OriginalCalls == 0, "actual helper requests Include/None and returns exact fresh unsorted result");
    Discover(false); Check(Probes.Inactive == 0 && Probes.Sort == 0, "helper preserves false includeInactive stack contract");
    Array newInventory = Array.CreateInstance(light, 1); newInventory.SetValue(first, 0); Probes.UnsortedResult = newInventory;
    Check(ReferenceEquals(Discover(true), newInventory), "subsequent query returns changed inventory; no retained result or duplicate discovery cache");
    Probes.Throw = true;
    int originalBefore = Probes.OriginalCalls, unsortedBefore = Probes.UnsortedCalls;
    try { Discover(true); throw new Exception("missing exception"); }
    catch (TargetInvocationException e) { Check(ReferenceEquals(e.InnerException, Probes.Error), "optimized query exception identity propagates"); }
    Check(Probes.OriginalCalls == originalBefore && Probes.UnsortedCalls == unsortedBefore + 1, "throwing optimized query is never retried through original");
    Probes.Throw = false;
    Call("SetEnabled", false);
    Check(ReferenceEquals(Discover(true), inventory) && Probes.IncludeInactive, "disabled helper delegates original query and result identity");
    Probes.Throw = true;
    try { Discover(false); throw new Exception("missing exception"); }
    catch (TargetInvocationException e) { Check(ReferenceEquals(e.InnerException, Probes.Error) && !Probes.IncludeInactive, "fallback preserves argument and original exception identity"); }
    Probes.Throw = false; Call("SetEnabled", true);

    // Real foreign hooks at the target, consumer and both generic query boundaries.
    var apply = AccessTools.DeclaredMethod(street, "Apply");
    var byType = boundaries.OfType<MethodInfo>().First(m => m.Name == "FindObjectsByType" && m.IsGenericMethod && !m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
    var differentInstantiation = finder.GetGenericMethodDefinition().MakeGenericMethod(finder.DeclaringType);
    foreach (var boundary in new MethodInfo[] { scan, apply, finder, byType, differentInstantiation })
    {
        foreign.Patch(boundary, prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.Noop)));
        Check(!(bool)Call("UseUnsorted") && (bool)Get("blocked"), "late foreign hook disables substitution: " + boundary.Name);
        Call("Tick");
        Check(!Patched() && Harmony.GetPatchInfo(boundary).Prefixes.Any(p => p.owner == foreign.Id), "withdrawal preserves foreign patch: " + boundary.Name);
        Reset(); Check(!Patched(), "pre-existing conflict refuses installation: " + boundary.Name);
        foreign.UnpatchAll(foreign.Id); Reset(); Check(Patched(), "fresh initialization after conflict removal installs: " + boundary.Name);
    }
    // Simulate HMP's default-priority same-call substitution after our lower-priority patch.
    Probes.Finder = finder;
    foreign.Patch(scan, transpiler: new HarmonyMethod(typeof(Probes), nameof(Probes.OtherFinder)));
    Check((bool)Get("blocked"), "later default-priority finder replacement composes without throwing");
    Call("Tick");
    Check(!Patched() && Harmony.GetPatchInfo(scan).Transpilers.Any(p => p.owner == foreign.Id), "same-call conflict leaves the other transpiler intact");
    foreign.UnpatchAll(foreign.Id); Reset();

    // Failed removal must remain safe without throwing again on every rendered frame.
    var unpatch = AccessTools.Method(typeof(Harmony), "Unpatch", new[] { typeof(MethodBase), typeof(MethodInfo) });
    Probes.OurTranspiler = rewrite;
    instrument.Patch(unpatch, prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.FailRemove)));
    Call("SetEnabled", false);
    Check(Patched() && (bool)Get("removeFailed") && !(bool)Get("active") && Probes.RemoveAttempts == 1,
        "failed unpatch retains disabled pass-through and records failure");
    Check(((string)Get("status")).Contains("unpatch failed"), "option disable preserves unpatch failure diagnostic");
    for (int i = 0; i < 100; i++) Call("Tick");
    Check(Probes.RemoveAttempts == 1 && ReferenceEquals(Discover(true), inventory), "failed unpatch never retries each frame and helper remains original");
    Call("EndCity"); Check(Probes.RemoveAttempts == 2, "explicit lifecycle boundary may retry failed removal");
    Call("Initialize");
    Check((bool)Get("blocked") && ((string)Get("status")).Contains("unpatch failed"), "reinitialization preserves persistent unpatch failure status");
    instrument.Unpatch(unpatch, AccessTools.Method(typeof(Probes), nameof(Probes.FailRemove)));
    Call("Shutdown"); Check(!Patched() && !(bool)Get("removeFailed"), "later successful lifecycle cleanup clears residual patch");
    Set("Enabled", true); Reset();

    instrument.Patch(unpatch, prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.SkipRemove)));
    Call("SetEnabled", false);
    Check(Patched() && (bool)Get("blocked") && (bool)Get("removeFailed") &&
        ((string)Get("status")).Contains("unpatch did not remove"), "silent unsuccessful unpatch is diagnosed and blocked");
    instrument.Unpatch(unpatch, AccessTools.Method(typeof(Probes), nameof(Probes.SkipRemove)));
    Call("Shutdown"); Set("Enabled", true); Reset();

    var digest = AccessTools.Method(feature, "Digest");
    Call("Shutdown");
    instrument.Patch(digest, prefix: new HarmonyMethod(typeof(Probes), nameof(Probes.BadHash)));
    Reset();
    Check(!Patched() && ((string)Get("status")).Contains("unreviewed Hyperreal"), "changed Hyperreal hash rejects the real installation path");
    Call("SetEnabled", false); Call("SetEnabled", true);
    Check(!Patched() && ((string)Get("status")).Contains("reload required"), "toggle cannot override incompatibility and keeps reason visible");
    instrument.Unpatch(digest, AccessTools.Method(typeof(Probes), nameof(Probes.BadHash))); Reset();
    Check(Patched(), "verified binary reinstalls only after fresh initialization");
    Call("Shutdown");
    Check(!Patched() && !(bool)Get("initialized") && !(bool)Get("inCity"), "mod unload removes own transpiler and disables helper");
    Check(ReferenceEquals(Discover(true), inventory), "even retained helper delegate uses original after unload");
    AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PolyModsHyperreal"), AssemblyBuilderAccess.Run);
    Reset();
    Check(!Patched() && ((string)Get("status")).Contains("ambiguous"), "ambiguous optional mod identity refuses installation");
    Console.WriteLine($"ALL {checks} HYPERREAL CHECKS PASSED (offline; native membership, visuals and performance unmeasured)");
    return 0;
}
finally
{
    Probes.Throw = false;
    foreign.UnpatchAll(foreign.Id); instrument.UnpatchAll(instrument.Id); Call("Shutdown");
}

static class Probes
{
    public static object OriginalResult, UnsortedResult;
    public static bool IncludeInactive, Throw;
    public static int Inactive, Sort, OriginalCalls, UnsortedCalls;
    public static readonly Exception Error = new InvalidOperationException("query fixture");
    public static MethodInfo Finder;
    public static MethodInfo OurTranspiler;
    public static int RemoveAttempts;
    public static void FailRemove(object[] __args)
    {
        if (Equals(__args[1], OurTranspiler)) { RemoveAttempts++; throw new InvalidOperationException("unpatch fixture"); }
    }
    public static bool SkipRemove(object[] __args) => !Equals(__args[1], OurTranspiler);
    public static bool BadHash(Assembly assembly, ref string __result)
    {
        if (assembly.GetName().Name != "PolyModsHyperreal") return true;
        __result = "changed-binary";
        return false;
    }
    public static void Noop() { }
    public static T[] Original<T>(bool includeInactive)
    {
        OriginalCalls++; IncludeInactive = includeInactive;
        if (Throw) throw Error;
        return (T[])OriginalResult;
    }
    public static T[] Unsorted<T>(int inactive, int sort)
    {
        UnsortedCalls++; Inactive = inactive; Sort = sort;
        if (Throw) throw Error;
        return (T[])UnsortedResult;
    }
    public static IEnumerable<CodeInstruction> ReplaceQueries(IEnumerable<CodeInstruction> code)
    {
        foreach (var instruction in code)
        {
            var copy = new CodeInstruction(instruction);
            if (copy.operand is MethodInfo m && m.DeclaringType.FullName == "UnityEngine.Object")
            {
                if (m.Name == "FindObjectsOfType") copy.operand = AccessTools.Method(typeof(Probes), nameof(Original)).MakeGenericMethod(m.GetGenericArguments());
                if (m.Name == "FindObjectsByType") copy.operand = AccessTools.Method(typeof(Probes), nameof(Unsorted)).MakeGenericMethod(m.GetGenericArguments());
            }
            yield return copy;
        }
    }
    public static IEnumerable<CodeInstruction> OtherFinder(IEnumerable<CodeInstruction> code)
    {
        var result = code.Select(i => new CodeInstruction(i)).ToList();
        var target = result.Single(i => i.Calls(Finder));
        target.operand = AccessTools.Method(typeof(Probes), nameof(Original)).MakeGenericMethod(Finder.GetGenericArguments());
        return result;
    }
}
