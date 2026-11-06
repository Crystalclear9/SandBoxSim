using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

internal partial class ModelGallery
{
    private readonly List<(ResidentRig Rig,int Sequence,Label Readout,string Title)> _interactionFlow=new();
    private void BuildInteractionFlow(NatureModels models,GridContainer grid,Label heading)
    {
        heading.Text="交互衔接 · 到站、作业、休息与存放物资";grid.Columns=3;
        for(int frame=0;frame<2;frame++)for(int sequence=0;sequence<3;sequence++)
        {
            var rig=models.Resident(frame==0?17:55,false,sequence==1?JobType.Builder:JobType.None);
            string title=(frame==0?"男性外观":"女性外观")+" · "+new[]{"走近、转向与拾取","作业、休息与起身","托举、送稳与松手"}[sequence];
            Closeup(grid,title,rig,2.10f,new(0,.82f,0),new(1.3f,.06f,4),true);
            var readout=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1).GetChild<Label>(1);
            _interactionFlow.Add((rig,sequence,readout,title));
        }
    }
    private void AdvanceInteractionFlow(float dt)
    {
        float t=_elapsed%12;
        foreach(var (rig,sequence,readout,title) in _interactionFlow)
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
                else if(t>=5.30f&&t<7.25f||t>=9.20f)
                {
                    if(rig.IsPivoting||rig.InteractionSettled)yaw=rig.AdvancePivot(yaw,t<7.25f?0:MathF.PI,dt);
                }
                else rig.FadePivot(dt);
                rig.Rotation=new(0,yaw,0);
                rig.ObserveLocomotion(rig.Position-previous,dt,0,false,remaining);
                action=outbound||returning?ActionKind.Wander:pickup?ActionKind.Take:ActionKind.None;
                phase=outbound||returning?ActionPhase.Moving:pickup?ActionPhase.Executing:ActionPhase.Idle;
            }
            else if(sequence==1)
            {
                action=t<2.7f?ActionKind.BuildHouse:t<4.65f?ActionKind.Eat:t<6.25f?ActionKind.Drink:
                    t<8.5f?ActionKind.Farm:t<10.55f?ActionKind.Sleep:ActionKind.Flee;
                phase=t<10.55f?ActionPhase.Executing:ActionPhase.Moving;
            }
            else
            {
                bool loaded=t<5.6f||t>=9.8f;
                rig.ShowCargo(loaded,t<9.8f?ResourceKind.Wood:ResourceKind.Stone);
                action=t<2.8f?ActionKind.Wander:t<6.75f?ActionKind.Deposit:t<9.8f?ActionKind.Eat:ActionKind.Wander;
                phase=t<2.8f||t>=9.8f?ActionPhase.Moving:t<5.6f?ActionPhase.Executing:t<6.75f?ActionPhase.Done:ActionPhase.Executing;
            }
            rig.PresentAction(dt,action,phase,false,0);
            string status=title+"\n"+rig.Handling;
            if(readout.Text!=status)readout.Text=status;
        }
    }
}
