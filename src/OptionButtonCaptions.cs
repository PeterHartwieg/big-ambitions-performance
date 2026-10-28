using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BigAmbitions.Mods;
using BigAmbitions.ModsInternal;
using HarmonyLib;
using Localizor.LanguageChangeEvent;

namespace CityPerformance
{
    // Build 3682 initializes the row label and callback but leaves the button's prefab caption unchanged.
    // Mirrors HART Mod Pack's src/OptionButtonCaptions.cs (owner id, logger and key mapping differ); mirror fixes there.
    static class OptionButtonCaptions
    {
        internal const string ExportLabelKey = "cityperformance:export";
        internal const string ExportKey = "cityperformance:button_export";
        const string CaptionPath = "ButtonContainer/Button/Text (TMP)";
        static readonly Harmony harmony = new Harmony("cityperformance.ui.buttoncaptions");
        static readonly ConditionalWeakTable<ModOptionsButtonControl, CaptionState> mutated = new ConditionalWeakTable<ModOptionsButtonControl, CaptionState>();
        static readonly HashSet<string> warnedMessages = new HashSet<string>(StringComparer.Ordinal);
#pragma warning disable CS0414 // Read by tools/DetailCheck.
        static bool warned;
#pragma warning restore CS0414

        sealed class CaptionState
        {
            internal TextLocalizationComponent Localized;
            internal LanguageChangeEventDataHolder Data;
            internal string Text;
            internal bool Enabled;

            internal void Restore()
            {
                // Another caption owner may already have rebound this component; only revert our own caption.
                if (Localized == null || Localized.Key != ExportKey) return;
                // Leave an added game component disabled: Destroy is deferred and a same-frame rebind may need it.
                SetCaption(Localized, Data, Text, Enabled);
            }
        }

        internal static MethodBase ResolveHook()
        {
            return typeof(ModOptionsButtonControl).GetMethod("Initialize", new[] { typeof(ModOption) });
        }

        internal static void Start()
        {
            harmony.Patch(ResolveHook(), postfix: new HarmonyMethod(typeof(OptionButtonCaptions), nameof(AfterInitialize)));
        }

        internal static void Stop() { harmony.UnpatchAll(harmony.Id); warnedMessages.Clear(); warned = false; }

        // Keep localizer/TMP types out of this method: a changed API then fails inside the try, not at the hook call.
        static void AfterInitialize(ModOptionsButtonControl __instance, ModOption option)
        {
            try { Caption(__instance, option); }
            catch (Exception ex) { Warn(ex.GetType().Name + ": " + ex.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Caption(ModOptionsButtonControl __instance, ModOption option)
        {
            var button = option as ButtonOption;
            string key = button != null && button.OnClick != null
                && button.OnClick.Method.DeclaringType?.Assembly == typeof(PerformanceMod).Assembly
                ? (button.Label == ExportLabelKey ? ExportKey : null) : null;
            if (key == null)
            {
                // A pooled control can be rebound to a foreign, unrelated or non-button option.
                if (__instance == null) return;
                if (mutated.TryGetValue(__instance, out var previous))
                {
                    mutated.Remove(__instance);
                    previous.Restore();
                    return;
                }
                // A clone of our row copies our caption without our tracking; clear only that, including its visible text.
                var stale = ExistingCaption(__instance);
                if (stale != null && stale.Key == ExportKey) SetCaption(stale, new LanguageChangeEventDataHolder(), string.Empty, false);
                return;
            }
            var caption = __instance.transform.Find(CaptionPath);
            if (caption == null) { Warn("the native button caption was not found"); return; }
            var localized = caption.GetComponent<TextLocalizationComponent>();
            bool added = localized == null;
            if (added) localized = caption.gameObject.AddComponent<TextLocalizationComponent>();
            if (!mutated.TryGetValue(__instance, out var original))
            {
                original = new CaptionState
                {
                    Localized = localized,
                    Data = added ? new LanguageChangeEventDataHolder() : new LanguageChangeEventDataHolder
                    {
                        Key = localized.Key, Arguments = localized.Arguments, Prefix = localized.Prefix,
                        Suffix = localized.Suffix, Format = localized.Format
                    },
                    Text = localized.TextContainer.text,
                    Enabled = !added && localized.enabled
                };
                mutated.Add(__instance, original);
            }
            localized.enabled = true;
            // Reset leftover formatting on clones or a prefab component, and force a text refresh.
            // Localization also reapplies the caption after a language change or re-enable.
            localized.SetData(new LanguageChangeEventDataHolder { Key = key });
        }

        // Unity boundaries, kept separate so the offline check can substitute them.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static TextLocalizationComponent ExistingCaption(ModOptionsButtonControl control)
        {
            var caption = control.transform.Find(CaptionPath);
            return caption == null ? null : caption.GetComponent<TextLocalizationComponent>();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void SetCaption(TextLocalizationComponent localized, LanguageChangeEventDataHolder data, string text, bool enabled)
        {
            localized.enabled = false;
            localized.SetData(data);
            if (text != null) localized.TextContainer.text = text;
            localized.enabled = enabled;
        }

        static void Warn(string message)
        {
            if (warnedMessages.Contains(message)) return;
            warnedMessages.Add(message);
            warned = true;
            PerformanceMod.Log("Button captions unavailable: " + message);
        }
    }
}
