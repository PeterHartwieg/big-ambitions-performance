using System;
using System.Linq;
using CityPerformance;
static class CleanupChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var p=new CleanupPlan();
        p.SetDelaySeconds(60); // Preserve the previous one-minute mode as a selectable baseline.
        check(p.Due(0,0,true,true,false)==CleanupReason.None,"no invented cleanup before a request");
        check(p.Request(10,100),"first midnight queues cleanup");
        check(!p.Request(69,200) && p.RequestedAt==10 && p.MemoryAtRequest==100,"later midnight coalesces without postponing deadline or memory baseline");
        check(p.Due(69.999,100,false,false,false)==CleanupReason.None,"visible play waits below the bound");
        check(p.Due(70,100,false,false,false)==CleanupReason.Deadline,"continuous driving eventually runs cleanup");
        check(p.Due(20,100,true,false,false)==CleanupReason.Hidden,"opaque screen is an early opportunity");
        check(p.Due(20,100,false,true,false)==CleanupReason.MemoryPressure,"low-memory signal ends deferral");
        check(p.Due(20,100+CleanupPlan.GrowthLimitBytes,false,false,false)==CleanupReason.MemoryPressure,"native growth ends deferral");
        check(p.Due(20,99,false,false,false)==CleanupReason.None,"falling memory does not wrap into pressure");
        check(p.Due(80,100,true,true,true)==CleanupReason.None,"active save or earlier cleanup takes precedence");
        check(p.Due(80,100,false,false,false)==CleanupReason.Deadline,"overdue cleanup executes when blocker clears");
        check(p.Due(9,100,false,false,false)==CleanupReason.Deadline,"clock reset cannot strand cleanup");
        p.Clear();p.Clear();check(!p.Pending,"drain and repeated teardown remove pending state");
        check(p.Request(100,200),"next city can start a fresh request");
        p.Clear();
        try{p.Request(double.NaN,0);check(false,"NaN request rejected");}catch(ArgumentOutOfRangeException){check(!p.Pending,"invalid request cannot strand a pending cleanup");}

        var batch=new CleanupPlan();
        check(batch.DelaySeconds==300,"new experiment defaults to five minutes");
        batch.Request(10,100);
        batch.Request(210,150);
        check(batch.RequestsInBatch==2 && batch.RequestedAt==10 && batch.MemoryAtRequest==100 && batch.DeadlineAt==310,
            "two midnights share the oldest request, deadline and memory baseline");
        check(batch.Due(70,150,false,false,false)==CleanupReason.None,"old one-minute deadline no longer forces a sweep");
        check(batch.Due(309.999,150,false,false,false)==CleanupReason.None &&
            batch.Due(310,150,false,false,false)==CleanupReason.Deadline,"five-minute batch still has a hard deadline");
        batch.SetDelaySeconds(600);
        check(batch.DeadlineAt==310,"increasing the slider cannot extend pending work");
        batch.SetDelaySeconds(60);
        check(batch.DeadlineAt==70 && batch.Due(210,150,false,false,false)==CleanupReason.Deadline,
            "reducing the slider shortens the pending deadline");
        check(batch.Due(500,150,true,true,true)==CleanupReason.None,"save blocker also takes precedence over a shortened deadline");
        batch.Clear();
        check(batch.RequestsInBatch==0 && batch.DeadlineAt==0 && batch.DelaySeconds==60,"draining forgets the batch but keeps configuration");
        batch.SetDelaySeconds(-100);check(batch.DelaySeconds==60,"negative preference clamps to one minute");
        batch.SetDelaySeconds(double.MaxValue);check(batch.DelaySeconds==600,"oversized preference clamps to ten minutes");
        batch.SetDelaySeconds(double.NaN);check(batch.DelaySeconds==300,"invalid numeric preference uses bounded default");
        batch.SetDelaySeconds(double.PositiveInfinity);check(batch.DelaySeconds==300,"infinite preference cannot remove deadline");
        batch.Request(1000,100);
        check(batch.Due(1001,0,false,false,false)==CleanupReason.MemoryUnavailable &&
            batch.Due(1001,-1,false,false,false)==CleanupReason.MemoryUnavailable,"missing memory telemetry ends batching");
        check(batch.Due(1001,0,true,true,true)==CleanupReason.None,"missing telemetry does not overlap an active save");
        check(batch.Due(1001,100+CleanupPlan.GrowthLimitBytes-1,false,false,false)==CleanupReason.None &&
            batch.Due(1001,100+CleanupPlan.GrowthLimitBytes,false,false,false)==CleanupReason.MemoryPressure,
            "larger batching window retains the 256 MiB growth guard");
        check(batch.Due(1001,100,true,false,false)==CleanupReason.Hidden,"hidden opportunity runs well before five minutes");
        batch.Clear();
        foreach(long memory in new long[]{0,-1})
        {
            try{batch.Request(1000,memory);check(false,"missing memory baseline rejected");}
            catch(ArgumentOutOfRangeException){check(!batch.Pending,"failed telemetry cannot create an unbounded request");}
        }
        // Accelerated-day scenario: real elapsed time, not frame count or simulated
        // game time. No hidden frames or pressure, and no lost midnight request.
        int sweeps=0;long covered=0;
        for(int t=0;t<=2100;t++)
        {
            if(t<2000 && t%200==0)batch.Request(t,100);
            if(batch.Due(t,100,false,false,false)!=CleanupReason.None)
            {sweeps++;covered+=batch.RequestsInBatch;batch.Clear();}
        }
        check(sweeps==5 && covered==10 && !batch.Pending,"ten two-hundred-second days coalesce to five sweeps without losing a request");
        batch.Request(2200,100);
        check(batch.Due(2199,100,false,false,false)==CleanupReason.Deadline,"clock rollback still cannot strand a batched request");

        var d=new DetailStore(4,3,3);
        d.Add(new DetailSample{Method=0,Frame=10,Day=2,Hour=0,Ms=277,DayBoundary=1},false);
        d.Add(new DetailSample{Method=1,Frame=10,Day=2,Hour=0,Ms=.01,DayBoundary=1},false);
        for(int i=0;i<250000;i++)d.Add(new DetailSample{Method=2,Frame=i+11,Ms=.001},false);
        check(d.Count==2 && d.DailyCount==2 && d.Counter(2).Calls==250000,"frequent shared callbacks cannot erase midnight samples; all still counted");
        for(int i=0;i<100;i++)d.Add(new DetailSample{Method=2,Frame=i+300000,Ms=2},false);
        var snapshot=d.Snapshot();
        check(snapshot.Length==6 && snapshot[0].Method==0 && snapshot[1].Method==1,"normal slow-call overflow cannot evict daily context");
        check(d.Overwritten==96 && d.DailyOverwritten==0,"independent overwrite counts");
        d.Add(new DetailSample{Method=1,Frame=400000,Ms=.01,Failed=1,DayBoundary=1},false);
        d.Add(new DetailSample{Method=0,Frame=400001,Ms=60,DayBoundary=1},false);
        check(d.DailyCount==3 && d.DailyOverwritten==1 && d.Counter(1).Errors==1,"daily capacity is bounded and preserves failure accounting");
        check(snapshot[0].Ms==277,"export snapshot survives subsequent daily overwrite");
        check(d.Snapshot().Count(x=>x.DayBoundary!=0)==3,"export carries explicit day-boundary context");
        d.Clear();check(d.Count==0 && d.DailyCount==0 && d.DailyOverwritten==0 && d.Counter(2).Calls==0,"new city clears both rings and counters");
    }
}
