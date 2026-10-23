using System.Reflection;
using HarmonyLib;

// Resolve and install the real native hook without invoking Unity. Visual/page-rebuild acceptance remains native.
static class ButtonCaptionChecks
{
    internal static void Run(Assembly mod, Assembly game)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); checks++; Console.WriteLine("PASS " + label); }
        var captions = mod.GetType("CityPerformance.OptionButtonCaptions", true);
        object Call(string name, params object[] args) => AccessTools.Method(captions, name).Invoke(null, args);
        var hook = (MethodBase)Call("ResolveHook");
        Check(hook.DeclaringType == game.GetType("BigAmbitions.ModsInternal.ModOptionsButtonControl", true)
            && hook.GetParameters().Single().ParameterType.FullName == "BigAmbitions.Mods.ModOption", "native option button initializer resolves");
        const string owner = "cityperformance.ui.buttoncaptions";
        try
        {
            Call("Start");
            Check(Harmony.GetPatchInfo(hook)?.Postfixes.Count(p => p.owner == owner) == 1, "one caption postfix installs under its own presentation owner");
            var api = Assembly.Load("BigAmbitions.ModAPI");
            var buttonType = api.GetType("BigAmbitions.Mods.ButtonOption", true);
            var foreign = Activator.CreateInstance(buttonType, new object[] { "cityperformance:export", (Action)(() => { }) });
            // A null UI makes either scope regression fail before it can affect another mod's button.
            Call("AfterInitialize", null, foreign);
            Check(!(bool)AccessTools.Field(captions, "warned").GetValue(null), "another assembly's identical label returns without accessing Unity");
            var callback = (Action)AccessTools.Method(mod.GetType("CityPerformance.FrameTrace", true), "RequestExport").CreateDelegate(typeof(Action));
            var unrelated = Activator.CreateInstance(buttonType, new object[] { "unrelated", callback });
            Call("AfterInitialize", null, unrelated);
            Call("AfterInitialize", null, null);
            Check(!(bool)AccessTools.Field(captions, "warned").GetValue(null), "unrelated own labels and non-button options return without accessing Unity");
            var localePath = Path.Combine(Path.GetDirectoryName(mod.Location), "Locales", "en.json");
            // Packaged locale is supplied beside the DLL by tools/build.sh.
            var locale = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localePath));
            string key = (string)AccessTools.Field(captions, "ExportKey").GetRawConstantValue();
            Check(locale.RootElement.GetProperty(key).GetString() == "Export"
                && locale.RootElement.GetProperty("cityperformance:export").GetString() == "Export current trace (background CSV write)",
                "package has a short caption and retains the descriptive row title");
        }
        finally { Call("Stop"); }
        Check(Harmony.GetPatchInfo(hook)?.Owners.Contains(owner) != true, "caption hook withdraws on unload");
        Console.WriteLine($"ALL {checks} BUTTON CAPTION CHECKS PASSED (offline; native visuals, language changes and page rebuilds unverified)");
    }
}
