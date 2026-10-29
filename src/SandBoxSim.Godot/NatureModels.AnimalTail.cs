using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static void BindWolfTail(Godot.Collections.Array arrays)
    {
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var b=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var w=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        for(int i=0;i<p.Length;i++)
        {
            float tail=0;for(int k=0;k<4;k++)if(b[i*4+k]==2)tail+=w[i*4+k];if(tail==0)continue;
            float along=Math.Clamp((.815693f-p[i].Y)/.51f,0,1);
            float root=1-Mathf.SmoothStep(.10f,.55f,along),tip=Mathf.SmoothStep(.55f,.95f,along),middle=1-root-tip;
            var weights=new Dictionary<int,float>();
            for(int k=0;k<4;k++)if(b[i*4+k]!=2){weights.TryGetValue(b[i*4+k],out float old);weights[b[i*4+k]]=old+w[i*4+k];}
            weights[2]=tail*root;weights[19]=tail*middle;weights[20]=tail*tip;
            var top=weights.OrderByDescending(e=>e.Value).ThenBy(e=>e.Key).Take(4).ToArray();float sum=top.Sum(e=>e.Value);
            for(int k=0;k<4;k++){b[i*4+k]=k<top.Length?top[k].Key:0;w[i*4+k]=k<top.Length?top[k].Value/sum:0;}
        }
        arrays[(int)Mesh.ArrayType.Bones]=b;arrays[(int)Mesh.ArrayType.Weights]=w;
    }
    private Vector3 WolfTailCenter(float height)
    {
        var a=AuthoredAnimalData(true).Mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var b=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var w=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();var sum=Vector3.Zero;int count=0;
        for(int i=0;i<p.Length;i++)
        {
            float tail=0;for(int k=0;k<4;k++)if(b[i*4+k] is 2 or 19 or 20)tail+=w[i*4+k];
            if(tail<.95f||MathF.Abs(p[i].Y-height)>.025f)continue;sum+=p[i];count++;
        }
        if(count<12)throw new InvalidOperationException("Missing source tail section");sum/=count;sum.X=0;return sum;
    }
    private void InstallWolfTail(AnimalRig rig)
    {
        var root=InTorso(rig.Tail,rig.Body).Origin;var middle=WolfTailCenter(.60f);var tip=WolfTailCenter(.38f);
        rig.TailMiddle=new Node3D {Name="TailMiddle",Position=middle-root};rig.Tail.AddChild(rig.TailMiddle);
        rig.TailTip=new Node3D {Name="TailTip",Position=tip-middle};rig.TailMiddle.AddChild(rig.TailTip);
    }
    private static void ValidateWolfTail(AnimalRig rig)
    {
        float length=rig.TailMiddle!.Position.Length()+rig.TailTip!.Position.Length(),travel=0;
        var prior=InTorso(rig.TailTip,rig.Body).Origin;
        for(int i=0;i<120;i++)
        {
            rig.Pose(1f/60,i<60,false);var current=InTorso(rig.TailTip,rig.Body).Origin;travel+=current.DistanceTo(prior);prior=current;
            if(MathF.Abs(rig.TailMiddle.Position.Length()+rig.TailTip.Position.Length()-length)>.000001f||!current.IsFinite())throw new InvalidOperationException("Segmented wolf tail stretches or becomes non-finite");
            if(!rig.FurSkeleton!.GetBonePose(19).Basis.IsEqualApprox(rig.TailMiddle.Basis)||!rig.FurSkeleton.GetBonePose(20).Basis.IsEqualApprox(rig.TailTip.Basis))
                throw new InvalidOperationException("Wolf tail skin does not follow its two distal joints");
        }
        var frozen=rig.TailTip.Transform;rig.Pose(1,true,true);
        if(frozen!=rig.TailTip.Transform||travel<.01f)throw new InvalidOperationException("Wolf distal tail is rigid or ignores pause");
        GD.Print($"SEGMENTED_TAIL_PASS: two source-section joints, fixed segment lengths, shared skin poses, travel={travel:F3} m and pause");
    }
}
