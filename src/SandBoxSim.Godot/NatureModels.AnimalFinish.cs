using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private const string CoatGradingShader=@"
vec3 wolf_eye_backing(vec3 colour,vec2 uv){
    // This atlas contains a painted eye. Suppress it beneath and around the
    // fitted cornea so two unrelated iris patterns do not appear together.
    float distance_px=length((uv-vec2(23.5,189.5)/256.0)*256.0);
    float mask=1.0-smoothstep(3.5,6.0,distance_px);
    float pigment=smoothstep(.04,.14,colour.r-colour.b);
    float luma=dot(colour,vec3(.2126,.7152,.0722));
    return mix(colour,vec3(min(.035,luma))*vec3(1.05,1.0,.90),mask*pigment);
}
vec3 coat_grade(vec3 colour,bool canine){
    float luma=dot(colour,vec3(.2126,.7152,.0722));
    if(canine){
        float target=.075+.67*sqrt(max(0.0,luma));
        vec3 neutral=mix(colour,vec3(luma)*vec3(1.025,1.01,.95),.40);
        return mix(colour,neutral*target/max(.015,luma),smoothstep(.025,.11,luma));
    }
    return mix(colour,vec3(luma)*vec3(1.04,1.0,.93),.18);
}
";
    private Mesh AuthoredGuardCoat(bool wolf)
    {
        string key="authored-guard-coat:"+wolf;if(_meshes.TryGetValue(key,out var cached))return cached;
        var mesh=AuthoredAnimalData(wolf).Mesh;
        var faces=new List<(Vector3[] P,Vector3[] N,Vector2[] U,int[] B,float[] W,int A,int C,int D)>();
        var cumulative=new List<float>();float area=0;
        for(int surface=0;surface<mesh.GetSurfaceCount();surface++)
        {
            // SurfaceTool commits do not retain names; the deer body is first.
            if(!wolf&&surface!=0)continue;
            var arrays=mesh.SurfaceGetArrays(surface);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=indices[i],b=indices[i+1],c=indices[i+2];var center=(p[a]+p[b]+p[c])/3;
                if(center.Y<.19f||center.Z<(wolf?-.855f:-.83f))continue;
                var eye=new Vector3(MathF.Abs(center.X),center.Y,center.Z)-new Vector3(.061f,.901f,-.785f);
                if(wolf&&(eye/new Vector3(.035f,.035f,.045f)).Length()<1)continue;
                float size=(p[b]-p[a]).Cross(p[c]-p[a]).Length();if(size<1e-8f)continue;
                float collar=MathF.Exp(-MathF.Pow((center.Z+.54f)/.23f,2));
                float tail=wolf?Math.Clamp((center.Z-.45f)/.25f,0,1):0;
                area+=size*(1+collar*4+tail*3);cumulative.Add(area);faces.Add((p,n,uv,bones,weights,a,b,c));
            }
        }
        if(area<=0)throw new InvalidOperationException("Authored coat has no source surface");
        uint seed=wolf?982451653u:961748941u;float Random(){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;return (seed&0xffffff)/16777216f;}
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        var material=new ShaderMaterial {Shader=new Shader {Code="shader_type spatial;render_mode cull_disabled;\nuniform sampler2D albedo_map:source_color,filter_linear_mipmap_anisotropic;uniform bool predator;\n"+CoatGradingShader+@"
void fragment(){if(!OUTPUT_IS_SRGB&&!FRONT_FACING)NORMAL=-NORMAL;ALBEDO=coat_grade(texture(albedo_map,UV).rgb,predator)*(predator?.86:.90)*(1.0+UV2.y*.07);ROUGHNESS=.91;SPECULAR=.15;}
"}};
        material.SetShaderParameter("predator",wolf);material.SetShaderParameter("albedo_map",GD.Load<Texture2D>(AuthoredRoot(wolf)+(wolf?"wolf-grey.png":"deer-body.png")));tool.SetMaterial(material);
        int count=wolf?640:400,boneCount=wolf?15:14;
        for(int strand=0;strand<count;strand++)
        {
            int chosen=cumulative.BinarySearch(Random()*area);if(chosen<0)chosen=~chosen;var f=faces[Math.Min(chosen,faces.Count-1)];
            float r=MathF.Sqrt(Random()),v=Random();float[] bary={1-r,r*(1-v),r*v};int[] vertices={f.A,f.C,f.D};
            var root=Vector3.Zero;var normal=Vector3.Zero;var uv=Vector2.Zero;var influence=new float[boneCount];
            for(int k=0;k<3;k++){int i=vertices[k];root+=f.P[i]*bary[k];normal+=f.N[i]*bary[k];uv+=f.U[i]*bary[k];for(int b=0;b<4;b++)influence[f.B[i*4+b]]+=f.W[i*4+b]*bary[k];}
            normal=normal.Normalized();var order=new int[boneCount];for(int i=0;i<order.Length;i++)order[i]=i;Array.Sort(order,(a,b)=>influence[b].CompareTo(influence[a]));
            var bones=new int[4];var weights=new float[4];float sum=0;for(int k=0;k<4;k++){bones[k]=order[k];sum+=influence[order[k]];}for(int k=0;k<4;k++)weights[k]=influence[bones[k]]/sum;
            float collar=MathF.Exp(-MathF.Pow((root.Z+.54f)/.23f,2)),tail=wolf?Math.Clamp((root.Z-.45f)/.25f,0,1):0;
            float length=(wolf?.010f:.003f)*(0.75f+Random()*.5f)*(1+collar*(wolf?1.8f:3f)+tail);
            var direction=root.Z<-.4f?new Vector3(0,-.8f,.35f):new Vector3(0,-.25f,1);direction-=normal*direction.Dot(normal);
            if(direction.LengthSquared()<.01f)direction=normal.Cross(Vector3.Right);direction=direction.Normalized();var across=normal.Cross(direction).Normalized();
            float width=wolf?.00045f:.00032f;
            void Vertex(Vector3 point,float t){tool.SetNormal(normal);tool.SetUV(uv);tool.SetUV2(new(t,(strand%7)/7f));tool.SetBones(bones);tool.SetWeights(weights);tool.AddVertex(point);}
            void Triangle((Vector3 P,float T) a,(Vector3 P,float T) b,(Vector3 P,float T) c){if((c.P-a.P).Cross(b.P-a.P).Dot(normal)<0)(b,c)=(c,b);Vertex(a.P,a.T);Vertex(b.P,b.T);Vertex(c.P,c.T);}
            for(int plane=0;plane<2;plane++)
            {
                var spread=plane==0?across:(across+normal*.6f).Normalized();var start=root+normal*.00035f;
                var middle=start+direction*length*.48f+normal*length*.14f;var tip=start+direction*length+normal*length*.18f;
                Triangle((start-spread*width,0),(start+spread*width,0),(middle-spread*width*.6f,.5f));
                Triangle((start+spread*width,0),(middle+spread*width*.6f,.5f),(middle-spread*width*.6f,.5f));
                Triangle((middle-spread*width*.6f,.5f),(middle+spread*width*.6f,.5f),(tip,1));
            }
        }
        tool.Index();return _meshes[key]=tool.Commit();
    }
}
