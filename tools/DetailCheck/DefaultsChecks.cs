using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

// Verify the actual registered options and compiled startup fallbacks agree, without Unity preferences.
static class DefaultsChecks
{
    internal static void Run(Assembly mod, Action<bool, string> check)
    {
        var expected = new Dictionary<string, bool> {
            ["refill"] = true, ["zones"] = true, ["trees"] = true,
            ["hyperrealdiscovery"] = true, ["bakeryhelp"] = true,
            ["cinematic"] = true, ["cameraaudio"] = true,
            ["cleanup"] = true, ["backgroundsaves"] = true,
            ["diagnostics"] = false, ["trace"] = false,
            ["autoexport"] = false, ["details"] = false
        };
        var type = mod.GetType("CityPerformance.PerformanceMod", true);
        var instance = Activator.CreateInstance(type);
        const string id = "defaults-check";
        AccessTools.Field(type, "modId").SetValue(instance, id);
        AccessTools.Method(type, "RegisterOptions").Invoke(instance, null);
        var service = Assembly.Load("BigAmbitions.ModAPI").GetType("BigAmbitions.Mods.OptionsService", true);
        var entries = (IDictionary)AccessTools.Field(service, "Entries").GetValue(null);
        var page = entries[id];
        var options = ((IEnumerable)page.GetType().GetProperty("Options").GetValue(page)).Cast<object>()
            .Where(o => o.GetType().Name == "ToggleOption").ToArray();
        check(options.Length == expected.Count, "all release toggles are covered by the defaults contract");
        foreach (var option in options)
        {
            string key = (string)option.GetType().GetProperty("Id").GetValue(option);
            bool value = (bool)option.GetType().GetProperty("DefaultValue").GetValue(option);
            check(expected.TryGetValue(key, out bool wanted) && value == wanted, "registered default: " + key + "=" + value);
        }
        var fallbacks = new Dictionary<string, bool>();
        foreach (var t in mod.GetTypes().Where(t => t == type || t.DeclaringType == type))
        foreach (var method in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.GetMethodBody() == null) continue;
            var il = PatchProcessor.GetOriginalInstructions(method);
            for (int i = 2; i < il.Count; i++)
                if (il[i].operand is MethodInfo call && call.DeclaringType == type && call.Name == "Pref")
                {
                    check(il[i-2].opcode == OpCodes.Ldstr && (il[i-1].opcode == OpCodes.Ldc_I4_0 || il[i-1].opcode == OpCodes.Ldc_I4_1), "startup fallback has an explicit boolean");
                    fallbacks.Add((string)il[i-2].operand, il[i-1].opcode == OpCodes.Ldc_I4_1);
                }
        }
        check(fallbacks.Count == expected.Count && expected.All(e => fallbacks.TryGetValue(e.Key, out bool v) && v == e.Value), "startup fallbacks match every registered release default");
        entries.Remove(id);
    }
}
