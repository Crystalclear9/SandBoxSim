using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private void AddResidentTailoring(ResidentRig rig,string cloth,string skin,JobType job)
    {
        // Curved hem stitches follow the existing garment, with a back vent and belt fastening.
        for(int i=0;i<24;i++)
        {
            float a=i*MathF.Tau/24;
            Detail(rig.Torso,"sphere",new(MathF.Cos(a)*.201f,.625f,MathF.Sin(a)*.146f),new(.004f,.005f,.004f),cloth);
        }
        Detail(rig.Torso,"box",new(.021f,.737f,-.15f),new(.033f,.075f,.012f),"#514237");
        foreach(float side in new[]{-1f,1f})
            FineBeam(rig.Torso,new(side*.012f,1.21f,-.123f),new(side*.023f,1.12f,-.161f),.007f,"#c2b69c");
        if(job is JobType.Gatherer or JobType.Hunter or JobType.Trader)
        {
            FineBeam(rig.Torso,new(-.16f,1.21f,-.10f),new(.19f,.82f,-.132f),.022f,"#786346");
            FineBeam(rig.Torso,new(-.16f,1.21f,.12f),new(.19f,.82f,.138f),.022f,"#786346");
            Detail(rig.Torso,"sphere",new(.23f,.80f,-.008f),new(.15f,.19f,.095f),"#786346");
            Detail(rig.Torso,"box",new(.23f,.875f,-.055f),new(.125f,.048f,.016f),"#a18c68");
        }
        if(job==JobType.Farmer)
        {
            for(int i=0;i<16;i++)
            {
                float a=i*MathF.Tau/16;
                Detail(rig.Head,"capsule",new(MathF.Cos(a)*.16f,.124f,MathF.Sin(a)*.16f),new(.008f,.033f,.008f),"#816a43",new(0,0,MathF.PI/2));
            }
            Detail(rig.Head,"cylinder",new(0,.139f,0),new(.226f,.018f,.226f),"#75654f");
        }
    }
}
