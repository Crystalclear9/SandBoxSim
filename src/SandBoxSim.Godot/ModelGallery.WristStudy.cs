using System.Collections.Generic;
using System.Linq;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ModelGallery
{
    private readonly List<(ResidentRig Rig,Camera3D Camera,int View,DirectionalLight3D Key,DirectionalLight3D Fill)> _wristStudies=new();
    private void BuildWristStudy(NatureModels models,GridContainer grid,Label heading)
    {
        grid.Columns=3;heading.Text="手掌与腕部 · 屈伸、侧偏、旋腕与握拳";
        for(int frame=0;frame<2;frame++)for(int view=0;view<3;view++)
        {
            var rig=models.Resident(frame==0?17:55,false,JobType.None);rig.AmbientMotionEnabled=false;
            Closeup(grid,(frame==0?"男性外观":"女性外观")+(view==0?" · 手背":view==1?" · 掌侧":" · 侧面"),rig,.30f,new(0,1,0),new(0,0,2),true);
            var viewport=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1).GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0);
            var stage=viewport.GetChild<Node3D>(0);var camera=stage.GetChild<Camera3D>(stage.GetChildCount()-1);
            // Isolate the actual connected skin through visibility layers;
            // the torso cannot obscure the wrist from the reverse camera.
            void Mask(Node parent)
            {
                if(parent is MeshInstance3D mesh)mesh.Layers=mesh==rig.HandSkins[1]?1u:2u;
                foreach(Node child in parent.GetChildren())Mask(child);
            }
            Mask(rig);camera.CullMask=1;
            foreach(var mesh in stage.GetChildren().OfType<MeshInstance3D>())mesh.Layers=2;
            var lights=stage.GetChildren().OfType<DirectionalLight3D>().ToArray();
            lights[0].LightEnergy=.72f;lights[1].LightEnergy=.30f;
            lights[0].LightCullMask=1;lights[1].LightCullMask=1;
            _wristStudies.Add((rig,camera,view,lights[0],lights[1]));
        }
        AdvanceWristStudy(0);
    }
    private void AdvanceWristStudy(float delta)
    {
        foreach(var (rig,camera,view,key,fill) in _wristStudies)
        {
            rig.StudyWrist(delta,_elapsed);
            var axis=rig.Elbows[1].GlobalBasis.Orthonormalized();
            var target=rig.Hands[1].GlobalPosition+axis*new Vector3(0,.023f,0);
            camera.GlobalPosition=target+axis*(view==0?new Vector3(.06f,.02f,2):view==1?new Vector3(.06f,.02f,-2):new Vector3(2,.02f,.06f));
            camera.LookAt(target,axis*Vector3.Up);
            key.GlobalBasis=camera.GlobalBasis*Basis.FromEuler(new(-.35f,-.45f,0));
            fill.GlobalBasis=camera.GlobalBasis*Basis.FromEuler(new(.15f,.65f,0));
        }
    }
}
