using System;
using System.Linq;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    // Runs on the disposable world created by MainGame's self-test.
    internal void ValidateResidentWorldMotion()
    {
        var sim=Game.Sim;
        var tiles=FindResidentMotionCorner();
        Vector3 Place(Vector2I tile)=>PositionAt(tile.X,tile.Y);
        var start=Place(tiles.Start);
        var corner=Place(tiles.Corner);
        var goal=Place(tiles.Goal);
        if(sim.InterveneSpawnHumans(tiles.Start.X,tiles.Start.Y,1,1)!=1)
            throw new InvalidOperationException("Resident route fixture could not spawn a walker");
        int slot=sim.Agents.AliveSlots().Single();
        sim.Agents.SetPosition(slot,tiles.Start.X,tiles.Start.Y);
        sim.Agents.SetAction(slot,ActionKind.Wander,ActionPhase.Moving);
        sim.Agents.ClearTarget(slot);
        SyncEntities();
        var rig=(ResidentRig)_people[sim.Society.Identity(slot)];
        rig.Rotation=new(0,HeadingYaw(Horizontal(corner-start)),0);
        _target=(start+goal)*.5f;_distance=10;_pitch=.62f;UpdateCamera();
        const float delta=1f/60;
        AnimateActors(delta);

        float peak=0,nearestCorner=float.PositiveInfinity,stepLift=0,plantDrift=0,landingTurn=0,landingTransfer=0;
        int pivotFrames=0,preparationFrames=0;
        float departureTransfer=0,departureRootDrift=0,departureFootDrift=0;
        bool retained=false,startedWork=false;
        var previousFeet=new[]{rig.Feet[0].GlobalPosition,rig.Feet[1].GlobalPosition};
        void MoveFor(int frames)
        {
            string before=sim.StateDigestString();
            for(int frame=0;frame<frames;frame++)
            {
                var previous=rig.Position;
                float previousYaw=rig.Rotation.Y;
                AnimateActors(delta);
                peak=MathF.Max(peak,Horizontal(rig.Position-previous).Length()/delta);
                nearestCorner=MathF.Min(nearestCorner,Horizontal(rig.Position-corner).Length());
                retained|=rig.Travel.Queued;
                if(!ScenerySegmentClear(previous,rig.Position))throw new InvalidOperationException("Resident crosses a scenery solid");
                var left=rig.Feet[0].GlobalPosition;var right=rig.Feet[1].GlobalPosition;
                if(rig.TryGetArrivalFacing(out _))
                {
                    landingTurn+=MathF.Abs(Mathf.AngleDifference(previousYaw,rig.Rotation.Y));
                    landingTransfer=MathF.Max(landingTransfer,MathF.Abs((rig.LowerGarmentFrame*(Vector3.Up*ResidentRig.HipHeight)).X));
                }
                if(rig.Travel.Preparation>0&&sim.Agents.PhaseOf(slot)==ActionPhase.Moving)
                {
                    preparationFrames++;
                    departureTransfer=MathF.Max(departureTransfer,MathF.Abs((rig.LowerGarmentFrame*(Vector3.Up*ResidentRig.HipHeight)).X));
                    departureRootDrift=MathF.Max(departureRootDrift,Horizontal(rig.Position-previous).Length());
                    departureFootDrift=MathF.Max(departureFootDrift,MathF.Max(Horizontal(left-previousFeet[0]).Length(),Horizontal(right-previousFeet[1]).Length()));
                }
                if(rig.IsPivoting)
                {
                    pivotFrames++;
                    float height=MathF.Max(left.Y-GroundHeight(left.X,left.Z),right.Y-GroundHeight(right.X,right.Z));
                    stepLift=MathF.Max(stepLift,height-.065f*rig.Scale.Y);
                    float support=MathF.Min(Horizontal(left-previousFeet[0]).Length(),Horizontal(right-previousFeet[1]).Length());
                    plantDrift=MathF.Max(plantDrift,support);
                }
                previousFeet[0]=left;previousFeet[1]=right;
                if(rig.GestureEngaged(ActionKind.Take))
                {
                    if(rig.IsPivoting||Horizontal(rig.Position-goal).Length()>.035f
                        ||!InteractionFacingAligned(rig.Rotation.Y,Horizontal(corner-rig.Position)))
                        throw new InvalidOperationException("Resident reaches while travelling or turning");
                    startedWork=true;
                }
            }
            if(before!=sim.StateDigestString())throw new InvalidOperationException("Resident playback changed simulation");
        }
        sim.Agents.SetPosition(slot,tiles.Corner.X,tiles.Corner.Y);MoveFor(56);
        sim.Agents.SetPosition(slot,tiles.Goal.X,tiles.Goal.Y);
        sim.Agents.SetAction(slot,ActionKind.Take,ActionPhase.Executing);
        sim.Agents.SetTarget(slot,tiles.Corner.X,tiles.Corner.Y);MoveFor(410);

        float oldPeak=Horizontal(SceneryActorPosition(start,corner,delta,10,false)-start).Length()/delta;
        if(!retained||nearestCorner>.026f||peak>ResidentRig.MaximumGaitSpeed||peak<.9f||oldPeak<5
            ||landingTurn<.60f||landingTransfer<.018f||pivotFrames<20||stepLift<.017f||plantDrift>.020f||!startedWork)
            throw new InvalidOperationException($"Resident world route: peak={peak}, corner={nearestCorner}, queue={retained}, landing turn={landingTurn}, pivots={pivotFrames}, lift={stepLift}, drift={plantDrift}, action={startedWork}");
        GD.Print($"RESIDENT_WORLD_MOTION_PASS: actual world actor, original root peak={oldPeak:F2} m/s, paced={peak:F2} m/s, recorded corner retained, pivot frames={pivotFrames}, foot lift={stepLift:F3} m, planted frame drift={plantDrift:F4} m, arrival then pickup, unchanged simulation");
        GD.Print($"ARRIVAL_TURN_PASS: {Mathf.RadToDeg(landingTurn):F1} degrees and {landingTransfer:F3} m lateral weight transfer during the two landing steps before any additional pivot and pickup");
        if(preparationFrames<6||departureTransfer<.015f||departureRootDrift>.001f||departureFootDrift>.010f)
            throw new InvalidOperationException($"Resident first step: frames={preparationFrames}, pelvis={departureTransfer}, root drift={departureRootDrift}, foot drift={departureFootDrift}");
        GD.Print($"RESIDENT_DEPARTURE_PASS: preparation={preparationFrames} frames, lateral pelvis transfer={departureTransfer:F3} m, root/feet drift={departureRootDrift:F4}/{departureFootDrift:F4} m before the first step");
        ValidateWorldCultivation(rig,slot,tiles.Goal,delta);
        ValidateResidentStations(tiles.Goal,tiles.Corner,delta);
        ValidateProjectedResidentMotion(delta);
        ValidateSceneHandoff(tiles.Start,tiles.Corner,delta);
        ValidateWorldStockContact(tiles.Start,delta);
    }

    internal void ValidateWorldStockContact()=>ValidateWorldStockContact(FindResidentMotionCorner().Start,1f/60);

    private void ValidateWorldStockContact(Vector2I tile,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        if(sim.InterveneSpawnHumans(tile.X,tile.Y,1,1)!=1)
            throw new InvalidOperationException("Stock contact fixture could not spawn a resident");
        int slot=agents.AliveSlots().Last();
        agents.SetLifeStage(slot,LifeStage.Adult);
        agents.SetPosition(slot,tile.X,tile.Y);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        foreach(var kind in ResidentRig.CargoKinds)agents.SetInventory(slot,kind,0);
        SyncEntities();
        var rig=(ResidentRig)_people[sim.Society.Identity(slot)];
        var logical=PositionAt(tile.X,tile.Y);
        _target=logical;_distance=8;_pitch=.62f;UpdateCamera();
        for(int i=0;i<3;i++)AnimateActors(delta);

        float amount=Math.Min(2,sim.Config.GroundStocks.TakeAmount);
        sim.GroundStocks.Deposit(tile.X,tile.Y,ResourceKind.Stone,amount,sim.Config.GroundStocks);
        agents.SetAction(slot,ActionKind.Take,ActionPhase.Executing);
        agents.SetTarget(slot,tile.X,tile.Y);
        string pending=sim.StateDigestString();
        if(!sim.Actions.TryGetPickupCandidate(slot,out var planned)
            ||!TryActionContact(rig,ActionKind.Take,planned,logical,out var plannedPoint,out _))
            throw new InvalidOperationException("World pickup cannot prepare at the actual stock");
        float preparedGap=float.PositiveInfinity;
        for(int i=0;i<240;i++)
        {
            AnimateActors(delta);
            if(rig.ScenePickupGrasped||rig.ScenePickupPieceVisible||rig.CargoMesh.Visible)
                throw new InvalidOperationException("Pending pickup shows an accepted object before simulation confirmation");
            preparedGap=Math.Min(preparedGap,(rig.HandWorldPose(1)*rig.Grip!.Position).DistanceTo(plannedPoint));
        }
        if(preparedGap>.085f||sim.StateDigestString()!=pending)
            throw new InvalidOperationException($"World pickup preparation misses its candidate: {preparedGap:F3} m");
        sim.Actions.Tick(sim.Clock);
        if(agents.HasTarget(slot)||!sim.Actions.TryGetCompletedContact(slot,out var taken)
            ||taken.Surface!=ActionSystem.ContactSurface.GroundStock||taken.Resource!=ResourceKind.Stone||taken.StockAmount!=amount
            ||!TryActionContact(rig,ActionKind.Take,taken,logical,out var pickup,out var stand))
            throw new InvalidOperationException("Take lost its actual resource and stock position on completion");
        string committed=sim.StateDigestString();
        float nearest=float.PositiveInfinity;
        bool grasped=false,carrying=false;
        int supportedLiftFrames=0,confirmationFrames=-1;
        for(int i=0;i<430;i++)
        {
            AnimateActors(delta);
            float distance=(rig.HandWorldPose(1)*rig.Grip!.Position).DistanceTo(pickup);
            nearest=Math.Min(nearest,distance);
            if(rig.ScenePickupGrasped&&!grasped&&(!rig.InteractionSettled||distance>.067f))
                throw new InvalidOperationException($"Ground grasp registered before supported contact: {distance:F3} m");
            if(rig.ScenePickupGrasped&&!grasped)confirmationFrames=i;
            grasped|=rig.ScenePickupGrasped;
            if(grasped&&rig.ScenePickupPieceVisible)supportedLiftFrames++;
            if(rig.CargoMesh.Visible&&!grasped)throw new InvalidOperationException("New cargo appeared before the pickup hand reached the stock");
            carrying|=rig.CargoMesh.Visible;
        }
        if(!grasped||!carrying||supportedLiftFrames<12||confirmationFrames>22||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World pickup failed: contact={nearest:F3} m, lift={supportedLiftFrames} frames, carrying={carrying}, stance={rig.Position.DistanceTo(stand):F3} m");

        agents.SetAction(slot,ActionKind.Deposit,ActionPhase.Executing);
        if(sim.Actions.TryGetCompletedContact(slot,out _))throw new InvalidOperationException("New action inherits an old stock site");
        agents.SetTarget(slot,tile.X,tile.Y);
        sim.Actions.Tick(sim.Clock);
        if(!sim.Actions.TryGetCompletedContact(slot,out var placed)||placed.Resource!=ResourceKind.Stone
            ||!TryActionContact(rig,ActionKind.Deposit,placed,logical,out var drop,out var dropStand))
            throw new InvalidOperationException("Deposit lost its actual stock position on completion");
        committed=sim.StateDigestString();
        float release=float.PositiveInfinity;
        int placementFrames=0,visiblePlacementFrames=0;
        float closestPlacement=float.PositiveInfinity;
        for(int i=0;i<360;i++)
        {
            AnimateActors(delta);
            if(rig.PlacingCargo)
            {
                placementFrames++;
                if(rig.CargoMesh.Visible)
                {
                    visiblePlacementFrames++;
                    var pose=rig.HandWorldPose(1)*rig.CargoMesh.Transform;
                    var basePoint=pose.Origin+Vector3.Up*rig.CargoMesh.Mesh.GetAabb().Position.Y*pose.Basis.Y.Length();
                    closestPlacement=Math.Min(closestPlacement,basePoint.DistanceTo(drop));
                }
            }
            if(!rig.ReleasedCargo)continue;
            var parcel=(rig.HandWorldPose(1)*rig.CargoMesh.Transform).Origin;
            release=Math.Min(release,Horizontal(parcel-drop).Length());
        }
        if(release>.067f||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World deposit released {release:F3} m away: placement {visiblePlacementFrames}/{placementFrames} frames, closest={closestPlacement:F3} m, stance={rig.Position.DistanceTo(dropStand):F3} m");

        var warehouse=Enumerable.Range(0,sim.World.Width*sim.World.Height)
            .Select(index=>new Vector2I(index%sim.World.Width,index/sim.World.Width))
            .Where(site=>site!=tile&&site.X>=2&&site.Y>=2&&site.X<sim.World.Width-2&&site.Y<sim.World.Height-2
                &&BuildingStore.CanPlaceAt(sim.World,BuildingKind.Storage,site.X,site.Y))
            .OrderBy(site=>site.DistanceSquaredTo(tile))
            .First(site=>SceneryPointClear(PositionAt(site.X,site.Y),1.7f));
        int storage=sim.Buildings.Place(sim.World,BuildingKind.Storage,warehouse.X,warehouse.Y,10);
        if(storage<0)throw new InvalidOperationException("Warehouse contact fixture could not place a warehouse");
        sim.Buildings.MarkComplete(storage,sim.Clock);
        sim.Storage.SetCapacity(storage,sim.Config.GroundStocks.DropAmount*4);
        BuildBuildings();
        var storageTile=PositionAt(warehouse.X,warehouse.Y);
        agents.SetPosition(slot,warehouse.X,warehouse.Y);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        agents.SetInventory(slot,ResourceKind.Wood,sim.Config.GroundStocks.DropAmount+2);
        rig.Position=storageTile;
        _target=storageTile;UpdateCamera();
        for(int i=0;i<180;i++)AnimateActors(delta);
        agents.SetAction(slot,ActionKind.StoreInBuilding,ActionPhase.Executing);
        agents.SetTarget(slot,warehouse.X,warehouse.Y);
        sim.Actions.Tick(sim.Clock);
        if(!sim.Actions.TryGetCompletedContact(slot,out var stored)||stored.Surface!=ActionSystem.ContactSurface.Storage
            ||stored.Resource!=ResourceKind.Wood||agents.InventoryOf(slot,ResourceKind.Wood)<=0
            ||!TryActionContact(rig,ActionKind.StoreInBuilding,stored,storageTile,out var crate,out var crateStand))
            throw new InvalidOperationException("Partial warehouse transfer lost its accepted resource or crate");
        committed=sim.StateDigestString();
        float crateRelease=float.PositiveInfinity;
        float closestCrate=float.PositiveInfinity;
        Vector3 closestOffset=Vector3.Zero;
        int cratePlacementFrames=0;
        bool remainingCargoVisible=false;
        for(int i=0;i<480;i++)
        {
            AnimateActors(delta);
            if(rig.PlacingCargo&&rig.CargoMesh.Visible)
            {
                var held=rig.HandWorldPose(1)*rig.CargoMesh.Transform;
                var offset=held.Origin+Vector3.Up*rig.CargoMesh.Mesh.GetAabb().Position.Y*held.Basis.Y.Length()-crate;
                if(offset.Length()<closestCrate){closestCrate=offset.Length();closestOffset=offset;}
                cratePlacementFrames++;
            }
            if(rig.ReleasedCargo)
            {
                var parcel=rig.HandWorldPose(1)*rig.CargoMesh.Transform;
                var bottom=parcel.Origin+Vector3.Up*rig.CargoMesh.Mesh.GetAabb().Position.Y*parcel.Basis.Y.Length();
                crateRelease=Math.Min(crateRelease,bottom.DistanceTo(crate));
            }
            else if(float.IsFinite(crateRelease)&&rig.CargoMesh.Visible)remainingCargoVisible=true;
        }
        if(crateRelease>.078f||!remainingCargoVisible||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World warehouse release missed its crate: gap={crateRelease:F3} m, closest={closestCrate:F3} m ({closestOffset:F3}) in {cratePlacementFrames} frames, stance={rig.Position.DistanceTo(crateStand):F3} m, remainder visible={remainingCargoVisible}");
        GD.Print($"WORLD_STOCK_CONTACT_PASS: palm {nearest:F3} m from depleted stock, releases {release:F3} m from pile and {crateRelease:F3} m from crate, partial cargo recovers, presentation is read-only");
        ValidateInterruptedPickup(rig,slot,tile,logical,delta);
        GD.Print($"WORLD_PICKUP_INTENT_PASS: candidate reached within {preparedGap:F3} m, confirmed after {confirmationFrames} frames without a second reach");
    }

    private void ValidateInterruptedPickup(ResidentRig rig,int slot,Vector2I tile,Vector3 logical,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        foreach(var kind in ResidentRig.CargoKinds)agents.SetInventory(slot,kind,0);
        agents.SetPosition(slot,tile.X,tile.Y);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        rig.Position=logical;_target=logical;UpdateCamera();
        for(int i=0;i<150;i++)AnimateActors(delta);
        agents.SetAction(slot,ActionKind.Take,ActionPhase.Executing);
        agents.SetTarget(slot,tile.X,tile.Y);
        for(int i=0;i<230;i++)AnimateActors(delta);
        if(!rig.GestureEngaged(ActionKind.Take)||rig.ScenePickupGrasped)
            throw new InvalidOperationException("Lost-source fixture did not prepare its original reach");
        float removed=sim.GroundStocks.Withdraw(tile.X,tile.Y,ResourceKind.Stone,float.MaxValue);
        string lost=sim.StateDigestString();
        for(int i=0;i<45;i++)
        {
            AnimateActors(delta);
            if(rig.GestureEngaged(ActionKind.Take)||rig.ScenePickupGrasped||rig.ScenePickupPieceVisible)
                throw new InvalidOperationException("Resident keeps reaching for depleted stock");
        }
        if(removed<=0||sim.StateDigestString()!=lost)throw new InvalidOperationException("Source loss was not a read-only presentation change");
        sim.GroundStocks.Deposit(tile.X,tile.Y,ResourceKind.Stone,removed,sim.Config.GroundStocks);
        sim.Actions.Tick(sim.Clock);
        for(int i=0;i<290&&!rig.ScenePickupGrasped;i++)AnimateActors(delta);
        if(!rig.ScenePickupGrasped||!rig.ScenePickupPieceVisible||rig.ScenePickupPiecePose is not {} before)
            throw new InvalidOperationException("Interruption fixture did not reach the held-object stage");

        agents.SetAction(slot,ActionKind.Flee,ActionPhase.Moving);
        agents.SetPosition(slot,tile.X+1,tile.Y);
        agents.ClearTarget(slot);
        string committed=sim.StateDigestString();
        AnimateActors(delta);
        var after=rig.ScenePickupPiecePose;
        float firstStep=after.HasValue?before.Origin.DistanceTo(after.Value.Origin):float.PositiveInfinity;
        if(!rig.RecoveringPickup||firstStep>.040f)
            throw new InvalidOperationException($"Interrupted pickup lost the object or jumped {firstStep:F3} m (recovery={rig.RecoveringPickup}, visible={rig.Visible})");
        int held=0;
        float travel=0,step=0;
        var start=rig.Position;
        for(int i=0;i<90;i++)
        {
            AnimateActors(delta);
            if(rig.ScenePickupPiecePose is not {} current)continue;
            held++;travel=Math.Max(travel,Horizontal(rig.Position-start).Length());
            step=Math.Max(step,current.Origin.DistanceTo(after!.Value.Origin));after=current;
        }
        if(held<20||travel<.03f||step>.060f||rig.RecoveringPickup||rig.ScenePickupPieceVisible||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"Pickup recovery loses continuity: held={held}, travel={travel:F3}, step={step:F3}");
        GD.Print($"WORLD_PICKUP_INTERRUPT_PASS: abandoned depleted source, kept confirmed object for {held} departure frames, max step {step:F3} m, no stale object or simulation write");
    }

    private void ValidateSceneHandoff(Vector2I giverTile,Vector2I receiverTile,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        var existing=agents.AliveSlots().ToArray();
        foreach(int slot in existing)
        {
            agents.SetPosition(slot,sim.World.Width-2,sim.World.Height-2);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        }
        if(sim.InterveneSpawnHumans(giverTile.X,giverTile.Y,2,2)!=2)
            throw new InvalidOperationException("Handoff fixture could not spawn two residents");
        var added=agents.AliveSlots().Except(existing).ToArray();
        int giverSlot=added[0],receiverSlot=added[1];
        agents.SetLifeStage(giverSlot,LifeStage.Adult);agents.SetLifeStage(receiverSlot,LifeStage.Adult);
        agents.SetPosition(giverSlot,giverTile.X,giverTile.Y);
        agents.SetPosition(receiverSlot,receiverTile.X,receiverTile.Y);
        agents.SetAction(receiverSlot,ActionKind.None,ActionPhase.Idle);agents.SetHunger(receiverSlot,.92f);
        agents.SetInventory(giverSlot,ResourceKind.Food,sim.Config.Relationship.ShareFoodAmount);
        agents.SetAction(giverSlot,ActionKind.ShareFood,ActionPhase.Executing);
        sim.Actions.Tick(sim.Clock);
        if(!sim.Actions.TryGetPresentationPartner(giverSlot,out int actual)||actual!=receiverSlot)
            throw new InvalidOperationException("World fixture does not have a committed food exchange");

        SyncEntities();
        long giverId=sim.Society.Identity(giverSlot),receiverId=sim.Society.Identity(receiverSlot);
        var giver=(ResidentRig)_people[giverId];var receiver=(ResidentRig)_people[receiverId];
        var start=giver.Position;var end=receiver.Position;
        giver.Rotation=new(0,HeadingYaw(Horizontal(end-start)),0);
        receiver.Rotation=new(0,HeadingYaw(Horizontal(start-end)),0);
        if(!HandoffStands(giver,receiver,out var give,out var take))
            throw new InvalidOperationException("Adjacent fixture has no safe handoff placement");
        var midpoint=Horizontal((give+take)*.5f);
        var obstacleKey=new Vector2I((int)MathF.Round(midpoint.X*.5f),(int)MathF.Round(midpoint.Y*.5f));
        bool hadSolid=_scenerySolids.TryGetValue(obstacleKey,out var previousSolid);
        try
        {
            _scenerySolids[obstacleKey]=(midpoint,.10f);
            if(!ValidHandoffStand(start,give)||!ValidHandoffStand(end,take)||HandoffStands(giver,receiver,out _,out _))
                throw new InvalidOperationException("Handoff routes can reach through a solid between two clear stands");
        }
        finally
        {
            if(hadSolid)_scenerySolids[obstacleKey]=previousSolid;
            else _scenerySolids.Remove(obstacleKey);
        }
        _target=(start+end)*.5f;_distance=9;_pitch=.62f;UpdateCamera();
        string committed=sim.StateDigestString();
        ResidentHandoff? motion=null;
        int parcelFrames=0;
        float approach=0;
        for(int i=0;i<490;i++)
        {
            AnimateActors(delta);
            if(motion==null&&_residentHandoffs.Count>0)motion=_residentHandoffs[0].Motion;
            if(motion?.ParcelVisible==true)
            {
                parcelFrames++;
                if(!motion.Transferred&&(!giver.InteractionSettled||!receiver.InteractionSettled))
                    throw new InvalidOperationException("Parcel appears before both world residents settle");
            }
            if(motion?.UsesMeeting==true)approach=MathF.Max(approach,Horizontal(giver.Position-start).Length());
        }
        if(motion==null||!motion.Transferred||!motion.Finished||approach<.20f||parcelFrames<18
            ||_residentHandoffs.Count!=0||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World handoff misses approach, transfer or one-shot cleanup: distance={approach}, frames={parcelFrames}, transferred={motion?.Transferred}");
        GD.Print($"WORLD_HANDOFF_PASS: committed adjacent-tile recipient, {approach:F3} m visual approach, {parcelFrames} supported frames, obstruction rejected and read-only playback");

        agents.SetInventory(giverSlot,ResourceKind.Food,sim.Config.Relationship.ShareFoodAmount);
        agents.SetHunger(receiverSlot,.92f);
        agents.SetAction(giverSlot,ActionKind.ShareFood,ActionPhase.Executing);
        sim.Actions.Tick(sim.Clock);
        ResidentHandoff? outgoing=null;
        for(int i=0;i<360;i++)
        {
            AnimateActors(delta);
            if(_residentHandoffs.Count==0||!_residentHandoffs[0].Motion.ParcelVisible)continue;
            outgoing=_residentHandoffs[0].Motion;
            break;
        }
        if(outgoing==null||outgoing.Transferred)throw new InvalidOperationException("Handoff revision fixture did not reach an untransferred offer");
        agents.SetAction(giverSlot,ActionKind.ShareFood,ActionPhase.Done);
        committed=sim.StateDigestString();
        AnimateActors(delta);
        if(outgoing.UsesMeeting||!outgoing.ParcelVisible)throw new InvalidOperationException("Superseded handoff failed to return its parcel to the current holder");
        for(int i=0;i<120;i++)AnimateActors(delta);
        if(outgoing.Transferred||!outgoing.Finished||_residentHandoffs.Count!=0||sim.StateDigestString()!=committed)
            throw new InvalidOperationException("Superseded handoff reaches the old recipient or leaves a parcel");
        GD.Print("WORLD_HANDOFF_RETARGET_PASS: superseded same-kind share recovers its parcel without transferring to the old recipient");
    }

    private void ValidateProjectedResidentMotion(float delta)
    {
        var sim=Game.Sim;
        var route=FindProjectedMotionRoute();
        Vector3 Place(Int2 tile)=>PositionAt(tile.X,tile.Y);
        var start=route[0];var goal=route[^1];
        if(sim.InterveneSpawnHumans(start.X,start.Y,1,1)!=1)
            throw new InvalidOperationException("Projected route fixture could not spawn a walker");
        int slot=sim.Agents.AliveSlots().Last();
        sim.Agents.SetPosition(slot,start.X,start.Y);
        sim.Agents.SetTarget(slot,goal.X,goal.Y);
        sim.Agents.SetAction(slot,ActionKind.Take,ActionPhase.Moving);
        SyncEntities();
        var rig=(ResidentRig)_people[sim.Society.Identity(slot)];
        rig.Rotation=new(0,HeadingYaw(Horizontal(Place(route[1])-Place(start))),0);
        _target=Place(start);_distance=11;UpdateCamera();

        double tickFraction=0;
        long tick=0;
        int crossed=0,projectedFrames=0,preparations=0;
        float lowCruise=float.PositiveInfinity,peak=0;
        float previousSpeed=0,peakDepartureAcceleration=0;
        int supportPairs=0;
        float squaredDrift=0,minimumClearance=float.PositiveInfinity,swingClearance=0;
        string lowestPose="";
        var previousSoles=new Vector3[2];
        var previousSupport=new bool[2];
        bool moving=true,working=false;
        for(int frame=0;frame<1800&&!working;frame++)
        {
            tickFraction+=delta*Game.ObservationTickRate;
            while(tickFraction>=1&&moving)
            {
                tickFraction--;
                var previous=new Int2(sim.Agents.XOf(slot),sim.Agents.YOf(slot));
                sim.Actions.Tick(++tick);
                var tile=new Int2(sim.Agents.XOf(slot),sim.Agents.YOf(slot));
                if(tile!=previous)crossed++;
                moving=sim.Agents.PhaseOf(slot)==ActionPhase.Moving;
            }
            var before=rig.Position;
            _target=before;UpdateCamera();
            string digest=sim.StateDigestString();
            long searches=sim.Pathfinder.TotalSearches;
            AnimateActors(delta,(float)Math.Min(1,tickFraction));
            if(sim.StateDigestString()!=digest||sim.Pathfinder.TotalSearches!=searches)
                throw new InvalidOperationException("Projected travel changed simulation or simulation search counts");
            float speed=Horizontal(rig.Position-before).Length()/delta;
            if(crossed==0)peakDepartureAcceleration=MathF.Max(peakDepartureAcceleration,(speed-previousSpeed)/delta);
            previousSpeed=speed;
            peak=MathF.Max(peak,speed);
            if(rig.Travel.Projecting)projectedFrames++;
            if(rig.Travel.Preparation>0&&crossed>0&&crossed<route.Length-1)preparations++;
            if(crossed>0&&crossed<route.Length-2)lowCruise=MathF.Min(lowCruise,speed);
            for(int side=0;side<2;side++)
            {
                float phase=Mathf.PosMod(rig.GaitCycle+side*.5f,1);
                float support=rig.GaitSupport;
                var boot=rig.Feet[side].GlobalTransform;
                var mark=boot*rig.BootSoleCurve[12];
                bool planted=rig.Travel.Projecting&&speed>.65f&&phase>.32f*support&&phase<.57f*support;
                if(planted&&previousSupport[side])
                {
                    squaredDrift+=Horizontal(mark-previousSoles[side]).LengthSquared();
                    supportPairs++;
                }
                previousSupport[side]=planted;previousSoles[side]=mark;
                if(!rig.Travel.Projecting||speed<.65f)continue;
                float clearance=float.PositiveInfinity;
                foreach(var sole in rig.BootSoleCurve)
                {
                    var point=boot*sole;
                    clearance=MathF.Min(clearance,point.Y-GroundHeight(point.X,point.Z));
                }
                if(clearance<minimumClearance)
                {
                    minimumClearance=clearance;
                    lowestPose=$"{frame}/{side}, phase={phase:F3}, speed={speed:F2}/{rig.GaitSpeed:F2}, camera={_camera.Position.DistanceTo(rig.Position):F2}";
                }
                if(phase>support)swingClearance=MathF.Max(swingClearance,clearance);
            }
            if(!ScenerySegmentClear(before,rig.Position)||!SceneryLegSupported(before,rig.Position))
                throw new InvalidOperationException("Projected travel crossed scenery or lost ground support");
            working=rig.GestureEngaged(ActionKind.Take);
            if(working&&(!rig.InteractionSettled||Horizontal(rig.Position-Place(goal)).Length()>.04f))
                throw new InvalidOperationException("Projected walker reached for a resource before settling");
        }
        if(crossed!=route.Length-1||!working||projectedFrames<180||preparations!=0||lowCruise<.40f||peak>ResidentRig.MaximumGaitSpeed+.001f)
            throw new InvalidOperationException($"Projected resident route: tiles={crossed}/{route.Length-1}, projected frames={projectedFrames}, false starts={preparations}, cruise floor={lowCruise}, peak={peak}, work={working}");
        float rmsDrift=MathF.Sqrt(squaredDrift/Math.Max(1,supportPairs));
        if(supportPairs<65||rmsDrift>.008f||minimumClearance<-.018f||swingClearance<.045f||peakDepartureAcceleration>4.2f)
            throw new InvalidOperationException($"World outsole contact: stance pairs={supportPairs}, RMS drift={rmsDrift}, lowest sole={minimumClearance} ({lowestPose}), swing={swingClearance}, departure acceleration={peakDepartureAcceleration}");
        GD.Print($"WORLD_OUTSOLE_PASS: {supportPairs} mid-stance frame pairs, RMS sole drift={rmsDrift:F5} m/frame, lowest outsole={minimumClearance:F4} m, swing clearance={swingClearance:F4} m, departure acceleration={peakDepartureAcceleration:F2} m/s² on actual terrain");

        sim.Agents.SetTarget(slot,start.X,start.Y);
        sim.Agents.SetAction(slot,ActionKind.Wander,ActionPhase.Moving);
        AnimateActors(delta,.7f);
        if(!rig.Travel.Projecting)throw new InvalidOperationException("New destination did not build a projection");
        sim.World.SetWalkable(start.X,start.Y,false);
        try
        {
            AnimateActors(delta,.8f);
            if(rig.Travel.Projecting)throw new InvalidOperationException("Projected route survived a topology change that blocked its destination");
        }
        finally {sim.World.SetWalkable(start.X,start.Y,true);}
        GD.Print($"RESIDENT_ROUTE_PROJECTION_PASS: {crossed} simulated legs, {projectedFrames} projected frames, {lowCruise:F2}–{peak:F2} m/s through the route, no repeated departures, supported turn, arrival before pickup, topology invalidation and read-only playback");
    }

    private Int2[] FindProjectedMotionRoute()
    {
        var world=Game.Sim.World;
        for(int y=3;y<world.Height-3;y++)for(int x=3;x<world.Width-8;x++)
        {
            if(!world.TileAt(x,y).Walkable)continue;
            foreach(int dy in new[]{-2,2})
            {
                var path=_observerPaths.FindPath(x,y,x+5,y+dy,_observedRoute);
                if(!path.Success||path.Length<6||path.Length>9)continue;
                bool supported=true,bends=false;
                Vector2 previous=Vector2.Zero;
                for(int i=1;i<path.Length&&supported;i++)
                {
                    var a=_observedRoute[i-1];var b=_observedRoute[i];
                    var from=PositionAt(a.X,a.Y);var to=PositionAt(b.X,b.Y);
                    var direction=Horizontal(to-from).Normalized();
                    if(i>1&&i<path.Length-1&&direction.Dot(previous)<.85f)bends=true;
                    previous=direction;
                    supported=ScenerySegmentClear(from,to)&&SceneryLegSupported(from,to);
                }
                if(supported&&bends)return _observedRoute[..path.Length];
            }
        }
        throw new InvalidOperationException("No supported multi-tile route with an interior turn");
    }

    private void ValidateWorldCultivation(ResidentRig rig,int slot,Vector2I tile,float delta)
    {
        var sim=Game.Sim;
        sim.Agents.SetPosition(slot,tile.X,tile.Y);
        sim.Agents.SetTarget(slot,tile.X,tile.Y);
        sim.Agents.SetAction(slot,ActionKind.Farm,ActionPhase.Executing);
        string before=sim.StateDigestString();
        int contacts=0;
        float first=float.PositiveInfinity,last=float.NegativeInfinity;
        for(int frame=0;frame<420;frame++)
        {
            AnimateActors(delta);
            if(frame<160||!rig.GestureEngaged(ActionKind.Farm))continue;
            var edge=rig.ToolMesh.GlobalTransform*ResidentRig.HoeCuttingEdge;
            if(MathF.Abs(edge.Y-GroundHeight(edge.X,edge.Z))>.035f)continue;
            contacts++;
            float depth=(rig.Transform.AffineInverse()*edge).Z;
            first=MathF.Min(first,depth);last=MathF.Max(last,depth);
        }
        if(contacts<15||last-first<.14f||sim.StateDigestString()!=before)
            throw new InvalidOperationException($"World cultivation lacks supported soil contact: frames={contacts}, path={last-first}");
        GD.Print($"WORLD_CULTIVATION_PASS: {contacts} frames near the actual terrain, {last-first:F3} m visible pull, unchanged simulation");
    }

    private void ValidateResidentStations(Vector2I tile,Vector2I departure,float delta)
    {
        var sim=Game.Sim;
        if(sim.InterveneSpawnHumans(tile.X,tile.Y,2,1)!=2)
            throw new InvalidOperationException("Resident station fixture could not spawn two neighbours");
        var slots=sim.Agents.AliveSlots().ToArray();
        foreach(int slot in slots)
        {
            sim.Agents.SetPosition(slot,tile.X,tile.Y);
            sim.Agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        }
        SyncEntities();
        var residents=slots.Select(slot=>(ResidentRig)_people[sim.Society.Identity(slot)]).ToArray();
        void PresentStationFrames()
        {
            string before=sim.StateDigestString();
            for(int frame=0;frame<150;frame++)AnimateActors(delta);
            if(before!=sim.StateDigestString())throw new InvalidOperationException("Resident stations changed simulation");
        }
        PresentStationFrames();
        float separation=float.PositiveInfinity;
        for(int i=0;i<residents.Length;i++)for(int j=i+1;j<residents.Length;j++)
            separation=MathF.Min(separation,Horizontal(residents[i].Position-residents[j].Position).Length());
        var reserved=residents.Select(rig=>rig.StationSlot).ToArray();
        if(residents.Length!=3||reserved.Any(station=>station<0)||reserved.Distinct().Count()!=3||separation<.50f)
            throw new InvalidOperationException($"Resident station separation: slots={string.Join(',',reserved)}, spacing={separation}");
        sim.Agents.SetPosition(slots[0],departure.X,departure.Y);
        sim.Agents.SetAction(slots[0],ActionKind.Wander,ActionPhase.Moving);
        PresentStationFrames();
        if(residents[1].StationSlot!=reserved[1]||residents[2].StationSlot!=reserved[2])
            throw new InvalidOperationException("Stationary residents shuffled when a neighbour left");
        GD.Print($"RESIDENT_STATIONS_PASS: three residents in one tile, minimum spacing={separation:F3} m, stable when a neighbour leaves, unchanged simulation");
    }

    private (Vector2I Start,Vector2I Corner,Vector2I Goal) FindResidentMotionCorner()
    {
        var world=Game.Sim.World;
        for(int y=2;y<world.Height-2;y++)for(int x=2;x<world.Width-2;x++)
        {
            var start=PositionAt(x-1,y);var turn=PositionAt(x,y);var goal=PositionAt(x,y+1);
            if(MathF.Abs(start.Y-turn.Y)>.07f||MathF.Abs(goal.Y-turn.Y)>.07f)continue;
            if(Enumerable.Range(0,ResidentStationOffsets.Length).Count(station=>TryResidentStation(new(x,y+1),station,out _))<3)continue;
            if(ScenerySegmentClear(start,turn)&&ScenerySegmentClear(turn,goal)&&SceneryLegSupported(start,turn)&&SceneryLegSupported(turn,goal))
                return (new(x-1,y),new(x,y),new(x,y+1));
        }
        throw new InvalidOperationException("No supported resident motion corner");
    }
}
