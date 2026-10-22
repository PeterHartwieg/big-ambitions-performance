using System;
using System.Threading;
using System.Threading.Tasks;

namespace CityPerformance
{
    // The audited game also uses Odin for feedback and asynchronous loading.
    // Native entry hooks share this gate with worker serialization and prewarming.
    internal static class SaveSerializationGate
    {
        private static readonly object sync = new object();
        private static int asynchronous;
        internal static bool Busy => Volatile.Read(ref asynchronous) != 0;
        internal static void Run(Action action)
        {
            lock (sync)
            {
                if (asynchronous != 0) throw new NotSupportedException("Native asynchronous deserialization is active");
                action();
            }
        }
        internal static void Enter(out bool __state)
        {
            __state = false; BackgroundSaves.Drain();
            Monitor.Enter(sync); __state = true;
        }
        internal static Exception Exit(bool __state, Exception __exception)
        {
            if (__state) Monitor.Exit(sync);
            return __exception;
        }
        internal static void EnterAsync(out bool __state)
        {
            __state = false; BackgroundSaves.Drain();
            lock (sync) { asynchronous++; __state = true; }
        }
        internal static Exception ExitAsync(bool __state, Task __result, Exception __exception)
        {
            if (__state)
            {
                if (__result == null) Complete();
                else __result.ContinueWith(_ => Complete(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return __exception;
        }
        private static void Complete() { lock (sync) asynchronous--; }
    }
}
