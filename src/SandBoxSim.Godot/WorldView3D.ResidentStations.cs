using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private const float StationSpacing=.57f;
    private static readonly Vector2[] ResidentStationOffsets=
    {
        Vector2.Zero,new(-StationSpacing,0),new(StationSpacing,0),new(0,-StationSpacing),new(0,StationSpacing),
        new(-StationSpacing,-StationSpacing),new(StationSpacing,-StationSpacing),
        new(-StationSpacing,StationSpacing),new(StationSpacing,StationSpacing),
    };
    private readonly Dictionary<Vector2I,int> _residentStationMasks=new();
    private float _residentStationWait;
    private int _residentStationTerrainRevision=-1,_residentStationSceneryRevision=-1;

    private bool TryResidentStation(Vector2I tile,int station,out Vector3 spot)
    {
        var centre=PositionAt(tile.X,tile.Y);
        if(station==0){spot=centre;return true;}
        var offset=ResidentStationOffsets[station];
        spot=centre+new Vector3(offset.X,0,offset.Y);
        spot.Y=GroundHeight(spot.X,spot.Z);
        return ScenerySegmentClear(centre,spot)&&SceneryLegSupported(centre,spot);
    }

    private void UpdateResidentStations(float delta)
    {
        _residentStationWait-=delta;
        if(_residentStationWait>0)return;
        _residentStationWait=.15f;
        var sim=Game.Sim;
        bool terrainChanged=_residentStationTerrainRevision!=sim.World.Revision
            ||_residentStationSceneryRevision!=_sceneryPlacementRevision;
        _residentStationTerrainRevision=sim.World.Revision;
        _residentStationSceneryRevision=_sceneryPlacementRevision;
        _residentStationMasks.Clear();

        // Keep the same place while neighbours arrive or leave. A new entrant
        // takes the next free supported place instead of moving everyone.
        foreach(var (id,node) in _people)
        {
            var rig=(ResidentRig)node;
            int slot=(int)(id&uint.MaxValue);
            if(!sim.Agents.IsSlotAlive(slot)||sim.Society.Identity(slot)!=id||
                sim.Agents.PhaseOf(slot)==ActionPhase.Moving)
            {rig.StationSlot=-1;continue;}
            var tile=new Vector2I(sim.Agents.XOf(slot),sim.Agents.YOf(slot));
            if(tile!=rig.StationTile){rig.StationTile=tile;rig.StationSlot=-1;}
            if(rig.StationSlot<0)continue;
            if(terrainChanged&&!TryResidentStation(tile,rig.StationSlot,out _)){rig.StationSlot=-1;continue;}
            _residentStationMasks[tile]=_residentStationMasks.GetValueOrDefault(tile)|(1<<rig.StationSlot);
        }
        foreach(var (id,node) in _people)
        {
            var rig=(ResidentRig)node;
            int slot=(int)(id&uint.MaxValue);
            if(rig.StationSlot>=0||!sim.Agents.IsSlotAlive(slot)||sim.Society.Identity(slot)!=id||
                sim.Agents.PhaseOf(slot)==ActionPhase.Moving)continue;
            var tile=rig.StationTile;
            int occupied=_residentStationMasks.GetValueOrDefault(tile);
            for(int station=0;station<ResidentStationOffsets.Length;station++)
            {
                int bit=1<<station;
                if((occupied&bit)!=0||!TryResidentStation(tile,station,out _))continue;
                rig.StationSlot=station;_residentStationMasks[tile]=occupied|bit;
                break;
            }
        }
    }
}
