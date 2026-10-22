using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityPerformance
{
    // Optional integration: no Hyperreal assembly reference or persistent scene-object cache.
    internal static class HyperrealDiscovery
    {
        internal const string Owner = PerformanceMod.Id + ".hyperreal";
        internal const string HyperrealHash = "7d1bd589947558e2f3cc476f06d4699618732835718ffd2335f2e5102cfcef10";
        private static readonly Harmony harmony = new Harmony(Owner);
        internal static bool Enabled = true;
        private static bool initialized, inCity, installed, active, blocked, rewritten, removeFailed;
        private static int pending;
        private static string status, inactiveReason;
        private static MethodInfo scan, originalFinder, replacement;
        private static MethodBase[] boundaries = Array.Empty<MethodBase>();
        private static readonly MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(HyperrealDiscovery), nameof(Rewrite));

        internal static void Initialize()
        {
            Shutdown();
            initialized = true;
            blocked = removeFailed;
            AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
            // Wait until city-ready: allow existing mod initialization (including HMP) to finish first.
            if (blocked) Status("inactive: " + inactiveReason + "; reload required");
            else Status("waiting for city-ready; requested=" + Enabled);
        }

        private static void AssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            // Loader notifications may run off the main thread. Never patch or call Unity here.
            try
            {
                var name = args.LoadedAssembly.GetName().Name;
                if (name == "PolyModsHyperreal" || name == "Unity.RenderPipelines.HighDefinition.Runtime")
                    Interlocked.Exchange(ref pending, 1);
            }
            catch { /* A loader notification is only a hint; city-ready also retries discovery. */ }
        }

        internal static void StartCity()
        {
            inCity = true;
            TryInstall();
        }

        internal static void EndCity()
        {
            inCity = false;
            Remove();
        }

        internal static void SetEnabled(bool value)
        {
            Enabled = value;
            if (!value) { Remove(); if (!removeFailed) Status("off"); }
            else if (blocked) Status("inactive: " + inactiveReason + "; reload required");
            else if (!inCity) Status("waiting for city-ready; requested=true");
            else TryInstall();
        }

        internal static void Tick()
        {
            // A failed unpatch is already a pass-through. Retry only at explicit lifecycle boundaries.
            if (blocked && installed && !removeFailed) Remove();
            if (Interlocked.Exchange(ref pending, 0) != 0) TryInstall();
        }

        internal static void Shutdown()
        {
            initialized = false;
            inCity = false;
            AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            Interlocked.Exchange(ref pending, 0);
            Remove();
        }

        private static void TryInstall()
        {
            if (!initialized || !inCity || !Enabled || blocked || installed) return;
            try
            {
                var candidates = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "PolyModsHyperreal").ToArray();
                if (candidates.Length == 0) { Status("Hyperreal absent; original behavior"); return; }
                if (candidates.Length != 1) { Block("ambiguous Hyperreal assemblies"); return; }
                var assembly = candidates[0];
                if (Digest(assembly) != HyperrealHash) { Block("unreviewed Hyperreal binary"); return; }
                var street = assembly.GetType("PolyModsHyperreal.StreetLights", true);
                scan = street.GetMethod("Scan", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (scan == null || scan.ReturnType != typeof(void) || scan.IsGenericMethod)
                { Block("Scan signature changed"); return; }

                var code = DetailedTrace.Instructions(scan).ToList();
                var finders = code.Where(i => IsFinder(i)).Select(i => (MethodInfo)i.operand).ToArray();
                if (finders.Length != 1) { Block("discovery call contract changed"); return; }
                originalFinder = finders[0];
                var light = originalFinder.GetGenericArguments()[0];
                if (light.FullName != "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData" ||
                    !typeof(Component).IsAssignableFrom(light) ||
                    !ReviewedPair(Digest(typeof(Object).Assembly), Digest(light.Assembly)))
                { Block("unreviewed Unity/HDRP binary pair"); return; }

                var byType = typeof(Object).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m =>
                    m.Name == "FindObjectsByType" && m.IsGenericMethodDefinition &&
                    m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(FindObjectsInactive), typeof(FindObjectsSortMode) }));
                replacement = AccessTools.DeclaredMethod(typeof(HyperrealDiscovery), nameof(Discover)).MakeGenericMethod(light);
                if (byType.MakeGenericMethod(light).ReturnType != originalFinder.ReturnType ||
                    replacement.ReturnType != originalFinder.ReturnType ||
                    Convert.ToInt32(typeof(FindObjectsInactive).GetField("Include").GetRawConstantValue()) != 1 ||
                    Convert.ToInt32(typeof(FindObjectsInactive).GetField("Exclude").GetRawConstantValue()) != 0 ||
                    Convert.ToInt32(typeof(FindObjectsSortMode).GetField("None").GetRawConstantValue()) != 0)
                { Block("replacement API contract changed"); return; }

                // Order changes are safe only for the audited consumers and unmodified query boundaries.
                boundaries = street.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Cast<MethodBase>().Concat(street.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    .Concat(typeof(Object).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == "FindObjectsOfType" || m.Name == "FindObjectsByType")
                        .SelectMany(m => m.IsGenericMethodDefinition ? new[] { m, m.MakeGenericMethod(light) } : new[] { m }))
                    .Concat(typeof(Resources).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .Where(m => m.Name == "ConvertObjects" && m.IsGenericMethodDefinition)
                        .SelectMany(m => new[] { m, m.MakeGenericMethod(light) })).Distinct().ToArray();
                string conflict = Conflict();
                if (conflict != null) { Block(conflict); return; }
                if (!CanRewrite(code)) { Block("discovery IL contract changed"); return; }
                rewritten = false;
                // Run after existing transpilers. In particular, HMP must see its original call first.
                harmony.Patch(scan, transpiler: new HarmonyMethod(transpiler) { priority = Priority.Last });
                installed = HasPatch();
                if (!installed || !rewritten || blocked) { Block("transpiler did not activate"); Remove(); return; }
                active = true;
                Status("active; full inactive-inclusive discovery, unsorted; no scene cache");
            }
            catch (Exception e)
            {
                Block("installation failed: " + e.GetType().Name + ": " + e.Message);
                Remove();
            }
        }

        internal static bool ReviewedPair(string core, string hdrp) =>
            core == "5afc743d350e95dc30379a6d2bafa2293b63259acf196fe3f7370f03644ef6f4" &&
            hdrp == "d0f869303996388151aa45aefb90c33147f0ae7961a1d0e0b406015ad5a8dfc6" ||
            core == "ce064ee6dcc296f6108771ab9fe817828a7f3e1aa3af6ae95aea2dd0a2f087d2" &&
            hdrp == "acedb057709b46696572e8a6874e97d8525c1b900db95be2f9d314257e512450";

        private static string Digest(Assembly assembly)
        {
            using (var hash = SHA256.Create())
            using (var file = File.OpenRead(assembly.Location))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        private static bool IsFinder(CodeInstruction i) => i.opcode == OpCodes.Call && i.operand is MethodInfo m &&
            m.DeclaringType == typeof(Object) && m.Name == "FindObjectsOfType" && m.IsGenericMethod &&
            !m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(bool) });

        private static bool CanRewrite(IList<CodeInstruction> code)
        {
            var matches = Enumerable.Range(0, code.Count).Where(i => IsFinder(code[i])).ToArray();
            return matches.Length == 1 && matches[0] > 0 && Equals(code[matches[0]].operand, originalFinder) &&
                code[matches[0] - 1].opcode == OpCodes.Ldc_I4_1 &&
                ((System.Collections.ICollection)typeof(CodeInstruction).GetField("labels").GetValue(code[matches[0]])).Count == 0;
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (blocked) return code;
            if (!CanRewrite(code))
            {
                Block("discovery IL changed during patch composition; original/composed body retained");
                return code;
            }
            var result = code.Select(i => new CodeInstruction(i)).ToArray();
            result.Single(IsFinder).operand = replacement;
            rewritten = true;
            return result;
        }

        private static T[] Discover<T>(bool includeInactive) where T : Object
        {
            // Guard failures select the original call. Exceptions from either native query propagate once.
            // No result cache: new/inactive/destroyed lights retain Hyperreal's discovery timing.
            if (!UseUnsorted()) return Object.FindObjectsOfType<T>(includeInactive);
            return Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        private static bool UseUnsorted()
        {
            if (!Enabled || !initialized || !inCity || !active || blocked) return false;
            try
            {
                var conflict = Conflict(); // Once per original scan (~8s), never per frame/light.
                if (conflict == null) return true;
                Block(conflict);
            }
            catch (Exception e) { Block("conflict inspection failed: " + e.GetType().Name); }
            return false;
        }

        private static string Conflict()
        {
            foreach (var method in boundaries)
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                if (info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                    .Any(p => p.owner != Owner || !Equals(p.PatchMethod, transpiler) || !Equals(method, scan)))
                    return "conflicting hook: " + method.DeclaringType.FullName + "." + method.Name;
            }
            // Mono may share generic code between reference-type instantiations. A hook registered
            // for Light or Renderer is not necessarily listed under HDAdditionalLightData's method.
            foreach (var method in Harmony.GetAllPatchedMethods().OfType<MethodInfo>())
                if (method.IsGenericMethod &&
                    (method.DeclaringType == typeof(Object) && (method.Name == "FindObjectsOfType" || method.Name == "FindObjectsByType") ||
                     method.DeclaringType == typeof(Resources) && method.Name == "ConvertObjects") &&
                    Harmony.GetPatchInfo(method)?.Owners.Count > 0)
                    return "conflicting shared generic query: " + method.DeclaringType.FullName + "." + method.Name;
            return null;
        }

        private static bool HasPatch() => scan != null && Harmony.GetPatchInfo(scan)?.Transpilers
            .Any(p => p.owner == Owner && Equals(p.PatchMethod, transpiler)) == true;

        private static void Remove()
        {
            active = false; // Even failed removal leaves a pass-through helper.
            try
            {
                if (HasPatch()) harmony.Unpatch(scan, transpiler);
                installed = HasPatch();
                removeFailed = installed;
                if (installed) Block("unpatch did not remove transpiler");
            }
            catch (Exception e) { installed = true; removeFailed = true; Block("unpatch failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Block(string reason) { active = false; blocked = true; inactiveReason = reason; Status("inactive: " + reason); }
        private static void Status(string value)
        {
            if (status == value) return;
            status = value;
            PerformanceMod.Log("Hyperreal discovery: " + value);
        }
    }
}
