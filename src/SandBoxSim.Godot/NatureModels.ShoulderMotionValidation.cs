using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Check the displayed upper-arm frame as well as hand positions. A reach
    // can satisfy every endpoint test while rolling the sleeve through 180°.
    private void ValidateShoulderMotion()
    {
        float maxTwist=0,maxStep=0;int samples=0;string worst="",worstStep="";
        foreach(int identity in new[]{17,55})
        foreach(var job in new[]{JobType.None,JobType.Farmer,JobType.Builder})
        {
            var rig=Resident(identity,false,job);
            try
            {
                var previous=new[]{rig.Arms[0].Quaternion,rig.Arms[1].Quaternion};
                foreach(var action in new[]{ActionKind.None,ActionKind.GatherWood,ActionKind.Farm,ActionKind.BuildHouse,ActionKind.Socialize,ActionKind.Eat,ActionKind.Take,ActionKind.None})
                for(int frame=0;frame<180;frame++)
                {
                    rig.PoseAction(1f/60,action,action==ActionKind.None?ActionPhase.Idle:ActionPhase.Executing,false,.37f);
                    for(int side=0;side<2;side++)
                    {
                        var basis=rig.Arms[side].Basis;
                        var direction=-basis.Y;
                        var swing=new Basis(new Quaternion(Vector3.Down,direction));
                        float twist=MathF.Abs(swing.X.SignedAngleTo(basis.X,direction));
                        float step=2*MathF.Acos(Math.Clamp(MathF.Abs(previous[side].Dot(rig.Arms[side].Quaternion)),0,1));
                        if(twist>maxTwist){maxTwist=twist;worst=$"{identity}/{job}/{action}/{frame}/{side}";}
                        if(step>maxStep){maxStep=step;worstStep=$"{identity}/{job}/{action}/{frame}/{side}";}
                        previous[side]=rig.Arms[side].Quaternion;samples++;
                        if(!basis.IsFinite()||MathF.Abs(basis.Determinant()-1)>.0001f)
                            throw new InvalidOperationException("Shoulder frame is not a proper rotation");
                    }
                }
            }
            finally{rig.Free();}
        }
        GD.Print($"SHOULDER_MOTION_METRICS: samples={samples}, axial={maxTwist:F3} rad, frame step={maxStep:F3} rad, worst={worst}, largest step={worstStep}");
        if(maxTwist>1.15f||maxStep>.22f)
            throw new InvalidOperationException($"Shoulder twists or snaps despite continuous hand positions: axial={maxTwist}, step={maxStep}, pose={worstStep}");
        GD.Print("SHOULDER_MOTION_PASS: displayed humeral rotation and angular continuity across two adult frames, bare reaches, tools and returns");
    }
}
