using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ModelGallery
{
    private readonly List<ResidentRig> _upperArmStudies=new();
    private void BuildUpperArmFlex(NatureModels models,GridContainer grid,Label heading)
    {
        bool elbowView=Collection=="elbow-joint";
        heading.Text=elbowView?"肘部连接 · 内侧弯折、外侧轮廓与连续旋转":"上臂细节 · 固定肩位、连续屈肘与旋腕 · 游戏实际衣装";grid.Columns=2;
        for(int frame=0;frame<2;frame++)for(int view=0;view<2;view++)
        {
            var rig=models.Resident(frame==0?17:55,false,JobType.None);rig.AmbientMotionEnabled=false;
            rig.StudyUpperArmReference(1,.08f,0);
            var centre=rig.Torso.Transform*(elbowView?rig.Arms[1].Transform*rig.Elbows[1].Position:rig.Arms[1].Position-rig.Arms[1].Basis.Y*.14f);
            var target=new Basis(Vector3.Up,MathF.PI)*(centre*rig.Scale);
            Closeup(grid,(frame==0?"男性外观":"女性外观")+(elbowView?(view==0?" · 前侧肘内侧连接":" · 侧面弯折与袖口"):(view==0?" · 前侧衣料与袖根":" · 侧面肌腹与袖口")),rig,elbowView?.34f:.48f,target,view==0?new(.15f,.04f,4):new(-4,.04f,.1f),true);
            _upperArmStudies.Add(rig);
        }
    }
    private void AdvanceUpperArmFlex(float delta)
    {
        float phase=Mathf.PosMod(_elapsed,8)/8;
        float bend=.08f+(Collection=="elbow-joint"?2.07f:1.75f)*(.5f-.5f*MathF.Cos(MathF.Tau*phase)),roll=.7f*MathF.Sin(MathF.Tau*phase);
        foreach(var rig in _upperArmStudies)
        {
            rig.StudyUpperArmReference(1,bend,roll);
        }
    }
}
