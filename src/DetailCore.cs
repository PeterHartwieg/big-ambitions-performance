using System;
using System.Globalization;
using System.IO;
namespace CityPerformance
{
    public struct DetailSample
    {
        public int Method, Frame, Day, Hour, Failed, DayBoundary;
        public double StartSeconds, Ms;
    }
    public struct DetailCounter { public long Calls, Errors; public double TotalMs, MaxMs; }
    // Fixed storage. Method names live separately and are only formatted during export.
    public sealed class DetailStore
    {
        private readonly DetailSample[] samples;
        private readonly DetailSample[] daily;
        private readonly DetailCounter[] counters;
        private int next, count;
        private int dailyNext, dailyCount;
        public long Overwritten { get; private set; }
        public long DailyOverwritten { get; private set; }
        public int DailyCount=>dailyCount;
        public int Count=>count+dailyCount;
        public DetailStore(int capacity, int methodCapacity, int dailyCapacity=0)
        {
            if(capacity<1 || methodCapacity<1 || dailyCapacity<0)throw new ArgumentOutOfRangeException();
            samples=new DetailSample[capacity];counters=new DetailCounter[methodCapacity];
            daily=new DetailSample[dailyCapacity];
        }
        public void Add(DetailSample sample, bool retainAll)
        {
            if(sample.Method<0 || sample.Method>=counters.Length || !Valid(sample.Ms))return;
            ref var c=ref counters[sample.Method];
            c.Calls++;c.TotalMs+=sample.Ms;c.MaxMs=Math.Max(c.MaxMs,sample.Ms);if(sample.Failed!=0)c.Errors++;
            if(sample.DayBoundary!=0 && daily.Length>0)
            {
                daily[dailyNext]=sample;dailyNext=(dailyNext+1)%daily.Length;
                if(dailyCount<daily.Length)dailyCount++;else DailyOverwritten++;
                return;
            }
            if(!retainAll && sample.Ms<1 && sample.Failed==0)return;
            samples[next]=sample;next=(next+1)%samples.Length;
            if(count<samples.Length)count++;else Overwritten++;
        }
        private static bool Valid(double ms)=>!double.IsNaN(ms) && !double.IsInfinity(ms) && ms>=0;
        public DetailSample[] Snapshot()
        {
            var result=new DetailSample[Count];int start=(next-count+samples.Length)%samples.Length;
            for(int i=0;i<count;i++)result[i]=samples[(start+i)%samples.Length];
            if(dailyCount>0)
            {
                start=(dailyNext-dailyCount+daily.Length)%daily.Length;
                for(int i=0;i<dailyCount;i++)result[count+i]=daily[(start+i)%daily.Length];
                Array.Sort(result,(a,b)=> { int order=a.Frame.CompareTo(b.Frame);return order!=0?order:a.StartSeconds.CompareTo(b.StartSeconds); });
            }
            return result;
        }
        public DetailCounter Counter(int id)=>counters[id];
        public void Clear(){next=count=dailyNext=dailyCount=0;Overwritten=DailyOverwritten=0;Array.Clear(counters,0,counters.Length);}
    }
    public static class DetailCsv
    {
        public static void Write(TextWriter writer,DetailSample[] samples)
        {
            writer.WriteLine("method_id,frame,start_monotonic_seconds,day,hour,inclusive_ms,exception,day_boundary");
            foreach(var s in samples)writer.WriteLine(string.Join(",",s.Method,s.Frame,s.StartSeconds.ToString("R",CultureInfo.InvariantCulture),s.Day,s.Hour,s.Ms.ToString("R",CultureInfo.InvariantCulture),s.Failed,s.DayBoundary));
        }
    }
}
