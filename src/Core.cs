using System;

namespace CityPerformance
{
    // Generation check for callbacks that cannot be unregistered from the game's late-load queue.
    public sealed class SessionLifetime
    {
        private int generation;
        private bool active;
        public int Begin() { active = true; return ++generation; }
        public bool IsCurrent(int ticket) => active && ticket == generation;
        public void End() { active = false; }
    }

    // One pending target, not a new timer for every request. No game objects or allocations here.
    public sealed class RefillPlan
    {
        public bool Pending { get; private set; }
        public int Target { get; private set; }
        public int Remaining { get; private set; }
        public int Request(int target, int count, int batch)
        {
            Target = target;
            Remaining = Math.Max(0, target - count);
            Pending = Remaining > 0;
            return Next(count, batch);
        }
        public int Next(int count, int batch)
        {
            if (batch < 1) throw new ArgumentOutOfRangeException(nameof(batch));
            if (count >= Target || Remaining == 0) { Pending = false; return count; }
            return count + Math.Min(Remaining, Math.Min(batch, Target - count));
        }
        public void Consume(int attempts)
        {
            if (attempts < 0) throw new ArgumentOutOfRangeException(nameof(attempts));
            Remaining = Math.Max(0, Remaining - attempts);
            Pending = Remaining > 0;
        }
        public void Cancel() { Pending = false; Remaining = 0; }
    }

    public sealed class FrameQuota
    {
        private readonly int limit;
        private int frame = -1, used;
        public FrameQuota(int limit) { if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit)); this.limit = limit; }
        public int Take(int frameNumber, int requested)
        {
            if (frame != frameNumber) { frame = frameNumber; used = 0; }
            int allowed = Math.Max(0, Math.Min(limit - used, requested));
            used += allowed;
            return allowed;
        }
    }

    public sealed class FrameWindow
    {
        public FrameHistogram Frames { get; } = new FrameHistogram();
        private readonly double interval;
        private double lastReport, lastFrame;
        private bool wasRecording;
        public FrameWindow(double intervalSeconds)
        {
            if (intervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            interval = intervalSeconds;
        }
        public bool Observe(double now, bool recording)
        {
            if (!recording || !wasRecording || now < lastFrame)
            {
                Frames.Clear(); lastFrame = lastReport = now; wasRecording = recording; return false;
            }
            Frames.Add((now - lastFrame) * 1000); lastFrame = now;
            if (now - lastReport < interval) return false;
            lastReport = now;
            return true;
        }
    }

    // Fixed memory; percentile is the upper edge of a 1ms bin, overflow reported separately.
    public sealed class FrameHistogram
    {
        private readonly long[] bins = new long[251];
        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Max { get; private set; }
        public long Over50 { get; private set; }
        public long Overflow => bins[250];
        public void Add(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0) return;
            Count++; Sum += ms; Max = Math.Max(Max, ms);
            if (ms > 50) Over50++;
            bins[Math.Min(250, (int)Math.Min(250, Math.Ceiling(ms)))]++;
        }
        public double Percentile(double quantile)
        {
            if (Count == 0) return 0;
            long rank = Math.Max(1, (long)Math.Ceiling(Count * quantile)), total = 0;
            for (int i = 0; i < bins.Length; i++) { total += bins[i]; if (total >= rank) return i; }
            return 250;
        }
        public void Clear() { Array.Clear(bins, 0, bins.Length); Count = Over50 = 0; Sum = Max = 0; }
    }
}
