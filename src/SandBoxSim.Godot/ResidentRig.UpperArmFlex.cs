using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private readonly float[] _studyNeutralRoll={float.NaN,float.NaN};
    internal void StudyUpperArmReference(int side,float bend,float roll)
    {
        // An unloaded joint-angle reference must not repeatedly solve a grip
        // whose wrist constraints can change the elbow plane and axial frame.
        var outward=new Vector3((side==0?-1:1)*MathF.Cos(.45f),0,-MathF.Sin(.45f));
        var upper=Vector3.Down.Rotated(Vector3.Down.Cross(outward).Normalized(),.55f);
        Arms[side].Basis=PreferredArmFrame(side,upper);
        Arms[side].Position=ShoulderGirdle(side,upper).Origin;
        var neutral=new Basis(Vector3.Right,bend);
        Hands[side].Basis=Basis.Identity;CurlHand(side,.22f);
        SyncShoulderBone(side);StudyForearmRotation(side,neutral,roll);
    }
    internal void StudyForearmRotation(int side,Basis neutral,float roll)
    {
        // Isolate axial rotation without asking wrist-limited IK to solve a
        // different grip. The wrist and elbow centre positions stay fixed.
        // Track the neutral reference separately from the final rolled pose.
        // Resetting to a principal angle each frame flips +pi to -pi, whereas
        // unwrapping against intermediate idle poses accumulates whole turns.
        Elbows[side].Basis=neutral;_forearmRoll[side]=float.NaN;
        float baseline=ForearmRoll(side);
        if(float.IsFinite(_studyNeutralRoll[side]))baseline=_studyNeutralRoll[side]+Mathf.Wrap(baseline-_studyNeutralRoll[side],-MathF.PI,MathF.PI);
        _studyNeutralRoll[side]=baseline;
        Elbows[side].Basis=neutral*new Basis(Hands[side].Position.Normalized(),roll);
        _forearmRoll[side]=baseline+roll;
        UpdateHandBones(side);
    }
}
