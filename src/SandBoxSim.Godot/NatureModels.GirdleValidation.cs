using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateGirdleMotion()
    {
        ValidateSleeveSwingTwist();
        int samples=0;float maximumArc=0,maximumStep=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                foreach(float plane in new[]{0f,30f,90f})
                {
                    var prior=new[]{rig.ShoulderRest(0),rig.ShoulderRest(1)};
                    for(int degrees=0;degrees<=135;degrees++)
                    {
                        Transform3D[] pair=new Transform3D[2];
                        for(int side=0;side<2;side++)
                        {
                            var outward=new Vector3((side==0?-1:1)*MathF.Cos(Mathf.DegToRad(plane)),0,-MathF.Sin(Mathf.DegToRad(plane)));
                            var axis=Vector3.Down.Cross(outward);
                            var upper=Vector3.Down.Rotated(axis,Mathf.DegToRad(degrees));
                            var pose=rig.ShoulderGirdle(side,upper);pair[side]=pose;
                            float arc=pose.Origin.DistanceTo(rig.ShoulderRest(side));maximumArc=MathF.Max(maximumArc,arc);
                            maximumStep=MathF.Max(maximumStep,pose.Origin.DistanceTo(prior[side]));prior[side]=pose.Origin;
                            if(!pose.IsFinite()||MathF.Abs(pose.Basis.Determinant()-1)>.00001f||arc>.04f)
                                throw new InvalidOperationException("Shoulder platform scales, is non-finite or leaves its bounded clavicle arc");
                            if(degrees==0&&!pose.IsEqualApprox(new Transform3D(Basis.Identity,rig.ShoulderRest(side))))
                                throw new InvalidOperationException("Resting arm retains an elevated shoulder platform");
                            if(degrees==135&&(pose.Basis.GetRotationQuaternion().GetAngle()<Mathf.DegToRad(20)||pose.Origin.Y<=rig.ShoulderRest(side).Y+.010f))
                                throw new InvalidOperationException("Overhead arm retains a fixed scapular platform");
                            var swing=new Basis(new Quaternion(Vector3.Down,upper));
                            foreach(float offset in new[]{-.55f,0f,.55f})
                            {
                                float total=0;
                                var arm=new Basis(upper,Math.Clamp(ResidentRig.SleeveNeutralRoll(upper,side)+offset,-ResidentRig.SleeveRollSampleLimit,ResidentRig.SleeveRollSampleLimit))*swing;
                                for(int p=0;p<3;p++)for(int h=0;h<2;h++)for(int roll=0;roll<3;roll++)
                                {
                                    float weight=ResidentRig.SleevePoseWeight(arm,side,p,h,roll);
                                    if(!float.IsFinite(weight)||weight<0||weight>1)throw new InvalidOperationException("Invalid orientation-driven shoulder shape weight");
                                    total+=weight;
                                }
                                if(total>1.0001f||(degrees>=90&&total<.99f))throw new InvalidOperationException("Shoulder corrective partition loses or duplicates volume");
                            }
                            samples++;
                        }
                        var mirrored=new Vector3(-pair[0].Origin.X,pair[0].Origin.Y,pair[0].Origin.Z);
                        if(mirrored.DistanceTo(pair[1].Origin)>.000001f)throw new InvalidOperationException("Left and right shoulder arcs are not mirrored");
                    }
                }
            }
            finally{rig.Free();}
        }
        // Each one-degree step is a continuous path, rather than independent
        // extreme-pose tests. Catch a protraction jump near the vertical arm.
        if(maximumStep>.001f)throw new InvalidOperationException("Shoulder platform jumps between neighbouring elevation samples");
        GD.Print($"SHOULDER_GIRDLE_PASS: samples={samples}, mirrored adult clavicle arcs, rotating scapular platforms, maximum endpoint travel={maximumArc:F4} m, largest one-degree step={maximumStep:F5} m, normalized orientation-driven corrections");
    }
    private static void ValidateSleeveSwingTwist()
    {
        int samples=0;float minimumRadius=1;
        foreach(float turn in new[]{-1.65f,-.75f,0f,.75f,1.65f})
        foreach(float lift in new[]{0f,.7f,1.7f})
        {
            var platform=new Quaternion(Vector3.Right,.22f);
            var swing=new Quaternion(Vector3.Back,lift);
            var arm=platform*swing*new Quaternion(Vector3.Down,turn);
            for(int i=0;i<=ResidentRig.SleeveSpans;i++)
            {
                float fraction=(float)i/ResidentRig.SleeveSpans;
                var pose=ResidentRig.SleeveRotation(platform,arm,fraction);
                var signed=ResidentRig.SleeveRotation(-platform,-arm,fraction);
                var expected=platform*new Quaternion(Vector3.Back,lift*fraction)*Vector3.Down;
                if((pose*Vector3.Down).DistanceTo(expected)>.00001f||MathF.Abs(pose.Dot(signed))<.99999f)
                    throw new InvalidOperationException("Axial sleeve twist changes its swing direction or depends on quaternion sign");
                if(i==0&&MathF.Abs(pose.Dot(platform))<.99999f||i==ResidentRig.SleeveSpans&&MathF.Abs(pose.Dot(arm))<.99999f)
                    throw new InvalidOperationException("Swing/twist sleeve detaches its shoulder root or cuff");
                if(lift==0&&i==1&&(pose*Vector3.Right).DistanceTo(platform*Vector3.Right)>.00001f)
                    throw new InvalidOperationException("Pure upper-arm twist turns the shoulder cap instead of the biceps");
                if(i>0)
                {
                    var prior=ResidentRig.SleeveRotation(platform,arm,(float)(i-1)/ResidentRig.SleeveSpans);
                    foreach(var axis in new[]{Vector3.Right,Vector3.Up,Vector3.Back})
                    {
                        float radius=((pose*axis+prior*axis)*.5f).Length();minimumRadius=MathF.Min(minimumRadius,radius);
                        if(radius<.94f)throw new InvalidOperationException("Swing/twist support intervals lose too much volume");
                    }
                }
                samples++;
            }
        }
        GD.Print($"SLEEVE_SWING_TWIST_PASS: samples={samples}, fixed cap under pure axial roll, exact endpoints, independent swing direction, quaternion sign invariance, minimum interval radius={minimumRadius:F4}");
    }
}
