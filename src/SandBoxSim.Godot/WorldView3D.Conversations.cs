using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private sealed class SceneConversation(long initiator,long recipient,uint revision,uint recipientRevision,
        Vector2I initiatorTile,Vector2I recipientTile,Vector3? firstStand,Vector3? secondStand)
    {
        internal readonly long Initiator=initiator,Recipient=recipient;
        internal readonly uint Revision=revision;
        internal readonly uint RecipientRevision=recipientRevision;
        internal readonly Vector2I InitiatorTile=initiatorTile,RecipientTile=recipientTile;
        internal readonly Vector3? FirstStand=firstStand,SecondStand=secondStand;
        internal float Time,Waiting;
    }

    private readonly List<SceneConversation> _residentConversations=new();
    private bool CanConverse(int slot,int other,ResidentRig rig)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        if(!rig.Visible||rig.HandoffReserved||rig.RecoveringPickup)return false;
        if(agents.PhaseOf(slot)==ActionPhase.Executing)
            return agents.ActionOf(slot)==ActionKind.Socialize&&sim.Actions.TryGetPresentationPartner(slot,out int partner)&&partner==other;
        return (agents.PhaseOf(slot) is ActionPhase.Idle or ActionPhase.Done)&&!rig.CompletionVisible
            &&agents.ActionOf(slot) is not (ActionKind.Flee or ActionKind.Attack or ActionKind.Sleep);
    }

    private bool ConversationPathClear(Vector3 a,Vector3 b)
        =>MathF.Abs(a.Y-b.Y)<.38f&&Horizontal(a-b).LengthSquared()<=2.6f*2.6f&&ScenerySegmentClear(a,b);

    private void PrepareResidentConversations(float delta,Vector3 camera)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        for(int i=_residentConversations.Count-1;i>=0;i--)
        {
            var pair=_residentConversations[i];
            int first=unchecked((int)pair.Initiator),second=unchecked((int)pair.Recipient);
            if(!TrySceneResident(pair.Initiator,out var a)||!TrySceneResident(pair.Recipient,out var b)
                ||!PairInitiatorAvailable(first,second,ActionKind.Socialize,pair.Revision)
                ||!PairRecipientAvailable(agents,second,pair.RecipientRevision)
                ||HandoffTile(first)!=pair.InitiatorTile||HandoffTile(second)!=pair.RecipientTile
                ||!CanConverse(first,second,a)||!CanConverse(second,first,b)
                ||a.Position.DistanceSquaredTo(camera)>19*19||b.Position.DistanceSquaredTo(camera)>19*19
                ||!ConversationPathClear(a.Position,b.Position)
                ||pair.FirstStand is {} firstStand&&!ValidHandoffStand(a.Position,firstStand)
                ||pair.SecondStand is {} secondStand&&!ValidHandoffStand(b.Position,secondStand))
            {
                _residentConversations.RemoveAt(i);
                continue;
            }
            var line=Horizontal(b.Position-a.Position);
            bool facing=line.LengthSquared()>=.60f*.60f&&a.InteractionSettled&&b.InteractionSettled
                &&InteractionFacingAligned(a.Rotation.Y,line)&&InteractionFacingAligned(b.Rotation.Y,-line);
            bool ready=facing&&HasPairReceipt(first,second,ActionKind.Socialize,pair.Revision);
            float step=Math.Clamp(delta,0,1f/30);
            pair.Waiting=ready?0:pair.Waiting+step;
            if(ready)pair.Time+=step;
            if(pair.Time>=3.25f||pair.Waiting>=2.7f)_residentConversations.RemoveAt(i);
        }
        if(_residentConversations.Count>=4)return;
        foreach(var (id,node) in _people)
        {
            int slot=unchecked((int)id);
            var a=(ResidentRig)node;
            if(!agents.IsSlotAlive(slot)||sim.Society.Identity(slot)!=id
                ||!TryPairCandidate(slot,ActionKind.Socialize,a,camera,17*17,out var candidate))continue;
            int other=candidate.Partner;
            long otherId=sim.Society.Identity(other);
            uint revision=candidate.Revision;
            if(a.LastConversation==(revision,otherId)||FindConversation(id)!=null||FindConversation(otherId)!=null
                ||!TrySceneResident(otherId,out var b)||!CanConverse(slot,other,a)||!CanConverse(other,slot,b)
                ||b.Position.DistanceSquaredTo(camera)>17*17||!ConversationPathClear(a.Position,b.Position))continue;
            Vector3? aStand=null,bStand=null;
            if(Horizontal(a.Position-b.Position).LengthSquared()<.65f*.65f)
            {
                if(!HandoffStands(a,b,out var standA,out var standB))continue;
                aStand=standA;bStand=standB;
            }
            a.LastConversation=(revision,otherId);
            if(TryPairCandidate(other,ActionKind.Socialize,b,camera,17*17,out var reciprocal)&&reciprocal.Partner==slot)
                b.LastConversation=(reciprocal.Revision,id);
            _residentConversations.Add(new(id,otherId,revision,candidate.PartnerRevision,HandoffTile(slot),HandoffTile(other),aStand,bStand));
            if(_residentConversations.Count>=4)break;
        }
    }

    private SceneConversation? FindConversation(long identity)
    {
        foreach(var pair in _residentConversations)
            if(pair.Initiator==identity||pair.Recipient==identity)return pair;
        return null;
    }

    private static ResidentRig.ConversationBeat ConversationBeat(SceneConversation pair,bool initiator)
    {
        if(pair.Waiting>0)return default;
        float t=pair.Time;
        float speechStart=initiator?.16f:1.62f,speechEnd=initiator?1.43f:2.96f;
        float nodStart=initiator?2.34f:.91f;
        float speech=ResidentRig.ReachEase((t-speechStart)/.19f)*(1-ResidentRig.ReachEase((t-speechEnd+.18f)/.18f));
        float nod=ResidentRig.ReachEase((t-nodStart)/.12f)*(1-ResidentRig.ReachEase((t-nodStart-.12f)/.18f));
        return new(speech,nod);
    }

    internal void ValidateSceneConversation()
    {
        const float dt=1f/60;
        var sim=Game.Sim;var agents=sim.Agents;
        var (aTile,bTile,otherTile)=FindResidentMotionCorner();
        if(sim.InterveneSpawnHumans(aTile.X,aTile.Y,1,1)!=1||sim.InterveneSpawnHumans(bTile.X,bTile.Y,1,1)!=1)
            throw new InvalidOperationException("Conversation fixture needs two residents");
        var slots=agents.AliveSlots().ToArray();
        int first=slots[0],second=slots[1];
        foreach(int slot in slots)
        {
            agents.SetLifeStage(slot,LifeStage.Adult);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        }
        agents.SetPosition(first,aTile.X,aTile.Y);agents.SetPosition(second,bTile.X,bTile.Y);
        SyncEntities();
        var a=(ResidentRig)_people[sim.Society.Identity(first)];
        var b=(ResidentRig)_people[sim.Society.Identity(second)];
        var between=Horizontal(b.Position-a.Position);
        a.Rotation=new(0,HeadingYaw(between)+1.5f,0);b.Rotation=new(0,HeadingYaw(-between)-1.4f,0);
        _target=(a.Position+b.Position)*.5f;_distance=8;_pitch=.62f;UpdateCamera();
        for(int frame=0;frame<12;frame++)AnimateActors(dt);

        void Confirm()
        {
            sim.Actions.Tick(sim.Clock);
            if(!sim.Actions.TryGetPresentationPartner(first,out int recipient)||recipient!=second)
                throw new InvalidOperationException("Socialize did not commit to the actual recipient");
        }
        agents.SetAction(first,ActionKind.Socialize,ActionPhase.Executing);
        string pending=sim.StateDigestString();
        int readyFrames=0;
        for(int frame=0;frame<150&&readyFrames<30;frame++)
        {
            AnimateActors(dt);
            var line=Horizontal(b.Position-a.Position);
            if(a.InteractionSettled&&b.InteractionSettled
                &&InteractionFacingAligned(a.Rotation.Y,line)&&InteractionFacingAligned(b.Rotation.Y,-line))readyFrames++;
            if(a.ConversationMouth>.02f||b.ConversationMouth>.02f)
                throw new InvalidOperationException("Residents speak before the simulation confirms the conversation");
        }
        if(readyFrames<30||_residentConversations.Count!=1||pending!=sim.StateDigestString())
            throw new InvalidOperationException("Unconfirmed residents cannot settle silently into a conversation");
        Confirm();
        string digest=sim.StateDigestString();
        int speechA=0,speechB=0,gap=0;
        float nodA=0,nodB=0;
        for(int frame=0;frame<360;frame++)
        {
            AnimateActors(dt);
            bool speaksA=a.ConversationMouth>.12f,speaksB=b.ConversationMouth>.12f;
            if(speaksA&&speaksB||speaksB&&speechA<30)throw new InvalidOperationException("Paired speech overlaps or replies before the opening phrase");
            if(speaksA)speechA++;
            if(speaksA&&speechA==20)
            {
                for(int decision=0;decision<3;decision++)
                {
                    agents.SetAction(first,ActionKind.None,ActionPhase.Idle);
                    agents.SetAction(second,ActionKind.None,ActionPhase.Idle);
                }
                digest=sim.StateDigestString();
            }
            if(speaksB)speechB++;
            if(speechA>0&&speechB==0&&!speaksA)gap++;
            if(speaksA||speaksB)
            {
                var line=Horizontal(b.Position-a.Position);
                if(!InteractionFacingAligned(a.Rotation.Y,line)||!InteractionFacingAligned(b.Rotation.Y,-line))
                    throw new InvalidOperationException("Residents speak before both are facing the conversation");
            }
            nodA=MathF.Max(nodA,a.ConversationNod);nodB=MathF.Max(nodB,b.ConversationNod);
        }
        if(speechA<45||speechB<45||gap is <7 or >31||nodA<.55f||nodB<.55f||_residentConversations.Count!=0||digest!=sim.StateDigestString())
            throw new InvalidOperationException($"Conversation: speech={speechA}/{speechB}, gap={gap}, nod={nodA:F2}/{nodB:F2}, active={_residentConversations.Count}");
        agents.SetAction(first,ActionKind.Socialize,ActionPhase.Executing);
        agents.SetAction(second,ActionKind.Socialize,ActionPhase.Executing);
        Confirm();
        for(int decision=0;decision<2;decision++)
        {
            agents.SetAction(first,ActionKind.None,ActionPhase.Idle);
            agents.SetAction(second,ActionKind.None,ActionPhase.Idle);
        }
        digest=sim.StateDigestString();
        int starts=0;
        bool replied=false,wasPaired=false;
        for(int frame=0;frame<400;frame++)
        {
            AnimateActors(dt);
            bool paired=_residentConversations.Count>0;
            if(paired&&!wasPaired)starts++;
            wasPaired=paired;
            replied|=b.ConversationMouth>.18f;
        }
        if(starts!=1||!replied||wasPaired||sim.StateDigestString()!=digest)
            throw new InvalidOperationException("Two reciprocal unobserved receipts fail to open exactly one conversation");

        agents.SetAction(first,ActionKind.Socialize,ActionPhase.Executing);
        Confirm();
        for(int frame=0;frame<90&&a.ConversationMouth<.18f;frame++)AnimateActors(dt);
        if(a.ConversationMouth<.18f)throw new InvalidOperationException("Second conversation never starts");
        agents.SetAction(second,ActionKind.Flee,ActionPhase.Moving);
        digest=sim.StateDigestString();
        for(int frame=0;frame<60;frame++)AnimateActors(dt);
        if(_residentConversations.Count!=0||a.ConversationMouth>.02f||b.ConversationMouth>.02f||sim.StateDigestString()!=digest)
            throw new InvalidOperationException("Interrupted conversation keeps talking or alters simulation");
        agents.SetAction(second,ActionKind.None,ActionPhase.Idle);
        agents.SetAction(first,ActionKind.Socialize,ActionPhase.Executing);
        Confirm();
        agents.SetAction(first,ActionKind.None,ActionPhase.Idle);
        agents.SetPosition(second,otherTile.X,otherTile.Y);
        digest=sim.StateDigestString();
        for(int frame=0;frame<110;frame++)
        {
            AnimateActors(dt);
            if(_residentConversations.Count!=0)throw new InvalidOperationException("Conversation follows a recipient away from the committed site");
        }
        if(sim.StateDigestString()!=digest)throw new InvalidOperationException("Discarding an old conversation changed the simulation");
        GD.Print("WORLD_CONVERSATION_CATCHUP_PASS: reciprocal receipts skipped entirely, one conversation and reply, moved recipient rejected, read-only");
        GD.Print($"WORLD_CONVERSATION_PASS: waited silently for confirmation; {speechA}/{speechB} speech frames across three idle decisions for both residents, {gap} silent frames at handoff, both nod, flee cancels, read-only");
    }
}
