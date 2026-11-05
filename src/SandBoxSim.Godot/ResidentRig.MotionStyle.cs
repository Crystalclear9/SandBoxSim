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
    private float ThoraxSway=>.060f-.044f*Flow;
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
        float counterTurn=Mathf.Lerp(MathF.Cos(phase-(.18f+.49f*Flow)),_capturedChest.Y,.62f);
        float pitch=Mathf.Lerp(-MathF.Cos(phase*2-.48f),_capturedChest.X,.66f);
        float roll=Mathf.Lerp(MathF.Sin(phase-.28f),_capturedChest.Z,.65f);
        return new Vector3(pitch*(.033f-.019f*Flow),counterTurn*GaitPelvisYaw*(1.30f-.30f*Flow),roll*ThoraxSway);
    }

    private Vector3 WorkingChest(float phase,float blend)
    {
        var arcs=WorkStageArcs(phase+WorkTorsoLead);
        var drive=new Vector3(0,.105f*arcs.X-.065f*arcs.Y,.040f*arcs.X-.029f*arcs.Y);
        var flow=new Vector3(0,-.026f*arcs.X+.051f*arcs.Y,-.014f*arcs.X+.024f*arcs.Y);
        return drive.Lerp(flow,Flow)*(RelativeWorkLoad*blend);
    }

    private Vector2 WorkStageArcs(float phase)
    {
        float t=Mathf.PosMod(phase,1);
        float preparation=WorkPreparation;
        // Weight is still on the supporting leg while the chest initiates
        // the downward stroke. The apex no longer passes through neutral.
        float loaded=preparation*.78f;
        float release=preparation-.055f,impact=preparation+.14f;
        float windup=ReachEase(t/loaded)*(1-ReachEase((t-loaded)/(preparation+.19f-loaded)));
        float through=ReachEase((t-release)/(impact-release))*(1-ReachEase((t-impact)/(1-impact)));
        return new(windup,through);
    }

    private Vector2 WorkWeightShift(float phase)
    {
        var arcs=WorkStageArcs(phase+WorkTorsoLead);
        // The drive winds onto the tool side and releases towards the work.
        // The other phrase transfers forward early and returns across centre.
        var drive=new Vector2(.043f*arcs.X-.015f*arcs.Y,.022f*arcs.X-.038f*arcs.Y);
        var flow=new Vector2(-.015f*arcs.X+.017f*arcs.Y,-.022f*arcs.X+.016f*arcs.Y);
        return drive.Lerp(flow,Flow);
    }

    private Vector3 ToolWindup(float lift,float arc)
    {
        float crown=lift*lift;
        var drive=new Vector3(.078f*arc,.045f*crown,.017f*arc);
        var flow=new Vector3(-.014f*arc,-.096f*crown,.025f*arc-.054f*crown);
        return drive.Lerp(flow,Flow);
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
    private Vector3 SocialChest(ResidentActionCapture.SocialPose pose)
    {
        return pose.Chest*new Vector3(.039f,-.082f,.039f);
    }
    private (Vector3 Target,float Weight,Basis Palm) SocialPhrase(int side,Vector3 travel)
    {
        float mirror=side==0?-1:1;
        float lift=Math.Clamp(travel.Y,0,1);
        float weight=Mathf.SmoothStep(.025f,.61f,lift);
        float spread=Math.Clamp(.205f+travel.X*.135f,.155f,.31f);
        var target=new Vector3(mirror*spread,.89f+.225f*lift,-.198f-.082f*travel.Z-.035f*lift);
        var palm=new Vector3(.50f+.29f*Flow+.11f*lift,-mirror*(.28f+.16f*travel.X),mirror*(.27f-.12f*Flow));
        return (target,weight,Basis.FromEuler(palm));
    }
}
