using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private const float PivotDuration=.48f;
    private readonly Transform3D[] _pivotFeet=new Transform3D[2];
    private float _pivotProgress,_pivotWeight,_pivotStartYaw,_pivotGoalYaw;
    private int _pivotLead;
    internal bool IsPivoting=>_pivotWeight>.001f;

    internal float AdvancePivot(float yaw,float target,float delta)
    {
        float turn=Mathf.AngleDifference(yaw,target);
        if(!IsPivoting||_pivotProgress>=1)
        {
            if(MathF.Abs(turn)<.16f)
            {
                CancelPivot();
                return yaw+Math.Clamp(turn,-delta*1.5f,delta*1.5f);
            }
            _pivotStartYaw=yaw;
            _pivotGoalYaw=yaw+Math.Clamp(turn,-.86f,.86f);
            _pivotLead=turn>0?0:1;
            _pivotWeight=1;
            _pivotProgress=0;
            var before=new Transform3D(new Basis(Vector3.Up,yaw),Vector3.Zero);
            for(int side=0;side<2;side++)
            {
                var foot=before*DisplayedFoot(side);
                foot.Origin=new(foot.Origin.X,RestFootCenter(side).Y,foot.Origin.Z);
                _pivotFeet[side]=foot;
            }
            ResetGroundContacts();
        }
        _pivotProgress=Mathf.MoveToward(_pivotProgress,1,delta/PivotDuration);
        return Mathf.Lerp(_pivotStartYaw,_pivotGoalYaw,ReachEase(_pivotProgress/.55f));
    }

    internal void CancelPivot()=>_pivotWeight=0;
    internal void FadePivot(float delta)=>_pivotWeight=Mathf.MoveToward(_pivotWeight,0,delta/.16f);

    private Vector3 PivotSupport()
    {
        float load=MathF.Max(0,MathF.Sin(MathF.PI*_pivotProgress))*_pivotWeight;
        float side=_pivotLead==0?1:-1;
        return new(side*.028f*load*MathF.Cos(MathF.PI*_pivotProgress),-.024f*load,0);
    }

    private void PosePivotLeg(int side)
    {
        if(!IsPivoting)return;
        float step=side==_pivotLead?_pivotProgress/.55f:(_pivotProgress-.55f)/.45f;
        step=Math.Clamp(step,0,1);
        float progress=ReachEase(step);
        var target=new Basis(Vector3.Up,_pivotGoalYaw);
        var source=_pivotFeet[side];
        var to=target*RestFootCenter(side);
        float clearance=Math.Clamp(ResidentTravel.Planar(to-source.Origin).Length()*.57f,0,.048f);
        float arc=MathF.Sin(MathF.PI*step);
        var foot=source.Origin.Lerp(to,progress)+Vector3.Up*(clearance*arc*arc);
        var rotation=source.Basis.GetRotationQuaternion().Slerp(target.GetRotationQuaternion(),progress);
        var inverse=new Basis(Vector3.Up,-Rotation.Y);
        var displayed=DisplayedFoot(side);
        var corrected=new Basis(displayed.Basis.GetRotationQuaternion().Slerp((inverse*new Basis(rotation)).GetRotationQuaternion(),_pivotWeight));
        SolveLegTo(side,displayed.Origin.Lerp(inverse*foot,_pivotWeight),corrected);
    }
}
