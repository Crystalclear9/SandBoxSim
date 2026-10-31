using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ModelGallery
{
    private readonly List<ResidentRig> _elevationStudies=new();
    private Label? _elevationReadout;
    private void BuildElevationStudy(NatureModels models,GridContainer grid,Label heading)
    {
        grid.Columns=3;_elevationReadout=heading;
        for(int frame=0;frame<2;frame++)for(int view=0;view<3;view++)
        {
            var rig=models.Resident(frame==0?17:55,false,JobType.None);
            Closeup(grid,(frame==0?"男性外观":"女性外观")+(view==0?" · 正面":view==1?" · 侧面":" · 背面"),rig,1.38f,new(0,1.24f,0),view==0?new(.03f,.03f,4):view==1?new(-4,.03f,.10f):new(.03f,.03f,-4),true);
            _elevationStudies.Add(rig);
        }
    }
    private void AdvanceElevationStudy(float delta)
    {
        int side=(_elapsed%12)<6?1:0;float cycle=_elapsed%6;
        // Alternate arms, including a high hold and an independent return.
        float weight=cycle<1.5f?ResidentRig.ReachEase(cycle/1.5f):cycle<2.5f?1:cycle<4?1-ResidentRig.ReachEase((cycle-2.5f)/1.5f):0;
        float angle=Mathf.DegToRad(135)*weight;
        foreach(var rig in _elevationStudies)rig.StudyArmElevation(delta,angle,Mathf.DegToRad(30),side);
        if(_elevationReadout!=null)_elevationReadout.Text=$"肩袖变形观察 · {(side==1?"右臂":"左臂")} {Mathf.RadToDeg(angle):0}° · 抬起、保持与放下";
    }
}
