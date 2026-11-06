using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private sealed record SceneHandoff(long Giver,long Receiver,Vector2I GiverTile,Vector2I ReceiverTile,
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
            &&agents.ActionOf(slot) is not (ActionKind.Flee or ActionKind.Attack);
    }

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
            if(!TrySceneResident(pair.Giver,out var giver)||!TrySceneResident(pair.Receiver,out var receiver)
                ||!giver.Visible||!receiver.Visible||giver.Position.DistanceSquaredTo(camera)>22*22
                ||receiver.Position.DistanceSquaredTo(camera)>22*22)
            {
                pair.Motion.Cancel();
            }
            else if(!CanJoinHandoff(unchecked((int)pair.Giver))||!CanJoinHandoff(unchecked((int)pair.Receiver))
                ||HandoffTile(unchecked((int)pair.Giver))!=pair.GiverTile||HandoffTile(unchecked((int)pair.Receiver))!=pair.ReceiverTile)
            {
                pair.Motion.Interrupt();
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
                ||agents.ActionOf(slot)!=ActionKind.ShareFood||agents.PhaseOf(slot)!=ActionPhase.Done)continue;
            uint revision=agents.ActionRevisionOf(slot);
            if(giver.LastSharePresentation==revision)continue;
            giver.LastSharePresentation=revision;
            if(giver.HandoffReserved||giver.IsChild||!giver.Visible||giver.Position.DistanceSquaredTo(camera)>18*18
                ||!sim.Actions.TryGetPresentationPartner(slot,out int other)||!CanJoinHandoff(other))continue;
            long receiverId=sim.Society.Identity(other);
            if(!TrySceneResident(receiverId,out var receiver)||receiver.HandoffReserved||receiver.IsChild||!receiver.Visible
                ||receiver.Position.DistanceSquaredTo(camera)>18*18||!HandoffStands(giver,receiver,out var give,out var take))continue;
            _residentHandoffs.Add(new(identity,receiverId,HandoffTile(slot),HandoffTile(other),give,take,new(giver,receiver)));
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
