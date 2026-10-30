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
    internal float WorkLift(float phase)
    {
        float t=Mathf.PosMod(phase,1);
        // A periodic phase warp gives a longer preparation and shorter return.
        // Unlike concatenated quintic segments, the apex has nonzero curvature:
        // the limb reverses direction without settling into a held key pose.
        float skew=.30f+.06f*(RelativeWorkLoad-1);
        float cycle=t-skew*(1-MathF.Cos(MathF.Tau*t))/MathF.Tau;
        return .5f-.5f*MathF.Cos(MathF.Tau*cycle);
    }
}
