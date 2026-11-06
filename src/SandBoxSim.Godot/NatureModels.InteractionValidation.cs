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
        ValidateObservedActionHandoffs();
        ValidateConsumptionInterrupts();
        ValidatePartnerLoss();
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
        float largestGap=0;int supportedFrames=0;
        foreach(int identity in new[]{17,55})
        foreach(var resource in ResidentRig.CargoKinds)
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                const float ground=.12f;
                rig.CargoGroundHeight=(_,_)=>ground;
                var action=resource==ResourceKind.Wood?ActionKind.Deposit:ActionKind.StoreInBuilding;
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
                for(int frame=1;frame<=84;frame++)
                {
                    rig.PresentAction(1f/60,action,ActionPhase.Done,false,0);
                    if(rig.ReleasedCargo)
                    {
                        released=frame;break;
                    }
                    if(!rig.CargoMesh.Visible)throw new InvalidOperationException("Placement hides the parcel before release");
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
                if(released<33||released>=74||descent<.025f||rig.PlacingCargo)
                    throw new InvalidOperationException($"Placement does not settle and release at contact: {identity}/{resource}, frame={released}, drop={descent}");
                var releasePose=rig.Transform*InTorso(rig.CargoMesh,rig);
                var release=releasePose.Origin;
                float bottom=rig.CargoMesh.Mesh.GetAabb().Position.Y*releasePose.Basis.Y.Length();
                rig.PresentAction(.5f,ActionKind.None,ActionPhase.Idle,true,0);
                if(rig.Transform*InTorso(rig.CargoMesh,rig)!=releasePose)
                    throw new InvalidOperationException("Pause moves a released parcel");
                float height=release.Y;bool landed=false;
                for(int frame=0;frame<48&&rig.ReleasedCargo;frame++)
                {
                    rig.Position+=new Vector3(.006f,0,0);
                    rig.PresentAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                    var current=(rig.Transform*InTorso(rig.CargoMesh,rig)).Origin;
                    if(MathF.Abs(current.X-release.X)>.0001f||MathF.Abs(current.Z-release.Z)>.0001f||
                        current.Y>height+.0001f||current.Y+bottom<ground-.0001f||rig.HeldObject.Length!=0)
                        throw new InvalidOperationException("A released parcel follows the hand, rises, or penetrates the ground");
                    landed|=MathF.Abs(current.Y+bottom-ground)<.0001f&&rig.CargoMesh.Visible;
                    height=current.Y;
                }
                if(!landed||rig.ReleasedCargo||rig.CargoMesh.Visible)
                    throw new InvalidOperationException("A released parcel fails to settle and disappear after contact");
                rig.ShowCargo(true,resource);
                for(int frame=0;frame<120;frame++)rig.PresentAction(1f/60,action,ActionPhase.Executing,false,0);
                rig.ShowCargo(false,resource);
                rig.PresentAction(1f/60,action,ActionPhase.Done,false,0);
                rig.PresentAction(1f/60,ActionKind.Flee,ActionPhase.Moving,false,0);
                if(rig.PlacingCargo||rig.CargoMesh.Visible)
                    throw new InvalidOperationException("Urgent movement retains a depleted parcel");
            }
            finally{rig.Free();}
        }
        GD.Print($"CARGO_PLACEMENT_PASS: two adult frames, four cargo meshes, {supportedFrames} placed frames, worst palm gap={largestGap*1000:F1} mm; contact release, ground landing, pause and interruption");
    }
}
