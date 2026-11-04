using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int HandBoneCount=56,ForearmIntervals=36,WristIntervals=5;
    internal static int ForearmBone(int station)=>station==ForearmIntervals?15:16+station;
    internal static int WristBone(int station)=>station==0?0:station==WristIntervals?15:15+ForearmIntervals+station;
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
    private readonly Quaternion[] _elbowSkinRotation={Quaternion.Identity,Quaternion.Identity};
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
        var inverse=Hands[side].Basis.Inverse();var axis=Hands[side].Position.Normalized();float roll=ForearmRoll(side);
        var unhinge=(Elbows[side].Basis*new Basis(axis,-roll)).Inverse().GetRotationQuaternion();
        return ForearmSection(fraction,inverse,axis,-Hands[side].Position,roll,unhinge);
    }
    internal static Transform3D ForearmSection(float fraction,Basis inverse,Vector3 axis,Vector3 pivot,float roll,Quaternion unhinge)
    {
        // The proximal ring follows the upper arm; the shaft gradually takes
        // the elbow hinge and pronation. Wrist/grip nodes remain exact IK nodes.
        float bridge=1-ReachEase(fraction/.45f);
        float pronation=Mathf.SmoothStep(.02f,.85f,fraction*fraction);
        var turn=new Basis(axis,-roll*(1-pronation))*new Basis(Quaternion.Identity.Slerp(unhinge,bridge));
        return new(inverse*turn,inverse*(pivot-turn*pivot));
    }
    internal static float ForearmSkinFraction(float height)
        =>MathF.Sqrt(Math.Clamp((.255f-height)/.210f,0,1));
    internal void ResetForearmStudy(int side)
    {
        Elbows[side].Basis=Basis.Identity;Hands[side].Basis=Basis.Identity;
        _forearmRoll[side]=float.NaN;UpdateHandBones(side);
    }
    private void UpdateForearmSkin(int side)
    {
        float roll=ForearmRoll(side);var inverse=Hands[side].Basis.Inverse();var wrist=inverse.GetRotationQuaternion();
        var elbow=Elbows[side].Basis.GetRotationQuaternion();
        if(MathF.Abs(wrist.Dot(_wristSkinRotation[side]))>=.999999f&&MathF.Abs(roll-_forearmRoll[side])<.00001f&&MathF.Abs(elbow.Dot(_elbowSkinRotation[side]))>=.999999f)return;
        _wristSkinRotation[side]=wrist;_forearmRoll[side]=roll;_elbowSkinRotation[side]=elbow;
        var axis=Hands[side].Position.Normalized();var pivot=-Hands[side].Position;
        var unhinge=(Elbows[side].Basis*new Basis(axis,-roll)).Inverse().GetRotationQuaternion();
        UpdateForearmNormals(side,roll,unhinge);
        for(int station=0;station<=ForearmIntervals;station++)
            HandSkeletons[side].SetBonePose(ForearmBone(station),ForearmSection((float)station/ForearmIntervals,inverse,axis,pivot,roll,unhinge));
        for(int station=1;station<WristIntervals;station++)
            HandSkeletons[side].SetBonePose(WristBone(station),new Transform3D(new Basis(Quaternion.Identity.Slerp(wrist,(float)station/WristIntervals)),Vector3.Zero));
    }
}
