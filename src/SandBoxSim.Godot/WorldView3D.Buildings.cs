using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
namespace SandBoxSim.Client;
public partial class WorldView3D
{
    private sealed class BuildingVisual
    {
        public required Node3D Root;
        public required MeshInstance3D Surface;
        public required Mesh Near,Far;
        public required int Generation;
        public required uint Identity;
        public required BuildingKind Kind;
        public required bool Complete;
        public bool Detailed=true;
    }
    private readonly Dictionary<int,BuildingVisual> _buildingVisuals=new();
    private readonly Dictionary<int,Label3D> _settlementLabels=new();
    private Node3D? _buildings;
    private void ResetBuildingVisuals()
    {
        if(_buildings!=null&&GodotObject.IsInstanceValid(_buildings))
        {_buildings.GetParent()?.RemoveChild(_buildings);_buildings.QueueFree();}
        _buildingVisuals.Clear();_settlementLabels.Clear();_buildings=null;
    }
    private void RemoveBuilding(int slot)
    {
        var root=_buildingVisuals[slot].Root;_buildings!.RemoveChild(root);root.QueueFree();_buildingVisuals.Remove(slot);
    }
    private void UpdateBuildingDetail()
    {
        foreach(var visual in _buildingVisuals.Values)
        {
            float distance=_camera.Position.DistanceSquaredTo(visual.Root.Position);
            bool detailed=visual.Detailed?distance<32*32:distance<24*24;
            if(detailed==visual.Detailed)continue;
            visual.Detailed=detailed;visual.Surface.Mesh=detailed?visual.Near:visual.Far;
        }
    }
    private void BuildBuildings()
    {
        using var profile=RenderProfile.Measure("BuildBuildings");
        if(_buildings==null){_buildings=new Node3D {Name="Buildings"};_scene.AddChild(_buildings);}
        var sim=Game.Sim;var store=sim.Buildings;
        foreach(int slot in _buildingVisuals.Keys.Where(slot=>!store.IsAlive(slot)).ToArray())RemoveBuilding(slot);
        for(int live=0;live<store.LiveCount;live++)
        {
            int slot=store.LiveAt(live),x=store.XOf(slot),y=store.YOf(slot);
            uint identity=unchecked((uint)(x*73856093^y*19349663^sim.World.Seed*83492791));
            var kind=store.KindOf(slot);bool complete=store.StateOf(slot)==BuildingState.Complete;int generation=store.GenerationOf(slot);
            if(!_buildingVisuals.TryGetValue(slot,out var visual)||visual.Generation!=generation||visual.Identity!=identity||visual.Kind!=kind||visual.Complete!=complete)
            {
                using var creation=RenderProfile.Measure("CreateBuilding");
                if(visual!=null)RemoveBuilding(slot);
                var node=_models.Building(kind,complete,identity);_buildings.AddChild(node);
                var surface=node.GetChild<MeshInstance3D>(0);
                visual=new BuildingVisual {Root=node,Surface=surface,Near=surface.Mesh,Far=_models.DistantBuilding(kind,complete,identity),Generation=generation,Identity=identity,Kind=kind,Complete=complete};
                _buildingVisuals[slot]=visual;
            }
            visual.Root.Position=PositionAt(x,y);
            int facing=(int)(identity%4),best=int.MinValue;
            for(int side=0;side<4;side++)
            {
                int direction=(side+(int)(identity%4))%4;
                int nx=x+(direction==1?1:direction==3?-1:0),ny=y+(direction==0?1:direction==2?-1:0);
                if(!sim.World.IsInBounds(nx,ny))continue;
                var tile=sim.World.TileAt(nx,ny);
                int score=tile.Terrain==TerrainKind.Road?10:tile.BuildingId>0?-5:tile.Walkable?1:-10;
                if(score>best){best=score;facing=direction;}
            }
            visual.Root.RotationDegrees=new(0,facing*90,0);
        }
        var liveSettlements=new HashSet<int>();
        for(int i=0;i<sim.Settlements.EntityCount;i++)
        {
            var settlement=sim.Settlements.At(i);if(settlement.Dissolved)continue;
            liveSettlements.Add(settlement.Id);
            if(!_settlementLabels.TryGetValue(settlement.Id,out var label))
            {
                label=new Label3D {Font=Game.Theme.DefaultFont,FontSize=32,PixelSize=.025f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Modulate=new Color("#ead9ad")};
                _settlementLabels[settlement.Id]=label;_buildings.AddChild(label);
            }
            label.Text=sim.Society.SettlementName(settlement.Id);label.Position=PositionAt(settlement.CenterX,settlement.CenterY)+Vector3.Up*5;
        }
        foreach(int id in _settlementLabels.Keys.Where(id=>!liveSettlements.Contains(id)).ToArray())
        {var label=_settlementLabels[id];_buildings.RemoveChild(label);label.QueueFree();_settlementLabels.Remove(id);}
        UpdateBuildingDetail();
    }
    public void ValidateIncrementalBuildings()
    {
        var sim=Game.Sim;var world=sim.World;
        for(int y=4;y<=12;y++)for(int x=4;x<=12;x++)world.SetTerrain(x,y,TerrainKind.Grass);
        int first=sim.Buildings.Place(world,BuildingKind.House,6,6,10),second=sim.Buildings.Place(world,BuildingKind.House,10,10,10);
        if(first<0||second<0)throw new Exception("Building cache fixture failed");
        BuildBuildings();var retained=_buildingVisuals[first].Root;var changed=_buildingVisuals[second].Root;
        string before=StateHash.ComputeDigest(sim);BuildBuildings();
        if(before!=StateHash.ComputeDigest(sim)||_buildingVisuals[first].Root!=retained)throw new Exception("Unchanged building regenerated or observation mutated world");
        world.SetTerrain(7,6,TerrainKind.Road);BuildBuildings();
        if(_buildingVisuals[first].Root!=retained||Math.Abs(retained.RotationDegrees.Y-90)>.01f)throw new Exception("Road-facing update regenerated geometry or did not rotate");
        world.Tiles[6*world.Width+6].Height+=.2f;BuildBuildings();
        if(_buildingVisuals[first].Root!=retained||retained.Position.DistanceTo(PositionAt(6,6))>.001f)throw new Exception("Ground height update lost building identity");
        sim.Buildings.MarkComplete(second,sim.Clock);BuildBuildings();
        if(_buildingVisuals[first].Root!=retained||_buildingVisuals[second].Root==changed)throw new Exception("Completion replaced unrelated building");
        sim.Buildings.Demolish(world,second);BuildBuildings();
        if(_buildingVisuals.ContainsKey(second)||_buildingVisuals[first].Root!=retained)throw new Exception("Demolition leaves stale geometry");
        int reused=sim.Buildings.Place(world,BuildingKind.Storage,10,10,10);BuildBuildings();
        if(reused<0||_buildingVisuals[reused].Kind!=BuildingKind.Storage||_buildingVisuals[reused].Generation!=sim.Buildings.GenerationOf(reused))throw new Exception("Reused building slot shows old geometry");
        GD.Print("BUILDING_CACHE_PASS: reuse, road/height updates, completion, demolition and slot generations");
    }
}
