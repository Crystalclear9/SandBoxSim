using System;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateInteractionSequencing()
    {
        int contactTransfers=0,visibleLoads=0;
        foreach(int identity in new[]{17,55})
        {
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
                foreach(var action in new[]{ActionKind.BuildHouse,ActionKind.GatherWood,ActionKind.Farm,
                    ActionKind.GatherStone,ActionKind.GatherIron,ActionKind.Hunt,ActionKind.Attack,
                    ActionKind.Eat,ActionKind.Drink,ActionKind.GatherFood,ActionKind.Socialize,
                    ActionKind.ShareFood,ActionKind.Trade,ActionKind.Deposit,ActionKind.StoreInBuilding})
                {
                    for(int frame=0;frame<240;frame++)
                    {
                        bool held=rig.ToolHeld;
                        var previousTool=InTorso(rig.ToolMesh,rig.Torso);
                        rig.PresentAction(1f/60,action,ActionPhase.Executing,false,.3f);
                        var next=InTorso(rig.ToolMesh,rig.Torso);
                        if(held!=rig.ToolHeld)
                        {
                            contactTransfers++;
                            if(previousTool.Origin.DistanceTo(next.Origin)>.025f)
                                throw new InvalidOperationException($"Tool ownership changes across a gap: {identity}/{action}");
                        }
                    }
                    if(!rig.GestureEngaged(action))
                        throw new InvalidOperationException($"Requested interaction does not engage: {identity}/{action}, {rig.Handling}");
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
        GD.Print($"INTERACTION_SEQUENCE_PASS: two adult frames, rendered arrival, fifteen action families, {contactTransfers} contact ownership changes, {visibleLoads} supported load frames, resource replacement, short completion, cancellation and pause");
    }
}
