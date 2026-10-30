using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateGirdleMotion()
    {
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
                                var arm=new Basis(upper,Math.Clamp(ResidentRig.SleeveNeutralRoll(upper,side)+offset,-ResidentRig.UpperArmRollLimit,ResidentRig.UpperArmRollLimit))*swing;
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
}
