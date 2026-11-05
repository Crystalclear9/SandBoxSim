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
    private float ThoraxSway=>.025f-.014f*Flow;
    private float StrokeAccent=>1.17f-.26f*Flow;
    private float WorkSkew=>.48f-.29f*Flow+.06f*(RelativeWorkLoad-1);
    private float WorkPreparation=>.5f+.28f*WorkSkew;
    private float WorkTorsoLead=>.105f-.075f*Flow;
    private float TurnChestShare=>.26f-.13f*Flow;
    private float TurnHeadShare=>.30f+.24f*Flow;

    private Vector3 WalkingChest(float phase)
    {
        // Keep the head and thorax legible against the alternating pelvis.
        // The flow end carries the rotation through the torso with less side lean.
        float counterTurn=MathF.Cos(phase-(.12f+.42f*Flow))*GaitPelvisYaw*(.92f+.38f*Flow);
        float pitch=-(.027f-.016f*Flow)*MathF.Cos(phase*2-.35f);
        return new Vector3(pitch,counterTurn,MathF.Sin(phase-.18f)*ThoraxSway);
    }

    private Vector3 WorkingChest(float phase,float blend)
    {
        float sweep=MathF.Sin((phase+WorkTorsoLead)*Mathf.Tau-.5f);
        float load=RelativeWorkLoad*blend;
        return new Vector3(0,(.046f-.025f*Flow)*sweep*load,(.026f-.016f*Flow)*sweep*load);
    }

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
        float lateral=.265f-.065f*Flow+.024f*Flow*MathF.Sin(timing-.35f);
        return new(mirror*lateral,1+emphasis+.020f*Flow*MathF.Cos(timing),
            -.22f-emphasis*(1.36f-.48f*Flow)+.018f*Flow*MathF.Sin(timing));
    }
}
