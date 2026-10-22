using System;
namespace CityPerformance
{
    public enum TraceScope { Refill, Stationary, Outside, Culling, Traffic, Tick, Save, Nearest }
    public struct TraceRow
    {
        public int Frame, Focused, FixedSteps, HourEvents, SaveEvents, Exceptions, QueueOverflow, Backlog;
        public int TreeRequested, TreeFaulted, TreeConfigured, TreeMapHold, TreeSetup;
        public int ZonesRequested, ZonesFaulted, RefillRequested, Verified;
        public double MonotonicSeconds, WallMs, RefillMs, StationaryMs, OutsideMs, CullingMs, TrafficMs, TickMs, SaveMs, NearestMs, OldestSeconds;
        public int Gc0, Gc1, Gc2, TrafficActive, TrafficCapacity, TrafficSampleFrame;
        public double TextureCurrentBytes, TextureDesiredBytes, TexturePending, TextureLoading, GpuLastNs, MainThreadLastNs, GcAllocLastBytes;
        public double GcLastNs,GcUsedBytes,GcReservedBytes,RenderThreadLastNs,PlayerLoopLastNs,ScriptsLastNs,LateScriptsLastNs,PhysicsLastNs,UiLastNs,PresentWaitLastNs;
        public double CameraCpuMs,BiggestCameraCpuMs;
        public int DetailRequested,DetailFaulted,DiagnosticSetup,CameraRenderCount,BiggestCameraId,RefillFaulted,RefillPending,RefillTarget,RefillActive,RefillRemaining;
        public static TraceRow Empty(int frame) => new TraceRow { Frame=frame, WallMs=double.NaN, MonotonicSeconds=double.NaN,
            TrafficActive=-1, TrafficCapacity=-1, TrafficSampleFrame=-1, TextureCurrentBytes=double.NaN, TextureDesiredBytes=double.NaN,
            TexturePending=double.NaN, TextureLoading=double.NaN, GpuLastNs=double.NaN, MainThreadLastNs=double.NaN, GcAllocLastBytes=double.NaN,
            GcLastNs=double.NaN,GcUsedBytes=double.NaN,GcReservedBytes=double.NaN,RenderThreadLastNs=double.NaN,PlayerLoopLastNs=double.NaN,
            ScriptsLastNs=double.NaN,LateScriptsLastNs=double.NaN,PhysicsLastNs=double.NaN,UiLastNs=double.NaN,PresentWaitLastNs=double.NaN,
            CameraCpuMs=double.NaN,BiggestCameraCpuMs=double.NaN,CameraRenderCount=-1,RefillTarget=-1,RefillActive=-1 };
    }
    // Retain earlier focused hitches after the recent-frame ring has rolled over.
    // Only completed samples enter this archive; no allocation or I/O on capture.
    public sealed class HitchArchive
    {
        private readonly TraceRow[] rows;
        private int count, smallest;
        public HitchArchive(int capacity)
        {
            if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));
            rows=new TraceRow[capacity];
        }
        public int Count => count;
        public void Add(TraceRow row)
        {
            if(row.Focused!=1 || double.IsNaN(row.WallMs) || double.IsInfinity(row.WallMs) || row.WallMs<=50)return;
            if(count<rows.Length)rows[count++]=row;
            else
            {
                if(row.WallMs<=rows[smallest].WallMs)return;
                rows[smallest]=row;
            }
            if(count==rows.Length)
            {
                smallest=0;
                for(int i=1;i<count;i++)if(rows[i].WallMs<rows[smallest].WallMs)smallest=i;
            }
        }
        public TraceRow[] Snapshot()
        {
            var result=new TraceRow[count];Array.Copy(rows,result,count);
            Array.Sort(result,(a,b)=>a.MonotonicSeconds.CompareTo(b.MonotonicSeconds));
            return result;
        }
        public void Clear(){count=smallest=0;}
    }
    public static class TraceCsv
    {
        public static void Write(System.IO.TextWriter writer,TraceRow[] rows)
        {
            writer.WriteLine("frame,focused,monotonic_seconds,wall_ms,fixed_steps,refill_ms,stationary_ms,outside_ms,culling_ms,traffic_ms,tick_ms,save_ms,nearest_ms,hour_events,save_events,exceptions,gc0_total,gc1_total,gc2_total,traffic_active,traffic_capacity,traffic_sample_frame,spawn_backlog,oldest_seconds,queue_overflow,texture_current_bytes,texture_desired_bytes,texture_pending,texture_loading,gpu_last_ns,main_thread_last_ns,gc_alloc_last_bytes,zones_requested,zones_faulted,refill_requested,verified_build,gc_last_ns,gc_used_bytes,gc_reserved_bytes,render_thread_last_ns,player_loop_last_ns,scripts_last_ns,late_scripts_last_ns,physics_last_ns,ui_last_ns,present_wait_last_ns,details_requested,details_faulted,diagnostic_setup,camera_render_count,camera_cpu_ms,biggest_camera_id,biggest_camera_cpu_ms,refill_faulted,refill_pending,refill_target,refill_active,refill_remaining,trees_requested,trees_faulted,trees_configured_records,trees_map_hold,trees_setup");
            foreach(var r in rows)writer.WriteLine(string.Join(",",new object[]{r.Frame,r.Focused,F(r.MonotonicSeconds),F(r.WallMs),r.FixedSteps,F(r.RefillMs),F(r.StationaryMs),F(r.OutsideMs),F(r.CullingMs),F(r.TrafficMs),F(r.TickMs),F(r.SaveMs),F(r.NearestMs),r.HourEvents,r.SaveEvents,r.Exceptions,r.Gc0,r.Gc1,r.Gc2,r.TrafficActive,r.TrafficCapacity,r.TrafficSampleFrame,r.Backlog,F(r.OldestSeconds),r.QueueOverflow,F(r.TextureCurrentBytes),F(r.TextureDesiredBytes),F(r.TexturePending),F(r.TextureLoading),F(r.GpuLastNs),F(r.MainThreadLastNs),F(r.GcAllocLastBytes),r.ZonesRequested,r.ZonesFaulted,r.RefillRequested,r.Verified,
                F(r.GcLastNs),F(r.GcUsedBytes),F(r.GcReservedBytes),F(r.RenderThreadLastNs),F(r.PlayerLoopLastNs),F(r.ScriptsLastNs),F(r.LateScriptsLastNs),F(r.PhysicsLastNs),F(r.UiLastNs),F(r.PresentWaitLastNs),r.DetailRequested,r.DetailFaulted,r.DiagnosticSetup,r.CameraRenderCount,F(r.CameraCpuMs),r.BiggestCameraId,F(r.BiggestCameraCpuMs),r.RefillFaulted,r.RefillPending,r.RefillTarget,r.RefillActive,r.RefillRemaining,r.TreeRequested,r.TreeFaulted,r.TreeConfigured,r.TreeMapHold,r.TreeSetup}));
        }
        private static string F(double value)=>double.IsNaN(value)?"NA":value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    }
    public sealed class TraceRing
    {
        private readonly TraceRow[] rows;
        private int index=-1, count;
        public TraceRing(int capacity) { if(capacity<1) throw new ArgumentOutOfRangeException(nameof(capacity)); rows=new TraceRow[capacity]; }
        public int Count => count;
        public int AvailableScopes { get; set; } = 255;
        public ref TraceRow At(int frame)
        {
            if(index<0 || rows[index].Frame != frame)
            { index=(index+1)%rows.Length; rows[index]=TraceRow.Empty(frame);
              if((AvailableScopes&1)==0)rows[index].RefillMs=double.NaN;
              if((AvailableScopes&2)==0)rows[index].StationaryMs=double.NaN;
              if((AvailableScopes&4)==0)rows[index].OutsideMs=double.NaN;
              if((AvailableScopes&8)==0)rows[index].CullingMs=double.NaN;
              if((AvailableScopes&16)==0)rows[index].TrafficMs=double.NaN;
              if((AvailableScopes&32)==0)rows[index].TickMs=double.NaN;
              if((AvailableScopes&64)==0)rows[index].SaveMs=double.NaN;
              if((AvailableScopes&128)==0)rows[index].NearestMs=double.NaN;
              count=Math.Min(count+1,rows.Length); }
            return ref rows[index];
        }
        public void AddCost(int frame, TraceScope scope, double ms, bool failed)
        {
            ref var r=ref At(frame);
            switch(scope)
            {
                case TraceScope.Refill:r.RefillMs+=ms;break; case TraceScope.Stationary:r.StationaryMs+=ms;break;
                case TraceScope.Outside:r.OutsideMs+=ms;break; case TraceScope.Culling:r.CullingMs+=ms;break;
                case TraceScope.Traffic:r.TrafficMs+=ms;break; case TraceScope.Tick:r.TickMs+=ms;break;
                case TraceScope.Save:r.SaveMs+=ms;break; case TraceScope.Nearest:r.NearestMs+=ms;break;
            }
            if(failed)r.Exceptions++;
        }
        public TraceRow[] Snapshot()
        {
            var result=new TraceRow[count]; int start=(index-count+1+rows.Length)%rows.Length;
            for(int i=0;i<count;i++)result[i]=rows[(start+i)%rows.Length];
            return result;
        }
        public void Clear(){index=-1;count=0;}
    }
}
