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
        heading.Text="交互衔接 · 到站、换工具与双手搬运";grid.Columns=3;
        for(int frame=0;frame<2;frame++)for(int sequence=0;sequence<3;sequence++)
        {
            var rig=models.Resident(frame==0?17:55,false,sequence==1?JobType.Builder:JobType.None);
            string title=(frame==0?"男性外观":"女性外观")+" · "+new[]{"靠近与拾取","作业与生活","搬运与交接"}[sequence];
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
                // A real local root trajectory with its residual arrival
                // distance, using the same observation path as the world.
                float travel=t<1.8f?.7f*t/1.8f:.82f-.12f*MathF.Exp(-8*(t-1.8f));
                var previous=rig.Position;rig.Position=new(0,0,travel-.82f);
                rig.ObserveLocomotion(rig.Position-previous,dt,0,false,.82f-travel);
                action=t<1.8f?ActionKind.Wander:t<5.4f?ActionKind.Take:ActionKind.None;
                phase=t<1.8f?ActionPhase.Moving:t<5.4f?ActionPhase.Executing:ActionPhase.Idle;
            }
            else if(sequence==1)
            {
                action=t<3?ActionKind.BuildHouse:t<5.2f?ActionKind.Eat:t<7?ActionKind.Drink:t<9.5f?ActionKind.Farm:ActionKind.Flee;
                phase=t<9.5f?ActionPhase.Executing:ActionPhase.Moving;
            }
            else
            {
                bool loaded=t<5.6f||t>=9.8f;
                rig.ShowCargo(loaded,t<9.8f?ResourceKind.Wood:ResourceKind.Stone);
                action=t<2.8f?ActionKind.Wander:t<5.6f?ActionKind.Deposit:t<6.4f?ActionKind.Deposit:t<9.8f?ActionKind.Eat:ActionKind.Wander;
                phase=t<2.8f||t>=9.8f?ActionPhase.Moving:t<5.6f?ActionPhase.Executing:t<6.4f?ActionPhase.Done:ActionPhase.Executing;
            }
            rig.PresentAction(dt,action,phase,false,0);
            string status=title+"\n"+rig.Handling;
            if(readout.Text!=status)readout.Text=status;
        }
    }
}
