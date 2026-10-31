using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record SleeveDetail(Vector3[] Detail,int[] Anchors);

    // Rest-detail deformation smoothing, baked into pose shapes. Unlike a
    // normal-only ridge filter, this transports the original sewn detail in
    // a local surface frame after relaxing the deformed low-frequency shape.
    private static Vector3[] RelaxSleeve(Vector3[] points,(int Index,float Weight)[][] patches,float[] free)
    {
        var current=(Vector3[])points.Clone();var next=new Vector3[points.Length];
        for(int pass=0;pass<6;pass++)
        {
            for(int i=0;i<points.Length;i++)
            {
                next[i]=current[i];if(free[i]<=0||patches[i].Length==0)continue;
                var mean=Vector3.Zero;foreach(var (j,w) in patches[i])mean+=current[j]*w;
                next[i]=current[i].Lerp(mean,.45f*free[i]);
            }
            (current,next)=(next,current);
        }
        return current;
    }
    private static Basis SleeveDetailFrame(Vector3 normal,Vector3 edge)
    {
        var tangent=edge-normal*edge.Dot(normal);
        if(tangent.LengthSquared()<1e-12f)
        {
            var axis=MathF.Abs(normal.X)<.8f?Vector3.Right:Vector3.Up;
            tangent=axis-normal*axis.Dot(normal);
        }
        tangent=tangent.Normalized();return new(tangent,normal.Cross(tangent).Normalized(),normal);
    }
    private static SleeveDetail BindSleeveDetail(Vector3[] source,int[] triangles,(int Index,float Weight)[][] patches,float[] free)
    {
        var rest=RelaxSleeve(source,patches,free);var normals=ClothSurfaceNormals(rest,triangles);
        var detail=new Vector3[source.Length];var anchors=new int[source.Length];
        for(int i=0;i<source.Length;i++)
        {
            int anchor=i;float longest=0;
            foreach(var (j,_) in patches[i])
            {
                var edge=rest[j]-rest[i];float length=(edge-normals[i]*edge.Dot(normals[i])).LengthSquared();
                if(length>longest){longest=length;anchor=j;}
            }
            anchors[i]=anchor;var frame=SleeveDetailFrame(normals[i],rest[anchor]-rest[i]);
            detail[i]=frame.Inverse()*(source[i]-rest[i]);
        }
        return new(detail,anchors);
    }
    private static Vector3[] RestoreSleeveDetail(Vector3[] posed,int[] triangles,(int Index,float Weight)[][] patches,float[] free,SleeveDetail binding)
    {
        var smooth=RelaxSleeve(posed,patches,free);var normals=ClothSurfaceNormals(smooth,triangles);
        var result=(Vector3[])posed.Clone();
        for(int i=0;i<posed.Length;i++)if(free[i]>0)
        {
            var frame=SleeveDetailFrame(normals[i],smooth[binding.Anchors[i]]-smooth[i]);
            var candidate=smooth[i]+frame*binding.Detail[i];var change=candidate-posed[i];
            // A smoothing pass must not replace the body fit or pull the
            // armhole away from its anatomical support. Existing bind/edge
            // constraints run on the resulting shape afterwards.
            if(change.Length()>.018f)change=change.Normalized()*.018f;
            result[i]+=change*free[i];
        }
        return result;
    }
    private static void ValidateSleeveDetail()
    {
        const int size=9;var source=new Vector3[size*size];var free=new float[source.Length];var ids=new List<int>();
        var patches=new (int Index,float Weight)[source.Length][];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            int i=y*size+x;source[i]=new(x*.01f,y*.01f,.001f*MathF.Sin(x*.7f)*MathF.Sin(y*.6f));
            free[i]=x>0&&x<size-1&&y>0&&y<size-1?1:0;
            if(x<size-1&&y<size-1){ids.AddRange(new[]{i,i+size,i+1,i+1,i+size,i+size+1});}
        }
        for(int i=0;i<source.Length;i++)
        {
            var patch=new List<(int,float)>();float sum=0;
            for(int j=0;j<source.Length;j++)
            {
                float distance=source[i].DistanceTo(source[j]);if(distance>=.025f)continue;
                float w=MathF.Pow(1-distance/.025f,2);patch.Add((j,w));sum+=w;
            }
            for(int j=0;j<patch.Count;j++)patch[j]=(patch[j].Item1,patch[j].Item2/sum);patches[i]=patch.ToArray();
        }
        var triangles=ids.ToArray();var bind=BindSleeveDetail(source,triangles,patches,free);
        var rest=RestoreSleeveDetail(source,triangles,patches,free,bind);
        var rigid=new Transform3D(new Basis(new Quaternion(new Vector3(1,2,3).Normalized(),1.1f)),new(.3f,-.2f,.1f));
        var moved=Array.ConvertAll(source,p=>rigid*p);var result=RestoreSleeveDetail(moved,triangles,patches,free,bind);
        float error=0;
        for(int i=0;i<source.Length;i++)
        {
            error=MathF.Max(error,MathF.Max(rest[i].DistanceTo(source[i]),result[i].DistanceTo(moved[i])));
            if(free[i]==0&&result[i]!=moved[i])throw new InvalidOperationException("Sleeve detail smoother moves a pinned boundary");
        }
        var creased=(Vector3[])source.Clone();
        for(int y=2;y<size-2;y++)for(int x=2;x<size-2;x++)creased[y*size+x].Z+=(x%2==0?1:-1)*.008f;
        var relaxed=RestoreSleeveDetail(creased,triangles,patches,free,bind);
        float Energy(Vector3[] p){float total=0;for(int y=2;y<size-2;y++)for(int x=2;x<size-2;x++){int i=y*size+x;float d=p[i-1].Z-2*p[i].Z+p[i+1].Z;total+=d*d;}return total;}
        float before=Energy(creased),after=Energy(relaxed);
        if(error>.000002f||after>=before*.9f)throw new InvalidOperationException($"Sleeve rest-detail restoration fails rigid invariance or crease reduction: {error}/{before}/{after}");
        GD.Print($"SLEEVE_DETAIL_PASS: rest/rigid error={error:F8} m, pinned borders, induced crease energy={before:F6} -> {after:F6}; original rest detail transported");
    }
}
