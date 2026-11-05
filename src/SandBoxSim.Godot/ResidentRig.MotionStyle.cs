using System;

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
}
