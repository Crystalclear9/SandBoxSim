using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int HandBoneCount=21,ForearmIntervals=5;
    internal static int ForearmBone(int station)=>station==ForearmIntervals?15:16+station;
    private readonly float[] _forearmRoll={float.NaN,float.NaN};
    internal float ForearmRoll(int side)
    {
        var bind=Hands[side].Position.Normalized();var direction=Elbows[side].Basis*bind;
        float flexion=MathF.Atan2(-direction.Z,-direction.Y)-MathF.Atan2(-bind.Z,-bind.Y);
        var relative=(new Basis(Vector3.Right,flexion).Inverse()*Elbows[side].Basis).GetRotationQuaternion();
        if(relative.W<0)relative=-relative;
        float angle=2*MathF.Atan2(new Vector3(relative.X,relative.Y,relative.Z).Dot(bind),relative.W);
        return float.IsFinite(_forearmRoll[side])?_forearmRoll[side]+Mathf.Wrap(angle-_forearmRoll[side],-MathF.PI,MathF.PI):angle;
    }
    internal Transform3D ForearmSectionPose(int side,float fraction)
    {
        // The elbow skin follows flexion; pronation accumulates towards the
        // wrist. Compensate inside the existing connected hand/forearm skin,
        // leaving the actual wrist, grip and elbow nodes exactly where IK put them.
        var wristInverse=Hands[side].Basis.Inverse();var axis=Hands[side].Position.Normalized();
        var turn=new Basis(axis,-ForearmRoll(side)*(1-fraction));var pivot=-Hands[side].Position;
        return new(wristInverse*turn,wristInverse*(pivot-turn*pivot));
    }
    private void UpdateForearmSkin(int side)
    {
        float roll=ForearmRoll(side);var wrist=Hands[side].Basis.Inverse().GetRotationQuaternion();
        if(MathF.Abs(wrist.Dot(_wristSkinRotation[side]))>=.999999f&&MathF.Abs(roll-_forearmRoll[side])<.00001f)return;
        _wristSkinRotation[side]=wrist;_forearmRoll[side]=roll;
        for(int station=0;station<=ForearmIntervals;station++)
            HandSkeletons[side].SetBonePose(ForearmBone(station),ForearmSectionPose(side,(float)station/ForearmIntervals));
    }
}
