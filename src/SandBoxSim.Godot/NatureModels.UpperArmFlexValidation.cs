using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public void ValidateUpperArmFlex()
    {
        int samples=0;float maxEdit=0,minArea=1,maxFluxRatio=0,maxBakeError=0,maxBakeBound=0,maxStudyRoll=0;
        foreach(int identity in new[]{17,55})foreach(var job in new[]{JobType.Farmer,JobType.Gatherer,JobType.Trader})
        {
            var rig=Resident(identity,false,job);
            try
            {
                var mesh=(ArrayMesh)rig.DetailedBodyMesh;
                if(mesh.GetBlendShapeCount()!=ResidentRig.BodyPoseCount)throw new InvalidOperationException("Upper-arm flex shapes absent from final body mesh");
                var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var uv=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
                var normals=ClothSurfaceNormals(p,ids);var area=new float[p.Length];
                for(int t=0;t<ids.Length;t+=3){int aa=ids[t],bb=ids[t+1],cc=ids[t+2];float v=(p[bb]-p[aa]).Cross(p[cc]-p[aa]).Length()/6;area[aa]+=v;area[bb]+=v;area[cc]+=v;}
                var shapes=mesh.SurfaceGetBlendShapeArrays(0);
                for(int side=0;side<2;side++)
                {
                    var q=shapes[ResidentRig.SleevePoseCount+side][(int)Mesh.ArrayType.Vertex].AsVector3Array();double flux=0,total=0;
                    for(int i=0;i<p.Length;i++)
                    {
                        var d=q[i]-p[i];if(!q[i].IsFinite())throw new InvalidOperationException("Nonfinite upper-arm shape");maxEdit=MathF.Max(maxEdit,d.Length());
                        bool active=(p[i].X<0)==(side==0)&&UpperArmFlexFreedom(uv[i])>0;
                        if(!active&&d.Length()>.000001f)throw new InvalidOperationException("Flex moves the opposite arm, torso, sleeve root or cuff");
                        double v=area[i]*normals[i].Dot(d);flux+=v;total+=Math.Abs(v);samples++;
                    }
                    maxFluxRatio=MathF.Max(maxFluxRatio,(float)(Math.Abs(flux)/Math.Max(total,1e-15)));
                    for(int t=0;t<ids.Length;t+=3)
                    {
                        int aa=ids[t],bb=ids[t+1],cc=ids[t+2];var before=(p[cc]-p[aa]).Cross(p[bb]-p[aa]);var after=(q[cc]-q[aa]).Cross(q[bb]-q[aa]);
                        if(before.LengthSquared()>1e-20f)minArea=MathF.Min(minArea,before.Dot(after)/before.LengthSquared());
                    }
                    float previous=-1;
                    for(int step=0;step<=40;step++)
                    {
                        rig.StudyArmElevation(1f/60,.8f,.5f,side,0,step*.05f);
                        float current=rig.BodySkin.GetBlendShapeValue(ResidentRig.SleevePoseCount+side);
                        if(current+1e-5f<previous||current<0||current>1)throw new InvalidOperationException("Upper-arm flex is not monotone/bounded under pure elbow bending");
                        if(previous>=0&&current-previous>.05f)throw new InvalidOperationException("Upper-arm flex snaps between adjacent elbow poses");previous=current;
                    }
                    if(previous<.99f)throw new InvalidOperationException("Bent elbow has no visible upper-arm response");
                    rig.StudyArmElevation(1f/60,.8f,.5f,side,0,.9f);
                    float neutral=rig.BodySkin.GetBlendShapeValue(ResidentRig.SleevePoseCount+side);
                    var neutralBasis=rig.Elbows[side].Basis;var neutralWrist=neutralBasis*rig.Hands[side].Position;
                    foreach(float roll in new[]{-1.2f,-.6f,.6f,1.2f})
                    {
                        rig.StudyForearmRotation(side,neutralBasis,roll);
                        if((rig.Elbows[side].Basis*rig.Hands[side].Position).DistanceTo(neutralWrist)>.000001f)throw new InvalidOperationException("Pure forearm rotation moves its wrist centre");
                        if(MathF.Abs(rig.BodySkin.GetBlendShapeValue(ResidentRig.SleevePoseCount+side)-neutral)>.0001f)throw new InvalidOperationException("Forearm rotation incorrectly drives upper-arm flex");
                    }
                    for(int tick=0;tick<120;tick++)
                    {
                        float phase=tick/120f,bend=.08f+1.75f*(.5f-.5f*MathF.Cos(MathF.Tau*phase));
                        rig.StudyArmElevation(1f/60,.55f,.45f,side,0,bend);
                        rig.StudyForearmRotation(side,rig.Elbows[side].Basis,.7f*MathF.Sin(MathF.Tau*phase));
                        float roll=rig.ForearmRoll(side);maxStudyRoll=MathF.Max(maxStudyRoll,MathF.Abs(roll));
                        if(!float.IsFinite(roll)||MathF.Abs(roll)>MathF.PI+.0001f)throw new InvalidOperationException("Absolute pose study accumulates extra forearm turns");
                    }
                    // Re-applying an absolute reference must be idempotent.
                    var reference=rig.Elbows[side].Basis;
                    rig.StudyForearmRotation(side,reference,.3f);
                    var held=new Transform3D[ResidentRig.HandBoneCount];
                    for(int b=0;b<held.Length;b++)held[b]=rig.HandSkeletons[side].GetBonePose(b);
                    for(int repeat=0;repeat<5;repeat++)
                    {
                        rig.StudyForearmRotation(side,reference,.3f);
                        for(int b=0;b<held.Length;b++)if(!held[b].IsEqualApprox(rig.HandSkeletons[side].GetBonePose(b)))throw new InvalidOperationException("Absolute forearm reference keeps winding the displayed skin");
                    }
                    rig.StudyArmElevation(1f/60,1.1f,.5f,side,.35f,1.5f);
                    var expected=CorrectedSleeveSource(rig,p);
                    using(var baked=rig.BodySkin.BakeMeshFromCurrentBlendShapeMix())
                    {
                        var actual=baked.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        if(actual.Length!=p.Length)throw new InvalidOperationException("Engine changes flex topology");
                        // A double-precision delta reference versus a float native
                        // sum of up to 122 absolute targets. Use the forward error
                        // bound gamma_n * sum(abs(terms)), capped at 20 micrometres.
                        var values=new float[shapes.Count];double totalWeight=0;
                        for(int k=0;k<values.Length;k++){values[k]=rig.BodySkin.GetBlendShapeValue(k);totalWeight+=values[k];}
                        double nu=(shapes.Count+3)*Math.Pow(2,-24),gamma=nu/(1-nu);
                        var targets=new Vector3[shapes.Count][];
                        for(int k=0;k<targets.Length;k++)if(values[k]!=0)targets[k]=shapes[k][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        for(int i=0;i<p.Length;i++)
                        {
                            double x=Math.Abs(p[i].X*(1-totalWeight)),y=Math.Abs(p[i].Y*(1-totalWeight)),z=Math.Abs(p[i].Z*(1-totalWeight));
                            for(int k=0;k<values.Length;k++)if(values[k]!=0){x+=Math.Abs(targets[k][i].X*values[k]);y+=Math.Abs(targets[k][i].Y*values[k]);z+=Math.Abs(targets[k][i].Z*values[k]);}
                            float bound=(float)Math.Min(.000020,Math.Max(.000003,gamma*Math.Sqrt(x*x+y*y+z*z)));
                            float error=actual[i].DistanceTo(expected[i]);maxBakeError=MathF.Max(maxBakeError,error);maxBakeBound=MathF.Max(maxBakeBound,bound);
                            if(!actual[i].IsFinite()||error>bound)throw new InvalidOperationException($"Engine loses combined shoulder/flex morphs: identity={identity}, job={job}, side={side}, vertex={i}, error={error}, roundingBound={bound}");
                        }
                    }
                    rig.SetHandDetail(false);rig.StudyArmElevation(1f/60,.8f,.5f,side,0,1.7f);rig.SetHandDetail(true);
                    float frozen=rig.BodySkin.GetBlendShapeValue(ResidentRig.SleevePoseCount+side);
                    if(frozen<.9f)throw new InvalidOperationException("LOD restore loses current elbow flex shape");
                    rig.PoseAction(1,ActionKind.None,ActionPhase.Idle,true,0);
                    if(frozen!=rig.BodySkin.GetBlendShapeValue(ResidentRig.SleevePoseCount+side))throw new InvalidOperationException("Pause changes upper-arm flex");
                }
            }
            finally{rig.Free();}
        }
        if(samples<1000||maxEdit<.0001f||maxEdit>.0035f||minArea<.80f||maxFluxRatio>.005f)
            throw new InvalidOperationException($"Upper-arm shape bounds fail: edit={maxEdit}, area={minArea}, first-order flux imbalance={maxFluxRatio}");
        GD.Print($"UPPER_ARM_FLEX_PASS: {samples} final mesh samples, adults/both arms/three cuts, maximum edit={maxEdit:F6} m, minimum oriented area={minArea:F3}, relative first-order flux imbalance={maxFluxRatio:F5}, max native bake error/bound={maxBakeError*1000000:F2}/{maxBakeBound*1000000:F2} micrometres, max continuous-study roll={maxStudyRoll:F3} rad; elbow-only response, twist independence, idempotent reference, engine morph bake, exact fixed roots/cuffs, pause and LOD");
    }
}
