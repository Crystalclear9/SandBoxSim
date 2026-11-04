using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static void ValidateSkinMicroMotion(ResidentRig rig)
    {
        float maxRootError=0,maxRootStep=0,maxBendError=0;int samples=0;
        for(int side=0;side<2;side++)for(int mode=0;mode<2;mode++)
        {
            rig.ResetForearmStudy(side);var offset=Vector3.Back*.020f;var probe=-rig.Hands[side].Position+offset;
            Vector3 previous=offset;
            for(int tick=0;tick<=240;tick++)
            {
                float angle=tick*.000020f;
                if(mode==0)rig.StudyWristRotation(side,new Basis(Vector3.Right,angle));
                else rig.StudyUpperArmReference(side,angle,0);
                var bone=rig.HandSkeletons[side].GetBonePose(ResidentRig.ForearmBone(0));
                var root=rig.Elbows[side].Basis*(rig.Hands[side].Position+rig.Hands[side].Basis*(bone*probe));
                maxRootError=MathF.Max(maxRootError,root.DistanceTo(offset));
                if(tick>0)maxRootStep=MathF.Max(maxRootStep,root.DistanceTo(previous));previous=root;
                if(mode==1)
                {
                    float displayed=0;
                    for(int b=0;b<ResidentRig.ForearmNormalBends;b++)for(int r=0;r<ResidentRig.ForearmNormalRolls;r++)
                        displayed+=rig.HandSkins[side].GetBlendShapeValue(b*ResidentRig.ForearmNormalRolls+r)*b*ResidentRig.ForearmBendStep;
                    maxBendError=MathF.Max(maxBendError,MathF.Abs(displayed-angle));
                }
                samples++;
            }
        }
        GD.Print($"SKIN_MICRO_MOTION_METRICS: samples={samples}, wrist/elbow/both arms, maximum root error/step={maxRootError:F8}/{maxRootStep:F8} m, pose-angle error={maxBendError:F8} rad");
        if(maxRootError>.000003f||maxRootStep>.000003f||maxBendError>.000002f)
            throw new InvalidOperationException("Slow wrist/elbow skin motion is quantized or lags its actual joints");
    }
}
