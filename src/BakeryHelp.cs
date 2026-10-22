using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityPerformance
{
    // Bakery owns the pages; this feature only avoids discovery/redundant rebuild work.
    internal static class BakeryHelp
    {
        internal const string Owner = PerformanceMod.Id + ".bakeryhelp";
        private const string BakeryHash = "6d68edab0f5600f51b912fb5228ac7fdc245e7962f8be968670715b2e60fee3a";
        private static readonly Harmony harmony = new Harmony(Owner);
        internal static bool Enabled = true;
        private static bool initialized, inCity, installed, active, blocked, removeFailed, rewritten;
        private static int pending;
        private static string status, inactiveReason;
        private static MethodInfo tryApply, refresh, loadCategories;
        private static Func<MonoBehaviour, bool, bool> applyHelp;
        private static MonoBehaviour buildingHelp;
        private static readonly Object[] empty = Array.Empty<Object>();
        private static MethodBase[] boundaries = Array.Empty<MethodBase>();
        private static readonly MethodInfo finder = AccessTools.DeclaredMethod(typeof(Resources), "FindObjectsOfTypeAll", new[] { typeof(Type) });
        private static readonly MethodInfo lookup = AccessTools.DeclaredMethod(typeof(BakeryHelp), nameof(FindHelp));
        private static readonly MethodInfo rewrite = AccessTools.DeclaredMethod(typeof(BakeryHelp), nameof(Rewrite));
        private static readonly MethodInfo refreshPrefix = AccessTools.DeclaredMethod(typeof(BakeryHelp), nameof(RefreshPrefix));
        private static readonly MethodInfo buildPrefix = AccessTools.DeclaredMethod(typeof(BakeryHelp), nameof(BuildPrefix));

        internal static void Initialize()
        {
            Shutdown(); initialized = true; blocked = removeFailed;
            AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
            Status(blocked ? "inactive: " + inactiveReason : "waiting for city-ready; requested=" + Enabled);
        }
        private static void AssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            try { if (args.LoadedAssembly.GetName().Name == "Bakery") Interlocked.Exchange(ref pending, 1); }
            catch { /* Main-thread city-ready retries optional discovery. */ }
        }
        internal static void StartCity() { inCity = true; TryInstall(); }
        internal static void EndCity() { inCity = false; Remove(); }
        internal static void Shutdown()
        {
            initialized = false; inCity = false;
            AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            Interlocked.Exchange(ref pending, 0); Remove();
        }
        internal static void Tick()
        {
            if (blocked && installed && !removeFailed) Remove();
            if (Interlocked.Exchange(ref pending, 0) != 0) TryInstall();
        }
        internal static void SetEnabled(bool value)
        {
            if (Enabled == value) return;
            Enabled = value;
            if (!value) { Remove(); if (!removeFailed) Status("off"); }
            else if (blocked) Status("inactive: " + inactiveReason + "; reload required");
            else if (!inCity) Status("waiting for city-ready; requested=true");
            else TryInstall();
        }
        private static void TryInstall()
        {
            if (!initialized || !inCity || !Enabled || installed || blocked) return;
            try
            {
                var candidates = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Bakery").ToArray();
                if (candidates.Length == 0) { Status("Bakery absent; original Help discovery"); return; }
                if (candidates.Length != 1) { Block("ambiguous Bakery assemblies"); return; }
                if (Digest(candidates[0]) != BakeryHash ||
                    Digest(typeof(HelpSystem).Assembly) != "9427561b81aaab2ada09c22a3c98dd9a4017a65225b389513885bf336852e911" ||
                    Digest(typeof(Object).Assembly) != "5afc743d350e95dc30379a6d2bafa2293b63259acf196fe3f7370f03644ef6f4" ||
                    Digest(typeof(InstanceBehavior<>).Assembly) != "fafddfadf7aca598343b4679d14bc4e1381153be9a54bf94f35daa6b49b27b38")
                { Block("unreviewed Bakery/game/Core/HGExtensions binary"); return; }
                var navigation = candidates[0].GetType("BakeryHelpNavigationPatch", true);
                tryApply = AccessTools.DeclaredMethod(navigation, "TryApply", new[] { typeof(MonoBehaviour), typeof(bool) });
                refresh = AccessTools.DeclaredMethod(navigation, "RefreshGeneratedNavigation", new[] { typeof(MonoBehaviour), typeof(Type), typeof(MonoBehaviour) });
                loadCategories = AccessTools.DeclaredMethod(typeof(HelpSystem), "LoadCategories", Type.EmptyTypes);
                if (tryApply == null || !tryApply.IsStatic || tryApply.ReturnType != typeof(bool) ||
                    refresh == null || !refresh.IsStatic || refresh.ReturnType != typeof(bool) ||
                    loadCategories == null || loadCategories.IsStatic || loadCategories.ReturnType != typeof(void))
                { Block("Help signature changed"); return; }
                var singleton = typeof(InstanceBehavior<HelpSystem>);
                var lifecycle = singleton.Assembly.GetType("Extensions.SingletonLifecycle", true);
                boundaries = navigation.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Cast<MethodBase>().Concat(new[] { loadCategories, finder })
                    .Concat(singleton.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    .Concat(lifecycle.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).Distinct().ToArray();
                var conflict = Conflict(); if (conflict != null) { Block(conflict); return; }
                if (!CanRewrite(DetailedTrace.Instructions(tryApply))) { Block("Help finder IL changed"); return; }
                applyHelp = (Func<MonoBehaviour, bool, bool>)Delegate.CreateDelegate(typeof(Func<MonoBehaviour, bool, bool>), tryApply);
                rewritten = false;
                harmony.Patch(tryApply, transpiler: new HarmonyMethod(rewrite) { priority = Priority.Last });
                harmony.Patch(refresh, prefix: new HarmonyMethod(refreshPrefix));
                harmony.Patch(loadCategories, prefix: new HarmonyMethod(buildPrefix));
                installed = HasAnyPatch();
                if (!rewritten || blocked || !HasPatch(tryApply, rewrite) || !HasPatch(refresh, refreshPrefix) || !HasPatch(loadCategories, buildPrefix))
                { Block("incomplete Help hook installation"); Remove(); return; }
                active = true;
                Status("active; registered Help lookup and scoped page build; pricing belongs to Hard Mode Patch");
                PopulateExisting();
            }
            catch (Exception e) { Block("installation failed: " + e.GetType().Name + ": " + e.Message); Remove(); }
        }
        private static string Digest(Assembly assembly)
        {
            using (var hash = SHA256.Create())
            using (var file = File.OpenRead(assembly.Location))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        private static bool CanRewrite(IEnumerable<CodeInstruction> instructions) => instructions.Count(i => i.Calls(finder)) == 1;
        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (blocked) return code;
            if (!CanRewrite(code)) { Block("Help finder changed during composition; composed body retained"); return code; }
            var result = code.Select(i => new CodeInstruction(i)).ToArray();
            result.Single(i => i.Calls(finder)).operand = lookup;
            rewritten = true;
            return result;
        }
        private static Object[] FindHelp(Type type)
        {
            if (type != typeof(HelpSystem) || !CanOptimize()) return Resources.FindObjectsOfTypeAll(type);
            var help = buildingHelp;
            // IsInitialized uses searchIfMissing:false. Never call Instance on a miss.
            if (help == null && InstanceBehavior<HelpSystem>.IsInitialized) help = InstanceBehavior<HelpSystem>.Instance;
            // A fresh one-element array avoids a nested caller overwriting a retained result.
            return help == null ? empty : new Object[] { help };
        }
        private static void BuildPrefix(HelpSystem __instance)
        {
            if (!CanOptimize() || buildingHelp != null || __instance == null) return;
            buildingHelp = __instance;
            try { applyHelp(__instance, false); }
            catch (Exception e) { Block("page insertion failed; original Help still builds: " + e.GetType().Name); }
            finally { buildingHelp = null; }
        }
        private static bool RefreshPrefix(ref bool __result)
        {
            if (buildingHelp == null || !CanOptimize()) return true;
            __result = false; return false;
        }
        private static void PopulateExisting()
        {
            if (!CanOptimize()) return;
            try
            {
                if (!InstanceBehavior<HelpSystem>.IsInitialized) return;
                var help = InstanceBehavior<HelpSystem>.Instance;
                if (help != null) applyHelp(help, false);
            }
            catch (Exception e) { Block("existing Help population failed: " + e.GetType().Name); }
        }
        private static bool CanOptimize()
        {
            if (!initialized || !inCity || !Enabled || !active || blocked) return false;
            try { var conflict = Conflict(); if (conflict == null) return true; Block(conflict); }
            catch (Exception e) { Block("conflict inspection failed: " + e.GetType().Name); }
            return false;
        }
        private static bool IsOwn(MethodBase method, Patch patch) => patch.owner == Owner &&
            (Equals(method, tryApply) && Equals(patch.PatchMethod, rewrite) ||
             Equals(method, refresh) && Equals(patch.PatchMethod, refreshPrefix) ||
             Equals(method, loadCategories) && Equals(patch.PatchMethod, buildPrefix));
        private static string Conflict()
        {
            foreach (var method in boundaries)
            {
                var info = Harmony.GetPatchInfo(method); if (info == null) continue;
                if (info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Any(p => !IsOwn(method, p)))
                    return "conflicting hook: " + method.DeclaringType.FullName + "." + method.Name;
            }
            foreach (var method in Harmony.GetAllPatchedMethods().OfType<MethodInfo>())
                if (method.DeclaringType != null && method.DeclaringType.IsGenericType &&
                    method.DeclaringType.GetGenericTypeDefinition() == typeof(InstanceBehavior<>) && Harmony.GetPatchInfo(method)?.Owners.Count > 0)
                    return "conflicting shared singleton hook: " + method.Name;
            return null;
        }
        private static bool HasPatch(MethodInfo method, MethodInfo patch)
        {
            var info = method == null ? null : Harmony.GetPatchInfo(method);
            return info != null && info.Prefixes.Concat(info.Transpilers).Any(p => p.owner == Owner && Equals(p.PatchMethod, patch));
        }
        private static bool HasAnyPatch() => HasPatch(tryApply, rewrite) || HasPatch(refresh, refreshPrefix) || HasPatch(loadCategories, buildPrefix);
        private static void Remove()
        {
            active = false; buildingHelp = null; applyHelp = null;
            bool failed = false;
            foreach (var pair in new[] { (tryApply, rewrite), (refresh, refreshPrefix), (loadCategories, buildPrefix) })
                try { if (HasPatch(pair.Item1, pair.Item2)) harmony.Unpatch(pair.Item1, pair.Item2); }
                catch (Exception e) { failed = true; Block("unpatch failed: " + e.GetType().Name); }
            try { installed = HasAnyPatch(); }
            catch (Exception e) { installed = true; failed = true; Block("unpatch inspection failed: " + e.GetType().Name); }
            removeFailed = failed || installed;
            if (installed && !failed) Block("unpatch did not remove all Help hooks");
        }
        private static void Block(string reason) { active = false; blocked = true; inactiveReason = reason; Status("inactive: " + reason); }
        private static void Status(string value)
        {
            if (status == value) return;
            status = value; PerformanceMod.Log("Bakery Help: " + value);
        }
    }
}
