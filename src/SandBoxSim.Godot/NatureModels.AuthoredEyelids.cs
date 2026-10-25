using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void InstallAuthoredEyelids(AnimalRig rig,bool wolf)
    {
        string key="authored-lid-material:"+wolf;
        if(!_residentMaterials.TryGetValue(key,out var shared))
        {
            var material=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform sampler2D coat_albedo:source_color,filter_linear_mipmap_anisotropic;
uniform bool predator=false;uniform float blink=0.0;
"+CoatGradingShader+@"
void fragment(){
// Sweep two curved skin margins over the source eye surface. No flattening
// of the eye, no detached spherical cap, and no visible shell while open.
vec2 p=UV2;
float open=1.0-clamp(blink,0.0,1.0);
float upper=(1.5-.20*p.x*p.x)*open-.18*blink;
float lower=(-1.5+.16*p.x*p.x)*open-.18*blink;
if(blink<.001||(p.y<upper&&p.y>lower))discard;
ALBEDO=coat_grade(texture(coat_albedo,UV).rgb,predator)*(predator?.48:.90);
float edge_distance=min(abs(p.y-upper),abs(p.y-lower));
float margin=1.0-smoothstep(.012,.036+fwidth(p.y),edge_distance);
ALBEDO*=1.0-margin*.30;
// A closed wolf lid has a dark horizontal lash margin; the source atlas
// contains a painted eye, so unmodified white brow fur reads as a second eye.
float crease=1.0-smoothstep(.035,.10+fwidth(p.y),abs(p.y+.18));
ALBEDO*=1.0-crease*.70*smoothstep(.82,.99,blink);
ROUGHNESS=.87;SPECULAR=.18;
}"}};
            material.SetShaderParameter("coat_albedo",GD.Load<Texture2D>(AuthoredRoot(wolf)+(wolf?"wolf-grey.png":"deer-head.png")));
            material.SetShaderParameter("predator",wolf);material.SetShaderParameter("blink",0f);
            _residentMaterials[key]=shared=material;
        }
        rig.LidMaterial=(ShaderMaterial)shared.Duplicate(false);
        var lids=Sculpt(rig.Head,AuthoredEyelidShell(wolf),Vector3.Zero,rig.LidMaterial);
        lids.Name="AuthoredEyelids";lids.VisibilityRangeEnd=20;lids.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        lids.Visible=false;rig.AuthoredEyelids=lids;
    }

    private Mesh AuthoredEyelidShell(bool wolf)
    {
        string key="authored-lid-shell:"+wolf;if(_meshes.TryGetValue(key,out var cached))return cached;
        var data=AuthoredAnimalData(wolf);var sources=new List<(Vector3[] P,Vector3[] N,Vector2[] U,int[] I)>();
        var skin=data.Mesh.SurfaceGetArrays(wolf?0:1);
        var skinP=skin[(int)Mesh.ArrayType.Vertex].AsVector3Array();var skinU=skin[(int)Mesh.ArrayType.TexUV].AsVector2Array();var skinI=skin[(int)Mesh.ArrayType.Index].AsInt32Array();
        if(wolf)
        {
            foreach(int side in new[]{-1,1})
            {
                var a=AuthoredWolfCornea(side).SurfaceGetArrays(0);
                var fullP=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var fullN=a[(int)Mesh.ArrayType.Normal].AsVector3Array();var fullU=a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                // The opaque lid needs fewer radial rings than the optical
                // dome. Retain every outer-rim sample, selecting alternating
                // source rings rather than smoothing or moving eye geometry.
                const int rings=4,sectors=40;
                var p=new Vector3[(rings+1)*(sectors+1)];var n=new Vector3[p.Length];var u=new Vector2[p.Length];var indices=new List<int>();
                int Index(int r,int s)=>r*(sectors+1)+s;
                for(int r=0;r<=rings;r++)for(int s=0;s<=sectors;s++)
                {
                    var target=Vector2.One*.5f+new Vector2(MathF.Cos(s*MathF.Tau/sectors),MathF.Sin(s*MathF.Tau/sectors))*(r/(float)rings*.5f);
                    int best=0;for(int k=1;k<fullU.Length;k++)if(fullU[k].DistanceSquaredTo(target)<fullU[best].DistanceSquaredTo(target))best=k;
                    int i=Index(r,s);p[i]=fullP[best];n[i]=fullN[best];u[i]=fullU[best];
                }
                void Triangle(int x,int y,int z)
                {
                    if((p[z]-p[x]).Cross(p[y]-p[x]).Dot(n[x]+n[y]+n[z])<0)(y,z)=(z,y);
                    indices.Add(x);indices.Add(y);indices.Add(z);
                }
                for(int s=0;s<sectors;s++)Triangle(Index(0,s),Index(1,s),Index(1,s+1));
                for(int r=1;r<rings;r++)for(int s=0;s<sectors;s++)
                {Triangle(Index(r,s),Index(r+1,s),Index(r,s+1));Triangle(Index(r,s+1),Index(r+1,s),Index(r+1,s+1));}
                sources.Add((p,n,u,indices.ToArray()));
            }
        }
        else
        {
            var a=data.Mesh.SurfaceGetArrays(2);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            for(int i=0;i<p.Length;i++)p[i]-=data.Joints[1];
            sources.Add((p,a[(int)Mesh.ArrayType.Normal].AsVector3Array(),a[(int)Mesh.ArrayType.TexUV2].AsVector2Array(),a[(int)Mesh.ArrayType.Index].AsInt32Array()));
        }
        // Broad-phase once per cached mesh, instead of reconstructing every
        // skin triangle's centroid for every eye vertex. The extra micrometre
        // keeps this AABB conservative at floating-point boundary values. The
        // original 8 cm spherical predicate and triangle order remain below.
        var low=new[]{Vector3.One*float.PositiveInfinity,Vector3.One*float.PositiveInfinity};
        var high=new[]{Vector3.One*float.NegativeInfinity,Vector3.One*float.NegativeInfinity};
        foreach(var source in sources)foreach(var local in source.P)
        {
            var point=local+data.Joints[1];if(!point.IsFinite()||point.X==0)throw new InvalidOperationException("Invalid source eye region");
            int side=point.X>0?1:0;
            low[side]=new(MathF.Min(low[side].X,point.X),MathF.Min(low[side].Y,point.Y),MathF.Min(low[side].Z,point.Z));
            high[side]=new(MathF.Max(high[side].X,point.X),MathF.Max(high[side].Y,point.Y),MathF.Max(high[side].Z,point.Z));
        }
        var candidates=new[]{new List<(int A,int B,int C,Vector3 Center)>(),new List<(int A,int B,int C,Vector3 Center)>()};
        for(int f=0;f<skinI.Length;f+=3)
        {
            int a=skinI[f],b=skinI[f+1],c=skinI[f+2];var center=(skinP[a]+skinP[b]+skinP[c])/3;
            if(!center.IsFinite())throw new InvalidOperationException("Nonfinite source skin triangle");
            if(center.X==0)continue;int side=center.X>0?1:0;const float margin=.080001f;
            if(center.X<low[side].X-margin||center.X>high[side].X+margin||center.Y<low[side].Y-margin||center.Y>high[side].Y+margin||center.Z<low[side].Z-margin||center.Z>high[side].Z+margin)continue;
            candidates[side].Add((a,b,c,center));
        }
        if(candidates[0].Count==0||candidates[1].Count==0)throw new InvalidOperationException("Source eye region has no finite candidate triangles");
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach(var source in sources)
        {
            var samples=new Vector2[source.P.Length];
            for(int v=0;v<source.P.Length;v++)
            {
                var point=source.P[v]+data.Joints[1];float best=float.PositiveInfinity;var sample=Vector2.Zero;
                foreach(var face in candidates[point.X>0?1:0])
                {
                    int a=face.A,b=face.B,c=face.C;var center=face.Center;
                    if(center.X*point.X<=0||center.DistanceSquaredTo(point)>.08f*.08f)continue;
                    var bary=ClosestBarycentric(point,skinP[a],skinP[b],skinP[c]);
                    var hit=skinP[a]*bary.X+skinP[b]*bary.Y+skinP[c]*bary.Z;
                    float distance=point.DistanceSquaredTo(hit);if(distance>=best)continue;
                    best=distance;sample=skinU[a]*bary.X+skinU[b]*bary.Y+skinU[c]*bary.Z;
                }
                if(!float.IsFinite(best)||best>.04f*.04f)throw new InvalidOperationException("Authored lid has no neighbouring source skin");
                if(wolf)
                {
                    // Continuous translation into the upper-rim coat avoids
                    // both the painted iris and a radial UV fold at its centre.
                    sample+=Vector2.Up*(8f/256);
                }
                samples[v]=sample;
            }
            foreach(int i in source.I)
            {
                tool.SetNormal(source.N[i]);tool.SetUV(samples[i]);
                tool.SetUV2(wolf?(source.U[i]-Vector2.One*.5f)*2:source.U[i]);
                tool.AddVertex(source.P[i]+source.N[i]*.00012f);
            }
        }
        tool.Index();var result=tool.Commit();result.SetMeta("ProjectionSkinTriangles",skinI.Length/3);
        result.SetMeta("ProjectionCandidatesLeft",candidates[0].Count);result.SetMeta("ProjectionCandidatesRight",candidates[1].Count);
        return _meshes[key]=result;
    }

    private void ValidateAuthoredEyelids(AnimalRig rig,AnimalRig peer,bool wolf)
    {
        var lid=rig.Head.GetNode<MeshInstance3D>("AuthoredEyelids");var other=peer.Head.GetNode<MeshInstance3D>("AuthoredEyelids");
        if(lid.Mesh!=other.Mesh||rig.LidMaterial==null||rig.LidMaterial==peer.LidMaterial||lid.MaterialOverride!=rig.LidMaterial||lid.VisibilityRangeEnd!=20)
            throw new InvalidOperationException("Authored eyelids lose shared geometry or independent animation");
        var a=lid.Mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();var uv=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        if(a[(int)Mesh.ArrayType.Index].AsInt32Array().Length/3>(wolf?560:352)||p.Length!=uv.Length)throw new InvalidOperationException("Authored eyelid shell exceeds source topology budget");
        for(int i=0;i<p.Length;i++)if(!p[i].IsFinite()||!uv[i].IsFinite()||!n[i].IsFinite()||MathF.Abs(n[i].Length()-1)>.01f)throw new InvalidOperationException("Nonfinite source-bound eyelid");
        var original=new List<Vector3>();
        if(wolf)foreach(int side in new[]{-1,1})original.AddRange(AuthoredWolfCornea(side).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
        else
        {
            var data=AuthoredAnimalData(false);
            foreach(var point in data.Mesh.SurfaceGetArrays(2)[(int)Mesh.ArrayType.Vertex].AsVector3Array())original.Add(point-data.Joints[1]);
        }
        var authored=AuthoredAnimalData(wolf);var skin=authored.Mesh.SurfaceGetArrays(wolf?0:1);
        var skinP=skin[(int)Mesh.ArrayType.Vertex].AsVector3Array();var skinU=skin[(int)Mesh.ArrayType.TexUV].AsVector2Array();var skinI=skin[(int)Mesh.ArrayType.Index].AsInt32Array();
        var actualUv=a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        for(int vertex=0;vertex<p.Length;vertex++)
        {
            var point=p[vertex];float closest=float.PositiveInfinity;var sourcePoint=Vector3.Zero;
            foreach(var source in original){float distance=source.DistanceTo(point);if(distance<closest){closest=distance;sourcePoint=source;}}
            if(MathF.Abs(closest-.00012f)>.000003f)throw new InvalidOperationException("Eyelid no longer follows its authored eye surface");
            if(vertex%Math.Max(1,p.Length/12)!=0)continue;
            // Reference the original full scan only in self-tests. Candidate
            // filtering must retain its exact arithmetic and first-hit ties.
            var world=sourcePoint+authored.Joints[1];float best=float.PositiveInfinity;var expected=Vector2.Zero;
            for(int f=0;f<skinI.Length;f+=3)
            {
                int aa=skinI[f],b=skinI[f+1],c=skinI[f+2];var center=(skinP[aa]+skinP[b]+skinP[c])/3;
                if(center.X*world.X<=0||center.DistanceSquaredTo(world)>.08f*.08f)continue;
                var bary=ClosestBarycentric(world,skinP[aa],skinP[b],skinP[c]);var hit=skinP[aa]*bary.X+skinP[b]*bary.Y+skinP[c]*bary.Z;
                float distance=world.DistanceSquaredTo(hit);if(distance>=best)continue;best=distance;expected=skinU[aa]*bary.X+skinU[b]*bary.Y+skinU[c]*bary.Z;
            }
            if(wolf)expected+=Vector2.Up*(8f/256);
            if(!float.IsFinite(best)||expected!=actualUv[vertex])throw new InvalidOperationException("Eye-region acceleration changes full-scan source UV coordinates");
        }
        rig.SetIdentity(0);rig.Pose(2.91f,false,false);
        if(rig.Blink<.99f||!lid.Visible||rig.LidMaterial.GetShaderParameter("blink").AsSingle()<.99f||peer.LidMaterial!.GetShaderParameter("blink").AsSingle()!=0||other.Visible)
            throw new InvalidOperationException("Authored animal eye does not close independently");
        var closed=rig.LidMaterial.GetShaderParameter("blink");rig.Pose(1,false,true);
        if(!rig.LidMaterial.GetShaderParameter("blink").Equals(closed))throw new InvalidOperationException("Paused authored eyelid moves");
        rig.Pose(.22f,false,false);if(rig.Blink!=0||lid.Visible)throw new InvalidOperationException("Authored eye does not reopen or keeps its unused draw visible");
        GD.Print($"AUTHORED_EYELIDS_PASS: {(wolf?"wolf":"deer")}, source eye shell, 0.12 mm offset, independent closure/reopen/pause");
        GD.Print($"AUTHORED_EYELID_PROJECTION: {(wolf?"wolf":"deer")}, {lid.Mesh.GetMeta("ProjectionSkinTriangles")} skin triangles, {lid.Mesh.GetMeta("ProjectionCandidatesLeft")}/{lid.Mesh.GetMeta("ProjectionCandidatesRight")} cached left/right candidates");
    }
}
