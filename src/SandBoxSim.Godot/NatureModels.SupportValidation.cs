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
                var anchors=new[]{InTorso(rig.Feet[0],rig).Origin,InTorso(rig.Feet[1],rig).Origin};
                var previous=(Vector3[])anchors.Clone();
                var lifts=new float[2];
                float leftLoad=0,rightLoad=0;
                bool staged=action is ActionKind.BuildHouse or ActionKind.Farm,planted=!staged;
                for(int frame=0;frame<300;frame++)
                {
                    rig.PoseAction(1f/60,action,ActionPhase.Executing,false,0);
                    bool plantNow=!planted&&rig.WorkStanceSettled;
                    if(!planted&&!plantNow)
                    {
                        float x=(rig.LowerGarmentFrame*(Vector3.Up*ResidentRig.HipHeight)).X;
                        rightLoad=MathF.Max(rightLoad,x);leftLoad=MathF.Max(leftLoad,-x);
                    }
                    float supportingFootDrift=float.PositiveInfinity;
                    for(int side=0;side<2;side++)
                    {
                        var foot=InTorso(rig.Feet[side],rig);
                        if(plantNow)anchors[side]=foot.Origin;
                        if(planted||plantNow)
                        {
                            float drift=foot.Origin.DistanceTo(anchors[side]);maxDrift=MathF.Max(maxDrift,drift);
                            if(drift>.0001f||foot.Basis.Y.Dot(Vector3.Up)<.99999f)
                                throw new InvalidOperationException($"Working stance slides or lifts the ankle: {identity}/{action}/{frame}, {drift}");
                        }
                        else
                        {
                            var step=foot.Origin-previous[side];
                            supportingFootDrift=MathF.Min(supportingFootDrift,new Vector2(step.X,step.Z).Length());
                            lifts[side]=MathF.Max(lifts[side],foot.Origin.Y-anchors[side].Y);
                        }
                        previous[side]=foot.Origin;
                        if(MathF.Abs(rig.Knees[side].Position.Length()-ResidentRig.ThighLength)>.000001f||
                            (rig.Feet[side].Position-rig.Feet[side].Basis*ResidentRig.FootCenterOffset-Vector3.Down*ResidentRig.ShinLength).Length()>.000001f)
                            throw new InvalidOperationException("Working stance changes leg lengths");
                        maxKnee=MathF.Max(maxKnee,MathF.Abs(rig.Knees[side].Rotation.X));
                        hipRange=MathF.Max(hipRange,MathF.Abs(rig.Legs[side].Position.Y-ResidentRig.HipHeight));
                    }
                    if(!planted&&!plantNow&&supportingFootDrift>.002f)
                        throw new InvalidOperationException("Both feet slide during work preparation");
                    planted|=plantNow;
                }
                if(staged&&(!planted||lifts[0]<.018f||lifts[1]<.018f||anchors[1].Z-anchors[0].Z<.16f||leftLoad<.019f||rightLoad<.019f))
                    throw new InvalidOperationException($"Work preparation lacks two supported weight shifts: {identity}/{action}, {leftLoad}/{rightLoad}");
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
        GD.Print($"WORK_SUPPORT_PASS: two adult frames/four tools, alternating preparation steps and pelvis transfer, planted ankle drift {maxDrift:F6} m, fixed leg lengths/level feet, knee and hip response, pause/return and bounded blink envelope");
        ValidateRestTransfer();
    }

    private void ValidateRestTransfer()
    {
        float worstStep=0,worstFootDrift=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                const float delta=1f/60;
                for(int i=0;i<90;i++)rig.PoseAction(delta,ActionKind.None,ActionPhase.Idle,false,0);
                var anchor=new[]{InTorso(rig.Feet[0],rig).Origin,InTorso(rig.Feet[1],rig).Origin};
                float previous=rig.Legs[0].Position.Y,standing=previous,lowest=previous;
                for(int frame=0;frame<220;frame++)
                {
                    bool resting=frame<100;
                    rig.PoseAction(delta,resting?ActionKind.Sleep:ActionKind.None,resting?ActionPhase.Executing:ActionPhase.Idle,false,0);
                    float y=rig.Legs[0].Position.Y;
                    worstStep=MathF.Max(worstStep,MathF.Abs(y-previous));lowest=MathF.Min(lowest,y);previous=y;
                    for(int side=0;side<2;side++)
                    {
                        var foot=InTorso(rig.Feet[side],rig).Origin-anchor[side];
                        worstFootDrift=MathF.Max(worstFootDrift,MathF.Sqrt(foot.X*foot.X+foot.Z*foot.Z));
                    }
                }
                if(standing-lowest<.080f||MathF.Abs(previous-standing)>.002f)
                    throw new InvalidOperationException("Resting pelvis fails to lower or recover");
            }
            finally{rig.Free();}
        }
        if(worstStep>.009f||worstFootDrift>.003f)throw new InvalidOperationException($"Rest transfer jumps or slides: hip={worstStep}, feet={worstFootDrift}");
        GD.Print($"REST_TRANSFER_PASS: two adult frames, pelvis lowers and recovers, maximum hip step={worstStep:F4} m, planar foot drift={worstFootDrift:F5} m");
    }
}
