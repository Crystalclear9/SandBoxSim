using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private readonly List<(MultiMeshInstance3D Batch,List<Transform3D> Roots)> _meadowBatches=new();
    private readonly Dictionary<MultiMeshInstance3D,Transform3D[]> _meadowSeeds=new();
    private Vector3[]? _meadowRootProbes;
    private readonly Dictionary<(Vector3 Point,Vector3 Scale,float Yaw),(int Revision,bool Valid,Transform3D Placement)> _meadowPlacements=new();
    private int[] _meadowGroundRevisions=Array.Empty<int>();
    private long _meadowPlacementEvaluations;
    private void ResetMeadowPlacements()
    {
        _meadowPlacements.Clear();_meadowGroundRevisions=new int[Game.Sim.World.Tiles.Length];
    }
    private void InvalidateMeadowPlacements(HashSet<int> changedTiles)
    {
        var world=Game.Sim.World;
        foreach(int index in changedTiles)
        {
            int x=index%world.Width,y=index/world.Width;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                if(world.IsInBounds(x+dx,y+dy))_meadowGroundRevisions[(y+dy)*world.Width+x+dx]++;
        }
    }
    private static uint MeadowSample(uint hash,int candidate)
    {
        uint sample=hash^unchecked((uint)(candidate+1)*2654435761u);
        sample^=sample>>16;sample=unchecked(sample*2146121005u);return sample^(sample>>15);
    }
    private int MeadowCandidateMask(Tile tile,int x,int y,uint hash)
    {
        if(!HasMeadow(tile,hash))return 0;
        float patch=TerrainNoise(x*.12f,y*.12f,Game.Sim.World.Seed);
        float threshold=.26f+patch*.48f+tile.Vegetation*.10f;
        int mask=0;
        for(int candidate=0;candidate<2;candidate++)
            if((MeadowSample(hash,candidate)&255)/255f<=threshold)mask|=1<<candidate;
        return mask;
    }
    private Vector3[] MeadowRootProbes()
    {
        if(_meadowRootProbes!=null)return _meadowRootProbes;
        // Read the actual root vertices once, not just an assumed clump centre.
        var data=_models.MeadowTurf().SurfaceGetArrays(0);
        var unique=new HashSet<Vector3>();
        foreach(var point in data[(int)Mesh.ArrayType.Vertex].AsVector3Array())if(point.Y<.000001f)unique.Add(point);
        _meadowRootProbes=new Vector3[unique.Count];unique.CopyTo(_meadowRootProbes);return _meadowRootProbes;
    }
    private bool TryMeadowPlacement(Vector3 point,Vector3 scale,float yaw,out Transform3D placement)
    {
        var world=Game.Sim.World;
        int index=Math.Clamp((int)MathF.Floor(point.Z*.5f),0,world.Height-1)*world.Width+Math.Clamp((int)MathF.Floor(point.X*.5f),0,world.Width-1);
        int revision=_meadowGroundRevisions[index];var key=(point,scale,yaw);
        if(_meadowPlacements.TryGetValue(key,out var cached)&&cached.Revision==revision)
        {placement=cached.Placement;return cached.Valid;}
        _meadowPlacementEvaluations++;
        placement=Transform3D.Identity;
        if(GroundNormal(point).Y<MathF.Cos(.35f)||GroundHeight(point.X,point.Z)<.03f)
        {_meadowPlacements[key]=(revision,false,placement);return false;}
        var rooted=RootedPlant(point,scale,yaw);
        // The terrain can curve within the footprint even on a gentle slope.
        // Reject that patch rather than leaving peripheral roots suspended.
        bool valid=MeadowRootsFit(rooted);
        if(valid)placement=rooted;
        _meadowPlacements[key]=(revision,valid,placement);return valid;
    }
    private bool MeadowRootsFit(Transform3D rooted)
    {
        foreach(var probe in MeadowRootProbes())
        {
            var root=rooted*probe;float gap=root.Y-GroundHeight(root.X,root.Z);
            if(gap>.012f||gap<-.035f)return false;
        }
        return true;
    }
    private void SyncMeadowRoots(HashSet<int> changedTiles)
    {
        if(changedTiles.Count==0)return;
        var world=Game.Sim.World;
        foreach(var entry in _meadowBatches)
        {
            if(!_meadowSeeds.TryGetValue(entry.Batch,out var seeds))_meadowSeeds[entry.Batch]=seeds=entry.Roots.ToArray();
            for(int i=0;i<seeds.Length;i++)
            {
                var seed=seeds[i];int x=(int)MathF.Floor(seed.Origin.X*.5f),y=(int)MathF.Floor(seed.Origin.Z*.5f);
                bool affected=false;
                for(int dy=-1;dy<=1&&!affected;dy++)for(int dx=-1;dx<=1;dx++)
                    if(world.IsInBounds(x+dx,y+dy)&&changedTiles.Contains((y+dy)*world.Width+x+dx)){affected=true;break;}
                if(!affected)continue;
                var normal=GroundNormal(seed.Origin);float height=GroundHeight(seed.Origin.X,seed.Origin.Z);
                var rotation=new Basis(new Quaternion(seed.Basis.Y.Normalized(),normal));
                var next=new Transform3D(rotation*seed.Basis,new(seed.Origin.X,height-.012f,seed.Origin.Z));
                // An unsuitable footprint is hidden and can reappear when soil
                // recovers. Keep the original seed so repeated updates cannot
                // accumulate rotation drift or lose the hidden clump's shape.
                if(normal.Y<MathF.Cos(.35f)||height<.03f||!MeadowRootsFit(next))next.Basis=Basis.FromScale(Vector3.Zero);
                if(next==entry.Roots[i])continue;
                entry.Roots[i]=next;entry.Batch.Multimesh.SetInstanceTransform(i,next);
            }
        }
    }
    private void ValidateMeadowPresentation()
    {
        if(_meadowPlacements.Count>0)
        {
            (Vector3 Point,Vector3 Scale,float Yaw) key=default;
            foreach(var candidate in _meadowPlacements.Keys){key=candidate;break;}
            bool before=TryMeadowPlacement(key.Point,key.Scale,key.Yaw,out var originalPlacement);
            long evaluations=_meadowPlacementEvaluations;
            if(TryMeadowPlacement(key.Point,key.Scale,key.Yaw,out var reused)!=before||reused!=originalPlacement||_meadowPlacementEvaluations!=evaluations)
                throw new InvalidOperationException("Unchanged meadow placement is recalculated or changed");
            int tile=(int)MathF.Floor(key.Point.Z*.5f)*Game.Sim.World.Width+(int)MathF.Floor(key.Point.X*.5f);
            InvalidateMeadowPlacements(new HashSet<int>{tile});
            if(TryMeadowPlacement(key.Point,key.Scale,key.Yaw,out var rebuilt)!=before||rebuilt!=originalPlacement||_meadowPlacementEvaluations!=evaluations+1)
                throw new InvalidOperationException("Meadow support invalidation reuses stale geometry");
            GD.Print("MEADOW_PLACEMENT_CACHE_PASS: unchanged accepted/rejected placement reuse, neighbourhood invalidation and identical rebuilt support");
        }
        int roots=0;
        foreach(var entry in _meadowBatches)
        {
            if(entry.Batch.Multimesh.Mesh!=_models.MeadowTurf()||entry.Batch.VisibilityRangeEnd!=34||entry.Batch.Multimesh.InstanceCount!=entry.Roots.Count)
                throw new InvalidOperationException("Meadow batch loses geometry, roots or distance budget");
            foreach(var placement in entry.Roots)
            {
                if(placement.Basis.Y.LengthSquared()<.000000000001f)continue;
                if(!placement.IsFinite()||placement.Basis.Y.Normalized().Dot(GroundNormal(placement.Origin))<.9999f)
                    throw new InvalidOperationException("Meadow root plane cannot follow its slope");
                foreach(var probe in MeadowRootProbes())
                {
                    var root=placement*probe;float gap=root.Y-GroundHeight(root.X,root.Z);
                    if(gap>.0121f||gap<-.0351f)throw new InvalidOperationException("Meadow perimeter root floats or is deeply buried");
                    roots++;
                }
            }
        }
        ValidateMeadowWear();
        var world=Game.Sim.World;int selected=-1;float lower=0,upper=0;
        for(int index=0;index<world.Tiles.Length&&selected<0;index++)
        {
            var tile=world.Tiles[index];if(tile.Terrain!=TerrainKind.Grass||tile.BuildingId!=0||tile.Fire==FireState.Burnt)continue;
            int x=index%world.Width,y=index/world.Width;uint hash=unchecked((uint)(x*73856093^y*19349663));
            float patch=TerrainNoise(x*.12f,y*.12f,world.Seed);
            for(int candidate=0;candidate<2;candidate++)
            {
                float boundary=((MeadowSample(hash,candidate)&255)/255f-.26f-patch*.48f)/.10f;
                if(boundary>.26f&&boundary<.89f){selected=index;lower=boundary-.01f;upper=boundary+.01f;break;}
            }
        }
        if(selected<0)throw new InvalidOperationException("No interior meadow threshold is available for refresh validation");
        var original=world.Tiles[selected];string digest=StateHash.ComputeDigest(Game.Sim);
        bool terrainPending=_pendingTerrain,naturePending=_pendingNature,buildingsPending=_pendingBuildings;
        var mesh=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh;
        try
        {
            world.Tiles[selected].Vegetation=lower;CheckTerrain(true);long below=_natureSignature;
            _pendingNature=false;world.Tiles[selected].Vegetation=upper;CheckTerrain(true);
            if(!_pendingNature||_natureSignature==below)throw new InvalidOperationException("Meadow density changes above 0.2 do not invalidate their candidate mask");
            long above=_natureSignature;_pendingNature=false;
            world.Tiles[selected].Vegetation=upper+.000001f;CheckTerrain(true);
            if(_pendingNature||_natureSignature!=above)throw new InvalidOperationException("Sub-threshold vegetation changes unnecessarily rebuild meadow batches");
            if(_terrainRoot.GetChild<MeshInstance3D>(0).Mesh!=mesh)throw new InvalidOperationException("Meadow density refresh rebuilt physical terrain");
        }
        finally
        {
            world.Tiles[selected]=original;CheckTerrain(true);
            _pendingTerrain=terrainPending;_pendingNature=naturePending;_pendingBuildings=buildingsPending;
        }
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Meadow validation changed simulation state");
        GD.Print($"MEADOW_PRESENTATION_PASS: {roots} actual perimeter roots, bounded slope/contact, interior density mask invalidation and stable sub-threshold updates");
    }
    private void ValidateMeadowWear()
    {
        foreach(var entry in _meadowBatches)
        for(int i=0;i<entry.Roots.Count;i++)
        {
            var before=entry.Roots[i];if(before.Basis.Y.LengthSquared()<.000000000001f)continue;
            var world=Game.Sim.World;int x=(int)MathF.Floor(before.Origin.X*.5f),y=(int)MathF.Floor(before.Origin.Z*.5f),index=y*world.Width+x;
            var original=world.Tiles[index];string digest=StateHash.ComputeDigest(Game.Sim);
            var geometry=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh;
            try
            {
                float wear=original.FootTraffic<.5f?.98f:.02f;
                world.Tiles[index].Height-=.006f*(wear-original.FootTraffic);world.Tiles[index].FootTraffic=wear;CheckTerrain();
                if(_terrainRoot.GetChild<MeshInstance3D>(0).Mesh!=geometry)throw new InvalidOperationException("Meadow compaction rebuilt physical geometry");
                var after=entry.Roots[i];
                if(after.Origin.DistanceTo(before.Origin)<.001f)throw new InvalidOperationException("Actual meadow roots do not follow compacted ground");
                if(after.Basis.Y.LengthSquared()>.000000000001f&&(!MeadowRootsFit(after)||after.Basis.Y.Normalized().Dot(GroundNormal(after.Origin))<.9999f))
                    throw new InvalidOperationException("Compacted meadow roots float or retain a stale slope");
                if(DisplayServer.GetName()!="headless"&&entry.Batch.Multimesh.GetInstanceTransform(i).Origin.DistanceTo(after.Origin)>.0001f)
                    throw new InvalidOperationException("Compacted meadow GPU placement differs from its CPU root");
            }
            finally{world.Tiles[index]=original;CheckTerrain();}
            if(entry.Roots[i].Origin.DistanceTo(before.Origin)>.0001f||StateHash.ComputeDigest(Game.Sim)!=digest)
                throw new InvalidOperationException("Meadow compaction validation does not restore roots and world");
            GD.Print("MEADOW_WEAR_PASS: actual batch roots follow byte-precision compaction without terrain rebuild; contact/slope, render placement and recovery");
            return;
        }
        throw new InvalidOperationException("No actual meadow instance is available for compaction validation");
    }
}
