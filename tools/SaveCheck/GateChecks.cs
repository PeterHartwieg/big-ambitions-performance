using System.Reflection;
using HarmonyLib;

static class GateChecks
{
    internal static void Run(Assembly mod)
    {
        var gate = mod.GetType("CityPerformance.SaveSerializationGate", true);
        var coordinator = mod.GetType("CityPerformance.BackgroundSaves", true);
        var run = AccessTools.Method(gate, "Run");
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS gate: " + label); }
        void Call(string name, params object[] args) => AccessTools.Method(gate, name).Invoke(null, args);
        bool Busy() => (bool)AccessTools.PropertyGetter(gate, "Busy").Invoke(null, null);
        var entered = new object[] { false }; Call("Enter", entered);
        using var started = new ManualResetEventSlim(); using var finished = new ManualResetEventSlim();
        Exception failure = null;
        var worker = new Thread(() => { try { started.Set(); run.Invoke(null, new object[] { (Action)(() => finished.Set()) }); } catch (Exception e) { failure = e; } });
        worker.Start(); started.Wait();
        Check(!finished.Wait(30), "native synchronous serializer excludes worker serialization");
        var original = new IOException("native serializer exception");
        var propagated = AccessTools.Method(gate, "Exit").Invoke(null, new object[] { true, original });
        Check(ReferenceEquals(propagated, original), "native serializer exception identity retained");
        Check(worker.Join(2000) && failure == null && finished.IsSet, "native completion releases worker gate");
        foreach (string completion in new[] { "success", "fault", "cancel", "outer-exception" })
        {
            var task = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var state = new object[] { false }; Call("EnterAsync", state);
            Check((bool)state[0] && Busy(), "async native operation reserves gate: " + completion);
            bool refused = false;
            try { run.Invoke(null, new object[] { (Action)(() => throw new Exception("must not serialize")) }); }
            catch (TargetInvocationException e) { refused = e.InnerException is NotSupportedException; }
            Check(refused, "worker refuses overlap with native async load: " + completion);
            Call("ExitAsync", true, task.Task, completion == "outer-exception" ? original : null);
            Check(Busy(), "native task remains reserved until completion: " + completion);
            if (completion == "fault") task.SetException(new IOException("native async failure"));
            else if (completion == "cancel") task.SetCanceled();
            else task.SetResult(7);
            Check(SpinWait.SpinUntil(() => !Busy(), 2000), "native task releases reservation: " + completion);
            if (task.Task.IsFaulted) _ = task.Task.Exception;
        }
        Call("EnterAsync", new object[] { false });
        Call("ExitAsync", true, null, original);
        Check(!Busy(), "synchronous failure before native task returns releases reservation");
        Check(AccessTools.Field(coordinator, "pending").GetValue(null) == null, "gate tests leave no save job");
        var jobType = coordinator.GetNestedType("Job", BindingFlags.NonPublic);
        foreach (var initialPriority in new[] { ThreadPriority.BelowNormal, ThreadPriority.Highest })
        {
            var job = Activator.CreateInstance(jobType, true);
            bool correctPriority = false;
            var draining = new Thread(() => {
                SpinWait.SpinUntil(() => (bool)AccessTools.Field(jobType, "Draining").GetValue(job), 2000);
                lock (AccessTools.Field(jobType, "PriorityLock").GetValue(job))
                    correctPriority = Thread.CurrentThread.Priority == (initialPriority == ThreadPriority.Highest ? ThreadPriority.Highest : ThreadPriority.Normal);
                AccessTools.Field(jobType, "Done").SetValue(job, true);
            });
            draining.Priority = initialPriority;
            AccessTools.Field(jobType, "Thread").SetValue(job, draining);
            ((ManualResetEventSlim)AccessTools.Field(jobType, "Ready").GetValue(job)).Set();
            AccessTools.Field(coordinator, "pending").SetValue(null, job);
            draining.Start(); AccessTools.Method(coordinator, "Drain").Invoke(null, null);
            Check(correctPriority && !draining.IsAlive && AccessTools.Field(coordinator, "pending").GetValue(null) == null,
                "blocking lifecycle drain completes job without lowering priority: " + initialPriority);
        }
        Console.WriteLine($"PASS {count} native serialization gate checks");
    }
}
