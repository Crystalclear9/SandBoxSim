using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateLocomotion()
    {
        float maxStep=0,minAnkle=10,maxClearance=0,maxSeparation=0,minSole=10;int samples=0;
        var paired=new[]{Resident(17,false,JobType.None),Resident(55,false,JobType.None)};
        try
        {
            foreach(var rig in paired)
            {
                var previous=new[]{InTorso(rig.Feet[0],rig).Origin,InTorso(rig.Feet[1],rig).Origin};
                for(int frame=0;frame<1080;frame++)
                {
                    bool moving=frame>=60&&frame<600||frame>=780&&frame<900;
                    float speed=frame<300?1.05f:frame<480?.55f:1.4f;
                    rig.ObserveLocomotion(moving?new(0,0,-speed/60):Vector3.Zero,1f/60,frame>=480&&frame<600?.5f:0,false);
                    var action=frame>=900?ActionKind.Take:ActionKind.Wander;
                    rig.PoseAction(1f/60,action,moving?ActionPhase.Moving:frame>=900?ActionPhase.Executing:ActionPhase.Idle,false,.3f);
                    for(int side=0;side<2;side++)
                    {
                        var ankle=InTorso(rig.Feet[side],rig).Origin;
                        float jump=ankle.DistanceTo(previous[side]);maxStep=MathF.Max(maxStep,jump);previous[side]=ankle;
                        if(!ankle.IsFinite()||jump>.060f||MathF.Abs(rig.Knees[side].Position.Length()-ResidentRig.ThighLength)>1e-6f||
                            MathF.Abs(rig.Feet[side].Position.Length()-MathF.Sqrt(ResidentRig.ShinLength*ResidentRig.ShinLength+.0016f))>1e-6f)
                            throw new InvalidOperationException($"Locomotion limb discontinuity: {frame}/{side}, step={jump}");
                        if(frame>=180&&frame<600)
                        {
                            minAnkle=MathF.Min(minAnkle,ankle.Y);maxClearance=MathF.Max(maxClearance,ankle.Y-.065f);samples++;
                            var actual=InTorso(rig.Feet[side],rig);
                            foreach(var point in rig.BootSoleCurve)minSole=MathF.Min(minSole,(actual*point).Y);
                        }
                    }
                    if(frame>=480&&frame<600)maxSeparation=MathF.Max(maxSeparation,MathF.Abs(rig.Head.Rotation.Y));
                }
                var foot=rig.Legs[0].Transform;var cloth=rig.PelvicClothPose;float cycle=rig.GaitCycle;
                rig.ObserveLocomotion(Vector3.One,1,.5f,true);
                rig.PoseAction(1,ActionKind.Wander,ActionPhase.Moving,true,0);
                if(foot!=rig.Legs[0].Transform||cloth!=rig.PelvicClothPose||cycle!=rig.GaitCycle)
                    throw new InvalidOperationException("Paused locomotion or pelvic clothing advances");
            }
            if(MathF.Abs(paired[0].GaitCycle-paired[1].GaitCycle)>.0001f||paired[1].GaitPelvisYaw<=paired[0].GaitPelvisYaw||
                minSole<-.003f||minAnkle<.06f||maxClearance<.05f||maxSeparation<.08f)
                throw new InvalidOperationException($"Locomotion profile/contact failure: ankle={minAnkle}, sole={minSole}, clearance={maxClearance}, turn={maxSeparation}");
        }
        finally{foreach(var rig in paired)rig.Free();}
        ValidateLocomotionLoads();
        ValidateSupportCoordination();
        ValidateLocomotionSampling();
        GD.Print($"LOCOMOTION_PASS: paired frames, 3 speeds, start/stop/turn/pickup, {samples} ankle samples and actual sole curves, fixed limb lengths; max step={maxStep:F4} m, minimum ankle/sole={minAnkle:F4}/{minSole:F4} m, clearance={maxClearance:F4} m, turn anticipation={maxSeparation:F3} rad, pause");
    }

    private void ValidateLocomotionSampling()
    {
        var near=Resident(17,false,JobType.None);var far=Resident(17,false,JobType.None);
        var child=Resident(42,true,JobType.None);float maxPhase=0,maxSpeed=0;
        try
        {
            child.ObserveLocomotion(new(.30f,0,0),1f/60,0,false);
            child.PoseAction(1f/60,ActionKind.Wander,ActionPhase.Moving,false,0);
            float starting=child.GaitSpeed;
            if(starting<.01f)throw new InvalidOperationException("Ordinary tile interpolation suppresses the starting step");
            child.ObserveLocomotion(new(2.9f,0,0),1f/60,0,false);
            child.PoseAction(1f/60,ActionKind.Wander,ActionPhase.Moving,false,0);
            if(child.GaitSpeed>=starting)throw new InvalidOperationException("A tile resynchronisation accelerates the walking cycle");
            for(int frame=0;frame<240;frame++)
            {
                float speed=1.05f+.35f*MathF.Sin(frame/60f*.7f);
                foreach(var rig in new[]{near,far})rig.ObserveLocomotion(new(0,0,-speed/60),1f/60,0,false);
                near.PoseAction(1f/60,ActionKind.Wander,ActionPhase.Moving,false,0);
                child.PoseAction(1f/60,ActionKind.Wander,ActionPhase.Moving,false,0);
                for(int side=0;side<2;side++)
                {
                    var root=child.LowerGarmentFrame*new Vector3(side==0?-child.HipHalfWidth:child.HipHalfWidth,ResidentRig.HipHeight,0);
                    if(child.Legs[side].Position.DistanceTo(root)>.000001f)throw new InvalidOperationException("Child gait loses the moving pelvis frame");
                }
                if(frame%8!=7)continue;
                far.PoseAction(8f/60,ActionKind.Wander,ActionPhase.Moving,false,0);
                maxPhase=MathF.Max(maxPhase,MathF.Abs(Mathf.PosMod(near.GaitCycle-far.GaitCycle+.5f,1)-.5f));
                maxSpeed=MathF.Max(maxSpeed,MathF.Abs(near.GaitSpeed-far.GaitSpeed));
            }
            if(maxPhase>.03f||maxSpeed>.05f)throw new InvalidOperationException($"World displacement accumulation loses gait timing: phase={maxPhase}, speed={maxSpeed}");
        }
        finally{near.Free();far.Free();child.Free();}
        GD.Print($"LOCOMOTION_SAMPLING_PASS: per-frame variable world displacement, stride-8 consumption, maximum cycle/speed difference={maxPhase:F4}/{maxSpeed:F4}; child pelvic roots remain attached");
    }

    private void ValidateLocomotionLoads()
    {
        float maxPalm=0;int contacts=0;
        foreach(int identity in new[]{17,55})foreach(bool cargo in new[]{false,true})
        {
            var rig=Resident(identity,false,cargo?JobType.None:JobType.Builder);
            try
            {
                if(cargo)rig.ShowCargo(true,SandBoxSim.Core.Environment.ResourceKind.Wood);
                var previous=new[]{InTorso(rig.Hands[0],rig).Origin,InTorso(rig.Hands[1],rig).Origin};
                for(int frame=0;frame<480;frame++)
                {
                    var action=cargo?ActionKind.Wander:ActionKind.BuildHouse;
                    rig.PoseAction(1f/60,action,frame<300?ActionPhase.Moving:ActionPhase.Executing,false,0);
                    for(int side=0;side<2;side++)
                    {
                        var next=InTorso(rig.Hands[side],rig).Origin;
                        float step=next.DistanceTo(previous[side]);maxPalm=MathF.Max(maxPalm,step);previous[side]=next;
                        if(step>.060f)throw new InvalidOperationException($"Loaded gait hand discontinuity: {identity}/{cargo}/{frame}/{side}, {step}");
                        if(frame%30==29)
                        {
                            var root=rig.Legs[side].Position;
                            rig.ConformFoot(side,new(0,.025f,0),new(.10f,1,-.12f));rig.SyncTrousers();
                            if(rig.Legs[side].Position!=root)throw new InvalidOperationException("Terrain correction detaches the hip root");
                            if(!rig.TrouserSkeleton!.GetBonePose(side+1).IsEqualApprox(rig.LowerGarmentFrame.AffineInverse()*rig.Legs[side].Transform))
                                throw new InvalidOperationException("Terrain correction leaves a stale trouser binding");
                        }
                    }
                    if(rig.ToolHeld)
                    {
                        var palm=InTorso(rig.Hands[1],rig.Torso)*rig.Grip!.Position;
                        if(palm.DistanceTo(InTorso(rig.ToolMesh,rig.Torso).Origin)>.001f)
                            throw new InvalidOperationException("Walk-to-work transition loses palm/tool contact");
                        contacts++;
                    }
                }
            }
            finally{rig.Free();}
        }
        if(contacts<600)throw new InvalidOperationException("Loaded locomotion test never reaches held-tool walking");
        GD.Print($"LOCOMOTION_LOAD_PASS: two frames, cargo/hammer walking to work, {contacts} actual held contacts, maximum palm step={maxPalm:F4} m; slope IK preserves hip roots and refreshes trouser binds");
    }
}
