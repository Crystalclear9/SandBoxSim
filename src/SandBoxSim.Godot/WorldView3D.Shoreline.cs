using System;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private float ShoreSupportedHeight(float x,float y,float sculpted)
    {
        var world=Game.Sim.World;
        int tx=Math.Clamp((int)MathF.Floor(x),0,world.Width-1),ty=Math.Clamp((int)MathF.Floor(y),0,world.Height-1);
        bool water=world.TerrainAt(tx,ty)==TerrainKind.Water;
        float nearest=float.PositiveInfinity;
        for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
        {
            int sx=tx+dx,sy=ty+dy;
            if(!world.IsInBounds(sx,sy)||(world.TerrainAt(sx,sy)==TerrainKind.Water)==water)continue;
            float a=MathF.Max(0,MathF.Max(sx-x,x-sx-1)),b=MathF.Max(0,MathF.Max(sy-y,y-sy-1));
            nearest=MathF.Min(nearest,MathF.Sqrt(a*a+b*b));
        }
        // Shared boundary vertices sit above the 2 cm water plane. Only the
        // logical water side descends to the bed. Dry tile centres, their foot
        // support and centre-to-centre walk corridors remain actual dry mesh.
        // The 8 cm rim retains 5.6 cm even after maximum 2.4 cm compaction.
        if(!float.IsFinite(nearest))return water?-.25f:MathF.Max(.08f,sculpted);
        float transition=Mathf.SmoothStep(0,.55f,nearest);
        return water?Mathf.Lerp(.08f,-.25f,transition):Mathf.Lerp(.08f,MathF.Max(.08f,sculpted),transition);
    }
    private void ValidateLogicalShoreline()
    {
        var world=Game.Sim.World;string digest=StateHash.ComputeDigest(Game.Sim);
        int dry=0,paths=0,wet=0;
        for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)
        {
            var terrain=world.TerrainAt(x,y);
            var center=new Vector3(x*2+1,0,y*2+1);
            if(terrain==TerrainKind.Water)
            {
                if(GroundHeight(center.X,center.Z)>=.019f)throw new InvalidOperationException("Logical water centre has no visible physical water depth");
                wet++;continue;
            }
            if(!TerrainInfo.IsWalkable(terrain))continue;
            foreach(var offset in new[]{Vector2.Zero,new Vector2(-.28f,0),new(.28f,0),new(0,-.28f),new(0,.28f)})
                if(GroundHeight(center.X+offset.X,center.Z+offset.Y)<=.021f)throw new InvalidOperationException($"Walkable dry tile/feet are submerged at {x},{y}");
            dry++;
            foreach(var direction in new[]{Vector2I.Right,Vector2I.Down})
            {
                int nx=x+direction.X,ny=y+direction.Y;
                if(!world.IsInBounds(nx,ny)||!TerrainInfo.IsWalkable(world.TerrainAt(nx,ny)))continue;
                for(int step=1;step<4;step++)
                {
                    var p=center+new Vector3(direction.X,0,direction.Y)*(step*.5f);
                    if(GroundHeight(p.X,p.Z)<=.021f)throw new InvalidOperationException("A dry centre-to-centre walk corridor crosses rendered water");
                    paths++;
                }
            }
        }
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Shoreline presentation validation changes simulation state");
        ValidateHabitatCoverage();
        GD.Print($"LOGICAL_SHORELINE_PASS: {dry} dry centres with foot support, {paths} dry path samples, {wet} water centres; shared terrain/picking height and unchanged simulation");
    }
    private void ValidateHabitatCoverage()
    {
        var world=Game.Sim.World;int index=world.Tiles.Length/2;var original=world.Tiles[index];
        string digest=StateHash.ComputeDigest(Game.Sim);var mesh=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh;
        bool terrainPending=_pendingTerrain,naturePending=_pendingNature,buildingsPending=_pendingBuildings;
        void CheckUploadedCoverage(float coverage)
        {
            if(_groundTintImage==null||_groundTintImage.GetFormat()!=Image.Format.Rgba8||MathF.Abs(_groundTintImage.GetPixel(index%world.Width,index/world.Width).A-coverage)>1f/255)
                throw new InvalidOperationException("Actual uploaded CPU ground coverage image stays stale");
            if(DisplayServer.GetName()!="headless")
            {
                using var rendered=_groundTintTexture!.GetImage();
                if(rendered==null||MathF.Abs(rendered.GetPixel(index%world.Width,index/world.Width).A-coverage)>1f/255)
                    throw new InvalidOperationException("Rendered ground coverage differs from its actual CPU upload");
            }
        }
        try
        {
            long beforeUpload=_groundTintUploadRevision;
            float coverage=original.Vegetation<.5f?.81f:.23f;world.Tiles[index].Vegetation=coverage;CheckTerrain(true);
            if(_groundTintUploadRevision<=beforeUpload)throw new InvalidOperationException("Vegetation changes do not execute a ground coverage texture upload");
            CheckUploadedCoverage(coverage);
            if(_terrainRoot.GetChild<MeshInstance3D>(0).Mesh!=mesh)throw new InvalidOperationException("Ground coverage shading rebuilds physical terrain");
        }
        finally
        {
            world.Tiles[index]=original;CheckTerrain(true);
            _pendingTerrain=terrainPending;_pendingNature=naturePending;_pendingBuildings=buildingsPending;
        }
        CheckUploadedCoverage(original.Vegetation);
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Habitat shading validation changes simulation state");
        GD.Print("HABITAT_COVERAGE_PASS: actual CPU coverage image and upload revision; GPU readback on a graphics display, no extra sampler or terrain rebuild, restored coverage/world");
    }
}
