using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<ulong,Vector3[][]> _validationSleeveShapes=new();
    private Vector3[] CorrectedSleeveSource(ResidentRig rig,Vector3[] source)
    {
        var mesh=(ArrayMesh)rig.DetailedBodyMesh;
        if(!_validationSleeveShapes.TryGetValue(mesh.GetInstanceId(),out var targets))
        {
            var shapes=mesh.SurfaceGetBlendShapeArrays(0);targets=new Vector3[shapes.Count][];
            for(int i=0;i<targets.Length;i++)targets[i]=shapes[i][(int)Mesh.ArrayType.Vertex].AsVector3Array();
            _validationSleeveShapes[mesh.GetInstanceId()]=targets;
        }
        var result=(Vector3[])source.Clone();
        for(int shape=0;shape<targets.Length;shape++)
        {
            float weight=rig.BodySkin.GetBlendShapeValue(shape);if(weight==0)continue;
            for(int i=0;i<result.Length;i++)result[i]+=(targets[shape][i]-source[i])*weight;
        }
        return result;
    }
    private void ValidateSleeveCorrectives()
    {
        int vertices=0,normalSamples=0;float displacement=0;double oldNormalError=0,newNormalError=0;
        foreach(int identity in new[]{17,55})foreach(var job in new[]{JobType.Farmer,JobType.Gatherer,JobType.Trader})
        {
            var rig=Resident(identity,false,job);
            try
            {
                var mesh=(ArrayMesh)rig.DetailedBodyMesh;
                if(mesh.GetBlendShapeCount()!=ResidentRig.SleevePoseCount||mesh.BlendShapeMode!=Mesh.BlendShapeMode.Normalized)
                    throw new InvalidOperationException("Adult cloth lost its pose-space shapes or blend mode in LOD generation");
                var arrays=mesh.SurfaceGetArrays(0);var source=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var uv=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();var shapes=mesh.SurfaceGetBlendShapeArrays(0);
                foreach(var shape in shapes)
                {
                    var points=shape[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=shape[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    if(points.Length!=source.Length||normals.Length!=source.Length)throw new InvalidOperationException("Corrective topology differs from cloth");
                    for(int i=0;i<source.Length;i++)
                    {
                        float distance=source[i].DistanceTo(points[i]);displacement=MathF.Max(displacement,distance);
                        if(!points[i].IsFinite()||!normals[i].IsFinite()||MathF.Abs(normals[i].Length()-1)>.001f||distance>.040f)
                            throw new InvalidOperationException("Corrective has an invalid normal or exceeds its bounded surface edit");
                        if((MathF.Abs(source[i].X)<.04f||source[i].Y<.90f||uv[i].X>=.23f)&&distance>.000001f)
                            throw new InvalidOperationException("Shoulder corrective moves the fixed neck, centre, waist or cuff");
                        vertices++;
                    }
                }
                // Both arms simultaneously: normalized shapes must remain
                // additive when their combined weights exceed one. Compare
                // with the engine's own morph bake, not only our formula.
                for(int shape=0;shape<ResidentRig.SleevePoseCount;shape++)rig.BodySkin.SetBlendShapeValue(shape,shape%ResidentRig.SleevePosesPerSide==4?.6f:shape%ResidentRig.SleevePosesPerSide==10?.4f:0);
                var expected=CorrectedSleeveSource(rig,source);
                using(var baked=rig.BodySkin.BakeMeshFromCurrentBlendShapeMix())
                {
                    var actual=baked.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    for(int i=0;i<actual.Length;i++)if(actual[i].DistanceTo(expected[i])>.000003f)throw new InvalidOperationException("Engine morph mix differs from evaluated surface");
                }
                for(int shape=0;shape<ResidentRig.SleevePoseCount;shape++)rig.BodySkin.SetBlendShapeValue(shape,0);
                for(int frame=0;frame<150;frame++)rig.StudyArmElevation(1f/60,Mathf.DegToRad(100),Mathf.DegToRad(45));
                var skin=new Transform3D[ResidentRig.SupportedBodyBones];
                for(int b=0;b<skin.Length;b++)skin[b]=rig.ShoulderSkeleton.GetBonePose(b)*rig.DetailedBodySkin.GetBindPose(b);
                var boneIds=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var boneWeights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var restNormals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var local=CorrectedSleeveSource(rig,source);var posed=new Vector3[source.Length];
                for(int i=0;i<posed.Length;i++)for(int j=0;j<4;j++)posed[i]+=skin[boneIds[i*4+j]]*local[i]*boneWeights[i*4+j];
                var geometric=ClothSurfaceNormals(posed,arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
                using(var baked=rig.BodySkin.BakeMeshFromCurrentBlendShapeMix())
                {
                    var current=baked.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    for(int i=0;i<source.Length;i++)if(source[i].X>0&&uv[i].X>.04f&&uv[i].X<.18f)
                    {
                        var before=Vector3.Zero;var after=Vector3.Zero;
                        for(int j=0;j<4;j++)
                        {var basis=skin[boneIds[i*4+j]].Basis;float weight=boneWeights[i*4+j];before+=basis*restNormals[i]*weight;after+=basis*current[i]*weight;}
                        oldNormalError+=MathF.Acos(Math.Clamp(before.Normalized().Dot(geometric[i]),-1,1));
                        newNormalError+=MathF.Acos(Math.Clamp(after.Normalized().Dot(geometric[i]),-1,1));normalSamples++;
                    }
                }
                rig.SetHandDetail(false);rig.StudyArmElevation(1f/60,1.5f,.4f);rig.SetHandDetail(true);
                float total=0;
                for(int shape=ResidentRig.SleevePosesPerSide;shape<ResidentRig.SleevePoseCount;shape++)total+=rig.BodySkin.GetBlendShapeValue(shape);
                if(total<.5f)throw new InvalidOperationException("LOD recovery lost the displayed sleeve corrective weights");
                var frozen=new float[ResidentRig.SleevePoseCount];
                for(int i=0;i<frozen.Length;i++)frozen[i]=rig.BodySkin.GetBlendShapeValue(i);
                rig.PoseAction(1,ActionKind.BuildHouse,ActionPhase.Executing,true,0);
                for(int i=0;i<frozen.Length;i++)if(frozen[i]!=rig.BodySkin.GetBlendShapeValue(i))throw new InvalidOperationException("Paused corrective keeps moving");
            }
            finally{rig.Free();}
        }
        GD.Print($"SLEEVE_CORRECTIVES_PASS: adultFrames=2, cuts=3, poses={ResidentRig.SleevePoseCount}, vertices={vertices}, maximumBindEdit={displacement:F5} m; normalized engine bake, shared shoulder seam, fixed neck/centre/waist/cuff, LOD and pause");
        GD.Print($"SLEEVE_NORMAL_METRICS: samples={normalSamples}, original={oldNormalError/normalSamples:F4} rad, corrective={newNormalError/normalSamples:F4} rad; geometric reference on the actual morphed/skinned cap");
        GD.Print($"POSED_SHOULDER_SURFACE_PASS: samples={_posedShoulderSamples}, mean source-surface normal residual={_posedShoulderBefore/_posedShoulderSamples:F5} -> {_posedShoulderAfter/_posedShoulderSamples:F5} m; weighted source anatomy in raised/rotated poses");
        if(_posedShoulderSamples<1000||_posedShoulderAfter>=_posedShoulderBefore*.95)throw new InvalidOperationException("Raised shoulder fit does not improve agreement with its posed anatomical source");
        GD.Print($"PROTRUDING_CAP_PASS: samples={_protrudingCapSamples}, full source distance={_protrudingCapBefore/Math.Max(1,_protrudingCapSamples):F5} -> {_protrudingCapAfter/Math.Max(1,_protrudingCapSamples):F5} m; includes cap ridges outside the former normal-fit radius");
        if(_protrudingCapSamples==0||_protrudingCapAfter>=_protrudingCapBefore*.9)throw new InvalidOperationException("Distant cap ridges are skipped or retained by shoulder fitting");
        if(newNormalError>=oldNormalError)throw new InvalidOperationException("Pose normals do not improve agreement with the actual deformed surface");
    }
}
