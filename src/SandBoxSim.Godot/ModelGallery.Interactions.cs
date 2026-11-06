using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

internal partial class ModelGallery
{
    private readonly List<(ResidentRig Rig,int Sequence,Label Readout,string Title,Camera3D Camera)> _interactionFlow=new();
    private sealed class HandoffStudy(ResidentRig giver,ResidentRig receiver,Label readout)
    {
        internal readonly ResidentRig Giver=giver,Receiver=receiver;
        internal readonly Label Readout=readout;
        internal ResidentHandoff? Motion;
    }
    private readonly List<HandoffStudy> _handoffStudies=new();
    private float _lastHandoffTime=float.MaxValue;
    private void BuildInteractionFlow(NatureModels models,GridContainer grid,Label heading)
    {
        heading.Text="交互衔接 · 到站、取用与双人递接";grid.Columns=3;
        for(int frame=0;frame<2;frame++)for(int sequence=0;sequence<3;sequence++)
        {
            if(sequence==2)
            {
                var group=new Node3D();
                var giver=models.Resident(frame==0?17:55,false,JobType.None);
                var receiver=models.Resident(frame==0?55:17,false,JobType.None);
                giver.Position=new(-.72f,0,0);receiver.Position=new(.72f,0,0);
                giver.Rotation=new(0,-MathF.PI/2,0);receiver.Rotation=new(0,MathF.PI/2,0);
                group.AddChild(giver);group.AddChild(receiver);
                Closeup(grid,"双人分享",group,1.72f,new(0,1.00f,0),new(.25f,.23f,4),false);
                var card=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1);
                card.GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0).RenderTargetUpdateMode=SubViewport.UpdateMode.WhenVisible;
                _handoffStudies.Add(new(giver,receiver,card.GetChild<Label>(1)));
                continue;
            }
            var rig=models.Resident(frame==0?17:55,false,sequence==1?JobType.Builder:JobType.None);
            string title=(frame==0?"男性外观":"女性外观")+" · "+(sequence==0?"到站拾取与屈膝放下":"工具、饮水与进食");
            Closeup(grid,title,rig,sequence==1?1.16f:2.10f,new(0,sequence==1?1.15f:.82f,0),
                new(sequence==1?-2.3f:1.3f,.06f,4),true);
            var column=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1);
            var stage=column.GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0).GetChild<Node3D>(0);
            _interactionFlow.Add((rig,sequence,column.GetChild<Label>(1),title,stage.GetChild<Camera3D>(stage.GetChildCount()-1)));
        }
    }
    private void AdvanceInteractionFlow(float dt)
    {
        float t=_elapsed%16.5f;
        foreach(var (rig,sequence,readout,title,camera) in _interactionFlow)
        {
            ActionKind action;ActionPhase phase;
            if(sequence==0)
            {
                // Take a real return walk so the loop never snaps the root.
                bool outbound=t<1.85f,returning=t>=7.25f&&t<9.20f;
                bool pickup=t>=1.85f&&t<5.30f;
                float travel=.82f*(ResidentRig.ReachEase(t/1.85f)-ResidentRig.ReachEase((t-7.25f)/1.95f));
                var previous=rig.Position;rig.Position=new(0,0,travel-.82f);
                float remaining=t<7.25f?.82f-travel:travel;
                float yaw=rig.Rotation.Y;
                rig.PlanArrival(Vector3.Zero,pickup?MathF.PI+.62f:null);
                if(pickup&&rig.TryGetArrivalFacing(out float facing))yaw=facing;
                else if(t>=5.30f&&t<7.25f||t>=9.20f&&t<11.05f)
                {
                    if(rig.IsPivoting||rig.InteractionSettled)yaw=rig.AdvancePivot(yaw,t<7.25f?0:MathF.PI,dt);
                }
                else rig.FadePivot(dt);
                rig.Rotation=new(0,yaw,0);
                rig.ObserveLocomotion(rig.Position-previous,dt,0,false,remaining);
                bool placement=t>=11.05f&&t<15.4f;
                rig.ShowCargo(t>=11.05f&&t<12.60f,ResourceKind.Wood);
                action=outbound||returning?ActionKind.Wander:pickup?ActionKind.Take:placement?ActionKind.Deposit:ActionKind.None;
                phase=outbound||returning?ActionPhase.Moving:pickup||placement&&t<12.60f?ActionPhase.Executing:
                    placement?ActionPhase.Done:ActionPhase.Idle;
            }
            else
            {
                action=t<2.4f?ActionKind.BuildHouse:t<6.2f?ActionKind.Drink:t<9.25f?ActionKind.Eat:
                    t<12.3f?ActionKind.Farm:t<14.65f?ActionKind.Sleep:ActionKind.Flee;
                phase=t<14.65f?ActionPhase.Executing:ActionPhase.Moving;
            }
            rig.PresentAction(dt,action,phase,false,0);
            if(sequence==0)
            {
                float crouch=Mathf.SmoothStep(.10f,.44f,ResidentRig.HipHeight-rig.Legs[0].Position.Y);
                var target=new Vector3(0,Mathf.Lerp(.84f,.45f,crouch),rig.Position.Z);
                camera.Size=Mathf.Lerp(1.86f,.98f,crouch);
                camera.Position=target+new Vector3(Mathf.Lerp(1.3f,3.7f,crouch),.14f,4);
                camera.LookAt(target);
            }
            string status=title+"\n"+rig.Handling;
            if(readout.Text!=status)readout.Text=status;
        }
        AdvanceHandoffStudy(dt);
    }

    private void AdvanceHandoffStudy(float dt)
    {
        float t=_elapsed%5.5f;
        bool restart=t<_lastHandoffTime;
        _lastHandoffTime=t;
        foreach(var study in _handoffStudies)
        {
            if(restart){study.Motion?.Cancel();study.Motion=new(study.Giver,study.Receiver);}
            float approach=ResidentRig.ReachEase(t/1.06f);
            float depart=ResidentRig.ReachEase((t-4.30f)/1.10f);
            float spacing=.46f+.26f*(1-approach+depart);
            MoveHandoffStudy(study.Giver,-spacing,dt,t,approach);
            MoveHandoffStudy(study.Receiver,spacing,dt,t,approach);
            var motion=study.Motion!;
            motion.Prepare();
            study.Giver.PresentAction(dt,ActionKind.ShareFood,ActionPhase.Done,false,0,hasPartner:false);
            study.Receiver.PresentAction(dt,ActionKind.None,ActionPhase.Idle,false,0);
            motion.Advance(dt);
            string status="双人分享\n"+(motion.Finished?"交接结束":study.Giver.Handling+"  ·  "+study.Receiver.Handling);
            if(study.Readout.Text!=status)study.Readout.Text=status;
        }
    }

    private static void MoveHandoffStudy(ResidentRig rig,float x,float dt,float t,float approach)
    {
        var previous=rig.Position;
        rig.Position=new(x,0,0);
        rig.ObserveLocomotion(rig.Position-previous,dt,0,false,t<1.06f?.26f*(1-approach):0);
    }
}
