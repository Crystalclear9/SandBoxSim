using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private sealed record SceneHandoff(long Giver,long Receiver,uint Revision,uint ReceiverRevision,Vector2I GiverTile,Vector2I ReceiverTile,
        Vector3 GiverStand,Vector3 ReceiverStand,ResidentHandoff Motion);
    private readonly List<SceneHandoff> _residentHandoffs=new();

    private bool TrySceneResident(long identity,out ResidentRig rig)
    {
        int slot=unchecked((int)identity);
        var sim=Game.Sim;
        if(sim.Agents.IsSlotAlive(slot)&&sim.Society.Identity(slot)==identity
            &&_people.TryGetValue(identity,out var person)&&person is ResidentRig resident&&GodotObject.IsInstanceValid(resident))
        {
            rig=resident;return true;
        }
        rig=null!;return false;
    }

    private Vector2I HandoffTile(int slot)=>new(Game.Sim.Agents.XOf(slot),Game.Sim.Agents.YOf(slot));

    private bool CanJoinHandoff(int slot)
    {
        var agents=Game.Sim.Agents;
        return agents.PhaseOf(slot) is ActionPhase.Idle or ActionPhase.Done
            &&agents.ActionOf(slot) is not (ActionKind.Flee or ActionKind.Attack or ActionKind.Hunt or ActionKind.Sleep);
    }

    private readonly record struct PairCandidate(int Partner,uint Revision,uint PartnerRevision,bool Confirmed);

    private bool TryPairCandidate(int slot,ActionKind action,ResidentRig rig,Vector3 camera,float distanceSquared,
        out PairCandidate candidate)
    {
        candidate=default;
        var sim=Game.Sim;var agents=sim.Agents;
        var live=agents.ActionOf(slot);
        var phase=agents.PhaseOf(slot);
        uint revision=agents.ActionRevisionOf(slot);
        bool executing=live==action&&phase==ActionPhase.Executing;
        ActionSystem.PartnerCompletion receipt=default;
        if(!executing)
        {
            if(live==action&&phase==ActionPhase.Done)
            {
                if(!sim.Actions.TryGetPartnerCompletion(slot,revision,out receipt))return false;
            }
            else if(live!=ActionKind.None||phase!=ActionPhase.Idle
                ||!sim.Actions.TryGetLatestPartnerCompletion(slot,out receipt)||!IsQuietPairSuccessor(agents,slot,receipt.Revision))
                return false;
            long age=sim.Clock-receipt.Tick;
            if(receipt.Action!=action||age<0||age>sim.Config.Clock.MaxCatchUpTicksPerFrame)return false;
        }
        if(!rig.Visible||rig.Position.DistanceSquaredTo(camera)>distanceSquared)return false;
        if(executing)
        {
            if(!sim.Actions.TryGetInteractionPartner(slot,out int other))return false;
            candidate=new(other,revision,agents.ActionRevisionOf(other),false);
            return true;
        }
        int partner=receipt.Partner.Slot;
        if(!PairSitesMatch(slot,receipt)||!PairRecipientAvailable(agents,partner,receipt.PartnerRevision))return false;
        candidate=new(partner,receipt.Revision,receipt.PartnerRevision,true);
        return true;
    }

    private bool PairSitesMatch(int slot,ActionSystem.PartnerCompletion receipt)
        =>HandoffTile(slot)==new Vector2I(receipt.Origin.X,receipt.Origin.Y)
            &&HandoffTile(receipt.Partner.Slot)==new Vector2I(receipt.PartnerTile.X,receipt.PartnerTile.Y);

    private bool HasPairReceipt(int slot,int partner,ActionKind action,uint revision)
    {
        return Game.Sim.Actions.TryGetPartnerCompletion(slot,revision,out var receipt)&&receipt.Action==action
            &&receipt.Partner.Slot==partner&&PairSitesMatch(slot,receipt);
    }

    private bool PairInitiatorAvailable(int slot,int partner,ActionKind action,uint revision)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        uint live=agents.ActionRevisionOf(slot);
        if(live==revision&&agents.ActionOf(slot)==action)
            return agents.PhaseOf(slot)==ActionPhase.Executing
                ?sim.Actions.TryGetInteractionPartner(slot,out int actual)&&actual==partner
                :agents.PhaseOf(slot)==ActionPhase.Done&&HasPairReceipt(slot,partner,action,revision);
        // An existing scene has its own playback timeout. Quiet decisions
        // can follow a confirmed gesture without restarting that timeout.
        return IsQuietPairSuccessor(agents,slot,revision)&&HasPairReceipt(slot,partner,action,revision);
    }

    private static bool IsQuietPairSuccessor(AgentStore agents,int slot,uint revision)
        =>unchecked(agents.ActionRevisionOf(slot)-revision) is >0 and <=ActionSystem.MaxPresentationRevisionGap
            &&agents.ActionOf(slot)==ActionKind.None&&agents.PhaseOf(slot)==ActionPhase.Idle;

    private static bool PairRecipientAvailable(AgentStore agents,int slot,uint revision)
        =>agents.ActionRevisionOf(slot)==revision||IsQuietPairSuccessor(agents,slot,revision);

    private bool HandoffStands(ResidentRig giver,ResidentRig receiver,out Vector3 give,out Vector3 take)
    {
        var between=Horizontal(receiver.Position-giver.Position);
        float distance=between.Length();
        give=take=Vector3.Zero;
        if(distance>2.35f)return false;
        var direction=distance>.04f?between/distance:Horizontal(-giver.Basis.Z).Normalized();
        var centre=(giver.Position+receiver.Position)*.5f;
        var offset=new Vector3(direction.X,0,direction.Y)*.46f;
        give=centre-offset;take=centre+offset;
        give.Y=GroundHeight(give.X,give.Z);take.Y=GroundHeight(take.X,take.Z);
        return ValidHandoffStand(giver.Position,give)&&ValidHandoffStand(receiver.Position,take)
            &&System.MathF.Abs(give.Y-take.Y)<.16f&&DryActorTarget(centre)&&ScenerySegmentClear(give,take);
    }

    private bool ValidHandoffStand(Vector3 from,Vector3 stand)
        =>DryActorTarget(stand)&&SceneryPointClear(stand,SceneryClearance)&&ScenerySegmentClear(from,stand)
            &&SceneryLegSupported(from,stand);

    private void PrepareResidentHandoffs(Vector3 camera)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        for(int i=_residentHandoffs.Count-1;i>=0;i--)
        {
            var pair=_residentHandoffs[i];
            int giverSlot=unchecked((int)pair.Giver),receiverSlot=unchecked((int)pair.Receiver);
            long holder=pair.Motion.Transferred?pair.Receiver:pair.Giver;
            if(!TrySceneResident(holder,out var owner)||!owner.Visible||owner.Position.DistanceSquaredTo(camera)>22*22)
            {
                pair.Motion.Cancel();
            }
            else if(pair.Motion.UsesMeeting)
            {
                if(!TrySceneResident(pair.Receiver,out var receiver)||!receiver.Visible||receiver.Position.DistanceSquaredTo(camera)>22*22
                    ||!PairInitiatorAvailable(giverSlot,receiverSlot,ActionKind.ShareFood,pair.Revision)||!CanJoinHandoff(receiverSlot)
                    ||!PairRecipientAvailable(agents,receiverSlot,pair.ReceiverRevision)
                    ||HandoffTile(giverSlot)!=pair.GiverTile||HandoffTile(receiverSlot)!=pair.ReceiverTile
                    ||!ValidHandoffStand(owner.Position,pair.GiverStand)||!ValidHandoffStand(receiver.Position,pair.ReceiverStand)
                    ||!ScenerySegmentClear(pair.GiverStand,pair.ReceiverStand))
                    pair.Motion.Interrupt();
                else if(HasPairReceipt(giverSlot,receiverSlot,ActionKind.ShareFood,pair.Revision))pair.Motion.Confirm();
            }
            if(pair.Motion.Finished)_residentHandoffs.RemoveAt(i);
            else pair.Motion.Prepare();
        }
        if(_residentHandoffs.Count>=4)return;
        foreach(var (identity,node) in _people)
        {
            int slot=unchecked((int)identity);
            var giver=(ResidentRig)node;
            if(!agents.IsSlotAlive(slot)||sim.Society.Identity(slot)!=identity
                ||!TryPairCandidate(slot,ActionKind.ShareFood,giver,camera,18*18,out var candidate))continue;
            if(giver.HandoffReserved||giver.RecoveringPickup||giver.CompletionVisible||FindConversation(identity)!=null
                ||giver.IsChild||!CanJoinHandoff(candidate.Partner))continue;
            int other=candidate.Partner;
            uint revision=candidate.Revision;
            long receiverId=sim.Society.Identity(other);
            if(giver.LastSharePresentation==(revision,receiverId)||FindConversation(receiverId)!=null
                ||!TrySceneResident(receiverId,out var receiver)||receiver.HandoffReserved||receiver.RecoveringPickup
                ||receiver.CompletionVisible||receiver.IsChild||!receiver.Visible
                ||receiver.Position.DistanceSquaredTo(camera)>18*18||!HandoffStands(giver,receiver,out var give,out var take))continue;
            giver.LastSharePresentation=(revision,receiverId);
            _residentHandoffs.Add(new(identity,receiverId,revision,candidate.PartnerRevision,HandoffTile(slot),HandoffTile(other),
                give,take,new(giver,receiver,candidate.Confirmed)));
            if(_residentHandoffs.Count>=4)break;
        }
    }

    private bool TryHandoffMeeting(long identity,out Vector3 stand,out Vector3 facing)
    {
        foreach(var pair in _residentHandoffs)
        {
            if(!pair.Motion.UsesMeeting)continue;
            if(pair.Giver==identity){stand=pair.GiverStand;facing=pair.ReceiverStand;return true;}
            if(pair.Receiver==identity){stand=pair.ReceiverStand;facing=pair.GiverStand;return true;}
        }
        stand=facing=Vector3.Zero;return false;
    }

    private void ClearResidentHandoffs()
    {
        foreach(var pair in _residentHandoffs)pair.Motion.Cancel();
        _residentHandoffs.Clear();
    }
}
