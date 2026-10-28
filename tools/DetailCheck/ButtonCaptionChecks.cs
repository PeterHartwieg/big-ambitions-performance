using System.Reflection;
using HarmonyLib;

// Resolve and install the real native hook without invoking Unity. Visual/page-rebuild acceptance remains native.
static class ButtonCaptionChecks
{
    static readonly InvalidOperationException TeardownError = new InvalidOperationException("injected early teardown failure");
    static readonly List<string> Warnings = new List<string>();
    static MethodBase CaptionHook;
    static bool OptionsRemovedWithCaptions;
    public static void Noop() { }
    public static bool SkipCleanup() { return false; }
    public static bool CaptureWarning(string text) { Warnings.Add(text); return false; }
    public static bool RemoveOptions()
    {
        OptionsRemovedWithCaptions = Harmony.GetPatchInfo(CaptionHook)?.Owners.Contains("cityperformance.ui.buttoncaptions") == true;
        return false;
    }
    public static void FailTeardown() { throw TeardownError; }
    const string ForeignKey = "foreign:caption";
    static FieldInfo KeyField;
    static object WriterCaption, CloneCaption;
    static readonly List<(string Key, bool Enabled)> CaptionWrites = new List<(string Key, bool Enabled)>();
    public static bool SkipOriginal() { return false; }
    // A foreign caption owner writing its own key on the same control (e.g. HART Mod Pack's identical hook).
    public static void ForeignCaption() { if (WriterCaption != null) KeyField.SetValue(WriterCaption, ForeignKey); }
    public static bool SubstituteExistingCaption(ref object __result) { __result = CloneCaption; return false; }
    public static bool SubstituteSetCaption(object localized, object data, bool enabled)
    {
        string key = (string)AccessTools.Field(data.GetType(), "Key").GetValue(data);
        KeyField.SetValue(localized, key);
        CaptionWrites.Add((key, enabled));
        return false;
    }

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
        var performance = mod.GetType("CityPerformance.PerformanceMod", true);
        Warnings.Clear();
        CaptionHook = hook;
        OptionsRemovedWithCaptions = false;
        var foreignOwner = new Harmony("cityperformance.caption.check.foreign");
        try
        {
            foreignOwner.Patch(AccessTools.Method(performance, "Log"),
                prefix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(CaptureWarning)));
            foreignOwner.Patch(hook, postfix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(Noop)));
            Call("Start");
            Check(Harmony.GetPatchInfo(hook)?.Postfixes.Count(p => p.owner == owner) == 1, "one caption postfix installs under its own presentation owner");
            var api = Assembly.Load("BigAmbitions.ModAPI");
            var buttonType = api.GetType("BigAmbitions.Mods.ButtonOption", true);
            var foreign = Activator.CreateInstance(buttonType, new object[] { "cityperformance:export", (Action)(() => { }) });
            // A null UI makes either scope regression fail before it can affect another mod's button.
            Call("AfterInitialize", null, foreign);
            Check(!(bool)AccessTools.Field(captions, "warned").GetValue(null), "another assembly's identical label returns without captioning");
            var callback = (Action)AccessTools.Method(mod.GetType("CityPerformance.FrameTrace", true), "RequestExport").CreateDelegate(typeof(Action));
            var unrelated = Activator.CreateInstance(buttonType, new object[] { "unrelated", callback });
            Call("AfterInitialize", null, unrelated);
            Check(!(bool)AccessTools.Field(captions, "warned").GetValue(null), "unrelated own labels return without captioning");
            var ownButton = Activator.CreateInstance(buttonType, new object[] { "cityperformance:export", callback });
            Call("AfterInitialize", null, ownButton);
            Check((bool)AccessTools.Field(captions, "warned").GetValue(null), "own callback and real label reach the caption path and warn for a null control");
            var messages = (System.Collections.Generic.HashSet<string>)AccessTools.Field(captions, "warnedMessages").GetValue(null);
            Call("AfterInitialize", null, ownButton);
            Call("Warn", "a different caption failure");
            Call("Warn", "a different caption failure");
            Check(messages.Count == 2 && Warnings.Count == 2, "each distinct failure logs exactly once");
            string exportKey = (string)AccessTools.Field(captions, "ExportKey").GetRawConstantValue();
            // Drive the real hook with a foreign caption writer; only the native body and two Unity boundaries are substituted.
            var writer = new Harmony("cityperformance.caption.check.writer");
            try
            {
                var unityObject = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object", true);
                var componentType = Assembly.Load("HGPlugins").GetType("Localizor.LanguageChangeEvent.TextLocalizationComponent", true);
                KeyField = AccessTools.Field(componentType, "key");
                object Alive(Type type)
                {
                    var o = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                    AccessTools.Field(unityObject, "m_CachedPtr").SetValue(o, (IntPtr)1);
                    return o;
                }
                writer.Patch(hook, prefix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(SkipOriginal)));
                writer.Patch(AccessTools.Method(captions, "SetCaption"), prefix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(SubstituteSetCaption)));
                writer.Patch(AccessTools.Method(captions, "ExistingCaption"), prefix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(SubstituteExistingCaption)));
                var table = AccessTools.Field(captions, "mutated").GetValue(null);
                var stateType = captions.GetNestedType("CaptionState", BindingFlags.NonPublic);
                bool Tracked(object control) => (bool)AccessTools.Method(table.GetType(), "TryGetValue").Invoke(table, new[] { control, null });
                var foreignButton = Activator.CreateInstance(buttonType, new object[] { "foreign:label", (Action)Noop });
                var writerMethod = AccessTools.Method(typeof(ButtonCaptionChecks), nameof(ForeignCaption));
                int warningCount = Warnings.Count;
                foreach (bool foreignFirst in new[] { true, false })
                {
                    var control = Alive(hook.DeclaringType);
                    var component = Alive(componentType);
                    KeyField.SetValue(component, exportKey);
                    var state = Activator.CreateInstance(stateType, true);
                    AccessTools.Field(stateType, "Localized").SetValue(state, component);
                    AccessTools.Field(stateType, "Text").SetValue(state, "Your Text Here");
                    AccessTools.Method(table.GetType(), "Add").Invoke(table, new[] { control, state });
                    writer.Patch(hook, postfix: new HarmonyMethod(writerMethod) { priority = foreignFirst ? Priority.First : Priority.Last });
                    WriterCaption = component;
                    CaptionWrites.Clear();
                    try { hook.Invoke(control, new[] { foreignButton }); }
                    finally { WriterCaption = null; writer.Unpatch(hook, writerMethod); }
                    bool kept = (string)KeyField.GetValue(component) == ForeignKey && !Tracked(control);
                    Check(foreignFirst ? kept && CaptionWrites.Count == 0
                        : kept && CaptionWrites.Count == 1 && CaptionWrites[0] == (null, false),
                        foreignFirst ? "a rebound control keeps a caption written before ours and drops our tracking"
                            : "a rebound control restores ours first and keeps a later foreign caption");
                }
                foreach (string existing in new[] { exportKey, ForeignKey })
                {
                    var control = Alive(hook.DeclaringType);
                    var component = Alive(componentType);
                    KeyField.SetValue(component, existing);
                    CloneCaption = component;
                    CaptionWrites.Clear();
                    try { hook.Invoke(control, new[] { foreignButton }); }
                    finally { CloneCaption = null; }
                    Check(existing == exportKey
                        ? (string)KeyField.GetValue(component) == null && CaptionWrites.Count == 1 && CaptionWrites[0] == (null, false)
                        : (string)KeyField.GetValue(component) == ForeignKey && CaptionWrites.Count == 0,
                        existing == exportKey ? "an untracked clone of our row has our caption cleared and disabled"
                            : "an untracked foreign caption is left untouched");
                }
                Check(Warnings.Count == warningCount, "rebound and cloned controls log no caption failure");
            }
            finally { writer.UnpatchAll(writer.Id); }
            var localePath = Path.Combine(Path.GetDirectoryName(mod.Location), "Locales", "en.json");
            // Packaged locale is supplied beside the DLL by tools/build.sh.
            var locale = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localePath));
            Check(locale.RootElement.GetProperty(exportKey).GetString() == "Export"
                && locale.RootElement.GetProperty("cityperformance:export").GetString() == "Export current trace (background CSV write)",
                "package has a short caption and retains the descriptive row title");
            var shutdown = AccessTools.Method(mod.GetType("CityPerformance.HyperrealDiscovery", true), "Shutdown");
            foreignOwner.Patch(shutdown, prefix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(FailTeardown)));
            var instance = Activator.CreateInstance(performance);
            try
            {
                AccessTools.Method(performance, "OnUnloadAsync").Invoke(instance, null);
                throw new Exception("FAIL teardown exception was lost");
            }
            catch (TargetInvocationException e)
            {
                Check(ReferenceEquals(e.InnerException, TeardownError), "real unload preserves an earlier teardown exception");
            }
            Check(Harmony.GetPatchInfo(hook)?.Owners.Contains(owner) != true
                && Harmony.GetPatchInfo(hook)?.Owners.Contains(foreignOwner.Id) == true,
                "real unload withdraws captions despite teardown failure and preserves the unrelated owner");
            Check(!(bool)AccessTools.Field(captions, "warned").GetValue(null) && messages.Count == 0,
                "real unload clears the warning latch and distinct messages");

            // Isolate native teardown calls to check ordering through the normal unload entry point.
            Call("Start");
            foreignOwner.Unpatch(shutdown, AccessTools.Method(typeof(ButtonCaptionChecks), nameof(FailTeardown)));
            foreach (var (type, method) in new[]
            {
                ("HyperrealDiscovery", "Shutdown"), ("BakeryHelp", "Shutdown"), ("BackgroundSaves", "Stop"),
                ("Refill", "Flush"), ("CityEntry", "Stop"), ("MidnightCleanup", "Shutdown"), ("DetailedTrace", "Shutdown")
            })
                foreignOwner.Patch(AccessTools.Method(mod.GetType("CityPerformance." + type, true), method),
                    prefix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(SkipCleanup)));
            foreignOwner.Patch(AccessTools.Method(api.GetType("BigAmbitions.Mods.OptionsService", true), "RemoveModOptions"),
                prefix: new HarmonyMethod(typeof(ButtonCaptionChecks), nameof(RemoveOptions)));
            AccessTools.Field(performance, "modId").SetValue(instance, "caption.check");
            var unload = (System.Threading.Tasks.Task)AccessTools.Method(performance, "OnUnloadAsync").Invoke(instance, null);
            unload.GetAwaiter().GetResult();
            Check(OptionsRemovedWithCaptions, "normal unload keeps captions installed until options removal");
            Check(Harmony.GetPatchInfo(hook)?.Owners.Contains(owner) != true
                && Harmony.GetPatchInfo(hook)?.Owners.Contains(foreignOwner.Id) == true,
                "normal unload then removes only the caption owner");
        }
        finally { Call("Stop"); foreignOwner.UnpatchAll(foreignOwner.Id); }
        Console.WriteLine($"ALL {checks} BUTTON CAPTION CHECKS PASSED (offline; native visuals, language changes and page rebuilds unverified)");
    }
}
