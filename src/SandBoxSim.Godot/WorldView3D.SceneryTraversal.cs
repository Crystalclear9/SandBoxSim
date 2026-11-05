using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private const float SceneryClearance=.28f;
    private const float CornerOffset=.94f;
    private const float MaximumSolidRadius=.63f;
    private readonly Dictionary<Vector2I,(Vector2 Center,float Radius)> _scenerySolids=new();
    private int _sceneryPlacementRevision;

    private bool HasSceneTree(Tile tile,int x,int y,uint hash)
    {
        if(tile.Terrain!=TerrainKind.Forest||tile.Vegetation<=.12f||tile.BuildingId!=0||tile.Fire==FireState.Burnt)
            return false;
        float grove=TerrainNoise(x*.085f,y*.085f,Game.Sim.World.Seed);
        return (MeadowSample(hash,29)&1023)/1024f<.08f+grove*.24f;
    }

    private bool NaturalSceneryGround(int x,int y)
    {
        var world=Game.Sim.World;
        if(!world.IsInBounds(x,y))return false;
        var tile=world.TileAt(x,y);
        return tile.Walkable&&tile.BuildingId==0&&tile.Fire!=FireState.Burnt
            &&tile.Terrain is not (TerrainKind.Road or TerrainKind.Farmland or TerrainKind.Water or TerrainKind.Lava);
    }

    private bool TrySolidSite(int x,int y,uint hash,float radius,out Vector3 point)
    {
        point=default;
        if(radius>MaximumSolidRadius)return false;
        for(int i=0;i<4;i++)
        {
            int side=(i+(int)(hash%4))%4;
            int dx=(side&1)==0?-1:1,dy=(side&2)==0?-1:1;
            var corner=new Vector2I(x+(dx>0?1:0),y+(dy>0?1:0));
            if(_scenerySolids.ContainsKey(corner)||!NaturalSceneryGround(x,y)
                ||!NaturalSceneryGround(x+dx,y)||!NaturalSceneryGround(x,y+dy)||!NaturalSceneryGround(x+dx,y+dy))continue;
            float px=x*2+1+dx*CornerOffset,pz=y*2+1+dy*CornerOffset;
            var candidate=new Vector3(px,GroundHeight(px,pz),pz);
            if(candidate.Y<=.045f)continue;
            _scenerySolids.Add(corner,(new Vector2(px,pz),radius));
            point=candidate;
            return true;
        }
        return false;
    }

    private void BuildSolidScenery(List<Transform3D> leaves,List<Transform3D> pines,List<Transform3D> rocks,
        List<Transform3D> ores,List<Transform3D> scree)
    {
        _scenerySolids.Clear();
        _sceneryPlacementRevision++;
        var world=Game.Sim.World;
        for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)
        {
            var tile=world.TileAt(x,y);
            uint hash=unchecked((uint)(x*73856093^y*19349663));
            if(HasSceneTree(tile,x,y,hash))
            {
                bool fir=hash%3==0;
                float scale=.76f+(hash%17)*.035f;
                float radius=_models.SolidSceneryRadius(fir?"conifer-tree":"broad-tree")*scale;
                if(!TrySolidSite(x,y,hash,radius,out var stem))continue;
                var tree=NatureModels.Transform(stem-Vector3.Up*.045f,Vector3.One*scale,new Vector3(0,hash%17,0));
                (fir?pines:leaves).Add(tree);
                RegisterSceneResource(x,y,ResourceKind.Wood,tree.Origin+Vector3.Up*(.85f*scale));
            }
            else if(tile.Terrain==TerrainKind.Mountain&&hash%9==0)
            {
                float radius=.51f+(hash%7)*.02f;
                if(!TrySolidSite(x,y,hash,radius,out var point))continue;
                float scale=radius/_models.SolidSceneryRadius("boulder");
                var rock=GroundedStone("boulder",point,new Vector3(.82f+(hash%3)*.09f,.79f+(hash%5)*.07f,1)*scale,
                    new Vector3(0,hash%31,0),.045f);
                (tile.Resource.Kind==ResourceKind.Iron?ores:rocks).Add(rock);
                RegisterSceneResource(x,y,tile.Resource.Kind,rock.Origin+Vector3.Up*(.49f*rock.Basis.Y.Length()));
                AddScreeFan(point,hash,scree);
            }
        }
    }

    private void AddScreeFan(Vector3 point,uint hash,List<Transform3D> scree)
    {
        var normal=GroundNormal(point);
        var downhill=new Vector3(normal.X,0,normal.Z);
        if(downhill.LengthSquared()<.0025f)downhill=new(MathF.Cos(hash%31),0,MathF.Sin(hash%31));
        downhill=downhill.Normalized();
        var sideways=new Vector3(-downhill.Z,0,downhill.X);
        for(int i=0;i<6;i++)
        {
            uint sample=MeadowSample(hash,i+11);
            float reach=.48f+i*.14f;
            float lateral=(((sample>>8)&255)/255f-.5f)*reach*1.25f;
            var at=point+downhill*reach+sideways*lateral;
            if(!NaturalSceneryGround((int)MathF.Floor(at.X*.5f),(int)MathF.Floor(at.Z*.5f))
                ||GroundHeight(at.X,at.Z)<=.035f)continue;
            float size=(.40f-i*.043f)*(.81f+((sample>>16)&255)/850f);
            var scale=new Vector3(size,size*.46f,size*1.5f);
            scree.Add(GroundedStone("pebble",at,scale,new Vector3(.08f,MathF.Atan2(downhill.X,downhill.Z)+lateral,.12f),.030f));
        }
    }

    private static Vector2 Horizontal(Vector3 point)=>new(point.X,point.Z);

    private bool SceneryPointClear(Vector3 point,float margin)
    {
        var p=Horizontal(point);
        var corner=new Vector2I((int)MathF.Round(p.X*.5f),(int)MathF.Round(p.Y*.5f));
        if(!_scenerySolids.TryGetValue(corner,out var obstacle))return true;
        float radius=obstacle.Radius+margin;
        return p.DistanceSquaredTo(obstacle.Center)>=radius*radius;
    }

    private bool ScenerySegmentClear(Vector3 from,Vector3 to)
    {
        var a=Horizontal(from);
        var b=Horizontal(to);
        var segment=b-a;
        float lengthSquared=segment.LengthSquared();
        // Each footprint, its clearance and its 6 cm offset fit within 1 m of its grid corner.
        int minX=(int)MathF.Ceiling((MathF.Min(a.X,b.X)-1)*.5f),maxX=(int)MathF.Floor((MathF.Max(a.X,b.X)+1)*.5f);
        int minY=(int)MathF.Ceiling((MathF.Min(a.Y,b.Y)-1)*.5f),maxY=(int)MathF.Floor((MathF.Max(a.Y,b.Y)+1)*.5f);
        for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
        {
            if(!_scenerySolids.TryGetValue(new Vector2I(x,y),out var obstacle))continue;
            float t=lengthSquared>0?Math.Clamp((obstacle.Center-a).Dot(segment)/lengthSquared,0,1):0;
            float radius=obstacle.Radius+SceneryClearance;
            if((a+segment*t).DistanceSquaredTo(obstacle.Center)<radius*radius)return false;
        }
        return true;
    }

    private bool SceneryLegSupported(Vector3 from,Vector3 to)
    {
        int count=Math.Max(1,(int)MathF.Ceiling(Horizontal(from-to).Length()/.20f));
        for(int i=0;i<=count;i++)
        {
            var sample=from.Lerp(to,i/(float)count);
            if(!DryActorTarget(sample)||(ActorNearShore(sample)&&!ActorFootSupport(sample)))return false;
        }
        return true;
    }

    private Vector3 SceneryWorkPosition(int x,int y,Vector3 logical,ActionKind action)
    {
        var focus=SceneResourceFocus(x,y,action);
        if(focus==logical)return logical;
        var p=Horizontal(focus);
        var corner=new Vector2I((int)MathF.Round(p.X*.5f),(int)MathF.Round(p.Y*.5f));
        if(!_scenerySolids.TryGetValue(corner,out var solid))return logical;
        var direction=Horizontal(focus-logical);
        float distance=direction.Length();
        float travel=Math.Clamp(distance-solid.Radius-.34f,0,.72f);
        if(travel<=0)return logical;
        var offset=direction*(travel/distance);
        var stand=new Vector3(logical.X+offset.X,0,logical.Z+offset.Y);
        if(!ScenerySegmentClear(logical,stand)||!SceneryLegSupported(logical,stand))return logical;
        stand.Y=GroundHeight(stand.X,stand.Z);
        return stand;
    }

    private Vector3 SceneryActorPosition(Vector3 current,Vector3 target,float delta,float response,bool paused,float maximumSpeed=float.PositiveInfinity)
    {
        if(paused)return current;
        float distance=Horizontal(target-current).LengthSquared();
        if(distance>MaximumAdjacentPresentationStep*MaximumAdjacentPresentationStep||distance<.000001f
            ||!DryActorTarget(target)||ScenerySegmentClear(current,target))
            return GroundedActorPosition(current,target,delta,response,false,maximumSpeed);

        int x=(int)MathF.Floor(current.X*.5f),y=(int)MathF.Floor(current.Z*.5f);
        int tx=(int)MathF.Floor(target.X*.5f),ty=(int)MathF.Floor(target.Z*.5f);
        var world=Game.Sim.World;
        Span<Vector2I> candidates=stackalloc Vector2I[]{new(tx,y),new(x,ty),new(x,y),new(tx,ty)};
        Vector3 waypoint=target;
        float best=float.PositiveInfinity;
        foreach(var tile in candidates)
        {
            if(!world.IsInBounds(tile.X,tile.Y))continue;
            var via=PositionAt(tile.X,tile.Y);
            float cost=Horizontal(via-current).Length()+Horizontal(target-via).Length();
            if(cost>=best||!ScenerySegmentClear(current,via)||!ScenerySegmentClear(via,target)
                ||!SceneryLegSupported(current,via)||!SceneryLegSupported(via,target))continue;
            waypoint=via;
            best=cost;
        }
        // Reuse the existing resynchronisation policy when no supported local leg exists.
        // Never interpolate through a solid merely to catch up to the simulator.
        return float.IsFinite(best)?GroundedActorPosition(current,waypoint,delta,response,false,maximumSpeed):target;
    }

    private void ValidateSceneryTraversal()
    {
        string digest=StateHash.ComputeDigest(Game.Sim);
        int corridors=0,detours=0;
        foreach(var (corner,solid) in _scenerySolids)
        {
            if(solid.Radius+SceneryClearance>=CornerOffset)throw new InvalidOperationException("Scenery occupies the cardinal clearance corridor");
            var nw=PositionAt(corner.X-1,corner.Y-1);
            var ne=PositionAt(corner.X,corner.Y-1);
            var sw=PositionAt(corner.X-1,corner.Y);
            var se=PositionAt(corner.X,corner.Y);
            if(!ScenerySegmentClear(nw,ne)||!ScenerySegmentClear(nw,sw)||!ScenerySegmentClear(ne,se)||!ScenerySegmentClear(sw,se))
                throw new InvalidOperationException("A real scenery footprint obstructs a cardinal step");
            corridors+=4;
            foreach(var (start,goal,response) in new[]{(nw,se,10f),(ne,sw,8f),(se,nw,8f),(sw,ne,10f)})
            {
                if(ScenerySegmentClear(start,goal))continue;
                var current=start;
                for(int frame=0;frame<180&&!SettledAt(current,goal);frame++)
                {
                    var next=SceneryActorPosition(current,goal,1f/60,response,false);
                    if(!ScenerySegmentClear(current,next)||!SceneryLegSupported(current,next)
                        ||Horizontal(next-current).Length()>.45f||MathF.Abs(next.Y-GroundHeight(next.X,next.Z))>.0001f)
                        throw new InvalidOperationException($"Scenery detour loses support or clearance: {corner}, {current} -> {next}");
                    current=next;
                }
                if(!SettledAt(current,goal)||SceneryActorPosition(start,goal,1,response,true)!=start)
                    throw new InvalidOperationException("Scenery detour cannot settle or changes a paused pose");
                detours++;
            }
        }
        if(corridors==0||detours==0||StateHash.ComputeDigest(Game.Sim)!=digest)
            throw new InvalidOperationException("Scenery traversal validation lacks coverage or changes simulation");
        GD.Print($"SCENERY_TRAVERSAL_PASS: {_scenerySolids.Count} real props, {corridors} cardinal corridors, {detours} continuous resident/animal detours, unchanged simulation");
    }
}
