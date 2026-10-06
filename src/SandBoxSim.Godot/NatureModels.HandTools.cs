using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal enum ResidentTool { None, Hammer, Axe, Pick, Hoe, Spear }
internal sealed partial class NatureModels
{
    private void AddHandTools(ResidentRig rig,JobType job)
    {
        rig.Grip=new Node3D {Name="HandGrip",Position=new(0,-.035f,-.0286f),Rotation=new(0,0,MathF.PI/2)};rig.Hands[1].AddChild(rig.Grip);
        rig.Holster=new Node3D {Name="BeltToolLoop",Position=new(.235f,.79f,-.14f),Rotation=new(0,0,.65f)};rig.Torso.AddChild(rig.Holster);
        if(!rig.IsChild)
        {
            Detail(rig.Torso,"masonry",rig.Holster.Position+new Vector3(0,0,.014f),new(.032f,.052f,.020f),"#514237");
            FineBeam(rig.Torso,TorsoSurface(.145f,.79f,false),rig.Holster.Position,.015f,"#514237");
        }
        rig.ToolMesh=new MeshInstance3D {Name="Tool",Visible=false};rig.Holster.AddChild(rig.ToolMesh);
        foreach(ResidentTool tool in Enum.GetValues<ResidentTool>()) if(tool!=ResidentTool.None) rig.ToolMeshes[tool]=HandTool(tool);
        rig.DefaultTool=rig.IsChild?ResidentTool.None:job switch {
            JobType.Farmer=>ResidentTool.Hoe, JobType.Miner=>ResidentTool.Pick,
            JobType.Builder or JobType.Craftsman=>ResidentTool.Hammer, JobType.Soldier=>ResidentTool.Spear,_=>ResidentTool.None };
        rig.SelectTool(rig.DefaultTool);
        rig.CargoMesh=new MeshInstance3D {Name="CarriedResources",Position=new(-.12f,-.106f,-.025f),Visible=false};rig.Hands[1].AddChild(rig.CargoMesh);
        foreach(var resource in new[]{ResourceKind.Food,ResourceKind.Wood,ResourceKind.Stone,ResourceKind.Iron})rig.CargoMeshes[resource]=CargoBundle(resource);
    }
    private Mesh CargoBundle(ResourceKind resource)
    {
        string key="cargo:"+resource;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var root=new Node3D();
        if(resource==ResourceKind.Wood)
        {
            for(int i=0;i<3;i++)Detail(root,"cylinder",new(0,-.025f,i*.035f-.035f),new(.041f,.25f,.041f),"#8b6c46",new(0,0,MathF.PI/2));
            foreach(float side in new[]{-1f,1f})FineBeam(root,new(side*.075f,-.049f,-.053f),new(side*.075f,-.007f,.053f),.005f,"#a18c68");
        }
        else
        {
            Detail(root,"seed",new(0,-.04f,0),new(.24f,.17f,.14f),resource==ResourceKind.Food?"#a8937d":"#645e53");
            for(int i=0;i<3;i++)FineBeam(root,new(-.08f+i*.08f,.025f,-.025f),new(-.08f+i*.08f,.025f,.025f),.004f,"#514237");
        }
        MergeResidentParts(root,key);mesh=root.GetChild<MeshInstance3D>(0).Mesh;_meshes[key]=mesh;root.Free();return mesh;
    }
    private Mesh FingerProxy(string skin,bool closed)
    {
        string key="finger-proxy:"+skin+closed;
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var root=new Node3D();
        Sculpt(root,Loft("resident-palm",new[]{new Vector4(-.035f,0,.026f,.022f),new(-.010f,0,.031f,.020f),new(.025f,0,.035f,.018f),new(.043f,0,.030f,.014f),new(.049f,0,.018f,.012f)}),Vector3.Zero,ResidentMaterial(skin),new(MathF.PI/2,0,0));
        Detail(root,"finger-low",new(0,.01f,0),new(.057f,.048f,.052f),skin);
        for(int digit=0;digit<4;digit++)Detail(root,"finger-low",new(-.022f+digit*.014f,closed?-.063f:-.070f,closed?-.030f:-.005f),new(.010f,closed?.030f:.062f,.010f),skin,new(closed?1.1f:0,0,0));
        Detail(root,"finger-low",new(.025f,-.035f,closed?-.026f:-.021f),new(.013f,.026f,.013f),skin,new(0,0,-.6f));
        MergeResidentParts(root,key);mesh=root.GetChild<MeshInstance3D>(0).Mesh;_meshes[key]=mesh;root.Free();return mesh;
    }
    private Mesh HandTool(ResidentTool kind)
    {
        string key="hand-tool:"+kind;
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var root=new Node3D();
        float length=kind==ResidentTool.Hoe?.61f:kind==ResidentTool.Spear?1.38f:.39f;
        Detail(root,"cylinder",new(0,kind==ResidentTool.Spear?.28f:-.11f,0),new(.024f,length,.024f),"#8b6c46");
        if(kind==ResidentTool.Hammer)
        {
            Detail(root,"masonry",new(0,-.30f,0),new(.13f,.046f,.046f),"#68716f");
            Detail(root,"cylinder",new(0,-.275f,0),new(.028f,.027f,.028f),"#514237");
        }
        else if(kind==ResidentTool.Pick)
        {
            Sculpt(root,Loft("pick-head",new[]{new Vector4(-.17f,-.035f,.004f,.004f),new(-.08f,-.005f,.014f,.015f),new(0,0,.022f,.024f),new(.08f,-.01f,.013f,.014f),new(.17f,-.042f,.003f,.003f)}),new(0,-.30f,0),ResidentMaterial("#68716f"),new(0,MathF.PI/2,0));
        }
        else if(kind==ResidentTool.Axe)
        {
            Sculpt(root,Loft("axe-blade",new[]{new Vector4(-.055f,0,.022f,.025f),new(0,0,.020f,.030f),new(.07f,-.010f,.010f,.065f),new(.10f,-.010f,.003f,.071f)}),new(0,-.27f,0),ResidentMaterial("#68716f"),new(0,MathF.PI/2,0));
        }
        else if(kind==ResidentTool.Hoe)
            Detail(root,"masonry",new(0,-.415f,-.045f),new(.13f,.016f,.10f),"#68716f",new(.25f,0,0));
        else Detail(root,"cone",new(0,1.035f,0),new(.046f,.15f,.029f),"#7d8581");
        MergeResidentParts(root,key);
        mesh=root.GetChild<MeshInstance3D>(0).Mesh;_meshes[key]=mesh;root.Free();return mesh;
    }
}
