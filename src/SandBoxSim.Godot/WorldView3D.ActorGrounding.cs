using System;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private const float MaximumAdjacentPresentationStep=2.85f;
    private Func<Vector3,bool>? _actorNearShore,_actorSupported;
    private Func<float,float,float>? _actorGround;
    private bool DryActorTarget(Vector3 point)
    {
        var world=Game.Sim.World;int x=(int)MathF.Floor(point.X*.5f),y=(int)MathF.Floor(point.Z*.5f);
        return world.IsInBounds(x,y)&&world.TileAt(x,y).Walkable&&world.TerrainAt(x,y)!=TerrainKind.Water;
    }
    private bool ActorNearShore(Vector3 point)
    {
        var world=Game.Sim.World;int x=(int)MathF.Floor(point.X*.5f),y=(int)MathF.Floor(point.Z*.5f);
        return !world.IsInBounds(x,y)||world.DistanceToWater(x,y,2)<=1;
    }
    private bool ActorFootSupport(Vector3 point)
    {
        for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
            if(GroundHeight(point.X+x*.28f,point.Z+z*.28f)<=.021f)return false;
        return true;
    }
    private Vector3 GroundedActorPosition(Vector3 current,Vector3 target,float delta,float response,bool paused)
    {
        if(paused)return current;
        return GroundedActorStep(current,target,delta,response,false,DryActorTarget(target),
            _actorNearShore??=ActorNearShore,_actorSupported??=ActorFootSupport,_actorGround??=GroundHeight);
    }
    private static Vector3 GroundedActorStep(Vector3 current,Vector3 target,float delta,float response,bool paused,bool dryTarget,
        Func<Vector3,bool> nearShore,Func<Vector3,bool> supported,Func<float,float,float> ground)
    {
        if(paused)return current;
        var next=current.Lerp(target,1-MathF.Exp(-delta*response));
        float dx=target.X-current.X,dz=target.Z-current.Z;
        // The model follows the current logical position, not an invented path.
        // A lag spanning unknown steps cannot safely be interpolated across a
        // river or a turn. Resynchronise XZ as well as Y to the true tile centre.
        if(dryTarget&&(dx*dx+dz*dz>MaximumAdjacentPresentationStep*MaximumAdjacentPresentationStep||(nearShore(next)&&!supported(next))))next=target;
        next.Y=ground(next.X,next.Z);return next;
    }
    private void ValidateActorGrounding()
    {
        string digest=StateHash.ComputeDigest(Game.Sim);int revision=Game.Sim.World.Revision;
        var mesh=_terrainRoot.GetChild<MeshInstance3D>(0).Mesh;int adjacent=0,corners=0;
        var world=Game.Sim.World;
        for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)
        {
            var target=PositionAt(x,y);if(!DryActorTarget(target))continue;
            if(adjacent==0&&x>0)
            {
                var current=PositionAt(x-1,y);
                if(DryActorTarget(current))
                {
                    var expected=current.Lerp(target,1-MathF.Exp(-.02f*10));expected.Y=GroundHeight(expected.X,expected.Z);
                    if(GroundedActorPosition(current,target,.02f,10,false).DistanceTo(expected)>.00001f)
                        throw new InvalidOperationException("Normal adjacent dry motion changed its exponential interpolation");
                    if(GroundedActorPosition(current,target,1,10,true)!=current)throw new InvalidOperationException("Paused actual actor grounding moves");
                    adjacent++;
                }
            }
            if(corners==0&&ActorNearShore(target))
                foreach(float dx in new[]{-.85f,.85f})foreach(float dz in new[]{-.85f,.85f})
                {
                    var candidate=target+new Vector3(dx,0,dz);if(ActorFootSupport(candidate))continue;
                    var current=target+new Vector3(dx*2,0,dz*2);current.Y=GroundHeight(current.X,current.Z);
                    var recovered=GroundedActorPosition(current,target,MathF.Log(2)/10,10,false);
                    if(recovered.DistanceTo(target)>.00001f)throw new InvalidOperationException("Actual shoreline corner makes a dry-target actor dive instead of resynchronising");
                    corners++;break;
                }
        }
        ValidateActorGroundingFixture();
        if(StateHash.ComputeDigest(Game.Sim)!=digest||world.Revision!=revision||_terrainRoot.GetChild<MeshInstance3D>(0).Mesh!=mesh)
            throw new InvalidOperationException("Actor grounding validation changes the world or terrain mesh");
        GD.Print($"ACTOR_GROUNDING_PASS: actual world adjacent={adjacent}, unsafe-corner recoveries={corners}; shared resident/deer/wolf step, pause, U-bend, non-dry targets and unchanged revision/digest");
    }
    private static void ValidateActorGroundingFixture()
    {
        const int width=7,height=7;var water=new bool[width*height];
        for(int y=1;y<=5;y++){water[y*width+2]=true;water[y*width+4]=true;}
        for(int x=2;x<=4;x++)water[5*width+x]=true;
        var masks=ShoreNeighbourMasks(water,width,height);
        int nx=width*GroundDetail,ny=height*GroundDetail;var vertices=new Vector3[(nx+1)*(ny+1)];var indices=new int[nx*ny*6];
        for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
            vertices[y*(nx+1)+x]=new(x*2f/GroundDetail,ContinuousShoreHeight(ShoreCoverage(masks[x/GroundDetail,y/GroundDetail],x%GroundDetail,y%GroundDetail),.08f),y*2f/GroundDetail);
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {
            int a=y*(nx+1)+x,k=(y*nx+x)*6;
            indices[k]=a;indices[k+1]=a+1;indices[k+2]=a+nx+1;indices[k+3]=a+1;indices[k+4]=a+nx+2;indices[k+5]=a+nx+1;
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Index]=indices;
        using var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        var actual=mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        float Ground(float x,float z)
        {
            float gx=Math.Clamp(x*GroundDetail*.5f,0,nx),gy=Math.Clamp(z*GroundDetail*.5f,0,ny);
            int ix=Math.Min((int)gx,nx-1),iy=Math.Min((int)gy,ny-1),a=iy*(nx+1)+ix;
            return GroundTriangleHeight(actual[a].Y,actual[a+1].Y,actual[a+nx+1].Y,actual[a+nx+2].Y,gx-ix,gy-iy);
        }
        bool Supported(Vector3 point)
        {
            foreach(float x in new[]{-.28f,0,.28f})foreach(float y in new[]{-.28f,0,.28f})if(Ground(point.X+x,point.Z+y)<=.021f)return false;
            return true;
        }
        Vector3 At(float x,float z)=>new(x,Ground(x,z),z);
        foreach(float response in new[]{10f,8f})
        {
            float delta=MathF.Log(2)/response;
            var current=At(3,7);var target=At(7,7);var submerged=current.Lerp(target,.5f);
            if(Supported(submerged)||GroundedActorStep(current,target,delta,response,false,true,_=>true,Supported,Ground).DistanceTo(target)>.00001f)
                throw new InvalidOperationException("U-shaped actual mesh does not resynchronise a dry-target actor across unknown steps");
            var closeTarget=At(7,9);var offRoute=At(5.3f,10.7f);var trial=offRoute.Lerp(closeTarget,.5f);
            if(Supported(trial)||offRoute.DistanceTo(closeTarget)>MaximumAdjacentPresentationStep||GroundedActorStep(offRoute,closeTarget,delta,response,false,true,_=>true,Supported,Ground).DistanceTo(closeTarget)>.00001f)
                throw new InvalidOperationException("Near-shore foot guard does not recover a short unsupported displayed step");
            var start=At(1,1);var end=At(3,1);var expected=start.Lerp(end,.5f);expected.Y=Ground(expected.X,expected.Z);
            if(GroundedActorStep(start,end,delta,response,false,true,_=>true,Supported,Ground).DistanceTo(expected)>.00001f)
                throw new InvalidOperationException("Safe adjacent fixture does not retain exponential interpolation");
            var wetTarget=At(5,7);var expectedWet=current.Lerp(wetTarget,.5f);expectedWet.Y=Ground(expectedWet.X,expectedWet.Z);
            if(GroundedActorStep(current,wetTarget,delta,response,false,false,_=>true,Supported,Ground).DistanceTo(expectedWet)>.00001f)
                throw new InvalidOperationException("Non-dry logical target is falsely moved onto a dry bank");
            bool queried=false;
            bool Query(Vector3 point){queried=true;return true;}
            float HeightQuery(float x,float y){queried=true;return 0;}
            if(GroundedActorStep(start,end,delta,response,true,true,Query,Query,HeightQuery)!=start||queried)
                throw new InvalidOperationException("Paused grounding queries or moves the displayed actor");
        }
    }
}
