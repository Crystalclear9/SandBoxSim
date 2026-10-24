using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static string AuthoredRoot(bool wolf)=>"res://assets/models/animals/"+(wolf?"0ad-wolf/":"cdmir-deer/");
    private readonly (ArrayMesh Mesh,Vector3[] Joints,Vector3[] Soles)?[] _authoredAnimals=new (ArrayMesh,Vector3[],Vector3[])?[2];
    private Material AuthoredAnimalMaterial(bool wolf,string name)
    {
        string folder=AuthoredRoot(wolf);string key="authored-animal:"+wolf+":"+name;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        if(name=="Eye")return _residentMaterials[key]=new StandardMaterial3D {AlbedoColor=new("#292018"),Roughness=.16f,MetallicSpecular=.4f};
        string texture=wolf?"wolf-grey":name=="Body"?"deer-body":name=="Antlers"?"deer-antlers":"deer-head";
        var material=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform sampler2D albedo_map:source_color,filter_linear_mipmap_anisotropic;
uniform sampler2D normal_map:hint_normal,filter_linear_mipmap_anisotropic;uniform bool has_normal=true;uniform float gain=1.0;
void fragment(){ALBEDO=texture(albedo_map,UV).rgb*gain;NORMAL_MAP=has_normal?texture(normal_map,UV).rgb:vec3(.5,.5,1);NORMAL_MAP_DEPTH=.32;ROUGHNESS=.86;SPECULAR=.18;}"}};
        material.SetShaderParameter("albedo_map",GD.Load<Texture2D>(folder+texture+".png"));
        if(!wolf)material.SetShaderParameter("normal_map",GD.Load<Texture2D>(folder+texture+"-normal.png"));
        material.SetShaderParameter("has_normal",!wolf);
        material.SetShaderParameter("gain",wolf?.76f:.90f);
        return _residentMaterials[key]=material;
    }
    private (ArrayMesh Mesh,Vector3[] Joints,Vector3[] Soles) AuthoredAnimalData(bool wolf)
    {
        int species=wolf?1:0;if(_authoredAnimals[species].HasValue)return _authoredAnimals[species]!.Value;
        string folder=AuthoredRoot(wolf);
        string raw=FileAccess.GetFileAsString(folder+(wolf?"wolf-authored.mesh.json":"deer-authored.mesh.json"));
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=(wolf?"19c72efc9c7f0e95248723e51a86c9f268937988db661e961332fac359deb303":"59c8a6ae08b32f185ab3670d64ebb05660cc369301f6911cb2c3a2f323b9e280"))throw new InvalidOperationException("Authored deer hash mismatch");
        using var document=JsonDocument.Parse(raw);var root=document.RootElement;
        if(root.GetProperty("source").GetProperty("license").GetString()!=(wolf?"CC-BY-SA-3.0":"CC0-1.0")||!FileAccess.FileExists(folder+"LICENSE.txt"))throw new InvalidOperationException("Missing deer licence");
        float[] Floats(JsonElement e){var a=new float[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())a[i++]=v.GetSingle();return a;}
        int[] Ints(JsonElement e){var a=new int[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())a[i++]=v.GetInt32();return a;}
        Vector3[] Vectors(JsonElement e){var a=new Vector3[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())a[i++]=new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());return a;}
        var mesh=new ArrayMesh();
        foreach(var source in root.GetProperty("surfaces").EnumerateArray())
        {
            var p=Floats(source.GetProperty("positions"));var n=Floats(source.GetProperty("normals"));var t=Floats(source.GetProperty("uv"));
            var vertices=new Vector3[p.Length/3];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
            if(n.Length!=p.Length||t.Length!=vertices.Length*2)throw new InvalidOperationException("Invalid deer attributes");
            for(int i=0;i<vertices.Length;i++){vertices[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);uv[i]=new(t[i*2],t[i*2+1]);}
            var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=uv;
            arrays[(int)Mesh.ArrayType.Bones]=Ints(source.GetProperty("bones"));arrays[(int)Mesh.ArrayType.Weights]=Floats(source.GetProperty("weights"));arrays[(int)Mesh.ArrayType.Index]=Ints(source.GetProperty("indices"));
            var part=new ArrayMesh();part.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            using var tool=new SurfaceTool();tool.CreateFrom(part,0);tool.GenerateTangents();tool.SetMaterial(AuthoredAnimalMaterial(wolf,source.GetProperty("material").GetString()!));tool.Commit(mesh);
        }
        return (_authoredAnimals[species]=((ArrayMesh)WithScreenLods(mesh),Vectors(root.GetProperty("joints")),Vectors(root.GetProperty("soles")))).Value;
    }
    private AnimalRig AuthoredAnimal(bool wolf)
    {
        var data=AuthoredAnimalData(wolf);var rig=new AnimalRig {Predator=wolf,RestSoles=new Vector3[4]};
        rig.Body=new Node3D {Name="Body"};rig.AddChild(rig.Body);
        rig.Neck=new Node3D {Name="Neck",Position=data.Joints[11]};rig.Body.AddChild(rig.Neck);
        rig.Head=new Node3D {Name="Head",Position=data.Joints[1]-data.Joints[11]};rig.Neck.AddChild(rig.Head);
        rig.Tail=new Node3D {Name="Tail",Position=data.Joints[2]};rig.Body.AddChild(rig.Tail);
        for(int i=0;i<4;i++)
        {
            rig.Legs[i]=new Node3D {Name="Leg"+i,Position=data.Joints[3+i*2]};rig.Body.AddChild(rig.Legs[i]);
            rig.Knees[i]=new Node3D {Name="Knee",Position=data.Joints[4+i*2]-data.Joints[3+i*2]};rig.Legs[i].AddChild(rig.Knees[i]);
            rig.RestSoles[i]=data.Soles[i]-data.Joints[4+i*2];
        }
        for(int i=0;i<2;i++){rig.Ears[i]=new Node3D {Name=i==0?"LeftEar":"RightEar",Position=data.Joints[12+i]-data.Joints[1]};rig.Head.AddChild(rig.Ears[i]);}
        var nodes=new Node3D[14];AnimalBones(rig).CopyTo(nodes,0);nodes[12]=rig.Ears[0];nodes[13]=rig.Ears[1];
        var skeleton=rig.FurSkeleton=new Skeleton3D {Name="FurSkeleton"};rig.Body.AddChild(skeleton);var skin=new Skin();
        for(int i=0;i<nodes.Length;i++)skeleton.AddBone("AnimalBone"+i);
        for(int i=0;i<nodes.Length;i++)
        {
            int parent=i==0?-1:i>=12?1:i==1?11:i>=4&&i<=10&&i%2==0?i-1:0;
            if(parent>=0)skeleton.SetBoneParent(i,parent);
            skeleton.SetBoneRest(i,i==0?Transform3D.Identity:nodes[i].Transform);skin.AddBind(i,InTorso(nodes[i],rig.Body).AffineInverse());
        }
        skeleton.ResetBonePoses();rig.FurSkin=new MeshInstance3D {Name=wolf?"AuthoredWolf":"AuthoredDeer",Mesh=data.Mesh,Skin=skin,Skeleton=new NodePath("../FurSkeleton")};rig.Body.AddChild(rig.FurSkin);
        return rig;
    }
    private void ValidateAuthoredAnimals()
    {
      foreach(bool wolf in new[]{false,true})
      {
        var rig=AuthoredAnimal(wolf);var peer=AuthoredAnimal(wolf);var mesh=rig.FurSkin!.Mesh;
        try
        {
        if(mesh!=peer.FurSkin!.Mesh||rig.FurSkeleton!.GetBoneCount()!=14)throw new InvalidOperationException("Animal cache or skeleton mismatch");
        int triangles=0;
        for(int s=0;s<mesh.GetSurfaceCount();s++)
        {
            var a=mesh.SurfaceGetArrays(s);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var bs=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var ws=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();triangles+=a[(int)Mesh.ArrayType.Index].AsInt32Array().Length/3;
            for(int i=0;i<p.Length;i++){float sum=0;for(int k=0;k<4;k++){if(bs[i*4+k]<0||bs[i*4+k]>=14||ws[i*4+k]<0)throw new InvalidOperationException("Invalid deer binding");sum+=ws[i*4+k];}if(!p[i].IsFinite()||!n[i].IsFinite()||MathF.Abs(sum-1)>4f/65535f)throw new InvalidOperationException("Invalid authored deer surface");}
        }
        if(triangles>24000||triangles<10000)throw new InvalidOperationException("Deer surface budget changed");
        foreach(float slope in new[]{0f,.16f,-.16f})for(int frame=0;frame<24;frame++)
        {
            float Ground(Vector3 p)=>p.Z*slope;
            rig.Pose(.04f,true,false,false,Ground);
            for(int leg=0;leg<4;leg++){var foot=rig.Body.Transform*(rig.Legs[leg].Transform*(rig.Knees[leg].Transform*rig.RestSoles![leg]));if(!foot.IsFinite()||foot.Y-Ground(foot)<-.035f||foot.Y-Ground(foot)>.14f)throw new InvalidOperationException($"Authored animal terrain contact: wolf={wolf}, slope={slope}, frame={frame}, leg={leg}, gap={foot.Y-Ground(foot)}");}
        }
        var pose=rig.Head.Transform;rig.Pose(1,true,true);if(pose!=rig.Head.Transform)throw new InvalidOperationException("Paused authored deer moved");
        }
        finally {rig.Free();peer.Free();}
      }
      GD.Print("AUTHORED_ANIMALS_PASS: CC0 deer/CC-BY-SA wolf surfaces, cache, 14 joints, weights, budgets, slopes and pause");
    }
}
