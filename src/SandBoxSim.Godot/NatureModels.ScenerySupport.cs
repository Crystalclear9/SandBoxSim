using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<string,Vector3[]> _stoneBases=new();
    private readonly Dictionary<string,float> _solidRadii=new();
    private readonly Dictionary<(string Shape,float Height),(Vector2 A,Vector2 B)[]> _workSections=new();

    private (Vector2 A,Vector2 B)[] WorkSection(string shape,float height)
    {
        if(_workSections.TryGetValue((shape,height),out var cached))return cached;
        var mesh=Shape(shape);
        int surface=shape=="boulder"?0:_environmentRootSurfaces[shape=="broad-tree"?"tree_small_02":"fir_sapling"];
        var data=mesh.SurfaceGetArrays(surface);
        var vertices=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=data[(int)Mesh.ArrayType.Index].AsInt32Array();
        var sections=new List<(Vector2,Vector2)>();
        for(int i=0;i<indices.Length;i+=3)
        {
            Vector2 first=default;bool found=false;
            for(int edge=0;edge<3;edge++)
            {
                var a=vertices[indices[i+edge]];
                var b=vertices[indices[i+(edge+1)%3]];
                if((a.Y<height)==(b.Y<height))continue;
                var hit=a.Lerp(b,(height-a.Y)/(b.Y-a.Y));
                var point=new Vector2(hit.X,hit.Z);
                if(found&&first.DistanceSquaredTo(point)>1e-10f)sections.Add((first,point));
                first=point;found=true;
            }
        }
        return _workSections[(shape,height)]=sections.ToArray();
    }

    internal bool TryWorkSurface(string shape,Transform3D placement,Vector3 stand,float height,out ResidentRig.ToolSurface contact)
    {
        contact=default;
        var from=placement.AffineInverse()*stand;
        var start=new Vector2(from.X,from.Z);
        if(start.LengthSquared()<.005f)return false;
        var direction=-start.Normalized();
        float closest=float.PositiveInfinity;
        Vector2 outward=default;
        foreach(var (a,b) in WorkSection(shape,height))
        {
            var segment=b-a;
            float divider=direction.Cross(segment);
            if(Mathf.Abs(divider)<1e-7f)continue;
            var offset=a-start;
            float distance=offset.Cross(segment)/divider;
            float portion=offset.Cross(direction)/divider;
            if(distance<0||distance>=closest||portion is <0 or >1)continue;
            closest=distance;
            outward=new Vector2(-segment.Y,segment.X).Normalized();
            if(outward.Dot(direction)>0)outward=-outward;
        }
        if(!float.IsFinite(closest))return false;
        var pointAtHeight=start+direction*closest;
        var normal=(placement.Basis.Inverse().Transposed()*new Vector3(outward.X,0,outward.Y)).Normalized();
        contact=new(placement*new Vector3(pointAtHeight.X,height,pointAtHeight.Y),normal);
        return true;
    }

    internal float SolidSceneryRadius(string shape)
    {
        if(_solidRadii.TryGetValue(shape,out float cached))return cached;
        var mesh=Shape(shape);
        bool rock=shape=="boulder";
        string asset=shape=="broad-tree"?"tree_small_02":"fir_sapling";
        int surface=rock?0:_environmentRootSurfaces[asset];
        // The fir's flexible low branches are outside its solid stem proxy.
        float top=rock?float.PositiveInfinity:shape=="broad-tree"?1.8f:.5f;
        var data=mesh.SurfaceGetArrays(surface);
        var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        float squared=0;
        void Include(Vector3 point)
        {
            squared=MathF.Max(squared,point.X*point.X+point.Z*point.Z);
        }
        foreach(var point in points)if(point.Y<=top)Include(point);
        if(!rock)
        {
            // Include triangle edges cut by the top of the stem section.
            var indices=data[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int i=0;i<indices.Length;i+=3)
            for(int edge=0;edge<3;edge++)
            {
                var a=points[indices[i+edge]];
                var b=points[indices[i+(edge+1)%3]];
                if((a.Y<=top)!=(b.Y<=top))Include(a.Lerp(b,(top-a.Y)/(b.Y-a.Y)));
            }
        }
        if(!float.IsFinite(squared)||squared<=0)throw new InvalidOperationException("Scenery mesh has no solid footprint");
        return _solidRadii[shape]=MathF.Sqrt(squared);
    }
    internal Vector3[] StoneBase(string shape)
    {
        if(_stoneBases.TryGetValue(shape,out var cached))return cached;
        var mesh=Shape(shape);float low=float.PositiveInfinity,high=float.NegativeInfinity;
        var points=new List<Vector3>();
        for(int s=0;s<mesh.GetSurfaceCount();s++)
            foreach(var p in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {points.Add(p);low=MathF.Min(low,p.Y);high=MathF.Max(high,p.Y);}
        var bins=new Vector3[16];var radius=new float[16];Array.Fill(radius,-1);
        foreach(var p in points)if(p.Y<=low+(high-low)*.20f)
        {
            int bin=Math.Min(15,(int)(Mathf.PosMod(MathF.Atan2(p.Z,p.X),Mathf.Tau)/Mathf.Tau*16));
            float r=p.X*p.X+p.Z*p.Z;
            if(r>radius[bin]){radius[bin]=r;bins[bin]=p;}
        }
        var result=new List<Vector3>();for(int i=0;i<16;i++)if(radius[i]>=0)result.Add(bins[i]);
        if(result.Count<4)throw new InvalidOperationException("Insufficient actual stone base support");
        return _stoneBases[shape]=result.ToArray();
    }
}
