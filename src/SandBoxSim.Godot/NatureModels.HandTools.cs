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
    private Mesh FingerProxy(string skin,bool closed,int side)
    {
        string key="finger-proxy:"+skin+closed+":"+side;
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        if(ResidentRig.HandMirror(side)<0)
        {
            var canonical=FingerProxy(skin,closed,0);var mirrored=new ArrayMesh();
            for(int surface=0;surface<canonical.GetSurfaceCount();surface++)
            {
                var arrays=canonical.SurfaceGetArrays(surface);
                var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for(int i=0;i<points.Length;i++){points[i].X=-points[i].X;normals[i].X=-normals[i].X;}
                var tangents=arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
                for(int i=0;i<tangents.Length;i+=4){tangents[i]=-tangents[i];tangents[i+3]=-tangents[i+3];}
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if(indices.Length==0){indices=new int[points.Length];for(int i=0;i<indices.Length;i++)indices[i]=i;}
                for(int i=0;i<indices.Length;i+=3)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
                arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.Index]=indices;
                if(tangents.Length>0)arrays[(int)Mesh.ArrayType.Tangent]=tangents;
                mirrored.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mirrored.SurfaceSetMaterial(surface,canonical.SurfaceGetMaterial(surface));
            }
            _meshes[key]=mirrored;return mirrored;
        }
        // A static bake of the same anatomy and joint poses as the close-up hand.
        // No per-frame skeleton work is required for distant residents.
        var source=AnatomicalHandMesh(0,skin,true);var result=new ArrayMesh();
        var rest=new Transform3D[16];var posed=new Transform3D[16];
        rest[0]=posed[0]=rest[15]=posed[15]=Transform3D.Identity;
        float curl=closed?1:0;
        for(int digit=0;digit<4;digit++)
        {
            float length=digit==0?.92f:digit==1?1:digit==2?.96f:.86f;
            var bends=ResidentRig.FingerBends(digit,curl);
            for(int joint=0;joint<3;joint++)
            {
                int bone=1+digit*3+joint,parent=joint==0?0:bone-1;
                var origin=joint==0?ResidentRig.FingerRoot(0,digit):new Vector3(0,-(joint==1?.026f:.018f)*length,0);
                var rotation=joint==0?new Vector3(bends.X,0,(digit-1.5f)*.025f*(1-curl)):new Vector3(joint==1?bends.Y:bends.Z,0,0);
                rest[bone]=rest[parent]*new Transform3D(Basis.Identity,origin);
                posed[bone]=posed[parent]*new Transform3D(Basis.FromEuler(rotation),origin);
            }
        }
        rest[13]=new(Basis.Identity,ResidentRig.ThumbRoot(0));rest[14]=rest[13]*new Transform3D(Basis.Identity,new(0,-.018f,0));
        posed[13]=new(Basis.FromEuler(new(curl*.25f,0,.15f+curl*.65f)),ResidentRig.ThumbRoot(0));
        posed[14]=posed[13]*new Transform3D(Basis.FromEuler(new(curl*.55f,0,0)),new(0,-.018f,0));
        var palette=new Transform3D[16];for(int i=0;i<16;i++)palette[i]=posed[i]*rest[i].AffineInverse();
        for(int surface=0;surface<source.GetSurfaceCount();surface++)
        {
            var arrays=source.SurfaceGetArrays(surface);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            for(int i=0;i<points.Length;i++)
            {
                Vector3 point=Vector3.Zero,normal=Vector3.Zero;
                for(int j=0;j<4;j++){var transform=palette[bones[i*4+j]];point+=(transform*points[i])*weights[i*4+j];normal+=(transform.Basis*normals[i])*weights[i*4+j];}
                points[i]=point;normals[i]=normal.Normalized();
            }
            arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;
            arrays[(int)Mesh.ArrayType.Bones]=default(Variant);arrays[(int)Mesh.ArrayType.Weights]=default(Variant);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);result.SurfaceSetMaterial(surface,source.SurfaceGetMaterial(surface));
        }
        _meshes[key]=result;return result;
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
