using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateWorkSupport()
    {
        float maxDrift=0,maxKnee=0,hipRange=0;
        foreach(int identity in new[]{17,55})
        foreach(var action in new[]{ActionKind.BuildHouse,ActionKind.Farm,ActionKind.GatherWood,ActionKind.GatherStone})
        {
            var rig=Resident(identity,false,JobType.Builder);
            try
            {
                var anchors=new[]{InTorso(rig.Feet[0],rig),InTorso(rig.Feet[1],rig)};
                for(int frame=0;frame<300;frame++)
                {
                    rig.PoseAction(1f/60,action,ActionPhase.Executing,false,0);
                    for(int side=0;side<2;side++)
                    {
                        var foot=InTorso(rig.Feet[side],rig);
                        float drift=foot.Origin.DistanceTo(anchors[side].Origin);maxDrift=MathF.Max(maxDrift,drift);
                        if(drift>.0001f||foot.Basis.Y.Dot(Vector3.Up)<.99999f)
                            throw new InvalidOperationException($"Working stance slides or lifts the ankle: {identity}/{action}/{frame}, {drift}");
                        if(MathF.Abs(rig.Knees[side].Position.Length()-ResidentRig.ThighLength)>.000001f||MathF.Abs(rig.Feet[side].Position.Length()-MathF.Sqrt(ResidentRig.ShinLength*ResidentRig.ShinLength+.04f*.04f))>.000001f)
                            throw new InvalidOperationException("Working stance changes leg lengths");
                        maxKnee=MathF.Max(maxKnee,MathF.Abs(rig.Knees[side].Rotation.X));
                        hipRange=MathF.Max(hipRange,MathF.Abs(rig.Legs[side].Position.Y-ResidentRig.HipHeight));
                    }
                }
                var frozen=rig.Legs[0].Transform;var knee=rig.Knees[1].Transform;var torso=rig.Torso.Transform;
                rig.PoseAction(1,action,ActionPhase.Executing,true,0);
                if(rig.Legs[0].Transform!=frozen||rig.Knees[1].Transform!=knee||rig.Torso.Transform!=torso)
                    throw new InvalidOperationException("Paused working stance moves");
                for(int i=0;i<90;i++)rig.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                if(MathF.Abs(rig.Knees[0].Rotation.X)>.001f)throw new InvalidOperationException("Working stance does not relax to idle");
            }
            finally{rig.Free();}
        }
        if(maxKnee<.12f||hipRange<.001f)throw new InvalidOperationException("Working stance leaves the lower body rigid");
        for(int i=0;i<3000;i++)
        {
            float blink=ResidentRig.FaceBlink(i/120f,.37f);
            if(!float.IsFinite(blink)||blink<0||blink>1)throw new InvalidOperationException("Invalid blink envelope");
        }
        GD.Print($"WORK_SUPPORT_PASS: two adult frames/four tools, actual ankle drift {maxDrift:F6} m, fixed leg lengths/level feet, knee and hip response, pause/return and bounded blink envelope");
    }
}
