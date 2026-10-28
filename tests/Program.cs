using System;
using CityPerformance;
class Tests
{
    static int checks;
    static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
    static void Main()
    {
        SaveChecks.Run((ok, label) => { Check(ok, label); Console.WriteLine("PASS save: " + label); });
        var lifetime = new SessionLifetime();
        Check(!lifetime.IsCurrent(0), "no callback before initialization");
        int first = lifetime.Begin();
        Check(lifetime.IsCurrent(first), "current callback accepted");
        lifetime.End();
        Check(!lifetime.IsCurrent(first), "late callback after unload rejected");
        int second = lifetime.Begin();
        Check(!lifetime.IsCurrent(first) && lifetime.IsCurrent(second), "old callback cannot affect new city");
        lifetime.End(); lifetime.End();
        Check(!lifetime.IsCurrent(second), "repeated teardown is safe");
        var p = new RefillPlan();
        Check(p.Request(100, 0, 2) == 2 && p.Target == 100 && p.Pending, "large refill bounded, target retained");
        p.Consume(2);
        int count = 2, steps = 0;
        while (p.Pending) { int next = p.Next(count, 2); Check(next-count <= 2, "batch bound"); p.Consume(next-count); count=next; steps++; }
        Check(count == 100 && steps == 49, "eventual exact convergence");
        p.Request(100, 5, 2); Check(p.Request(8, 5, 2) == 7 && p.Target == 8, "new lower target supersedes");
        Check(p.Request(1, 5, 2) == 5 && !p.Pending, "decrease cancels refill");
        Check(p.Request(0, 0, 2) == 0 && !p.Pending, "zero target");
        p.Request(10, 0, 2); p.Cancel(); Check(!p.Pending, "unload cancellation");
        p.Request(10, 0, 2); Check(p.Next(20, 2) == 20 && !p.Pending, "external spawn convergence");
        p.Request(6, 2, 2); p.Consume(2);
        Check(p.Remaining == 2 && p.Next(0, 10) == 2, "departures do not expand original acquisition budget");
        p.Consume(2);
        Check(!p.Pending && p.Next(0, 2) == 0, "no endless refill if active count keeps falling");
        p.Request(20, 2, 2); p.Cancel();
        Check(p.Remaining == 0 && !p.Pending, "teardown removes unused acquisition budget");
        var q = new FrameQuota(2);
        Check(q.Take(42, 1)==1 && q.Take(42, 2)==1 && q.Take(42, 2)==0, "repeated requests share cap");
        Check(q.Take(43, 10)==2 && q.Take(43, 1)==0, "frame rollover");
        Check(q.Take(44, -10)==0 && q.Take(44, 2)==2, "bad request cannot refund quota");
        var window = new FrameWindow(30);
        Check(!window.Observe(1000, true) && window.Frames.Count == 0, "no first-frame uptime spike or immediate report");
        Check(!window.Observe(1000.016, true) && window.Frames.Count == 1, "consecutive frame cadence");
        Check(window.Observe(1030, true), "full wall-clock window before report");
        window.Frames.Clear();
        Check(!window.Observe(1030.016, true) && window.Frames.Count == 1, "report preserves next frame interval");
        Check(!window.Observe(1050, false) && window.Frames.Count == 0, "focus/load suspension drops partial window");
        Check(!window.Observe(3000, true) && window.Frames.Count == 0, "resume does not count background gap");
        Check(!window.Observe(2999, true) && window.Frames.Count == 0, "clock reset starts fresh window");
        var h = new FrameHistogram();
        for(int i=0;i<95;i++) h.Add(16.2);
        for(int i=0;i<4;i++) h.Add(51);
        h.Add(1234); h.Add(double.NaN); h.Add(-1); h.Add(double.PositiveInfinity);
        Check(h.Count==100 && h.Percentile(.95)==17 && h.Percentile(.99)==51, "nearest-rank bins");
        Check(h.Over50==5 && h.Overflow==1 && h.Max==1234, "hitches and overflow retained");
        h.Clear(); Check(h.Count==0 && h.Max==0 && h.Overflow==0 && h.Percentile(.99)==0, "reset");
        AdvancedChecks.Run(Check);
        CleanupChecks.Run(Check);
        TreeChecks.Run(Check);
        CinematicChecks.Run(Check);
        RefillOwnershipChecks.Run(Check);
        Console.WriteLine($"PASS: {checks} checks");
    }
}
