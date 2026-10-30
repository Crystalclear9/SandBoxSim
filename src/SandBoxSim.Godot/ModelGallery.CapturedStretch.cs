using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ModelGallery
{
    private readonly List<ResidentRig> _capturedStudies=new();
    private void BuildCapturedStudy(NatureModels models,GridContainer grid,Label heading)
    {
        grid.Columns=3;heading.Text="捕捉动作重定向 · CMU 143_30 伸展 · 游戏实际模型";
        for(int frame=0;frame<2;frame++)for(int view=0;view<3;view++)
        {
            var rig=models.Resident(frame==0?17:55,false,JobType.None);rig.AmbientMotionEnabled=false;
            Closeup(grid,(frame==0?"男性外观":"女性外观")+(view==0?" · 正面":view==1?" · 侧面":" · 背面"),rig,1.70f,new(0,1.10f,0),view==0?new(.03f,.03f,4):view==1?new(-4,.03f,.10f):new(.03f,.03f,-4),true);
            _capturedStudies.Add(rig);
        }
    }
    private void AdvanceCapturedStudy(float delta)
    {
        foreach(var rig in _capturedStudies)rig.StudyCapturedStretch(delta,_elapsed);
    }
}
