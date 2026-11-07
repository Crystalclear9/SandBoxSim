using System;
using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

/// <summary>One presentation-only parcel, advanced after both participants have posed.</summary>
internal sealed class ResidentHandoff
{
    private enum Stage { Approach, Offer, Meet, Receive, Recover, Done }
    private const int GivingHand=1,ReceivingHand=0;
    private const float ParcelScale=.68f;
    private readonly ResidentRig _giver,_receiver;
    private readonly MeshInstance3D _parcel;
    private readonly Vector3 _givingSupport,_receivingSupport;
    private Stage _stage;
    private Basis _basis=Basis.Identity;
    private Vector3 _transferOffset,_transferVelocity,_previousReceiverPalm;
    private Vector3 _returnStart;
    private float _time,_contact;
    private bool _receiverOwns;
    private bool _confirmed,_giverReserved=true,_receiverReserved=true;

    internal ResidentHandoff(ResidentRig giver,ResidentRig receiver,bool confirmed=true)
    {
        _giver=giver;_receiver=receiver;
        _confirmed=confirmed;
        var supports=giver.CargoSupports[ResourceKind.Food];
        _givingSupport=supports.Left;_receivingSupport=supports.Right;
        _parcel=new MeshInstance3D {Name="SharedFoodParcel",Mesh=giver.CargoMeshes[ResourceKind.Food],Visible=false};
        giver.Hands[GivingHand].AddChild(_parcel);
        Prepare();
    }

    internal bool Finished=>_stage==Stage.Done;
    internal bool UsesMeeting=>_stage is Stage.Approach or Stage.Offer or Stage.Meet;
    internal bool ParcelVisible=>!Finished&&_parcel.Visible;
    internal bool Transferred {get;private set;}
    internal Transform3D ParcelPose {get;private set;}
    internal float ReceiverGap=>Palm(_receiver,ReceivingHand).DistanceTo(ParcelPose*_receivingSupport);
    internal float OwnerGap=>_receiverOwns?ReceiverGap:Palm(_giver,GivingHand).DistanceTo(ParcelPose*_givingSupport);
    private static Vector3 Palm(ResidentRig rig,int side)=>rig.HandWorldPose(side)*ResidentRig.CargoPalmContact;
    internal void Confirm()=>_confirmed=true;

    internal void Prepare()
    {
        if(Finished)return;
        if(!UsesMeeting)
        {
            var holder=_receiverOwns?_receiver:_giver;
            int hand=_receiverOwns?ReceivingHand:GivingHand;
            float weight=ResidentRig.ReachEase(_time/.46f);
            holder.PlanHandoff(hand,_returnStart.Lerp(holder.HandoffWaist(hand),weight),.67f,
                _receiverOwns?"接稳 / 收回":"收回食物",recovery:true);
            return;
        }
        Vector3 giver=_giver.RigWorldSpace.Origin,receiver=_receiver.RigWorldSpace.Origin;
        var direction=receiver-giver;direction.Y=0;
        float distance=direction.Length();
        bool awaitingConfirmation=false;
        if(_stage==Stage.Approach&&distance is >=.60f and <=1.17f&&_giver.InteractionSettled&&_receiver.InteractionSettled)
        {
            direction/=distance;
            var forward=-_giver.RigWorldSpace.Basis.Z.Normalized();
            var backward=-_receiver.RigWorldSpace.Basis.Z.Normalized();
            if(forward.Dot(direction)>.93f&&backward.Dot(-direction)>.93f)
            {
                if(_confirmed)
                {
                    _basis=new Basis(direction,Vector3.Up,direction.Cross(Vector3.Up)).Scaled(Vector3.One*ParcelScale);
                    Next(Stage.Offer);
                }
                else awaitingConfirmation=true;
            }
        }
        var midpoint=(giver+receiver)*.5f+Vector3.Up*(.94f*(_giver.Scale.Y+_receiver.Scale.Y)*.5f);
        // Both right/left hands are on the same side of the line between
        // actors, on opposite faces of the actual parcel.
        midpoint+=_basis.Z*.09f;
        var offer=_giver.HandoffWaist(GivingHand);
        Vector3? giverTarget=_stage switch
        {
            Stage.Offer=>offer,
            Stage.Meet=>midpoint+_basis*_givingSupport,
            _=>null
        };
        Vector3? receiverTarget=_stage switch
        {
            Stage.Meet when _time>=.16f&&ParcelVisible=>ParcelPose*_receivingSupport,
            _=>null
        };
        string approach=awaitingConfirmation?"已就位 / 等待确认":"走近交接";
        _giver.PlanHandoff(GivingHand,giverTarget,ParcelVisible?.67f:.10f,
            _stage==Stage.Approach?approach:_stage==Stage.Offer?"拿稳食物":"递出 / 等待接住");
        _receiver.PlanHandoff(ReceivingHand,receiverTarget,.16f,
            _stage==Stage.Approach?approach:_stage==Stage.Meet?"伸手接取":"等待递物");
    }

    internal void Advance(float delta)
    {
        if(Finished||!float.IsFinite(delta)||delta<=0)return;
        // A skipped render frame cannot certify several contact samples.
        delta=MathF.Min(delta,1f/30);
        _time+=delta;
        if(_stage==Stage.Approach){if(_time>=3.5f)Cancel();return;}
        if(_stage==Stage.Offer)
        {
            bool ready=_giver.HandoffHandReady&&Palm(_giver,GivingHand).DistanceTo(_giver.HandoffWaist(GivingHand))<.032f;
            _contact=ready?_contact+delta:0;
            if(_contact>=.12f){_parcel.Visible=true;Next(Stage.Meet);}
            else if(_time>=2.6f){Cancel();return;}
        }
        if(!ParcelVisible)return;
        var holder=_receiverOwns?_receiver:_giver;
        int side=_receiverOwns?ReceivingHand:GivingHand;
        var support=_receiverOwns?_receivingSupport:_givingSupport;
        var currentPalm=Palm(holder,side);
        var priorParcel=ParcelPose.Origin;
        if(_receiverOwns)
            ResidentRig.DampMotion(ref _transferOffset,ref _transferVelocity,Vector3.Zero,.075f,delta);
        ParcelPose=new Transform3D(_basis,currentPalm-_basis*support+_transferOffset);
        _parcel.Transform=holder.HandWorldPose(side).AffineInverse()*ParcelPose;

        if(_stage==Stage.Meet)
        {
            var receiverPalm=Palm(_receiver,ReceivingHand);
            var receiverSupport=ParcelPose*_receivingSupport;
            bool bothReady=_giver.HandoffHandReady&&_receiver.HandoffHandReady&&receiverPalm.DistanceTo(receiverSupport)<.018f;
            _contact=bothReady?_contact+delta:0;
            if(_contact>=.10f)
            {
                // Match the parcel's outgoing position and velocity relative
                // to the receiving palm before fading to its support point.
                _transferOffset=receiverSupport-receiverPalm;
                _transferVelocity=((ParcelPose.Origin-priorParcel-receiverPalm+_previousReceiverPalm)/delta).LimitLength(.30f);
                _returnStart=receiverPalm;
                _parcel.Reparent(_receiver.Hands[ReceivingHand],false);
                _parcel.Transform=_receiver.HandWorldPose(ReceivingHand).AffineInverse()*ParcelPose;
                _receiverOwns=Transferred=true;
                Next(Stage.Receive);
                Release(_giver,ref _giverReserved);
            }
            else if(_time>=2.5f)Interrupt();
            _previousReceiverPalm=receiverPalm;
        }
        else if(_stage is Stage.Receive or Stage.Recover)
        {
            bool down=holder.HandoffHandReady&&currentPalm.DistanceTo(holder.HandoffWaist(side))<.045f;
            _contact=down?_contact+delta:0;
            if(_contact>=.20f||_time>=1.65f)Cancel();
        }
    }

    internal void Interrupt()
    {
        if(Finished||_stage==Stage.Recover)return;
        if(ParcelVisible)
        {
            _returnStart=_receiverOwns?Palm(_receiver,ReceivingHand):Palm(_giver,GivingHand);
            Next(Stage.Recover);
            if(_receiverOwns)Release(_giver,ref _giverReserved);
            else Release(_receiver,ref _receiverReserved);
        }
        else Cancel();
    }

    internal void Cancel()
    {
        if(Finished)return;
        _stage=Stage.Done;
        Release(_giver,ref _giverReserved);
        Release(_receiver,ref _receiverReserved);
        if(GodotObject.IsInstanceValid(_parcel))
        {
            _parcel.Visible=false;
            if(_parcel.IsInsideTree())_parcel.QueueFree();else _parcel.Free();
        }
    }

    private static void Release(ResidentRig rig,ref bool reserved)
    {
        if(!reserved)return;
        reserved=false;
        if(GodotObject.IsInstanceValid(rig))rig.PlanHandoff(-1,null,0,"");
    }

    private void Next(Stage stage)
    {
        _stage=stage;
        _time=_contact=0;
    }
}
