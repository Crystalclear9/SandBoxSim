using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateForearmTwist()
    {
        int samples=0;float minimumRadius=1,minimumWristRadius=1,maximumWristError=0,maximumElbowError=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                for(int side=0;side<2;side++)for(int turn=-16;turn<=16;turn++)
                {
                    rig.StudyArmElevation(1f/60,1.1f,.5f,side,turn*.10f,1.1f);
                    var skeleton=rig.HandSkeletons[side];var wrist=rig.Hands[side].Basis;
                    var axis=rig.Hands[side].Position.Normalized();var elbow=-rig.Hands[side].Position;
                    var upper=rig.Elbows[side].Basis;float roll=rig.ForearmRoll(side);
                    var noRoll=upper*new Basis(axis,-roll);
                    for(int station=0;station<=ResidentRig.ForearmIntervals;station++)
                    {
                        var pose=skeleton.GetBonePose(ResidentRig.ForearmBone(station));
                        foreach(var offset in new[]{Vector3.Right*.03f,Vector3.Back*.03f})
                        {
                            var p=elbow+axis*(.21f*station/ResidentRig.ForearmIntervals)+offset;
                            var displayed=upper*(rig.Hands[side].Position+wrist*(pose*p));
                            if(station==0)maximumElbowError=MathF.Max(maximumElbowError,displayed.DistanceTo(offset));
                            if(station==ResidentRig.ForearmIntervals)maximumWristError=MathF.Max(maximumWristError,displayed.DistanceTo(upper*(p+rig.Hands[side].Position)));
                            if(station>0)
                            {
                                var prior=skeleton.GetBonePose(ResidentRig.ForearmBone(station-1));
                                var a=wrist*(pose.Basis*offset);var b=wrist*(prior.Basis*offset);
                                minimumRadius=MathF.Min(minimumRadius,((a+b)*.5f).Length()/offset.Length());
                            }
                            samples++;
                        }
                    }
                    for(int station=0;station<=ResidentRig.WristIntervals;station++)
                    {
                        var pose=skeleton.GetBonePose(ResidentRig.WristBone(station));
                        var expected=rig.WristSectionPose(side,(float)station/ResidentRig.WristIntervals);
                        // Skeleton3D decomposes and reconstitutes the basis;
                        // use a metre/axis tolerance, not per-component relative
                        // equality on near-zero quaternion reconstruction terms.
                        if(pose.Origin.DistanceTo(expected.Origin)>.00001f||pose.Basis.X.DistanceTo(expected.Basis.X)>.00001f||pose.Basis.Y.DistanceTo(expected.Basis.Y)>.00001f||pose.Basis.Z.DistanceTo(expected.Basis.Z)>.00001f)
                            throw new InvalidOperationException($"Displayed wrist station misses its continuous rotation: identity={identity}, side={side}, turn={turn}, station={station}, actual={pose}, expected={rig.WristSectionPose(side,(float)station/ResidentRig.WristIntervals)}");
                        if(station==0)continue;
                        var prior=skeleton.GetBonePose(ResidentRig.WristBone(station-1));
                        foreach(var radial in new[]{Vector3.Right,Vector3.Back})
                            minimumWristRadius=MathF.Min(minimumWristRadius,((pose.Basis*radial+prior.Basis*radial)*.5f).Length());
                    }
                    var supports=new int[ResidentRig.ForearmIntervals+ResidentRig.WristIntervals];
                    for(int i=0;i<=ResidentRig.ForearmIntervals;i++)supports[i]=ResidentRig.ForearmBone(i);
                    for(int i=1;i<ResidentRig.WristIntervals;i++)supports[ResidentRig.ForearmIntervals+i]=ResidentRig.WristBone(i);
                    var frozen=new Transform3D[supports.Length];
                    for(int i=0;i<frozen.Length;i++)frozen[i]=skeleton.GetBonePose(supports[i]);
                    rig.PoseAction(1,ActionKind.None,ActionPhase.Idle,true,0);
                    rig.SetHandDetail(false);rig.SetHandDetail(true);
                    for(int i=0;i<frozen.Length;i++)if(!frozen[i].IsEqualApprox(skeleton.GetBonePose(supports[i])))throw new InvalidOperationException("Pause or LOD changes a displayed wrist/forearm support");
                    if(skeleton.GetBoneCount()!=ResidentRig.HandBoneCount)throw new InvalidOperationException("Forearm supports lost on LOD recovery");
                }
            }
            finally{rig.Free();}
        }
        if(maximumElbowError>.00001f||maximumWristError>.00001f||minimumRadius<.97f)throw new InvalidOperationException("Forearm twist rolls the elbow, detaches the wrist or collapses an interval");
        if(minimumWristRadius<.985f)throw new InvalidOperationException("Wrist flexion collapses a displayed cross section");
        var transforms=new[]{Transform3D.Identity,new Transform3D(new Basis(Vector3.Up,2.2f),Vector3.Zero),Transform3D.Identity,Transform3D.Identity};
        var rigid=BlendRigidShoulder(Vector3.Right,transforms,new(.5f,.5f,0,0));
        if(MathF.Abs(rigid.Length()-1)>.00001f)throw new InvalidOperationException("Baked shoulder support still collapses under rotation blending");
        GD.Print($"FOREARM_TWIST_PASS: samples={samples}, adultFrames=2, both arms, elbow/wrist error={maximumElbowError:F6}/{maximumWristError:F6} m, minimum forearm/wrist interval radius={minimumRadius:F4}/{minimumWristRadius:F4}; hand contacts, pause and LOD retained");
        ValidateWristMotion();
    }
    public void ValidateHandContinuity()
    {
        ValidateForearmSurfaces();ValidateForearmTwist();ValidateElbowSkin();ValidateForearmNormals();ValidateUpperArmFlex();
        for(int frame=0;frame<2;frame++)
        {
            var rig=Resident(frame==0?17:55,false,JobType.None);
            try{ValidateForearmSupport(rig,frame);}finally{rig.Free();}
        }
        ValidateHandling();
    }
    private void ValidateElbowSkin()
    {
        int samples=0;float maxRoot=0,maxShaft=0,minimumVolume=1;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                for(int side=0;side<2;side++)
                {
                    var arrays=rig.HandSkins[side].Mesh.SurfaceGetArrays(0);
                    var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    foreach(float bend in new[]{0f,.3f,.8f,1.3f,1.8f,.5f})
                    {
                        rig.StudyArmElevation(1f/60,.7f,.5f,side,0,bend);
                        foreach(float height in new[]{.06f,.12f,.18f,.21f})
                        {
                            var axis=rig.Hands[side].Position.Normalized();var pivot=-rig.Hands[side].Position;
                            var centre=pivot+axis*((pivot.Y-height)/(-axis.Y));
                            float station=ResidentRig.ForearmSkinFraction(height)*ResidentRig.ForearmIntervals;
                            int low=Math.Min((int)station,ResidentRig.ForearmIntervals-1);float t=station-low;
                            var a=rig.HandSkeletons[side].GetBonePose(ResidentRig.ForearmBone(low))*centre;
                            var b=rig.HandSkeletons[side].GetBonePose(ResidentRig.ForearmBone(low+1))*centre;
                            var actual=rig.Elbows[side].Basis*(rig.Hands[side].Position+rig.Hands[side].Basis*a.Lerp(b,t));
                            var expected=rig.Elbows[side].Basis*(centre+rig.Hands[side].Position);
                            maxShaft=MathF.Max(maxShaft,actual.DistanceTo(expected));
                        }
                        for(int i=0;i<points.Length;i++)if(points[i].Y>.0451f)
                        {
                            Vector3 q=Vector3.Zero,x=q,y=q,z=q;
                            for(int j=0;j<4;j++)
                            {
                                var pose=rig.HandSkeletons[side].GetBonePose(bones[i*4+j]);float w=weights[i*4+j];
                                q+=(pose*points[i])*w;x+=pose.Basis.X*w;y+=pose.Basis.Y*w;z+=pose.Basis.Z*w;
                            }
                            minimumVolume=MathF.Min(minimumVolume,new Basis(x,y,z).Determinant());
                            if(points[i].Y>.260f)
                            {
                                var support=ForearmSupport(identity==55?1:0);
                                if(MathF.Abs(points[i].X)>support.Width[^1]*.65f||MathF.Abs(points[i].Z)>support.Depth[^1]*.80f)
                                    throw new InvalidOperationException("Proximal forearm returns to the oversized generic sculpt");
                            }
                            if(points[i].Y>=.273f)
                            {
                                var actual=rig.Elbows[side].Basis*(rig.Hands[side].Position+rig.Hands[side].Basis*q);
                                maxRoot=MathF.Max(maxRoot,actual.DistanceTo(points[i]+rig.Hands[side].Position));samples++;
                            }
                        }
                    }
                }
            }
            finally{rig.Free();}
        }
        if(samples<100||maxRoot>.0001f||maxShaft>.0003f||minimumVolume<.97f)
            throw new InvalidOperationException($"Elbow bridge loses upper-arm attachment or muscle volume: samples={samples}, root={maxRoot}, shaft={maxShaft}, determinant={minimumVolume}");
        GD.Print($"ELBOW_SKIN_PASS: {samples} actual proximal skin samples, both adults/arms, flexion-only reversals; upper-arm root/shaft error={maxRoot:F6}/{maxShaft:F6} m, minimum blended determinant={minimumVolume:F4}");
    }
    private void ValidateWristMotion()
    {
        float minimumVolume=1,maxStep=0,maxSeam=0;int samples=0;
        for(int frame=0;frame<2;frame++)
        {
            var rig=Resident(frame==0?17:55,false,JobType.None);
            try
            {
                rig.SetHandDetail(true);
                var a=rig.HandSkins[1].Mesh.SurfaceGetArrays(0);
                var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var b=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var w=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var prior=new Vector3[p.Length];
                var palette=new Transform3D[ResidentRig.HandBoneCount];
                var global=new Transform3D[palette.Length];
                for(int tick=0;tick<480;tick++)
                {
                    rig.StudyWrist(1f/60,tick/60f);
                    // These substeps occur inside one engine frame. Recompose
                    // from current local poses and actual inverse binds instead
                    // of reading the renderer's deferred global-pose cache.
                    var skeleton=rig.HandSkeletons[1];
                    for(int bone=0;bone<palette.Length;bone++)
                    {
                        var local=skeleton.GetBoneRest(bone)*skeleton.GetBonePose(bone);int parent=skeleton.GetBoneParent(bone);
                        global[bone]=parent<0?local:global[parent]*local;
                        palette[bone]=global[bone]*rig.HandSkins[1].Skin.GetBindPose(bone);
                    }
                    for(int i=0;i<p.Length;i++)if(p[i].Y>.005f&&p[i].Y<=.075f)
                    {
                        Vector3 q=Vector3.Zero,x=Vector3.Zero,y=x,z=x;
                        for(int j=0;j<4;j++)
                        {
                            var pose=palette[b[i*4+j]];float weight=w[i*4+j];
                            q+=(pose*p[i])*weight;x+=pose.Basis.X*weight;y+=pose.Basis.Y*weight;z+=pose.Basis.Z*weight;
                        }
                        if(!q.IsFinite())throw new InvalidOperationException("Wrist motion has a nonfinite displayed surface");
                        minimumVolume=MathF.Min(minimumVolume,new Basis(x,y,z).Determinant());
                        var displayed=rig.Hands[1].Basis*q;
                        if(tick>0)maxStep=MathF.Max(maxStep,displayed.DistanceTo(prior[i]));prior[i]=displayed;
                        if(MathF.Abs(p[i].Y-.045f)<.000001f)maxSeam=MathF.Max(maxSeam,displayed.DistanceTo(p[i]));
                        samples++;
                    }
                }
            }
            finally{rig.Free();}
        }
        if(minimumVolume<.990f||maxStep>.006f||maxSeam>.0001f)
            throw new InvalidOperationException($"Displayed wrist loses volume/continuity: determinant={minimumVolume}, step={maxStep}, seam={maxSeam}");
        GD.Print($"WRIST_MOTION_PASS: {samples} actual two-frame skin samples over flexion/deviation/pronation/grip; minimum blended determinant={minimumVolume:F4}, max step/seam={maxStep:F5}/{maxSeam:F6} m");
    }
}
