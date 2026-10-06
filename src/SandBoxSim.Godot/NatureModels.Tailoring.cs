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
        Detail(rig.Torso,"box",TorsoSurface(.021f,.737f,false,.006f),new(.033f,.075f,.012f),"#514237");
        foreach(float side in new[]{-1f,1f})
            FineBeam(rig.Torso,new(side*.012f,1.21f,-.123f),new(side*.023f,1.12f,-.161f),.007f,"#c2b69c");
        if(job is JobType.Gatherer or JobType.Hunter or JobType.Trader)
        {
            Sculpt(rig.Torso,FittedRibbon("shoulder-front",.81f,1.20f),Vector3.Zero,ResidentMaterial("#786346"));
            Sculpt(rig.Torso,FittedRibbon("shoulder-back",.81f,1.20f,true),Vector3.Zero,ResidentMaterial("#786346"));
            Detail(rig.Torso,"sphere",new(.23f,.80f,-.008f),new(.15f,.19f,.095f),"#786346");
            Detail(rig.Torso,"box",new(.23f,.875f,-.055f),new(.125f,.048f,.016f),"#a18c68");
        }
    }
}
