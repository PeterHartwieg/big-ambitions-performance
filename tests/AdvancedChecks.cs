using System;
using CityPerformance;
static class AdvancedChecks
{
    public static void Run(Action<bool,string> check)
    {
        var b=new StepBudget(16,4,2);
        var tickets=new StepBudget.Ticket[12];var remaining=new int[12];
        for(int i=0;i<tickets.Length;i++){tickets[i]=b.Register(new object(),0);remaining[i]=5;}
        int total=0;
        for(int frame=1;frame<100 && b.Count>0;frame++)
        {
            int grants=0;
            for(int i=0;i<tickets.Length;i++)
            {
                if(!b.TryBegin(tickets[i],frame))continue;
                grants++;total++;remaining[i]--;b.End(.7);
                if(remaining[i]==0)b.Cancel(tickets[i]);
                check(!b.TryBegin(tickets[i],frame),"one step per zone per rendered frame");
            }
            check(grants<=3,"shared elapsed budget across independently resumed coroutines");
        }
        check(total==60 && b.Count==0,"fair eventual completion of all zone groups");
        var owner=new object();var first=b.Register(owner,10);var second=b.Register(owner,11);
        check(!first.Active && second.Active && b.Count==1,"replacement invalidates original zone ticket");
        check(!b.TryBegin(first,100),"cancelled ticket cannot activate");
        b.CancelOwner(new object());check(second.Active,"unrelated owner cannot cancel ticket");
        b.CancelOwner(owner);check(!second.Active && b.Count==0,"clear releases exact owner ticket");
        var dead=new object();var live=new object();var deadTicket=b.Register(dead,1);var liveTicket=b.Register(live,2);
        b.RemoveInvalid(x=>!ReferenceEquals(x,dead));
        check(!deadTicket.Active && b.TryBegin(liveTicket,101),"destroyed zone cannot block queue head");
        b.End(8);var other=b.Register(new object(),2);
        check(!b.TryBegin(other,101),"single expensive step prevents additional work in same frame");
        check(b.TryBegin(liveTicket,102) && b.TryBegin(other,102),"budget resets next frame while preserving FIFO fairness");
        b.Clear();check(!other.Active && !liveTicket.Active && b.Count==0,"load/map cancellation invalidates all pending tickets");
        var capped=new StepBudget(1,1,1);var c=capped.Register(owner,0);
        check(capped.Register(new object(),0)==null,"queue capacity is bounded");
        var replacement=capped.Register(owner,2);check(replacement!=null && !c.Active,"replacement permitted at capacity");
        check(capped.OldestAge(5)==3,"backlog age uses creation time");
        var faultBudget=new StepBudget(2,2,1);var f=faultBudget.Register(new object(),0);var next=faultBudget.Register(new object(),0);
        check(faultBudget.TryBegin(f,1),"failed operation granted once");
        try{try{throw new InvalidOperationException();}finally{faultBudget.End(2);faultBudget.Cancel(f);}}catch(InvalidOperationException){}
        check(!faultBudget.TryBegin(next,1) && faultBudget.TryBegin(next,2),"exception work accounted without poisoning next frame");
        var ring=new TraceRing(3);
        for(int i=10;i<15;i++){ring.At(i).FixedSteps=i-10;ring.AddCost(i,TraceScope.Stationary,1.25,false);ring.AddCost(i,TraceScope.Stationary,.5,true);}
        var snap=ring.Snapshot();
        check(ring.Count==3 && snap[0].Frame==12 && snap[2].Frame==14,"trace rollover retains newest frames chronologically");
        check(snap[2].StationaryMs==1.75 && snap[2].Exceptions==1 && snap[2].FixedSteps==4,"cost and exception correlation stays on rendered frame");
        check(double.IsNaN(snap[0].GpuLastNs) && snap[0].TrafficCapacity==-1,"unavailable counters are not zero cost");
        ring.At(14).StationaryMs=99;check(snap[2].StationaryMs==1.75,"export snapshot independent from live ring");
        ring.Clear();ring.AvailableScopes=1;ring.At(20).RefillMs=2;
        check(ring.Count==1 && double.IsNaN(ring.Snapshot()[0].NearestMs) && ring.Snapshot()[0].RefillMs==2,"missing hook scope is explicitly unavailable");
        ring.Clear();check(ring.Count==0 && ring.Snapshot().Length==0,"city load removes old trace data");
        var archive=new HitchArchive(3);
        var row=TraceRow.Empty(1);row.Focused=1;row.WallMs=50;archive.Add(row);
        row.WallMs=double.NaN;archive.Add(row);row.WallMs=double.PositiveInfinity;archive.Add(row);
        row.WallMs=1000;row.Focused=0;archive.Add(row);
        check(archive.Count==0,"archive excludes invalid, background and non-hitch samples");
        row.Focused=1;
        for(int i=0;i<10;i++){row.Frame=i;row.MonotonicSeconds=i;row.WallMs=60+i;row.StationaryMs=i;archive.Add(row);}
        var worst=archive.Snapshot();
        check(archive.Count==3 && worst[0].Frame==7 && worst[2].Frame==9,"bounded archive retains slowest samples chronologically");
        row.Frame=11;row.MonotonicSeconds=11;row.WallMs=500;archive.Add(row);
        check(worst[0].Frame==7 && archive.Snapshot()[0].Frame==8,"hitch export is independent of live capture");
        row.Frame=12;row.MonotonicSeconds=12;row.WallMs=51;archive.Add(row);
        check(archive.Snapshot()[0].Frame==8,"smaller later hitches cannot displace worse early hitches");
        check(archive.Snapshot()[0].StationaryMs==8,"retained hitch preserves frame-correlated scope costs");
        archive.Clear();check(archive.Count==0 && archive.Snapshot().Length==0,"teardown clears previous session hitches");
        row=TraceRow.Empty(42);row.Focused=1;row.WallMs=16.25;row.ZonesRequested=1;row.Verified=1;
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("de-DE");
            using(var writer=new System.IO.StringWriter())
            {
                TraceCsv.Write(writer,new[]{row});
                var lines=writer.ToString().Trim().Split('\n');
                var headers=lines[0].Trim().Split(',');var values=lines[1].Trim().Split(',');
                check(headers.Length==63 && values.Length==headers.Length,"CSV preserves exact column count under decimal-comma culture");
                check(values[3]=="16.25" && values[29]=="NA","CSV preserves invariant decimals and unavailable recorder data");
                check(values[32]=="1" && values[33]=="0" && values[35]=="1","CSV records optimization selection and verified-build status");
                check(values[36]=="NA" && values[41]=="NA" && values[49]=="-1" && values[50]=="NA","unavailable extended timing/camera data stays distinct from zero");
            }
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        var detail=new DetailStore(3,2);
        detail.Add(new DetailSample{Method=0,Frame=1,Ms=.1},false);
        check(detail.Count==0 && detail.Counter(0).Calls==1,"small discovery calls contribute counters without filling detail ring");
        detail.Add(new DetailSample{Method=0,Frame=2,Ms=.1},true);
        detail.Add(new DetailSample{Method=1,Frame=2,Ms=.01,Failed=1},false);
        check(detail.Count==2 && detail.Counter(1).Errors==1,"hourly work and exceptions retained even below threshold");
        detail.Add(new DetailSample{Method=0,Frame=3,Ms=25},true);
        var previous=detail.Snapshot();
        detail.Add(new DetailSample{Method=1,Frame=4,Ms=60},true);
        check(detail.Count==3 && detail.Overwritten==1 && detail.Snapshot()[0].Frame==2 && detail.Snapshot()[2].Frame==4,"detail ring rolls over chronologically with explicit lost sample count");
        check(previous[0].Frame==2 && previous[2].Frame==3,"detail export is independent of later writes");
        check(detail.Counter(0).Calls==3 && detail.Counter(0).MaxMs==25 && detail.Counter(1).MaxMs==60,"summary retains all calls independently of sample retention");
        detail.Add(new DetailSample{Method=0,Ms=double.NaN},true);detail.Add(new DetailSample{Method=0,Ms=-2},true);detail.Add(new DetailSample{Method=9,Ms=1},true);
        check(detail.Counter(0).Calls==3 && detail.Count==3,"invalid timings and IDs cannot corrupt detail data");
        culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("de-DE");
            using(var writer=new System.IO.StringWriter())
            {
                DetailCsv.Write(writer,new[]{new DetailSample{Method=1,Frame=20,Day=3,Hour=6,StartSeconds=1.25,Ms=2.75,Failed=1}});
                var lines=writer.ToString().Trim().Split('\n');
                check(lines[1].Trim()=="1,20,1.25,3,6,2.75,1,0","method export keeps invariant numbers and frame/hour/daily context");
            }
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        detail.Clear();check(detail.Count==0 && detail.Overwritten==0 && detail.Counter(0).Calls==0,"city reset clears detail samples and counters");
    }
}
