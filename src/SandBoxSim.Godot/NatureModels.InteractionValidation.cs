using System;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateInteractionSequencing()
    {
        ValidateCargoPlacement();
        ValidateObservedCargoStow();
        ValidateObservedActionHandoffs();
        ValidateConsumptionInterrupts();
        ValidateContactBoundaries();
        ValidatePickupContactTiming();
        ValidatePickupIntoPlacement();
        ValidateInterruptedPlacementAlignment();
        ValidatePlacementReturn();
        ValidateUnavailableInteractionContacts();
        ValidateActionRetarget();
        ValidatePartnerLoss();
        ValidateSharedParcel();
        var actions=Enum.GetValues<ActionKind>();
        int contactTransfers=0,visibleLoads=0;
        foreach(int identity in new[]{17,55})
        {
            var handoff=Resident(identity,false,JobType.None);
            try
            {
                foreach(var pair in new[]{(ActionKind.Eat,ActionKind.Drink),(ActionKind.Take,ActionKind.GatherFood),(ActionKind.Deposit,ActionKind.ShareFood)})
                {
                    for(int i=0;i<150;i++)handoff.PoseAction(1f/60,pair.Item1,ActionPhase.Executing,false,0);
                    var previous=InTorso(handoff.Hands[1],handoff.Torso).Origin;
                    handoff.PoseAction(1f/60,pair.Item2,ActionPhase.Executing,false,0);
                    if(!handoff.GestureEngaged(pair.Item2)||previous.DistanceTo(InTorso(handoff.Hands[1],handoff.Torso).Origin)>.025f)
                        throw new InvalidOperationException("Compatible interaction returns to idle or snaps during handoff");
                }
            }
            finally{handoff.Free();}
            var rig=Resident(identity,false,JobType.Builder);
            try
            {
                // The simulator has arrived; the displayed root has not.
                for(int frame=0;frame<90;frame++)
                {
                    rig.ObserveLocomotion(new(0,0,-.75f/60),1f/60,0,false,.20f);
                    rig.PresentAction(1f/60,ActionKind.Take,ActionPhase.Executing,false,0);
                    if(rig.InteractionSettled||rig.GestureEngaged(ActionKind.Take)||rig.Torso.Rotation.X<-.15f)
                        throw new InvalidOperationException("Pickup begins while the rendered root is approaching");
                }
                if(rig.GaitSpeed<.5f)throw new InvalidOperationException("Arrival slide has no walking support");
                for(int frame=0;frame<180;frame++)
                {
                    rig.ObserveLocomotion(Vector3.Zero,1f/60,0,false);
                    rig.PresentAction(1f/60,ActionKind.Take,ActionPhase.Executing,false,0);
                }
                if(!rig.InteractionSettled||!rig.GestureEngaged(ActionKind.Take))
                    throw new InvalidOperationException("Settled arrival never enters the requested pickup");

                // All tool families, bare actions and failures share the same
                // real pose path; verify each ownership change at contact.
                foreach(var action in actions)
                {
                    var phase=action switch
                    {
                        ActionKind.None=>ActionPhase.Idle,
                        ActionKind.Wander or ActionKind.Explore or ActionKind.Migrate or ActionKind.Flee=>ActionPhase.Moving,
                        _=>ActionPhase.Executing
                    };
                    for(int frame=0;frame<240;frame++)
                    {
                        var movement=phase==ActionPhase.Moving?new Vector3(0,0,-.85f/60):Vector3.Zero;
                        rig.ObserveLocomotion(movement,1f/60,0,false);
                        bool held=rig.ToolHeld;
                        var previousTool=InTorso(rig.ToolMesh,rig.Torso);
                        rig.PresentAction(1f/60,action,phase,false,.3f);
                        var next=InTorso(rig.ToolMesh,rig.Torso);
                        if(held!=rig.ToolHeld)
                        {
                            contactTransfers++;
                            if(previousTool.Origin.DistanceTo(next.Origin)>.025f)
                                throw new InvalidOperationException($"Tool ownership changes across a gap: {identity}/{action}");
                        }
                    }
                    if(rig.GestureEngaged(action)!=(phase==ActionPhase.Executing)
                        ||phase==ActionPhase.Moving&&rig.GaitSpeed<.1f)
                        throw new InvalidOperationException($"Requested interaction does not match its phase: {identity}/{action}, {rig.Handling}");
                }
                rig.ShowCargo(true,ResourceKind.Wood);
                for(int frame=0;frame<180;frame++)
                {
                    rig.PresentAction(1f/60,ActionKind.Wander,ActionPhase.Moving,false,0);
                    if(!rig.CargoMesh.Visible)continue;
                    visibleLoads++;
                    for(int side=0;side<2;side++)
                    {
                        var actual=InTorso(rig.Hands[side],rig.Torso)*rig.Grip!.Position;
                        var target=rig.CargoSupportTarget(side);
                        if(actual.DistanceTo(target)>.03f)throw new InvalidOperationException($"Load loses hand support: {identity}/{frame}/{side}, gap={actual.DistanceTo(target)}");
                    }
                }
                if(!rig.CargoMesh.Visible)throw new InvalidOperationException("Cargo never acquires support");
                rig.ShowCargo(true,ResourceKind.Stone);
                if(rig.CargoMesh.Visible)throw new InvalidOperationException("Changing the carried resource replaces a supported object instantaneously");
                for(int frame=0;frame<180;frame++)rig.PresentAction(1f/60,ActionKind.Deposit,ActionPhase.Executing,false,0);
                if(!rig.CargoMesh.Visible)throw new InvalidOperationException("Transfer never reacquires the changed load");
                rig.ShowCargo(false,ResourceKind.Stone);
                rig.PresentAction(1f/60,ActionKind.Deposit,ActionPhase.Failed,false,0);
                if(rig.CompletionVisible||rig.CargoMesh.Visible)throw new InvalidOperationException("Failure replays an exchange or retains removed stock");
            }
            finally{rig.Free();}

            // Observe completion immediately after a different tool is in use:
            // setup time must not consume the whole visible meal gesture.
            var shortAction=Resident(identity,false,JobType.Builder);
            try
            {
                for(int i=0;i<180;i++)shortAction.PresentAction(1f/60,ActionKind.BuildHouse,ActionPhase.Executing,false,0);
                bool meal=false;
                for(int i=0;i<360;i++)
                {
                    shortAction.PresentAction(1f/60,ActionKind.Eat,ActionPhase.Done,false,0);
                    meal|=shortAction.GestureEngaged(ActionKind.Eat);
                }
                if(!meal||shortAction.CompletionVisible)throw new InvalidOperationException("Short completed meal expires while stowing or never finishes");
                shortAction.PresentAction(1f/60,ActionKind.Take,ActionPhase.Done,false,0);
                shortAction.PresentAction(1f/60,ActionKind.Flee,ActionPhase.Moving,false,0);
                if(shortAction.CompletionVisible)throw new InvalidOperationException("Urgent movement queues behind a completed interaction");
                var frozen=shortAction.Torso.Transform;
                shortAction.PresentAction(1,ActionKind.Take,ActionPhase.Executing,true,0);
                if(shortAction.Torso.Transform!=frozen)throw new InvalidOperationException("Paused interaction sequencing advances");
            }
            finally{shortAction.Free();}
        }
        if(contactTransfers<20||visibleLoads<100)throw new InvalidOperationException("Interaction checks did not exercise contact changes and carried loads");
        GD.Print($"INTERACTION_SEQUENCE_PASS: two adult frames, all {actions.Length} action kinds, rendered arrival, {contactTransfers} contact ownership changes, {visibleLoads} supported load frames, resource replacement, short completion, cancellation and pause");
    }

    private void ValidateObservedCargoStow()
    {
        float shortestHold=float.PositiveInfinity,maximumStep=0;
        foreach(int identity in new[]{17,55})
        foreach(float delta in new[]{1f/60,1f/30})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                for(float elapsed=0;elapsed<2;elapsed+=delta)
                    rig.PresentAction(delta,ActionKind.Wander,ActionPhase.Moving,false,0,1,inventoryCargo:ResourceKind.Wood);
                if(rig.SupportedCargo!=ResourceKind.Wood)
                    throw new InvalidOperationException("Cargo stow fixture has no supported wood");
                var previous=rig.HandWorldPose(1)*rig.CargoMesh.Position;
                float held=0;
                bool stowed=false,loaded=false;
                for(float elapsed=0;elapsed<2;elapsed+=delta)
                {
                    rig.PresentAction(delta,ActionKind.Flee,ActionPhase.Moving,false,0,2,inventoryCargo:ResourceKind.Stone);
                    if(rig.PlacingCargo||rig.ReleasedCargo||rig.CompletionVisible)
                        throw new InvalidOperationException("Inventory replacement creates a stale placement during departure");
                    if(rig.SupportedCargo==ResourceKind.Wood)
                    {
                        if(stowed)throw new InvalidOperationException("Outgoing parcel appears again after stowing");
                        held+=delta;
                        var current=rig.HandWorldPose(1)*rig.CargoMesh.Position;
                        maximumStep=MathF.Max(maximumStep,current.DistanceTo(previous)/delta);
                        previous=current;
                    }
                    else if(rig.SupportedCargo==ResourceKind.None)stowed=true;
                    else if(rig.SupportedCargo==ResourceKind.Stone&&stowed){loaded=true;break;}
                    else throw new InvalidOperationException("Incoming inventory replaces a displayed parcel before stowing");
                }
                if(held<.30f||!loaded)
                    throw new InvalidOperationException($"Walking inventory handoff did not stow and reacquire: {identity}, {delta:F3} s steps, old support {held:F3} s, new load {loaded}");
                shortestHold=MathF.Min(shortestHold,held);

                for(float elapsed=0;elapsed<1.2f;elapsed+=delta)
                    rig.PresentAction(delta,ActionKind.Wander,ActionPhase.Moving,false,0,3,inventoryCargo:ResourceKind.Stone);
                rig.PresentAction(delta,ActionKind.Sleep,ActionPhase.Executing,false,0,4,inventoryCargo:ResourceKind.None);
                if(rig.SupportedCargo!=ResourceKind.Stone||rig.RestPoseWeight>.01f)
                    throw new InvalidOperationException("The last parcel vanishes or the resident sits before stowing it");
                for(float elapsed=0;elapsed<3;elapsed+=delta)
                    rig.PresentAction(delta,ActionKind.Sleep,ActionPhase.Executing,false,0,4,inventoryCargo:ResourceKind.None);
                if(rig.CargoMesh.Visible||!rig.GestureEngaged(ActionKind.Sleep))
                    throw new InvalidOperationException("Parcel stow blocks the next rest action");

                // The isolated portrait receives inventory, but has no scene crate.
                for(float elapsed=0;elapsed<2;elapsed+=delta)
                    rig.PresentAction(delta,ActionKind.StoreInBuilding,ActionPhase.Executing,false,0,5,inventoryCargo:ResourceKind.Wood);
                if(rig.SupportedCargo!=ResourceKind.Wood)
                    throw new InvalidOperationException("Portrait placement fixture has no supported wood");
                bool released=false;
                for(float elapsed=0;elapsed<2;elapsed+=delta)
                {
                    rig.PresentAction(delta,ActionKind.StoreInBuilding,ActionPhase.Done,false,0,5,inventoryCargo:ResourceKind.None);
                    released|=rig.ReleasedCargo;
                }
                if(!released||rig.CargoMesh.Visible)
                    throw new InvalidOperationException("Receipt without a scene crate fails to complete its portrait release");
            }
            finally{rig.Free();}
        }
        if(maximumStep>1.5f)throw new InvalidOperationException($"Outgoing parcel jerks during stow: peak {maximumStep:F3} m/s");
        GD.Print($"OBSERVED_CARGO_STOW_PASS: two adult frames at 30/60 Hz, old parcel supported for at least {shortestHold:F3} s, peak {maximumStep:F3} m/s, no release at a stale site, walking and rest resume");
    }

    private void ValidateUnavailableInteractionContacts()
    {
        const float delta=1f/60;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.Builder);
            try
            {
                uint revision=0;
                foreach(var action in new[]{ActionKind.Take,ActionKind.GatherFood,ActionKind.Deposit,ActionKind.StoreInBuilding,
                    ActionKind.Farm,ActionKind.BuildHouse,ActionKind.Drink,ActionKind.Eat,ActionKind.Hunt,ActionKind.Attack,ActionKind.Socialize,ActionKind.ShareFood})
                {
                    revision++;
                    for(int frame=0;frame<120;frame++)
                    {
                        rig.PresentAction(delta,action,ActionPhase.Executing,false,0,revision,contactReady:false);
                        if(rig.GestureEngaged(action)||rig.Cup.Visible||rig.Bite.Visible||rig.PresentedToolImpacts!=0)
                            throw new InvalidOperationException($"Missing interaction target plays a contact: {action}/{frame}");
                    }
                    rig.PresentAction(delta,action,ActionPhase.Failed,false,0,revision);
                    if(rig.CompletionVisible)throw new InvalidOperationException($"Failed interaction queues a completion: {action}");
                }
                foreach(bool appears in new[]{true,false})
                {
                    revision++;
                    rig.PresentAction(delta,ActionKind.BuildHouse,ActionPhase.Executing,false,0,revision,contactReady:false);
                    for(int frame=0;frame<85;frame++)
                    {
                        bool ready=appears&&frame>=15;
                        rig.PresentAction(delta,ActionKind.BuildHouse,ActionPhase.Done,false,0,revision,
                            contactReady:ready,deferContact:!ready);
                        if(!ready&&rig.GestureEngaged(ActionKind.BuildHouse))throw new InvalidOperationException("Hammer swings before its post appears");
                    }
                    if(!appears&&rig.CompletionVisible)throw new InvalidOperationException("Unrendered worksite leaves an unbounded wait");
                    for(int frame=0;frame<350;frame++)rig.PresentAction(delta,ActionKind.BuildHouse,ActionPhase.Done,false,0,revision);
                    if(rig.PresentedToolImpacts!=1||rig.CompletionVisible)
                        throw new InvalidOperationException("A delayed worksite loses, repeats or replays an expired stroke");
                }
            }
            finally{rig.Free();}
        }
        GD.Print("INTERACTION_READINESS_PASS: 12 unavailable actions on both adult rigs, one delayed construction impact and no expired replay");
    }

    private void ValidatePickupContactTiming()
    {
        const float delta=1f/60;
        int lateContacts=0;
        float maximumRecoveryStep=0,maximumShiftStep=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var actual=new Vector3(.035f,.10f,-.46f);
                var missed=new Vector3(actual.X,-.50f,actual.Z);
                var mesh=GroundPickupPiece(ResourceKind.Stone,0);
                var replacement=GroundPickupPiece(ResourceKind.Wood,0);
                var target=new ResidentRig.InteractionTarget(new(2,3),0,
                    new(new(2,3),ResourceKind.Stone,SandBoxSim.Core.Systems.ActionSystem.ContactSurface.GroundStock));
                rig.PresentAction(delta,ActionKind.None,ActionPhase.Idle,false,0,1);
                rig.SetPickupPiece(mesh,true);
                for(int frame=0;frame<86;frame++)
                {
                    rig.PresentAction(delta,ActionKind.Take,ActionPhase.Done,false,0,2,sceneContact:missed,target:target);
                    if(rig.ScenePickupGrasped||rig.ScenePickupPieceVisible)
                        throw new InvalidOperationException($"Unreachable stock enters the hand: {identity}");
                }
                for(int frame=0;frame<30&&!rig.ScenePickupGrasped;frame++)
                    rig.PresentAction(delta,ActionKind.Take,ActionPhase.Done,false,0,2,sceneContact:actual,target:target);
                if(!rig.ScenePickupGrasped||rig.ScenePickupPiecePose is not {} pose
                    ||pose.Origin.DistanceTo(actual)>.012f
                    ||(rig.HandWorldPose(1)*rig.Grip!.Position).DistanceTo(actual)>.055f)
                    throw new InvalidOperationException($"A late reachable target misses its pickup: {identity}, state={rig.Handling}, gap={(rig.HandWorldPose(1)*rig.Grip!.Position).DistanceTo(actual):F3}");
                lateContacts++;

                var attached=rig.HandWorldPose(1).AffineInverse()*pose;
                rig.SetPickupPiece(replacement,false);
                for(int frame=0;frame<10;frame++)
                {
                    rig.PresentAction(delta,ActionKind.Take,ActionPhase.Done,false,0,2,sceneContact:actual+new Vector3(.24f,.15f,0),target:target);
                    if(rig.ScenePickupPiecePose is not {} held
                        ||(rig.HandWorldPose(1).AffineInverse()*held).Origin.DistanceTo(attached.Origin)>.0001f
                        ||rig.GetNode<MeshInstance3D>("PickedUpResource").Mesh!=mesh)
                        throw new InvalidOperationException($"A refreshed stock model replaces the gripped object: {identity}");
                    pose=held;
                }
                rig.SetPickupPiece(null,false);
                rig.PresentAction(delta,ActionKind.Take,ActionPhase.Done,false,0,2,sceneContact:actual,target:target,contactReady:false);
                if(!rig.RecoveringPickup||rig.ScenePickupPiecePose is not {} recovery)
                    throw new InvalidOperationException($"An invalidated pickup drops the supported prop: {identity}");
                maximumRecoveryStep=MathF.Max(maximumRecoveryStep,recovery.Origin.DistanceTo(pose.Origin));
                for(int frame=0;frame<120;frame++)
                    rig.PresentAction(delta,ActionKind.None,ActionPhase.Idle,false,0,3);

                rig.SetPickupPiece(mesh,true);
                for(int frame=0;frame<210;frame++)
                {
                    rig.PresentAction(delta,ActionKind.Take,ActionPhase.Done,false,0,4,sceneContact:missed,target:target);
                    if(rig.ScenePickupGrasped||rig.ScenePickupPieceVisible)
                        throw new InvalidOperationException($"Expired reach creates an object without contact: {identity}");
                }
                if(rig.CompletionVisible||rig.RecoveringPickup)
                    throw new InvalidOperationException($"Unreachable pickup never leaves the contact wait: {identity}");

                for(int frame=0;frame<120;frame++)
                    rig.PresentAction(delta,ActionKind.Take,ActionPhase.Executing,false,0,5,sceneContact:actual,target:target);
                if(!rig.GestureEngaged(ActionKind.Take))
                    throw new InvalidOperationException("Shifted pickup fixture did not prepare its initial reach");
                var shifted=actual+Vector3.Right*.19f;
                var wrist=rig.HandWorldPose(1).Origin;
                for(int frame=0;frame<6;frame++)
                {
                    rig.PresentAction(delta,ActionKind.Take,ActionPhase.Executing,false,0,5,sceneContact:shifted,target:target);
                    var after=rig.HandWorldPose(1).Origin;
                    float step=wrist.DistanceTo(after);
                    maximumShiftStep=MathF.Max(maximumShiftStep,step);
                    if(rig.GestureEngaged(ActionKind.Take)||rig.ScenePickupPieceVisible||step>.045f)
                        throw new InvalidOperationException("A shifted pickup inherits the prepared reach or snaps the wrist");
                    wrist=after;
                }
                for(int frame=0;frame<120;frame++)
                    rig.PresentAction(delta,ActionKind.Take,ActionPhase.Executing,false,0,5,sceneContact:shifted,target:target);
                if(!rig.GestureEngaged(ActionKind.Take)||rig.ScenePickupPieceVisible)
                    throw new InvalidOperationException("A shifted pickup cannot prepare again without an early prop");
            }
            finally{rig.Free();}
        }
        if(maximumRecoveryStep>.06f)throw new InvalidOperationException($"Lost contact jolts a held object: {maximumRecoveryStep:F3} m");
        GD.Print($"PICKUP_CONTACT_TIMING_PASS: {lateContacts} late grasps, new reach after source shift, maximum shift wrist step={maximumShiftStep:F3} m, invalid contact recovers, unreachable targets expire, maximum recovery step={maximumRecoveryStep:F3} m");
    }

    private void ValidateInterruptedPlacementAlignment()
    {
        const float delta=1f/60;
        int releases=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var point=new Vector3(0,0,-.54f);
                rig.CargoGroundHeight=(_,_)=>0;
                for(int frame=0;frame<120;frame++)
                    rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Executing,false,0,1,inventoryCargo:ResourceKind.Wood);
                for(int frame=0;frame<60;frame++)
                    rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Done,false,0,1,sceneContact:point,
                        contactResource:ResourceKind.Wood,inventoryCargo:ResourceKind.None);
                if(!rig.PlacingCargo||rig.SupportedCargo!=ResourceKind.Wood)
                    throw new InvalidOperationException($"Interrupted placement has no load in descent: {identity}");
                for(int frame=0;frame<45;frame++)
                {
                    rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Done,false,0,1,targetAligned:false,sceneContact:point,
                        contactResource:ResourceKind.Wood,inventoryCargo:ResourceKind.None);
                    if(rig.PlacingCargo||rig.ReleasedCargo||rig.SupportedCargo!=ResourceKind.Wood)
                        throw new InvalidOperationException($"A lost alignment releases or drops a held parcel: {identity}/{frame}");
                }
                for(int frame=0;frame<165;frame++)
                {
                    rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Done,false,0,1,sceneContact:point,
                        contactResource:ResourceKind.Wood,inventoryCargo:ResourceKind.None);
                    if(!rig.ReleasedCargo)continue;
                    var world=rig.HandWorldPose(1)*rig.CargoMesh.Transform;
                    var basePoint=world.Origin+Vector3.Up*rig.CargoMesh.Mesh.GetAabb().Position.Y*world.Basis.Y.Length();
                    if(basePoint.DistanceTo(point)>.075f)
                        throw new InvalidOperationException($"Resumed placement releases away from its original support: {identity}, {basePoint}");
                    releases++;
                    break;
                }
            }
            finally{rig.Free();}
        }
        if(releases!=2)throw new InvalidOperationException("A re-aligned placement never returns to its support");
        GD.Print("PLACEMENT_REALIGN_PASS: both adult rigs hold the parcel during alignment loss, replan from the displayed pose and release only at the original support");
    }

    private void ValidatePlacementReturn()
    {
        float maxStep=0;
        int recovered=0;
        foreach(int identity in new[]{17,55})
        foreach(float delta in new[]{1f/60,1f/30})
        foreach(bool timeout in new[]{false,true})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var point=new Vector3(0,0,-.54f);
                float ground=0;
                rig.CargoGroundHeight=(_,_)=>ground;
                for(float elapsed=0;elapsed<2;elapsed+=delta)
                    rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Executing,false,0,1,inventoryCargo:ResourceKind.Wood);
                void Place()=>rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Done,false,0,1,
                    sceneContact:point,contactResource:ResourceKind.Wood,inventoryCargo:ResourceKind.None);
                for(float elapsed=0;elapsed<.91f;elapsed+=delta)Place();
                if(!rig.PlacingCargo||rig.SupportedCargo!=ResourceKind.Wood)
                    throw new InvalidOperationException($"Placement return fixture does not have a lowered parcel: {identity}/{delta}");
                var start=rig.HandWorldPose(1)*rig.CargoMesh.Position;
                var previous=start;
                float held=0,rise=0;
                bool stowed=false;
                ground=-.32f;
                for(float elapsed=0;elapsed<3.4f;elapsed+=delta)
                {
                    if(timeout)Place();
                    else rig.PresentAction(delta,ActionKind.None,ActionPhase.Idle,false,0,2,inventoryCargo:ResourceKind.None);
                    if(rig.ReleasedCargo)
                        throw new InvalidOperationException("An aborted placement releases without a valid support");
                    if(rig.PlacingCargo)
                    {
                        previous=rig.HandWorldPose(1)*rig.CargoMesh.Position;
                        start=previous;
                        continue;
                    }
                    if(rig.SupportedCargo==ResourceKind.None){stowed=true;continue;}
                    if(stowed)throw new InvalidOperationException("An aborted placement replays its parcel after recovery");
                    var current=rig.HandWorldPose(1)*rig.CargoMesh.Position;
                    maxStep=MathF.Max(maxStep,current.DistanceTo(previous)/delta);
                    rise=MathF.Max(rise,current.Y-start.Y);
                    previous=current;
                    held+=delta;
                }
                if(!stowed||held<.45f||rise<.22f)
                    throw new InvalidOperationException($"Unreleased parcel does not rise before stowing: {identity}/{delta}/{timeout}, held={held:F3} s, rise={rise:F3} m");
                recovered++;
            }
            finally{rig.Free();}
        }
        if(maxStep>1.9f)
            throw new InvalidOperationException($"An aborted placement jerks while returning: peak {maxStep:F3} m/s");
        GD.Print($"PLACEMENT_RETURN_PASS: {recovered} interrupted or unsupported placements at 30/60 Hz, peak {maxStep:F3} m/s, raised in both hands without false release or replay");
    }

    private void ValidatePickupIntoPlacement()
    {
        const float delta=1f/20;
        var rig=Resident(17,false,JobType.None);
        try
        {
            var pickup=new Vector3(.035f,.10f,-.46f);
            var placement=new Vector3(0,0,-.54f);
            var target=new ResidentRig.InteractionTarget(new(2,3),0,
                new(new(2,3),ResourceKind.Stone,SandBoxSim.Core.Systems.ActionSystem.ContactSurface.GroundStock));
            rig.CargoGroundHeight=(_,_)=>0;
            rig.SetPickupPiece(GroundPickupPiece(ResourceKind.Stone,0),true);
            rig.PresentAction(delta,ActionKind.None,ActionPhase.Idle,false,0,1);
            for(int i=0;i<80&&!rig.ScenePickupGrasped;i++)
                rig.PresentAction(delta,ActionKind.Take,ActionPhase.Done,false,0,2,
                    sceneContact:pickup,target:target,inventoryCargo:ResourceKind.Stone);
            if(!rig.ScenePickupGrasped||!rig.ScenePickupPieceVisible||rig.CargoMesh.Visible)
                throw new InvalidOperationException("Rapid placement fixture did not start from a one-hand grasp");

            bool supported=false,released=false;
            for(int i=0;i<150;i++)
            {
                rig.PresentAction(delta,ActionKind.Deposit,ActionPhase.Done,false,0,3,
                    sceneContact:placement,contactResource:ResourceKind.Stone,inventoryCargo:ResourceKind.None);
                if(!supported&&rig.ScenePickupPieceVisible==rig.CargoMesh.Visible||rig.RecoveringPickup
                    ||rig.ScenePickupPieceVisible&&rig.CargoMesh.Visible)
                    throw new InvalidOperationException("Rapid placement loses, duplicates or pockets the object before dual-palm support");
                supported|=rig.SupportedCargo==ResourceKind.Stone;
                released|=rig.ReleasedCargo;
            }
            if(!supported||!released||rig.CompletionVisible||rig.CargoMesh.Visible)
                throw new InvalidOperationException("The already grasped item does not reach its committed support");
        }
        finally{rig.Free();}
        GD.Print("PICKUP_TO_PLACEMENT_PASS: 20 Hz pickup hands the same resource to both palms and its confirmed placement without pocketing");
    }

    private void ValidateActionRetarget()
    {
        const float delta=1f/60;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var old=new ResidentRig.InteractionTarget(null,11,null);
                var next=old with {Partner=21};
                for(int i=0;i<240;i++)rig.PresentAction(delta,ActionKind.Socialize,ActionPhase.Executing,false,0,8,target:old);
                if(!rig.GestureEngaged(ActionKind.Socialize))throw new InvalidOperationException("Social retarget fixture did not engage its first partner");
                var before=rig.HandWorldPose(1).Origin;
                for(int i=0;i<6;i++)
                {
                    rig.PresentAction(delta,ActionKind.Socialize,ActionPhase.Executing,false,0,8,target:next);
                    if(rig.GestureEngaged(ActionKind.Socialize))throw new InvalidOperationException("A different partner inherits an active gesture");
                    var after=rig.HandWorldPose(1).Origin;
                    if(before.DistanceTo(after)>.035f)throw new InvalidOperationException("Retarget snaps the outgoing hand");
                    before=after;
                }
                for(int i=0;i<120;i++)rig.PresentAction(delta,ActionKind.Socialize,ActionPhase.Executing,false,0,8,target:next);
                if(!rig.GestureEngaged(ActionKind.Socialize))throw new InvalidOperationException("Social contact does not recover on the new partner");
            }
            finally{rig.Free();}
        }
        GD.Print("INTERACTION_RETARGET_PASS: both adult rigs release a superseded social target with bounded wrist movement and engage the new target");
    }

    private void ValidateSharedParcel()
    {
        float worstGrip=0,maxStep=0,transferVelocityChange=0;
        int overlap=0;
        foreach(bool reversed in new[]{false,true})
        {
            var giver=Resident(reversed?55:17,false,JobType.None);
            var receiver=Resident(reversed?17:55,false,JobType.None);
            giver.Position=new(-.46f,0,0);receiver.Position=new(.46f,0,0);
            giver.Rotation=new(0,-MathF.PI/2,0);receiver.Rotation=new(0,MathF.PI/2,0);
            try
            {
                foreach(bool interrupt in new[]{false,true})
                {
                    var exchange=new ResidentHandoff(giver,receiver);
                    bool sawParcel=false,stopped=false;
                    int preparationFrames=0;
                    bool compareTransferVelocity=false;
                    Vector3 previousVelocity=Vector3.Zero;
                    for(int frame=0;frame<390&&!exchange.Finished;frame++)
                    {
                        exchange.Prepare();
                        giver.PresentAction(1f/60,ActionKind.ShareFood,ActionPhase.Done,false,0,hasPartner:false);
                        receiver.PresentAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                        var previous=exchange.ParcelPose;
                        bool visible=exchange.ParcelVisible;
                        bool transferred=exchange.Transferred;
                        if(!sawParcel&&receiver.HandoffHandReady)preparationFrames++;
                        exchange.Advance(1f/60);
                        if(!exchange.ParcelVisible)continue;
                        if(visible)
                        {
                            var step=exchange.ParcelPose.Origin-previous.Origin;
                            var velocity=step*60;
                            maxStep=MathF.Max(maxStep,step.Length());
                            if(compareTransferVelocity)
                                transferVelocityChange=MathF.Max(transferVelocityChange,velocity.DistanceTo(previousVelocity));
                            previousVelocity=velocity;
                            compareTransferVelocity=!transferred&&exchange.Transferred;
                        }
                        sawParcel=true;
                        worstGrip=MathF.Max(worstGrip,exchange.OwnerGap);
                        if(!exchange.Transferred&&receiver.HandoffHandReady&&exchange.ReceiverGap<.018f)overlap++;
                        if(interrupt&&!stopped)
                        {
                            stopped=true;exchange.Interrupt();
                            if(!exchange.ParcelVisible)throw new InvalidOperationException("Interrupted parcel vanishes before recovery");
                        }
                    }
                    if(!sawParcel||preparationFrames<8||!exchange.Finished||exchange.Transferred==interrupt||giver.HandoffReserved||receiver.HandoffReserved)
                        throw new InvalidOperationException($"Two-person exchange does not resolve: reversed={reversed}, interrupted={interrupt}, parcel={sawParcel}, preparation={preparationFrames}, transferred={exchange.Transferred}");
                }
            }
            finally{giver.Free();receiver.Free();}
        }
        if(overlap<8||worstGrip>.020f||maxStep>.025f||transferVelocityChange>.30f)
            throw new InvalidOperationException($"Parcel contact gap={worstGrip}, step={maxStep}, shared frames={overlap}, speed change={transferVelocityChange:F3} m/s");
        GD.Print($"SHARED_PARCEL_PASS: both adult roles, {overlap} receiver contact frames before transfer, maximum owner gap {worstGrip*1000:F1} mm, maximum parcel step {maxStep*1000:F1} mm, transfer velocity change {transferVelocityChange:F3} m/s, interrupt and recovery");
    }

    private void ValidatePartnerLoss()
    {
        var rig=Resident(17,false,JobType.None);
        try
        {
            foreach(var action in new[]{ActionKind.Socialize,ActionKind.ShareFood,ActionKind.Attack})
            {
                for(int i=0;i<180;i++)rig.PresentAction(1f/60,action,ActionPhase.Executing,false,0,1,false);
                if(rig.GestureEngaged(action)||rig.ToolHeld)
                    throw new InvalidOperationException($"Partner action starts towards empty space: {action}");
                rig.PresentAction(1f/60,action,ActionPhase.Done,false,0,2,true);
                if(!rig.CompletionVisible)throw new InvalidOperationException($"Committed interaction loses its finish: {action}");
                rig.PresentAction(1f/60,action,ActionPhase.Done,false,0,2,false);
                if(rig.GestureEngaged(action)||rig.CompletionVisible)
                    throw new InvalidOperationException($"Partner loss leaves an active completion: {action}");
            }
        }
        finally{rig.Free();}
        GD.Print("PARTNER_HANDOFF_PASS: social, share and attack wait for a participant and stop when that participant leaves");
    }

    private void ValidateConsumptionInterrupts()
    {
        float releaseGap=0;int supportedReturnFrames=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                rig.PresentAction(1f/60,ActionKind.Drink,ActionPhase.Executing,false,0,1);
                bool sawCup=false;
                for(int i=0;i<360;i++)
                {
                    rig.PresentAction(1f/60,ActionKind.Drink,ActionPhase.Done,false,0,1);
                    sawCup|=rig.Cup.Visible;
                    if(sawCup&&!rig.CompletionVisible)break;
                }
                if(!sawCup||rig.ConsumeContacts!=1||rig.Cup.Visible||rig.CompletionVisible)
                    throw new InvalidOperationException($"Short drinking must complete exactly one contact and return: {identity}, contacts={rig.ConsumeContacts}, {rig.Handling}");
                releaseGap=MathF.Max(releaseGap,(InTorso(rig.Hands[1],rig.Torso)*rig.Grip!.Position).DistanceTo(rig.ConsumePocket));
                for(int i=0;i<60;i++)rig.PresentAction(1f/60,ActionKind.Drink,ActionPhase.Done,false,0,1);
                if(rig.ConsumeContacts!=1||rig.Cup.Visible)
                    throw new InvalidOperationException("Completed drinking is replayed without a new action");

                for(int i=0;i<180&&!rig.Cup.Visible;i++)
                    rig.PresentAction(1f/60,ActionKind.Drink,ActionPhase.Executing,false,0,2);
                if(!rig.Cup.Visible)throw new InvalidOperationException("Interrupted drinking never acquires the cup");
                for(int i=0;i<22;i++)rig.PresentAction(1f/60,ActionKind.Drink,ActionPhase.Executing,false,0,2);
                var frozen=InTorso(rig.Cup,rig.Torso);
                rig.PresentAction(1,ActionKind.Flee,ActionPhase.Moving,true,0,3);
                if(frozen!=InTorso(rig.Cup,rig.Torso))throw new InvalidOperationException("Paused interruption moves the cup");
                bool gait=false;
                for(int i=0;i<160;i++)
                {
                    rig.PresentAction(1f/60,ActionKind.Flee,ActionPhase.Moving,false,0,3);
                    gait|=rig.GaitSpeed>.1f&&rig.Cup.Visible;
                    if(rig.Cup.Visible)supportedReturnFrames++;
                }
                if(!gait||rig.ConsumeContacts!=1||rig.Cup.Visible||rig.CompletionVisible)
                    throw new InvalidOperationException($"Flee must recover the held cup without swallowing: {identity}, contacts={rig.ConsumeContacts}, {rig.Handling}");
            }
            finally{rig.Free();}
        }
        if(releaseGap>.035f||supportedReturnFrames<12)
            throw new InvalidOperationException($"Released cup leaves hand before returning to the waist: gap={releaseGap}");
        GD.Print($"CONSUMPTION_CONTACT_PASS: two adult frames, exactly one sip on short completion, return gap={releaseGap*1000:F1} mm, {supportedReturnFrames} held frames during interrupted gait, pause and no duplicate contact");
    }

    private void ValidateObservedActionHandoffs()
    {
        float largestCadenceDifference=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.Builder);
            try
            {
                rig.PresentAction(1f/60,ActionKind.Eat,ActionPhase.Done,false,0,1);
                if(rig.CompletionVisible)throw new InvalidOperationException("Initial snapshot replays a completed action");
                rig.PresentAction(1f/60,ActionKind.Eat,ActionPhase.Done,false,0,2);
                if(!rig.CompletionVisible)throw new InvalidOperationException("A distinct completion of the same action was lost");
                rig.PresentAction(1f/60,ActionKind.Flee,ActionPhase.Moving,true,0,3);
                if(!rig.CompletionVisible)throw new InvalidOperationException("Paused observation interrupts an active completion");
                rig.PresentAction(1f/60,ActionKind.Flee,ActionPhase.Moving,false,0,3);
                if(rig.CompletionVisible||rig.GestureEngaged(ActionKind.Eat))
                    throw new InvalidOperationException("Flee does not interrupt the completed meal");
                rig.ObserveOffscreenAction(ActionKind.Eat,ActionPhase.Done,4);
                rig.PresentAction(1f/60,ActionKind.Eat,ActionPhase.Done,false,0,4);
                if(rig.CompletionVisible)throw new InvalidOperationException("Returning on camera replays a hidden completion");

                for(int i=0;i<180;i++)rig.PresentAction(1f/60,ActionKind.BuildHouse,ActionPhase.Executing,false,0,5);
                if(!rig.GestureEngaged(ActionKind.BuildHouse))throw new InvalidOperationException("Rest handoff never starts from a held hammer");
                for(int i=0;i<180;i++)
                {
                    rig.PresentAction(1f/60,ActionKind.Sleep,ActionPhase.Executing,false,0,6);
                    if((rig.ToolHeld||rig.Handling=="松开手指")&&rig.RestPoseWeight>.01f)
                        throw new InvalidOperationException("Pelvis lowers before the tool is stowed and released");
                }
                if(!rig.GestureEngaged(ActionKind.Sleep))throw new InvalidOperationException("Rest fails to take over after tool release");
                for(int i=0;i<180;i++)
                {
                    rig.PresentAction(1f/60,ActionKind.Take,ActionPhase.Executing,false,0,7);
                    if(rig.RestPoseWeight>=.20f&&rig.GestureEngaged(ActionKind.Take))
                        throw new InvalidOperationException("Pickup engages before the resident rises from rest");
                }
                if(!rig.GestureEngaged(ActionKind.Take))throw new InvalidOperationException("Pickup fails to start after rising");
            }
            finally{rig.Free();}

            float near=MealCompletionTime(identity,1f/60),far=MealCompletionTime(identity,.2f);
            largestCadenceDifference=MathF.Max(largestCadenceDifference,MathF.Abs(near-far));
            if(MathF.Abs(near-far)>.24f)throw new InvalidOperationException($"Sparse observation changes the meal handoff duration: {near}/{far}");
        }
        GD.Print($"OBSERVED_HANDOFF_PASS: two adult frames, repeat completions, pause, offscreen expiry, tool release before rest, rise before pickup; 60 Hz/5 Hz duration difference {largestCadenceDifference:F3} s");
    }

    private void ValidateContactBoundaries()
    {
        const float step=1f/60;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                foreach(var action in new[]{ActionKind.Take,ActionKind.BuildHouse,ActionKind.Socialize})
                {
                    for(int frame=0;frame<120;frame++)
                        rig.PresentAction(step,action,ActionPhase.Executing,false,0,targetAligned:false);
                    if(rig.GestureEngaged(action))throw new InvalidOperationException($"Contact begins before facing the target: {action}");
                    for(int frame=0;frame<240;frame++)
                        rig.PresentAction(step,action,ActionPhase.Executing,false,0);
                    if(!rig.GestureEngaged(action))throw new InvalidOperationException($"Contact does not resume once facing the target: {action}");
                }
                rig.ShowCargo(true,ResourceKind.Wood);
                for(int frame=0;frame<150;frame++)
                    rig.PresentAction(step,ActionKind.Deposit,ActionPhase.Executing,false,0,11);
                if(!rig.CargoMesh.Visible)throw new InvalidOperationException("Deferred placement fixture has no supported load");
                rig.ShowCargo(false,ResourceKind.Wood);
                for(int frame=0;frame<14;frame++)
                {
                    rig.ObserveLocomotion(new(0,0,-.6f*step),step,0,false,.22f);
                    rig.PresentAction(step,ActionKind.Deposit,ActionPhase.Done,false,0,11,targetAligned:false);
                    if(rig.PlacingCargo||!rig.CompletionVisible||!rig.CargoMesh.Visible)
                        throw new InvalidOperationException("Completed deposit either loses the held parcel or lowers it while approaching");
                }
                for(int frame=0;frame<48;frame++)
                {
                    rig.ObserveLocomotion(Vector3.Zero,step,0,false);
                    rig.PresentAction(step,ActionKind.Deposit,ActionPhase.Done,false,0,11,targetAligned:false);
                }
                if(!rig.InteractionSettled||rig.PlacingCargo||!rig.CargoMesh.Visible)
                    throw new InvalidOperationException("A settled but misaligned deposit releases its parcel");
                rig.PresentAction(step,ActionKind.Deposit,ActionPhase.Done,false,0,11);
                if(!rig.PlacingCargo)throw new InvalidOperationException("Aligned arrival does not resume its deferred placement");
                rig.PresentAction(step,ActionKind.Flee,ActionPhase.Moving,false,0,12);
                if(rig.CompletionVisible||rig.PlacingCargo||rig.ReleasedCargo)
                    throw new InvalidOperationException("Flee leaves the previous placement active");
                for(int frame=0;frame<80;frame++)
                    rig.PresentAction(step,ActionKind.Flee,ActionPhase.Moving,false,0,12);
                if(rig.CargoMesh.Visible)throw new InvalidOperationException("Flee fails to stow the cancelled parcel");

                for(int frame=0;frame<180&&!rig.Cup.Visible;frame++)
                    rig.PresentAction(step,ActionKind.Drink,ActionPhase.Executing,false,0,13);
                if(!rig.Cup.Visible)throw new InvalidOperationException("Repeated-drink fixture does not hold the outgoing cup");
                for(int frame=0;frame<18;frame++)rig.PresentAction(step,ActionKind.Drink,ActionPhase.Executing,false,0,13);
                int before=rig.ConsumeContacts;
                rig.PresentAction(step,ActionKind.Drink,ActionPhase.Executing,false,0,14);
                if(!rig.Cup.Visible||rig.Handling!="放低杯子 / 收手")
                    throw new InvalidOperationException("A repeated drink reuses or instantly removes its outgoing cup");
                bool released=false;
                for(int frame=0;frame<360;frame++)
                {
                    rig.PresentAction(step,ActionKind.Drink,ActionPhase.Done,false,0,14);
                    if(!released&&rig.ConsumeContacts!=before)
                        throw new InvalidOperationException("An outgoing cup's contact is charged to a new drink");
                    released|=!rig.Cup.Visible;
                    if(released&&!rig.CompletionVisible)break;
                }
                if(!released||rig.ConsumeContacts!=before+1||rig.Cup.Visible||rig.CompletionVisible)
                    throw new InvalidOperationException("Repeated drinking does not finish exactly one new contact");
            }
            finally{rig.Free();}
        }
        GD.Print("CONTACT_BOUNDARIES_PASS: both adult frames, delayed target-facing contact, deferred deposit, and distinct same-kind drink release");
    }

    private float MealCompletionTime(int identity,float delta)
    {
        var rig=Resident(identity,false,JobType.Builder);
        try
        {
            for(int i=0;i<180;i++)rig.PresentAction(1f/60,ActionKind.BuildHouse,ActionPhase.Executing,false,0,1);
            bool sawMeal=false;
            for(float elapsed=delta;elapsed<6;elapsed+=delta)
            {
                rig.PresentAction(delta,ActionKind.Eat,ActionPhase.Done,false,0,2);
                sawMeal|=rig.GestureEngaged(ActionKind.Eat);
                if(sawMeal&&!rig.CompletionVisible)return elapsed;
            }
            throw new InvalidOperationException("Meal handoff does not finish after stowing a held tool");
        }
        finally{rig.Free();}
    }

    private void ValidateCargoPlacement()
    {
        float largestGap=0,largestReleaseHeight=0;int supportedFrames=0;
        foreach(int identity in new[]{17,55})
        foreach(var resource in ResidentRig.CargoKinds)
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                float ground=resource==ResourceKind.Stone?-.055f:.12f;
                rig.CargoGroundHeight=(_,_)=>ground;
                var action=resource is ResourceKind.Wood or ResourceKind.Stone?ActionKind.Deposit:ActionKind.StoreInBuilding;
                rig.ShowCargo(true,resource);
                for(int frame=0;frame<120;frame++)rig.PresentAction(1f/60,action,ActionPhase.Executing,false,0);
                if(!rig.CargoMesh.Visible)throw new InvalidOperationException($"Cargo never reaches its two palm supports: {identity}/{resource}");
                rig.ShowCargo(false,resource);
                rig.PresentAction(1f/60,action,ActionPhase.Done,false,0);
                if(!rig.PlacingCargo||!rig.CargoMesh.Visible)
                    throw new InvalidOperationException("A completed placement removes its parcel before hands finish");
                var start=InTorso(rig.CargoMesh,rig);
                rig.PresentAction(.5f,action,ActionPhase.Done,true,0);
                if(InTorso(rig.CargoMesh,rig)!=start||!rig.PlacingCargo)
                    throw new InvalidOperationException("Pause advances a parcel placement");
                float descent=0;int released=-1;
                var (left,right)=rig.CargoSupports[resource];
                for(int frame=1;frame<=140;frame++)
                {
                    rig.PresentAction(1f/60,action,ActionPhase.Done,false,0);
                    if(rig.ReleasedCargo)
                    {
                        released=frame;break;
                    }
                    if(!rig.CargoMesh.Visible)
                    {
                        var lp=InTorso(rig.Hands[0],rig.Torso)*rig.Grip!.Position;
                        var rp=InTorso(rig.Hands[1],rig.Torso)*rig.Grip.Position;
                        throw new InvalidOperationException($"Placement loses its parcel: {identity}/{resource}, frame={frame}, placing={rig.PlacingCargo}, palm errors={lp.DistanceTo(rig.CargoSupportTarget(0)):F4}/{rp.DistanceTo(rig.CargoSupportTarget(1)):F4}, gap={lp.DistanceTo(rp):F4}");
                    }
                    var inTorso=InTorso(rig.CargoMesh,rig.Torso);
                    descent=MathF.Max(descent,start.Origin.Y-(rig.Torso.Transform*inTorso).Origin.Y);
                    for(int side=0;side<2;side++)
                    {
                        var palm=InTorso(rig.Hands[side],rig.Torso)*ResidentRig.CargoPalmContact;
                        float gap=palm.DistanceTo(inTorso*(side==0?left:right));
                        if(gap>.0211f)throw new InvalidOperationException($"A visible parcel loses its support: {identity}/{resource}/{gap}");
                        largestGap=MathF.Max(largestGap,gap);
                    }
                    supportedFrames++;
                }
                bool floor=action==ActionKind.Deposit;
                if(released<(floor?65:33)||released>=(floor?135:74)||descent<(floor?.44f:.025f)||rig.PlacingCargo)
                    throw new InvalidOperationException($"Placement does not settle and release at contact: {identity}/{resource}, frame={released}, drop={descent}");
                var releasePose=rig.Transform*InTorso(rig.CargoMesh,rig);
                var release=releasePose.Origin;
                float bottom=rig.CargoMesh.Mesh.GetAabb().Position.Y*releasePose.Basis.Y.Length();
                if(floor)
                {
                    float heightAtRelease=release.Y+bottom-ground;
                    if(heightAtRelease<-.0001f||heightAtRelease>.039f)
                        throw new InvalidOperationException($"Ground placement releases away from the support: {heightAtRelease} m");
                    largestReleaseHeight=MathF.Max(largestReleaseHeight,heightAtRelease);
                }
                else if(release.Y+bottom-ground<.20f)
                    throw new InvalidOperationException("Warehouse handoff releases at ground height");
                rig.PresentAction(.5f,ActionKind.None,ActionPhase.Idle,true,0);
                if(rig.Transform*InTorso(rig.CargoMesh,rig)!=releasePose)
                    throw new InvalidOperationException("Pause moves a released parcel");
                float height=release.Y;bool landed=false;
                for(int frame=0;frame<48&&rig.ReleasedCargo;frame++)
                {
                    rig.Position+=new Vector3(.006f,0,0);
                    rig.PresentAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                    var current=(rig.Transform*InTorso(rig.CargoMesh,rig)).Origin;
                    bool vertical=floor?current.Y<=height+.0001f&&current.Y+bottom>=ground-.0001f
                        :MathF.Abs(current.Y-release.Y)<=.0001f;
                    if(MathF.Abs(current.X-release.X)>.0001f||MathF.Abs(current.Z-release.Z)>.0001f||!vertical||rig.HeldObject.Length!=0)
                        throw new InvalidOperationException("A released parcel follows the hand or leaves its expected support height");
                    landed|=rig.CargoMesh.Visible&&(!floor||MathF.Abs(current.Y+bottom-ground)<.0001f);
                    height=current.Y;
                }
                if(!landed||rig.ReleasedCargo||rig.CargoMesh.Visible)
                    throw new InvalidOperationException("A released parcel fails to settle and disappear after contact");
                rig.ShowCargo(true,resource);
                for(int frame=0;frame<120;frame++)rig.PresentAction(1f/60,action,ActionPhase.Executing,false,0);
                rig.ShowCargo(false,resource);
                rig.PresentAction(1f/60,action,ActionPhase.Done,false,0);
                rig.PresentAction(1f/60,ActionKind.Flee,ActionPhase.Moving,false,0);
                if(rig.PlacingCargo||rig.ReleasedCargo)
                    throw new InvalidOperationException("Urgent movement leaves the old placement active");
                for(int frame=0;frame<80;frame++)rig.PresentAction(1f/60,ActionKind.Flee,ActionPhase.Moving,false,0);
                if(rig.CargoMesh.Visible)throw new InvalidOperationException("Urgent movement retains a depleted parcel after recovery");
            }
            finally{rig.Free();}
        }
        GD.Print($"CARGO_PLACEMENT_PASS: two adult frames, four cargo meshes, {supportedFrames} placed frames, worst palm gap={largestGap*1000:F1} mm, ground release height <= {largestReleaseHeight*1000:F1} mm; warehouse handoff holds its height; pause and interruption");
    }
}
