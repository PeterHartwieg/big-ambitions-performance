using System;
using System.Reflection;
using System.Reflection.Emit;
using CityPerformance;

static class RloDistrictSaveChecks
{
    // A stand-in for RLO 1.0.5's DistrictSaveDirector: a static bool Waiting property over a settable field.
    static Assembly Fake(string assemblyName, string typeName, string property, Type propertyType, out FieldInfo field)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(assemblyName);
        var type = module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var backing = type.DefineField("value", propertyType, FieldAttributes.Public | FieldAttributes.Static);
        var getter = type.DefineMethod("get_" + property, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, backing);
        il.Emit(OpCodes.Ret);
        type.DefineProperty(property, PropertyAttributes.None, propertyType, null).SetGetMethod(getter);
        field = type.CreateType().GetField("value");
        return assembly;
    }

    internal static void Run(Action<bool, string> check)
    {
        var other = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SomeOtherMod"), AssemblyBuilderAccess.Run);
        check(!RloDistrictSave.HoldsSave(new Assembly[0]), "without RLO, background saves are not held back");
        check(!RloDistrictSave.HoldsSave(new Assembly[] { other }), "unrelated assemblies do not hold back background saves");

        var rlo = Fake("RealLifeOverhaul", RloDistrictSave.DirectorType, RloDistrictSave.WaitingProperty, typeof(bool), out var waiting);
        waiting.SetValue(null, false);
        check(!RloDistrictSave.HoldsSave(new Assembly[] { other, rlo }), "RLO with nothing taken out keeps background saves");
        waiting.SetValue(null, true);
        check(RloDistrictSave.HoldsSave(new Assembly[] { other, rlo }, out var audited) && audited, "RLO holding district records sends that save to native serialization");
        waiting.SetValue(null, false);
        check(!RloDistrictSave.HoldsSave(new Assembly[] { rlo }), "the cached lookup follows RLO's live state");

        var renamed = Fake("RealLifeOverhaul", "RealLifeOverhaul.Elsewhere.Director", RloDistrictSave.WaitingProperty, typeof(bool), out _);
        check(RloDistrictSave.HoldsSave(new Assembly[] { renamed }, out audited) && !audited, "an RLO without the audited director stays on native saving and reports it as unaudited");
        var retyped = Fake("RealLifeOverhaul", RloDistrictSave.DirectorType, RloDistrictSave.WaitingProperty, typeof(int), out _);
        check(RloDistrictSave.HoldsSave(new Assembly[] { retyped }, out audited) && !audited, "an RLO whose Waiting is not a bool stays on native saving and reports it as unaudited");
        check(!RloDistrictSave.HoldsSave(new Assembly[] { other }), "the cached lookup is dropped when RLO is absent");
    }
}
