using System;
namespace CityPerformance
{
    public enum CleanupReason { None, Hidden, MemoryPressure, Deadline, MemoryUnavailable }

    // One bounded batch. Neither later midnights nor increasing the setting
    // may postpone an already pending request.
    public sealed class CleanupPlan
    {
        public const double MinimumDelaySeconds = 60, DefaultDelaySeconds = 300, MaximumDelaySeconds = 600;
        public const long GrowthLimitBytes = 256L * 1024 * 1024;
        public double DelaySeconds { get; private set; } = DefaultDelaySeconds;
        public bool Pending { get; private set; }
        public double RequestedAt { get; private set; }
        public double DeadlineAt { get; private set; }
        public long MemoryAtRequest { get; private set; }
        public long RequestsInBatch { get; private set; }
        public void SetDelaySeconds(double value)
        {
            DelaySeconds = double.IsNaN(value) || double.IsInfinity(value) ? DefaultDelaySeconds :
                Math.Max(MinimumDelaySeconds, Math.Min(MaximumDelaySeconds, value));
            if (Pending) DeadlineAt = Math.Min(DeadlineAt, RequestedAt + DelaySeconds);
        }
        public bool Request(double now, long memory)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || memory <= 0) throw new ArgumentOutOfRangeException();
            if (Pending) { if (RequestsInBatch < long.MaxValue) RequestsInBatch++; return false; }
            Pending = true; RequestedAt = now; DeadlineAt = now + DelaySeconds;
            MemoryAtRequest = memory; RequestsInBatch = 1; return true;
        }
        public CleanupReason Due(double now, long memory, bool hidden, bool pressure, bool busy)
        {
            if (!Pending || busy) return CleanupReason.None;
            if (memory <= 0) return CleanupReason.MemoryUnavailable;
            if (pressure || memory >= MemoryAtRequest && memory - MemoryAtRequest >= GrowthLimitBytes) return CleanupReason.MemoryPressure;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < RequestedAt || now >= DeadlineAt) return CleanupReason.Deadline;
            return hidden ? CleanupReason.Hidden : CleanupReason.None;
        }
        public void Clear() { Pending = false; RequestedAt = DeadlineAt = 0; MemoryAtRequest = RequestsInBatch = 0; }
    }
}
