using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public void ValidateResidentMotion(){ValidateShoulderMotion();ValidateGarmentMotion();ValidateReachTrajectories();ValidateHandling();}
    // Check the displayed upper-arm frame as well as hand positions. A reach
    // can satisfy every endpoint test while rolling the sleeve through 180掳.
    private void ValidateShoulderMotion()
    {
        ValidateForearmTwist();
        ValidateAnatomicalArmReference();
        ValidateElbowSwivelInertia();
        ValidateSleeveDetail();
        ValidateGirdleMotion();
        ValidateMotionReference();
        ValidateSleeveCorrectives();
        ValidateShoulderFit();
        ValidateElevationSurface();
        float maxTwist=0,maxStep=0,maxElbowDeviation=0,maxForearmStep=0;int samples=0,supportSamples=0;string worst="",worstStep="",worstElbow="";
        foreach(int identity in new[]{17,42,55})
        foreach(var job in new[]{JobType.None,JobType.Farmer,JobType.Builder})
        {
            var rig=Resident(identity,false,job);
            try
            {
                var previous=new[]{rig.Arms[0].Quaternion,rig.Arms[1].Quaternion};
                var previousRoll=new[]{rig.ForearmRoll(0),rig.ForearmRoll(1)};
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
                        var localLower=rig.Elbows[side].Basis*rig.Hands[side].Position.Normalized();
                        float deviation=MathF.Asin(Math.Clamp(MathF.Abs(localLower.X),0,1));
                        if(deviation>maxElbowDeviation){maxElbowDeviation=deviation;worstElbow=$"{identity}/{job}/{action}/{frame}/{side}";}
                        if(twist>maxTwist){maxTwist=twist;worst=$"{identity}/{job}/{action}/{frame}/{side}";}
                        if(step>maxStep){maxStep=step;worstStep=$"{identity}/{job}/{action}/{frame}/{side}";}
                        previous[side]=rig.Arms[side].Quaternion;samples++;
                        float roll=rig.ForearmRoll(side);maxForearmStep=MathF.Max(maxForearmStep,MathF.Abs(roll-previousRoll[side]));previousRoll[side]=roll;
                        if(!basis.IsFinite()||MathF.Abs(basis.Determinant()-1)>.0001f)
                            throw new InvalidOperationException("Shoulder frame is not a proper rotation");
                        if(rig.HasShoulderSupports&&frame%15==0){ValidateSleeveSupport(rig,side);supportSamples++;}
                    }
                }
            }
            finally{rig.Free();}
        }
        GD.Print($"SHOULDER_MOTION_METRICS: samples={samples}, axial={maxTwist:F3} rad, frame step={maxStep:F3} rad, worst={worst}, largest step={worstStep}");
        GD.Print($"ELBOW_PLANE_METRICS: maximum off-hinge deviation={Mathf.RadToDeg(maxElbowDeviation):F2} degrees, pose={worstElbow}");
        GD.Print($"FOREARM_ROLL_CONTINUITY: maximum displayed axial step={maxForearmStep:F4} rad at 60 Hz, samples={samples}");
        if(maxForearmStep>.35f)throw new InvalidOperationException("Forearm skin flips while hand endpoints remain continuous");
        if(maxElbowDeviation>Mathf.DegToRad(.5f)||maxStep>.22f)
            throw new InvalidOperationException($"Shoulder twists or snaps despite continuous hand positions: axial={maxTwist}, step={maxStep}, pose={worstStep}");
        GD.Print($"SHOULDER_MOTION_PASS: three adult identities, bare reaches/tools/returns, {supportSamples} actual intermediate sleeve poses and radius checks");
    }
    private static void ValidateElbowSwivelInertia()
    {
        float Run(int hz)
        {
            float angle=0,speed=0,dt=1f/hz;bool reversed=false;
            for(int frame=0;frame<hz*4;frame++)
            {
                float target=frame<hz/10?1:frame<hz/2?-1:.5f;
                float previous=speed;
                float step=ResidentRig.StepElbowSwivel(target-angle,ref speed,2,dt);angle+=step;
                if(MathF.Abs(speed-previous)>12*dt+.00001f||MathF.Abs(step)>2*dt+.00001f)
                    throw new InvalidOperationException("Elbow swivel reverses abruptly or exceeds its speed/acceleration budget");
                if(frame==hz/10){reversed=true;if(speed<=0)throw new InvalidOperationException("An elbow target change discards the incoming swivel velocity");}
            }
            if(!reversed||MathF.Abs(angle-.5f)>.002f||MathF.Abs(speed)>.02f)
                throw new InvalidOperationException("Elbow swivel inertia does not settle at the requested plane");
            return angle;
        }
        float coarse=Run(60),fine=Run(120);
        if(MathF.Abs(coarse-fine)>.002f)throw new InvalidOperationException("Elbow swivel settling depends on frame rate");
        GD.Print("ELBOW_INERTIA_PASS: target reversal retains incoming speed, bounded acceleration, pause and 60/120 Hz settling");
        float held=1;if(ResidentRig.StepElbowSwivel(1,ref held,2,0)!=0||held!=1)throw new InvalidOperationException("Paused elbow swivel changes state");
    }
    private void ValidateAnatomicalArmReference()
    {
        int samples=0;float maximumArmChange=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                foreach(int side in new[]{0,1})
                {
                    for(int step=0;step<180;step++)rig.StudyArmElevation(1f/60,0,Mathf.DegToRad(30),side);
                    var lower=(rig.Arms[side].Basis*rig.Elbows[side].Basis*rig.Hands[side].Position).Normalized();
                    var upper=-rig.Arms[side].Basis.Y;
                    var swing=new Basis(new Quaternion(Vector3.Down,upper));
                    float axial=MathF.Abs(swing.X.SignedAngleTo(rig.Arms[side].Basis.X,upper));
                    // The live coupled solver retains a small settling error;
                    // distinguish neutral axial twist from elevation itself.
                    if(axial>Mathf.DegToRad(2)||upper.AngleTo(Vector3.Down)>Mathf.DegToRad(1)||lower.Z>-.25f)
                        throw new InvalidOperationException($"Hanging arm begins axially twisted or flexes the forearm sideways/backwards: {identity}/{side}, axial={axial}, upper={upper}, lower={lower}");
                    for(int step=0;step<180;step++)rig.StudyArmElevation(1f/60,Mathf.DegToRad(70),Mathf.DegToRad(30),side,0,1);
                    var fixedArm=rig.Arms[side].Quaternion;float firstRoll=rig.ForearmRoll(side),rollTravel=0;
                    for(int step=0;step<=120;step++)
                    {
                        float pronation=.9f*MathF.Sin(MathF.Tau*step/120);
                        rig.StudyArmElevation(1f/60,Mathf.DegToRad(70),Mathf.DegToRad(30),side,pronation,1);
                        float change=rig.Arms[side].Quaternion.AngleTo(fixedArm);maximumArmChange=MathF.Max(maximumArmChange,change);
                        rollTravel=MathF.Max(rollTravel,MathF.Abs(rig.ForearmRoll(side)-firstRoll));samples++;
                        if(change>.005f)throw new InvalidOperationException("Forearm pronation rotates the upper arm reference and shoulder skin");
                    }
                    if(rollTravel<.5f)throw new InvalidOperationException("Pronation test has no displayed forearm rotation");
                }
                var eating=Resident(identity,false,JobType.None);
                try
                {
                    for(int step=0;step<240;step++)
                    {
                        eating.PoseAction(1f/60,ActionKind.Eat,ActionPhase.Executing,false,.3f);
                        var wrist=InTorso(eating.Hands[1],eating.Torso).Origin;
                        if(step>60&&wrist.Y<eating.Arms[1].Position.Y+.02f&&wrist.Z<-.12f
                            &&(-eating.Arms[1].Basis.Y).AngleTo(Vector3.Down)>Mathf.DegToRad(65))
                            throw new InvalidOperationException("Low front eating reach flares the upper arm instead of supporting the elbow below the shoulder");
                    }
                }
                finally{eating.Free();}
            }
            finally{rig.Free();}
        }
        GD.Print($"ANATOMICAL_ARM_REFERENCE_PASS: {samples} adult/left/right pronation samples, neutral hanging flexion forward, maximum humeral change={maximumArmChange:F6} rad");
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
        if(!rig.ShoulderSkeleton.GetBonePose(ResidentRig.GirdleBone(side)).IsEqualApprox(rig.DisplayedGirdle(side)))
            throw new InvalidOperationException("Rendered shoulder platform does not track the displayed arm");
        for(int family=0;family<2;family++)for(int span=1;span<ResidentRig.SleeveSpans;span++)
        {
            int bone=ResidentRig.SleeveSupportBone(side,span,family==1);float fraction=(float)span/ResidentRig.SleeveSpans;var pose=rig.ShoulderSkeleton.GetBonePose(bone);var arm=rig.Arms[side].Transform;
            var girdle=rig.DisplayedGirdle(side).Basis.GetRotationQuaternion();
            var rotation=ResidentRig.SleeveRotation(girdle,arm.Basis.GetRotationQuaternion(),fraction);
            var centre=arm.Origin+(family==1?ResidentRig.SleeveCapCorrection(arm.Basis.GetRotationQuaternion(),fraction,girdle):Vector3.Zero);
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
