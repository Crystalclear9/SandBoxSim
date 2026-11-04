using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateForearmNormals()
    {
        int samples=0;double oldError=0,newError=0;float displacement=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                for(int side=0;side<2;side++)
                {
                    var mesh=(ArrayMesh)rig.HandSkins[side].Mesh;
                    if(mesh.GetBlendShapeCount()!=ResidentRig.ForearmNormalPoseCount||mesh.BlendShapeMode!=Mesh.BlendShapeMode.Normalized)
                        throw new InvalidOperationException("Near forearm lost normal-only pose samples");
                    var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    var shapes=mesh.SurfaceGetBlendShapeArrays(0);var normals=new Vector3[shapes.Count][];
                    for(int surface=0;surface<mesh.GetSurfaceCount();surface++)
                    {
                        var source=mesh.SurfaceGetArrays(surface);var sourceP=source[(int)Mesh.ArrayType.Vertex].AsVector3Array();var sourceN=source[(int)Mesh.ArrayType.Normal].AsVector3Array();
                        var targets=mesh.SurfaceGetBlendShapeArrays(surface);
                        for(int shape=0;shape<targets.Count;shape++)
                        {
                            var q=targets[shape][(int)Mesh.ArrayType.Vertex].AsVector3Array();var nn=targets[shape][(int)Mesh.ArrayType.Normal].AsVector3Array();
                            if(surface==0)normals[shape]=nn;
                            for(int i=0;i<q.Length;i++)
                            {
                                displacement=MathF.Max(displacement,q[i].DistanceTo(sourceP[i]));
                                if(!nn[i].IsFinite()||MathF.Abs(nn[i].Length()-1)>.002f)throw new InvalidOperationException("Nonfinite or unnormalized forearm pose normal");
                                if((surface>0||ForearmNormalFreedom(sourceP[i])==0)&&nn[i].DistanceTo(sourceN[i])>.001f)
                                    throw new InvalidOperationException("Forearm normal correction reaches fixed wrist, fingers, nails or elbow root");
                            }
                        }
                    }
                    var rest=SkinSurfaceNormals(p,ids,p);
                    foreach(float bend in new[]{.35f,.95f,1.65f})foreach(float roll in new[]{-1.2f,-.4f,.7f,1.2f})
                    {
                        rig.StudyArmElevation(1f/60,.7f,.5f,side,roll,bend);
                        var palette=new Transform3D[ResidentRig.HandBoneCount];var global=new Transform3D[palette.Length];var skeleton=rig.HandSkeletons[side];
                        for(int bone=0;bone<palette.Length;bone++)
                        {
                            var local=skeleton.GetBoneRest(bone)*skeleton.GetBonePose(bone);int parent=skeleton.GetBoneParent(bone);
                            global[bone]=parent<0?local:global[parent]*local;palette[bone]=global[bone]*rig.HandSkins[side].Skin.GetBindPose(bone);
                        }
                        var posed=new Vector3[p.Length];var blends=new Basis[p.Length];var values=new float[normals.Length];float total=0;
                        for(int k=0;k<values.Length;k++){values[k]=rig.HandSkins[side].GetBlendShapeValue(k);total+=values[k];if(values[k]<0)throw new InvalidOperationException("Negative forearm pose weight");}
                        if(MathF.Abs(total-1)>.00001f)throw new InvalidOperationException("Forearm pose weights are not normalized");
                        for(int i=0;i<p.Length;i++)
                        {
                            var basis=new Basis(Vector3.Zero,Vector3.Zero,Vector3.Zero);Vector3 origin=Vector3.Zero;
                            for(int j=0;j<4;j++){var pose=palette[bones[i*4+j]];float w=weights[i*4+j];basis.X+=pose.Basis.X*w;basis.Y+=pose.Basis.Y*w;basis.Z+=pose.Basis.Z*w;origin+=pose.Origin*w;}
                            blends[i]=basis;posed[i]=basis*p[i]+origin;
                        }
                        var actual=SkinSurfaceNormals(posed,ids,p);
                        for(int i=0;i<p.Length;i++)if(ForearmNormalFreedom(p[i])>.95f&&rest[i].LengthSquared()>.5f&&actual[i].LengthSquared()>.5f)
                        {
                            Vector3 corrected=Vector3.Zero;for(int k=0;k<values.Length;k++)corrected+=normals[k][i]*values[k];
                            var old=(blends[i]*n[i]).Normalized();var target=new Quaternion((blends[i]*rest[i]).Normalized(),actual[i])*old;
                            oldError+=old.AngleTo(target);newError+=(blends[i]*corrected).Normalized().AngleTo(target);samples++;
                        }
                        rig.PoseAction(1,ActionKind.None,ActionPhase.Idle,true,0);rig.SetHandDetail(false);rig.SetHandDetail(true);
                        for(int k=0;k<values.Length;k++)if(MathF.Abs(values[k]-rig.HandSkins[side].GetBlendShapeValue(k))>.00001f)
                            throw new InvalidOperationException("Pause or LOD restores stale forearm normal weights");
                    }
                }
            }
            finally{rig.Free();}
        }
        if(displacement>.000001f||samples<1000||newError>=oldError*.90)
            throw new InvalidOperationException($"Forearm normal correction fails: points move={displacement}, samples={samples}, angular error={oldError}/{newError}");
        GD.Print($"FOREARM_NORMAL_PASS: {samples} actual skin samples, adults/both arms, combined intermediate bend/roll; mean normal error {oldError/samples*180/Math.PI:F3} -> {newError/samples*180/Math.PI:F3} degrees, vertex displacement={displacement:F7} m; fixed fingers/wrist/nails, pause and LOD retained");
    }
}
