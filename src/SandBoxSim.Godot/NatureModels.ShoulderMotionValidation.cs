using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public void ValidateResidentMotion(){ValidateShoulderMotion();ValidateGarmentMotion();}
    // Check the displayed upper-arm frame as well as hand positions. A reach
    // can satisfy every endpoint test while rolling the sleeve through 180掳.
    private void ValidateShoulderMotion()
    {
        ValidateSleeveCorrectives();
        ValidateShoulderFit();
        ValidateElevationSurface();
        float maxTwist=0,maxStep=0;int samples=0,supportSamples=0;string worst="",worstStep="";
        foreach(int identity in new[]{17,42,55})
        foreach(var job in new[]{JobType.None,JobType.Farmer,JobType.Builder})
        {
            var rig=Resident(identity,false,job);
            try
            {
                var previous=new[]{rig.Arms[0].Quaternion,rig.Arms[1].Quaternion};
                foreach(var action in new[]{ActionKind.None,ActionKind.GatherWood,ActionKind.Farm,ActionKind.BuildHouse,ActionKind.Socialize,ActionKind.Eat,ActionKind.Take,ActionKind.None})
                for(int frame=0;frame<180;frame++)
                {
                    rig.PoseAction(1f/60,action,action==ActionKind.None?ActionPhase.Idle:ActionPhase.Executing,false,.37f);
                    for(int side=0;side<2;side++)
                    {
                        var basis=rig.Arms[side].Basis;
                        var direction=-basis.Y;
                        var swing=new Basis(new Quaternion(Vector3.Down,direction));
                        float twist=MathF.Abs(swing.X.SignedAngleTo(basis.X,direction));
                        float step=2*MathF.Acos(Math.Clamp(MathF.Abs(previous[side].Dot(rig.Arms[side].Quaternion)),0,1));
                        if(twist>maxTwist){maxTwist=twist;worst=$"{identity}/{job}/{action}/{frame}/{side}";}
                        if(step>maxStep){maxStep=step;worstStep=$"{identity}/{job}/{action}/{frame}/{side}";}
                        previous[side]=rig.Arms[side].Quaternion;samples++;
                        if(!basis.IsFinite()||MathF.Abs(basis.Determinant()-1)>.0001f)
                            throw new InvalidOperationException("Shoulder frame is not a proper rotation");
                        if(rig.HasShoulderSupports&&frame%15==0){ValidateSleeveSupport(rig,side);supportSamples++;}
                    }
                }
            }
            finally{rig.Free();}
        }
        GD.Print($"SHOULDER_MOTION_METRICS: samples={samples}, axial={maxTwist:F3} rad, frame step={maxStep:F3} rad, worst={worst}, largest step={worstStep}");
        if(maxTwist>ResidentRig.UpperArmRollLimit+.02f||maxStep>.22f)
            throw new InvalidOperationException($"Shoulder twists or snaps despite continuous hand positions: axial={maxTwist}, step={maxStep}, pose={worstStep}");
        GD.Print($"SHOULDER_MOTION_PASS: three adult identities, bare reaches/tools/returns, {supportSamples} actual intermediate sleeve poses and radius checks");
    }
    private void ValidateShoulderFit()
    {
        float maximum=0;
        foreach(int identity in new[]{17,42,55})
        {
            var neutral=Resident(identity,false,JobType.None);
            var carrying=Resident(identity,false,JobType.Builder);
            try
            {
                if(identity!=17&&MathF.Abs(neutral.ShoulderHalfWidth-FemaleShoulderWidth)>.00001f)
                    throw new InvalidOperationException($"Female shoulder uses a generic width instead of its source joint: actual={neutral.ShoulderHalfWidth}, source={FemaleShoulderWidth}");
                for(int frame=0;frame<240;frame++)
                {
                    neutral.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                    carrying.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                }
                var free=InTorso(neutral.Hands[1],neutral.Torso).Origin;
                var held=InTorso(carrying.Hands[1],carrying.Torso).Origin;
                float difference=MathF.Abs(held.X-free.X);if(identity!=17)maximum=MathF.Max(maximum,difference);
                if(!carrying.ToolHeld||(identity!=17&&difference>.025f))
                    throw new InvalidOperationException($"Narrow shoulder reaches outward to the common carry target: {identity}, free={free}, held={held}");
                if(!neutral.HasShoulderSupports||neutral.ShoulderSkeleton.GetBoneCount()!=ResidentRig.SupportedBodyBones||neutral.DetailedBodySkin.GetBindCount()!=ResidentRig.SupportedBodyBones)
                    throw new InvalidOperationException("Fitted sleeve lacks intermediate supports");
                var arrays=neutral.DetailedBodyMesh.SurfaceGetArrays(0);var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();int supported=0;
                for(int i=0;i<bones.Length;i++)if(bones[i]>=4&&weights[i]>.01f)supported++;
                if(supported<100)throw new InvalidOperationException("Intermediate sleeve bones have no fitted surface influence");
                for(int side=0;side<2;side++)
                    if(!(neutral.ShoulderSkeleton.GetBoneRest(side+1)*neutral.DetailedBodySkin.GetBindPose(side+1)).IsEqualApprox(Transform3D.Identity))
                        throw new InvalidOperationException("Fitted shoulder centre and inverse bind differ");
                carrying.SetHandDetail(false);
                for(int frame=0;frame<60;frame++)carrying.PoseAction(1f/60,ActionKind.BuildHouse,ActionPhase.Executing,false,0);
                carrying.SetHandDetail(true);
                for(int side=0;side<2;side++)ValidateSleeveSupport(carrying,side);
                var frozen=new Transform3D[ResidentRig.SupportedBodyBones];
                for(int bone=0;bone<frozen.Length;bone++)frozen[bone]=carrying.ShoulderSkeleton.GetBonePose(bone);
                carrying.PoseAction(1,ActionKind.BuildHouse,ActionPhase.Executing,true,0);
                for(int bone=0;bone<frozen.Length;bone++)if(frozen[bone]!=carrying.ShoulderSkeleton.GetBonePose(bone))throw new InvalidOperationException("Paused sleeve support keeps moving");
            }
            finally{neutral.Free();carrying.Free();}
        }
        GD.Print($"SHOULDER_FIT_PASS: three adult identities, source half-width={FemaleShoulderWidth:F5} m, neutral/carry wrist lateral difference={maximum:F4} m, fitted inverse binds");
    }
    private static void ValidateSleeveSupport(ResidentRig rig,int side)
    {
        for(int family=0;family<2;family++)for(int span=1;span<ResidentRig.SleeveSpans;span++)
        {
            int bone=ResidentRig.SleeveSupportBone(side,span,family==1);float fraction=(float)span/ResidentRig.SleeveSpans;var pose=rig.ShoulderSkeleton.GetBonePose(bone);var arm=rig.Arms[side].Transform;
            var rotation=Quaternion.Identity.Slerp(arm.Basis.GetRotationQuaternion(),fraction);
            var centre=rig.ShoulderRest(side).Lerp(arm.Origin,fraction)+(family==1?ResidentRig.SleeveCapCorrection(arm.Basis.GetRotationQuaternion(),fraction):Vector3.Zero);
            if(pose.Origin.DistanceTo(centre)>.00001f||MathF.Abs(rotation.Dot(pose.Basis.GetRotationQuaternion()))<.99999f)
                throw new InvalidOperationException("Intermediate sleeve pose does not track the displayed arm");
            var transformed=pose*rig.DetailedBodySkin.GetBindPose(bone);
            // Probe a ring about the real fitted pivot, using the actual skin
            // transform. Each fractional-rotation support must preserve the
            // radius around its fitted pivot, including after LOD recovery.
            foreach(var axis in new[]{Vector3.Right,Vector3.Back})
            {
                var point=transformed*(rig.ShoulderRest(side)+axis*.06f);
                if(MathF.Abs(point.DistanceTo(centre)-.06f)>.00002f)
                    throw new InvalidOperationException("Intermediate sleeve support scales the shoulder cross-section");
            }
        }
    }
}
