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
    internal void ValidateCaravanPresentation()
    {
        var sim=Game.Sim;var agents=sim.Agents;
        var from=new Vector2I(10,22);var to=new Vector2I(48,22);
        for(int y=0;y<sim.World.Height;y++)for(int x=0;x<sim.World.Width;x++)
            sim.World.SetTerrain(x,y,TerrainKind.Grass);
        sim.World.RefreshSpatialIndex();
        int Warehouse(Vector2I centre,int id)
        {
            if(sim.InterveneSpawnHumans(centre.X,centre.Y,8,0)!=8)
                throw new InvalidOperationException("Caravan fixture could not populate a settlement");
            sim.Settlements.Restore(id,centre.X,centre.Y,SettlementTier.Village,8,2,1,0,0,false);
            int building=sim.Buildings.Place(sim.World,BuildingKind.Storage,centre.X,centre.Y+1,1);
            if(building<0)throw new InvalidOperationException("Caravan fixture could not place a warehouse");
            sim.Buildings.MarkComplete(building,0);sim.Storage.SetCapacity(building,600);
            return building;
        }
        int seller=Warehouse(from,1),buyer=Warehouse(to,2);
        sim.Society.TickDay(0);sim.Civilizations.TickDay(0);
        sim.Storage.Deposit(seller,ResourceKind.Food,200);sim.Storage.Deposit(buyer,ResourceKind.Wood,300);
        if(!sim.Civilizations.TryDispatch(1,2))throw new InvalidOperationException("Caravan fixture did not dispatch from real inventory");
        var caravan=sim.Civilizations.Caravans.Single();
        int slot=unchecked((int)caravan.Merchant);
        agents.SetLifeStage(slot,LifeStage.Adult);
        foreach(var kind in ResidentRig.CargoKinds)agents.SetInventory(slot,kind,0);
        ValidateWorldDimensions();SyncEntities();
        var rig=(ResidentRig)_people[caravan.Merchant];
        const float dt=1f/60;
        void LookAtMerchant()
        {
            _target=rig.Position;_distance=8;_pitch=.62f;UpdateCamera();
        }
        void Play(int frames)
        {
            string digest=sim.StateDigestString();
            for(int i=0;i<frames;i++)AnimateActors(dt);
            if(sim.StateDigestString()!=digest)throw new InvalidOperationException("Caravan playback changed the ledger");
        }
        void JumpToStop(Vector2I tile)
        {
            agents.SetPosition(slot,tile.X,tile.Y);
            rig.Position=PositionAt(tile.X,tile.Y);rig.Travel=default;
            LookAtMerchant();
        }

        LookAtMerchant();Play(90);
        if(rig.SupportedCargo!=ResourceKind.Food||!rig.SupportingCaravanCargo||agents.InventoryOf(slot,ResourceKind.Food)>0)
            throw new InvalidOperationException("Outbound caravan does not show its actual reserved food");
        var before=rig.Position;
        sim.Civilizations.TickFast(1);Play(75);
        if(Horizontal(rig.Position-before).Length()<.12f||!rig.SupportingCaravanCargo)
            throw new InvalidOperationException("The outbound merchant does not carry the parcel during actual travel");

        agents.SetInventory(slot,ResourceKind.Food,1);agents.SetHunger(slot,.80f);
        sim.Civilizations.TickFast(2);Play(1);
        if(agents.ActionOf(slot)!=ActionKind.None||!rig.SupportingCaravanCargo)
            throw new InvalidOperationException("A needs break removes the shipment before the merchant can stow it");
        Play(120);
        if(rig.SupportingCaravanCargo||rig.SupportedCargo!=ResourceKind.Food||caravan.Cargo<=0)
            throw new InvalidOperationException("Personal food and the reserved shipment are treated as the same load");
        agents.SetHunger(slot,0);sim.Civilizations.TickFast(3);Play(120);
        if(!rig.SupportingCaravanCargo||rig.SupportedCargo!=ResourceKind.Food)
            throw new InvalidOperationException("Resuming trade does not restore the reserved shipment");

        JumpToStop(to);sim.Civilizations.TickFast(4);Play(1);
        if(!caravan.Returning||!caravan.PaymentInTransit||rig.SupportedCargo!=ResourceKind.Food)
            throw new InvalidOperationException("Buyer settlement replaces the held parcel before stowing");
        Play(120);
        if(!rig.SupportingCaravanCargo||rig.SupportedCargo!=ResourceKind.Wood||agents.InventoryOf(slot,ResourceKind.Wood)>0)
            throw new InvalidOperationException("The return leg does not show its actual payment");
        JumpToStop(from);sim.Civilizations.TickFast(5);SyncEntities();
        if(!caravan.Delivered||_people[caravan.Merchant]!=rig||!rig.SupportingCaravanCargo)
            throw new InvalidOperationException("Returning merchant changes outfit before recovering its parcel");
        Play(120);SyncEntities();
        if(_people[caravan.Merchant]==rig||sim.Civilizations.Relation(1,2).Trades!=1
            ||sim.Storage.AmountOf(buyer,ResourceKind.Food)<=0||sim.Storage.AmountOf(seller,ResourceKind.Wood)<=0
            ||agents.InventoryOf(slot,ResourceKind.Food)!=1)
            throw new InvalidOperationException("The completed caravan remains visible or lost its actual delivery");
        GD.Print("WORLD_CARAVAN_LOAD_PASS: dispatched food on actual walk, same-kind personal load during break, reserved cargo restored, paid wood on return, stow before outfit, read-only");
    }

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
        float departureTransfer=0,departureRootDrift=0,departureFootDrift=0,hitchRootStep=-1;
        bool retained=false,settledAtGoal=false;
        var previousFeet=new[]{rig.Feet[0].GlobalPosition,rig.Feet[1].GlobalPosition};
        void MoveFor(int frames)
        {
            string before=sim.StateDigestString();
            float priorStep=0;
            for(int frame=0;frame<frames;frame++)
            {
                var previous=rig.Position;
                float previousYaw=rig.Rotation.Y;
                bool hitch=hitchRootStep<0&&priorStep>.012f;
                AnimateActors(hitch?.60f:delta);
                priorStep=Horizontal(rig.Position-previous).Length();
                if(hitch)hitchRootStep=priorStep;
                peak=MathF.Max(peak,priorStep/(hitch?MaximumActorObservationStep:delta));
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
                if(sim.Agents.ActionOf(slot)==ActionKind.Take)
                {
                    if(rig.GestureEngaged(ActionKind.Take))throw new InvalidOperationException("Resident reaches for a missing pile");
                    settledAtGoal|=rig.InteractionSettled&&Horizontal(rig.Position-goal).Length()<.035f
                        &&InteractionFacingAligned(rig.Rotation.Y,Horizontal(corner-rig.Position));
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
            ||landingTurn<.60f||landingTransfer<.018f||pivotFrames<20||stepLift<.017f||plantDrift>.020f||!settledAtGoal)
            throw new InvalidOperationException($"Resident world route: peak={peak}, corner={nearestCorner}, queue={retained}, landing turn={landingTurn}, pivots={pivotFrames}, lift={stepLift}, drift={plantDrift}, settled={settledAtGoal}");
        GD.Print($"RESIDENT_WORLD_MOTION_PASS: actual world actor, original root peak={oldPeak:F2} m/s, paced={peak:F2} m/s, recorded corner retained, pivot frames={pivotFrames}, foot lift={stepLift:F3} m, planted frame drift={plantDrift:F4} m, arrival without reaching for missing stock, unchanged simulation");
        GD.Print($"ARRIVAL_TURN_PASS: {Mathf.RadToDeg(landingTurn):F1} degrees and {landingTransfer:F3} m lateral weight transfer during the two landing steps before any additional pivot and pickup");
        if(preparationFrames<6||departureTransfer<.015f||departureRootDrift>.001f||departureFootDrift>.010f)
            throw new InvalidOperationException($"Resident first step: frames={preparationFrames}, pelvis={departureTransfer}, root drift={departureRootDrift}, foot drift={departureFootDrift}");
        if(hitchRootStep<=.001f||hitchRootStep>ResidentRig.MaximumGaitSpeed*MaximumActorObservationStep+.001f)
            throw new InvalidOperationException($"A single stalled frame replays resident travel: root step={hitchRootStep:F4} m");
        GD.Print($"WORLD_ACTOR_HITCH_PASS: a 600 ms render hitch moves the travelling root {hitchRootStep:F3} m on supported terrain, read-only");
        GD.Print($"RESIDENT_DEPARTURE_PASS: preparation={preparationFrames} frames, lateral pelvis transfer={departureTransfer:F3} m, root/feet drift={departureRootDrift:F4}/{departureFootDrift:F4} m before the first step");
        ValidateResidentStations(tiles.Goal,tiles.Corner,delta);
        ValidateProjectedResidentMotion(delta);
        ValidateRedirectedResident(delta);
        ValidateSceneHandoff(tiles.Start,tiles.Corner,delta);
        ValidateWorldStockContact(tiles.Start,delta);
        ValidateWorldToolContact(tiles.Goal,delta);
        ValidateCommittedCultivation(tiles.Goal,delta);
        ValidateSupportRelease(rig,slot,tiles.Start,tiles.Corner);
        ValidateFootfallPreemption(rig,slot,tiles.Start,tiles.Corner,delta);
        ValidateSceneSpear();
    }

    internal void ValidateSceneSpear()
    {
        var sim=Game.Sim;var agents=sim.Agents;
        var tile=Enumerable.Range(0,sim.World.Width*sim.World.Height)
            .Select(i=>new Vector2I(i%sim.World.Width,i/sim.World.Width))
            .First(p=>p.X>=3&&p.Y>=3&&p.X<sim.World.Width-4&&p.Y<sim.World.Height-3
                &&!sim.Wildlife.HasPreyNearby(p.X,p.Y,3)
                &&TryResidentStation(p,1,out var west)&&TryResidentStation(p,2,out var east)
                &&ScenerySegmentClear(west,east)&&SceneryLegSupported(east,PositionAt(p.X+1,p.Y))
                &&ScenerySegmentClear(east,PositionAt(p.X+1,p.Y))&&!ActorNearShore(PositionAt(p.X+1,p.Y)));
        if(sim.InterveneSpawnHumans(tile.X,tile.Y,2,1)!=2)
            throw new InvalidOperationException("Spear fixture could not spawn two residents");
        var slots=agents.AliveSlots().TakeLast(2).ToArray();
        int hunter=slots[0],opponent=slots[1];
        foreach(int slot in slots)
        {
            agents.SetLifeStage(slot,LifeStage.Adult);
            agents.SetPosition(slot,tile.X,tile.Y);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        }
        sim.Relationships.Interact(hunter,opponent,-1,sim.Clock);
        SyncEntities();
        var rig=(ResidentRig)_people[sim.Society.Identity(hunter)];
        var victim=(ResidentRig)_people[sim.Society.Identity(opponent)];
        void Station(ResidentRig resident,int index)
        {
            TryResidentStation(tile,index,out var place);
            resident.StationTile=tile;resident.StationSlot=index;resident.Position=place;resident.Travel=default;
        }
        Station(rig,1);Station(victim,2);
        _target=PositionAt(tile.X,tile.Y);_distance=8;_pitch=.62f;UpdateCamera();
        const float delta=1f/60;
        float Play(int frames,Func<Vector3>? aim=null)
        {
            string digest=sim.StateDigestString();
            float gap=float.PositiveInfinity;
            Vector3? previousTip=null;
            for(int i=0;i<frames;i++)
            {
                AnimateActors(delta);
                if(aim!=null&&rig.ToolHeld&&rig.ToolStrokePhase is >.57f and <.81f)
                {
                    var tip=rig.WorldToolTip;
                    if(previousTip is {} previous&&tip.DistanceTo(previous)>.14f)
                        throw new InvalidOperationException("The spear tip jumps during extension or recovery");
                    previousTip=tip;
                    gap=MathF.Min(gap,tip.DistanceTo(aim()));
                }
                else previousTip=null;
            }
            if(sim.StateDigestString()!=digest)throw new InvalidOperationException("Spear playback changed simulation");
            return gap;
        }
        void Guard(ActionKind action)
        {
            rig.Rotation=new(0,HeadingYaw(Horizontal(Vector3.Right)),0);
            agents.SetAction(hunter,action,ActionPhase.Executing);
            Play(230);
            if(!rig.GestureEngaged(action)||MathF.Abs(rig.ToolStrokePhase-.48f)>.001f)
                throw new InvalidOperationException($"{action} did not hold its loaded guard: {rig.Handling}, phase={rig.ToolStrokePhase:F3}");
        }

        // The engine's abstract combat radius is deliberately larger than a spear.
        agents.SetPosition(opponent,tile.X+2,tile.Y);
        victim.Position=PositionAt(tile.X+2,tile.Y);
        agents.SetAction(hunter,ActionKind.Attack,ActionPhase.Executing);
        float health=agents.HealthOf(opponent);
        sim.Actions.Tick(sim.Clock);Play(125);
        if(agents.HealthOf(opponent)>=health||rig.PresentedToolImpacts!=0)
            throw new InvalidOperationException("An abstract distant attack either failed to settle or displayed a thrust");

        agents.SetPosition(opponent,tile.X,tile.Y);
        Station(victim,2);Play(12);
        Guard(ActionKind.Attack);
        if(rig.PresentedSurfaceContacts!=0)
            throw new InvalidOperationException("The loaded guard makes contact before confirmation");
        var standing=victim.Position;
        victim.Position+=Vector3.Back*.14f;
        Play(1);
        if(!rig.GestureEngaged(ActionKind.Attack)||rig.ToolStrokePhase<.479f)
            throw new InvalidOperationException("The same moving resident resets an already loaded guard");
        victim.Position=standing+Vector3.Back*.43f;
        if(!TrySpearSurface(rig,rig.Position,ResidentAttackPoint(victim,rig.Position),out _))
            throw new InvalidOperationException("Discontinuous aim fixture needs a reachable second position");
        var wrist=rig.HandWorldPose(1).Origin;
        Play(1);
        float guardShiftStep=wrist.DistanceTo(rig.HandWorldPose(1).Origin);
        if(rig.GestureEngaged(ActionKind.Attack)||guardShiftStep>.055f)
            throw new InvalidOperationException("A displaced opponent inherits the loaded guard or snaps the wrist");
        victim.Position=standing;
        Play(150);
        if(!rig.GestureEngaged(ActionKind.Attack)||MathF.Abs(rig.ToolStrokePhase-.48f)>.001f)
            throw new InvalidOperationException("Guard never reacquires the same reachable opponent");
        var middle=Horizontal((rig.Position+victim.Position)*.5f);
        var key=new Vector2I((int)MathF.Round(middle.X*.5f),(int)MathF.Round(middle.Y*.5f));
        bool hadSolid=_scenerySolids.TryGetValue(key,out var prior);
        try
        {
            _scenerySolids[key]=(middle,.10f);
            sim.Actions.Tick(sim.Clock);
            agents.SetAction(hunter,ActionKind.None,ActionPhase.Idle);
            Play(100);
            if(rig.PresentedToolImpacts!=0)throw new InvalidOperationException("The confirmed thrust crosses an obstruction");
        }
        finally
        {
            if(hadSolid)_scenerySolids[key]=prior;
            else _scenerySolids.Remove(key);
        }
        Guard(ActionKind.Attack);
        uint attackRevision=agents.ActionRevisionOf(hunter);
        sim.Actions.Tick(sim.Clock);
        if(!sim.Actions.TryGetContactCompletion(hunter,attackRevision,out var attack)
            ||attack.Site?.TargetIdentity!=sim.Society.Identity(opponent))
            throw new InvalidOperationException("Attack receipt did not preserve its actual opponent");
        agents.SetAction(hunter,ActionKind.None,ActionPhase.Idle);
        agents.SetAction(hunter,ActionKind.None,ActionPhase.Idle);
        float attackGap=Play(115,()=>ResidentAttackPoint(victim,rig.Position));
        if(rig.PresentedToolImpacts!=1||rig.PresentedSurfaceContacts!=1)
            throw new InvalidOperationException($"A reachable attack missed its rendered body point: strikes={rig.PresentedToolImpacts}, contacts={rig.PresentedSurfaceContacts}, nearest={attackGap:F3} m");

        Station(rig,2);
        int SpawnPrey()=>sim.Wildlife.Add(sim.World,tile.X+1,tile.Y,0,sim.Random.Get(RngStream.Events));
        long PreyIdentity(int prey)=>((long)sim.Wildlife.GenerationOf(prey)<<32)|(uint)prey;
        void CommitHunt(int prey)
        {
            long identity=PreyIdentity(prey);
            for(int i=0;i<sim.Config.Wildlife.HuntTicks;i++)agents.IncrementActionTicks(hunter);
            sim.Actions.Tick(sim.Clock);SyncEntities();
            if(sim.Wildlife.IsAlive(prey)||!sim.Actions.TryGetCompletedContact(hunter,out var receipt)||receipt.TargetIdentity!=identity)
                throw new InvalidOperationException("Hunt did not record the committed prey generation");
        }
        int original=SpawnPrey();SyncEntities();Station(rig,2);Guard(ActionKind.Hunt);
        long originalIdentity=PreyIdentity(original);
        sim.Wildlife.Kill(original,hunted:false);
        int replacement=SpawnPrey();
        if(replacement!=original||PreyIdentity(replacement)==originalIdentity)
            throw new InvalidOperationException("Hunt fixture did not reuse the prey slot with a different generation");
        CommitHunt(replacement);Play(100);
        if(rig.PresentedToolImpacts!=1)
            throw new InvalidOperationException("A replaced prey inherited the prior animal's guarded contact");
        int committed=SpawnPrey();SyncEntities();Station(rig,2);Guard(ActionKind.Hunt);
        var huntPoint=rig.PreparedSpearAim(ActionKind.Hunt,agents.ActionRevisionOf(hunter),PreyIdentity(committed))?.Point
            ??throw new InvalidOperationException("Hunt fixture has no live aim point");
        CommitHunt(committed);
        agents.SetAction(hunter,ActionKind.Wander,ActionPhase.Moving);
        agents.SetAction(hunter,ActionKind.None,ActionPhase.Idle);
        float huntGap=Play(115,()=>huntPoint);
        if(rig.PresentedToolImpacts!=2||rig.PresentedSurfaceContacts!=2)
            throw new InvalidOperationException($"The committed hunt loses the last actual aim: strikes={rig.PresentedToolImpacts}, contacts={rig.PresentedSurfaceContacts}, nearest={huntGap:F3} m");
        GD.Print($"WORLD_SPEAR_TARGET_PASS: small movement keeps guard, abrupt displacement rearms with wrist step={guardShiftStep:F3} m, distant and occluded strikes suppressed; one contact on the resident ({attackGap:F3} m) and one on the prey ({huntGap:F3} m) across skipped decisions, replaced prey rejected, read-only");
    }

    private void ValidateSupportRelease(ResidentRig rig,int slot,Vector2I start,Vector2I destination)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        var origin=PositionAt(start.X,start.Y);
        var goal=PositionAt(destination.X,destination.Y);
        _target=origin;_distance=8;UpdateCamera();
        sim.GroundStocks.Deposit(start.X,start.Y,ResourceKind.Stone,2,sim.Config.GroundStocks);
        var departures=Game.PacingResidents?new[]{
            (ActionKind.Sleep,ActionKind.Wander,60),(ActionKind.Take,ActionKind.Wander,24),
            (ActionKind.Sleep,ActionKind.Flee,60)}
            :new[]{(ActionKind.Sleep,ActionKind.Wander,60),(ActionKind.Sleep,ActionKind.Flee,60)};
        foreach(var (outgoing,incoming,rate) in departures)
        {
            float dt=1f/rate;
            agents.SetPosition(slot,start.X,start.Y);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
            agents.ClearTarget(slot);
            rig.Position=origin;
            rig.Rotation=new(0,HeadingYaw(Horizontal(goal-origin)),0);
            rig.Travel=default;
            for(int frame=0;frame<rate;frame++)AnimateActors(dt);
            agents.SetAction(slot,outgoing,ActionPhase.Executing);
            if(outgoing==ActionKind.Take)agents.SetTarget(slot,start.X,start.Y);
            for(int frame=0;frame<5*rate;frame++)AnimateActors(dt);
            if(!rig.NeedsSupportRelease||!rig.GestureEngaged(outgoing)||rig.ScenePickupGrasped)
                throw new InvalidOperationException($"Departure fixture did not establish {outgoing}");

            agents.SetAction(slot,incoming,ActionPhase.Moving);
            agents.SetPosition(slot,destination.X,destination.Y);
            agents.ClearTarget(slot);
            string digest=sim.StateDigestString();
            var before=rig.Position;
            float yaw=rig.Rotation.Y;
            int held=0,started=-1;
            float footDrift=0;
            var left=rig.Feet[0].GlobalPosition;var right=rig.Feet[1].GlobalPosition;
            for(int frame=0;frame<rate*2;frame++)
            {
                bool supported=rig.NeedsSupportRelease;
                AnimateActors(dt);
                if(rig.ReleasingSupport)
                {
                    held++;
                    footDrift=MathF.Max(footDrift,MathF.Max(Horizontal(rig.Feet[0].GlobalPosition-left).Length(),
                        Horizontal(rig.Feet[1].GlobalPosition-right).Length()));
                    if(Horizontal(rig.Position-before).Length()>.001f||MathF.Abs(Mathf.AngleDifference(yaw,rig.Rotation.Y))>.001f)
                        throw new InvalidOperationException("Root moved while the outgoing pose was reclaiming its support");
                }
                if(Horizontal(rig.Position-before).Length()<=.012f)continue;
                started=frame;
                if(incoming==ActionKind.Wander&&supported)
                    throw new InvalidOperationException($"Walking started before the {outgoing} pose recovered");
                break;
            }
            if(started<0||(incoming==ActionKind.Wander?(held<3||started>rate):(held!=0||started>rate/3))
                ||footDrift>.025f||rig.ScenePickupPieceVisible||sim.StateDigestString()!=digest)
                throw new InvalidOperationException($"Departure {outgoing} to {incoming}: held={held}, first step={started}, foot drift={footDrift:F3} m at {rate} Hz");
            GD.Print($"WORLD_SUPPORT_RELEASE_PASS: {outgoing} to {incoming} at {rate} Hz, paced={Game.PacingResidents}, {held} planted release frames, {footDrift:F3} m sole drift, departure on frame {started}, read-only");
        }
    }

    private void ValidateFootfallPreemption(ResidentRig rig,int slot,Vector2I start,Vector2I destination,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        var origin=PositionAt(start.X,start.Y);
        var goal=PositionAt(destination.X,destination.Y);
        var forward=Horizontal(goal-origin);
        _target=(origin+goal)*.5f;_distance=8;UpdateCamera();
        void Reset()
        {
            agents.SetPosition(slot,start.X,start.Y);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
            agents.ClearTarget(slot);
            rig.Position=origin;rig.Rotation=new(0,HeadingYaw(forward),0);
            rig.Travel=default;rig.CancelPivot();
            for(int frame=0;frame<100;frame++)AnimateActors(delta);
        }

        void PrepareTurn()
        {
            Reset();
            agents.SetAction(slot,ActionKind.Take,ActionPhase.Executing);
            agents.SetTarget(slot,start.X,start.Y);
            if(!sim.Actions.TryGetPickupCandidate(slot,out var source)
                ||!TryActionContact(rig,ActionKind.Take,source,origin,out var point,out var stand))
                throw new InvalidOperationException("Interrupted turn needs a reachable pickup");
            rig.Position=stand;rig.Travel=default;
            rig.Rotation=new(0,HeadingYaw(Horizontal(stand-point)),0);
            for(int frame=0;frame<170&&!rig.PivotStepInFlight;frame++)AnimateActors(delta);
            if(!rig.PivotStepInFlight)throw new InvalidOperationException("Interrupted turn needs a step in the air");
            for(int frame=0;frame<5;frame++)AnimateActors(delta);
        }

        void MoveTo(Vector2I tile,ActionKind action)
        {
            agents.SetAction(slot,action,ActionPhase.Moving);
            agents.SetPosition(slot,tile.X,tile.Y);
            agents.ClearTarget(slot);
        }

        (int Hold,float Drift) CheckDeparture(bool pivot)
        {
            string digest=sim.StateDigestString();
            var initial=rig.Position;
            int hold=0;
            float drift=0;
            bool started=false;
            for(int frame=0;frame<145;frame++)
            {
                bool airborne=pivot?rig.PivotStepInFlight:rig.ArrivalStepInFlight;
                AnimateActors(delta);
                float displacement=Horizontal(rig.Position-initial).Length();
                if(airborne)
                {
                    hold++;
                    drift=MathF.Max(drift,displacement);
                }
                if(displacement>.025f){started=true;break;}
            }
            if(hold<4||drift>.002f||!started||sim.StateDigestString()!=digest)
                throw new InvalidOperationException($"Departure before footfall: pivot={pivot}, held={hold}, drift={drift:F4} m, started={started}");
            return (hold,drift);
        }

        PrepareTurn();
        MoveTo(destination,ActionKind.Wander);
        var turn=CheckDeparture(pivot:true);
        PrepareTurn();
        MoveTo(destination,ActionKind.Flee);
        AnimateActors(delta);
        if(rig.PivotStepInFlight)throw new InvalidOperationException("Emergency travel waits for the previous pivot");

        Reset();
        agents.SetPosition(slot,destination.X,destination.Y);
        for(int frame=0;frame<260&&!rig.ArrivalStepInFlight;frame++)AnimateActors(delta);
        if(!rig.ArrivalStepInFlight)throw new InvalidOperationException("Interrupted arrival needs a final landing step");
        MoveTo(start,ActionKind.Wander);
        var arrival=CheckDeparture(pivot:false);
        GD.Print($"WORLD_FOOTFALL_HANDOFF_PASS: pivot/arrival hold {turn.Hold}/{arrival.Hold} frames, root drift {turn.Drift:F4}/{arrival.Drift:F4} m, emergency bypass, read-only");
    }

    internal void ValidateWorldToolContact()=>ValidateWorldToolContact(FindResidentMotionCorner().Goal,1f/60);

    internal void ValidateAcceleratedContact()
    {
        if(Game.PacingResidents)throw new InvalidOperationException("Accelerated contact fixture requires a non-paced world");
        const float delta=1f/60;
        var sim=Game.Sim;var agents=sim.Agents;
        var (tile,neighbor,_)=FindResidentMotionCorner();
        var logical=PositionAt(tile.X,tile.Y);
        if(sim.InterveneSpawnHumans(tile.X,tile.Y,1,1)!=1)throw new InvalidOperationException("Could not spawn contact fixture");
        int slot=agents.AliveSlots().Last();
        agents.SetLifeStage(slot,LifeStage.Adult);
        foreach(var kind in ResidentRig.CargoKinds)agents.SetInventory(slot,kind,0);
        agents.SetAction(slot,ActionKind.Take,ActionPhase.Executing);agents.SetTarget(slot,tile.X,tile.Y);
        SyncEntities();
        var rig=(ResidentRig)_people[sim.Society.Identity(slot)];
        _target=logical;_distance=8;_pitch=.62f;UpdateCamera();
        for(int i=0;i<100;i++)
        {
            AnimateActors(delta);
            if(rig.GestureEngaged(ActionKind.Take))throw new InvalidOperationException("Accelerated resident picks up an empty tile");
        }
        float amount=sim.GroundStocks.Deposit(tile.X,tile.Y,ResourceKind.Stone,2,sim.Config.GroundStocks);
        if(amount<=0||!sim.Actions.TryGetPickupCandidate(slot,out var candidate)
            ||!TryActionContact(rig,ActionKind.Take,candidate,logical,out var point,out var stand))
            throw new InvalidOperationException("Accelerated fixture has no supported stock");
        rig.Position=stand;
        rig.Rotation=new(0,HeadingYaw(Horizontal(point-stand))+MathF.PI*.84f,0);
        int pivotFrames=0;
        float closest=float.PositiveInfinity;
        string digest=sim.StateDigestString();
        for(int i=0;i<270;i++)
        {
            AnimateActors(delta);
            if(rig.IsPivoting)
            {
                pivotFrames++;
                if(rig.GestureEngaged(ActionKind.Take))throw new InvalidOperationException("Resident reaches before the turn lands");
            }
            if(rig.GestureEngaged(ActionKind.Take))
                closest=MathF.Min(closest,(rig.HandWorldPose(1)*rig.Grip!.Position).DistanceTo(point));
            if(rig.ScenePickupPieceVisible)throw new InvalidOperationException("Unconfirmed contact owns a parcel");
        }
        if(pivotFrames<25||closest>.085f||sim.StateDigestString()!=digest)
            throw new InvalidOperationException($"Accelerated contact: pivot frames={pivotFrames}, gap={closest:F3} m, state={rig.Handling}");
        sim.GroundStocks.Withdraw(tile.X,tile.Y,ResourceKind.Stone,amount);
        digest=sim.StateDigestString();
        for(int i=0;i<85;i++)
        {
            AnimateActors(delta);
            if(rig.GestureEngaged(ActionKind.Take)||rig.ScenePickupGrasped||rig.ScenePickupPieceVisible)
                throw new InvalidOperationException("Depleted stock retains an active reach or parcel");
        }
        if(sim.StateDigestString()!=digest)throw new InvalidOperationException("Accelerated contact mutated simulation");
        GD.Print($"WORLD_ACCELERATED_CONTACT_PASS: {pivotFrames} planted turn frames, prepared gap={closest:F3} m, empty and depleted stock stay untouched");

        agents.SetInventory(slot,ResourceKind.Stone,2);
        agents.SetAction(slot,ActionKind.Deposit,ActionPhase.Executing);
        agents.SetTarget(slot,tile.X,tile.Y);
        if(!sim.Actions.TryGetPlacementCandidate(slot,out var destination)
            ||!TryActionContact(rig,ActionKind.Deposit,destination,logical,out var drop,out var dropStand))
            throw new InvalidOperationException("Accelerated deposit fixture has no supported destination");
        var start=Enumerable.Range(0,8).Select(side=>logical+Vector3.Forward.Rotated(Vector3.Up,side*MathF.PI/4)*.55f)
            .First(point=>Horizontal(dropStand-point).Length()>.30f&&SceneryPointClear(point,SceneryClearance)
                &&ScenerySegmentClear(point,dropStand)&&SceneryLegSupported(point,dropStand));
        start.Y=GroundHeight(start.X,start.Z);
        rig.Position=start;
        rig.Rotation=new(0,HeadingYaw(Horizontal(dropStand-start)),0);
        digest=sim.StateDigestString();
        float speed=0,peakAcceleration=0;
        int preparing=0;
        for(int frame=0;frame<260;frame++)
        {
            var before=rig.Position;
            AnimateActors(delta);
            float nextSpeed=Horizontal(rig.Position-before).Length()/delta;
            peakAcceleration=MathF.Max(peakAcceleration,(nextSpeed-speed)/delta);
            speed=nextSpeed;
            if(rig.Travel.Preparation>0&&nextSpeed<.001f)preparing++;
            if(rig.PlacingCargo||rig.ReleasedCargo)
                throw new InvalidOperationException("Accelerated deposit released cargo before its receipt");
        }
        if(preparing<4||peakAcceleration>4.2f||!rig.InteractionSettled
            ||Horizontal(rig.Position-dropStand).Length()>.035f
            ||!InteractionFacingAligned(rig.Rotation.Y,Horizontal(drop-rig.Position))||sim.StateDigestString()!=digest)
            throw new InvalidOperationException($"Accelerated deposit approach: preparation={preparing}, acceleration={peakAcceleration:F2}, stance={rig.Position.DistanceTo(dropStand):F3}");
        GD.Print($"WORLD_ACCELERATED_APPROACH_PASS: {preparing} stationary preparation frames, peak acceleration={peakAcceleration:F2} m/s², settled and facing the pile with no premature release");
        ValidateSupportRelease(rig,slot,tile,neighbor);
        ValidateFootfallPreemption(rig,slot,tile,neighbor,delta);
    }

    private void ValidateWorldToolContact(Vector2I origin,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        var tile=Enumerable.Range(0,sim.World.Width*sim.World.Height)
            .Select(index=>new Vector2I(index%sim.World.Width,index/sim.World.Width))
            .Where(p=>p.X>1&&p.Y>1&&p.X<sim.World.Width-2&&p.Y<sim.World.Height-2
                &&sim.World.TileAt(p.X,p.Y).BuildingId==0)
            .OrderBy(p=>p.DistanceSquaredTo(origin))
            .First(p=>DryActorTarget(PositionAt(p.X,p.Y))&&SceneryPointClear(PositionAt(p.X,p.Y),.75f)
                &&ActorFootSupport(PositionAt(p.X,p.Y)));
        var stand=PositionAt(tile.X,tile.Y);
        var tree=new Transform3D(new Basis(Vector3.Up,.37f),stand+Vector3.Forward*.69f);
        var prop=new MeshInstance3D {Name="WorkContactFixtureTree",Mesh=_models.Shape("broad-tree"),Transform=tree};
        _props.AddChild(prop);
        try
        {
            sim.World.SetResource(tile.X,tile.Y,new ResourceNode {Kind=ResourceKind.Wood,Amount=30,Capacity=30});
            if(sim.InterveneSpawnHumans(tile.X,tile.Y,1,1)!=1)
                throw new InvalidOperationException("Strike fixture could not spawn a resident");
            int slot=agents.AliveSlots().Last();
            agents.SetLifeStage(slot,LifeStage.Adult);
            agents.SetPosition(slot,tile.X,tile.Y);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
            SyncEntities();
            var rig=(ResidentRig)_people[sim.Society.Identity(slot)];
            rig.Position=stand;rig.Rotation=Vector3.Zero;
            _target=stand;_distance=8;_pitch=.62f;UpdateCamera();
            AnimateActors(delta);
            agents.SetAction(slot,ActionKind.GatherWood,ActionPhase.Executing);
            agents.SetTarget(slot,tile.X,tile.Y);
            _sceneResourceAnchors.Remove(tile.Y*sim.World.Width+tile.X);
            for(int frame=0;frame<180;frame++)AnimateActors(delta);
            if(rig.GestureEngaged(ActionKind.GatherWood)||rig.PresentedToolImpacts!=0)
                throw new InvalidOperationException("A logical wood resource without a rendered strike surface allows a stroke");
            RegisterSceneResource(tile.X,tile.Y,ResourceKind.Wood,tree.Origin+Vector3.Up*.85f,"broad-tree",tree);
            if(!TryPlanToolSurface(ActionKind.GatherWood,tile.X,tile.Y,stand,out var strike,out bool requiresSurface)
                ||!requiresSurface||!ReachableToolSurface(rig,stand,strike))
                throw new InvalidOperationException("Tree contact fixture has no reachable front surface");
            string committed=sim.StateDigestString();
            float closest=float.PositiveInfinity;
            for(int i=0;i<300;i++)
            {
                AnimateActors(delta);
                if(rig.ToolHeld)closest=MathF.Min(closest,rig.WorldToolTip.DistanceTo(strike.Point));
            }
            int contacts=rig.PresentedSurfaceContacts;
            if(contacts==0||contacts!=rig.PresentedToolImpacts||closest>.060f||sim.StateDigestString()!=committed)
                throw new InvalidOperationException($"World axe does not touch the transformed tree: gap={closest:F3} m, contacts={contacts}, stand={rig.Position.DistanceTo(stand):F3}");

            int impacts=rig.PresentedToolImpacts;
            for(int frame=0;frame<150&&rig.PresentedToolImpacts==impacts;frame++)AnimateActors(delta);
            if(rig.PresentedToolImpacts!=impacts+1)
                throw new InvalidOperationException("Tree fixture did not reach its last executing strike");
            sim.Actions.Tick(sim.Clock);
            if(!sim.Actions.TryGetCompletedContact(slot,out var firstReceipt))
                throw new InvalidOperationException("The first harvest did not record its tree");
            AnimateActors(delta);
            float recovery=rig.ToolStrokePhase;
            uint previousRevision=agents.ActionRevisionOf(slot);
            agents.SetAction(slot,ActionKind.GatherWood,ActionPhase.Executing);
            agents.SetTarget(slot,tile.X,tile.Y);
            sim.Actions.Tick(sim.Clock);
            if(agents.ActionRevisionOf(slot)==previousRevision||!sim.Actions.TryGetCompletedContact(slot,out var secondReceipt)
                ||secondReceipt!=firstReceipt)
                throw new InvalidOperationException("The second harvest did not commit to the same tree");
            committed=sim.StateDigestString();
            AnimateActors(delta);
            float recoveryStep=rig.ToolStrokePhase-recovery;
            if(!rig.GestureEngaged(ActionKind.GatherWood)||recoveryStep<=0||recoveryStep>.04f
                ||rig.PresentedToolImpacts!=impacts+1)
                throw new InvalidOperationException($"The second world harvest restarts the ongoing recovery: phase step={recoveryStep:F3}, state={rig.Handling}");
            for(int frame=0;frame<210;frame++)AnimateActors(delta);
            if(rig.PresentedToolImpacts!=impacts+2||rig.CompletionVisible||sim.StateDigestString()!=committed)
                throw new InvalidOperationException("Consecutive world harvests do not produce exactly one new visible strike");
            agents.SetAction(slot,ActionKind.GatherWood,ActionPhase.Executing);
            agents.SetTarget(slot,tile.X,tile.Y);
            int beforeDeparture=rig.PresentedToolImpacts;
            int preparation=0;
            for(;preparation<260;preparation++)
            {
                AnimateActors(delta);
                if(rig.GestureEngaged(ActionKind.GatherWood)&&rig.ToolStrokePhase is >=.72f and <.82f)break;
            }
            if(preparation==260||rig.PresentedToolImpacts!=beforeDeparture)
                throw new InvalidOperationException("Tool departure fixture did not prepare a fresh swing");
            uint striking=agents.ActionRevisionOf(slot);
            sim.Actions.Tick(sim.Clock);
            agents.SetAction(slot,ActionKind.Wander,ActionPhase.Moving);
            agents.SetTarget(slot,tile.X+1,tile.Y);
            uint walking=agents.ActionRevisionOf(slot);
            committed=sim.StateDigestString();
            int contactFrame=-1,departureFrame=-1;
            for(int frame=0;frame<90;frame++)
            {
                AnimateActors(delta);
                if(frame==0&&rig.ObservedActionRevision!=striking)
                    throw new InvalidOperationException("Ordinary departure discards a prepared committed strike");
                if(contactFrame<0&&rig.PresentedToolImpacts>beforeDeparture)contactFrame=frame;
                if(departureFrame<0&&rig.ObservedActionRevision==walking)departureFrame=frame;
            }
            if(contactFrame<0||departureFrame!=contactFrame+1||rig.PresentedToolImpacts!=beforeDeparture+1
                ||sim.StateDigestString()!=committed)
                throw new InvalidOperationException($"A committed strike does not release the next action on contact: impact={contactFrame}, departure={departureFrame}, strikes={rig.PresentedToolImpacts-beforeDeparture}");
            GD.Print($"WORLD_WORK_DEPARTURE_PASS: one committed axe hit in {contactFrame+1} frames; walking takes over on the next frame while the tool returns, read-only");
            contacts=rig.PresentedSurfaceContacts;
            sim.World.ClearResource(tile.X,tile.Y);
            agents.SetAction(slot,ActionKind.GatherWood,ActionPhase.Executing);
            agents.SetTarget(slot,tile.X,tile.Y);
            committed=sim.StateDigestString();
            for(int i=0;i<75;i++)AnimateActors(delta);
            if(rig.GestureEngaged(ActionKind.GatherWood)||rig.PresentedSurfaceContacts!=contacts||sim.StateDigestString()!=committed)
                throw new InvalidOperationException("Removing the active tree leaves a stale swing or changes the simulation");
            GD.Print($"WORLD_TOOL_SURFACE_PASS: axe reaches actual transformed trunk within {closest:F3} m, {contacts} contacts, missing and depleted surfaces suppress strikes");
            GD.Print($"WORLD_REPEAT_WORK_PASS: two harvest receipts at the same rendered tree, recovery advances by {recoveryStep:F3}, one new strike and unchanged simulation during playback");
        }
        finally
        {
            _sceneResourceAnchors.Remove(tile.Y*sim.World.Width+tile.X);
            _props.RemoveChild(prop);prop.QueueFree();
        }
    }

    private void ValidateCommittedCultivation(Vector2I origin,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        var tile=Enumerable.Range(0,sim.World.Width*sim.World.Height)
            .Select(index=>new Vector2I(index%sim.World.Width,index/sim.World.Width))
            .Where(p=>p.X>1&&p.Y>1&&p.X<sim.World.Width-2&&p.Y<sim.World.Height-2
                &&sim.World.TileAt(p.X,p.Y).BuildingId==0&&sim.World.TileAt(p.X+1,p.Y).BuildingId==0
                &&sim.World.TileAt(p.X,p.Y+1).BuildingId==0)
            .OrderBy(p=>p.DistanceSquaredTo(origin))
            .First(p=>DryActorTarget(PositionAt(p.X,p.Y))&&SceneryPointClear(PositionAt(p.X,p.Y),.72f)
                &&ActorFootSupport(PositionAt(p.X,p.Y)));
        // Keep the irrigation fixture independent of the procedural shore.
        for(int y=tile.Y-2;y<=tile.Y+2;y++)for(int x=tile.X-2;x<=tile.X+2;x++)
        {
            sim.World.SetTerrain(x,y,TerrainKind.Grass);
            sim.World.Tiles[y*sim.World.Width+x].Height=.34f;
        }
        sim.World.SetTerrain(tile.X+1,tile.Y,TerrainKind.Water);
        sim.World.SetTerrain(tile.X,tile.Y+1,TerrainKind.Road);
        int farm=sim.Buildings.Place(sim.World,BuildingKind.Farm,tile.X,tile.Y,10);
        if(farm<0)throw new InvalidOperationException("Cultivation receipt fixture could not place a farm");
        sim.Buildings.MarkComplete(farm,sim.Clock);CheckTerrain();BuildBuildings();
        if(sim.InterveneSpawnHumans(tile.X,tile.Y,1,1)!=1)
            throw new InvalidOperationException("Cultivation receipt fixture could not spawn a resident");
        int slot=agents.AliveSlots().Last();
        agents.SetLifeStage(slot,LifeStage.Adult);
        agents.SetPosition(slot,tile.X,tile.Y);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        SyncEntities();
        var rig=(ResidentRig)_people[sim.Society.Identity(slot)];
        _target=PositionAt(tile.X,tile.Y);_distance=9;_pitch=.62f;UpdateCamera();
        AnimateActors(delta);
        void WorkVisit()
        {
            agents.SetAction(slot,ActionKind.Farm,ActionPhase.Executing);
            agents.SetTarget(slot,tile.X,tile.Y);
            sim.Actions.Tick(sim.Clock);
            if(agents.PhaseOf(slot)!=ActionPhase.Done||agents.HasTarget(slot))
                throw new InvalidOperationException("Farm visit did not finish through the simulation");
        }
        int initial=rig.PresentedToolImpacts,initialContacts=rig.PresentedSurfaceContacts;
        agents.SetAction(slot,ActionKind.Farm,ActionPhase.Executing);
        agents.SetTarget(slot,tile.X,tile.Y);
        if(!sim.Actions.TryGetCultivationCandidate(slot,out var plan)||plan.BuildingGeneration!=sim.Buildings.GenerationOf(farm))
            throw new InvalidOperationException("Active cultivation cannot plan around the field it will visit");
        string planned=sim.StateDigestString();
        for(int i=0;i<150;i++)AnimateActors(delta);
        if(rig.PresentedToolImpacts!=initial||sim.StateDigestString()!=planned)
            throw new InvalidOperationException("The planned field plays a soil strike before the simulation accepts the work");
        WorkVisit();
        if(!sim.Actions.TryGetCompletedContact(slot,out var receipt)||receipt.Tile!=new Int2(tile.X,tile.Y)
            ||receipt.Surface!=ActionSystem.ContactSurface.BuildingWork||sim.Buildings.LaborOf(farm)<=0)
            throw new InvalidOperationException("Completed cultivation loses the actual farm after the action clears its target");
        string committed=sim.StateDigestString();
        int soilContact=0;
        float closest=float.PositiveInfinity;
        string closestPose="";
        for(int i=0;i<360;i++)
        {
            AnimateActors(delta);
            if(!rig.ToolHeld||rig.CurrentTool!=ResidentTool.Hoe||rig.ToolStrokePhase is <.28f or >.58f)continue;
            var edge=rig.ToolMesh.GlobalTransform*ResidentRig.HoeCuttingEdge;
            float height=CultivationSurfaceHeight(edge,tile.X,tile.Y);
            float gap=MathF.Abs(edge.Y-height);
            if(gap<closest)
            {
                closest=gap;
                closestPose=$"phase={rig.ToolStrokePhase:F2}, edge={rig.RigWorldSpace.AffineInverse()*edge}, bed={height-rig.Position.Y:F3}, actor={rig.Position}, field={_buildingVisuals[farm].Root.Position}";
            }
            if(gap<.040f)soilContact++;
        }
        if(rig.PresentedToolImpacts!=initial+1||rig.PresentedSurfaceContacts!=initialContacts+1
            ||soilContact<5||rig.CompletionVisible||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"Committed farm misses its soil stroke: impacts={rig.PresentedToolImpacts-initial}, contact frames={soilContact}, closest={closest:F3} m ({closestPose})");

        sim.Buildings.AddLabor(farm,sim.Config.Buildings.FarmLaborPerDayCap);
        WorkVisit();
        if(sim.Actions.TryGetCompletedContact(slot,out _))throw new InvalidOperationException("A full farm reports nonexistent work");
        committed=sim.StateDigestString();
        for(int i=0;i<220;i++)AnimateActors(delta);
        if(rig.PresentedToolImpacts!=initial+1||rig.CompletionVisible||sim.StateDigestString()!=committed)
            throw new InvalidOperationException("A full farm plays another stroke or changes simulation state");
        GD.Print($"WORLD_COMMITTED_WORK_PASS: actual farm survives target clearing, {soilContact} soil-contact frames, a full farm plays no extra stroke");
    }

    internal void ValidateWorldStockContact()=>ValidateWorldStockContact(FindResidentMotionCorner().Start,1f/60);
    internal void ValidateWorldWorkContact()=>ValidateCommittedCultivation(FindResidentMotionCorner().Goal,1f/60);

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
        int firstGlance=-1,firstReach=-1;
        for(int i=0;i<240;i++)
        {
            AnimateActors(delta);
            if(rig.ScenePickupGrasped||rig.ScenePickupPieceVisible||rig.CargoMesh.Visible)
                throw new InvalidOperationException("Pending pickup shows an accepted object before simulation confirmation");
            float gap=(rig.HandWorldPose(1)*rig.Grip!.Position).DistanceTo(plannedPoint);
            preparedGap=Math.Min(preparedGap,gap);
            if(firstGlance<0&&(rig.Torso.Basis*rig.Head.Basis*Vector3.Forward).Normalized().Y<-.20f)firstGlance=i;
            if(firstReach<0&&gap<.12f)firstReach=i;
        }
        if(preparedGap>.085f||sim.StateDigestString()!=pending)
            throw new InvalidOperationException($"World pickup preparation misses its candidate: {preparedGap:F3} m");
        if(firstGlance<0||firstReach-firstGlance<6)
            throw new InvalidOperationException($"World pickup does not look ahead of the hand: glance={firstGlance}, reach={firstReach}");
        GD.Print($"WORLD_CONTACT_LOOK_PASS: eyes and head anticipate a real stock target by {firstReach-firstGlance} frames at 60 Hz, before grasping");
        sim.Actions.Tick(sim.Clock);
        if(agents.HasTarget(slot)||!sim.Actions.TryGetCompletedContact(slot,out var taken)
            ||taken.Surface!=ActionSystem.ContactSurface.GroundStock||taken.Resource!=ResourceKind.Stone||taken.StockAmount!=amount
            ||!TryActionContact(rig,ActionKind.Take,taken,logical,out var pickup,out var stand))
            throw new InvalidOperationException("Take lost its actual resource and stock position on completion");
        string committed=sim.StateDigestString();
        float nearest=float.PositiveInfinity;
        bool grasped=false,carrying=false,departedPickup=false;
        int supportedLiftFrames=0,confirmationFrames=-1;
        float pickupStep=0;
        Vector3? previousPiece=null;
        var jobBefore=agents.JobOf(slot);
        var changedJob=jobBefore==JobType.Gatherer?JobType.Builder:JobType.Gatherer;
        long identity=sim.Society.Identity(slot);
        for(int i=0;i<430;i++)
        {
            AnimateActors(i==0?.60f:delta);
            float distance=(rig.HandWorldPose(1)*rig.Grip!.Position).DistanceTo(pickup);
            nearest=Math.Min(nearest,distance);
            if(rig.ScenePickupGrasped&&!grasped&&(!rig.InteractionSettled||distance>.067f))
                throw new InvalidOperationException($"Ground grasp registered before supported contact: {distance:F3} m");
            if(rig.ScenePickupGrasped&&!grasped)confirmationFrames=i;
            grasped|=rig.ScenePickupGrasped;
            if(grasped&&!carrying)
            {
                if(rig.CargoMesh.Visible==rig.ScenePickupPieceVisible)
                    throw new InvalidOperationException("Pickup to carry shows two parcels or leaves both hands empty");
                if(rig.ScenePickupPiecePose is {} piece)
                {
                    supportedLiftFrames++;
                    if(previousPiece is {} previous)pickupStep=MathF.Max(pickupStep,piece.Origin.DistanceTo(previous));
                    previousPiece=piece.Origin;
                    if(supportedLiftFrames==9)
                    {
                        agents.SetJob(slot,changedJob);
                        SyncEntities();
                        if(_people[identity]!=rig||rig.ScenePickupPiecePose!=piece)
                            throw new InvalidOperationException("An outfit change removes a supported pickup");
                    }
                    if(supportedLiftFrames is 9 or 13)
                    {
                        agents.SetAction(slot,departedPickup?ActionKind.Wander:ActionKind.None,
                            departedPickup?ActionPhase.Moving:ActionPhase.Idle);
                        departedPickup=true;
                        committed=sim.StateDigestString();
                    }
                }
                else if(rig.SupportedCargo!=ResourceKind.Stone)
                    throw new InvalidOperationException("Pickup to carry switches to a different resource");
                if(departedPickup&&rig.RecoveringPickup)
                    throw new InvalidOperationException("A compatible successor pockets the already grasped piece");
            }
            if(rig.CargoMesh.Visible&&!grasped)throw new InvalidOperationException("New cargo appeared before the pickup hand reached the stock");
            carrying|=rig.CargoMesh.Visible;
        }
        if(!grasped||!carrying||!departedPickup||supportedLiftFrames<12||pickupStep>.075f||confirmationFrames>22||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World pickup failed: contact={nearest:F3} m, lift={supportedLiftFrames} frames, carrying={carrying}, stance={rig.Position.DistanceTo(stand):F3} m");
        SyncEntities();
        if(_people[identity]!=rig||rig.SupportedCargo!=ResourceKind.Stone)
            throw new InvalidOperationException("An outfit change discards a parcel after the two-hand handoff");
        GD.Print($"WORLD_PICKUP_CARRY_PASS: {supportedLiftFrames} continuously held pickup frames across a successor, {pickupStep:F3} m maximum piece step, no pocket recovery, blank or duplicate frame at two-hand support, read-only");

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
        bool changedFacing=false,protectedDrop=false;
        int realignmentFrames=0;
        float closestPlacement=float.PositiveInfinity;
        for(int i=0;i<360;i++)
        {
            AnimateActors(delta);
            if(changedFacing&&rig.IsPivoting)
            {
                realignmentFrames++;
                if(rig.PlacingCargo||rig.ReleasedCargo||rig.SupportedCargo!=ResourceKind.Stone)
                    throw new InvalidOperationException("The realigned resident releases the load during the planted turn");
            }
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
                if(placementFrames==50&&!changedFacing)
                {
                    changedFacing=true;
                    rig.Rotation=new(0,rig.Rotation.Y+.9f,0);
                }
            }
            if(!rig.ReleasedCargo)continue;
            if(!protectedDrop)
            {
                SyncEntities();
                if(_people[identity]!=rig)throw new InvalidOperationException("An outfit change erases the released parcel before it settles");
                protectedDrop=true;
            }
            var parcel=(rig.HandWorldPose(1)*rig.CargoMesh.Transform).Origin;
            release=Math.Min(release,Horizontal(parcel-drop).Length());
        }
        if(release>.067f||realignmentFrames<10||!protectedDrop||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World deposit released {release:F3} m away: placement {visiblePlacementFrames}/{placementFrames} frames, closest={closestPlacement:F3} m, stance={rig.Position.DistanceTo(dropStand):F3} m");
        GD.Print($"WORLD_PLACEMENT_REALIGN_PASS: holds the stone through {realignmentFrames} planted turn frames, resumes to within {release:F3} m of its deposit point");

        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        for(int frame=0;frame<180&&rig.ContactPoseInFlight;frame++)AnimateActors(delta);
        var finalRoot=rig.Transform;
        var beforeOutfit=sim.StateDigestString();
        SyncEntities();
        if(_people[identity]==rig||_people[identity].Transform!=finalRoot||_personModels[identity].Job!=changedJob
            ||sim.StateDigestString()!=beforeOutfit)
            throw new InvalidOperationException("A completed contact never releases the deferred outfit or changes the simulation");
        GD.Print("WORLD_CONTACT_CADENCE_PASS: a 600 ms render hitch at confirmation; pickup and carry survive an outfit change until release, read-only");
        // The warehouse scenario below tests an ordinary inventory change,
        // after the preceding outfit change has fully resolved.
        agents.SetJob(slot,jobBefore);
        SyncEntities();
        rig=(ResidentRig)_people[identity];

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
        float stone=sim.Config.GroundStocks.DropAmount+3;
        agents.SetInventory(slot,ResourceKind.Stone,stone);
        sim.Storage.Deposit(storage,ResourceKind.Stone,sim.Storage.CapacityOf(storage));
        AnimateActors(delta);
        if(rig.SupportedCargo!=ResourceKind.Wood)throw new InvalidOperationException("A newly dominant resource replaces a supported parcel");
        agents.SetAction(slot,ActionKind.StoreInBuilding,ActionPhase.Executing);
        agents.SetTarget(slot,warehouse.X,warehouse.Y);
        if(!sim.Actions.TryGetPlacementCandidate(slot,out var plannedStore)||plannedStore.Resource!=ResourceKind.Wood
            ||!TryActionContact(rig,ActionKind.StoreInBuilding,plannedStore,storageTile,out _,out var plannedCrateStand))
            throw new InvalidOperationException("Warehouse approach did not select the actual crate and an accepted resource");
        string warehouseBefore=sim.StateDigestString();
        for(int frame=0;frame<300;frame++)
        {
            AnimateActors(delta);
            if(rig.PlacingCargo||rig.ReleasedCargo||rig.SupportedCargo!=ResourceKind.Wood)
                throw new InvalidOperationException("Warehouse preparation switched or released the carried parcel");
        }
        if(!rig.InteractionSettled||Horizontal(rig.Position-plannedCrateStand).Length()>.035f||sim.StateDigestString()!=warehouseBefore)
            throw new InvalidOperationException("Warehouse approach failed to settle before the simulation receipt");
        sim.Actions.Tick(sim.Clock);
        if(!sim.Actions.TryGetCompletedContact(slot,out var stored)||stored.Surface!=ActionSystem.ContactSurface.Storage
            ||stored.Resource!=ResourceKind.Wood||agents.InventoryOf(slot,ResourceKind.Wood)<=0||agents.InventoryOf(slot,ResourceKind.Stone)!=stone
            ||!TryActionContact(rig,ActionKind.StoreInBuilding,stored,storageTile,out var crate,out var crateStand)
            ||crateStand.DistanceSquaredTo(plannedCrateStand)>.001f*.001f)
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
                if(rig.CargoMesh.Mesh!=rig.CargoMeshes[ResourceKind.Wood])throw new InvalidOperationException("Remaining inventory replaces the released warehouse parcel");
                var parcel=rig.HandWorldPose(1)*rig.CargoMesh.Transform;
                var bottom=parcel.Origin+Vector3.Up*rig.CargoMesh.Mesh.GetAabb().Position.Y*parcel.Basis.Y.Length();
                crateRelease=Math.Min(crateRelease,bottom.DistanceTo(crate));
            }
            else if(float.IsFinite(crateRelease)&&rig.SupportedCargo==ResourceKind.Stone)remainingCargoVisible=true;
        }
        if(crateRelease>.078f||!remainingCargoVisible||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World warehouse release missed its crate: gap={crateRelease:F3} m, closest={closestCrate:F3} m ({closestOffset:F3}) in {cratePlacementFrames} frames, stance={rig.Position.DistanceTo(crateStand):F3} m, remainder visible={remainingCargoVisible}");
        GD.Print($"WORLD_STOCK_CONTACT_PASS: palm {nearest:F3} m from depleted stock, releases {release:F3} m from pile and {crateRelease:F3} m from crate, wood stays wood until release before remaining stone appears, read-only");
        ValidateRapidWarehousePlacement(rig,slot,warehouse,delta);
        ValidateReplacedWarehouseContact(rig,slot,warehouse,delta);
        ValidateInterruptedPickup(rig,slot,tile,logical,delta);
        ValidateSkippedContact(rig,slot,tile,logical,delta);
        GD.Print($"WORLD_PICKUP_INTENT_PASS: candidate reached within {preparedGap:F3} m, confirmed after {confirmationFrames} frames without a second reach");
    }

    private void ValidateReplacedWarehouseContact(ResidentRig rig,int slot,Vector2I tile,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        agents.SetInventory(slot,ResourceKind.Wood,1);
        agents.SetAction(slot,ActionKind.StoreInBuilding,ActionPhase.Executing);
        agents.SetTarget(slot,tile.X,tile.Y);
        if(!sim.Actions.TryGetPlacementCandidate(slot,out var planned)
            ||!TryActionContact(rig,ActionKind.StoreInBuilding,planned,PositionAt(tile.X,tile.Y),out var crate,out var stand))
            throw new InvalidOperationException("The original warehouse has no prepared destination");
        rig.Position=stand;rig.Travel=default;
        rig.Rotation=new(0,HeadingYaw(Horizontal(crate-stand)),0);
        _target=stand;UpdateCamera();
        for(int frame=0;frame<240;frame++)AnimateActors(delta);
        if(!rig.InteractionSettled||rig.SupportedCargo!=ResourceKind.Wood)
            throw new InvalidOperationException($"The old warehouse fixture has no settled parcel: {rig.SupportedCargo}, {rig.Position.DistanceTo(stand):F3} m, {rig.Handling}");
        int old=sim.World.TileAt(tile.X,tile.Y).BuildingId-1;
        sim.Buildings.Demolish(sim.World,old);
        int replacement=sim.Buildings.Place(sim.World,BuildingKind.Storage,tile.X,tile.Y,10);
        if(replacement!=old)throw new InvalidOperationException("Warehouse fixture did not reuse the original building slot");
        sim.Buildings.MarkComplete(replacement,sim.Clock);
        sim.Storage.SetCapacity(replacement,sim.Config.GroundStocks.DropAmount*4);
        sim.Storage.Withdraw(replacement,ResourceKind.Wood,float.MaxValue);
        BuildBuildings();
        sim.Actions.Tick(sim.Clock);
        if(!sim.Actions.TryGetLatestContactCompletion(slot,out var receipt)||receipt.Action!=ActionKind.StoreInBuilding
            ||receipt.Site is not {} site||site.BuildingGeneration==planned.BuildingGeneration
            ||sim.Storage.AmountOf(replacement,ResourceKind.Wood)<=0)
            throw new InvalidOperationException("The new warehouse did not accept the committed parcel");
        var matching=receipt with {Site=planned};
        if(!rig.CanBridgeContact(matching,recent:true)||rig.CanBridgeContact(matching,recent:false))
            throw new InvalidOperationException("The warehouse pose does not distinguish fresh and expired receipts");
        if(rig.CanBridgeContact(receipt,recent:true))
            throw new InvalidOperationException("An old warehouse pose can claim a different building generation");
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        string digest=sim.StateDigestString();
        for(int frame=0;frame<130;frame++)
        {
            AnimateActors(delta);
            if(rig.PlacingCargo||rig.ReleasedCargo)throw new InvalidOperationException("A replaced warehouse inherits the old deposit pose");
        }
        if(sim.StateDigestString()!=digest)throw new InvalidOperationException("Warehouse playback changes committed stock");
        GD.Print("WORLD_CONTACT_SITE_REPLACED_PASS: a reused warehouse slot accepts real stock but never inherits the old presentation contact, read-only");
    }

    private void ValidateRapidWarehousePlacement(ResidentRig rig,int slot,Vector2I tile,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        foreach(var kind in ResidentRig.CargoKinds)agents.SetInventory(slot,kind,0);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        for(int frame=0;frame<60;frame++)AnimateActors(delta);
        if(rig.CargoMesh.Visible)throw new InvalidOperationException("Rapid warehouse fixture starts with a displayed load");
        agents.SetInventory(slot,ResourceKind.Iron,1);
        agents.SetAction(slot,ActionKind.StoreInBuilding,ActionPhase.Executing);
        agents.SetTarget(slot,tile.X,tile.Y);
        sim.Actions.Tick(sim.Clock);
        if(!sim.Actions.TryGetCompletedContact(slot,out var receipt)||receipt.Resource!=ResourceKind.Iron||agents.InventoryOf(slot,ResourceKind.Iron)>0)
            throw new InvalidOperationException("Rapid warehouse fixture did not commit all iron before the first visual frame");
        string digest=sim.StateDigestString();
        int held=0,releases=0;
        bool released=false;
        for(int frame=0;frame<420;frame++)
        {
            AnimateActors(delta);
            if(rig.SupportedCargo==ResourceKind.Iron)held++;
            if(rig.ReleasedCargo&&!released)
            {
                if(held<12||rig.CargoMesh.Mesh!=rig.CargoMeshes[ResourceKind.Iron])
                    throw new InvalidOperationException("Rapid delivery releases a parcel without establishing two-handed support");
                releases++;
            }
            released=rig.ReleasedCargo;
        }
        if(releases!=1||rig.CargoMesh.Visible||sim.StateDigestString()!=digest)
            throw new InvalidOperationException($"Rapid delivery: held frames={held}, releases={releases}, state={rig.Handling}");
        GD.Print($"WORLD_RAPID_DELIVERY_PASS: committed without a shown load, {held} supported iron frames and one release, read-only");
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

    private void ValidateSkippedContact(ResidentRig rig,int slot,Vector2I tile,Vector3 logical,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;
        foreach(var (urgent,moved,skipped) in new[]{(false,false,2),(true,false,0),(false,true,0),(false,false,3)})
        {
            foreach(var kind in ResidentRig.CargoKinds)
            {
                agents.SetInventory(slot,kind,0);
                sim.GroundStocks.Withdraw(tile.X,tile.Y,kind,float.MaxValue);
            }
            agents.SetPosition(slot,tile.X,tile.Y);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
            rig.Position=logical;_target=logical;UpdateCamera();
            for(int i=0;i<150;i++)AnimateActors(delta);
            sim.GroundStocks.Deposit(tile.X,tile.Y,ResourceKind.Stone,1,sim.Config.GroundStocks);
            agents.SetAction(slot,ActionKind.Take,ActionPhase.Executing);
            agents.SetTarget(slot,tile.X,tile.Y);
            for(int i=0;i<230;i++)AnimateActors(delta);
            if(!rig.GestureEngaged(ActionKind.Take)||rig.ScenePickupGrasped)
                throw new InvalidOperationException("Skipped contact fixture failed to prepare its pickup");

            sim.Actions.Tick(sim.Clock);
            for(int i=0;i<skipped;i++)agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
            agents.SetAction(slot,urgent?ActionKind.Flee:ActionKind.Wander,ActionPhase.Moving);
            agents.SetTarget(slot,tile.X+1,tile.Y);
            if(moved)agents.SetPosition(slot,tile.X+1,tile.Y);
            if(sim.Actions.TryGetCompletedContact(slot,out _)||!sim.Actions.TryGetLatestContactCompletion(slot,out var pickup)
                ||pickup.Action!=ActionKind.Take||pickup.Site?.Resource!=ResourceKind.Stone)
                throw new InvalidOperationException("The skipped pickup did not retain its own committed resource");
            string digest=sim.StateDigestString();
            int grasps=0;bool grasped=false;
            bool interrupted=urgent||moved||skipped==3;
            if(!interrupted&&TryBridgeResidentContact(rig,slot,ActionKind.Wander,ActionPhase.Moving,agents.ActionRevisionOf(slot),
                new(tile.X,tile.Y),agents.TargetOf(slot),logical,0,0,out _,out _,out _))
                throw new InvalidOperationException("A paused observation claims an unshown pickup");
            for(int i=0;i<160;i++)
            {
                AnimateActors(delta);
                if(rig.ScenePickupGrasped&&!grasped)grasps++;
                grasped=rig.ScenePickupGrasped;
                if(interrupted&&rig.ScenePickupPieceVisible)throw new InvalidOperationException("Departure waits for an unshown grasp");
            }
            if(grasps!=(interrupted?0:1)||rig.ScenePickupPieceVisible||sim.StateDigestString()!=digest)
                throw new InvalidOperationException($"Skipped pickup was missed or replayed: urgent={urgent}, moved={moved}, grasps={grasps}, {rig.Handling}");

            if(interrupted)continue;
            agents.SetAction(slot,ActionKind.Deposit,ActionPhase.Executing);
            agents.SetTarget(slot,tile.X,tile.Y);
            for(int i=0;i<210;i++)AnimateActors(delta);
            if(rig.SupportedCargo!=ResourceKind.Stone)throw new InvalidOperationException("Skipped placement has no supported load");
            sim.Actions.Tick(sim.Clock);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
            agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
            digest=sim.StateDigestString();
            int releases=0;bool released=false;
            for(int i=0;i<190;i++)
            {
                AnimateActors(delta);
                if(rig.ReleasedCargo&&!released)releases++;
                released=rig.ReleasedCargo;
            }
            if(releases!=1||rig.CargoMesh.Visible||sim.StateDigestString()!=digest)
                throw new InvalidOperationException($"Skipped placement was missed or replayed: releases={releases}, {rig.Handling}");
        }
        agents.SetPosition(slot,tile.X,tile.Y);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        agents.SetInventory(slot,ResourceKind.Stone,0);
        agents.ClearTarget(slot);
        for(int i=0;i<120;i++)AnimateActors(delta);
        agents.SetAction(slot,ActionKind.Eat,ActionPhase.Executing);
        agents.SetInventory(slot,ResourceKind.Food,.2f);
        agents.SetHunger(slot,.18f);
        agents.ClearTarget(slot);
        int previousContacts=rig.ConsumeContacts;
        float gap=float.PositiveInfinity;
        string beforeMeal=sim.StateDigestString();
        for(int i=0;i<240;i++)
        {
            AnimateActors(delta);
            if(rig.Bite.Visible)gap=(rig.HandWorldPose(1)*ResidentRig.BiteCentre)
                .DistanceTo(rig.RigWorldSpace*rig.Torso.Transform*rig.MouthContact)/rig.Scale.X;
            if(gap<.045f||rig.ConsumeContacts!=previousContacts||rig.ConversationMouth>.025f)
                throw new InvalidOperationException("An unconfirmed meal reaches the mouth");
        }
        if(!rig.ConsumptionPrepared||!rig.Bite.Visible||sim.StateDigestString()!=beforeMeal)
            throw new InvalidOperationException($"The unconfirmed meal cannot wait with the food in hand: {gap:F3} m, {rig.Handling}");
        uint mealRevision=agents.ActionRevisionOf(slot);
        sim.Actions.Tick(sim.Clock);
        sim.GroundStocks.Deposit(tile.X,tile.Y,ResourceKind.Stone,1,sim.Config.GroundStocks);
        agents.SetAction(slot,ActionKind.Take,ActionPhase.Executing);
        agents.SetTarget(slot,tile.X,tile.Y);
        sim.Actions.Tick(sim.Clock);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        if(!sim.Actions.TryGetLatestContactCompletion(slot,out var latest)||latest.Action!=ActionKind.Take
            ||!sim.Actions.TryGetContactCompletion(slot,mealRevision,out var meal)||meal.Action!=ActionKind.Eat||meal.Site.HasValue)
            throw new InvalidOperationException("The committed meal must remain accessible after a newer pickup commits");
        string afterMeal=sim.StateDigestString();
        float chew=0;
        for(int i=0;i<150;i++)
        {
            AnimateActors(delta);
            if(rig.ConsumeContacts>previousContacts)chew=MathF.Max(chew,rig.ConversationMouth);
            if(rig.ScenePickupPieceVisible||rig.ScenePickupGrasped)
                throw new InvalidOperationException("The unobserved newer pickup cannot play in place of the prepared meal");
        }
        if(rig.ConsumeContacts!=previousContacts+1||chew<.055f||rig.Bite.Visible||sim.StateDigestString()!=afterMeal)
            throw new InvalidOperationException($"Skipped meal did not reach the mouth once and recover: contacts={rig.ConsumeContacts-previousContacts}, {rig.Handling}");
        GD.Print("WORLD_CONTACT_BRIDGE_PASS: prepared pickup, deposit and meal survive two or three decisions once; newer unobserved pickup does not replace the meal; flight, tile changes and older decisions cancel, read-only");
        ValidateConfirmedDrink(rig,slot,delta);
    }

    private void ValidateConfirmedDrink(ResidentRig rig,int slot,float delta)
    {
        var sim=Game.Sim;var agents=sim.Agents;var world=sim.World;
        var tile=Enumerable.Range(0,world.Width*world.Height)
            .Select(i=>new Vector2I(i%world.Width,i/world.Width))
            .First(p=>world.TileAt(p.X,p.Y).Walkable&&TryShoreContact(PositionAt(p.X,p.Y),out _,cardinalOnly:true));
        agents.SetPosition(slot,tile.X,tile.Y);
        rig.Position=PositionAt(tile.X,tile.Y);rig.Travel=default;
        if(TryResidentWaterContact(slot,rig.Position,out var bank))rig.Rotation=new(0,HeadingYaw(Horizontal(bank-rig.Position)),0);
        _target=rig.Position;_distance=8;UpdateCamera();
        for(int i=0;i<120;i++)AnimateActors(delta);
        int contacts=rig.ConsumeContacts;
        agents.SetAction(slot,ActionKind.Drink,ActionPhase.Done);
        for(int i=0;i<180;i++)
        {
            AnimateActors(delta);
            if(rig.Cup.Visible||rig.ConsumeContacts!=contacts)
                throw new InvalidOperationException("A Done phase without a drink receipt creates a visible sip");
        }
        foreach(bool confirmed in new[]{false,true})
        {
            agents.SetNeed(slot,NeedIndex.Thirst,.20f);
            agents.SetAction(slot,ActionKind.Drink,ActionPhase.Executing);
            agents.ClearTarget(slot);
            string pending=sim.StateDigestString();
            for(int i=0;i<240;i++)
            {
                AnimateActors(delta);
                if(rig.ConsumeContacts!=contacts)throw new InvalidOperationException("The resident drinks without confirmation");
            }
            if(!rig.ConsumptionPrepared||!rig.Cup.Visible||sim.StateDigestString()!=pending)
                throw new InvalidOperationException("The resident does not hold the cup while awaiting a valid drink");
            if(confirmed)sim.Actions.Tick(sim.Clock);
            agents.SetAction(slot,confirmed?ActionKind.None:ActionKind.Flee,confirmed?ActionPhase.Idle:ActionPhase.Moving);
            string digest=sim.StateDigestString();
            AnimateActors(delta);
            if(!rig.Cup.Visible)throw new InvalidOperationException("The cup vanishes during the decision change");
            for(int i=0;i<220;i++)AnimateActors(delta);
            if(rig.ConsumeContacts!=contacts+(confirmed?1:0)||rig.Cup.Visible||sim.StateDigestString()!=digest)
                throw new InvalidOperationException($"Drink confirmation or interruption failed: {rig.ConsumeContacts-contacts}, {rig.Handling}");
        }
        void RequestDrink()
        {
            agents.SetNeed(slot,NeedIndex.Thirst,.20f);
            agents.SetAction(slot,ActionKind.Drink,ActionPhase.Executing);
        }
        int startContacts=rig.ConsumeContacts;
        RequestDrink();
        for(int i=0;i<180&&!rig.ConsumptionPrepared;i++)AnimateActors(delta);
        sim.Actions.Tick(sim.Clock);
        for(int i=0;i<120&&rig.ConsumeContacts==startContacts;i++)AnimateActors(delta);
        if(rig.ConsumeContacts!=startContacts+1||!rig.Cup.Visible)
            throw new InvalidOperationException("The first of two drinks did not leave a held cup");
        RequestDrink();
        string between=sim.StateDigestString();
        for(int i=0;i<95;i++)
        {
            AnimateActors(delta);
            if(!rig.Cup.Visible||rig.ConsumeContacts!=startContacts+1)
                throw new InvalidOperationException("A same-shore successor releases its cup or sips before confirmation");
        }
        if(!rig.ConsumptionPrepared||sim.StateDigestString()!=between)
            throw new InvalidOperationException("A retained cup does not settle at chest height while awaiting the next confirmation");
        sim.Actions.Tick(sim.Clock);
        string secondSip=sim.StateDigestString();
        for(int i=0;i<190;i++)AnimateActors(delta);
        if(rig.ConsumeContacts!=startContacts+2||rig.Cup.Visible||sim.StateDigestString()!=secondSip)
            throw new InvalidOperationException("The second confirmed drink must sip once and return the cup");
        GD.Print("WORLD_SHARED_CUP_PASS: same shore, cup stays in hand over two independently confirmed sips, no early mouth contact, read-only");
        GD.Print("WORLD_CONSUMPTION_COMMIT_PASS: no unconfirmed contact, no cup from an empty receipt, interrupted cup returns, confirmed meal and drink each contact once across skipped Done, chewing survives the decision change, read-only");
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

        SyncEntities();
        long giverId=sim.Society.Identity(giverSlot),receiverId=sim.Society.Identity(receiverSlot);
        var giver=(ResidentRig)_people[giverId];var receiver=(ResidentRig)_people[receiverId];
        var start=giver.Position;var end=receiver.Position;
        giver.Rotation=new(0,HeadingYaw(Horizontal(end-start))+.9f,0);
        receiver.Rotation=new(0,HeadingYaw(Horizontal(start-end))-.9f,0);
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
        string pending=sim.StateDigestString();
        ResidentHandoff? motion=null;
        float approach=0;
        bool faced=false;
        for(int i=0;i<200;i++)
        {
            AnimateActors(delta);
            if(motion==null&&_residentHandoffs.Count>0)motion=_residentHandoffs[0].Motion;
            if(motion?.ParcelVisible==true)throw new InvalidOperationException("A proposed handoff shows food before simulation commits");
            if(motion?.UsesMeeting!=true)continue;
            approach=MathF.Max(approach,Horizontal(giver.Position-start).Length());
            var line=Horizontal(receiver.Position-giver.Position);
            faced=giver.InteractionSettled&&receiver.InteractionSettled&&InteractionFacingAligned(giver.Rotation.Y,line)
                &&InteractionFacingAligned(receiver.Rotation.Y,-line);
            if(faced)break;
        }
        if(motion==null||!faced||approach<.20f||pending!=sim.StateDigestString())
            throw new InvalidOperationException($"Uncommitted handoff fails to approach and face the actual partner: {approach:F3} m, faced={faced}");
        uint revision=agents.ActionRevisionOf(giverSlot);
        sim.Actions.Tick(sim.Clock);
        for(int decision=0;decision<3;decision++)
        {
            agents.SetAction(giverSlot,ActionKind.None,ActionPhase.Idle);
            agents.SetAction(receiverSlot,ActionKind.None,ActionPhase.Idle);
        }
        if(sim.Actions.TryGetPresentationPartner(giverSlot,out _)||!HasPairReceipt(giverSlot,receiverSlot,ActionKind.ShareFood,revision))
            throw new InvalidOperationException("World fixture does not have a committed food exchange across the skipped phase");
        string committed=sim.StateDigestString();
        int parcelFrames=0,offeringFrames=0;
        for(int i=0;i<490;i++)
        {
            AnimateActors(delta);
            if(motion?.ParcelVisible==true)
            {
                parcelFrames++;
                if(!motion.Transferred)offeringFrames++;
                if(!motion.Transferred&&(!giver.InteractionSettled||!receiver.InteractionSettled))
                    throw new InvalidOperationException("Parcel appears before both world residents settle");
                if(motion.Transferred&&(giver.HandoffReserved||motion.UsesMeeting||!receiver.HandoffReserved))
                    throw new InvalidOperationException("Released giver still waits while the receiver draws the parcel back");
            }
        }
        if(motion==null||!motion.Transferred||!motion.Finished||approach<.20f||parcelFrames<18
            ||offeringFrames>40||parcelFrames-offeringFrames>50
            ||_residentHandoffs.Count!=0||sim.StateDigestString()!=committed)
            throw new InvalidOperationException($"World handoff misses approach, timely transfer or one-shot cleanup: distance={approach}, frames={parcelFrames}, offer={offeringFrames}, transferred={motion?.Transferred}");
        GD.Print($"WORLD_HANDOFF_PASS: prepared adjacent-tile recipient, {approach:F3} m visual approach, {parcelFrames} supported frames ({offeringFrames} before transfer), skipped Done, independent giver release and read-only playback");

        agents.SetInventory(giverSlot,ResourceKind.Food,sim.Config.Relationship.ShareFoodAmount);
        agents.SetHunger(receiverSlot,.92f);
        agents.SetAction(giverSlot,ActionKind.ShareFood,ActionPhase.Executing);
        agents.SetAction(receiverSlot,ActionKind.Sleep,ActionPhase.Executing);
        sim.Actions.Tick(sim.Clock);
        AnimateActors(delta);
        if(_residentHandoffs.Count!=0)throw new InvalidOperationException("A committed exchange steals an occupied receiver");
        agents.SetAction(receiverSlot,ActionKind.None,ActionPhase.Idle);
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
        if(outgoing.UsesMeeting||!outgoing.ParcelVisible||receiver.HandoffReserved)
            throw new InvalidOperationException("Superseded handoff keeps the released receiver or fails to recover the parcel");
        for(int i=0;i<120;i++)AnimateActors(delta);
        if(outgoing.Transferred||!outgoing.Finished||_residentHandoffs.Count!=0||sim.StateDigestString()!=committed)
            throw new InvalidOperationException("Superseded handoff reaches the old recipient or leaves a parcel");
        GD.Print("WORLD_HANDOFF_RETARGET_PASS: occupied receiver can join after becoming free, superseded share releases that receiver and recovers its parcel");

        agents.SetInventory(giverSlot,ResourceKind.Food,sim.Config.Relationship.ShareFoodAmount);
        agents.SetHunger(receiverSlot,.92f);
        agents.SetAction(giverSlot,ActionKind.ShareFood,ActionPhase.Executing);
        sim.Actions.Tick(sim.Clock);
        for(int decision=0;decision<2;decision++)
        {
            agents.SetAction(giverSlot,ActionKind.None,ActionPhase.Idle);
            agents.SetAction(receiverSlot,ActionKind.None,ActionPhase.Idle);
        }
        committed=sim.StateDigestString();
        ResidentHandoff? resumed=null;
        for(int i=0;i<490;i++)
        {
            AnimateActors(delta);
            if(_residentHandoffs.Count==0)continue;
            var active=_residentHandoffs[0];
            if(active.Receiver!=receiverId||resumed!=null&&resumed!=active.Motion)
                throw new InvalidOperationException("Unobserved share uses another recipient or replays");
            resumed=active.Motion;
        }
        if(resumed==null||!resumed.Transferred||!resumed.Finished||_residentHandoffs.Count!=0||sim.StateDigestString()!=committed)
            throw new InvalidOperationException("Unobserved share never completes its committed handoff");
        GD.Print("WORLD_HANDOFF_CATCHUP_PASS: no rendered executing or done frame, one handover to the committed recipient, read-only");
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
        float deposited=sim.GroundStocks.Deposit(goal.X,goal.Y,ResourceKind.Food,12,sim.Config.GroundStocks);
        var stock=new ActionSystem.ActionContact(goal,ResourceKind.Food,ActionSystem.ContactSurface.GroundStock,deposited);
        if(deposited<=0||!TryActionContact(rig,ActionKind.Take,stock,Place(goal),out _,out var pickupStand))
            throw new InvalidOperationException("Projected route has no supported pickup stand");
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
            if(working&&(!rig.InteractionSettled||Horizontal(rig.Position-pickupStand).Length()>.04f))
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

    private void ValidateRedirectedResident(float delta)
    {
        var sim=Game.Sim;
        var agents=sim.Agents;
        bool Straight(Int2 start,int direction)
        {
            var path=_observerPaths.FindPath(start.X,start.Y,start.X+2*direction,start.Y,_observedRoute);
            return path.Success&&path.Length==3&&_observedRoute[1]==new Int2(start.X+direction,start.Y);
        }
        var tile=Enumerable.Range(0,sim.World.Width*sim.World.Height)
            .Select(i=>new Int2(i%sim.World.Width,i/sim.World.Width))
            .First(p=>p.X>=3&&p.X<sim.World.Width-3&&p.Y>=2&&p.Y<sim.World.Height-2
                &&Enumerable.Range(-2,4).All(dx=>ResidentRouteLegClear(new(p.X+dx,p.Y),new(p.X+dx+1,p.Y)))
                &&Straight(p,1)&&Straight(p,-1));
        var origin=PositionAt(tile.X,tile.Y);
        if(sim.InterveneSpawnHumans(tile.X,tile.Y,1,1)!=1)
            throw new InvalidOperationException("Redirect fixture could not spawn a walker");
        int slot=agents.AliveSlots().Last();
        agents.SetPosition(slot,tile.X,tile.Y);
        agents.SetLifeStage(slot,LifeStage.Adult);
        agents.SetAction(slot,ActionKind.None,ActionPhase.Idle);
        agents.SetInventory(slot,ResourceKind.Wood,4);
        SyncEntities();
        var rig=(ResidentRig)_people[sim.Society.Identity(slot)];
        var forward=Horizontal(PositionAt(tile.X+1,tile.Y)-origin).Normalized();
        rig.Rotation=new(0,HeadingYaw(forward),0);
        _target=origin;_distance=9;_pitch=.62f;UpdateCamera();
        for(int frame=0;frame<130;frame++)AnimateActors(delta);
        if(rig.SupportedCargo!=ResourceKind.Wood)
            throw new InvalidOperationException("Redirect fixture cannot support its load");

        double ticks=0;
        long tick=sim.Clock;
        void Advance()
        {
            ticks+=delta*Game.ObservationTickRate;
            while(ticks>=1){ticks--;sim.Actions.Tick(++tick);}
            _target=rig.Position;UpdateCamera();
            string digest=sim.StateDigestString();
            AnimateActors(delta,(float)ticks);
            if(sim.StateDigestString()!=digest)
                throw new InvalidOperationException("A rendered redirect changes simulation state");
        }

        agents.SetAction(slot,ActionKind.Wander,ActionPhase.Moving);
        agents.SetTarget(slot,tile.X+2,tile.Y);
        float entrySpeed=0;
        for(int frame=0;frame<160;frame++)
        {
            var prior=rig.Position;
            Advance();
            entrySpeed=Horizontal(rig.Position-prior).Dot(forward)/delta;
            if(entrySpeed>.80f&&Horizontal(rig.Position-origin).Dot(forward)>.40f)break;
        }
        if(entrySpeed<=.80f||agents.XOf(slot)!=tile.X||agents.YOf(slot)!=tile.Y||!rig.Travel.Projecting)
            throw new InvalidOperationException($"Redirect fixture never reaches a projected forward step: speed={entrySpeed:F3}");

        agents.SetAction(slot,ActionKind.Flee,ActionPhase.Moving);
        agents.SetTarget(slot,tile.X-2,tile.Y);
        sim.Actions.ClearMoveProgress(slot);
        float previousSpeed=entrySpeed,brakeDistance=0,brakingPeak=0,pivotDrift=0,soleDrift=0,reverseDistance=0;
        int redirectFrames=0,pivotFrames=0;
        Vector3 Sole(int side)=>rig.Feet[side].GlobalTransform*rig.BootSoleCurve[12];
        for(int frame=0;frame<230&&reverseDistance<.25f;frame++)
        {
            var prior=rig.Position;
            var left=Sole(0);
            var right=Sole(1);
            Advance();
            var movement=Horizontal(rig.Position-prior);
            float signedStep=movement.Dot(forward),speed=signedStep/delta;
            if(movement.Length()>ResidentRig.MaximumGaitSpeed*delta+.001f
                ||!ScenerySegmentClear(prior,rig.Position)||!SceneryLegSupported(prior,rig.Position))
                throw new InvalidOperationException("Redirect braking crosses unsupported ground");
            if(rig.SupportedCargo!=ResourceKind.Wood||rig.ReleasedCargo)
                throw new InvalidOperationException("Redirect drops or replaces the carried parcel");
            if(rig.Travel.Redirecting)
            {
                redirectFrames++;
                brakeDistance+=MathF.Max(0,signedStep);
                brakingPeak=MathF.Max(brakingPeak,(previousSpeed-speed)/delta);
                if(signedStep<-.001f)throw new InvalidOperationException("Resident reverses translation before planting and turning");
            }
            if(rig.IsPivoting)
            {
                pivotFrames++;
                pivotDrift=MathF.Max(pivotDrift,movement.Length());
                soleDrift=MathF.Max(soleDrift,MathF.Min(Horizontal(Sole(0)-left).Length(),
                    Horizontal(Sole(1)-right).Length()));
            }
            if(signedStep<-.001f)
            {
                if(!InteractionFacingAligned(rig.Rotation.Y,movement.Normalized()))
                    throw new InvalidOperationException($"Reverse step starts before facing the new route: frame={frame}, yaw={rig.Rotation.Y:F3}/{HeadingYaw(movement):F3}, step={signedStep:F4}, projected={rig.Travel.Projecting}, redirect={redirectFrames}, pivot={pivotFrames}");
                reverseDistance-=signedStep;
            }
            previousSpeed=speed;
        }
        if(redirectFrames<24||pivotFrames<14||brakeDistance<.035f||brakeDistance>.33f||brakingPeak>4.7f
            ||pivotDrift>.003f||soleDrift>.022f||reverseDistance<.25f)
            throw new InvalidOperationException($"World redirect: frames={redirectFrames}/{pivotFrames}, braking distance={brakeDistance:F3}, peak braking={brakingPeak:F2}, root/sole drift={pivotDrift:F4}/{soleDrift:F4}, resumed={reverseDistance:F3}");
        GD.Print($"WORLD_REDIRECT_PASS: {entrySpeed:F2} m/s entry, {brakeDistance:F3} m forward braking, {pivotFrames} pivot frames, root/sole drift={pivotDrift:F4}/{soleDrift:F4} m, reversed with load and unchanged simulation");
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
