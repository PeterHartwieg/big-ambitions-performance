using System;
using System.IO;
using CityPerformance;
static class TreeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(TreeLodPolicy.TryThresholds(.25f, .004f, out float t) && t == .15f, "Manhattan keeps detailed trees until a smaller screen footprint");
        check(TreeLodPolicy.TryThresholds(.10872f, .004f, out t) && t == .10872f, "already conservative pine transition is preserved");
        check(TreeLodPolicy.TryThresholds(.20f, .035f, out t) && t == .15f, "Hamptons transition is also conservative");
        check(TreeLodPolicy.TryThresholds(.15f, 0, out t) && t == .15f, "already non-culling group remains supported");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,0f,1.01f})
            check(!TreeLodPolicy.TryThresholds(invalid,0,out t),"invalid first threshold rejected");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,.25f,.3f})
            check(!TreeLodPolicy.TryThresholds(.25f,invalid,out t),"invalid or reversed culling boundary rejected");
        int level=TreeLodPolicy.AfterMap(0,true,false,true);
        check(level==1,"normal map uses the original second mesh");
        check(TreeLodPolicy.AfterMap(level,false,false,false)==1,"inactive tree retains last vanilla forced level on map close");
        check(TreeLodPolicy.AfterMap(level,true,false,false)==0,"active tree returns to vanilla detailed baseline");
        check(TreeLodPolicy.AfterMap(0,true,true,true)==0 && TreeLodPolicy.AfterMap(1,true,true,false)==1,"low-detail map early return never invents a vanilla write");
        check(TreeMeshCatalog.Matches("TreeBroadleaf_L1_LOD0",5578,16704,"TreeBroadleaf_L1_LOD1",3897,9672),"audited Windows mesh pair qualifies");
        check(TreeMeshCatalog.Matches("Cypress_LOD0",3887,5811,"Cypress_LOD1",2506,3498),"Hamptons pair requires matching live geometry metadata");
        check(!TreeMeshCatalog.Matches("TreeBroadleaf_L1_LOD0",5578,16704,"TreeBroadleaf_XL2_LOD1",2625,5118),"mixed species are refused even when both meshes are known");
        check(!TreeMeshCatalog.Matches("Cypress_LOD0",3888,5811,"Cypress_LOD1",2506,3498),"changed high-detail geometry is refused");
        check(!TreeMeshCatalog.Matches("Cypress_LOD0",3887,5811,"Cypress_LOD1",2506,3499),"changed low-detail geometry is refused");
        check(!TreeMeshCatalog.Matches("new_LOD0",10,30,"new_LOD1",5,15),"uninspected asset family is refused");
        check(!TreeMeshCatalog.Matches(null,0,0,null,0,0),"missing mesh name is refused");
        using(var writer=new StringWriter())
        {
            var row=TraceRow.Empty(42);row.TreeRequested=1;row.TreeConfigured=4695;row.TreeSetup=1;
            TraceCsv.Write(writer,new[]{row});var lines=writer.ToString().Trim().Split('\n');var headers=lines[0].Trim().Split(',');var values=lines[1].Trim().Split(',');
            check(values[Array.IndexOf(headers,"trees_requested")]=="1" && values[Array.IndexOf(headers,"trees_configured_records")]=="4695" && values[Array.IndexOf(headers,"trees_setup")]=="1", "trace preserves settings and setup windows for matched A/B comparisons");
        }
    }
}
