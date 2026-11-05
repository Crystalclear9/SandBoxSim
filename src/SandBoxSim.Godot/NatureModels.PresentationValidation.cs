using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateMotionContinuity()
    {
        var unchanged=Resident(17,false,JobType.Miner);var switched=Resident(17,false,JobType.Miner);
        try
        {
            for(int frame=0;frame<240;frame++)
            {
                unchanged.PoseAction(1f/60,ActionKind.GatherStone,ActionPhase.Executing,false,0);
                switched.PoseAction(1f/60,frame<180?ActionKind.GatherStone:ActionKind.GatherIron,ActionPhase.Executing,false,0);
                if(frame>=180&&InTorso(unchanged.ToolMesh,unchanged.Torso).Origin.DistanceTo(InTorso(switched.ToolMesh,switched.Torso).Origin)>.00001f)
                    throw new InvalidOperationException("Changing resource with the same held pick restarts the stroke");
            }
        }
        finally{unchanged.Free();switched.Free();}
        var p=new Vector3(.2f,-.1f,.3f);var v=new Vector3(.8f,.1f,-.2f);var target=new Vector3(-.1f,.4f,.2f);
        var split=p;var splitV=v;
        ResidentRig.DampMotion(ref p,ref v,target,.04f,.1f);
        for(int i=0;i<12;i++)ResidentRig.DampMotion(ref split,ref splitV,target,.04f,1f/120);
        if(p.DistanceTo(split)>.00001f||v.DistanceTo(splitV)>.0001f)throw new InvalidOperationException("Motion damping depends on subdivision");
        var saved=p;var savedV=v;
        ResidentRig.DampMotion(ref p,ref v,Vector3.One,.04f,0);
        if(p.DistanceTo(saved)>.000001f||v!=savedV)throw new InvalidOperationException("New target discards motion velocity before time advances");
        float maxPalmStep=0,maxElbowStep=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.Builder);
            try
            {
                Vector3 palm=Vector3.Zero,elbow=Vector3.Zero;bool held=false,observed=false;
                var previousTool=Transform3D.Identity;
                foreach(var action in new[]{ActionKind.BuildHouse,ActionKind.None,ActionKind.Farm,ActionKind.GatherStone,ActionKind.Eat,ActionKind.GatherWood})
                for(int frame=0;frame<200;frame++)
                {
                    rig.PoseAction(1f/60,action,action==ActionKind.None?ActionPhase.Idle:ActionPhase.Executing,false,0);
                    var next=InTorso(rig.Hands[1],rig.Torso)*rig.Grip!.Position;
                    var nextElbow=InTorso(rig.Elbows[1],rig.Torso).Origin;
                    var tool=InTorso(rig.ToolMesh,rig.Torso);
                    if(observed&&(tool.Origin.DistanceTo(previousTool.Origin)>.040f||2*MathF.Acos(Math.Clamp(MathF.Abs(tool.Basis.GetRotationQuaternion().Dot(previousTool.Basis.GetRotationQuaternion())),0,1))>.20f))
                        throw new InvalidOperationException($"Tool reparenting or transition jumps: {identity}/{action}/{frame}");
                    if(held&&rig.ToolHeld)
                    {
                        maxPalmStep=MathF.Max(maxPalmStep,palm.DistanceTo(next));
                        maxElbowStep=MathF.Max(maxElbowStep,elbow.DistanceTo(nextElbow));
                        if(palm.DistanceTo(next)>.040f||elbow.DistanceTo(nextElbow)>.050f)
                            throw new InvalidOperationException($"Tool action jumps: {identity}/{action}/{frame}: palm={palm.DistanceTo(next)}, elbow={elbow.DistanceTo(nextElbow)}");
                        if(next.DistanceTo(InTorso(rig.ToolMesh,rig.Torso).Origin)>.001f)throw new InvalidOperationException("Filtered tool loses palm contact");
                    }
                    held=rig.ToolHeld;palm=next;elbow=nextElbow;previousTool=tool;observed=true;
                }
            }
            finally{rig.Free();}
        }
        GD.Print($"MOTION_CONTINUITY_PASS: exact damping subdivision/target change, two adult frames and six action transitions, maximum palm/elbow step {maxPalmStep:F4}/{maxElbowStep:F4} m, held contact");
    }
    private void ValidateEffortPresentation()
    {
        ValidateMotionStyles();
        var profile=Resident(17,false,JobType.Builder);
        try
        {
            float peak=0;for(int i=0;i<2000;i++)if(profile.WorkLift(i/2000f)>profile.WorkLift(peak))peak=i/2000f;
            if(profile.WorkLift(peak)-profile.WorkLift(peak-.02f)<.001f||profile.WorkLift(peak)-profile.WorkLift(peak+.02f)<.001f)
                throw new InvalidOperationException("Work apex still holds a flat key pose");
            if(MathF.Abs(profile.WorkLift(-.001f)-profile.WorkLift(.999f))>.00001f||
                MathF.Abs(profile.WorkLift(.001f)-profile.WorkLift(1.001f))>.00001f)
                throw new InvalidOperationException("Work cycle is discontinuous at its wrap");
        }
        finally{profile.Free();}
        var rigs=new[]{Resident(17,false,JobType.Builder),Resident(55,false,JobType.Builder),Resident(55,false,JobType.Builder)};
        int[] peaks=new int[3];float[] lean=new float[3];
        try
        {
            rigs[0].WorkCapacity=rigs[1].WorkCapacity=1.1f;rigs[2].WorkCapacity=.8f;
            rigs[2].SetWorkCondition(.8f,1);
            for(int index=0;index<rigs.Length;index++)
            {
                var rig=rigs[index];bool rising=false,observed=false,completed=false;
                float trough=0,crest=0;int anchor=-1;
                for(int frame=0;frame<1500;frame++)
                {
                    rig.PoseAction(1f/60,ActionKind.BuildHouse,ActionPhase.Executing,false,0);
                    if(!rig.ToolHeld)continue;
                    var tool=InTorso(rig.ToolMesh,rig.Torso);var palm=InTorso(rig.Hands[1],rig.Torso)*rig.Grip!.Position;
                    if(palm.DistanceTo(tool.Origin)>.001f)throw new InvalidOperationException("Effort profile detaches the held tool");
                    // Count actual excursions, not crossings of an absolute
                    // world height. Different shoulder/holster fits can put
                    // the same stroke on opposite sides of that old plane.
                    float y=tool.Origin.Y;
                    if(!observed){trough=crest=y;observed=true;}
                    bool peak=false;
                    if(rising)
                    {
                        crest=MathF.Max(crest,y);
                        if(crest-y>=.06f){peak=true;rising=false;trough=y;}
                    }
                    else
                    {
                        trough=MathF.Min(trough,y);
                        if(y-trough>=.06f){rising=true;crest=y;}
                    }
                    // Align each measurement to its first settled excursion,
                    // so body-dependent draw time does not shift the window.
                    if(peak&&frame>150)
                    {
                        if(anchor<0)anchor=frame;
                        else peaks[index]++;
                    }
                    if(anchor>=0)
                    {
                        lean[index]=MathF.Max(lean[index],MathF.Abs(rig.Torso.Rotation.X));
                        if(frame-anchor>=650){completed=true;break;}
                    }
                }
                if(!completed)throw new InvalidOperationException("Effort observation never reaches a complete settled measurement window");
            }
            if(peaks[0]<5||peaks[0]!=peaks[1]||peaks[2]>=peaks[1]||lean[2]<lean[1]+.003f)
                throw new InvalidOperationException($"Effort does not reflect capacity/condition: peaks={string.Join(',',peaks)}, lean={string.Join(',',lean)}");
            var sample=rigs[0];var previous=InTorso(sample.ToolMesh,sample.Torso).Origin;
            sample.SetWorkCondition(.9f,.8f);sample.PoseAction(1f/60,ActionKind.BuildHouse,ActionPhase.Executing,false,0);
            if(previous.DistanceTo(InTorso(sample.ToolMesh,sample.Torso).Origin)>.05f)throw new InvalidOperationException("Changing fatigue resets the stroke phase");
            var frozen=sample.Torso.Transform;var toolPose=sample.ToolMesh.Transform;
            sample.PoseAction(1,ActionKind.BuildHouse,ActionPhase.Executing,true,0);
            if(sample.Torso.Transform!=frozen||sample.ToolMesh.Transform!=toolPose)throw new InvalidOperationException("Paused effort presentation moves");
            foreach(var action in new[]{ActionKind.GatherFood,ActionKind.None,ActionKind.BuildHouse,ActionKind.Eat})
            {
                var head=sample.Head.Rotation;float twist=sample.Torso.Rotation.Y;
                sample.PoseAction(1f/60,action,ActionPhase.Executing,false,0);
                if(sample.Head.Rotation.DistanceTo(head)>.035f||MathF.Abs(sample.Torso.Rotation.Y-twist)>.012f)
                    throw new InvalidOperationException("Action transition snaps the head or torso twist");
                for(int frame=0;frame<90;frame++)sample.PoseAction(1f/60,action,ActionPhase.Executing,false,0);
            }
            GD.Print($"EFFORT_PRESENTATION_PASS: observed strokes={string.Join(',',peaks)}, equal capacity across adult frames, fatigue response, held contact, phase continuity and pause");
        }
        finally{foreach(var rig in rigs)rig.Free();}
    }
    private void ValidateMotionStyles()
    {
        // Identical geometry, capacity and displacement isolate the style.
        // Observe solved feet and knees as well as the hand and tool paths.
        var a=Resident(17,false,JobType.Builder);var b=Resident(17,false,JobType.Builder);
        a.MotionFlow=0;b.MotionFlow=1;
        double walk=0,work=0;int walking=0,working=0;
        float[] cycles=new float[2],lowestFoot={float.PositiveInfinity,float.PositiveInfinity};
        float[] highestFoot={float.NegativeInfinity,float.NegativeInfinity},kneeBend=new float[2];
        float[] toolCrown=new float[2];
        float driveSide=0,flowSide=0;
        var styles=new[]{a,b};
        try
        {
            foreach(var rig in new[]{a,b})
            {
                const float h=.0001f;
                float left=(rig.WorkLift(0)-rig.WorkLift(-h))/h;
                float right=(rig.WorkLift(h)-rig.WorkLift(0))/h;
                if(MathF.Abs(left-right)>.005f||MathF.Abs(rig.WorkLift(.27f)-rig.WorkLift(1.27f))>.00001f)
                    throw new InvalidOperationException("Motion style breaks periodic stroke continuity");
                for(int i=0;i<=1000;i++)
                {
                    var offset=rig.StrokeFollowThrough(i/1000f);
                    if(!offset.IsFinite()||offset.Length()>.020f||offset.Y!=0)
                        throw new InvalidOperationException("Stroke recovery leaves its bounded spatial arc");
                }
                if(rig.StrokeFollowThrough(0)!=Vector3.Zero||rig.StrokeFollowThrough(1)!=Vector3.Zero
                    ||rig.StrokeFollowThrough(1-h).Length()/h>.001f)
                    throw new InvalidOperationException("Stroke recovery snaps at its periodic boundary");
            }
            for(int frame=0;frame<600;frame++)
            {
                bool moving=frame>=300;
                for(int index=0;index<styles.Length;index++)
                {
                    var rig=styles[index];
                    float phase=rig.GaitCycle;
                    rig.ObserveLocomotion(moving?new(0,0,-1.05f/60):Vector3.Zero,1f/60,0,false);
                    rig.PoseAction(1f/60,moving?ActionKind.Wander:ActionKind.BuildHouse,
                        moving?ActionPhase.Moving:ActionPhase.Executing,false,0);
                    if(frame>=150&&frame<300)
                    {
                        kneeBend[index]=MathF.Max(kneeBend[index],MathF.Abs(rig.Knees[0].Rotation.X));
                        toolCrown[index]=MathF.Max(toolCrown[index],InTorso(rig.ToolMesh,rig.Torso).Origin.Y);
                        float hip=(rig.LowerGarmentFrame*new Vector3(0,ResidentRig.HipHeight,0)).X;
                        if(index==0)driveSide=MathF.Max(driveSide,hip);
                        else flowSide=MathF.Min(flowSide,hip);
                    }
                    if(frame<420)continue;
                    cycles[index]+=Mathf.PosMod(rig.GaitCycle-phase,1);
                    float foot=InTorso(rig.Feet[0],rig).Origin.Z;
                    lowestFoot[index]=MathF.Min(lowestFoot[index],foot);
                    highestFoot[index]=MathF.Max(highestFoot[index],foot);
                }
                if(frame>=150&&frame<300){work+=Math.Pow(InTorso(a.ToolMesh,a.Torso).Origin.DistanceTo(InTorso(b.ToolMesh,b.Torso).Origin),2);working++;}
                if(frame>=420){walk+=Math.Pow(InTorso(a.Hands[0],a).Origin.DistanceTo(InTorso(b.Hands[0],b).Origin),2);walking++;}
            }
            double walkRms=Math.Sqrt(walk/walking),workRms=Math.Sqrt(work/working);
            if(!double.IsFinite(walkRms)||!double.IsFinite(workRms)||walkRms<.025||workRms<.015)
                throw new InvalidOperationException($"Motion profiles remain visually identical: walk={walkRms}, work={workRms}");
            float strideA=highestFoot[0]-lowestFoot[0],strideB=highestFoot[1]-lowestFoot[1];
            if(cycles[0]<=0||cycles[1]/cycles[0]<1.18f||MathF.Abs(a.GaitSpeed-b.GaitSpeed)>.00001f
                ||strideA-strideB<.055f||kneeBend[0]-kneeBend[1]<.06f)
                throw new InvalidOperationException($"Motion style does not reach the lower body: cycles={cycles[0]}/{cycles[1]}, feet={strideA}/{strideB}, knees={kneeBend[0]}/{kneeBend[1]}");
            if(toolCrown[0]-toolCrown[1]<.070f||driveSide<.026f||flowSide>-.008f)
                throw new InvalidOperationException($"Motion styles differ only in timing: tool peaks={toolCrown[0]}/{toolCrown[1]}, preparation shift={driveSide}/{flowSide}");
            double social=0;int socialSamples=0;
            int[] pairedHands=new int[2];
            for(int frame=0;frame<420;frame++)
            {
                foreach(var rig in new[]{a,b})rig.PoseAction(1f/60,ActionKind.Socialize,ActionPhase.Executing,false,0);
                if(frame<180)continue;
                for(int index=0;index<styles.Length;index++)
                {
                    var rig=styles[index];
                    if(InTorso(rig.Hands[0],rig.Torso).Origin.Y>.90f&&InTorso(rig.Hands[1],rig.Torso).Origin.Y>.90f)pairedHands[index]++;
                }
                for(int side=0;side<2;side++)
                {
                    var pa=InTorso(a.Hands[side],a.Torso).Origin;var pb=InTorso(b.Hands[side],b.Torso).Origin;
                    if(!pa.IsFinite()||!pb.IsFinite())throw new InvalidOperationException("Invalid styled social hand");
                    social+=Math.Pow(pa.DistanceTo(pb),2);socialSamples++;
                }
            }
            double socialRms=Math.Sqrt(social/socialSamples);
            if(socialRms<.012||a.StrokeFollowThrough(.8f).DistanceTo(b.StrokeFollowThrough(.8f))<.003f)
                throw new InvalidOperationException("Style changes timing without distinct gesture/recovery paths");
            if(pairedHands[1]-pairedHands[0]<24)
                throw new InvalidOperationException($"Conversation lacks distinct hand phrasing: {pairedHands[0]}/{pairedHands[1]}");
            GD.Print($"MOTION_CHOREOGRAPHY_PASS: same body and tool, solved hammer grip peaks={toolCrown[0]:F3}/{toolCrown[1]:F3} m, opposite preparation hip shift={driveSide:F3}/{flowSide:F3} m, raised bilateral gesture frames={pairedHands[0]}/{pairedHands[1]}");
            GD.Print($"SPATIAL_MOTION_STYLE_PASS: identical body, bilateral social wrist RMS={socialRms:F4} m; distinct bounded recovery arcs, fixed endpoints and continuous wrap");
            GD.Print($"MOTION_STYLE_PASS: identical body/capacity/speed, solved wrist/tool RMS separation={walkRms:F4}/{workRms:F4} m; periodic stroke continuity; art direction, not sex classification");
            GD.Print($"MOTION_STYLE_LOWER_BODY_PASS: same 1.05 m/s, cadence ratio={cycles[1]/cycles[0]:F3}, solved foot travel={strideA:F3}/{strideB:F3} m, work knee peaks={kneeBend[0]:F3}/{kneeBend[1]:F3} rad");
        }
        finally{a.Free();b.Free();}
    }
    private void ValidateReachTrajectories()
    {
        float maxHandStep=0,maxElbowStep=0;int fingertipSamples=0;
        foreach(var action in new[]{ActionKind.Eat,ActionKind.Drink,ActionKind.Socialize})
        {
            var rig=Resident(42,false,JobType.Gatherer);
            var reference=Resident(42,false,JobType.Gatherer);
            try
            {
                Vector3 previousHand=Vector3.Zero,previousElbow=Vector3.Zero;
                float low=float.PositiveInfinity,high=float.NegativeInfinity,closestMouth=float.PositiveInfinity,closestTip=float.PositiveInfinity;int nearTip=0;
                for(int frame=0;frame<360;frame++)
                {
                    rig.PoseAction(1f/60,action,ActionPhase.Executing,false,.37f);
                    reference.PoseAction(1f/120,action,ActionPhase.Executing,false,.37f);
                    reference.PoseAction(1f/120,action,ActionPhase.Executing,false,.37f);
                    var hand=InTorso(rig.Hands[1],rig.Torso);var elbow=InTorso(rig.Elbows[1],rig.Torso).Origin;
                    if(frame>0)
                    {
                        maxHandStep=MathF.Max(maxHandStep,hand.Origin.DistanceTo(previousHand));
                        maxElbowStep=MathF.Max(maxElbowStep,elbow.DistanceTo(previousElbow));
                        if(hand.Origin.DistanceTo(previousHand)>.025f||elbow.DistanceTo(previousElbow)>.030f)
                            throw new InvalidOperationException($"Discontinuous reach: {action}/{frame}, hand={hand.Origin.DistanceTo(previousHand)}, elbow={elbow.DistanceTo(previousElbow)}");
                    }
                    if(hand.Origin.DistanceTo(InTorso(reference.Hands[1],reference.Torso).Origin)>.008f)
                        throw new InvalidOperationException("Reach path changes with 60/120 Hz cadence: "+action);
                    previousHand=hand.Origin;previousElbow=elbow;
                    if(frame>100&&action!=ActionKind.Socialize)
                    {
                        var palm=hand*rig.Grip!.Position;
                        low=MathF.Min(low,palm.Y);high=MathF.Max(high,palm.Y);
                        closestMouth=MathF.Min(closestMouth,palm.DistanceTo(rig.Head.Transform*rig.Mouth.Position));
                        var tip=InTorso(rig.Fingertips[1,0],rig.Torso)*new Vector3(0,-.014f*.92f,0);
                        float contact=tip.DistanceTo(rig.Head.Transform*rig.Mouth.Position);
                        closestTip=MathF.Min(closestTip,contact);if(contact<.05f){nearTip++;fingertipSamples++;}
                    }
                }
                if(action!=ActionKind.Socialize&&(closestTip>.05f||nearTip<12))
                    throw new InvalidOperationException($"Consuming finger pad misses the mouth: {action}, distance={closestTip}, near frames={nearTip}");
                if(action!=ActionKind.Socialize&&(high-low<.12f||closestMouth>.13f))
                    throw new InvalidOperationException($"Consuming hand does not lift, reach the mouth and return: {action}, excursion={high-low}, mouth={closestMouth}");
            }
            finally{rig.Free();reference.Free();}
        }
        GD.Print($"REACH_TRAJECTORY_PASS: eat/drink/social, 60/120 Hz, maximum per-frame hand/elbow step {maxHandStep:F4}/{maxElbowStep:F4} m, mouth approach/return and {fingertipSamples} displayed index-pad proximity samples");
    }
    private void ValidatePresentationCadence()
    {
        // Compare visible motion after the same elapsed time at close-up 60 Hz,
        // 60 FPS / stride 8 and 30 FPS / stride 8, not just a clock counter.
        foreach(var action in new[]{ActionKind.None,ActionKind.GatherWood,ActionKind.Socialize})
        {
            var phase=action==ActionKind.None?ActionPhase.Moving:ActionPhase.Executing;
            var rigs=new[]{Resident(42,false,JobType.Gatherer),Resident(42,false,JobType.Gatherer),Resident(42,false,JobType.Gatherer)};
            try
            {
                foreach(var rig in rigs)for(int frame=0;frame<120;frame++)rig.PresentAction(1f/60,action,phase,false,.37f);
                rigs[1].UpdateDistanceDetail(22*22);rigs[2].UpdateDistanceDetail(22*22);
                for(int sample=0;sample<5;sample++)
                {
                    int[] counts={48,6,3};
                    for(int cadence=0;cadence<rigs.Length;cadence++)for(int frame=0;frame<counts[cadence];frame++)
                        rigs[cadence].PresentAction(.8f/counts[cadence],action,phase,false,.37f);
                    for(int cadence=1;cadence<rigs.Length;cadence++)
                    {
                        var near=rigs[0];var far=rigs[cadence];
                        if(near.ToolHeld!=far.ToolHeld||near.Head.Rotation.DistanceTo(far.Head.Rotation)>.02f
                            ||near.Legs[0].Rotation.DistanceTo(far.Legs[0].Rotation)>.005f
                            ||near.Knees[1].Rotation.DistanceTo(far.Knees[1].Rotation)>.005f)
                            throw new InvalidOperationException("Resident motion loses time at a distant update cadence: "+action);
                        for(int side=0;side<2;side++)
                            if(InTorso(near.Hands[side],near.Torso).Origin.DistanceTo(InTorso(far.Hands[side],far.Torso).Origin)>.025f)
                                throw new InvalidOperationException("Distant tool/social hand falls behind the close-up pose: "+action);
                    }
                }
                foreach(var rig in rigs)
                {
                    rig.PresentAction(.8f,action,ActionPhase.Done,false,.37f);
                    if(rig.ToolHeld)throw new InvalidOperationException("Elapsed stow time did not release the tool");
                    var hand=rig.Hands[1].Transform;var head=rig.Head.Transform;
                    rig.PresentAction(.8f,action,phase,true,.37f);
                    if(hand!=rig.Hands[1].Transform||head!=rig.Head.Transform)throw new InvalidOperationException("Paused accumulated pose continues moving");
                }
            }
            finally{foreach(var rig in rigs)rig.Free();}
        }
        var detail=Resident(42,false,JobType.Gatherer);
        try
        {
            void Check(bool near)
            {
                if(detail.HeadSkin.Visible!=near||detail.HandSkins[0].Visible!=near||detail.HandProxies[1].Visible==near
                    ||detail.BodySkin.Mesh!=(near?detail.DetailedBodyMesh:detail.DistantBodyMesh)
                    ||(detail.ShoulderSkeleton.ProcessMode==Node.ProcessModeEnum.Disabled)==near)
                    throw new InvalidOperationException("Resident distance bands disagree about meshes or skeleton state");
            }
            detail.UpdateDistanceDetail(22*22);Check(false);
            foreach(float distance in new[]{18.1f,17.9f,18.2f,17.8f}){detail.UpdateDistanceDetail(distance*distance);Check(false);}
            detail.UpdateDistanceDetail(15.9f*15.9f);Check(true);
            foreach(float distance in new[]{17.9f,18.1f,19.9f,18.2f}){detail.UpdateDistanceDetail(distance*distance);Check(true);}
            detail.UpdateDistanceDetail(20.1f*20.1f);Check(false);
        }
        finally{detail.Free();}
        GD.Print("RESIDENT_CADENCE_PASS: near/far elapsed-time gait, tool and social poses, stow, pause and stable detail bands");
    }
}
