using System;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private static readonly float[][] ShoreKernel=CreateShoreKernel();
    private static float[][] CreateShoreKernel()
    {
        // Compact C2 radial weights retain the dry/wet centre classification.
        // The 1.4-tile radius rounds the stair-step outline across corners;
        // centre heights are blended, not exact interpolation of old heights.
        // Cache all fine-grid phases: no per-vertex square roots or allocations.
        var phases=new float[GroundDetail*GroundDetail][];
        for(int py=0;py<GroundDetail;py++)for(int px=0;px<GroundDetail;px++)
        {
            var weights=new float[9];float sum=0;int index=0;
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
            {
                float dx=x+.5f-px/(float)GroundDetail,dy=y+.5f-py/(float)GroundDetail;
                float r=MathF.Sqrt(dx*dx+dy*dy)/1.40f,t=MathF.Max(0,1-r);
                float weight=t*t*t*t*(4*r+1);weights[index++]=weight;sum+=weight;
            }
            if(sum<=0)throw new InvalidOperationException("Shore kernel leaves an uncovered point");
            for(int i=0;i<weights.Length;i++)weights[i]/=sum;
            phases[py*GroundDetail+px]=weights;
        }
        return phases;
    }
    private static int[,] ShoreNeighbourMasks(bool[] water,int width,int height,bool outsideWet=false)
    {
        var masks=new int[width+1,height+1];
        for(int y=0;y<=height;y++)for(int x=0;x<=width;x++)
        {
            int mask=0,index=0;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++,index++)
            {
                int sx=x+dx,sy=y+dy;
                bool outside=sx<0||sy<0||sx>=width||sy>=height;
                bool wet=outside&&outsideWet||water[Math.Clamp(sy,0,height-1)*width+Math.Clamp(sx,0,width-1)];
                if(wet)mask|=1<<index;
            }
            masks[x,y]=mask;
        }
        return masks;
    }
    private int[,] BuildShoreNeighbourMasks()
    {
        var world=Game.Sim.World;var water=new bool[world.Tiles.Length];
        for(int i=0;i<water.Length;i++)water[i]=world.Tiles[i].Terrain==TerrainKind.Water;
        return ShoreNeighbourMasks(water,world.Width,world.Height);
    }
    private static float ShoreCoverage(int mask,int px,int py)
    {
        if(mask==0)return 0;if(mask==511)return 1;
        var weights=ShoreKernel[py*GroundDetail+px];float coverage=0;
        for(int i=0;i<weights.Length;i++)if((mask&(1<<i))!=0)coverage+=weights[i];
        return Math.Clamp(coverage,0,1);
    }
    private static float ContinuousShoreHeight(float coverage,float sculpted)
    {
        float smooth=coverage*coverage*(3-2*coverage);
        float bed=.08f-.33f*smooth*smooth*MathF.Sqrt(smooth);
        float inland=1-Mathf.SmoothStep(.02f,.35f,coverage);
        return bed+MathF.Max(0,sculpted-.08f)*inland;
    }
    private static float ShoreSupportedHeight(float x,float y,float sculpted,int mask)
        =>ContinuousShoreHeight(ShoreCoverage(mask,(int)(x*GroundDetail)%GroundDetail,(int)(y*GroundDetail)%GroundDetail),sculpted);
    private static float GroundTriangleHeight(float a,float b,float c,float d,float u,float v)
        =>u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v);
    private void ValidateLogicalShoreline()
    {
        var world=Game.Sim.World;string digest=StateHash.ComputeDigest(Game.Sim);
        ValidateShoreLayouts();
        ValidateActorGrounding();
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
            if(!world.TileAt(x,y).Walkable)continue;
            foreach(float dx in new[]{-.28f,0,.28f})foreach(float dy in new[]{-.28f,0,.28f})
                if(GroundHeight(center.X+dx,center.Z+dy)<=.021f)throw new InvalidOperationException($"Walkable dry tile/feet are submerged at {x},{y}");
            dry++;
            foreach(var direction in new[]{Vector2I.Right,Vector2I.Down,new Vector2I(1,1),new Vector2I(-1,1)})
            {
                int nx=x+direction.X,ny=y+direction.Y;
                if(!world.IsInBounds(nx,ny)||!world.TileAt(nx,ny).Walkable)continue;
                if(direction.X!=0&&direction.Y!=0&&(!world.TileAt(nx,y).Walkable||!world.TileAt(x,ny).Walkable))continue;
                var across=new Vector3(-direction.Y,0,direction.X).Normalized()*.28f;
                for(int step=1;step<4;step++)
                foreach(float side in new[]{-1f,0,1f})
                {
                    var p=center+new Vector3(direction.X,0,direction.Y)*(step*.5f)+across*side;
                    if(GroundHeight(p.X,p.Z)<=.021f)throw new InvalidOperationException("A dry centre-to-centre walk corridor crosses rendered water");
                    paths++;
                }
            }
        }
        if(StateHash.ComputeDigest(Game.Sim)!=digest)throw new InvalidOperationException("Shoreline presentation validation changes simulation state");
        ValidateHabitatCoverage();
        GD.Print($"LOGICAL_SHORELINE_PASS: {dry} dry centres with 28 cm foot support, {paths} cardinal/allowed-diagonal corridor samples, {wet} wet centres; shared terrain/picking height and unchanged simulation");
    }
    private static bool _shoreLayoutsValidated;
    private static void ValidateShoreLayouts()
    {
        if(_shoreLayoutsValidated)return;
        int layouts=0,samples=0;
        void Fixture(int layout,int width,int height,bool outsideWet)
        {
            var water=new bool[width*height];for(int i=0;i<water.Length;i++)water[i]=(layout&(1<<i))!=0;
            var masks=ShoreNeighbourMasks(water,width,height,outsideWet);
            int nx=width*GroundDetail,ny=height*GroundDetail;var grid=new float[nx+1,ny+1];
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
                grid[x,y]=ContinuousShoreHeight(ShoreCoverage(masks[x/GroundDetail,y/GroundDetail],x%GroundDetail,y%GroundDetail),.08f)-.024f;
            float Sample(float x,float z)
            {
                float gx=Math.Clamp(x*GroundDetail*.5f,0,nx),gy=Math.Clamp(z*GroundDetail*.5f,0,ny);
                int ix=Math.Min((int)gx,nx-1),iy=Math.Min((int)gy,ny-1);
                return GroundTriangleHeight(grid[ix,iy],grid[ix+1,iy],grid[ix,iy+1],grid[ix+1,iy+1],gx-ix,gy-iy);
            }
            void DrySample(float x,float y)
            {
                if(Sample(x,y)<=.021f)throw new InvalidOperationException($"Shore layout {layout}/{width}x{height} submerges a supported dry point at {x},{y}");
                samples++;
            }
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                if(water[y*width+x])
                {if(Sample(x*2+1,y*2+1)>=.019f)throw new InvalidOperationException("Compact shoreline dries a logical water centre");continue;}
                foreach(float dx in new[]{-.28f,0,.28f})foreach(float dy in new[]{-.28f,0,.28f})DrySample(x*2+1+dx,y*2+1+dy);
                foreach(var direction in new[]{Vector2I.Right,Vector2I.Down,new Vector2I(1,1),new Vector2I(-1,1)})
                {
                    int tx=x+direction.X,ty=y+direction.Y;
                    if(tx<0||ty<0||tx>=width||ty>=height||water[ty*width+tx])continue;
                    if(direction.X!=0&&direction.Y!=0&&(water[y*width+tx]||water[ty*width+x]))continue;
                    var across=new Vector2(-direction.Y,direction.X).Normalized()*.28f;
                    for(int step=1;step<4;step++)foreach(float side in new[]{-1f,0,1f})
                        DrySample(x*2+1+direction.X*step*.5f+across.X*side,y*2+1+direction.Y*step*.5f+across.Y*side);
                }
            }
            // A lone dry tile surrounded by water must have a rounded corner,
            // while the foot support above remains dry after full compaction.
            if(width==3&&height==3&&layout==(511^(1<<4))&&Sample(4,4)>=.019f)
                throw new InvalidOperationException("Shoreline still pins every dry corner to a square rim");
            layouts++;
        }
        foreach(bool outsideWet in new[]{false,true})
        {
            for(int layout=0;layout<512;layout++)Fixture(layout,3,3,outsideWet);
            for(int width=1;width<=2;width++)for(int height=1;height<=3;height++)
                for(int layout=0;layout<(1<<(width*height));layout++)Fixture(layout,width,height,outsideWet);
        }
        _shoreLayoutsValidated=true;
        GD.Print($"SHORE_LAYOUTS_PASS: {layouts} exhaustive interior/edge/corner layouts, {samples} actual triangle samples at maximum compaction, 28 cm feet and no-corner-cut diagonal routes");
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
