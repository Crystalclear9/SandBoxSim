using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Presentation coefficients, not measured strength or simulation skills.
    internal float WorkCapacity=1;
    private float _workFatigue,_workHealth=1,_strokePhase;
    private bool _finishToolStroke,_toolImpactSeen,_rearmToolStroke,_awaitToolCommit;
    private float _spearContactWait;
    internal int PresentedToolImpacts {get;private set;}
    internal float ToolStrokePhase=>_strokePhase;
    public void SetWorkCondition(float fatigue,float health)
    {
        _workFatigue=float.IsFinite(fatigue)?Math.Clamp(fatigue,0,1):0;
        _workHealth=float.IsFinite(health)?Math.Clamp(health,0,1):1;
    }
    internal float RelativeWorkLoad=>Math.Clamp((CurrentTool switch
    {ResidentTool.Pick=>1.25f,ResidentTool.Axe=>1.10f,ResidentTool.Hoe=>.90f,ResidentTool.Spear=>.85f,_=>.75f})/
        MathF.Max(.65f,WorkCapacity)*(1+.20f*_workFatigue+.15f*(1-_workHealth)),.60f,1.70f);
    private float WorkPeriod=>(CurrentTool==ResidentTool.Hoe?2.05f:CurrentTool==ResidentTool.Pick?1.35f:CurrentTool==ResidentTool.Axe?1.25f:1.05f)*
        (1+.18f*(RelativeWorkLoad-1)+.20f*_workFatigue);

    private static float ToolImpactPhase(ResidentTool tool)=>tool switch
    {
        ResidentTool.Hoe=>.36f,
        ResidentTool.Spear=>.68f,
        ResidentTool.Hammer=>.88f,
        ResidentTool.Pick=>.87f,
        _=>.85f
    };

    private string ToolStrokeHandling=>CurrentTool switch
    {
        ResidentTool.Hoe=>_strokePhase<.29f?"落锄":_strokePhase<.54f?"沿土面回拉":"抬刃 / 收势",
        ResidentTool.Spear=>_awaitToolCommit&&_strokePhase>=.47f?"瞄准 / 等待时机":_strokePhase<=.68f?"蓄力 / 前刺":"收矛 / 复位",
        _=>_strokePhase<WorkPreparation?"抬起工具":_surfaceTouched?"触及表面 / 收势":
            _strokePhase<ToolImpactPhase(CurrentTool)?"向目标挥下":"卸力 / 收势"
    };

    private void ResetToolPhrase(bool retainPose,bool keepImpact=false)
    {
        _finishToolStroke=_awaitToolCommit=false;
        _spearContactWait=0;
        if(!keepImpact)_toolImpactSeen=false;
        _rearmToolStroke=retainPose&&_workBlend>.025f;
        if(!_rearmToolStroke)_strokePhase=0;
    }

    private void AdvanceWorkStroke(float delta)
    {
        float before=_strokePhase;
        float next=before+delta/WorkPeriod;
        if(_awaitToolCommit&&CurrentTool==ResidentTool.Spear)next=MathF.Min(next,.48f);
        float impact=ToolImpactPhase(CurrentTool);
        if(CurrentTool==ResidentTool.Spear&&_toolSurface.HasValue&&!_surfaceTouched&&next>=impact
            &&before<=impact&&_spearContactWait<.18f)
        {
            // Let the displayed shaft reach its contact window before
            // withdrawing. If it cannot reach, recover from the miss.
            next=impact;
            _spearContactWait+=delta;
        }
        if(before<impact&&next>=impact)
        {
            _toolImpactSeen=true;
            if(CurrentTool!=ResidentTool.Spear||!_toolSurface.HasValue)PresentedToolImpacts++;
        }
        // A new receipt can arrive while the previous stroke is recovering.
        // Finish this receipt only after an impact in its own stroke.
        if(_finishToolStroke&&_toolImpactSeen&&next>=1)
        {
            _strokePhase=1;
            _finishToolStroke=false;
        }
        else _strokePhase=Mathf.PosMod(next,1);
    }
    internal float WorkLift(float phase)
    {
        float t=Mathf.PosMod(phase,1);
        // A periodic phase warp gives a longer preparation and shorter return.
        // Unlike concatenated quintic segments, the apex has nonzero curvature:
        // the limb reverses direction without settling into a held key pose.
        float skew=WorkSkew;
        float cycle=t-skew*(1-MathF.Cos(MathF.Tau*t))/MathF.Tau;
        return .5f-.5f*MathF.Cos(MathF.Tau*cycle);
    }
}
