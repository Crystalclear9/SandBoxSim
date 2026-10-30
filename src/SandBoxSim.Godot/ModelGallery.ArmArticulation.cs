using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ModelGallery
{
    private readonly List<ResidentRig> _armStudies=new();
    private void BuildArmArticulation(NatureModels models,GridContainer grid,Label heading)
    {
        heading.Text="上肢近景 · 抬臂、屈肘与旋前／旋后 · 游戏实际模型";grid.Columns=3;
        for(int frame=0;frame<2;frame++)for(int view=0;view<3;view++)
        {
            var rig=models.Resident(frame==0?17:55,false,JobType.None);rig.AmbientMotionEnabled=false;
            Closeup(grid,(frame==0?"男性外观":"女性外观")+(view==0?" · 正面":view==1?" · 侧面":" · 背面"),rig,1.04f,new(0,1.17f,0),view==0?new(.03f,.03f,4):view==1?new(-4,.03f,.10f):new(.03f,.03f,-4),true);
            _armStudies.Add(rig);
        }
    }
    private void AdvanceArmArticulation(float delta)
    {
        float cycle=Mathf.PosMod(_elapsed,8);
        float lift=cycle<2?ResidentRig.ReachEase(cycle/2):cycle<6?1:1-ResidentRig.ReachEase((cycle-6)/2);
        float roll=cycle<2||cycle>6?0:-1.3f*MathF.Sin(Mathf.Tau*ResidentRig.ReachEase((cycle-2)/4));
        foreach(var rig in _armStudies)rig.StudyArmElevation(delta,Mathf.DegToRad(20+50*lift),Mathf.DegToRad(30),1,roll,.30f+.90f*lift);
    }
}
