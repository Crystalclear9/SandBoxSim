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
        public Mesh? Near;
        public required Mesh Far;
        public required int Generation;
        public required uint Identity;
        public required BuildingKind Kind;
        public required bool Complete;
        public bool Detailed;
        public Node3D? Life;public int LifeKey=-1;
        public MeshInstance3D? Foundation;
        public Vector4 FoundationDepths;
    }
    private readonly Dictionary<int,BuildingVisual> _buildingVisuals=new();
    private readonly Dictionary<int,Label3D> _settlementLabels=new();
    private Node3D? _buildings;
    private ulong _detailBuildFrame=ulong.MaxValue;
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
        foreach(var pair in _buildingVisuals)
        {UpdateHouseLife(pair.Value,pair.Key);UpdateFarmLife(pair.Value,pair.Key);}
        foreach(var visual in _buildingVisuals.Values)
        {
            float distance=_camera.Position.DistanceSquaredTo(visual.Root.Position);
            bool detailed=visual.Detailed?distance<32*32:distance<24*24;
            if(detailed&&visual.Near==null)
            {
                ulong frame=Engine.GetProcessFrames();if(_detailBuildFrame==frame)continue;
                _detailBuildFrame=frame;
                using var profile=RenderProfile.Measure("CreateBuildingDetail");
                var model=_models.Building(visual.Kind,visual.Complete,visual.Identity);
                visual.Near=model.GetChild<MeshInstance3D>(0).Mesh;model.Free();
            }
            if(detailed==visual.Detailed)continue;
            visual.Detailed=detailed;visual.Surface.Mesh=detailed?visual.Near:visual.Far;
        }
    }
    private bool BuildBuildings(int maxCreations=int.MaxValue)
    {
        using var profile=RenderProfile.Measure("BuildBuildings");
        if(_buildings==null){_buildings=new Node3D {Name="Buildings"};_scene.AddChild(_buildings);}
        var sim=Game.Sim;var store=sim.Buildings;int creations=0;bool remaining=false;
        foreach(int slot in _buildingVisuals.Keys.Where(slot=>!store.IsAlive(slot)).ToArray())RemoveBuilding(slot);
        for(int live=0;live<store.LiveCount;live++)
        {
            int slot=store.LiveAt(live),x=store.XOf(slot),y=store.YOf(slot);
            uint identity=unchecked((uint)(x*73856093^y*19349663^sim.World.Seed*83492791));
            var kind=store.KindOf(slot);bool complete=store.StateOf(slot)==BuildingState.Complete;int generation=store.GenerationOf(slot);
            if(!_buildingVisuals.TryGetValue(slot,out var visual)||visual.Generation!=generation||visual.Identity!=identity||visual.Kind!=kind||visual.Complete!=complete)
            {
                if(creations>=maxCreations){remaining=true;continue;}creations++;
                using var creation=RenderProfile.Measure("CreateBuilding");
                if(visual!=null)RemoveBuilding(slot);
                var node=_models.DistantBuildingNode(kind,complete,identity);_buildings.AddChild(node);
                var surface=node.GetChild<MeshInstance3D>(0);
                visual=new BuildingVisual {Root=node,Surface=surface,Far=surface.Mesh,Generation=generation,Identity=identity,Kind=kind,Complete=complete};
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
            if(kind is BuildingKind.House or BuildingKind.Storage)
            {
                // The simulation stores one occupied cell per building. Fit the complete
                // silhouette to nearby occupied cells, including roofs and domestic props.
                // This also handles dense legacy saves without moving simulation entities.
                var footprint=NatureModels.BuildingFootprint(identity);
                float scale=.96f+(identity%140%7)*.008f;
                float span=MathF.Max(footprint.X,footprint.Y)+1.5f;
                for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++)
                {
                    if((dx==0&&dy==0)||!sim.World.IsInBounds(x+dx,y+dy))continue;
                    if(sim.World.TileAt(x+dx,y+dy).BuildingId==0)continue;
                    scale=MathF.Min(scale,(Math.Max(Math.Abs(dx),Math.Abs(dy))*2-.15f)/span);
                }
                visual.Root.Scale=Vector3.One*scale;
            }
            UpdateFoundation(visual);
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
        UpdateBuildingDetail();return remaining;
    }
    private void UpdateFoundation(BuildingVisual visual)
    {
        if(visual.Kind is not (BuildingKind.House or BuildingKind.Storage))return;
        uint identity=visual.Identity%140;
        var footprint=NatureModels.BuildingFootprint(identity);
        float halfWidth=(footprint.X+.1f)*.5f,halfDepth=(footprint.Y+.1f)*.5f;
        var corners=new[]{new Vector3(-halfWidth,0,-halfDepth),new(halfWidth,0,-halfDepth),
            new Vector3(halfWidth,0,halfDepth),new(-halfWidth,0,halfDepth)};
        var depths=Vector4.Zero;
        for(int i=0;i<4;i++)
        {
            var world=visual.Root.Transform*corners[i];
            depths[i]=MathF.Min(-.08f,(GroundHeight(world.X,world.Z)-visual.Root.Position.Y)/visual.Root.Scale.Y-.035f);
        }
        if(visual.Foundation!=null&&visual.FoundationDepths==depths)return;
        visual.FoundationDepths=depths;
        if(visual.Foundation==null)
        {
            visual.Foundation=new MeshInstance3D {Name="TerrainFittedFoundation",MaterialOverride=_models.Material(6)};
            visual.Root.AddChild(visual.Foundation);
        }
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int side=0;side<4;side++)
        {
            int next=(side+1)%4;var a=corners[side]+Vector3.Up*.035f;var b=corners[next]+Vector3.Up*.035f;
            var c=corners[side]+Vector3.Up*depths[side];var d=corners[next]+Vector3.Up*depths[next];
            var normal=(a+b)*.5f;normal.Y=0;normal=normal.Normalized();
            void Triangle(Vector3 x,Vector3 y,Vector3 z)
            {
                if((y-x).Cross(z-x).Dot(normal)>0)(y,z)=(z,y);
                foreach(var point in new[]{x,y,z}){tool.SetNormal(normal);tool.SetUV(new(point.X+point.Z,point.Y));tool.AddVertex(point);}
            }
            Triangle(a,b,c);Triangle(b,d,c);
        }
        tool.Index();visual.Foundation.Mesh=tool.Commit();
    }
    public void ValidateIncrementalBuildings()
    {
        var sim=Game.Sim;var world=sim.World;
        for(int y=4;y<=12;y++)for(int x=4;x<=12;x++)world.SetTerrain(x,y,TerrainKind.Grass);
        int first=sim.Buildings.Place(world,BuildingKind.House,6,6,10),second=sim.Buildings.Place(world,BuildingKind.House,10,10,10);
        if(first<0||second<0)throw new Exception("Building cache fixture failed");
        CheckTerrain();BuildBuildings();var retained=_buildingVisuals[first].Root;var changed=_buildingVisuals[second].Root;
        var foundation=_buildingVisuals[first].Foundation!.Mesh;
        string before=StateHash.ComputeDigest(sim);BuildBuildings();
        if(before!=StateHash.ComputeDigest(sim)||_buildingVisuals[first].Root!=retained||_buildingVisuals[first].Foundation!.Mesh!=foundation)throw new Exception("Unchanged building regenerated or observation mutated world");
        world.SetTerrain(7,6,TerrainKind.Road);CheckTerrain();BuildBuildings();
        if(_buildingVisuals[first].Root!=retained||Math.Abs(retained.RotationDegrees.Y-90)>.01f)throw new Exception("Road-facing update regenerated geometry or did not rotate");
        world.Tiles[6*world.Width+6].Height+=.2f;CheckTerrain();BuildBuildings();
        if(_buildingVisuals[first].Root!=retained||retained.Position.DistanceTo(PositionAt(6,6))>.001f)throw new Exception("Ground height update lost building identity");
        var skirt=_buildingVisuals[first].Foundation!.Mesh.SurfaceGetArrays(0);
        foreach(var point in skirt[(int)Mesh.ArrayType.Vertex].AsVector3Array())if(point.Y<-.035f)
        {
            var fitted=retained.Transform*point;
            if(!fitted.IsFinite()||fitted.Y>GroundHeight(fitted.X,fitted.Z)+.005f)throw new Exception("Building foundation floats above hillside");
        }
        GD.Print("BUILDING_FOUNDATION_PASS: terrain rebuild, road-facing rotation, cached skirt and actual hillside contact");
        sim.Buildings.MarkComplete(second,sim.Clock);BuildBuildings();
        if(_buildingVisuals[first].Root!=retained||_buildingVisuals[second].Root==changed)throw new Exception("Completion replaced unrelated building");
        sim.Buildings.Demolish(world,second);BuildBuildings();
        if(_buildingVisuals.ContainsKey(second)||_buildingVisuals[first].Root!=retained)throw new Exception("Demolition leaves stale geometry");
        int reused=sim.Buildings.Place(world,BuildingKind.Storage,10,10,10);BuildBuildings();
        if(reused<0||_buildingVisuals[reused].Kind!=BuildingKind.Storage||_buildingVisuals[reused].Generation!=sim.Buildings.GenerationOf(reused))throw new Exception("Reused building slot shows old geometry");
        int queuedA=sim.Buildings.Place(world,BuildingKind.House,8,8,10),queuedB=sim.Buildings.Place(world,BuildingKind.Storage,12,12,10);
        if(queuedA<0||queuedB<0)throw new Exception("Deferred building fixture failed");
        int priorCount=_buildingVisuals.Count;
        if(!BuildBuildings(1)||_buildingVisuals.Count!=priorCount+1)throw new Exception("Per-frame building creation limit failed");
        sim.Buildings.Demolish(world,queuedB);BuildBuildings();
        if(_buildingVisuals.ContainsKey(queuedB)||!_buildingVisuals.ContainsKey(queuedA))throw new Exception("Queued building was resurrected after demolition");
        GD.Print("BUILDING_CACHE_PASS: reuse, road/height updates, completion, demolition and slot generations");
        world.SetTerrain(7,6,TerrainKind.Grass);
        sim.Buildings.MarkComplete(first,sim.Clock);
        int neighbour=sim.Buildings.Place(world,BuildingKind.House,7,6,10);
        if(neighbour<0)throw new Exception("Dense building fixture failed");
        sim.Buildings.MarkComplete(neighbour,sim.Clock);BuildBuildings();
        var left=_buildingVisuals[first];var right=_buildingVisuals[neighbour];
        var leftBounds=left.Root.Transform*left.Surface.Mesh.GetAabb();
        var rightBounds=right.Root.Transform*right.Surface.Mesh.GetAabb();
        if(leftBounds.Intersects(rightBounds))throw new Exception("Dense building roofs intersect");
        float crowdedScale=left.Root.Scale.X;
        sim.Buildings.Demolish(world,neighbour);BuildBuildings();
        if(left.Root.Scale.X<=crowdedScale)throw new Exception("Building plot did not expand after neighbour demolition");
        GD.Print("BUILDING_PLOT_PASS: adjacent mesh bounds remain separate and demolition refreshes available space");
    }
}
