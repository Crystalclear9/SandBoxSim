using System;
using System.Linq;
using Godot;
using SandBoxSim.Core.Agents;

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
        sim.Agents.SetTarget(slot,tiles.Goal.X,tiles.Goal.Y);
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
