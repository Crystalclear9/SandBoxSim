using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private Vector3 _supportAngle,_supportVelocity;
    private float _workStance;
    internal bool WorkStanceSettled=>_workStance>=1;
    private bool UsesStagedStance=>CurrentTool is ResidentTool.Hammer or ResidentTool.Hoe;
    internal const float NeutralHipHalfWidth=.085f;
    internal float HipHalfWidth=NeutralHipHalfWidth,ArmRestClearance;
    private void AdvanceWorkStance(float delta,bool wanted)
        =>_workStance=Mathf.MoveToward(_workStance,wanted?1:0,delta/(wanted?.43f:.32f));

    private void PoseWorkFoot(int side,float walking)
    {
        if(_workStance<=0||walking>=.999f)return;
        float phase=Math.Clamp((_workStance-(side==0?0:.51f))/(side==0?.51f:.49f),0,1);
        float shift=ReachEase(phase);
        float rise=MathF.Sin(phase*MathF.PI);
        float mirror=side==0?-1:1;
        var target=RestFootCenter(side)+new Vector3(mirror*.028f*shift,.032f*rise*rise,
            (side==0?-.126f:.058f)*shift);
        var foot=DisplayedFoot(side);
        var sole=new Basis(Vector3.Up,mirror*.14f*shift);
        SolveLegTo(side,foot.Origin.Lerp(target,1-walking),new Basis(foot.Basis.GetRotationQuaternion().Slerp(sole.GetRotationQuaternion(),1-walking)));
    }
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
        // Prepare the shift to the planted leg before each new work footfall.
        float transfer=ReachPulse(_workStance/.51f)-ReachPulse((_workStance-.51f)/.49f);
        float hipX=(_supportAngle.Y+(.034f-.007f*Flow)*transfer)*weight;
        float hipZ=-.035f*thigh+_supportAngle.Z*weight;
        float lower=ShinLength;
        float shinAngle=MathF.Asin(Math.Clamp((hipZ-ThighLength*MathF.Sin(thigh))/lower,-1,1));
        float reach=ThighLength+ShinLength;
        float vertical=ThighLength*MathF.Cos(thigh)+lower*MathF.Cos(shinAngle);
        float plantedReach=MathF.Sqrt(MathF.Max(0,reach*reach-hipX*hipX-hipZ*hipZ));
        float stanceDrop=(.032f-.008f*Flow)*ReachEase(_workStance)*weight;
        return new(hipX,MathF.Min(vertical,plantedReach)-reach-stanceDrop,hipZ);
    }
}
