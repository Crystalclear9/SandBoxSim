using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private Vector3 _supportAngle,_supportVelocity;
    internal const float NeutralHipHalfWidth=.085f;
    internal float HipHalfWidth=NeutralHipHalfWidth,ArmRestClearance;
    private Vector3 WorkSupport(float delta,bool active,float weight)
    {
        // Integrate only this cheap support filter at close-up cadence when a
        // distant actor arrives with accumulated time. Do not repeat full IK.
        int steps=active?Math.Max(1,(int)MathF.Ceiling(delta*60)):1;float step=delta/steps;
        for(int i=0;i<steps;i++)
        {
            float phase=Mathf.PosMod(_strokePhase-(_draw>=.99f?(delta-(i+1)*step)/WorkPeriod:0),1);
            float flex=.073f+.030f*(1-Flow)+(.078f+.062f*(1-Flow))*(1-WorkLift(phase));
            float target=active?Math.Clamp(flex*RelativeWorkLoad,.06f,.24f):0;
            var shift=active?WorkWeightShift(phase):Vector2.Zero;
            DampMotion(ref _supportAngle,ref _supportVelocity,new(target,shift.X,shift.Y),.085f,step);
        }
        float thigh=Math.Clamp(_supportAngle.X,0,.26f)*weight;
        float hipX=_supportAngle.Y*weight;
        float hipZ=-.035f*thigh+_supportAngle.Z*weight;
        float lower=ShinLength;
        float shinAngle=MathF.Asin(Math.Clamp((hipZ-ThighLength*MathF.Sin(thigh))/lower,-1,1));
        float reach=ThighLength+ShinLength;
        float vertical=ThighLength*MathF.Cos(thigh)+lower*MathF.Cos(shinAngle);
        float plantedReach=MathF.Sqrt(MathF.Max(0,reach*reach-hipX*hipX-hipZ*hipZ));
        return new(hipX,MathF.Min(vertical,plantedReach)-reach,hipZ);
    }
}
