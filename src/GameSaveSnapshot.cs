using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using HarmonyLib;
using OdinSerializer;
using OdinSerializer.Utilities;
using Player.SaveSystem;

namespace CityPerformance
{
    internal static class GameSaveSnapshot
    {
        private static readonly SaveGameSerializationPolicy policy = new SaveGameSerializationPolicy();
        private static readonly Dictionary<string, string> hashes = new Dictionary<string, string> {
            { "BigAmbitions", "9427561b81aaab2ada09c22a3c98dd9a4017a65225b389513885bf336852e911" },
            { "BigAmbitions.Items", "db07038215ea8ab2430fb942b86a19e93b5fb84f70639d87606b194786d5bab1" },
            { "DayNightCycle", "28a354809e8c3fdfe24ac601d31afe061816b1bd7c50f4aaa604dd442aa4fd28" },
            { "HGExtensions", "fafddfadf7aca598343b4679d14bc4e1381153be9a54bf94f35daa6b49b27b38" },
            { "BigAmbitions.InteriorDesigner", "04bab0df97d0bf7eb4c04b299e1b59bd81c254b5f57e8edca82f661b3ef50c5c" },
            { "BigAmbitions.Characters", "82864add2ff57fbb68d2933b1abebca89154430e012c224b7ebe37925a306ad8" },
            { "BigAmbitions.Neighborhoods", "867cb792e3295f9c67cf0a5ec04464546c9bd337fa47a578d30409e03eeb7205" },
            { "UnityEngine.CoreModule", "5afc743d350e95dc30379a6d2bafa2293b63259acf196fe3f7370f03644ef6f4" },
            { "OdinSerializer", "ba78fc5cd196cf7c2700e5843974d8442373b290bf34458ea9c9da667a60f27a" }
        };
        internal static SaveSnapshotSchema Schema;
        private static readonly HashSet<Type> warmed = new HashSet<Type>();
        internal static void Initialize()
        {
            foreach (var pair in hashes)
            {
                var assembly = Assembly.Load(pair.Key);
                using (var hash = SHA256.Create())
                using (var file = File.OpenRead(assembly.Location))
                    if (BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != pair.Value)
                        throw new NotSupportedException("Unaudited save dependency: " + pair.Key);
            }
            Schema = new SaveSnapshotSchema(Fields, typeof(GameInstance));
            warmed.Clear();
            Schema.Prepare(typeof(GameInstance));
        }
        internal static void PrepareWorker()
        {
            SaveSerializationGate.Run(() =>
            {
                string diagnostic = null;
                UnityEngine.Application.LogCallback observer = (message, stack, kind) => {
                    if (kind != UnityEngine.LogType.Log) diagnostic = diagnostic ?? message;
                };
                UnityEngine.Application.logMessageReceived += observer;
                try
                {
                    foreach (var type in Schema.WarmTypes)
                    {
                        if (warmed.Contains(type)) continue;
                        Serializer.Get(type);
                        if (!FormatterUtilities.IsPrimitiveType(type))
                            foreach (bool weak in new[] { false, true })
                            {
                                var formatter = FormatterLocator.GetFormatter(type, policy, weak).GetType();
                                if (SaveSnapshotSchema.UsesFieldPlan(type) && type.Assembly != typeof(object).Assembly)
                                {
                                    var definition = formatter.IsGenericType ? formatter.GetGenericTypeDefinition() : formatter;
                                    string name = definition.FullName;
                                    if (definition.Assembly != typeof(SerializationUtility).Assembly ||
                                        !(name == "OdinSerializer.ReflectionFormatter`1" || name == "OdinSerializer.FormatterEmitter+RuntimeEmittedFormatter`1" ||
                                          name == "OdinSerializer.EmptyTypeFormatter`1" || type == typeof(UnityEngine.Color32) && name == "OdinSerializer.Color32Formatter"))
                                        throw new NotSupportedException("Unaudited record formatter: " + type.FullName + " -> " + formatter.FullName);
                                }
                            }
                        if (diagnostic != null) throw new NotSupportedException("Save formatter warmup diagnostic: " + diagnostic);
                        warmed.Add(type);
                    }
                }
                finally { UnityEngine.Application.logMessageReceived -= observer; }
            });
        }
        internal static FieldInfo[] Fields(Type type)
        {
            if (!hashes.ContainsKey(type.Assembly.GetName().Name) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                typeof(ISelfFormatter).IsAssignableFrom(type) || typeof(ISerializable).IsAssignableFrom(type) ||
                type.Assembly == typeof(UnityEngine.Color32).Assembly && type != typeof(UnityEngine.Color32) ||
                typeof(UnityEngine.ISerializationCallbackReceiver).IsAssignableFrom(type))
                throw new NotSupportedException("Unaudited or Unity-backed save record: " + type.FullName);
            for (Type t = type; t != null; t = t.BaseType)
                foreach (var method in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (method.IsDefined(typeof(OnSerializingAttribute), true) || method.IsDefined(typeof(OnSerializedAttribute), true))
                        throw new NotSupportedException("Save serialization callback: " + type.FullName);
            return FormatterUtilities.GetSerializableMembers(type, policy).Select(m => (m as FieldInfo ??
                throw new NotSupportedException("Save property requires audit: " + type.FullName + "." + m.Name)).DeAliasField()).ToArray();
        }
        internal static bool ForeignFormatterOrMultiplayer()
        {
            var odin = typeof(SerializationUtility).Assembly;
            if (Harmony.GetAllPatchedMethods().Any(method => method.DeclaringType?.Assembly == odin &&
                Harmony.GetPatchInfo(method)?.Owners.Any(owner => owner != PerformanceMod.Id) == true)) return true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (MidnightCleanup.IsMultiplayerAssembly(assembly.GetName().Name)) return true;
                if (assembly == odin) continue;
                // Only the exact assembly emitted by the audited Odin emitter can
                // bypass attribute inspection. Other dynamic assemblies may register
                // formatters; inspection failures are handled by the caller's guard.
                try
                {
                    if (assembly.IsDynamic && ReferenceEquals(assembly,
                        AccessTools.Field(typeof(FormatterEmitter), "runtimeEmittedAssembly")?.GetValue(null))) continue;
                    foreach (var attribute in assembly.GetCustomAttributesData())
                        if (attribute.AttributeType.Namespace == "OdinSerializer" && attribute.AttributeType.Name.StartsWith("RegisterFormatter", StringComparison.Ordinal)) return true;
                }
                catch (Exception e) { throw new NotSupportedException("Cannot inspect save formatter registrations in " + assembly.FullName, e); }
            }
            return false;
        }
        private sealed class WorkerLogger : ILogger
        {
            internal string Issue;
            public void LogWarning(string warning) { Issue = Issue ?? warning; throw new InvalidOperationException("Save serializer warning: " + warning); }
            public void LogError(string error) { Issue = Issue ?? error; throw new InvalidOperationException("Save serializer error: " + error); }
            public void LogException(Exception exception) { Issue = Issue ?? exception.ToString(); throw new InvalidOperationException("Save serializer exception", exception); }
        }
        internal static void Serialize(Stream stream, GameInstance snapshot)
        {
            // Never use the base game's static SerializationContext or its Unity logger.
            var context = new SerializationContext();
            var logger = new WorkerLogger();
            context.Config.SerializationPolicy = policy;
            context.Config.DebugContext.Logger = logger;
            context.Config.DebugContext.ErrorHandlingPolicy = ErrorHandlingPolicy.ThrowOnErrors;
            context.Config.DebugContext.LoggingPolicy = LoggingPolicy.LogWarningsAndErrors;
            try { SerializationUtility.SerializeValue(snapshot, stream, DataFormat.Binary, context); }
            catch (Exception e) when (logger.Issue != null) { throw new InvalidOperationException("Save serializer diagnostic: " + logger.Issue, e); }
        }
    }
}
