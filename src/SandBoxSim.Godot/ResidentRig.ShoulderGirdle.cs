using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal static int GirdleBone(int side)=>4+4*(SleeveSpans-1)+side;
    // Presentation model, not a clinical joint reconstruction. Separate the
    // sternoclavicular arc (joint position) from scapular upward rotation and
    // posterior tilt (the platform from which the humerus rotates).
    internal Transform3D ShoulderGirdle(int side,Vector3 upper)
    {
        var rest=ShoulderRest(side);float sign=side==0?-1:1;
        if(upper.LengthSquared()<.000001f)return new(Basis.Identity,rest);
        var direction=upper.Normalized();
        float elevation=MathF.Acos(Math.Clamp(direction.Dot(Vector3.Down),-1,1));
        float raised=Mathf.SmoothStep(Mathf.DegToRad(25),Mathf.DegToRad(150),elevation);
        var horizontal=new Vector3(direction.X,0,direction.Z);
        // Clavicular elevation stays below ten degrees; retraction increases
        // overhead while low forward reaches can protract. A bent elbow by
        // itself does not drive this mechanism.
        var clavicle=ClavicleRotation(side,direction);
        var medial=new Vector3(sign*.052f,rest.Y+.008f,.008f);
        var shoulder=medial+clavicle*(rest-medial);
        var plane=horizontal.LengthSquared()>.000001f?horizontal.Normalized():new Vector3(sign,0,0);
        var axis=Vector3.Down.Cross(plane).Normalized();
        var scapula=new Basis(axis,Mathf.DegToRad(32)*raised)
            *new Basis(Vector3.Right,Mathf.DegToRad(10)*raised);
        return new(scapula,shoulder);
    }
    internal static Basis ClavicleRotation(int side,Vector3 direction)
    {
        float sign=side==0?-1:1,elevation=MathF.Acos(Math.Clamp(direction.Normalized().Dot(Vector3.Down),-1,1));
        float raised=Mathf.SmoothStep(Mathf.DegToRad(25),Mathf.DegToRad(150),elevation);
        var horizontal=new Vector3(direction.X,0,direction.Z);
        float forward=horizontal.LengthSquared()>.000001f?MathF.Max(0,-horizontal.Normalized().Z):0;
        float reach=Mathf.SmoothStep(Mathf.DegToRad(5),Mathf.DegToRad(45),elevation);
        return new Basis(Vector3.Up,sign*Mathf.DegToRad(6*forward*reach*(1-raised)-8*raised))
            *new Basis(Vector3.Back,sign*Mathf.DegToRad(8)*raised);
    }
    internal Transform3D DisplayedGirdle(int side)
    {
        var pose=ShoulderGirdle(side,-Arms[side].Basis.Y);
        // Keep the displayed IK/return shoulder, including its continuous
        // transition, as the common origin of skin, limb and equipment.
        pose.Origin=Arms[side].Position;return pose;
    }
    internal static Quaternion SleeveRotation(Quaternion girdle,Quaternion arm,float weight)
    {
        var relative=(girdle.Inverse()*arm).Normalized();if(relative.W<0)relative=-relative;
        // Swing aims the arm; twist rolls around its local long axis. Delay
        // axial twist through the shoulder cap and distribute it down the
        // upper arm, rather than twisting the deltoid/chest with the biceps.
        var twist=new Quaternion(0,relative.Y,0,relative.W);
        twist=twist.LengthSquared()<.00000001f?Quaternion.Identity:twist.Normalized();
        var swing=(relative*twist.Inverse()).Normalized();
        float roll=Mathf.SmoothStep(.20f,1,weight);
        return (girdle*Quaternion.Identity.Slerp(swing,weight)*Quaternion.Identity.Slerp(twist,roll)).Normalized();
    }
}
