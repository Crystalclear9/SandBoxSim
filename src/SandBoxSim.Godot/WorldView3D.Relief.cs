using System;
using Godot;
using SandBoxSim.Core.Environment;
namespace SandBoxSim.Client;
public partial class WorldView3D
{
    private readonly System.Collections.Generic.List<(MultiMeshInstance3D Batch,System.Collections.Generic.List<Transform3D> Roots)> _flowerBatches=new();
    private void SyncFlowerRoots()
    {
        foreach(var entry in _flowerBatches)
        for(int i=0;i<entry.Roots.Count;i++)
        {
            var transform=entry.Roots[i];var p=transform.Origin;float height=GroundHeight(p.X,p.Z)-.012f;
            if(MathF.Abs(p.Y-height)<.000001f)continue;
            transform.Origin=new(p.X,height,p.Z);entry.Roots[i]=transform;entry.Batch.Multimesh.SetInstanceTransform(i,transform);
        }
    }
    private Transform3D RootedPlant(Vector3 point,Vector3 scale,float yaw)
    {
        var normal=GroundNormal(point);
        var tilt=new Quaternion(Vector3.Up,normal);
        float angle=tilt.GetAngle();if(angle>.35f)tilt=Quaternion.Identity.Slerp(tilt,.35f/angle);
        point.Y=GroundHeight(point.X,point.Z)-.012f;
        return new Transform3D(new Basis(tilt)*new Basis(Vector3.Up,yaw)*Basis.FromScale(scale),point);
    }
    private Vector3 GroundNormal(Vector3 point)
    {
        const float radius=.18f;
        return new Vector3((GroundHeight(point.X-radius,point.Z)-GroundHeight(point.X+radius,point.Z))/(radius*2),1,
            (GroundHeight(point.X,point.Z-radius)-GroundHeight(point.X,point.Z+radius))/(radius*2)).Normalized();
    }
    private bool SupportsReeds(Vector3 point)
    {
        float height=GroundHeight(point.X,point.Z);
        return height>=-.06f&&height<=.58f&&GroundNormal(point).Y>=.80f;
    }
    private void ValidateGroundedFlowers()
    {
        SyncFlowerRoots();
        int count=0;
        foreach(var entry in _flowerBatches)
        {
            var batch=entry.Batch;
            if(batch.VisibilityRangeEnd!=42||batch.CastShadow!=GeometryInstance3D.ShadowCastingSetting.Off)throw new InvalidOperationException("Flower batch loses its distance budget");
            if(batch.Multimesh.InstanceCount!=entry.Roots.Count)throw new InvalidOperationException("Flower batch loses CPU placements");
            for(int i=0;i<entry.Roots.Count;i++)
            {
                var transform=entry.Roots[i];var p=transform.Origin;
                if(!transform.IsFinite()||MathF.Abs(p.Y-GroundHeight(p.X,p.Z)+.012f)>.0001f)throw new InvalidOperationException($"Flower root contact: origin={p}, ground={GroundHeight(p.X,p.Z)}, finite={transform.IsFinite()}");
                if(DisplayServer.GetName()!="headless"&&batch.Multimesh.GetInstanceTransform(i).Origin.DistanceTo(p)>.0001f)throw new InvalidOperationException("Rendered flower transform differs from its CPU placement");
                count++;
            }
        }
        GD.Print("GROUNDED_FLOWERS_PASS: "+count+" CPU placements, finite transforms, terrain contact, bounded drawing range; GPU transforms checked with a graphics display");
    }
    private static float TerrainNoise(float x,float y,int seed)
    {
        int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);float u=x-ix,v=y-iy;
        u=u*u*(3-2*u);v=v*v*(3-2*v);
        float At(int a,int b)
        {
            uint h=unchecked((uint)(a*374761393+b*668265263+seed*1274126177));
            h=(h^(h>>13))*1274126177u;h^=h>>16;return (h&65535)/65535f;
        }
        return (At(ix,iy)*(1-u)+At(ix+1,iy)*u)*(1-v)+(At(ix,iy+1)*(1-u)+At(ix+1,iy+1)*u)*v;
    }
    private static float NaturalRelief(float x,float y,int seed)
        =>(TerrainNoise(x*.19f,y*.19f,seed)-.5f)*.25f
          +(TerrainNoise(x*.71f,y*.71f,seed)-.5f)*.065f
          +(TerrainNoise(x*1.83f+17,y*1.83f-9,seed)-.5f)*.017f;
    private float GroundVertex(int x,int y)
    {
        int tx=Math.Clamp(x/GroundDetail,0,Game.Sim.World.Width-1),ty=Math.Clamp(y/GroundDetail,0,Game.Sim.World.Height-1);
        return _groundHeights![x,y]-Game.Sim.World.TileAt(tx,ty).FootTraffic*.024f;
    }
    private float GroundHeight(float x,float z)
    {
        if(_groundHeights==null)return 0;
        float gx=Math.Clamp(x*GroundDetail*.5f,0,_groundHeights.GetLength(0)-1);
        float gz=Math.Clamp(z*GroundDetail*.5f,0,_groundHeights.GetLength(1)-1);
        // An epsilon smaller than a float ULP rounds back onto the last vertex
        // on large maps. Clamp cell indices explicitly, including outside probes.
        int ix=Math.Min((int)gx,_groundHeights.GetLength(0)-2),iz=Math.Min((int)gz,_groundHeights.GetLength(1)-2);float u=gx-ix,v=gz-iz;
        float a=GroundVertex(ix,iz),b=GroundVertex(ix+1,iz),c=GroundVertex(ix,iz+1),d=GroundVertex(ix+1,iz+1);
        return u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v);
    }
    private Vector2I? Pick(Vector2 screen)
    {
        var hit=SurfaceHit(screen);if(!hit.HasValue)return null;
        return new Vector2I(Math.Clamp((int)(hit.Value.X*.5f),0,Game.Sim.World.Width-1),Math.Clamp((int)(hit.Value.Z*.5f),0,Game.Sim.World.Height-1));
    }
    private Vector3? SurfaceHit(Vector2 screen)
    {
        if(_groundHeights==null)return null;
        Vector3 origin=_camera.ProjectRayOrigin(screen),ray=_camera.ProjectRayNormal(screen);
        float enter=0,exit=_camera.Far;
        bool Slab(float o,float d,float maximum)
        {
            if(MathF.Abs(d)<.000001f)return o>=0&&o<=maximum;
            float a=-o/d,b=(maximum-o)/d;if(a>b)(a,b)=(b,a);enter=MathF.Max(enter,a);exit=MathF.Min(exit,b);return enter<=exit;
        }
        if(!Slab(origin.X,ray.X,Game.Sim.World.Width*2)||!Slab(origin.Z,ray.Z,Game.Sim.World.Height*2))return null;
        int nx=_groundHeights.GetLength(0)-1,nz=_groundHeights.GetLength(1)-1;
        float cell=2f/GroundDetail;var start=origin+ray*(enter+.00001f);
        int x=Math.Clamp((int)MathF.Floor(start.X/cell),0,nx-1),z=Math.Clamp((int)MathF.Floor(start.Z/cell),0,nz-1);
        int sx=ray.X>=0?1:-1,sz=ray.Z>=0?1:-1;
        float stepX=MathF.Abs(ray.X)<.000001f?float.PositiveInfinity:cell/MathF.Abs(ray.X);
        float stepZ=MathF.Abs(ray.Z)<.000001f?float.PositiveInfinity:cell/MathF.Abs(ray.Z);
        float nextX=float.IsInfinity(stepX)?stepX:((x+(sx>0?1:0))*cell-origin.X)/ray.X;
        float nextZ=float.IsInfinity(stepZ)?stepZ:((z+(sz>0?1:0))*cell-origin.Z)/ray.Z;
        float Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var e=b-a;var f=c-a;var cross=ray.Cross(f);float det=e.Dot(cross);
            if(MathF.Abs(det)<.0000001f)return float.PositiveInfinity;
            var offset=origin-a;float u=offset.Dot(cross)/det;if(u<-.00001f||u>1.00001f)return float.PositiveInfinity;
            var q=offset.Cross(e);float v=ray.Dot(q)/det;if(v<-.00001f||u+v>1.00001f)return float.PositiveInfinity;
            float t=f.Dot(q)/det;return t>=enter-.0001f&&t<=exit+.0001f?t:float.PositiveInfinity;
        }
        float water=float.PositiveInfinity;
        if(ray.Y<-.00001f)
        {
            float t=(.02f-origin.Y)/ray.Y;var w=origin+ray*t;
            if(t>=enter&&t<=exit&&Game.Sim.World.IsInBounds((int)MathF.Floor(w.X*.5f),(int)MathF.Floor(w.Z*.5f))
                &&Game.Sim.World.TerrainAt((int)(w.X*.5f),(int)(w.Z*.5f))==TerrainKind.Water)water=t;
        }
        for(int guard=0;guard<=nx+nz;guard++)
        {
            var a=new Vector3(x*cell,GroundVertex(x,z),z*cell);var b=new Vector3((x+1)*cell,GroundVertex(x+1,z),z*cell);
            var c=new Vector3(x*cell,GroundVertex(x,z+1),(z+1)*cell);var d=new Vector3((x+1)*cell,GroundVertex(x+1,z+1),(z+1)*cell);
            float t=MathF.Min(Triangle(a,b,c),Triangle(b,d,c));
            if(float.IsFinite(t))return origin+ray*MathF.Min(t,water);
            if(nextX<nextZ){if(nextX>exit)break;x+=sx;nextX+=stepX;}else{if(nextZ>exit)break;z+=sz;nextZ+=stepZ;}
            if(x<0||z<0||x>=nx||z>=nz)break;
        }
        return float.IsFinite(water)?origin+ray*water:null;
    }
    private void ConformFeet(ResidentRig rig)
    {
        for(int side=0;side<2;side++)
        {
            var foot=rig.Feet[side];var point=foot.GlobalPosition;
            float center=GroundHeight(point.X,point.Z),left=GroundHeight(point.X-.12f,point.Z),right=GroundHeight(point.X+.12f,point.Z);
            float front=GroundHeight(point.X,point.Z-.14f),back=GroundHeight(point.X,point.Z+.14f);
            // Small corrections preserve the authored leg pose; large ledges are
            // represented by locomotion, not stretched disconnected limbs.
            var leg=rig.Legs[side].Position;leg.Y+=Math.Clamp(center-rig.GlobalPosition.Y,-.07f,.07f)/MathF.Max(.01f,rig.GlobalBasis.Y.Length());rig.Legs[side].Position=leg;
            var normal=new Vector3((left-right)/.24f,1,(front-back)/.28f).Normalized();
            var local=rig.GlobalBasis.Transposed()*normal;
            var rotation=foot.Rotation;rotation.X+=Math.Clamp(MathF.Atan2(local.Z,local.Y),-.24f,.24f);rotation.Z=Math.Clamp(-MathF.Atan2(local.X,local.Y),-.24f,.24f);foot.Rotation=rotation;
        }
    }
}
