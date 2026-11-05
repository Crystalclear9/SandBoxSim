using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<string,Vector3[]> _stoneBases=new();
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
