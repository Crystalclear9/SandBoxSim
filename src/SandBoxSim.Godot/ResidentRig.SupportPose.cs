using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private Vector3 _supportAngle,_supportVelocity;
    internal float HipHalfWidth=.10f;
    // A sagittal planted stance. Preserve the authored thigh/shin lengths and
    // the ankle's forward offset, rather than translating the knees or feet.
    private (Vector3 Hip,float Thigh,float Knee) WorkSupport(float delta,bool active,float weight)
    {
        // Integrate only this cheap support filter at close-up cadence when a
        // distant actor arrives with accumulated time. Do not repeat full IK.
        int steps=active?Math.Max(1,(int)MathF.Ceiling(delta*60)):1;float step=delta/steps;
        for(int i=0;i<steps;i++)
        {
            float phase=Mathf.PosMod(_strokePhase-(_draw>=.99f?(delta-(i+1)*step)/WorkPeriod:0),1);
            float target=active?Math.Clamp((.075f+.10f*(1-WorkLift(phase)))*RelativeWorkLoad,.06f,.24f):0;
            DampMotion(ref _supportAngle,ref _supportVelocity,new(target,0,0),.085f,step);
        }
        float thigh=Math.Clamp(_supportAngle.X,0,.26f)*weight;
        float hipZ=-.035f*thigh;
        float lower=MathF.Sqrt(ShinLength*ShinLength+.04f*.04f),ankleAngle=MathF.Atan2(.04f,ShinLength);
        float shinAngle=MathF.Asin(Math.Clamp((.04f+hipZ-ThighLength*MathF.Sin(thigh))/lower,-1,1));
        float hipY=HipHeight-ThighLength-ShinLength+ThighLength*MathF.Cos(thigh)+lower*MathF.Cos(shinAngle);
        return (new(0,hipY-HipHeight,hipZ),thigh,shinAngle-ankleAngle-thigh);
    }
}
