using System;
using System.Reflection;
using BigAmbitions.Mods;
using BigAmbitions.ModsInternal;
using HarmonyLib;
using Localizor.LanguageChangeEvent;

namespace CityPerformance
{
    // Build 3682 initializes the descriptive row label and callback, leaving the button's TMP caption unchanged.
    // Apply after every native initialization, including options-page rebuilds, and only to our export action.
    static class OptionButtonCaptions
    {
        internal const string ExportKey = "cityperformance:button_export";
        static readonly Harmony harmony = new Harmony("cityperformance.ui.buttoncaptions");
        static bool warned;

        internal static MethodBase ResolveHook()
        {
            return typeof(ModOptionsButtonControl).GetMethod("Initialize", new[] { typeof(ModOption) });
        }

        internal static void Start()
        {
            harmony.Patch(ResolveHook(), postfix: new HarmonyMethod(typeof(OptionButtonCaptions), nameof(AfterInitialize)));
        }

        internal static void Stop() { harmony.UnpatchAll(harmony.Id); warned = false; }

        static void AfterInitialize(ModOptionsButtonControl __instance, ModOption option)
        {
            var button = option as ButtonOption;
            if (button == null || button.Label != "cityperformance:export" || button.OnClick == null
                || button.OnClick.Method.DeclaringType?.Assembly != typeof(PerformanceMod).Assembly) return;
            try
            {
                // Keep the descriptive row label intact; this is the caption inside the instantiated button.
                var caption = __instance.transform.Find("ButtonContainer/Button/Text (TMP)");
                if (caption == null) { Warn("the native button caption was not found"); return; }
                var localized = caption.GetComponent<TextLocalizationComponent>()
                    ?? caption.gameObject.AddComponent<TextLocalizationComponent>();
                // SetData refreshes reused controls even if the key is unchanged and clears prefab formatting.
                // Localization owns subsequent language changes and re-enables, so it cannot restore the placeholder.
                localized.SetData(new LanguageChangeEventDataHolder { Key = ExportKey });
            }
            catch (Exception ex) { Warn(ex.Message); }
        }

        static void Warn(string message)
        {
            if (warned) return;
            warned = true;
            PerformanceMod.Log("Button captions unavailable: " + message);
        }
    }
}
