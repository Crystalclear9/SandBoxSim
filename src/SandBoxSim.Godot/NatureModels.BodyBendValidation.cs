using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateLumbarClothing()
    {
        int probes=0;float excursion=0;
        foreach(int identity in new[]{17,55})
        foreach(var job in new[]{JobType.Farmer,JobType.Gatherer,JobType.Trader})
        {
            var rig=Resident(identity,false,job);
            try
            {
                if(rig.ShoulderSkeleton.GetBoneCount()!=(rig.HasShoulderSupports?ResidentRig.SupportedBodyBones:4)||rig.DetailedBodySkin.GetBindCount()!=rig.ShoulderSkeleton.GetBoneCount())
                    throw new InvalidOperationException("Adult garment lacks its pelvic cloth binding");
                var cloth=rig.DetailedBodyMesh.SurfaceGetArrays(0);var p=cloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones=cloth[(int)Mesh.ArrayType.Bones].AsInt32Array();var w=cloth[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var ids=cloth[(int)Mesh.ArrayType.Index].AsInt32Array();var posed=new Vector3[p.Length];
                var pants=rig.Trousers!.Mesh.SurfaceGetArrays(0);var pp=pants[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                float hem=float.PositiveInfinity;foreach(var point in p)hem=MathF.Min(hem,point.Y);
                var pb=pants[(int)Mesh.ArrayType.Bones].AsInt32Array();var pw=pants[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                foreach(var action in new[]{ActionKind.Take,ActionKind.BuildHouse,ActionKind.None})
                for(int f=0;f<120;f++)
                {
                    rig.PoseAction(1f/60,action,action==ActionKind.None?ActionPhase.Moving:ActionPhase.Executing,false,0);
                    if(f%30!=29)continue;
                    if(!rig.ShoulderSkeleton.GetBonePose(3).IsEqualApprox(rig.PelvicClothPose)||
                        !rig.LowerGarmentFrame.Basis.IsEqualApprox(action==ActionKind.None?rig.GaitPelvisBasis:Basis.Identity))
                        throw new InvalidOperationException("Lower cloth inherits the rigid chest rotation");
                    var transforms=new Transform3D[rig.ShoulderSkeleton.GetBoneCount()];
                    for(int b=0;b<transforms.Length;b++)transforms[b]=rig.Torso.Transform*rig.ShoulderSkeleton.GetBonePose(b)*rig.DetailedBodySkin.GetBindPose(b);
                    for(int i=0;i<p.Length;i++)
                    {
                        var v=Vector3.Zero;for(int j=0;j<4;j++)v+=transforms[bones[i*4+j]]*p[i]*w[i*4+j];posed[i]=v;
                        if(!v.IsFinite())throw new InvalidOperationException("Non-finite lumbar skin deformation");
                        if(p[i].Y<.84f)
                        {
                            if(w[i*4+3]<.999f)throw new InvalidOperationException("The shirt hem is still bound to the rigid chest");
                            excursion=MathF.Max(excursion,v.DistanceTo(rig.Torso.Transform*p[i]));
                        }
                    }
                    if(MathF.Abs(rig.Holster.Basis.Determinant()-1)>.0001f)throw new InvalidOperationException("Lumbar bending stretches the mounted tool");
                    var trouserTransforms=new[]{rig.LowerGarmentFrame,rig.Legs[0].Transform*rig.Trousers.Skin.GetBindPose(1),rig.Legs[1].Transform*rig.Trousers.Skin.GetBindPose(2),
                        rig.Legs[0].Transform*rig.Knees[0].Transform*rig.Trousers.Skin.GetBindPose(3),rig.Legs[1].Transform*rig.Knees[1].Transform*rig.Trousers.Skin.GetBindPose(4)};
                    for(int i=0;i<pp.Length;i+=9)
                    {
                        // A short blouse deliberately exposes the hip. Check
                        // the actual overlapping waistband, not exposed pants.
                        if(pp[i].Y<MathF.Max(.82f,hem+.020f)||pp[i].Y>.91f)continue;
                        var point=Vector3.Zero;for(int j=0;j<4;j++)point+=trouserTransforms[pb[i*4+j]]*pp[i]*pw[i*4+j];
                        float front=float.PositiveInfinity,back=float.NegativeInfinity;
                        for(int t=0;t<ids.Length;t+=3)
                        {
                            var a=posed[ids[t]];var b=posed[ids[t+1]];var c=posed[ids[t+2]];
                            var ab=new Vector2(b.X-a.X,b.Y-a.Y);var ac=new Vector2(c.X-a.X,c.Y-a.Y);float det=ab.Cross(ac);
                            if(MathF.Abs(det)<1e-10f)continue;var hit=new Vector2(point.X-a.X,point.Y-a.Y);
                            // Inverse skin transforms introduce sub-micrometre
                            // roundoff at shared triangle edges. Include those
                            // edges without relaxing the millimetre clearance.
                            float u=hit.Cross(ac)/det,v=ab.Cross(hit)/det;if(u<-.000001f||v<-.000001f||u+v>1.000001f)continue;
                            float z=a.Z*(1-u-v)+b.Z*u+c.Z*v;front=MathF.Min(front,z);back=MathF.Max(back,z);
                        }
                        if(!float.IsFinite(front)||point.Z<front-.001f||point.Z>back+.001f)
                            throw new InvalidOperationException($"Trousers pierce the lower shirt: {identity}/{job}/{action}/{f}, source={pp[i]}, point={point}, cloth={front}/{back}, hem={hem}, torso={rig.Torso.Rotation}, pelvis={rig.LowerGarmentFrame}");
                        probes++;
                    }
                }
                var frozen=rig.PelvicClothPose;var mount=rig.Holster.Transform;
                rig.PoseAction(1,ActionKind.Take,ActionPhase.Executing,true,0);
                if(rig.PelvicClothPose!=frozen||rig.Holster.Transform!=mount)throw new InvalidOperationException("Paused lower cloth or equipment moves");
            }
            finally{rig.Free();}
        }
        if(excursion<.015f||probes==0)throw new InvalidOperationException("Lumbar clothing remains a rigid plank");
        float sway=0,knee=0,entryDrift=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var anchors=new[]{InTorso(rig.Feet[0],rig).Origin,InTorso(rig.Feet[1],rig).Origin};
                for(int f=0;f<240;f++)
                {
                    rig.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,.31f);
                    for(int side=0;side<2;side++)
                    {
                        var foot=InTorso(rig.Feet[side],rig);
                        float drift=foot.Origin.DistanceTo(anchors[side]);if(f<21)entryDrift=MathF.Max(entryDrift,drift);
                        if(drift>(f<21?.004f:.0001f)||foot.Basis.Y.Dot(Vector3.Up)<.99999f)
                            throw new InvalidOperationException($"Relaxed stance slides the feet or tilts the soles: {identity}/{f}/{side}, ankle={foot.Origin}, target={anchors[side]}, up={foot.Basis.Y}, hip={rig.Legs[side].Position}");
                        if(MathF.Abs(rig.Knees[side].Position.Length()-ResidentRig.ThighLength)>.000001f)
                            throw new InvalidOperationException("Relaxed stance changes the thigh length");
                        sway=MathF.Max(sway,MathF.Abs(rig.Legs[side].Position.X-(side==0?-rig.HipHalfWidth:rig.HipHalfWidth)));
                        knee=MathF.Max(knee,MathF.Abs(rig.Knees[side].Rotation.X));
                    }
                }
                var leg=rig.Legs[0].Transform;rig.PoseAction(1,ActionKind.None,ActionPhase.Idle,true,0);
                if(leg!=rig.Legs[0].Transform)throw new InvalidOperationException("Paused relaxed stance moves");
            }
            finally{rig.Free();}
        }
        if(sway<.005f||knee<.05f)throw new InvalidOperationException("Relaxed stance retains locked knees and a fixed pelvis");
        GD.Print($"LUMBAR_CLOTH_PASS: 2 frames/3 cuts, 3 actions, {probes} actual posed trouser-clearance rays; pelvic/chest excursion={excursion:F3} m, rigid mounts and pause");
        GD.Print($"RELAXED_STANCE_PASS: 2 frames/480 steps, planted level soles after 0.35s entry, entry drift={entryDrift:F4} m, fixed thigh lengths, sway={sway:F3} m, knee={knee:F3} rad and pause");
    }
}
