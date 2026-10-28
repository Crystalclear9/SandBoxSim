using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal static float FaceBlink(float time,float identity)
    {
        // Staggered, deterministic windows; fast closure and slower reopening.
        // No simulation RNG, frame counter, or wall-clock dependency.
        float clock=time+identity*.37f,window=MathF.Floor(clock/4.3f);
        float start=2+.8f*(.5f+.5f*MathF.Sin(window*12.989f+identity*7.13f));
        float t=Mathf.PosMod(clock,4.3f)-start;
        return t<0||t>=.22f?0:t<.065f?ReachEase(t/.065f):1-ReachEase((t-.065f)/.155f);
    }
    private float MouthActivity(ActionKind action,float emphasis)
    {
        if(action==ActionKind.Socialize)
            return Math.Clamp(.35f+.55f*MathF.Abs(MathF.Sin(_workTime*7)+.25f*MathF.Sin(_workTime*13)),0,1)*emphasis*_socialBlend;
        if(action is not (ActionKind.Eat or ActionKind.Drink))return 0;
        float cycle=Mathf.PosMod(_bareTime/2.6f,1);
        float nearMouth=ReachEase((cycle-.35f)/.12f)*(1-ReachEase((cycle-.88f)/.12f));
        return nearMouth*(action==ActionKind.Drink?.15f:.35f+.15f*MathF.Abs(MathF.Sin(_bareTime*6)));
    }
}
