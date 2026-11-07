using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal readonly Dictionary<ResourceKind,(Vector3 Left,Vector3 Right)> CargoSupports=new();
    internal static readonly Vector3 CargoPalmContact=new(0,-.029f,-.010f);
    private static readonly Basis CargoPalmBasis=new(Vector3.Right,MathF.PI/2);
    private float _cargoDepth,_cargoTuck,_placementAge,_placementContact;
    private Vector2 _placementStart;
    private bool _placingCargo;
    private bool _placementPending;
    private bool _stowingCargo;
    private bool _cargoFromCaravan;
    private float _placementWait;
    private ResourceKind _placementResource;
    private bool WaitingForPlacementParcel=>_placementPending&&(_placementResource!=_cargoKind||_cargoFromCaravan);
    private bool _groundPlacement;
    private Vector3? _placementSurface;
    private bool PlacementUsesSurface=>_groundPlacement||_placementSurface.HasValue;
    private float _floorCrouch,_floorDrop=.46f,_floorLean=.91f;
    private Vector3 _floorCorrection;
    private Transform3D _floorStart,_floorGoal;
    internal bool PlacingCargo=>_placingCargo;
    private bool _releasedCargo;
    private bool _releasedOntoSurface;
    private Transform3D _releasedCargoPose;
    private float _releasedCargoTime,_releasedCargoFloor,_releasedCargoDuration;
    internal bool ReleasedCargo=>_releasedCargo;
    internal ResourceKind SupportedCargo=>CargoMesh.Visible&&!_releasedCargo?_cargoKind:ResourceKind.None;
    internal bool SupportingCaravanCargo=>_cargoFromCaravan&&SupportedCargo!=ResourceKind.None;
    internal Func<float,float,float>? CargoGroundHeight;

    private static bool IsPlacement(ActionKind action)=>action is ActionKind.Deposit or ActionKind.StoreInBuilding;

    private void ObserveCargoPlacement(ActionKind action,ActionPhase phase,bool newlyDone,bool newRun,ResourceKind contactResource,
        ResourceKind? inventoryCargo,bool fromCaravan)
    {
        bool alreadyFinishing=_placingCargo||_placementPending;
        bool lostSurface=_placementSurface.HasValue&&(!_sceneContact.HasValue
            ||_placementSurface.Value.DistanceSquaredTo(_sceneContact.Value)>.14f*.14f);
        if(newRun||!IsPlacement(action)||phase!=ActionPhase.Done||lostSurface)
            _placingCargo=_placementPending=false;
        bool matchingParcel=_sceneContact.HasValue&&contactResource==_cargoKind;
        bool confirmedParcel=inventoryCargo.HasValue&&_sceneContact.HasValue&&contactResource!=ResourceKind.None;
        bool emptyInventory=inventoryCargo.HasValue?inventoryCargo==ResourceKind.None:!_hasCargo;
        bool supportedParcel=(emptyInventory||matchingParcel)&&CargoMesh.Visible&&!_releasedCargo;
        if(newlyDone&&(!alreadyFinishing||newRun)&&IsPlacement(action)&&(confirmedParcel||supportedParcel))
        {
            _placementPending=true;_placementWait=0;
            _groundPlacement=action==ActionKind.Deposit;
            _placementSurface=_sceneContact;
            _placementResource=confirmedParcel?contactResource:_cargoKind;
        }
        if(WaitingForPlacementParcel&&!_releasedCargo&&!_stowingCargo&&!CargoMesh.Visible)
            ShowCargo(true,_placementResource);
        if(inventoryCargo is not {} remaining)return;
        // A display parcel already supported by both palms keeps its identity
        // until the hands bring it back to the waist. A new inventory reading
        // must not replace it in the middle of the reach or the next step.
        if(!_placingCargo&&!_releasedCargo)
        {
            var nextParcel=_placementPending?_placementResource:remaining;
            bool nextFromCaravan=fromCaravan&&!_placementPending;
            if(nextParcel==_cargoKind&&nextFromCaravan==_cargoFromCaravan)_stowingCargo=false;
            else if(SupportedCargo!=ResourceKind.None)_stowingCargo=true;
        }
        // A committed parcel can be in the hands after leaving inventory.
        // Keep its mesh through palm support and release, then show the remainder.
        if(_placementPending||_placingCargo||_releasedCargo||_stowingCargo)_hasCargo=remaining!=ResourceKind.None;
        else ShowCargo(remaining!=ResourceKind.None,remaining,fromCaravan);
    }

    private void AdvancePendingPlacement(float delta,bool targetAligned)
    {
        bool ready=targetAligned&&InteractionSettled&&!HandoffReserved;
        if(_placingCargo&&!ready)
        {
            // An interrupted descent can continue only from the displayed
            // parcel pose, after the receiver is aligned and grounded again.
            _placingCargo=false;
            _placementPending=true;
            _placementContact=0;
        }
        if(!_placementPending)return;
        _placementWait+=delta;
        // A rendered warehouse entrance can lie outside the logical building
        // tile. Allow the same approach budget as other committed gestures.
        if(_placementWait>=(_placementSurface.HasValue?6f:2.5f)){_placementPending=false;return;}
        if(!ready||_releasedCargo||!CargoMesh.Visible||WaitingForPlacementParcel)return;
        _placementPending=false;_placingCargo=true;
        _placementAge=_placementContact=0;
        _placementStart=new(_cargoLift,_cargoDepth);
        if(PlacementUsesSurface)PlanGroundPlacement();
    }

    private void PlanGroundPlacement()
    {
        var actor=RigWorldSpace;
        _floorStart=HandWorldPose(1)*CargoMesh.Transform;
        var point=_placementSurface??actor*new Vector3(0,0,-.54f);
        var ground=_groundPlacement?CargoGroundHeight?.Invoke(point.X,point.Z)??point.Y:point.Y;
        float below=Mathf.Clamp((actor.Origin.Y-ground)/actor.Basis.Y.Length(),0,.14f);
        _floorDrop=(_groundPlacement?.46f:.36f)+below*.55f;
        _floorLean=(_groundPlacement?.91f:.83f)+below*1.15f;
        _floorCorrection=Vector3.Zero;
        point.Y=ground-CargoMesh.Mesh.GetAabb().Position.Y*actor.Basis.Y.Length()+(_groundPlacement?.035f:.018f);
        var basis=new Basis(Vector3.Up,actor.Basis.Orthonormalized().GetEuler().Y);
        _floorGoal=new(basis.Scaled(actor.Basis.Scale),point);
    }

    private float GroundPlacementBody(float delta)
    {
        _floorCrouch=Mathf.MoveToward(_floorCrouch,_placingCargo&&PlacementUsesSurface?1:0,delta/.83f);
        return ReachEase(_floorCrouch);
    }

    private Basis PlacementPalmBasis=>_placingCargo&&PlacementUsesSurface
        ?new Basis(CargoPalmBasis.GetRotationQuaternion().Slerp((Torso.Basis.Inverse()*CargoPalmBasis).GetRotationQuaternion(),Smooth(_floorCrouch)))
        :CargoPalmBasis;

    private void SetCargoReach(float delta)
    {
        float reach=_bareBlend>0&&CompatibleBareActions(_bareAction,ActionKind.Deposit)
            ?ReachPulse((_bareTime%1.8f)/1.8f)*Smooth(_bareBlend):0;
        if(_placingCargo)
        {
            _placementAge+=delta;
            float settle=ReachEase(_placementAge/.52f);
            _cargoLift=Mathf.Lerp(_placementStart.X,-.043f,settle);
            _cargoDepth=Mathf.Lerp(_placementStart.Y,.050f,settle);
        }
        else
        {
            _cargoLift=reach*(IsPlacement(_bareAction)?-.023f:.052f);
            _cargoDepth=reach*.036f;
        }
    }

    private bool RetainCargoDuringTuck(bool wanted,float delta)
    {
        if(wanted&&!_stowingCargo)
        {
            _cargoTuck=Mathf.MoveToward(_cargoTuck,0,delta/.32f);
            return true;
        }
        if(_stowingCargo||_hasCargo&&SupportedCargo!=ResourceKind.None&&!_placingCargo&&(!_placementPending||WaitingForPlacementParcel))
        {
            _cargoTuck=MathF.Min(1.6f,_cargoTuck+delta/.38f);
            if(_cargoTuck<1)return true;
            const float handTolerance=.037f;
            bool retain=_cargoTuck<1.6f&&((_previousHands[0]*PalmOffset(0)).DistanceTo(CargoSupportTarget(0))>handTolerance
                ||(_previousHands[1]*PalmOffset(1)).DistanceTo(CargoSupportTarget(1))>handTolerance);
            if(!retain)_stowingCargo=false;
            return retain;
        }
        if(_cargoBlend<=.001f)_cargoTuck=0;
        return false;
    }

    internal Vector3 CargoSupportTarget(int side)
    {
        var (left,right)=CargoSupports[_cargoKind];
        if(_placingCargo&&PlacementUsesSurface)
        {
            float descent=ReachEase((_placementAge-.08f)/1.04f);
            var parcel=_floorStart.InterpolateWith(_floorGoal,descent);
            parcel.Origin+=_floorCorrection;
            var hand=(RigWorldSpace*Torso.Transform).AffineInverse()*(parcel*(side==0?left:right));
            // Use the last actual wrist basis. The anatomical wrist limit
            // can turn the underside contact away from the desired pose.
            return hand+_previousHands[side].Basis*(Grip!.Position-CargoPalmContact);
        }
        float tuck=ReachEase(_cargoTuck);
        var centre=BodyPoint(new(0,.887f+_cargoLift-.087f*tuck,-.292f-_cargoDepth+.074f*tuck));
        var support=(side==0?left:right)-(left+right)*.5f;
        return centre+support+CargoPalmBasis*(Grip!.Position-CargoPalmContact);
    }

    private void UpdateCarriedObject(float delta,bool wanted)
    {
        if(_releasedCargo)
        {
            UpdateReleasedCargo(delta);
            return;
        }
        if(!wanted||_cargoBlend<.999f)
        {
            CargoMesh.Visible=false;
            return;
        }
        var leftHand=Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform;
        var rightHand=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform;
        var left=leftHand*CargoPalmContact;var right=rightHand*CargoPalmContact;
        bool supported=(leftHand*PalmOffset(0)).DistanceTo(CargoSupportTarget(0))<.025f
            &&(rightHand*PalmOffset(1)).DistanceTo(CargoSupportTarget(1))<.025f;
        var (a,b)=CargoSupports[_cargoKind];
        var palmSpan=right-left;var meshSpan=b-a;
        float error=MathF.Abs(palmSpan.Length()-meshSpan.Length())*.5f;
        CargoMesh.Visible=(CargoMesh.Visible||supported)&&error<.021f;
        if(CargoMesh.Visible)
        {
            var basis=new Basis(new Quaternion(meshSpan.Normalized(),palmSpan.Normalized()));
            var origin=(left+right)*.5f-basis*((a+b)*.5f);
            CargoMesh.Transform=rightHand.AffineInverse()*new Transform3D(basis,origin);
        }
        if(!_placingCargo)return;
        bool onFloor=true;
        if(PlacementUsesSurface)
        {
            var pose=HandWorldPose(1)*CargoMesh.Transform;
            var p=pose.Origin;
            float ground=_groundPlacement?CargoGroundHeight?.Invoke(p.X,p.Z)??RigWorldSpace.Origin.Y:_placementSurface!.Value.Y;
            float gap=p.Y+CargoMesh.Mesh.GetAabb().Position.Y*pose.Basis.Y.Length()-ground;
            if(CargoMesh.Visible&&(_placementAge>=1.12f||_placementAge>=.86f&&gap<.014f))
            {
                float verticalStep=delta*(_groundPlacement?.13f:.19f);
                _floorCorrection.Y=Mathf.Clamp(_floorCorrection.Y+Math.Clamp(.014f-gap,-verticalStep,verticalStep),_groundPlacement?-.09f:-.14f,.12f);
                if(_placementSurface.HasValue)
                {
                    var offset=_floorGoal.Origin-p;
                    var planar=new Vector2(_floorCorrection.X,_floorCorrection.Z);
                    planar=(planar+new Vector2(offset.X,offset.Z).LimitLength(delta*.16f)).LimitLength(.11f);
                    _floorCorrection.X=planar.X;
                    _floorCorrection.Z=planar.Y;
                }
            }
            onFloor=gap is >=0 and <=.038f&&(!_placementSurface.HasValue
                ||new Vector2(p.X-_floorGoal.Origin.X,p.Z-_floorGoal.Origin.Z).LengthSquared()<.065f*.065f);
        }
        float readyAt=PlacementUsesSurface?1.12f:.52f;
        // The parcel follows the solved palms. During a surface placement,
        // the span error measures the actual two underside contacts after IK.
        bool holdingParcel=PlacementUsesSurface?error<.021f:supported;
        _placementContact=holdingParcel&&CargoMesh.Visible&&onFloor&&_placementAge>=readyAt?_placementContact+delta:0;
        if(_placementContact>=.085f||_placementAge>=(PlacementUsesSurface?2.25f:1.25f))
        {
            bool releasedAtContact=_placementContact>=.085f;
            _placingCargo=false;
            if(releasedAtContact)
            {
                _releasedCargo=true;
                if(_hasCargo)_cargoBlend=0;
                _releasedOntoSurface=PlacementUsesSurface;
                _releasedCargoTime=0;
                var actor=IsInsideTree()?GlobalTransform:Transform;
                _releasedCargoPose=actor*Torso.Transform*rightHand*CargoMesh.Transform;
                var position=_releasedCargoPose.Origin;
                if(_releasedOntoSurface)
                {
                    float ground=_groundPlacement?CargoGroundHeight?.Invoke(position.X,position.Z)??actor.Origin.Y:_placementSurface!.Value.Y;
                    _releasedCargoFloor=ground-CargoMesh.Mesh.GetAabb().Position.Y*_releasedCargoPose.Basis.Y.Length();
                    _releasedCargoDuration=MathF.Sqrt(MathF.Max(0,position.Y-_releasedCargoFloor)/4.9f);
                }
                else _releasedCargoDuration=.08f;
            }
            else CargoMesh.Visible=false;
        }
    }

    private void UpdateReleasedCargo(float delta)
    {
        _releasedCargoTime+=delta;
        var actor=IsInsideTree()?GlobalTransform:Transform;
        var hand=actor*Torso.Transform*Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform;
        var pose=_releasedCargoPose;
        if(_releasedOntoSurface)
        {
            var orientation=pose.Basis.Orthonormalized();
            float yaw=orientation.GetEuler().Y;
            var grounded=new Basis(Vector3.Up,yaw);
            pose.Basis=orientation.Slerp(grounded,ReachEase(_releasedCargoTime/.27f))*Basis.FromScale(pose.Basis.Scale);
            float height=pose.Origin.Y-4.9f*_releasedCargoTime*_releasedCargoTime;
            pose.Origin=new(pose.Origin.X,MathF.Max(_releasedCargoFloor,height),pose.Origin.Z);
        }
        // A warehouse handover has already been accepted by the inventory.
        // Keep the parcel on its release pose for the visual handoff.
        CargoMesh.Transform=hand.AffineInverse()*pose;
        CargoMesh.Visible=_releasedCargoTime<_releasedCargoDuration+.12f;
        if(!CargoMesh.Visible)_releasedCargo=false;
    }

    internal void ClearTransientCargo()
    {
        if(_placingCargo||_placementPending||_releasedCargo||_stowingCargo||_cargoTuck>0)CargoMesh.Visible=false;
        _placingCargo=_placementPending=_releasedCargo=_stowingCargo=false;
        _cargoTuck=0;
    }
}
