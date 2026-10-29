using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateSupportCoordination()
    {
        float maxHipStep=0,maxPickupDrift=0,maxCarryElbow=0;int supports=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                float previous=ResidentRig.HipHeight,minKnee=float.PositiveInfinity;
                for(int f=0;f<600;f++)
                {
                    rig.ObserveLocomotion(new(0,0,-1.05f/60),1f/60,0,false);
                    rig.PoseAction(1f/60,ActionKind.Wander,ActionPhase.Moving,false,0);
                    float hip=(rig.LowerGarmentFrame*new Vector3(0,ResidentRig.HipHeight,0)).Y;
                    maxHipStep=MathF.Max(maxHipStep,MathF.Abs(hip-previous));previous=hip;
                    if(f<120)continue;
                    for(int side=0;side<2;side++)
                    {
                        float phase=Mathf.PosMod(rig.GaitCycle+side*.5f,1);
                        if(phase>.15f&&phase<.40f){minKnee=MathF.Min(minKnee,MathF.Abs(rig.Knees[side].Rotation.X));supports++;}
                    }
                }
                if(minKnee>.40f||maxHipStep>.015f)throw new InvalidOperationException($"Persistent crouch or pelvis discontinuity: knee={minKnee}, hip step={maxHipStep}");
                for(int f=0;f<150;f++)
                {
                    rig.PoseAction(1f/60,ActionKind.Take,ActionPhase.Executing,false,0);
                    if(f<90)continue;
                    for(int side=0;side<2;side++)
                    {
                        var anchor=new Vector3(side==0?-rig.HipHalfWidth:rig.HipHalfWidth,ResidentRig.HipHeight-ResidentRig.ThighLength-ResidentRig.ShinLength,-.04f);
                        maxPickupDrift=MathF.Max(maxPickupDrift,InTorso(rig.Feet[side],rig).Origin.DistanceTo(anchor));
                    }
                }
            }
            finally{rig.Free();}
            rig=Resident(identity,false,JobType.Builder);
            try
            {
                for(int f=0;f<180;f++)rig.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                var palm=InTorso(rig.Hands[1],rig.Torso)*rig.Grip!.Position;
                var shoulder=InTorso(rig.Arms[1],rig).Origin;var elbow=InTorso(rig.Elbows[1],rig).Origin;var wrist=InTorso(rig.Hands[1],rig).Origin;
                float flexion=(elbow-shoulder).AngleTo(wrist-elbow);maxCarryElbow=MathF.Max(maxCarryElbow,flexion);
                if(!rig.ToolHeld||palm.Y>.78f||flexion>1.6f)throw new InvalidOperationException($"Carry returns to a high folded arm: palm={palm}, elbow={flexion}");
            }
            finally{rig.Free();}
        }
        if(maxPickupDrift>.001f)throw new InvalidOperationException("Bending drags the planted feet: "+maxPickupDrift);
        GD.Print($"SUPPORT_COORDINATION_PASS: two adult frames, {supports} support samples, mid-stance extension, pelvis step={maxHipStep:F4} m, pickup ankle drift={maxPickupDrift:F6} m, carry elbow={maxCarryElbow:F3} rad");
    }
}
