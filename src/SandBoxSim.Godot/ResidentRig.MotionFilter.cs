using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private Vector3 _toolPosition,_toolVelocity,_toolAngularVelocity,_torsoVelocity,_headVelocity;
    private Quaternion _toolRotation=Quaternion.Identity;
    private bool _toolFilterReady;
    // Exact critically damped step for a stationary target over this interval.
    // Retain velocity across target changes; do not damp already solved joints.
    internal static void DampMotion(ref Vector3 value,ref Vector3 velocity,Vector3 target,float halfLife,float dt)
    {
        float rate=2*MathF.Log(2)/halfLife,decay=MathF.Exp(-rate*dt);
        var error=value-target;var c=velocity+error*rate;
        value=target+(error+c*dt)*decay;velocity=(velocity-c*(rate*dt))*decay;
    }
    private static Quaternion DampRotation(Quaternion value,ref Vector3 velocity,Quaternion target,float halfLife,float dt)
    {
        var offset=(value*target.Inverse()).Normalized();if(offset.W<0)offset=-offset;
        float angle=offset.GetAngle();var error=angle<.000001f?Vector3.Zero:offset.GetAxis()*angle;
        DampMotion(ref error,ref velocity,Vector3.Zero,halfLife,dt);
        float length=error.Length();return ((length<.000001f?Quaternion.Identity:new Quaternion(error/length,length))*target).Normalized();
    }
    private (Vector3 Position,Basis Basis) FilterHeldTool(Vector3 target,Basis basis)
    {
        if(!_toolFilterReady)
        {
            var displayed=_previousHands[1]*Grip!.Transform;
            _toolPosition=displayed.Origin;_toolRotation=displayed.Basis.GetRotationQuaternion();
            _toolVelocity=_toolAngularVelocity=Vector3.Zero;_toolFilterReady=true;
        }
        DampMotion(ref _toolPosition,ref _toolVelocity,target,.040f,_poseStep);
        var previous=_toolRotation;
        _toolRotation=DampRotation(previous,ref _toolAngularVelocity,basis.GetRotationQuaternion(),_returning?.020f:.060f,_poseStep);
        float angle=2*MathF.Acos(Math.Clamp(MathF.Abs(previous.Dot(_toolRotation)),0,1));
        float maximum=(_returning?6:4)*_poseStep;
        if(angle>maximum&&angle>0)
        {
            float ratio=maximum/angle;
            _toolRotation=previous.Slerp(_toolRotation,ratio);_toolAngularVelocity*=ratio;
        }
        return (_toolPosition,new Basis(_toolRotation));
    }
}
