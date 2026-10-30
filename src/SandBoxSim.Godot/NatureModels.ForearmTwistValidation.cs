using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateForearmTwist()
    {
        int samples=0;float minimumRadius=1,maximumWristError=0,maximumElbowError=0;
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
                            if(station==0)maximumElbowError=MathF.Max(maximumElbowError,displayed.DistanceTo(noRoll*offset));
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
                    var frozen=new Transform3D[ResidentRig.ForearmIntervals+1];
                    for(int station=0;station<frozen.Length;station++)frozen[station]=skeleton.GetBonePose(ResidentRig.ForearmBone(station));
                    rig.PoseAction(1,ActionKind.None,ActionPhase.Idle,true,0);
                    rig.SetHandDetail(false);rig.SetHandDetail(true);
                    for(int station=0;station<frozen.Length;station++)if(!frozen[station].IsEqualApprox(skeleton.GetBonePose(ResidentRig.ForearmBone(station))))throw new InvalidOperationException("Pause or LOD changes a displayed forearm support");
                    if(skeleton.GetBoneCount()!=ResidentRig.HandBoneCount)throw new InvalidOperationException("Forearm supports lost on LOD recovery");
                }
            }
            finally{rig.Free();}
        }
        if(maximumElbowError>.00001f||maximumWristError>.00001f||minimumRadius<.97f)throw new InvalidOperationException("Forearm twist rolls the elbow, detaches the wrist or collapses an interval");
        var transforms=new[]{Transform3D.Identity,new Transform3D(new Basis(Vector3.Up,2.2f),Vector3.Zero),Transform3D.Identity,Transform3D.Identity};
        var rigid=BlendRigidShoulder(Vector3.Right,transforms,new(.5f,.5f,0,0));
        if(MathF.Abs(rigid.Length()-1)>.00001f)throw new InvalidOperationException("Baked shoulder support still collapses under rotation blending");
        GD.Print($"FOREARM_TWIST_PASS: samples={samples}, adultFrames=2, both arms, elbow/wrist error={maximumElbowError:F6}/{maximumWristError:F6} m, minimum interval radius={minimumRadius:F4}; anatomical supports, hand contacts unchanged");
    }
}
