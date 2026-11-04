using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record BodiceSurface(Vector3[] Source,int[] Indices,Vector2[] Pattern,Vector3[] Result);
    private readonly Dictionary<int,BodiceSurface> _bodiceSurfaces=new();
    private sealed class BodiceNeighborhood(Vector3[] chart)
    {
        internal readonly Vector3[] Chart=(Vector3[])chart.Clone();
        internal readonly int[][] Candidates=new int[chart.Length][];
        internal int Calls;
    }
    private readonly Dictionary<(int Cut,bool Pose),BodiceNeighborhood> _bodiceNeighborhoods=new();

    // Complete the front AFTER shoulder repair, before skin weights and pose
    // baking. Area-weighted quadratic patches suppress narrow cap ridges while
    // retaining the broader chest curvature. No per-frame fitting is involved.
    private void CompleteBodiceSurface(Vector3[] points,int[] indices,Vector2[] pattern,Vector3[]? canonical=null,float[]? participation=null)
    {
        if(!FittedFigure)return;
        if(canonical==null&&_bodiceSurfaces.TryGetValue(_garmentCut,out var cached)&&cached.Source.AsSpan().SequenceEqual(points)
            &&cached.Indices.AsSpan().SequenceEqual(indices)&&cached.Pattern.AsSpan().SequenceEqual(pattern))
        {Array.Copy(cached.Result,points,points.Length);return;}
        var rest=(Vector3[])points.Clone();var chart=canonical??rest;var areas=new float[points.Length];var active=new float[points.Length];
        var edges=new Dictionary<(int,int),int>();
        for(int t=0;t<indices.Length;t+=3)
        {
            int a=indices[t],b=indices[t+1],c=indices[t+2];
            float area=(chart[b]-chart[a]).Cross(chart[c]-chart[a]).Length()/6;
            areas[a]+=area;areas[b]+=area;areas[c]+=area;
            for(int k=0;k<3;k++)
            {
                int x=indices[t+k],y=indices[t+(k+1)%3];var edge=x<y?(x,y):(y,x);
                edges.TryGetValue(edge,out int n);edges[edge]=n+1;
            }
        }
        for(int i=0;i<points.Length;i++)
        {
            var p=chart[i];
            active[i]=(pattern[i].X<0?1:1-Mathf.SmoothStep(.040f,.100f,pattern[i].X))*Mathf.SmoothStep(.045f,.070f,MathF.Abs(p.X))
                *(1-Mathf.SmoothStep(.135f,.175f,MathF.Abs(p.X)))
                *Mathf.SmoothStep(1.16f,1.19f,p.Y)*(1-Mathf.SmoothStep(1.25f,1.29f,p.Y))
                *Mathf.SmoothStep(.015f,.040f,-p.Z);
            float neck=(1-Mathf.SmoothStep(.075f,.095f,MathF.Abs(p.X)))*Mathf.SmoothStep(1.20f,1.235f,p.Y);
            active[i]*=1-neck;
            if(participation!=null)active[i]*=participation[i];
        }
        foreach(var edge in edges)if(edge.Value==1){active[edge.Key.Item1]=0;active[edge.Key.Item2]=0;}
        var key=(_garmentCut,canonical!=null);
        if(!_bodiceNeighborhoods.TryGetValue(key,out var neighborhood)||!neighborhood.Chart.AsSpan().SequenceEqual(chart))
            _bodiceNeighborhoods[key]=neighborhood=new BodiceNeighborhood(chart);
        int call=++neighborhood.Calls;
        bool verify=call==1||canonical!=null&&(call==60||call==120);
        var delta=new float[points.Length];var fitted=new bool[points.Length];var depths=new float[points.Length];double before=0,after=0;int count=0;float maximum=0;
        for(int i=0;i<rest.Length;i++)if(active[i]>0)
        {
            // Membership depends on the immutable canonical chart, not the
            // pose. Keep ascending source indices so summation stays bit exact.
            var candidates=neighborhood.Candidates[i]??=BodiceCandidates(chart,i);
            bool success=TryBodicePatch(rest,areas,i,out float depth,chart,candidates);
            if(verify)
            {
                bool reference=TryBodicePatch(rest,areas,i,out float original,chart);
                if(reference!=success||BitConverter.SingleToInt32Bits(original)!=BitConverter.SingleToInt32Bits(depth))
                    throw new InvalidOperationException("Cached bodice neighborhood changes the reference fit");
            }
            if(!success)continue;
            fitted[i]=true;depths[i]=depth;
            delta[i]=Math.Clamp(depth-rest[i].Z,-.010f,.010f)*active[i];
            before+=Math.Pow(depth-rest[i].Z,2)*active[i];count++;
        }
        if(canonical!=null&&count==0)return;
        // A slender cap triangle must not undo the whole front-panel edit.
        // Backtrack its own vertices; recheck the boundary on the next pass.
        var gain=new float[points.Length];Array.Fill(gain,1);
        for(int attempt=0;attempt<64;attempt++)
        {
            for(int i=0;i<points.Length;i++)points[i]=rest[i]+Vector3.Back*(delta[i]*gain[i]);
            bool valid=true;
            for(int t=0;t<indices.Length;t+=3)
            {
                int a=indices[t],b=indices[t+1],c=indices[t+2];
                var n=(rest[c]-rest[a]).Cross(rest[b]-rest[a]);var updated=(points[c]-points[a]).Cross(points[b]-points[a]);
                if(n.Dot(updated)>=.25f*n.LengthSquared())continue;
                valid=false;gain[a]*=.5f;gain[b]*=.5f;gain[c]*=.5f;
            }
            if(valid)break;
            if(attempt==63)throw new InvalidOperationException("Bodice patch completion folds its repaired input");
        }
        for(int i=0;i<points.Length;i++)
        {
            float edit=points[i].DistanceTo(rest[i]);maximum=MathF.Max(maximum,edit);
            if(!points[i].IsFinite()||edit>.01001f*active[i]+.000001f||(active[i]==0&&points[i]!=rest[i]))
                throw new InvalidOperationException("Bodice patch completion loses its fixed neckline, cuff or source bound");
            if(fitted[i])after+=Math.Pow(depths[i]-points[i].Z,2)*active[i];
        }
        if(canonical==null&&count<20||after>before+.00000001)throw new InvalidOperationException("Bodice completion does not reduce its narrow surface residual");
        if(canonical!=null)
        {
            if(verify)GD.Print($"BODICE_NEIGHBOR_PASS: cut={_garmentCut}, pose={call}, active={count}; reference scan and cached candidates yield bit-identical depths");
            _posedBodiceSamples+=count;_posedBodiceMaximum=MathF.Max(_posedBodiceMaximum,maximum);return;
        }
        _bodiceSurfaces[_garmentCut]=new(rest,(int[])indices.Clone(),(Vector2[])pattern.Clone(),(Vector3[])points.Clone());
        GD.Print($"BODICE_SURFACE_FLOW: cut={_garmentCut}, vertices={count}, local quadratic residual={Math.Sqrt(before/count)*1000:F3}/{Math.Sqrt(after/count)*1000:F3} mm, maximum edit={maximum*1000:F3} mm; local backtracking, fixed neck/cuff/apex/waist; shared sleeve root");
    }

    private int _posedBodiceSamples;
    private float _posedBodiceMaximum;

    private static void ValidateBodicePatches()
    {
        foreach(bool curved in new[]{false,true})
        {
            var points=new List<Vector3>{new(0,1.20f,-.076f)};
            var areas=new List<float>{.000001f};
            for(int x=-6;x<=6;x++)for(int y=-6;y<=6;y++)
            {
                float u=x*.004f,v=y*.004f;
                points.Add(new(u,1.20f+v,-.080f+.3f*u+.15f*v+(curved?7*u*u+3*u*v+2*v*v:0)));
                areas.Add(1);
            }
            // A zero-area remnant must not pull an otherwise well supported cap.
            points.Add(new(.005f,1.205f,-.040f));areas.Add(0);
            if(!TryBodicePatch(points.ToArray(),areas.ToArray(),0,out float depth)||MathF.Abs(depth+.080f)>.000002f)
                throw new InvalidOperationException("Bodice patch erases a broad plane/quadratic shape or follows a narrow outlier");
        }
        GD.Print("BODICE_PATCH_PASS: preserves planar/quadratic broad forms, rejects a narrow low-area spike and a zero-area remnant");
    }

    private static int[] BodiceCandidates(Vector3[] chart,int index)
    {
        const float radius=.035f;var root=chart[index];var candidates=new List<int>();
        for(int j=0;j<chart.Length;j++)
        {
            var p=chart[j];
            if(p.Z>=-.010f||MathF.Abs(p.Z-root.Z)>.045f)continue;
            float du=(p.X-root.X)/radius,dv=(p.Y-root.Y)/radius;
            if(du*du+dv*dv<1)candidates.Add(j);
        }
        return candidates.ToArray();
    }

    private static bool TryBodicePatch(Vector3[] points,float[] areas,int index,out float depth,Vector3[]? chart=null,int[]? candidates=null)
    {
        const float radius=.035f;var centre=points[index];
        Span<double> system=stackalloc double[42];Span<double> basis=stackalloc double[6];
        system.Clear();double total=0;int count=0;
        for(int n=0;n<(candidates?.Length??points.Length);n++)
        {
            int j=candidates==null?n:candidates[n];
            var p=points[j];var reference=chart==null?p:chart[j];var root=chart==null?centre:chart[index];
            if(reference.Z>=-.010f||MathF.Abs(reference.Z-root.Z)>.045f)continue;
            float du=(reference.X-root.X)/radius,dv=(reference.Y-root.Y)/radius;
            if(du*du+dv*dv>=1)continue;
            double u=(p.X-centre.X)/radius,v=(p.Y-centre.Y)/radius,r2=u*u+v*v;
            if(r2>=1||areas[j]<=0)continue;
            double weight=areas[j]*Math.Pow(1-r2,3);total+=weight;count++;
            basis[0]=1;basis[1]=u;basis[2]=v;basis[3]=u*u;basis[4]=u*v;basis[5]=v*v;
            for(int row=0;row<6;row++)
            {
                for(int col=0;col<6;col++)system[row*7+col]+=weight*basis[row]*basis[col];
                system[row*7+6]+=weight*basis[row]*(p.Z-centre.Z);
            }
        }
        depth=centre.Z;if(count<12||total<1e-10)return false;
        for(int k=1;k<6;k++)system[k*7+k]+=total*1e-6;
        for(int k=0;k<6;k++)
        {
            int pivot=k;for(int row=k+1;row<6;row++)if(Math.Abs(system[row*7+k])>Math.Abs(system[pivot*7+k]))pivot=row;
            if(Math.Abs(system[pivot*7+k])<total*1e-10)return false;
            for(int col=k;col<7;col++)(system[k*7+col],system[pivot*7+col])=(system[pivot*7+col],system[k*7+col]);
            double diagonal=system[k*7+k];for(int col=k;col<7;col++)system[k*7+col]/=diagonal;
            for(int row=0;row<6;row++)if(row!=k)
            {
                double factor=system[row*7+k];for(int col=k;col<7;col++)system[row*7+col]-=factor*system[k*7+col];
            }
        }
        depth=centre.Z+(float)system[6];return float.IsFinite(depth);
    }
}
