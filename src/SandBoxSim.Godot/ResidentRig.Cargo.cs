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
    private float _cargoDepth,_placementAge,_placementContact;
    private Vector2 _placementStart;
    private bool _placingCargo;
    internal bool PlacingCargo=>_placingCargo;
    private bool _releasedCargo;
    private Transform3D _releasedCargoPose;
    private float _releasedCargoTime,_releasedCargoFloor,_releasedCargoDuration;
    internal bool ReleasedCargo=>_releasedCargo;
    internal Func<float,float,float>? CargoGroundHeight;

    private static bool IsPlacement(ActionKind action)=>action is ActionKind.Deposit or ActionKind.StoreInBuilding;

    private void ObserveCargoPlacement(ActionKind action,ActionPhase phase,bool newlyDone)
    {
        if(!IsPlacement(action)||action!=_observedAction||phase!=ActionPhase.Done||_hasCargo||!InteractionSettled)
            _placingCargo=false;
        if(newlyDone&&IsPlacement(action)&&!_hasCargo&&CargoMesh.Visible&&!_releasedCargo&&InteractionSettled)
        {
            // Inventory has already committed. Keep only the already
            // displayed parcel, for the time needed to finish its set-down.
            _placingCargo=true;
            _placementAge=_placementContact=0;
            _placementStart=new(_cargoLift,_cargoDepth);
        }
    }

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

    internal Vector3 CargoSupportTarget(int side)
    {
        var (left,right)=CargoSupports[_cargoKind];
        var centre=BodyPoint(new(0,.887f+_cargoLift,-.292f-_cargoDepth));
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
        bool supported=(leftHand*Grip!.Position).DistanceTo(CargoSupportTarget(0))<.025f
            &&(rightHand*Grip.Position).DistanceTo(CargoSupportTarget(1))<.025f;
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
        _placementContact=supported&&CargoMesh.Visible&&_placementAge>=.52f?_placementContact+delta:0;
        if(_placementContact>=.085f||_placementAge>=1.25f)
        {
            bool releasedAtContact=_placementContact>=.085f;
            _placingCargo=false;
            if(releasedAtContact)
            {
                _releasedCargo=true;
                _releasedCargoTime=0;
                var actor=IsInsideTree()?GlobalTransform:Transform;
                _releasedCargoPose=actor*Torso.Transform*rightHand*CargoMesh.Transform;
                var position=_releasedCargoPose.Origin;
                float ground=CargoGroundHeight?.Invoke(position.X,position.Z)??actor.Origin.Y;
                _releasedCargoFloor=ground-CargoMesh.Mesh.GetAabb().Position.Y*_releasedCargoPose.Basis.Y.Length();
                _releasedCargoDuration=MathF.Sqrt(MathF.Max(0,position.Y-_releasedCargoFloor)/4.9f);
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
        var orientation=pose.Basis.Orthonormalized();
        float yaw=orientation.GetEuler().Y;
        var grounded=new Basis(Vector3.Up,yaw);
        pose.Basis=orientation.Slerp(grounded,ReachEase(_releasedCargoTime/.27f))*Basis.FromScale(pose.Basis.Scale);
        float height=pose.Origin.Y-4.9f*_releasedCargoTime*_releasedCargoTime;
        pose.Origin=new(pose.Origin.X,MathF.Max(_releasedCargoFloor,height),pose.Origin.Z);
        CargoMesh.Transform=hand.AffineInverse()*pose;
        CargoMesh.Visible=_releasedCargoTime<_releasedCargoDuration+.12f;
        if(!CargoMesh.Visible)_releasedCargo=false;
    }

    internal void ClearTransientCargo()
    {
        if(_placingCargo||_releasedCargo)CargoMesh.Visible=false;
        _placingCargo=_releasedCargo=false;
    }
}
