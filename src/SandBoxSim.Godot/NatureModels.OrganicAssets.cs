using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private bool _exportingAnimals;
    public void ExportAnimalSources(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);_exportingAnimals=true;
        foreach(bool wolf in new[]{false,true})
        {
            var rig=DetailedAnimal(wolf);var fur=Fur(wolf);
            var nodes=AnimalBones(rig);var boneMap=new Dictionary<Node3D,int>();
            for(int i=0;i<nodes.Length;i++)boneMap[nodes[i]]=i;
            var points=new List<float>();var indices=new List<int>();var bones=new List<int>();
            void Visit(Node3D node,int bone)
            {
                if(boneMap.TryGetValue(node,out int b))bone=b;
                if(node is MeshInstance3D part)
                {
                    var transform=InTorso(part,rig.Body);
                    for(int s=0;s<part.Mesh.GetSurfaceCount();s++)
                    {
                        if((part.MaterialOverride??part.Mesh.SurfaceGetMaterial(s))!=fur)continue;
                        var data=part.Mesh.SurfaceGetArrays(s);var vertices=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();int start=points.Count/3;
                        foreach(var p in vertices){var v=transform*p;points.Add(v.X);points.Add(v.Y);points.Add(v.Z);bones.Add(bone==0&&v.Z<-.48f&&v.Y>(wolf?.82f:1.13f)?11:bone);}
                        var triangles=data[(int)Mesh.ArrayType.Index].AsInt32Array();foreach(int i in triangles)indices.Add(start+i);
                    }
                }
                foreach(Node child in node.GetChildren())if(child is Node3D model)Visit(model,bone);
            }
            Visit(rig.Body,0);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder,wolf?"wolf-source.json":"deer-source.json"),JsonSerializer.Serialize(new{schemaVersion=1,wolf,positions=points,indices,bones}));
            rig.Free();
        }
        _exportingAnimals=false;GD.Print("ANIMAL_SOURCES_EXPORTED");
    }
    private void InstallAnimalSurface(AnimalRig rig,bool wolf)
    {
        var fur=Fur(wolf);var nodes=AnimalBones(rig);string key=wolf?"continuous-wolf":"continuous-deer";
        if(!_meshes.TryGetValue(key,out var mesh))
        {
            string raw=FileAccess.GetFileAsString("res://assets/models/animals/"+(wolf?"wolf":"deer")+".skin.json");
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            if(hash!=(wolf?"457858588602c8f0c23ed222ad9c975517eb53abe605d70c890a27c7c04ce015":"c5517c950b8909d211b5b0c19916fe3883d5c92412120be208fb97d342421473"))throw new InvalidOperationException("Animal skin asset hash mismatch");
            using var document=JsonDocument.Parse(raw);var root=document.RootElement;
            float[] Floats(string name){var e=root.GetProperty(name);var values=new float[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())values[i++]=v.GetSingle();return values;}
            int[] Ints(string name){var e=root.GetProperty(name);var values=new int[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())values[i++]=v.GetInt32();return values;}
            var p=Floats("positions");var n=Floats("normals");var u=Floats("uv");var vertices=new Vector3[p.Length/3];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
            for(int i=0;i<vertices.Length;i++){vertices[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);uv[i]=new(u[i*2],u[i*2+1]);}
            var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=uv;
            arrays[(int)Mesh.ArrayType.Bones]=Ints("bones");arrays[(int)Mesh.ArrayType.Weights]=Floats("weights");arrays[(int)Mesh.ArrayType.Index]=Ints("indices");
            var baked=new ArrayMesh();baked.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);baked.SurfaceSetMaterial(0,fur);mesh=WithScreenLods(baked);_meshes[key]=mesh;
        }
        var original=new List<MeshInstance3D>();
        void Visit(Node3D node){foreach(Node child in node.GetChildren())if(child is Node3D model){if(model is MeshInstance3D part&&part.MaterialOverride==fur)original.Add(part);Visit(model);}}
        Visit(rig.Body);foreach(var part in original){part.GetParent().RemoveChild(part);part.Free();}
        var skeleton=rig.FurSkeleton=new Skeleton3D {Name="FurSkeleton"};rig.Body.AddChild(skeleton);var skin=new Skin();var rest=new Transform3D[nodes.Length];
        for(int i=0;i<nodes.Length;i++)skeleton.AddBone("CoatBone"+i);
        for(int i=0;i<nodes.Length;i++)
        {
            int parent=i==0?-1:i==1?11:i>=4&&i%2==0?i-1:0;
            if(parent>=0)skeleton.SetBoneParent(i,parent);
            var local=i==0?Transform3D.Identity:nodes[i].Transform;skeleton.SetBoneRest(i,local);rest[i]=InTorso(nodes[i],rig.Body);skin.AddBind(i,rest[i].AffineInverse());
        }
        skeleton.ResetBonePoses();rig.FurSkin=new MeshInstance3D {Name="ContinuousCoat",Mesh=mesh,Skin=skin,Skeleton=new NodePath("../FurSkeleton")};rig.Body.AddChild(rig.FurSkin);
        var groom=new MeshInstance3D {Name="GuardHairCoat",Mesh=GroomAnimalCoat(mesh,wolf),Skin=skin,
            Skeleton=new NodePath("../FurSkeleton"),VisibilityRangeEnd=18,VisibilityRangeEndMargin=3,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        rig.Body.AddChild(groom);

    }
    private static Node3D[] AnimalBones(AnimalRig rig)=>new[]{rig.Body,rig.Head,rig.Tail,rig.Legs[0],rig.Knees[0],rig.Legs[1],rig.Knees[1],rig.Legs[2],rig.Knees[2],rig.Legs[3],rig.Knees[3],rig.Neck};
}
