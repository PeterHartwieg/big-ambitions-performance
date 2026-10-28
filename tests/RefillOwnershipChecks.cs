using System;
using System.Reflection;
using System.Reflection.Emit;
using CityPerformance;

static class RefillOwnershipChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(RefillOwnership.Matches("PersistentCity") && RefillOwnership.Matches("persistentcity"), "assembly simple name is case insensitive");
        check(!RefillOwnership.Matches(null) && !RefillOwnership.Matches("PersistentCity.Tests") &&
            !RefillOwnership.Matches("OtherPersistentCity"), "unrelated assemblies do not claim refill");
        using (var stopped = new RefillOwnership())
        using (var late = new RefillOwnership())
        {
            check(!late.Present, "CP alone keeps refill ownership");
            stopped.Dispose();
            AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PersistentCity.Tests"), AssemblyBuilderAccess.Run);
            check(!late.Present, "unrelated late load leaves refill unchanged");
            AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PersistentCity"), AssemblyBuilderAccess.Run);
            check(late.Present, "Persistent City loaded after CP is detected without any Harmony hooks");
            check(!stopped.Present, "unload detaches assembly notification");
            using (var early = new RefillOwnership())
                check(early.Present, "Persistent City loaded before CP is detected by initial scan");
        }
    }
}
