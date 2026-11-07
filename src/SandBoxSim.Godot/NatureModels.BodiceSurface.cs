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
                *Mathf.SmoothStep(1.09f,1.14f,p.Y)*(1-Mathf.SmoothStep(1.25f,1.29f,p.Y))
                *Mathf.SmoothStep(.015f,.040f,-p.Z);
            float neck=(1-Mathf.SmoothStep(.075f,.095f,MathF.Abs(p.X)))*Mathf.SmoothStep(1.20f,1.235f,p.Y);
            active[i]*=1-neck;
            if(pattern[i].X<0)
            {
                // Continue the patch across the diagonal front yoke. A
                // narrow remnant between collar and shoulder should follow
                // the chest plane, while the sewn opening stays pinned.
                float x=MathF.Abs(p.X);
                float yoke=TorsoRamp(.034f,.052f,x)*(1-TorsoRamp(.083f,.118f,x))
                    *TorsoRamp(1.219f,1.239f,p.Y)*(1-TorsoRamp(1.269f,1.282f,p.Y))
                    *TorsoRamp(.023f,.044f,-p.Z);
                active[i]=MathF.Max(active[i],yoke);
            }
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
        ApplyBodiceDepth(points,rest,indices,delta);
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
        FitFemaleFrontDrape(points,indices,pattern);
        _bodiceSurfaces[_garmentCut]=new(rest,(int[])indices.Clone(),(Vector2[])pattern.Clone(),(Vector3[])points.Clone());
        GD.Print($"BODICE_SURFACE_FLOW: cut={_garmentCut}, vertices={count}, local quadratic residual={Math.Sqrt(before/count)*1000:F3}/{Math.Sqrt(after/count)*1000:F3} mm, maximum edit={maximum*1000:F3} mm; local backtracking, fixed neck/cuff/apex/waist; shared sleeve root");
    }

    private void FitFemaleFrontDrape(Vector3[] points,int[] indices,Vector2[] pattern)
    {
        // Solve on the sewn exterior in X/Y metres. Rays in the input
        // envelope have unequal spacing across the chest and side wall.
        const int firstRow=58,rows=37,columns=41; // 1.030–1.210 m above the sole
        const float step=.005f,halfWidth=.100f;
        float bottom=TrunkLow+firstRow*step;
        var skin=FemaleThoraxSupport();
        var section=new Vector2[TrunkColumns];
        var before=new float[rows*columns];var lower=new float[before.Length];var upper=new float[before.Length];
        for(int r=0;r<rows;r++)
        {
            float y=bottom+r*step;
            for(int a=0;a<TrunkColumns;a++)
            {
                float angle=a*Mathf.Tau/TrunkColumns;
                section[a]=new Vector2(MathF.Cos(angle),MathF.Sin(angle))*skin[(firstRow+r)*TrunkColumns+a];
            }
            for(int c=0;c<columns;c++)
            {
                int i=r*columns+c;float x=c*step-halfWidth;
                float depth=before[i]=-GarmentDepth(points,indices,x,y,false);
                if(!float.IsFinite(depth))throw new InvalidOperationException($"Missing finished front-panel section at {x:F3}/{y:F3}");
                float skinDepth=float.NegativeInfinity;
                for(int j=0;j<TrunkColumns;j++)
                {
                    var a=section[j];var b=section[(j+1)%TrunkColumns];
                    if((x-a.X)*(x-b.X)>0||MathF.Abs(b.X-a.X)<1e-8f)continue;
                    skinDepth=MathF.Max(skinDepth,-Mathf.Lerp(a.Y,b.Y,(x-a.X)/(b.X-a.X)));
                }
                float mask=TorsoRamp(1.035f,1.063f,y)*(1-TorsoRamp(1.170f,1.197f,y))
                    *(1-TorsoRamp(.076f,.098f,MathF.Abs(x)));
                lower[i]=MathF.Max(MathF.Min(depth,skinDepth+.002f),depth-.005f*mask);
                upper[i]=depth+.004f*mask;
            }
        }
        var fitted=(float[])before.Clone();
        const float bending=36;
        for(int pass=0;pass<80;pass++)for(int sweep=0;sweep<2;sweep++)
        for(int y=2;y<rows-2;y++)for(int x=2;x<columns-2;x++)
        {
            int r=sweep==0?y:rows-1-y,c=sweep==0?x:columns-1-x,i=r*columns+c;
            if(lower[i]>=upper[i])continue;
            float neighbour=4*(fitted[i-1]+fitted[i+1]+fitted[i-columns]+fitted[i+columns]);
            float second=fitted[i-2]+fitted[i+2]+fitted[i-2*columns]+fitted[i+2*columns];
            fitted[i]=Math.Clamp((before[i]+bending*(neighbour-second))/(1+12*bending),lower[i],upper[i]);
        }
        var delta=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var p=points[i];
            float row=(p.Y-bottom)/step,col=(p.X+halfWidth)/step;
            if(pattern[i].X>=0||p.Z>=-.025f||row<=0||row>=rows-1||col<=0||col>=columns-1)continue;
            int r=(int)row,c=(int)col,a=r*columns+c;
            float x=col-c,y=row-r;
            float Sample(float[] values)=>Mathf.Lerp(Mathf.Lerp(values[a],values[a+1],x),
                Mathf.Lerp(values[a+columns],values[a+columns+1],x),y);
            float original=Sample(before);
            // The projected chart belongs to the front exterior only.
            float exterior=1-TorsoRamp(.002f,.012f,p.Z+original);
            delta[i]=(original-Sample(fitted))*exterior;
        }
        ApplyBodiceDepth(points,(Vector3[])points.Clone(),indices,delta);
    }

    private static void FitFemaleRearDrape(Vector3[] points,int[] indices,Vector2[] pattern)
    {
        const int columns=25;
        const float spacing=.006f,halfWidth=.072f,probe=.006f;
        var lower=new Vector2[columns];
        var upper=new Vector2[columns];
        var samples=new Vector4[columns];
        for(int col=0;col<columns;col++)
        {
            float x=col*spacing-halfWidth;
            float shoulder=TorsoRamp(.018f,.120f,MathF.Abs(x));
            float low=1.169f+.010f*shoulder,high=1.282f-.034f*shoulder;
            float Depth(float height)=>GarmentDepth(points,indices,x,height,true);
            lower[col]=new(low,Depth(low));
            upper[col]=new(high,Depth(high));
            samples[col]=new(Depth(low-probe),Depth(low+probe),Depth(high-probe),Depth(high+probe));
        }

        var delta=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var p=points[i];
            if(pattern[i].X>=0||p.Z<.020f||MathF.Abs(p.X)>=halfWidth||p.Y<1.169f||p.Y>=1.282f)continue;
            float column=Math.Clamp((p.X+halfWidth)/spacing,0,columns-1);
            int col=Math.Min((int)column,columns-2);
            float fraction=column-col;
            var a=lower[col].Lerp(lower[col+1],fraction);
            var b=upper[col].Lerp(upper[col+1],fraction);
            var depths=samples[col].Lerp(samples[col+1],fraction);
            if(!a.IsFinite()||!b.IsFinite()||!depths.IsFinite()||p.Y<=a.X||p.Y>=b.X)continue;
            float length=b.X-a.X,t=(p.Y-a.X)/length,t2=t*t,t3=t2*t;
            float startSlope=Math.Clamp((depths.Y-depths.X)/(2*probe),-.60f,.38f);
            float endSlope=Math.Clamp((depths.W-depths.Z)/(2*probe),-.85f,.30f);
            // Bridge the collar and upper back along the cloth grain, with
            // tangents from the actual two supports. Keep the sewn neckline
            // and outer shoulder fixed as the depth fit fades out.
            float target=(2*t3-3*t2+1)*a.Y+(t3-2*t2+t)*length*startSlope
                +(-2*t3+3*t2)*b.Y+(t3-t2)*length*endSlope;
            float band=TorsoRamp(0,.22f,t)*(1-TorsoRamp(.78f,1,t));
            float side=1-TorsoRamp(.043f,.068f,MathF.Abs(p.X));
            delta[i]=Math.Clamp(target-p.Z,-.010f,.003f)*band*side*TorsoRamp(.020f,.042f,p.Z);
        }
        ApplyBodiceDepth(points,(Vector3[])points.Clone(),indices,delta);
    }

    private static void ApplyBodiceDepth(Vector3[] points,Vector3[] rest,int[] indices,float[] delta)
    {
        // A slender cap triangle must not undo the whole panel edit.
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
            if(valid)return;
        }
        throw new InvalidOperationException("Bodice depth fit folds its repaired input");
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
        const float radius=.045f;var root=chart[index];var candidates=new List<int>();
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
        const float radius=.045f;var centre=points[index];
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
