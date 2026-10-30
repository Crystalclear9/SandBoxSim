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
            Closeup(grid,(frame==0?"男性外观":"女性外观")+(view==0?" · 正面":view==1?" · 侧面":" · 背面"),rig,1.65f,new(0,1.12f,0),view==0?new(.03f,.03f,4):view==1?new(-4,.03f,.10f):new(.03f,.03f,-4),true);
            _elevationStudies.Add(rig);
        }
    }
    private void AdvanceElevationStudy(float delta)
    {
        float cycle=_elapsed%12;
        // Three seconds raise, a hold, three seconds lower, then rest.
        float weight=cycle<3?ResidentRig.ReachEase(cycle/3):cycle<5?1:cycle<8?1-ResidentRig.ReachEase((cycle-5)/3):0;
        float angle=Mathf.DegToRad(135)*weight;
        foreach(var rig in _elevationStudies)rig.StudyArmElevation(delta,angle,Mathf.DegToRad(30));
        if(_elevationReadout!=null)_elevationReadout.Text=$"肩袖变形观察 · 抬臂 {Mathf.RadToDeg(angle):0}° · 抬起、保持与放下";
    }
}
