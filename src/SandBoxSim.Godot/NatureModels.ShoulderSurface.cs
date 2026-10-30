using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private sealed record ShoulderCage(Vector3[] Points,int[] Triangles,Vector3[] Source,Vector4[] Weights,Vector3 Shoulder,Quaternion ArmBind);
    private readonly ShoulderCage?[] _shoulderSurfaces=new ShoulderCage?[2];
    private ShoulderCage ShoulderSurface()
    {
        int frame=BodyFrame==1?1:0;if(_shoulderSurfaces[frame] is {} cached)return cached;
        string name=frame==0?"male":"female",raw=FileAccess.GetFileAsString("res://assets/models/human/shoulder-surface-"+name+".json").Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=(frame==0?"e0e716f8a47881a6a745af8ea7f2de8c7ac5cae630eac9ba7ac119879d8ddcc7":"5f16464a98ffd5336808ccbfbacbd4df360d90b48f316fefc725471fd76a995a"))throw new InvalidOperationException("Changed anatomical shoulder surface");
        using var json=JsonDocument.Parse(raw);var root=json.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=2||root.GetProperty("license").GetString()!="CC0-1.0")throw new InvalidOperationException("Invalid shoulder surface contract");
        var vertices=new List<Vector3>();var indices=new List<int>();
        foreach(var p in root.GetProperty("vertices").EnumerateArray())
        {
            var point=new Vector3(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle());
            if(!point.IsFinite())throw new InvalidOperationException("Invalid shoulder source vertex");vertices.Add(point);
        }
        foreach(var triangle in root.GetProperty("triangles").EnumerateArray())foreach(var i in triangle.EnumerateArray())
        {int index=i.GetInt32();if(index<0||index>=vertices.Count)throw new InvalidOperationException("Invalid shoulder source index");indices.Add(index);}
        if(vertices.Count<500||indices.Count<3000||indices.Count%3!=0)throw new InvalidOperationException("Incomplete shoulder source surface");
        var source=new List<Vector3>();var weights=new List<Vector4>();
        foreach(var p in root.GetProperty("aPoseVertices").EnumerateArray())source.Add(new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle()));
        foreach(var p in root.GetProperty("skinWeights").EnumerateArray())
        {
            var w=new Vector4(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle(),p[3].GetSingle());
            if(!w.IsFinite()||MathF.Abs(w.X+w.Y+w.Z+w.W-1)>.00001f||w.X<0||w.Y<0||w.Z<0||w.W<0)throw new InvalidOperationException("Invalid authored shoulder weights");weights.Add(w);
        }
        if(source.Count!=vertices.Count||weights.Count!=vertices.Count)throw new InvalidOperationException("Shoulder cage skin topology differs");
        var s=root.GetProperty("shoulder");var bind=root.GetProperty("armBind");
        var result=new ShoulderCage(vertices.ToArray(),indices.ToArray(),source.ToArray(),weights.ToArray(),new(s[0].GetSingle(),s[1].GetSingle(),s[2].GetSingle()),new Quaternion(bind[0].GetSingle(),bind[1].GetSingle(),bind[2].GetSingle(),bind[3].GetSingle()).Normalized());
        _shoulderSurfaces[frame]=result;return result;
    }
    private static Vector3 ClosestShoulderPoint(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=ab.Dot(ap),d2=ac.Dot(ap);if(d1<=0&&d2<=0)return a;
        var bp=p-b;float d3=ab.Dot(bp),d4=ac.Dot(bp);if(d3>=0&&d4<=d3)return b;
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        var cp=p-c;float d5=ab.Dot(cp),d6=ac.Dot(cp);if(d6>=0&&d5<=d6)return c;
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/(d4-d3+d5-d6));
        float denominator=1/(va+vb+vc);return a+ab*(vb*denominator)+ac*(vc*denominator);
    }
    private void FitShoulderSurface(Vector3[] points,int[] triangles,Vector2[] pattern,float[] sleeveDistance)
    {
        var cage=ShoulderSurface();var normals=ClothSurfaceNormals(points,triangles);var offsets=new Vector3[points.Length];var free=new bool[points.Length];
        float shoulder=.202f*ArmWidthScale;int fitted=0,candidates=0;
        for(int i=0;i<points.Length;i++)
        {
            var p=points[i];float sign=p.X<0?-1:1;
            float mask=Mathf.SmoothStep(shoulder-.075f,shoulder-.025f,MathF.Abs(p.X))*Mathf.SmoothStep(1.06f,1.14f,p.Y)
                *(1-Mathf.SmoothStep(1.30f,1.34f,p.Y))*(1-Mathf.SmoothStep(.09f,.18f,sleeveDistance[i]));
            if(mask<=0||pattern[i].X>.20f)continue;
            candidates++;
            var sample=new Vector3(MathF.Abs(p.X),p.Y,p.Z);var normal=new Vector3(normals[i].X*sign,normals[i].Y,normals[i].Z);
            float nearest=.08f*.08f;var hit=Vector3.Zero;var surfaceNormal=Vector3.Zero;bool found=false;
            for(int t=0;t<cage.Triangles.Length;t+=3)
            {
                var a=cage.Points[cage.Triangles[t]];var b=cage.Points[cage.Triangles[t+1]];var c=cage.Points[cage.Triangles[t+2]];
                var n=(c-a).Cross(b-a).Normalized();if(n.Dot(normal)<.20f)continue;
                var closest=ClosestShoulderPoint(sample,a,b,c);float distance=closest.DistanceSquaredTo(sample);
                if(distance>=nearest)continue;nearest=distance;hit=closest;surfaceNormal=n;found=true;
            }
            if(!found)continue;
            // Fit along the cloth normal: preserve tangential panel layout,
            // use an actual axilla/deltoid surface rather than a convex tube.
            float amount=Math.Clamp((hit+surfaceNormal*.009f-sample).Dot(normal),-.025f,.025f)*mask;
            offsets[i]=normals[i]*amount;free[i]=true;fitted++;
        }
        if(candidates==0)return;
        if(fitted<candidates*.5f)throw new InvalidOperationException($"Anatomical shoulder surface does not cover the garment: frame={BodyFrame}, vertices={points.Length}, candidates={candidates}, fitted={fitted}");
        var edges=new HashSet<(int A,int B)>();
        for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
        {int a=triangles[i+j],b=triangles[i+(j+1)%3];edges.Add(a<b?(a,b):(b,a));}
        // The dense source cap contains sub-millimetre edges. Limit the fit's
        // displacement gradient before moving any vertices, including across
        // the shared torso/sleeve boundary; centre, neck and cuffs stay fixed.
        for(int pass=0;pass<120;pass++)foreach(var (a,b) in edges)
        {
            var difference=offsets[a]-offsets[b];float distance=difference.Length(),limit=points[a].DistanceTo(points[b])*.24f;
            if(distance<=limit||distance<.0000001f)continue;
            float wa=free[a]?1:0,wb=free[b]?1:0;if(wa+wb==0)continue;
            var correction=difference*((distance-limit)/distance/(wa+wb));offsets[a]-=correction*wa;offsets[b]+=correction*wb;
        }
        // Avoid folding slender source triangles while preserving all source
        // indices and the existing rolled-cuff attachment.
        for(int pass=0;pass<12;pass++)for(int i=0;i<triangles.Length;i+=3)
        {
            int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var before=(points[c]-points[a]).Cross(points[b]-points[a]);
            var after=(points[c]+offsets[c]-points[a]-offsets[a]).Cross(points[b]+offsets[b]-points[a]-offsets[a]);
            if(before.Dot(after)>=.25f*before.LengthSquared())continue;
            offsets[a]*=.5f;offsets[b]*=.5f;offsets[c]*=.5f;
        }
        for(int i=0;i<points.Length;i++)points[i]+=offsets[i];
    }
    private int _posedShoulderSamples;
    private double _posedShoulderBefore,_posedShoulderAfter;
    private int _protrudingCapSamples;
    private double _protrudingCapBefore,_protrudingCapAfter;
    private (Vector3[] Goals,Vector3[] Normals,float[] Active) FitPosedShoulder(Vector3[] rest,Vector3[] posed,Vector3[] target,int[] triangles,Vector2[] uv,float[] active,ResidentRig rig,int side,Quaternion rotation,Transform3D girdle,ShoulderBinding[] bindings)
    {
        var cage=ShoulderSurface();float sign=side==0?-1:1;
        var skin=PoseShoulderSupport(rig,side,rotation,girdle);
        var trunk=PoseShoulderSupport(rig,side,rotation,girdle,true);
        var goals=new Vector3[rest.Length];var normals=ClothSurfaceNormals(posed,triangles);var accepted=new float[rest.Length];
        for(int i=0;i<rest.Length;i++)
        {
            if((rest[i].X<0)!=(side==0)||active[i]<=0||uv[i].X>.17f)continue;
            float mask=active[i]*(uv[i].X>=0?1-Mathf.SmoothStep(.11f,.17f,uv[i].X):1);if(mask<=0)continue;
            var binding=bindings[i];if(binding.Triangle<0)continue;int t=binding.Triangle;
            // A chest panel touching the resting arm is not attached to its
            // humerus. Retain the same surface correspondence while transferring
            // humeral participation only into the sleeve, across the sewn seam.
            float armParticipation=uv[i].X<0?0:Mathf.SmoothStep(.01f,.08f,uv[i].X);
            Vector3 Point(int index)=>trunk[index].Lerp(skin[index],armParticipation);
            var a=Point(cage.Triangles[t]);var b=Point(cage.Triangles[t+1]);var c=Point(cage.Triangles[t+2]);
            if((b-a).Cross(c-a).LengthSquared()<1e-15f)throw new InvalidOperationException("Anatomical shoulder support triangle collapsed");
            var goal=a*binding.Barycentric.X+b*binding.Barycentric.Y+c*binding.Barycentric.Z+ShoulderTriangleFrame(a,b,c)*binding.Offset;
            goals[i]=new(goal.X*sign,goal.Y,goal.Z);accepted[i]=mask;
            // Bind once in rest space. The same source triangle and cloth
            // offset are transported through every pose; no closest-surface
            // reassignment can slide between deltoid, axilla and chest.
            var change=goals[i]-posed[i];
            if(change.Length()>.10f)change=change.Normalized()*.10f;
            target[i]=posed[i]+change*mask;
        }
        return(goals,normals,accepted);
    }
}
