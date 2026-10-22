using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace CityPerformance
{
    // Main-thread capture only. Plans contain metadata, never live game objects.
    // The caller supplies an audited field policy; unknown records fail before they reach a worker.
    public sealed class SaveSnapshotSchema
    {
        private readonly Func<Type, FieldInfo[]> fields;
        private readonly Dictionary<Type, bool> inline = new Dictionary<Type, bool>();
        private readonly HashSet<Type> observed = new HashSet<Type>();
        private readonly Type traceRoot;
        private readonly Dictionary<Type, Func<SaveSnapshotCopy, object, object>> plans = new Dictionary<Type, Func<SaveSnapshotCopy, object, object>>();
        private static readonly Func<object, object> shallowCopy = CreateShallowCopy();
        private static Func<object, object> CreateShallowCopy()
        {
            var method = new DynamicMethod("SaveSnapshotShallowCopy", typeof(object), new[] { typeof(object) }, typeof(SaveSnapshotSchema), true);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)); il.Emit(OpCodes.Ret);
            return (Func<object, object>)method.CreateDelegate(typeof(Func<object, object>));
        }
        public SaveSnapshotSchema(Func<Type, FieldInfo[]> fields, Type traceRoot = null) { this.fields = fields; this.traceRoot = traceRoot; }
        public int PlanCount => plans.Count;
        public int Revision => plans.Count + inline.Count + observed.Count;
        public IEnumerable<Type> PlannedTypes => plans.Keys;
        public IEnumerable<Type> WarmTypes => plans.Keys.Concat(inline.Where(p => p.Key.IsValueType).Select(p => p.Key)).Concat(observed).Distinct();
        internal void Observe(Type type) { observed.Add(type); }
        public static bool UsesFieldPlan(Type t) => !Atomic(t) && !t.IsArray && !IsContainer(t) &&
            !(t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Tuple<,>) || t.GetGenericTypeDefinition() == typeof(ValueTuple<,>)));
        internal static object CloneBox(object value) => shallowCopy(value);
        public static bool Atomic(Type t) => t.IsPrimitive && t != typeof(IntPtr) && t != typeof(UIntPtr) || t.IsEnum ||
            t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(TimeSpan) || t == typeof(Guid);
        internal bool Inline(Type t)
        {
            if (Atomic(t)) { observed.Add(t); return true; }
            if (inline.TryGetValue(t, out bool result)) return result;
            inline[t] = false;
            if (!t.IsValueType || t.IsPointer || t.IsByRef || t == typeof(IntPtr) || t == typeof(UIntPtr)) return false;
            RefuseSpecialRecord(t);
            // Only audited records (or nullable/value tuples) can bypass the graph copier.
            if (Nullable.GetUnderlyingType(t) == null && !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ValueTuple<,>)) && fields(t) == null)
                throw new NotSupportedException("Unknown snapshot value: " + t.FullName);
            result = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => Inline(f.FieldType));
            inline[t] = result; return result;
        }

        internal Func<SaveSnapshotCopy, object, object> Plan(Type t)
        {
            if (plans.TryGetValue(t, out var plan)) return plan;
            RefuseSpecialRecord(t);
            if (t.IsArray)
            {
                if (t.GetArrayRank() != 1 || t != t.GetElementType().MakeArrayType()) throw new NotSupportedException("Only zero-based vectors are supported");
                plan = ArrayPlan;
            }
            else if (IsContainer(t))
            {
                var d = t.GetGenericTypeDefinition();
                string method = d == typeof(List<>) ? nameof(ListPlan) : d == typeof(Dictionary<,>) ? nameof(DictionaryPlan) :
                    d == typeof(Queue<>) ? nameof(QueuePlan) : d == typeof(Stack<>) ? nameof(StackPlan) : nameof(SetPlan);
                plan = (Func<SaveSnapshotCopy, object, object>)typeof(SaveSnapshotSchema).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                    .MakeGenericMethod(t.GenericTypeArguments).CreateDelegate(typeof(Func<SaveSnapshotCopy, object, object>));
            }
            else
            {
                FieldInfo[] members;
                if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Tuple<,>) || t.GetGenericTypeDefinition() == typeof(ValueTuple<,>)))
                    members = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                else members = fields(t);
                if (members == null || t.IsAbstract || t.IsInterface || t.IsPointer || t.IsByRef)
                    throw new NotSupportedException("Unknown snapshot record: " + t.FullName);
                var method = new DynamicMethod("SaveSnapshotFields", typeof(void), new[] { typeof(SaveSnapshotCopy), typeof(object), typeof(object) }, typeof(SaveSnapshotSchema), true);
                var il = method.GetILGenerator();
                var identities = new HashSet<(Module, int, Type)>(members.Select(f => (f.Module, f.MetadataToken, f.DeclaringType)));
                // Copy inline values with the runtime's bulk field copy, then clear every
                // non-save field before detaching references. Runtime Unity caches never survive.
                for (Type cursor = t; cursor != null; cursor = cursor.BaseType)
                    foreach (var field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!identities.Contains((field.Module, field.MetadataToken, field.DeclaringType)))
                        {
                            il.Emit(OpCodes.Ldarg_2); il.Emit(t.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, t);
                            il.Emit(OpCodes.Ldflda, field); il.Emit(OpCodes.Initobj, field.FieldType);
                        }
                foreach (var field in members)
                {
                    if (field.IsStatic) throw new NotSupportedException("Static snapshot field");
                    if (Inline(field.FieldType)) continue;
                    if (t == traceRoot) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, field.Name); il.Emit(OpCodes.Callvirt, typeof(SaveSnapshotCopy).GetMethod(nameof(SaveSnapshotCopy.BeginSection))); }
                    il.Emit(OpCodes.Ldarg_2); il.Emit(t.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, t);
                    bool direct = Inline(field.FieldType);
                    if (!direct) il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1); il.Emit(t.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, t);
                    il.Emit(OpCodes.Ldfld, field);
                    if (!direct)
                    {
                        if (field.FieldType.IsValueType) il.Emit(OpCodes.Box, field.FieldType);
                        il.Emit(OpCodes.Callvirt, typeof(SaveSnapshotCopy).GetMethod(nameof(SaveSnapshotCopy.Copy)));
                        il.Emit(OpCodes.Unbox_Any, field.FieldType);
                    }
                    il.Emit(OpCodes.Stfld, field);
                    if (t == traceRoot) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, typeof(SaveSnapshotCopy).GetMethod(nameof(SaveSnapshotCopy.EndSection))); }
                }
                il.Emit(OpCodes.Ret);
                var fill = (Action<SaveSnapshotCopy, object, object>)method.CreateDelegate(typeof(Action<SaveSnapshotCopy, object, object>));
                plan = (copy, source) => {
                    var destination = shallowCopy(source);
                    copy.Remember(source, destination);
                    fill(copy, source, destination);
                    return destination;
                };
            }
            plans.Add(t, plan); return plan;
        }
        public void Prepare(Type type)
        {
            if (Atomic(type)) { observed.Add(type); return; }
            if (plans.ContainsKey(type) || type.IsAbstract || type.IsInterface || typeof(Delegate).IsAssignableFrom(type)) return;
            if (Nullable.GetUnderlyingType(type) is Type underlying) { Prepare(underlying); return; }
            Plan(type);
            if (type.IsArray) Prepare(type.GetElementType());
            else if (IsContainer(type) || type.IsGenericType && type.Assembly == typeof(Tuple<,>).Assembly)
                foreach (var arg in type.GenericTypeArguments) Prepare(arg);
            else foreach (var field in fields(type)) Prepare(field.FieldType);
        }
        private static bool IsContainer(Type t) => t.IsGenericType && new[] { typeof(List<>), typeof(Dictionary<,>), typeof(Queue<>), typeof(Stack<>), typeof(HashSet<>) }.Contains(t.GetGenericTypeDefinition());
        private static void RefuseSpecialRecord(Type t)
        {
            if (IsContainer(t) || t.IsArray) return;
            if (typeof(Delegate).IsAssignableFrom(t) || typeof(ISerializable).IsAssignableFrom(t) ||
                typeof(System.Collections.IEnumerable).IsAssignableFrom(t))
                throw new NotSupportedException("Snapshot refuses callbacks/custom serialization/collection records: " + t.FullName);
        }
        private static object ArrayPlan(SaveSnapshotCopy c, object value)
        {
            var a = (Array)value; c.Elements(a.Length);
            var b = (Array)a.Clone(); c.Remember(a, b);
            if (!c.Inline(a.GetType().GetElementType())) for (int i = 0; i < a.Length; i++) b.SetValue(c.Copy(a.GetValue(i)), i);
            return b;
        }
        private static T Element<T>(SaveSnapshotCopy c, T value) => c.Inline(typeof(T)) ? value : (T)c.Copy(value);
        private static object ListPlan<T>(SaveSnapshotCopy c, object value)
        {
            var a = (List<T>)value; c.Elements(a.Count);
            var b = new List<T>(a.Count); c.Remember(a, b);
            if (c.Inline(typeof(T))) b.AddRange(a); else foreach (var x in a) b.Add((T)c.Copy(x));
            return b;
        }
        private static void CheckComparer<T>(IEqualityComparer<T> comparer)
        {
            var type = comparer.GetType();
            // Odin can deserialize a fresh instance of a framework comparer. These exact
            // stateless framework types are safe even when they are not the singleton.
            bool standard = type.Assembly == typeof(EqualityComparer<>).Assembly && type.IsGenericType && type.GenericTypeArguments.SequenceEqual(new[] { typeof(T) }) &&
                new[] { "System.Collections.Generic.GenericEqualityComparer`1", "System.Collections.Generic.ObjectEqualityComparer`1", "System.Collections.Generic.EnumEqualityComparer`1", "System.Collections.Generic.NullableEqualityComparer`1" }.Contains(type.GetGenericTypeDefinition().FullName);
            if (standard) for (Type t = type; t != null; t = t.BaseType)
                if (t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length != 0) standard = false;
            if (!standard && !ReferenceEquals(comparer, EqualityComparer<T>.Default) && !ReferenceEquals(comparer, StringComparer.Ordinal) && !ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException("Custom snapshot collection comparer: " + comparer.GetType().FullName);
        }
        private static object DictionaryPlan<K, V>(SaveSnapshotCopy c, object value)
        {
            var a = (Dictionary<K, V>)value; CheckComparer(a.Comparer); c.Observe(a.Comparer.GetType()); c.Elements(a.Count);
            var b = new Dictionary<K, V>(a.Count, a.Comparer); c.Remember(a, b);
            foreach (var x in a) b.Add(Element(c, x.Key), Element(c, x.Value)); return b;
        }
        private static object SetPlan<T>(SaveSnapshotCopy c, object value)
        {
            var a = (HashSet<T>)value; CheckComparer(a.Comparer); c.Observe(a.Comparer.GetType()); c.Elements(a.Count);
            var b = new HashSet<T>(a.Comparer); c.Remember(a, b);
            foreach (var x in a) b.Add(Element(c, x)); return b;
        }
        private static object QueuePlan<T>(SaveSnapshotCopy c, object value)
        {
            var a = (Queue<T>)value; c.Elements(a.Count);
            var b = new Queue<T>(a.Count); c.Remember(a, b);
            foreach (var x in a) b.Enqueue(Element(c, x)); return b;
        }
        private static object StackPlan<T>(SaveSnapshotCopy c, object value)
        {
            var a = (Stack<T>)value; c.Elements(a.Count);
            var b = new Stack<T>(a.Count); c.Remember(a, b);
            var items = a.ToArray(); for (int i = items.Length - 1; i >= 0; i--) b.Push(Element(c, items[i])); return b;
        }
    }

    public sealed class SaveSnapshotCopy
    {
        private sealed class Identity : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
        private readonly SaveSnapshotSchema schema;
        private readonly Dictionary<object, object> copies = new Dictionary<object, object>(new Identity());
        private readonly long deadline;
        private int depth, visits;
        private string section; private long sectionStart; private int sectionObjects;
        public readonly List<(string Name, double Milliseconds, int Objects)> Sections = new List<(string, double, int)>();
        public int Objects { get; private set; }
        public long ElementCount { get; private set; }
        public SaveSnapshotCopy(SaveSnapshotSchema schema, double maximumMilliseconds = 250)
        { this.schema = schema; deadline = Stopwatch.GetTimestamp() + (long)(maximumMilliseconds * Stopwatch.Frequency / 1000); }
        public object Copy(object source)
        {
            if (source == null) return null;
            var type = source.GetType();
            schema.Observe(type);
            if (SaveSnapshotSchema.Atomic(type)) return source;
            if (copies.TryGetValue(source, out var existing)) return existing;
            if (schema.Inline(type))
            {
                // A value behind object/interface is a mutable box. Detach it while
                // retaining aliases to the same box within this snapshot.
                var clone = SaveSnapshotSchema.CloneBox(source); Remember(source, clone); return clone;
            }
            if (++depth > 128) { depth--; throw new NotSupportedException("Snapshot graph depth exceeds 128"); }
            try
            {
                if (++visits > 2000000 || (visits & 1023) == 0 && Stopwatch.GetTimestamp() > deadline)
                    throw new NotSupportedException("Snapshot capture budget exceeded");
                return schema.Plan(type)(this, source);
            }
            finally { depth--; }
        }
        internal void Remember(object source, object destination) { Objects++; copies.Add(source, destination); }
        internal bool Inline(Type type) => schema.Inline(type);
        internal void Observe(Type type) => schema.Observe(type);
        public void BeginSection(string name) { section = name; sectionStart = Stopwatch.GetTimestamp(); sectionObjects = Objects; }
        public void EndSection() { Sections.Add((section, (Stopwatch.GetTimestamp() - sectionStart) * 1000.0 / Stopwatch.Frequency, Objects - sectionObjects)); }
        internal void Elements(int count)
        {
            ElementCount += count;
            if (ElementCount > 10000000 || Stopwatch.GetTimestamp() > deadline) throw new NotSupportedException("Snapshot element/time budget exceeded");
        }
    }
}
