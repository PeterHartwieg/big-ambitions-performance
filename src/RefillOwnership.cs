using System;
using System.Reflection;

namespace CityPerformance
{
    // Assembly presence, not mod callbacks or Harmony installation order, decides ownership.
    // The loader callback only sets a flag; cancellation/logging stay on the game's thread.
    internal sealed class RefillOwnership : IDisposable
    {
        private volatile bool present;
        internal bool Present => present;
        internal static bool Matches(string name) =>
            string.Equals(name, "PersistentCity", StringComparison.OrdinalIgnoreCase);

        internal RefillOwnership()
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            // Subscribe before scanning so a concurrent load cannot fall between the two.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) Observe(assembly);
        }

        private void Observe(Assembly assembly)
        {
            if (Matches(assembly.GetName().Name)) present = true;
        }
        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args) => Observe(args.LoadedAssembly);
        public void Dispose() => AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
    }
}
