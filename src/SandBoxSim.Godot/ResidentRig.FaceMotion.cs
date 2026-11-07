using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal readonly record struct ConversationBeat(float Speech,float Nod);
    internal (uint Revision,long Partner)? LastConversation;
    private ConversationBeat? _conversation;
    internal float ConversationMouth=>_mouthAmount;
    internal float ConversationNod {get;private set;}

    internal float RestLid;
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
        if(_chewRemaining>0)
        {
            float elapsed=.62f-_chewRemaining;
            return .32f*(.55f+.45f*MathF.Sin(elapsed*24))*(1-ReachEase(elapsed/.62f));
        }
        if(action!=_consumeAction)return 0;
        if(_consumeStage==ConsumeStage.Use)
            return ReachEase(_consumeContactTime/.06f)*(action==ActionKind.Drink?.15f:.40f);
        return 0;
    }
}
