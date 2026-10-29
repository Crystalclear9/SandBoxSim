using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidatePawArticulation(AnimalRig rig,bool wolf)
    {
        var data=AuthoredAnimalData(wolf);var a=data.Mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var supports=new List<(int Foot,Vector3 Root,int A,int B,int C,float U,float V)>();
        int nails=0;float maximumRootGap=0;
        for(int foot=0;foot<4;foot++)
        {
            var detail=rig.Paws[foot]!.GetNode<MeshInstance3D>("Claws");var geometry=detail.Mesh.SurfaceGetArrays(0);
            var cp=geometry[(int)Mesh.ArrayType.Vertex].AsVector3Array();var cn=geometry[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if(detail.Mesh!=PawKeratin(wolf,foot)||detail.VisibilityRangeEnd!=14)throw new InvalidOperationException("Keratin cache or distance policy changed");
            for(int i=0;i<cp.Length;i++)if(!cp[i].IsFinite()||!cn[i].IsFinite()||MathF.Abs(cn[i].Length()-1)>.01f)throw new InvalidOperationException("Invalid claw/hoof keratin geometry");
            foreach(var root in detail.Mesh.GetMeta("SourceRoots").AsVector3Array())
            {
                nails++;bool found=false;
                for(int t=0;t<ids.Length;t+=3)
                {
                    int aa=ids[t],b=ids[t+1],c=ids[t+2];if(AuthoredFoot(aa,bones,weights,wolf)!=foot)continue;
                    var ab=p[b]-p[aa];var ac=p[c]-p[aa];var d=root-p[aa];
                    float m=ab.Dot(ab),n=ab.Dot(ac),o=ac.Dot(ac),det=m*o-n*n;if(MathF.Abs(det)<1e-15f)continue;
                    float u=(d.Dot(ab)*o-d.Dot(ac)*n)/det,v=(d.Dot(ac)*m-d.Dot(ab)*n)/det;
                    if(u<-.00001f||v<-.00001f||u+v>1.00001f||(p[aa]+ab*u+ac*v).DistanceTo(root)>.00002f)continue;
                    supports.Add((foot,root,aa,b,c,u,v));found=true;break;
                }
                if(!found)throw new InvalidOperationException("Claw root has no actual source triangle support");
            }
        }
        if(nails!=(wolf?18:8))throw new InvalidOperationException("Wrong species-specific claw/dewclaw arrangement");
        var posed=new Vector3[p.Length];int samples=0,contacts=0;
        foreach(float slope in new[]{0f,.12f,-.12f})
        for(int frame=0;frame<120;frame++)
        {
            float Ground(Vector3 point)=>point.Z*slope;
            rig.Pose(1f/60,true,false,false,Ground);if(frame%12!=11)continue;
            int count=rig.FurSkeleton!.GetBoneCount();var palette=new Transform3D[count];var ready=new bool[count];
            Transform3D Pose(int b)
            {
                if(ready[b])return palette[b];int parent=rig.FurSkeleton.GetBoneParent(b);
                palette[b]=(parent<0?Transform3D.Identity:Pose(parent))*rig.FurSkeleton.GetBonePose(b);ready[b]=true;return palette[b];
            }
            for(int b=0;b<count;b++)Pose(b);
            for(int b=0;b<count;b++)palette[b]*=rig.FurSkin!.Skin.GetBindPose(b);
            for(int i=0;i<p.Length;i++)
            {
                var point=Vector3.Zero;for(int k=0;k<4;k++)point+=palette[bones[i*4+k]]*p[i]*weights[i*4+k];posed[i]=point;
                if(!point.IsFinite())throw new InvalidOperationException("Non-finite articulated foot skin");
                int foot=AuthoredFoot(i,bones,weights,wolf);
                if(foot<0||rig.PawLift[foot]>.001f||p[i].Y>.015f)continue;
                var world=rig.Body.Transform*point;
                if(world.Y-Ground(world)<-.010f)throw new InvalidOperationException($"Planted toe penetrates terrain: {wolf}/{foot}/{slope}/{frame}, {world.Y-Ground(world)}");
                contacts++;
            }
            foreach(var support in supports)
            {
                var source=posed[support.A]*(1-support.U-support.V)+posed[support.B]*support.U+posed[support.C]*support.V;
                var rigid=InTorso(rig.Paws[support.Foot]!,rig.Body)*(support.Root-AuthoredPawAnchor(data.Soles[support.Foot],wolf));
                float gap=source.DistanceTo(rigid);maximumRootGap=MathF.Max(maximumRootGap,gap);
                if(gap>.0015f)throw new InvalidOperationException($"Keratin floats from its articulated skin: {wolf}/{support.Foot}, {gap}");
            }
            for(int foot=0;foot<4;foot++)if(rig.PawLift[foot]<.001f)
            {
                var normal=new Vector3(0,1,-slope).Normalized();var up=(rig.Body.Basis*InTorso(rig.Paws[foot]!,rig.Body).Basis*Vector3.Up).Normalized();
                if(up.Dot(normal)<.999f)throw new InvalidOperationException("Planted hoof/paw rotates with the shin instead of the terrain");
            }
            samples++;
        }
        var frozen=rig.Paws[0]!.Transform;rig.Pose(1,true,true,false);
        if(rig.Paws[0]!.Transform!=frozen)throw new InvalidOperationException("Paused foot articulation continues");
        if(contacts==0)throw new InvalidOperationException("No stance-phase actual sole vertices checked");
        for(int i=0;i<100;i++)
        {
            float phase=i*Mathf.Tau/100;var sample=AnimalRig.GaitContact(phase,wolf);
            if(!float.IsFinite(sample.Travel)||sample.Lift<0||sample.Lift>.0751f)throw new InvalidOperationException("Invalid species gait trajectory");
        }
        foreach(float boundary in new[]{0f,Mathf.Tau*(wolf?.58f:.74f)})
        {
            var before=AnimalRig.GaitContact(boundary-.00001f,wolf);var after=AnimalRig.GaitContact(boundary+.00001f,wolf);
            if(MathF.Abs(before.Travel-after.Travel)>.0001f||MathF.Abs(before.Lift-after.Lift)>.0001f)
                throw new InvalidOperationException("Foot trajectory jumps at stance/swing or cycle boundaries");
        }
        GD.Print($"ARTICULATED_PAWS_PASS: {(wolf?"wolf":"deer")}, {nails} source-supported keratin roots, {samples} slope/gait poses, {contacts} actual stance vertices, maximum root gap={maximumRootGap:F6} m, level support and pause");
    }
}
