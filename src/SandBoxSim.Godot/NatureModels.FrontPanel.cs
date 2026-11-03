using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
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
            var arrays=distant.SurfaceGetArrays(surface);
            var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
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
            arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            result.SurfaceSetMaterial(surface,distant.SurfaceGetMaterial(surface));
        }
        return _meshes[key]=WithScreenLods(result);
    }
}
