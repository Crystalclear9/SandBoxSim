using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static Godot.Collections.Array RefineFrontTriangles(Godot.Collections.Array arrays)
    {
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var split=new HashSet<(int,int)>();
        static (int,int) Edge(int a,int b)=>a<b?(a,b):(b,a);
        for(int t=0;t<ids.Length;t+=3)
        {
            int a=ids[t],b=ids[t+1],c=ids[t+2];
            if(uv[a].X<.5f||uv[b].X<.5f||uv[c].X<.5f)continue;
            if(MathF.Max(p[a].Y,MathF.Max(p[b].Y,p[c].Y))<1.07f||MathF.Min(p[a].Y,MathF.Min(p[b].Y,p[c].Y))>1.22f
                ||MathF.Min(p[a].X,MathF.Min(p[b].X,p[c].X))>.09f||MathF.Max(p[a].X,MathF.Max(p[b].X,p[c].X))<-.09f
                ||(p[a].Z+p[b].Z+p[c].Z)/3>-.06f)continue;
            split.Add(Edge(a,b));split.Add(Edge(b,c));split.Add(Edge(c,a));
        }
        if(split.Count==0)return arrays;
        var positions=new List<Vector3>(p);
        var normals=new List<Vector3>(arrays[(int)Mesh.ArrayType.Normal].AsVector3Array());
        var texture=new List<Vector2>(uv);
        var chart=new List<Vector2>(arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array());
        var colours=new List<Color>(arrays[(int)Mesh.ArrayType.Color].AsColorArray());
        var midpoint=new Dictionary<(int,int),int>();var triangles=new List<int>();
        int Mid(int a,int b)
        {
            var edge=Edge(a,b);if(midpoint.TryGetValue(edge,out int existing))return existing;
            int result=positions.Count;midpoint.Add(edge,result);
            positions.Add((p[a]+p[b])*.5f);normals.Add((normals[a]+normals[b]).Normalized());
            texture.Add((uv[a]+uv[b])*.5f);chart.Add((chart[a]+chart[b])*.5f);colours.Add(colours[a].Lerp(colours[b],.5f));
            return result;
        }
        void Tri(int a,int b,int c){triangles.Add(a);triangles.Add(b);triangles.Add(c);}
        for(int t=0;t<ids.Length;t+=3)
        {
            int a=ids[t],b=ids[t+1],c=ids[t+2];
            int mask=(split.Contains(Edge(a,b))?1:0)|(split.Contains(Edge(b,c))?2:0)|(split.Contains(Edge(c,a))?4:0);
            int ab=(mask&1)!=0?Mid(a,b):-1,bc=(mask&2)!=0?Mid(b,c):-1,ca=(mask&4)!=0?Mid(c,a):-1;
            switch(mask)
            {
                case 0:Tri(a,b,c);break;
                case 1:Tri(a,ab,c);Tri(ab,b,c);break;
                case 2:Tri(b,bc,a);Tri(bc,c,a);break;
                case 4:Tri(c,ca,b);Tri(ca,a,b);break;
                case 3:Tri(b,bc,ab);Tri(a,ab,c);Tri(ab,bc,c);break;
                case 6:Tri(c,ca,bc);Tri(b,bc,a);Tri(bc,ca,a);break;
                case 5:Tri(a,ab,ca);Tri(c,ca,b);Tri(ca,ab,b);break;
                case 7:Tri(a,ab,ca);Tri(ab,b,bc);Tri(ca,bc,c);Tri(ab,bc,ca);break;
            }
        }
        // Shared edge IDs and green neighbour splits avoid T-junctions.
        // This is a static distant batch; GPU index LODs are rebuilt afterwards.
        arrays[(int)Mesh.ArrayType.Vertex]=positions.ToArray();arrays[(int)Mesh.ArrayType.Normal]=normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV]=texture.ToArray();arrays[(int)Mesh.ArrayType.TexUV2]=chart.ToArray();
        arrays[(int)Mesh.ArrayType.Color]=colours.ToArray();arrays[(int)Mesh.ArrayType.Index]=triangles.ToArray();
        arrays[(int)Mesh.ArrayType.Bones]=default(Variant);arrays[(int)Mesh.ArrayType.Weights]=default(Variant);
        return arrays;
    }
    // The distant torso has its own lower-density topology. Carry the finished
    // front cloth into it after tailoring, rather than reverting to the old
    // construction cage when the camera moves away. This runs once per cache.
    private Mesh MatchDistantFrontPanel(Mesh distant,Mesh detailed)
    {
        string key="front-panel:"+distant.GetInstanceId()+":"+detailed.GetInstanceId();
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var near=detailed.SurfaceGetArrays(0);
        var vertices=near[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=near[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var triangles=near[(int)Mesh.ArrayType.Index].AsInt32Array();
        var result=new ArrayMesh();
        for(int surface=0;surface<distant.GetSurfaceCount();surface++)
        {
            var arrays=RefineFrontTriangles(distant.SurfaceGetArrays(surface));
            var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var chart=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            var rest=(Vector3[])p.Clone();
            for(int i=0;i<p.Length;i++)
            {
                // UV.x is the existing fitted-cloth flag, independent of dye.
                // Accessories in the same distant batch keep their own fit.
                if(uv.Length!=p.Length||uv[i].X<.5f)continue;
                float freedom=Mathf.SmoothStep(.045f,.090f,-p[i].Z)
                    // Stop before the axilla: its shoulder/sleeve topology
                    // has a different support and is not a front-facing span.
                    *(1-Mathf.SmoothStep(.080f,.110f,MathF.Abs(p[i].X)))
                    *Mathf.SmoothStep(1.04f,1.09f,p[i].Y)*(1-Mathf.SmoothStep(1.19f,1.24f,p[i].Y));
                if(freedom<=0)continue;
                float depth=float.PositiveInfinity;Vector3 normal=Vector3.Zero;
                for(int t=0;t<triangles.Length;t+=3)
                {
                    int ia=triangles[t],ib=triangles[t+1],ic=triangles[t+2];
                    var a=vertices[ia];var b=vertices[ib];var c=vertices[ic];
                    if(p[i].Y<MathF.Min(a.Y,MathF.Min(b.Y,c.Y))||p[i].Y>MathF.Max(a.Y,MathF.Max(b.Y,c.Y))
                        ||p[i].X<MathF.Min(a.X,MathF.Min(b.X,c.X))||p[i].X>MathF.Max(a.X,MathF.Max(b.X,c.X)))continue;
                    var ab=new Vector2(b.X-a.X,b.Y-a.Y);var ac=new Vector2(c.X-a.X,c.Y-a.Y);
                    float determinant=ab.Cross(ac);if(MathF.Abs(determinant)<1e-10f)continue;
                    var q=new Vector2(p[i].X-a.X,p[i].Y-a.Y);
                    float v=q.Cross(ac)/determinant,w=ab.Cross(q)/determinant;
                    if(v<-.00001f||w<-.00001f||v+w>1.00001f)continue;
                    float z=a.Z*(1-v-w)+b.Z*v+c.Z*w;
                    if(z>=depth)continue;
                    depth=z;normal=normals[ia]*(1-v-w)+normals[ib]*v+normals[ic]*w;
                }
                if(!float.IsFinite(depth)||MathF.Abs(depth-p[i].Z)*freedom>.012f||normal.LengthSquared()<1e-12f)
                    throw new InvalidOperationException($"Distant front panel loses its finished cloth support: cut={_garmentCut}, p={p[i]}, target={depth}, normal={normal.LengthSquared()}, freedom={freedom}");
                p[i].Z=Mathf.Lerp(p[i].Z,depth,freedom);
                n[i]=n[i].Lerp(normal.Normalized(),freedom).Normalized();
                if(chart.Length==p.Length&&chart[i].X<0)chart[i]=new(-2-MathF.Atan2(p[i].Z,p[i].X)/Mathf.Tau,p[i].Y);
            }
            var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int t=0;t<indices.Length;t+=3)
            {
                int a=indices[t],b=indices[t+1],c=indices[t+2];
                var before=(rest[c]-rest[a]).Cross(rest[b]-rest[a]);
                var after=(p[c]-p[a]).Cross(p[b]-p[a]);
                if(before.LengthSquared()>1e-20f&&before.Dot(after)<=0)
                    throw new InvalidOperationException("Distant front panel reverses a source triangle");
            }
            arrays[(int)Mesh.ArrayType.Vertex]=p;arrays[(int)Mesh.ArrayType.Normal]=n;
            arrays[(int)Mesh.ArrayType.TexUV2]=chart;
            arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            result.SurfaceSetMaterial(surface,distant.SurfaceGetMaterial(surface));
        }
        return _meshes[key]=WithScreenLods(result);
    }
}
