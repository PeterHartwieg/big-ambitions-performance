using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

if (args.Length < 1) throw new ArgumentException("SaveCheck <Managed>");
string managed = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (context, name) => {
    if (new[] { "mscorlib", "System", "System.Core", "netstandard" }.Contains(name.Name)) return null;
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "BigAmbitions.dll"));
var odin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "OdinSerializer.dll"));
var policy = Activator.CreateInstance(game.GetType("Player.SaveSystem.SaveGameSerializationPolicy", true));
var getMembers = odin.GetType("OdinSerializer.FormatterUtilities").GetMethod("GetSerializableMembers");
if (args.Length == 3 && args[1] == "--hooks")
{
    SaveHookChecks.Run(game, AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2])));
    return;
}
if (args.Length >= 3 && args[1] == "--roundtrip")
{
    var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
    RoundtripChecks.Run(game, odin, mod, args.Skip(3).ToArray());
    return;
}
var seen = new HashSet<Type>();
void Walk(Type type) {
    if (!seen.Add(type)) return;
    if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)) return;
    if (type.IsArray) { Walk(type.GetElementType()); return; }
    if (type.IsGenericType && type.Assembly == typeof(List<>).Assembly) {
        Console.WriteLine("BCL " + type);
        foreach (var a in type.GenericTypeArguments) Walk(a);
        return;
    }
    var members = (MemberInfo[])getMembers.Invoke(null, new[] { (object)type, policy });
    Console.WriteLine("DATA " + type + " @ " + type.Assembly.GetName().Name + " members=" + members.Length + " interfaces=" + string.Join(",", type.GetInterfaces().Select(x=>x.Name)));
    foreach (var m in members) {
        if (m is FieldInfo f) Walk(f.FieldType);
        else Console.WriteLine("PROPERTY " + type + "." + m.Name);
    }
    foreach (var m in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))
        if (m.GetCustomAttributesData().Any(a=>a.AttributeType.Namespace=="System.Runtime.Serialization")) Console.WriteLine("CALLBACK " + type + "." + m.Name);
}
Walk(game.GetType("GameInstance", true));
foreach(var a in seen.Select(t=>t.Assembly).Distinct()) Console.WriteLine("ASSEMBLY " + a.Location);
