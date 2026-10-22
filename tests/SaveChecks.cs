using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using CityPerformance;

internal static class SaveChecks
{
    private sealed class Record
    {
        public int Number;
        public Record Alias;
        public List<Record> List;
        public Dictionary<string, Record> Dictionary;
        public Queue<Record> Queue;
        public Stack<Record> Stack;
        public HashSet<string> Set;
        public Tuple<Record, int> Tuple;
        public (Record, int)? Optional;
        public int[] Array;
        public Action Callback;
        [NonSerialized] public object Runtime;
    }
    private sealed class Unknown { }
    private struct UnknownValue { public int Value; }
    private interface IMutable { void Increment(); int Value { get; } }
    private struct MutableValue : IMutable { public int Number; public void Increment() { Number++; } public int Value => Number; }
    private struct CustomSerialized : ISerializable { public int Number; public void GetObjectData(SerializationInfo i, StreamingContext c) { } }
    private sealed class Bag : List<Record> { }
    private sealed class BoxHolder { public object One, Two; }
    private class BaseRecord { public int Number; public Record Link; [NonSerialized] public object Cache; }
    private sealed class DerivedRecord : BaseRecord { public new int Number; public new Record Link; }
    private sealed class CustomComparer : IEqualityComparer<string>
    { public bool Equals(string x, string y) => x == y; public int GetHashCode(string x) => x.GetHashCode(); }
    internal static void Run(Action<bool, string> check)
    {
        var schema = new SaveSnapshotSchema(t => t == typeof(Record) || t == typeof(DerivedRecord) || t == typeof(MutableValue) || t == typeof(CustomSerialized) || t == typeof(BoxHolder) || t == typeof(Bag) ? t.GetFields().Where(f => !f.IsDefined(typeof(NonSerializedAttribute))).ToArray() : null);
        schema.Prepare(typeof(Record));
        var a = new Record { Number = 7, Runtime = new Unknown(), Array = new[] { 1, 2 }, Set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Tree" } };
        a.Alias = a; a.List = new List<Record> { a, a };
        a.Dictionary = new Dictionary<string, Record>(StringComparer.Ordinal) { ["one"] = a };
        a.Queue = new Queue<Record>(new[] { a, new Record { Number = 8 } });
        a.Stack = new Stack<Record>(new[] { a, new Record { Number = 9 } });
        a.Tuple = Tuple.Create(a, 11); a.Optional = (a, 12);
        var b = (Record)new SaveSnapshotCopy(schema).Copy(a);
        check(!ReferenceEquals(a, b) && ReferenceEquals(b, b.Alias), "save snapshot preserves cycles without sharing live records");
        check(ReferenceEquals(b, b.List[0]) && ReferenceEquals(b.List[0], b.List[1]) && ReferenceEquals(b, b.Dictionary["one"]), "save snapshot preserves aliases across collections");
        check(b.Queue.Peek() == b && b.Queue.Last().Number == 8 && b.Stack.Peek().Number == 9 && b.Stack.Last() == b, "save queue/stack ordering survives capture");
        check(b.Tuple.Item1 == b && b.Tuple.Item2 == 11 && b.Optional.Value.Item1 == b && b.Optional.Value.Item2 == 12, "tuple and nullable struct references are detached");
        check(b.Runtime == null && b.Set.Contains("TREE"), "runtime caches omitted and approved comparer preserved");
        a.Number = 33; a.Array[0] = 55; a.List.Clear(); a.Dictionary.Clear(); a.Queue.Clear(); a.Set.Clear();
        check(b.Number == 7 && b.Array[0] == 1 && b.List.Count == 2 && b.Dictionary.Count == 1 && b.Queue.Count == 2 && b.Set.Count == 1, "mutations after capture cannot change frozen snapshot");
        void Refuses(Action action, string message) { bool refused = false; try { action(); } catch (NotSupportedException) { refused = true; } check(refused, message); }
        Refuses(() => new SaveSnapshotCopy(schema).Copy(new Unknown()), "unknown record is refused");
        Refuses(() => new SaveSnapshotCopy(schema).Copy(new UnknownValue { Value = 9 }), "unknown value type cannot bypass schema validation");
        Refuses(() => new SaveSnapshotCopy(schema).Copy(new CustomSerialized { Number = 1 }), "inline value cannot bypass custom serialization refusal");
        Refuses(() => new SaveSnapshotCopy(schema).Copy(new Bag { new Record { Number = 5 } }), "collection subclass cannot become an empty field-based record");
        object box = new MutableValue { Number = 4 };
        var holder = (BoxHolder)new SaveSnapshotCopy(schema).Copy(new BoxHolder { One = box, Two = box });
        ((IMutable)box).Increment();
        check(((IMutable)holder.One).Value == 4 && !ReferenceEquals(box, holder.One), "mutable value box detaches from live interface mutation");
        check(ReferenceEquals(holder.One, holder.Two), "aliases to the same mutable value box survive capture");
        int revision = schema.Revision;
        new SaveSnapshotCopy(schema).Copy(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        check(schema.Revision > revision && schema.WarmTypes.Contains(StringComparer.OrdinalIgnoreCase.GetType()), "new dictionary comparer expands validation and prewarm types");
        var inherited = new DerivedRecord { Number = 42, Link = new Record { Number = 43 } };
        ((BaseRecord)inherited).Number = 7; ((BaseRecord)inherited).Link = new Record { Number = 8 }; inherited.Cache = new Unknown();
        var inheritedCopy = (DerivedRecord)new SaveSnapshotCopy(schema).Copy(inherited);
        check(inheritedCopy.Number == 42 && ((BaseRecord)inheritedCopy).Number == 7, "hidden inline base and derived fields survive reflected-field identity differences");
        check(inheritedCopy.Link.Number == 43 && ((BaseRecord)inheritedCopy).Link.Number == 8 && !ReferenceEquals(inheritedCopy.Link, inherited.Link) && !ReferenceEquals(((BaseRecord)inheritedCopy).Link, ((BaseRecord)inherited).Link), "hidden reference fields both detach");
        check(inheritedCopy.Cache == null && inherited.Cache != null, "inherited runtime cache clears only on snapshot");
        Refuses(() => new SaveSnapshotCopy(schema).Copy(new Record { Callback = () => {} }), "live callback is refused");
        Refuses(() => new SaveSnapshotCopy(schema).Copy(new Dictionary<string, int>(new CustomComparer())), "custom comparer is refused");
        Refuses(() => new SaveSnapshotCopy(schema).Copy(new int[2, 2]), "unsupported array shape refused");
        Refuses(() => new SaveSnapshotCopy(schema, -1).Copy(new List<int> { 1 }), "capture deadline refuses expensive continuation");
        var deep = new Record(); var cursor = deep; for (int i = 0; i < 150; i++) { cursor.Alias = new Record(); cursor = cursor.Alias; }
        Refuses(() => new SaveSnapshotCopy(schema).Copy(deep), "excessive graph depth refused");
        string dir = Path.Combine(Path.GetTempPath(), "city-save-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string dest = Path.Combine(dir, "save.hsg"), data = Path.Combine(dir, "data.tmp"), meta = Path.Combine(dir, "meta.tmp");
            void Old() { File.WriteAllText(dest, "old-data"); File.WriteAllText(dest + ".meta", "old-meta"); File.WriteAllText(data, "new-data"); File.WriteAllText(meta, "new-meta"); }
            foreach (string point in new[] { "before-data", "after-data", "after-meta" })
            {
                Old(); bool threw = false;
                try { SaveFileCommit.Publish(data, meta, dest, p => { if (p == point) throw new IOException("injected failure"); }); } catch (IOException) { threw = true; }
                check(threw && File.ReadAllText(dest) == "old-data" && File.ReadAllText(dest + ".meta") == "old-meta", "save publication rolls back failure at " + point);
            }
            Old(); SaveFileCommit.Publish(data, meta, dest);
            check(File.ReadAllText(dest) == "new-data" && File.ReadAllText(dest + ".meta") == "new-meta", "new data and metadata published");
            check(File.ReadAllText(dest + ".cityperformance-previous") == "old-data" && File.ReadAllText(dest + ".cityperformance-previous-metadata") == "old-meta", "prior save pair retained without advertising a duplicate native slot");
            foreach (string target in new[] { dest, dest + ".meta" })
            {
                Old(); bool threw = false;
                try { SaveFileCommit.Publish(data, meta, dest, replace: (source, destination) => {
                    if (destination == target) { File.Delete(destination); throw new IOException("Windows replacement moved destination then failed"); }
                    File.Replace(source, destination, null);
                }); } catch (IOException) { threw = true; }
                check(threw && File.ReadAllText(dest) == "old-data" && File.ReadAllText(dest + ".meta") == "old-meta", "partial replacement failure restores missing destination: " + Path.GetExtension(target));
                Old(); threw = false;
                try { SaveFileCommit.Publish(data, meta, dest, replace: (source, destination) => {
                    File.Replace(source, destination, null);
                    if (destination == target) throw new IOException("replacement changed target before reporting failure");
                }); } catch (IOException) { threw = true; }
                check(threw && File.ReadAllText(dest) == "old-data" && File.ReadAllText(dest + ".meta") == "old-meta", "replacement failure restores changed existing destination: " + Path.GetExtension(target));
            }
            Old(); bool rollbackThrew = false;
            try { SaveFileCommit.Publish(data, meta, dest, p => {
                if (p == "after-meta") { Directory.CreateDirectory(dest + ".meta.cityperformance-rollback"); throw new IOException("publication failed"); }
            }); } catch (AggregateException e) { rollbackThrew = e.InnerExceptions.Count == 2; }
            check(rollbackThrew && File.ReadAllText(dest) == "old-data" && File.ReadAllText(dest + ".cityperformance-previous-metadata") == "old-meta",
                "data rollback still runs if metadata rollback fails; recovery metadata retained");
            Directory.Delete(dest + ".meta.cityperformance-rollback");
            Old(); File.Delete(dest + ".meta"); SaveFileCommit.Publish(data, meta, dest);
            check(File.ReadAllText(dest + ".cityperformance-previous") == "old-data" && !File.Exists(dest + ".cityperformance-previous-metadata"),
                "incomplete previous slot cannot acquire stale backup metadata");
            foreach (string point in new[] { "before-data", "after-data" })
            {
                File.Delete(dest); File.Delete(dest + ".meta"); File.WriteAllText(data, "first"); File.WriteAllText(meta, "first-meta");
                string foreign = point == "before-data" ? dest : dest + ".meta";
                bool moveFailed = false;
                try { SaveFileCommit.Publish(data, meta, dest, p => { if (p == point) File.WriteAllText(foreign, "external-file"); }); }
                catch (IOException) { moveFailed = true; }
                check(moveFailed && File.ReadAllText(foreign) == "external-file" && (point == "before-data" || !File.Exists(dest)),
                    "failed new-slot move does not delete a file created by another writer: " + point);
            }
            File.Delete(dest); File.Delete(dest + ".meta"); File.WriteAllText(data, "first"); File.WriteAllText(meta, "first-meta");
            File.WriteAllText(dest + ".cityperformance-previous", "older-recovery"); File.WriteAllText(dest + ".cityperformance-previous-metadata", "older-recovery-meta");
            try { SaveFileCommit.Publish(data, meta, dest, p => { if (p == "after-data") throw new IOException(); }); } catch (IOException) { }
            check(!File.Exists(dest) && !File.Exists(dest + ".meta"), "failed first save leaves no partial discoverable save");
            check(File.ReadAllText(dest + ".cityperformance-previous") == "older-recovery" && File.ReadAllText(dest + ".cityperformance-previous-metadata") == "older-recovery-meta",
                "missing current slot preserves older recovery evidence on failed new publication");
            File.WriteAllText(data, "first"); File.WriteAllText(meta, "first-meta"); SaveFileCommit.Publish(data, meta, dest);
            check(File.ReadAllText(dest) == "first" && File.ReadAllText(dest + ".meta") == "first-meta", "first save publishes without existing backup");
        }
        finally { Directory.Delete(dir, true); }
    }
}
