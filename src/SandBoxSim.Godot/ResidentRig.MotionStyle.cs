using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Art direction on a continuous axis, independent of work capacity and
    // simulation speed. These are stylized adult defaults, not measurements
    // or constraints on an individual's strength or identity.
    internal float MotionFlow=.5f;
    private float Flow=>Math.Clamp(MotionFlow,0,1);
    private float ArmFollowTime=>.045f+.020f*Flow;
    private float ReachFollowTime=>.033f+.008f*Flow;
    private float GestureRotationTime=>.036f+.010f*Flow;
    private float ArmReturnTime=>.048f+.014f*Flow;
    private float TorsoFollowTime=>.055f+.015f*Flow;
    private float ThoraxSway=>.009f-.005f*Flow;
    private float StrokeAccent=>1.08f-.16f*Flow;
    private float WorkSkew=>.39f-.16f*Flow+.06f*(RelativeWorkLoad-1);
    private float WorkPreparation=>.5f+.28f*WorkSkew;
    private float WorkTorsoLead=>.065f-.050f*Flow;
    private float TurnChestShare=>.20f-.08f*Flow;
    private float TurnHeadShare=>.34f+.08f*Flow;

    // Distinct recovery paths, with zero velocity/acceleration at their
    // boundaries. They end at the same functional contact and carry poses.
    internal Vector3 StrokeFollowThrough(float phase)
    {
        float t=Mathf.PosMod(phase,1);
        if(t<=WorkPreparation)return Vector3.Zero;
        float arc=MathF.Sin(MathF.PI*ReachEase((t-WorkPreparation)/(1-WorkPreparation)));
        return new Vector3(.008f*(1-Flow),0,-(.018f-.012f*Flow))*arc;
    }
    private Vector3 SocialGestureTarget(int side,float timing,float emphasis)
    {
        float mirror=side==0?-1:1;
        // Open, forward emphasis versus a narrower, softly orbiting gesture.
        // This alters the planned palm path before the same constrained IK.
        float lateral=.24f-.04f*Flow+.018f*Flow*MathF.Sin(timing-.35f);
        return new(mirror*lateral,1+emphasis+.012f*Flow*MathF.Cos(timing),
            -.22f-emphasis+.012f*Flow*MathF.Sin(timing));
    }
}
