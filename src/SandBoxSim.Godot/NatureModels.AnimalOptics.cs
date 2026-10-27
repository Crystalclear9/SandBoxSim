using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateWolfEyeAtlas(Mesh mesh)
    {
        var coat=(ShaderMaterial)AuthoredAnimalMaterial(true,"Wolf");
        var lens=(ShaderMaterial)AuthoredWolfEyeMaterial();
        var patches=new[]{coat.GetShaderParameter("eye_coat_patch_a").AsVector2(),coat.GetShaderParameter("eye_coat_patch_b").AsVector2()};
        float scale=coat.GetShaderParameter("eye_coat_patch_scale").AsSingle();
        float repair=coat.GetShaderParameter("eye_coat_repair_strength").AsSingle();
        if(repair!=lens.GetShaderParameter("eye_coat_repair_strength").AsSingle()||!float.IsFinite(repair)||repair<=0||repair>=1)
            throw new InvalidOperationException("Wolf eye margin loses its bounded shared source-coat blend");
        if(patches[0]!=lens.GetShaderParameter("eye_coat_patch_a").AsVector2()||patches[1]!=lens.GetShaderParameter("eye_coat_patch_b").AsVector2()||scale!=lens.GetShaderParameter("eye_coat_patch_scale").AsSingle()||scale<.85f||scale>1.15f)
            throw new InvalidOperationException("Optical rim and wolf coat disagree on bounded source atlas repair");
        var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv=a[(int)Mesh.ArrayType.TexUV].AsVector2Array();var indices=a[(int)Mesh.ArrayType.Index].AsInt32Array();
        var faces=new[]{new List<(int A,int B,int C)>(),new List<(int A,int B,int C)>()};
        for(int i=0;i<indices.Length;i+=3)
        {
            int aa=indices[i],b=indices[i+1],c=indices[i+2];
            var center=(p[aa]+p[b]+p[c])/3;
            if(center.Y<.70f||center.Z>-.54f)continue;
            faces[center.X>0?1:0].Add((aa,b,c));
        }
        bool TrySample(int side,Vector2 at,out Vector3 point)
        {
            foreach(var f in faces[side])
            {
                var ab=uv[f.B]-uv[f.A];var ac=uv[f.C]-uv[f.A];float det=ab.Cross(ac);
                if(MathF.Abs(det)<1e-12f)continue;
                var d=at-uv[f.A];float v=d.Cross(ac)/det,w=ab.Cross(d)/det;
                if(v<-.00001f||w<-.00001f||v+w>1.00001f)continue;
                point=p[f.A]*(1-v-w)+p[f.B]*v+p[f.C]*w;return true;
            }
            point=Vector3.Zero;return false;
        }
        var centerUv=new Vector2(23.5f/256,189.5f/256);int samples=0;
        var source=(Texture2D)coat.GetShaderParameter("albedo_map").AsGodotObject();
        using var pixels=source.GetImage();
        if(pixels.IsCompressed()&&pixels.Decompress()!=Error.Ok)throw new InvalidOperationException("Cannot inspect actual source wolf atlas");
        float largest=0,minimumLuma=1;
        for(int side=0;side<2;side++)for(int y=-13;y<=13;y+=2)for(int x=-13;x<=13;x+=2)
        {
            var region=new Vector2((-.56f*x+.83f*y)/8.5f,(.83f*x+.56f*y)/4.8f);
            if(region.LengthSquared()>=1.45f*1.45f)continue;
            var at=centerUv+new Vector2(x,y)/256;
            if(!TrySample(side,at,out var original))continue;
            foreach(var patch in patches)
            {
                var lookup=patch+(at-centerUv)*scale;
                var px=(lookup-centerUv)*256;
                var sourceRegion=new Vector2((-.56f*px.X+.83f*px.Y)/8.5f,(.83f*px.X+.56f*px.Y)/4.8f);
                if(sourceRegion.Length()<=1.50f||!TrySample(side,lookup,out var neighbouring))
                    throw new InvalidOperationException("Eye coat reconstruction borrows the painted eye or an unrelated atlas island");
                float distance=original.DistanceTo(neighbouring);
                if(!neighbouring.IsFinite()||original.X*neighbouring.X<=0||distance>.14f)
                    throw new InvalidOperationException("Eye repair leaves neighbouring authored head fur: "+distance);
                // Include bilinear support and one extra source texel, rather
                // than checking only a bright centre next to the black eye.
                int sx=(int)(lookup.X*pixels.GetWidth()-.5f),sy=(int)(lookup.Y*pixels.GetHeight()-.5f);
                for(int iy=sy-1;iy<=sy+2;iy++)for(int ix=sx-1;ix<=sx+2;ix++)
                {
                    var colour=pixels.GetPixel(ix,iy);float luma=colour.R*.2126f+colour.G*.7152f+colour.B*.0722f;
                    if(luma<.10f)throw new InvalidOperationException("Eye coat reconstruction samples a painted black pixel: "+luma);
                    minimumLuma=MathF.Min(minimumLuma,luma);
                }
                largest=MathF.Max(largest,distance);samples++;
            }
        }
        if(samples<200)throw new InvalidOperationException("Insufficient bilateral authored eye UV coverage");
        GD.Print($"ANIMAL_EYE_ATLAS_PASS: {samples} bilateral bounded source coat lookups outside painted eye, same head islands, maximum {largest*1000:0.0} mm, minimum source luma {minimumLuma:0.000}; optical rim and coat share continuous reconstruction");
    }
}
