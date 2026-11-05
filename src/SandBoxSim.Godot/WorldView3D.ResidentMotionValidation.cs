using System;
using System.Linq;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Foundation;

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

        float peak=0,nearestCorner=float.PositiveInfinity,stepLift=0,plantDrift=0;
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
                AnimateActors(delta);
                peak=MathF.Max(peak,Horizontal(rig.Position-previous).Length()/delta);
                nearestCorner=MathF.Min(nearestCorner,Horizontal(rig.Position-corner).Length());
                retained|=rig.Travel.Queued;
                if(!ScenerySegmentClear(previous,rig.Position))throw new InvalidOperationException("Resident crosses a scenery solid");
                var left=rig.Feet[0].GlobalPosition;var right=rig.Feet[1].GlobalPosition;
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
                    if(rig.IsPivoting||Horizontal(rig.Position-goal).Length()>.035f)
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
            ||pivotFrames<20||stepLift<.017f||plantDrift>.020f||!startedWork)
            throw new InvalidOperationException($"Resident world route: peak={peak}, corner={nearestCorner}, queue={retained}, pivots={pivotFrames}, lift={stepLift}, drift={plantDrift}, action={startedWork}");
        GD.Print($"RESIDENT_WORLD_MOTION_PASS: actual world actor, original root peak={oldPeak:F2} m/s, paced={peak:F2} m/s, recorded corner retained, pivot frames={pivotFrames}, foot lift={stepLift:F3} m, planted frame drift={plantDrift:F4} m, arrival then pickup, unchanged simulation");
        if(preparationFrames<6||departureTransfer<.015f||departureRootDrift>.001f||departureFootDrift>.010f)
            throw new InvalidOperationException($"Resident first step: frames={preparationFrames}, pelvis={departureTransfer}, root drift={departureRootDrift}, foot drift={departureFootDrift}");
        GD.Print($"RESIDENT_DEPARTURE_PASS: preparation={preparationFrames} frames, lateral pelvis transfer={departureTransfer:F3} m, root/feet drift={departureRootDrift:F4}/{departureFootDrift:F4} m before the first step");
        ValidateWorldCultivation(rig,slot,tiles.Goal,delta);
        ValidateResidentStations(tiles.Goal,tiles.Corner,delta);
        ValidateProjectedResidentMotion(delta);
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
        _target=(Place(start)+Place(goal))*.5f;_distance=14;UpdateCamera();

        double tickFraction=0;
        long tick=0;
        int crossed=0,projectedFrames=0,preparations=0;
        float lowCruise=float.PositiveInfinity,peak=0;
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
            string digest=sim.StateDigestString();
            long searches=sim.Pathfinder.TotalSearches;
            AnimateActors(delta,(float)Math.Min(1,tickFraction));
            if(sim.StateDigestString()!=digest||sim.Pathfinder.TotalSearches!=searches)
                throw new InvalidOperationException("Projected travel changed simulation or simulation search counts");
            float speed=Horizontal(rig.Position-before).Length()/delta;
            peak=MathF.Max(peak,speed);
            if(rig.Travel.Projecting)projectedFrames++;
            if(rig.Travel.Preparation>0&&crossed>0&&crossed<route.Length-1)preparations++;
            if(crossed>0&&crossed<route.Length-2)lowCruise=MathF.Min(lowCruise,speed);
            if(!ScenerySegmentClear(before,rig.Position)||!SceneryLegSupported(before,rig.Position))
                throw new InvalidOperationException("Projected travel crossed scenery or lost ground support");
            working=rig.GestureEngaged(ActionKind.Take);
            if(working&&(!rig.InteractionSettled||Horizontal(rig.Position-Place(goal)).Length()>.04f))
                throw new InvalidOperationException("Projected walker reached for a resource before settling");
        }
        if(crossed!=route.Length-1||!working||projectedFrames<180||preparations!=0||lowCruise<.40f||peak>ResidentRig.MaximumGaitSpeed+.001f)
            throw new InvalidOperationException($"Projected resident route: tiles={crossed}/{route.Length-1}, projected frames={projectedFrames}, false starts={preparations}, cruise floor={lowCruise}, peak={peak}, work={working}");

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
