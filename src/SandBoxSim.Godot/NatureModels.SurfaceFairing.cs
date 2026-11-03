using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Cached rest-surface fairing. Positive/negative passes suppress small
    // ridges without using a uniformly shrinking Laplacian on the silhouette.
    private static void FairRestSurface(Vector3[] points,int[] triangles,float[] freedom,float limit,bool bridgeHollows=false,bool roundRelief=false)
    {
        var rest=(Vector3[])points.Clone();
        var neighbours=new HashSet<int>[points.Length];
        for(int i=0;i<points.Length;i++)neighbours[i]=new();
        for(int t=0;t<triangles.Length;t+=3)for(int j=0;j<3;j++)
        {
            int a=triangles[t+j],b=triangles[t+(j+1)%3];neighbours[a].Add(b);neighbours[b].Add(a);
        }
        for(int i=0;i<points.Length;i++)if(neighbours[i].Count==0)freedom[i]=0;
        var normals=FairingNormals(rest,triangles,freedom);
        for(int pass=0;pass<20;pass++)
        {
            var old=(Vector3[])points.Clone();float gain=bridgeHollows||roundRelief?.46f:pass%2==0?.46f:-.47f;
            for(int i=0;i<points.Length;i++)if(freedom[i]>0&&neighbours[i].Count>0)
            {
                Vector3 average=Vector3.Zero;float sum=0;
                foreach(int j in neighbours[i])
                {
                    float weight=1/MathF.Max(.0005f,rest[i].DistanceTo(rest[j]));
                    average+=old[j]*weight;sum+=weight;
                }
                float displacement=(average/sum-old[i]).Dot(normals[i])*gain*freedom[i];
                if(bridgeHollows)displacement=MathF.Max(0,displacement);
                var candidate=old[i]+normals[i]*displacement;
                var offset=candidate-rest[i];float bound=limit*freedom[i];
                if(offset.Length()>bound)offset=offset.Normalized()*bound;
                points[i]=rest[i]+offset;
            }
        }
        // Respect tiny triangles and preserve every constrained border exactly.
        for(int pass=0;pass<12;pass++)for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
            var before=(rest[c]-rest[a]).Cross(rest[b]-rest[a]);
            var after=(points[c]-points[a]).Cross(points[b]-points[a]);
            if(before.Dot(after)>=.50f*before.LengthSquared())continue;
            points[a]=rest[a].Lerp(points[a],.5f);points[b]=rest[b].Lerp(points[b],.5f);points[c]=rest[c].Lerp(points[c],.5f);
        }
        if(roundRelief)
        {
            // Backtrack the whole edit if a tiny sewn triangle loses a usable
            // area-weighted vertex normal. Keep the original normal threshold;
            // independently repairing faces can still leave a near-zero sum.
            for(int attempt=0;attempt<16;attempt++)
            {
                var area=new Vector3[points.Length];
                for(int t=0;t<triangles.Length;t+=3)
                {
                    int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                    var n=(points[c]-points[a]).Cross(points[b]-points[a]);area[a]+=n;area[b]+=n;area[c]+=n;
                }
                bool valid=true;for(int i=0;i<area.Length;i++)if(neighbours[i].Count>0&&area[i].LengthSquared()<1e-16f){valid=false;break;}
                if(valid)break;
                if(attempt==15)throw new InvalidOperationException("Rounded cloth loses its source normal area");
                for(int i=0;i<points.Length;i++)points[i]=rest[i].Lerp(points[i],.5f);
            }
        }
        for(int i=0;i<points.Length;i++)
            if(!points[i].IsFinite()||points[i].DistanceTo(rest[i])>limit*freedom[i]+.000001f)
                throw new InvalidOperationException("Rest fairing loses its source bound or pinned attachment");
        for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];var before=(rest[c]-rest[a]).Cross(rest[b]-rest[a]);
            var after=(points[c]-points[a]).Cross(points[b]-points[a]);
            if(before.LengthSquared()>1e-20f&&before.Dot(after)<=0)throw new InvalidOperationException("Rest fairing reverses a source triangle");
        }
    }
    private void FairGarmentRest(Vector3[] points,int[] indices,Vector2[] pattern)
    {
        var freedom=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var p=points[i];float angle=MathF.Atan2(p.Z,p.X);
            float height=Mathf.SmoothStep(.94f,1.01f,p.Y)*(1-Mathf.SmoothStep(1.25f,1.29f,p.Y));
            float collar=1-Mathf.SmoothStep(1.19f,1.25f,p.Y)*(1-Mathf.SmoothStep(.055f,.085f,MathF.Abs(p.X)));
            float belt=Mathf.SmoothStep(.015f,.040f,MathF.Abs(p.Y-FinishedBeltHeight(angle)));
            freedom[i]=height*collar*belt*(1-Mathf.SmoothStep(.12f,.20f,MathF.Max(0,pattern[i].X)));
        }
        FairRestSurface(points,indices,freedom,.008f,true);
        if(BodyFrame==1)
        {
            // The old one-sided bridge fills hollows but retains every raised
            // cap ridge. Round the shoulder cloth in both normal directions,
            // with a small source bound and exact collar/cuff/waist supports.
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];
                freedom[i]=Mathf.SmoothStep(.065f,.110f,MathF.Abs(p.X))
                    *Mathf.SmoothStep(1.14f,1.19f,p.Y)*(1-Mathf.SmoothStep(1.26f,1.29f,p.Y))
                    *(1-Mathf.SmoothStep(.17f,.23f,MathF.Max(0,pattern[i].X)));
            }
            FairRestSurface(points,indices,freedom,.008f,roundRelief:true);
        }
    }
    private readonly Dictionary<string,(Vector3[] Points,Vector3[] Normals)> _forearmFairings=new();
    private void FairForearmRest(Vector3[] points,Vector3[] normals,int[] indices,int frame,string source)
    {
        if(frame!=1)return;
        if(_forearmFairings.TryGetValue(source,out var cached))
        {
            Array.Copy(cached.Points,points,points.Length);Array.Copy(cached.Normals,normals,normals.Length);return;
        }
        var freedom=new float[points.Length];
        for(int i=0;i<points.Length;i++)freedom[i]=Mathf.SmoothStep(.080f,.120f,points[i].Y)*(1-Mathf.SmoothStep(.225f,.250f,points[i].Y));
        FairRestSurface(points,indices,freedom,.0008f);
        var updated=FairingNormals(points,indices,freedom);
        for(int i=0;i<points.Length;i++)if(freedom[i]>0)normals[i]=updated[i];
        _forearmFairings[source]=((Vector3[])points.Clone(),(Vector3[])normals.Clone());
    }
    private static Vector3[] FairingNormals(Vector3[] points,int[] triangles,float[] active)
    {
        var result=new Vector3[points.Length];
        for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
            var n=(points[c]-points[a]).Cross(points[b]-points[a]);result[a]+=n;result[b]+=n;result[c]+=n;
        }
        for(int i=0;i<result.Length;i++)
        {
            if(result[i].LengthSquared()>1e-20f)result[i]=result[i].Normalized();
            else if(active[i]>0)throw new InvalidOperationException("Degenerate active fairing surface");
        }
        return result;
    }
    private bool _restFairingValidated;
    private void ValidateRestFairing()
    {
        if(_restFairingValidated)return;
        const int width=13;var points=new Vector3[width*width];var freedom=new float[points.Length];var triangles=new List<int>();
        for(int y=0;y<width;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;points[i]=new(x*.006f,y*.006f,((x+y)%2==0?1:-1)*.002f);
            freedom[i]=x>0&&x<width-1&&y>0&&y<width-1?1:0;
            if(x<width-1&&y<width-1)triangles.AddRange(new[]{i,i+1,i+width,i+1,i+width+1,i+width});
        }
        var rest=(Vector3[])points.Clone();FairRestSurface(points,triangles.ToArray(),freedom,.008f,true);
        float low=float.PositiveInfinity,high=float.NegativeInfinity;
        for(int y=2;y<width-2;y++)for(int x=2;x<width-2;x++){float z=points[y*width+x].Z;low=MathF.Min(low,z);high=MathF.Max(high,z);}
        for(int i=0;i<points.Length;i++)if(freedom[i]==0&&points[i]!=rest[i])throw new InvalidOperationException("Cloth fairing moves a sewn border");
        if(high-low>=.0032f)throw new InvalidOperationException("Cloth fairing retains the source corrugation");
        GD.Print($"REST_SURFACE_PASS: constrained cloth fixture, relief range=.00400 -> {high-low:F5} m, exact borders, bounded displacement and outward triangles");
        _restFairingValidated=true;
    }
}
