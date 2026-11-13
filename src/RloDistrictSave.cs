using System;
using System.Collections.Generic;
using System.Reflection;

namespace CityPerformance
{
    // Real Life Overhaul 1.0.5 takes records naming its own places out of the game's lists in the save event and
    // puts them back when the game logs "Game <temp> saved in N ms" from the native serializer on the main thread.
    // A background capture skips that native call, so the records would stay out until RLO's next-frame fallback.
    // While RLO holds records, that save stays native. A changed RLO that still loads its assembly also stays native.
    internal static class RloDistrictSave
    {
        internal const string AssemblyName = "RealLifeOverhaul";
        internal const string DirectorType = "RealLifeOverhaul.DistrictSave.DistrictSaveDirector";
        internal const string WaitingProperty = "Waiting";
        private static PropertyInfo waiting;

        internal static bool HoldsSave(out bool audited) => HoldsSave(AppDomain.CurrentDomain.GetAssemblies(), out audited);

        internal static bool HoldsSave(IEnumerable<Assembly> assemblies) => HoldsSave(assemblies, out _);

        // audited is false when RLO is loaded but its director could not be read in the audited shape.
        internal static bool HoldsSave(IEnumerable<Assembly> assemblies, out bool audited)
        {
            audited = true;
            Assembly rlo = null;
            foreach (var assembly in assemblies)
            {
                try { if (string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase)) { rlo = assembly; break; } }
                catch (Exception) { }
            }
            if (rlo == null) return false;
            try
            {
                var property = waiting;
                if (property == null || property.DeclaringType?.Assembly != rlo)
                {
                    property = rlo.GetType(DirectorType, false)?.GetProperty(WaitingProperty, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (property == null || property.PropertyType != typeof(bool) || property.GetIndexParameters().Length != 0) { audited = false; return true; }
                    waiting = property;
                }
                return (bool)property.GetValue(null);
            }
            catch (Exception) { audited = false; return true; }
        }
    }
}
