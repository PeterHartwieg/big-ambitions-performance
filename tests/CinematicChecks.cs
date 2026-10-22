using System;
using CityPerformance;
static class CinematicChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(CinematicCore.Scale(float.NaN)==1 && CinematicCore.Scale(float.PositiveInfinity)==1, "non-finite distance setting fails to baseline");
        check(CinematicCore.Scale(-1)==1 && CinematicCore.Scale(500)==2 && CinematicCore.Scale(150)==1.5f, "distance range stays at 100–200 percent");
        check(CinematicCore.Band(59.9f,60,200)==0 && CinematicCore.Band(60,60,200)==1 && CinematicCore.Band(200,60,200)==2, "distance boundaries match native bands");
        for(int vanilla=0;vanilla<3;vanilla++)for(int camera=0;camera<3;camera++)for(int directional=0;directional<3;directional++)
            check(CinematicCore.Combine(vanilla,camera,directional)<=vanilla && CinematicCore.Combine(vanilla,camera,directional)<=camera, "either camera and the player retain their required detail");
        double needed=0;
        check(CinematicCore.Hold(0,2,ref needed,10,true)==0 && needed==10, "newly required detail activates immediately");
        check(CinematicCore.Hold(2,0,ref needed,11.9,true)==0, "brief turn keeps previous detail warm");
        check(CinematicCore.Hold(2,0,ref needed,12,true)==2, "detail releases after grace period");
        check(CinematicCore.Hold(2,0,ref needed,10.1,false)==2, "disable bypasses hold to restore native behavior");
        check(CinematicCore.Hold(1,0,ref needed,13,true)==1, "downgrade can retain intermediate distance band");
        check(CinematicCore.SphereInside(-4,4) && !CinematicCore.SphereInside(-4.01f,4), "large objects intersecting a viewing edge stay active");
    }
}
