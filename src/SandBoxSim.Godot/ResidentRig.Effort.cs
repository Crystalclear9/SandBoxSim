using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Presentation coefficients, not measured strength or simulation skills.
    internal float WorkCapacity=1;
    private float _workFatigue,_workHealth=1,_strokePhase;
    public void SetWorkCondition(float fatigue,float health)
    {
        _workFatigue=float.IsFinite(fatigue)?Math.Clamp(fatigue,0,1):0;
        _workHealth=float.IsFinite(health)?Math.Clamp(health,0,1):1;
    }
    internal float RelativeWorkLoad=>Math.Clamp((CurrentTool switch
    {ResidentTool.Pick=>1.25f,ResidentTool.Axe=>1.10f,ResidentTool.Hoe=>.90f,ResidentTool.Spear=>.85f,_=>.75f})/
        MathF.Max(.65f,WorkCapacity)*(1+.20f*_workFatigue+.15f*(1-_workHealth)),.60f,1.70f);
    private float WorkPeriod=>(CurrentTool==ResidentTool.Hoe?1.55f:CurrentTool==ResidentTool.Pick?1.35f:CurrentTool==ResidentTool.Axe?1.25f:1.05f)*
        (1+.18f*(RelativeWorkLoad-1)+.20f*_workFatigue);
    private float WorkLift(float phase)
    {
        float t=Mathf.PosMod(phase,1),prepare=.48f+.055f*RelativeWorkLoad;
        if(t<prepare)return ReachEase(t/prepare);
        if(t<.79f)return 1-ReachEase((t-prepare)/(.79f-prepare));
        // Small absorption and recovery after contact; no frozen final quarter.
        return -.045f*MathF.Sin(MathF.PI*ReachEase((t-.79f)/.21f));
    }
}
