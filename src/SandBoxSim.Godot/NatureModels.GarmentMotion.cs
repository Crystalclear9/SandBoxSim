using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Evaluate the final mesh with the actual action solver and skeleton bind
    // matrices. Rest-pose topology checks alone cannot reveal stretched axillae.
    private void ValidateGarmentMotion()
    {
        float maximum=0,shoulderExcursion=0;int poses=0;
        foreach(int identity in new[]{17,55})
        foreach(var job in new[]{JobType.Farmer,JobType.Gatherer,JobType.Trader})
        {
            var rig=Resident(identity,false,job);
            try
            {
                var arrays=rig.BodySkin.Mesh.SurfaceGetArrays(0);
                var source=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var seam=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                var edges=new HashSet<(int A,int B)>();
                for(int i=0;i<indices.Length;i+=3)
                {
                    void Add(int a,int b)
                    {if(source[a].Y>1&&source[b].Y>1&&source[a].DistanceSquaredTo(source[b])>1e-10f)edges.Add(a<b?(a,b):(b,a));}
                    Add(indices[i],indices[i+1]);Add(indices[i+1],indices[i+2]);Add(indices[i+2],indices[i]);
                }
                var posed=new Vector3[source.Length];
                foreach(var action in new[]{ActionKind.None,ActionKind.GatherWood,ActionKind.Farm,ActionKind.BuildHouse,ActionKind.Socialize,ActionKind.Eat,ActionKind.Take})
                {
                    var phase=action==ActionKind.None?ActionPhase.Moving:ActionPhase.Executing;
                    for(int frame=0;frame<60;frame++)
                    {
                        rig.PresentAction(1f/30,action,phase,false,.73f);
                        if(frame%15!=14)continue;
                        var transforms=new Transform3D[rig.ShoulderSkeleton.GetBoneCount()];
                        for(int bone=0;bone<transforms.Length;bone++)
                            transforms[bone]=rig.ShoulderSkeleton.GetBonePose(bone)*rig.DetailedBodySkin.GetBindPose(bone);
                        for(int side=0;side<2;side++)
                        {
                            var expected=rig.Arms[side].Transform*rig.DetailedBodySkin.GetBindPose(side+1);
                            if(!transforms[side+1].IsEqualApprox(expected))throw new InvalidOperationException("Garment skeleton does not match the actual upper-arm pose");
                            float excursion=rig.Arms[side].Position.DistanceTo(rig.ShoulderRest(side));
                            if(excursion>.026f)throw new InvalidOperationException("Shoulder girdle exceeds its bounded reach support");
                            shoulderExcursion=MathF.Max(shoulderExcursion,excursion);
                        }
                        for(int i=0;i<source.Length;i++)
                        {
                            var point=Vector3.Zero;
                            for(int j=0;j<4;j++)point+=(transforms[bones[i*4+j]]*source[i])*weights[i*4+j];
                            if(!point.IsFinite())throw new InvalidOperationException("Non-finite garment action deformation");
                            posed[i]=point;
                            // Godot quantizes skin weights to 16 bits. Compare
                            // with the decoded weights actually rendered, not
                            // an ideal sum=1 that introduces a 10–20 μm error.
                            var trunk=Vector3.Zero;
                            for(int j=0;j<4;j++)trunk+=(bones[i*4+j]==3?rig.PelvicClothPose*source[i]:source[i])*weights[i*4+j];
                            if((MathF.Abs(source[i].X)<.04f||source[i].Y<.90f||(source[i].Y>1.25f&&seam[i].X<=0))&&point.DistanceTo(trunk)>.000002f)
                                throw new InvalidOperationException($"Arm motion pulls the garment centre, neckline or pelvic waist: {identity}/{job}/{action}, source={source[i]}, posed={point}, expected={trunk}, weights={weights[i*4]}/{weights[i*4+1]}/{weights[i*4+2]}/{weights[i*4+3]}");
                            if(seam[i].X<0&&source[i].Y<ResidentRig.BodyY(1.18f)-.10f&&point.DistanceTo(trunk)>.035f)
                                throw new InvalidOperationException("Arm motion drags the lower side chest into a wing");
                            if(seam[i].X>.2629f)
                            {
                                var cuff=transforms[source[i].X<0?1:2]*source[i];
                                if(point.DistanceTo(cuff)>.000002f)throw new InvalidOperationException("Garment cuff detaches from the upper arm");
                            }
                        }
                        foreach(var edge in edges)
                        {
                            float ratio=posed[edge.A].DistanceTo(posed[edge.B])/source[edge.A].DistanceTo(source[edge.B]);
                            maximum=MathF.Max(maximum,ratio);
                            if(ratio>4.5f)throw new InvalidOperationException($"Stretched garment action edge: {job}/{action}, ratio={ratio}, rest={source[edge.A]} -> {source[edge.B]}, seam={seam[edge.A]} -> {seam[edge.B]}");
                        }
                        poses++;
                    }
                    var frozenLeft=rig.Arms[0].Transform;var frozenRight=rig.Arms[1].Transform;
                    rig.PresentAction(1,action,phase,true,.73f);
                    if(rig.Arms[0].Transform!=frozenLeft||rig.Arms[1].Transform!=frozenRight)throw new InvalidOperationException("Paused reach moves the shoulder girdle");
                }
            }
            finally{rig.Free();}
        }
        if(shoulderExcursion<.005f)throw new InvalidOperationException("Reaching actions retain a fixed shoulder anchor");
        GD.Print($"GARMENT_MOTION_PASS: cuts=3, actualActionPoses={poses}, maximumEdgeStretch={maximum:0.00}, shoulderExcursion={shoulderExcursion:0.000} m, fixed centre/neck/waist, attached cuffs and paused girdle");
    }
}
