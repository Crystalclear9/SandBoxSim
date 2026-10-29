using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly float[][] _clothEnvelopes=new float[2][];

    // A woven panel spans hollows between its supports. Use the convex section
    // of the attributed body, rather than reproducing every muscular groove.
    // This is a cached bind-space tailoring operation, not cloth simulation.
    private float ClothEnvelope(float[] support,float y,float angle)
    {
        int frame=BodyFrame==1?1:0;
        if(_clothEnvelopes[frame]==null)
        {
            var data=new float[TrunkRows*TrunkColumns];
            static float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b-a).Cross(c-a);
            for(int row=0;row<TrunkRows;row++)
            {
                float height=TrunkLow+row*.005f;
                var ring=new List<Vector2>(TrunkColumns);
                for(int col=0;col<TrunkColumns;col++)
                {
                    float a=col*Mathf.Tau/TrunkColumns;
                    float r=(TrunkSample(support,height-.030f,a)+4*TrunkSample(support,height-.015f,a)+
                        6*TrunkSample(support,height,a)+4*TrunkSample(support,height+.015f,a)+TrunkSample(support,height+.030f,a))/16;
                    ring.Add(new(MathF.Cos(a)*r,MathF.Sin(a)*r));
                }
                ring.Sort((a,b)=>a.X==b.X?a.Y.CompareTo(b.Y):a.X.CompareTo(b.X));
                var hull=new List<Vector2>(TrunkColumns*2);
                foreach(var p in ring){while(hull.Count>=2&&Cross(hull[^2],hull[^1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
                int lower=hull.Count;
                for(int i=ring.Count-2;i>=0;i--){var p=ring[i];while(hull.Count>lower&&Cross(hull[^2],hull[^1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
                hull.RemoveAt(hull.Count-1);
                for(int col=0;col<TrunkColumns;col++)
                {
                    float a=col*Mathf.Tau/TrunkColumns;var ray=new Vector2(MathF.Cos(a),MathF.Sin(a));
                    float radius=float.PositiveInfinity;
                    for(int i=0;i<hull.Count;i++)
                    {
                        var p=hull[i];var edge=hull[(i+1)%hull.Count]-p;
                        float divisor=ray.Cross(edge);if(MathF.Abs(divisor)<1e-7f)continue;
                        float t=p.Cross(edge)/divisor,u=p.Cross(ray)/divisor;
                        if(t>0&&u>=-.00001f&&u<=1.00001f)radius=MathF.Min(radius,t);
                    }
                    if(!float.IsFinite(radius))throw new InvalidOperationException("Incomplete convex cloth section");
                    // Ease belongs to the garment, not to an extra chest bulge.
                    data[row*TrunkColumns+col]=radius+.014f+.004f*MathF.Exp(-MathF.Pow((height-1.02f)/.13f,2));
                }
            }
            _clothEnvelopes[frame]=data;
        }
        return TrunkSample(_clothEnvelopes[frame],y,angle);
    }

    private static Vector3[] ClothSurfaceNormals(Vector3[] points,int[] indices)
    {
        var normals=new Vector3[points.Length];
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2];
            // Godot's front faces use clockwise winding.
            var n=(points[c]-points[a]).Cross(points[b]-points[a]);
            normals[a]+=n;normals[b]+=n;normals[c]+=n;
        }
        for(int i=0;i<normals.Length;i++)
        {
            if(!normals[i].IsFinite()||normals[i].LengthSquared()<1e-16f)
                throw new InvalidOperationException("Degenerate fitted cloth normal");
            normals[i]=normals[i].Normalized();
        }
        return normals;
    }
}
