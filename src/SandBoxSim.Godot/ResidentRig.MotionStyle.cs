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
        float counterTurn=MathF.Cos(phase-(.18f+.49f*Flow))*GaitPelvisYaw*(1.30f-.30f*Flow);
        float pitch=-(.033f-.019f*Flow)*MathF.Cos(phase*2-.48f);
        return new Vector3(pitch,counterTurn,MathF.Sin(phase-.28f)*ThoraxSway);
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
        return t<preparation
            ?new(MathF.Sin(MathF.PI*ReachEase(t/preparation)),0)
            :new(0,MathF.Sin(MathF.PI*ReachEase((t-preparation)/(1-preparation))));
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
    private Vector3 SocialChest(float phase)
    {
        // The trunk sets up a phrase, then yields before the hands settle.
        float emphasis=ReachPulse(Mathf.PosMod(phase+.09f,1));
        float answer=ReachPulse(Mathf.PosMod(phase-.12f,1));
        var drive=new Vector3(-.022f*emphasis,-.083f*emphasis,.040f*emphasis);
        var flow=new Vector3(-.046f*emphasis,.041f*emphasis-.057f*answer,-.030f*emphasis+.037f*answer);
        return drive.Lerp(flow,Flow);
    }
    private (Vector3 Target,float Weight,Basis Palm) SocialPhrase(int side,float phase)
    {
        float mirror=side==0?-1:1;
        float lead=ReachPulse(Mathf.PosMod(phase,1));
        float echo=ReachPulse(Mathf.PosMod(phase-.13f,1));
        float driveWeight=side==1?lead:echo*.32f;
        float turn=Mathf.PosMod(phase+(side==1?.08f:-.045f),1);
        float flowWeight=ReachPulse(turn)*(side==1?1:.92f);
        float arc=MathF.Sin(turn*Mathf.Tau);
        var drive=new Vector3(mirror*(side==1?.275f:.177f),side==1?1.035f:.87f,-.185f-.135f*lead);
        var flow=new Vector3(mirror*(.190f+.074f*arc),1.015f+.056f*MathF.Cos(turn*Mathf.Tau),-.246f-.040f*arc);
        var drivePalm=new Vector3(.50f,-mirror*.30f,mirror*.32f);
        var flowPalm=new Vector3(.89f+.17f*arc,-mirror*(.20f+.13f*arc),mirror*(.19f+.09f*arc));
        return (drive.Lerp(flow,Flow),Mathf.Lerp(driveWeight,flowWeight,Flow),Basis.FromEuler(drivePalm.Lerp(flowPalm,Flow)));
    }
}
