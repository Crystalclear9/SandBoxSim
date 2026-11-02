using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int HandBoneCount=25,ForearmIntervals=5,WristIntervals=5;
    internal static int ForearmBone(int station)=>station==ForearmIntervals?15:16+station;
    internal static int WristBone(int station)=>station==0?0:station==WristIntervals?15:20+station;
    internal Transform3D WristSectionPose(int side,float fraction)
        =>new(new Basis(Quaternion.Identity.Slerp(Hands[side].Basis.Inverse().GetRotationQuaternion(),fraction)),Vector3.Zero);
    internal void StudyWrist(float delta,float time)
    {
        float phase=Mathf.PosMod(time,8),progress=ReachEase((phase%2)/2),wave=MathF.Sin(MathF.Tau*progress);
        StudyArmElevation(delta,.45f,.55f,1,phase>=4&&phase<6?wave:0,1.15f);
        Hands[1].Basis=Basis.FromEuler(new(phase<2?.55f*wave:0,0,phase>=2&&phase<4?.25f*wave:0));
        CurlHand(1,phase>=6?.15f+.85f*(.5f-.5f*MathF.Cos(MathF.Tau*progress)):.15f);
        UpdateHandBones(1);
    }
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
        for(int station=1;station<WristIntervals;station++)
            HandSkeletons[side].SetBonePose(WristBone(station),WristSectionPose(side,(float)station/WristIntervals));
    }
}
