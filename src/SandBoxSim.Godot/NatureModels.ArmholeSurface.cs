using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record ArmholeRepair(Vector3[] Source,int[] Triangles,Vector2[] Pattern,Vector3[] Result);
    private readonly Dictionary<(int Frame,int Cut),ArmholeRepair> _armholeRepairs=new();
    // Repair the rest cap itself. Preserving a folded input triangle is not
    // a useful constraint for a newly tailored exterior cloth surface.
    private void RelaxArmholeSurface(Vector3[] points,int[] triangles,Vector2[] pattern)
    {
        var key=(BodyFrame,_garmentCut);
        if(_armholeRepairs.TryGetValue(key,out var cached)&&cached.Source.AsSpan().SequenceEqual(points)
            &&cached.Triangles.AsSpan().SequenceEqual(triangles)&&cached.Pattern.AsSpan().SequenceEqual(pattern))
        {Array.Copy(cached.Result,points,points.Length);return;}
        var cage=ShoulderSurface();var rest=(Vector3[])points.Clone();var goal=(Vector3[])points.Clone();
        var free=new float[points.Length];var adjacent=new HashSet<int>[points.Length];
        for(int i=0;i<points.Length;i++)adjacent[i]=new();
        for(int t=0;t<triangles.Length;t+=3)for(int k=0;k<3;k++)
        {int a=triangles[t+k],b=triangles[t+(k+1)%3];adjacent[a].Add(b);adjacent[b].Add(a);}
        var seamDistance=new float[points.Length];Array.Fill(seamDistance,float.PositiveInfinity);
        var queue=new PriorityQueue<int,float>();
        for(int i=0;i<points.Length;i++)if(pattern[i].X==0){seamDistance[i]=0;queue.Enqueue(i,0);}
        if(queue.Count==0)return;
        while(queue.TryDequeue(out int a,out float travelled))
        {
            if(travelled>seamDistance[a])continue;
            foreach(int b in adjacent[a])
            {
                float travelledNext=travelled+rest[a].DistanceTo(rest[b]);
                if(travelledNext>=seamDistance[b])continue;seamDistance[b]=travelledNext;queue.Enqueue(b,travelledNext);
            }
        }
        for(int i=0;i<points.Length;i++)
        {
            float along=pattern[i].X;
            free[i]=(along>=0?1-Mathf.SmoothStep(.13f,.21f,along):1-Mathf.SmoothStep(0,.045f,seamDistance[i]))
                *Mathf.SmoothStep(.045f,.075f,MathF.Abs(points[i].X))
                *Mathf.SmoothStep(1.02f,1.08f,points[i].Y)*(1-Mathf.SmoothStep(1.29f,1.33f,points[i].Y));
            if(free[i]<=0)continue;
            var p=new Vector3(MathF.Abs(points[i].X),points[i].Y,points[i].Z);
            float nearest=.07f*.07f;Vector3 hit=p,normal=Vector3.Zero;
            for(int t=0;t<cage.Triangles.Length;t+=3)
            {
                int ia=cage.Triangles[t],ib=cage.Triangles[t+1],ic=cage.Triangles[t+2];
                if(along>.045f&&(cage.Weights[ia].Y+cage.Weights[ib].Y+cage.Weights[ic].Y)/3<.20f)continue;
                var a=cage.Points[ia];var b=cage.Points[ib];var c=cage.Points[ic];
                if((c-a).Cross(b-a).LengthSquared()<1e-12f)continue;
                var q=ClosestShoulderPoint(p,a,b,c);float distance=p.DistanceSquaredTo(q);
                if(distance>=nearest)continue;
                nearest=distance;hit=q;normal=(c-a).Cross(b-a).Normalized();
            }
            if(normal.LengthSquared()<.5f){free[i]=0;continue;}
            var g=hit+normal*(BodyFrame==1?.006f:.008f);
            goal[i]=new(g.X*(points[i].X<0?-1:1),g.Y,g.Z);
            points[i]=points[i].Lerp(goal[i],.75f*free[i]);
        }
        var next=new Vector3[points.Length];
        for(int pass=0;pass<96;pass++)
        {
            for(int i=0;i<points.Length;i++)
            {
                next[i]=points[i];if(free[i]<=0||adjacent[i].Count==0)continue;
                var mean=Vector3.Zero;foreach(int j in adjacent[i])mean+=points[j];mean/=adjacent[i].Count;
                var candidate=points[i].Lerp(mean,.45f*free[i]).Lerp(goal[i],.02f*free[i]);
                // The rib panel owns the trunk exterior even beside a deep
                // axilla. A shoulder-only source must not pull cloth into it.
                if(pattern[i].X<=0&&candidate.Y<1.20f)
                {
                    float radius=new Vector2(candidate.X,candidate.Z).Length();
                    var support=BodyFrame==1?FemaleThoraxSupport():_trunkMale!;
                    float exterior=ClothEnvelope(support,candidate.Y,MathF.Atan2(candidate.Z,candidate.X));
                    float required=Mathf.Lerp(radius,MathF.Max(radius,exterior),1-Mathf.SmoothStep(1.16f,1.20f,candidate.Y));
                    if(radius<required&&radius>.00001f)candidate=new(candidate.X*required/radius,candidate.Y,candidate.Z*required/radius);
                }
                var delta=candidate-rest[i];float limit=.045f*free[i];
                if(delta.Length()>limit)delta=delta.Normalized()*limit;
                next[i]=rest[i]+delta;
            }
            Array.Copy(next,points,points.Length);
        }
        float maximumEdit=0;
        for(int i=0;i<points.Length;i++)
        {
            maximumEdit=MathF.Max(maximumEdit,points[i].DistanceTo(rest[i]));
            if(!points[i].IsFinite()||points[i].DistanceTo(rest[i])>.04501f*free[i]+.000001f
                ||free[i]==0&&points[i]!=rest[i])throw new InvalidOperationException("Armhole repair loses its fixed boundary or displacement bound");
        }
        var before=ArmholeCreases(rest,triangles,pattern);var after=ArmholeCreases(points,triangles,pattern);
        if(after.Sharp>Math.Max(8,before.Sharp*.40f))throw new InvalidOperationException($"Armhole repair retains folded input: {before.Sharp}/{after.Sharp}");
        _armholeRepairs[key]=new(rest,(int[])triangles.Clone(),(Vector2[])pattern.Clone(),(Vector3[])points.Clone());
        GD.Print($"ARMHOLE_REST_FLOW: frame={BodyFrame}, cut={_garmentCut}, edges={after.Edges}, sharp before/after={before.Sharp}/{after.Sharp}, maximum edit={maximumEdit:F5} m; shared seam relaxed, neck/centre/waist/cuff fixed");

    }
    private static (int Edges,int Sharp,float LongestInnerFold) ArmholeCreases(Vector3[] points,int[] triangles,Vector2[] pattern,Vector3[]? rest=null,int raisedSide=-1)
    {
        var normals=new Vector3[triangles.Length/3];var edges=new Dictionary<(int,int),List<int>>();
        for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];var n=(points[c]-points[a]).Cross(points[b]-points[a]);
            if(!n.IsFinite()||n.LengthSquared()<1e-20f)throw new InvalidOperationException("Armhole surface has a nonfinite or collapsed triangle");
            normals[t/3]=n.Normalized();
            for(int k=0;k<3;k++)
            {
                int x=triangles[t+k],y=triangles[t+(k+1)%3];var key=x<y?(x,y):(y,x);
                if(!edges.TryGetValue(key,out var faces))edges[key]=faces=new();faces.Add(t/3);
            }
        }
        int samples=0,sharp=0;float longestInnerFold=0;
        foreach(var edge in edges)
        {
            var (a,b)=edge.Key;if(edge.Value.Count!=2||pattern[a].X<0||pattern[b].X<0||pattern[a].X>=.20f||pattern[b].X>=.20f)continue;
            float turn=normals[edge.Value[0]].Dot(normals[edge.Value[1]]);
            if(turn<-.17364818f)sharp++;
            samples++;
            if(rest==null||turn>=-.70710678f||MathF.Min(pattern[a].X,pattern[b].X)<=.04f||MathF.Max(pattern[a].X,pattern[b].X)>=.14f)continue;
            var centre=(rest[a]+rest[b])*.5f;
            if((centre.X<0)==(raisedSide==0)&&MathF.Abs(centre.X)>.07f&&centre.Y>1.08f&&centre.Y<1.17f)
                longestInnerFold=MathF.Max(longestInnerFold,points[a].DistanceTo(points[b]));
        }
        return(samples,sharp,longestInnerFold);
    }
    private (int Edges,int Sharp,float LongestInnerFold,float WallFlare,int WallSamples) ArmholePoseCreases(ResidentRig rig,int raisedSide)
    {
        var arrays=rig.DetailedBodyMesh.SurfaceGetArrays(0);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var local=CorrectedSleeveSource(rig,p);var posed=new Vector3[p.Length];
        var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var palette=new Transform3D[ResidentRig.SupportedBodyBones];
        for(int i=0;i<palette.Length;i++)palette[i]=rig.ShoulderSkeleton.GetBonePose(i)*rig.DetailedBodySkin.GetBindPose(i);
        for(int i=0;i<posed.Length;i++)for(int j=0;j<4;j++)posed[i]+=(palette[bones[i*4+j]]*local[i])*weights[i*4+j];
        var pattern=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var creases=ArmholeCreases(posed,arrays[(int)Mesh.ArrayType.Index].AsInt32Array(),pattern,p,raisedSide);
        float flare=0,sign=raisedSide==0?-1:1;int samples=0;
        for(int i=0;i<p.Length;i++)
        {
            var rest=p[i];
            // Inspect the side of the finished bodice rather than the
            // movable seam or the front/rear volume of the axillary folds.
            if(pattern[i].X>=0||rest.X*sign<.100f||MathF.Abs(rest.Z)>.030f||rest.Y<1.090f||rest.Y>1.115f)continue;
            flare=MathF.Max(flare,sign*(posed[i].X-rest.X));samples++;
        }
        return(creases.Edges,creases.Sharp,creases.LongestInnerFold,flare,samples);
    }

}
