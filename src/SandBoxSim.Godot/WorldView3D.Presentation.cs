using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private readonly PoseSchedule _poseSchedule=new();
    private readonly Dictionary<long,ResourceKind> _caravanLoads=new();
    private const float MaximumActorObservationStep=1f/24;
    private static int ResidentPoseStride(float distanceSquared,bool paced,bool contactPose)
        =>distanceSquared>45*45?8:distanceSquared>30*30?6:
            distanceSquared>(paced||contactPose?18*18:12*12)?2:1;

    private void ObserveCaravanLoads()
    {
        _caravanLoads.Clear();
        foreach(var caravan in Game.Sim.Civilizations.Caravans)
        {
            if(caravan.Delivered)continue;
            // Escrow at the buyer is only merchandise on the return leg.
            bool payment=caravan.Returning&&!caravan.Aborted&&caravan.PaymentInTransit;
            int kind=payment?caravan.Payment:caravan.Resource;
            float amount=payment?caravan.PaymentCargo:caravan.Cargo;
            if(kind is >=(int)ResourceKind.Food and <=(int)ResourceKind.Iron&&float.IsFinite(amount)&&amount>.01f)
                _caravanLoads.TryAdd(caravan.Merchant,(ResourceKind)kind);
        }
    }

    private readonly record struct PoseCue(ActionKind Action,ActionPhase Phase,uint Revision,ResidentRig.InteractionTarget Target,bool Aligned,
        ResidentRig.ToolSurface? Surface=null,bool ContactReady=true,Vector3? PickupPoint=null);
    /// <summary>Accumulate time only while the observed action and its contacts are current.</summary>
    private sealed class PoseSchedule
    {
        private readonly Dictionary<long,(PoseCue Cue,float Time)> _elapsed=new();
        public int Count=>_elapsed.Count;
        public bool RetainsFacing(long identity,PoseCue cue)
            =>_elapsed.TryGetValue(identity,out var pending)&&SameInteraction(pending.Cue,cue with {Aligned=true});

        private static bool SameInteraction(PoseCue previous,PoseCue current)
        {
            // A moving body updates the aim continuously. Its identity and
            // eligibility still decide whether old presentation time applies.
            if((current.Action is ActionKind.Attack or ActionKind.Hunt)&&previous.Surface is {} prior&&current.Surface is {} next
                &&previous.Target.Partner==current.Target.Partner&&ResidentRig.ContinuesSpearAim(prior,next))
                previous=previous with {Surface=current.Surface,Target=previous.Target with {Tile=current.Target.Tile}};
            if(previous.PickupPoint is {} source&&current.PickupPoint is {} destination
                &&source.DistanceSquaredTo(destination)<=ResidentRig.PickupReplanDistance*ResidentRig.PickupReplanDistance)
                previous=previous with {PickupPoint=current.PickupPoint};
            return previous==current;
        }

        public bool Observe(long identity,PoseCue cue,float delta,bool active)
        {
            if(!active){Forget(identity);return false;}
            bool changed=!_elapsed.TryGetValue(identity,out var pending)||!SameInteraction(pending.Cue,cue);
            float time=float.IsFinite(delta)&&delta>0?delta:0;
            // The world already advanced its target, inventory and contacts.
            // Applying prior time to this pose would skip the new approach or
            // replay a superseded interaction against current state.
            _elapsed[identity]=(cue,MathF.Min(1,(changed?0:pending.Time)+time));
            return changed;
        }
        public float Consume(long identity)
        {
            if(!_elapsed.TryGetValue(identity,out var elapsed))return 0;
            _elapsed[identity]=(elapsed.Cue,0);
            return elapsed.Time;
        }
        public void Forget(long identity)=>_elapsed.Remove(identity);
        public void Clear()=>_elapsed.Clear();
    }
    private static void ValidatePoseScheduling()
    {
        if(ResidentPoseStride(15*15,false,false)!=2||ResidentPoseStride(15*15,false,true)!=1
            ||ResidentPoseStride(19*19,false,true)!=2||ResidentPoseStride(46*46,true,true)!=8)
            throw new InvalidOperationException("Contact cadence fails to update nearby supports or extends to distant residents");
        var schedule=new PoseSchedule();const long actor=(3L<<32)|7;
        var cue=new PoseCue(ActionKind.Take,ActionPhase.Executing,12,new(new Vector2I(2,3),0,null),true);
        float actual=0,consumed=0;
        float[] jitter={.01f,.01f,.01f,.01f,.01f,.01f,.01f,.08f};
        foreach(float delta in jitter){schedule.Observe(actor,cue,delta,true);actual+=delta;}
        consumed+=schedule.Consume(actor);
        if(MathF.Abs(consumed-.15f)>.00001f||MathF.Abs(actual-consumed)>.00001f)
            throw new InvalidOperationException("Distant pose time follows the last frame instead of actual elapsed intervals");
        // Change distance bands before a pending update; neither invent nor lose time.
        for(int frame=0;frame<32;frame++)
        {
            float delta=frame%7==0?.07f:.009f;actual+=delta;schedule.Observe(actor,cue,delta,true);
            int stride=frame<11?8:frame<19?1:6;
            if((frame+7)%stride==0)consumed+=schedule.Consume(actor);
        }
        consumed+=schedule.Consume(actor);
        if(MathF.Abs(actual-consumed)>.00001f)throw new InvalidOperationException("Stride change loses or duplicates pending animation time");
        foreach(var next in new[]{cue with {Phase=ActionPhase.Done},cue with {Revision=13},
            cue with {Surface=new(new(.2f,.8f,-.5f),Vector3.Back)},
            cue with {ContactReady=false},
            cue with {Target=cue.Target with {Tile=new Vector2I(4,3)}},cue with {Target=cue.Target with {Partner=51}},
            cue with {Target=cue.Target with {Pickup=new(new(2,3),SandBoxSim.Core.Environment.ResourceKind.Stone,
                SandBoxSim.Core.Systems.ActionSystem.ContactSurface.GroundStock)}},
            cue with {Target=cue.Target with {Worksite=new(new(2,3),SandBoxSim.Core.Environment.ResourceKind.None,
                SandBoxSim.Core.Systems.ActionSystem.ContactSurface.BuildingWork,BuildingGeneration:4)}},
            cue with {Aligned=false},cue with {Action=ActionKind.Flee}})
        {
            schedule.Observe(actor,cue,.14f,true);
            if(!schedule.RetainsFacing(actor,cue)||next.Aligned&&schedule.RetainsFacing(actor,next))
                throw new InvalidOperationException("Facing latch leaked to a different interaction target");
            if(!schedule.Observe(actor,next,.016f,true)||MathF.Abs(schedule.Consume(actor)-.016f)>.000001f
                ||schedule.Observe(actor,next,.016f,true))
                throw new InvalidOperationException("An interaction boundary defers the next pose or applies superseded time to it");
        }
        var tracking=cue with {Action=ActionKind.Hunt,Surface=new(new(.2f,.8f,-.5f),Vector3.Back)};
        var moving=tracking with {Surface=new(new(.3f,.8f,-.5f),Vector3.Back),
            Target=tracking.Target with {Tile=new Vector2I(3,3)}};
        schedule.Observe(actor,tracking,.021f,true);
        if(!schedule.RetainsFacing(actor,moving)||schedule.Observe(actor,moving,.030f,true)
            ||MathF.Abs(schedule.Consume(actor)-.051f)>.00001f)
            throw new InvalidOperationException("The same moving prey loses its facing latch or accumulated pose time");
        schedule.Observe(actor,moving,.032f,true);
        var jumped=moving with {Surface=new(new(.7f,.8f,-.5f),Vector3.Back)};
        if(schedule.RetainsFacing(actor,jumped)||!schedule.Observe(actor,jumped,.016f,true)
            ||MathF.Abs(schedule.Consume(actor)-.016f)>.00001f)
            throw new InvalidOperationException("A discontinuous aim inherits the old pose interval");
        schedule.Observe(actor,moving,.032f,true);
        var replaced=moving with {Target=moving.Target with {Partner=1L<<32}};
        if(schedule.RetainsFacing(actor,replaced)||!schedule.Observe(actor,replaced,.016f,true)
            ||MathF.Abs(schedule.Consume(actor)-.016f)>.00001f)
            throw new InvalidOperationException("A different prey generation inherits the previous target's pose time");
        var pickup=cue with {PickupPoint=new(.2f,.1f,-.46f)};
        var shifted=pickup with {PickupPoint=new(.4f,.1f,-.46f)};
        schedule.Observe(actor,pickup,.032f,true);
        if(schedule.RetainsFacing(actor,shifted)||!schedule.Observe(actor,shifted,.016f,true)
            ||MathF.Abs(schedule.Consume(actor)-.016f)>.00001f)
            throw new InvalidOperationException("A shifted pickup inherits the old pose interval");
        schedule.Observe(actor,cue,.08f,true);schedule.Observe(actor,cue,.1f,false);
        if(schedule.Consume(actor)!=0)throw new InvalidOperationException("Paused or hidden resident retains pose catch-up debt");
        schedule.Observe(actor,cue,.04f,true);schedule.Forget(actor);
        const long successor=(4L<<32)|7;
        if(schedule.Consume(successor)!=0||schedule.Count!=0)throw new InvalidOperationException("Reused slot inherits another resident's animation debt");
        schedule.Observe(successor,cue,.02f,true);schedule.Clear();
        if(schedule.Consume(successor)!=0||schedule.Count!=0)throw new InvalidOperationException("World replacement retains pose scheduling state");
        schedule.Observe(actor,cue,10,true);
        if(schedule.Consume(actor)!=1)throw new InvalidOperationException("Long render stalls exceed the bounded pose catch-up budget");
        GD.Print("POSE_SCHEDULER_PASS: variable intervals and stride, moving aim preserves timing, immediate action/phase/revision/target/partner/facing boundaries, no stale pose time, pause and identity reuse");
    }
}
