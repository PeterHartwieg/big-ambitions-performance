using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;

// Runs the native named-argument localizer against the packaged slider labels, without starting Unity.
static class SliderLabelChecks
{
    internal static void Run(Assembly mod, Assembly game)
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("FAIL " + label);
            checks++;
            Console.WriteLine("PASS " + label);
        }
        var api = Assembly.Load("BigAmbitions.ModAPI");
        var service = api.GetType("BigAmbitions.Mods.OptionsService", true);
        var entries = (IDictionary)AccessTools.Field(service, "Entries").GetValue(null);
        var performance = mod.GetType("CityPerformance.PerformanceMod", true);
        var instance = Activator.CreateInstance(performance);
        const string modId = "cityperformance.check.sliderlabels";
        AccessTools.Field(performance, "modId").SetValue(instance, modId);

        var localizer = Assembly.Load("HGPlugins").GetType("Localizor.LocalizorManager", true);
        // Supply only in-memory tables; skip Resources/PlayerPrefs and all native Unity UI calls.
        var fields = new[] { "Initialized", "LoadedLocaleValue", "FallbackLanguage", "Mode" }
            .Select(name => AccessTools.Field(localizer, name)).ToArray();
        var previous = fields.Select(field => field.GetValue(null)).ToArray();
        var tables = (IDictionary)AccessTools.Field(localizer, "LocalizationDictionary").GetValue(null);
        var previousTables = tables.Keys.Cast<object>().Select(key => new DictionaryEntry(key, tables[key])).ToArray();
        try
        {
            AccessTools.Method(performance, "RegisterOptions").Invoke(instance, null);
            var options = (IEnumerable)AccessTools.Property(entries[modId].GetType(), "Options").GetValue(entries[modId]);
            var sliders = options.Cast<object>().Where(option => option.GetType().Name == "SliderOption").ToArray();
            Check(sliders.Length == 2, "real option registration supplies both sliders");
            using var locale = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(mod.Location), "Locales", "en.json")));
            var english = locale.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString());
            tables.Clear();
            tables.Add("en", english);
            tables.Add("de", new Dictionary<string, string>());
            fields[0].SetValue(null, true);
            fields[1].SetValue(null, "en");
            fields[2].SetValue(null, "en");
            fields[3].SetValue(null, Enum.ToObject(fields[3].FieldType, 0));
            var getLocalization = localizer.GetMethods().Single(method => method.Name == "GetLocalization"
                && method.GetParameters().Length == 3 && method.GetParameters()[1].ParameterType == typeof(object));

            var control = game.GetType("BigAmbitions.ModsInternal.ModOptionsSliderControl", true);
            var initialize = AccessTools.Method(control, "Initialize");
            // Use the actual argument constructors emitted for initialization and value-change callbacks.
            var methods = new[] { initialize }.Concat(control.GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)));
            var factories = methods.SelectMany(method => PatchProcessor.GetOriginalInstructions(method))
                .Select(instruction => instruction.operand).OfType<ConstructorInfo>()
                .Where(ctor => ctor.DeclaringType.GetProperty("value")?.PropertyType == typeof(int)).ToArray();
            Check(factories.Length == 2, "native initialization and value-change paths both supply named integer value arguments");
            foreach (var slider in sliders)
            {
                object Read(string name) => AccessTools.Property(slider.GetType(), name).GetValue(slider);
                string id = (string)Read("Id"), key = (string)Read("ValueLabelKey");
                Check(id == "cinematicdistance" && key == "cityperformance:percent"
                    || id == "cleanupminutes" && key == "cityperformance:minutes", id + " selects its packaged value label");
                var values = new[] { (int)Read("Min"), (int)Read("DefaultValue"), (int)Read("Max"), id == "cinematicdistance" ? 175 : 7 };
                foreach (string language in new[] { "en", "de" })
                {
                    fields[1].SetValue(null, language);
                    foreach (int value in values)
                    {
                        string expected = value + (id == "cinematicdistance" ? "%" : " min");
                        Check(factories.All(factory => (string)getLocalization.Invoke(null,
                            new object[] { key, factory.Invoke(new object[] { value }), null }) == expected),
                            $"{id} {language}: initial and changed value render as {expected}");
                    }
                }
            }
        }
        finally
        {
            entries.Remove(modId);
            tables.Clear();
            foreach (var table in previousTables) tables.Add(table.Key, table.Value);
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, previous[i]);
        }
        Console.WriteLine($"ALL {checks} SLIDER LABEL CHECKS PASSED (offline; native UI appearance and interaction unverified)");
    }
}
