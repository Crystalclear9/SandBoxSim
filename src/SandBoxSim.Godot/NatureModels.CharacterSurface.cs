using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _hairShader,_lipShader;
    private Material LipMaterial(string color)
    {
        string key="lips:"+color;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _lipShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;varying vec3 lip_point;
void vertex(){lip_point=VERTEX;}
void fragment(){
float edge=smoothstep(.010,.026,abs(lip_point.x));
float crease=exp(-pow(lip_point.y/.0008,2.0))*(1.0-edge);
float line=sin(lip_point.x*4600.0+sin(lip_point.y*1300.0));
line/=1.0+pow(fwidth(lip_point.x*4600.0),2.0);
ALBEDO=mix(tone.rgb,tone.rgb*vec3(1.02,.79,.78),.48*(1.0-edge))*(1.0-crease*.12+line*.012);
ROUGHNESS=.62;SPECULAR=.25;}"};
        var material=new ShaderMaterial {Shader=_lipShader};material.SetShaderParameter("tone",new Color(color));
        return _residentMaterials[key]=material;
    }
    private Shader? _anatomicalSkinShader;
    private readonly System.Collections.Generic.Dictionary<string,Texture2D> _anatomicalNormals=new();
    private NoiseTexture2D? _skinGrain,_skinMicroNormal;
    private Material SkinSurface(string color)=>FaceSurfaceMaterial(color);
    private Material AnatomicalFaceMaterial(string color)
    {
        string key="anatomical-face:"+color+":"+_faceVariant;
        if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        string normalPath=HumanData().NormalPath;
        if(!_anatomicalNormals.TryGetValue(normalPath,out var normal))
            _anatomicalNormals[normalPath]=normal=GD.Load<Texture2D>(normalPath);
        if(normal.GetWidth()!=2048||normal.GetHeight()!=2048)throw new InvalidOperationException("Invalid imported human normal atlas dimensions");
        _anatomicalSkinShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;
uniform sampler2D sculpt_normal:hint_normal,filter_linear_mipmap_anisotropic;
uniform float beard=0.0;
varying vec3 skin_point;
void vertex(){skin_point=VERTEX;}
float skin_noise(vec2 p){
vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);vec2 q=vec2(127.1,311.7);
return mix(mix(fract(sin(dot(i,q))*43758.5453),fract(sin(dot(i+vec2(1,0),q))*43758.5453),f.x),
mix(fract(sin(dot(i+vec2(0,1),q))*43758.5453),fract(sin(dot(i+vec2(1),q))*43758.5453),f.x),f.y);}
void fragment(){
vec3 p=skin_point;
float front=1.0-smoothstep(-.060,-.025,p.z);
float warmth=skin_noise(p.xy*75.0+p.z*13.0)-.5;
float cheek=exp(-pow((abs(p.x)-.046)/.028,2.0)-pow((p.y+.016)/.035,2.0))*front;
float nose=exp(-pow(p.x/.018,2.0)-pow((p.y+.018)/.032,2.0))*front;
float temple=exp(-pow((abs(p.x)-.065)/.02,2.0)-pow((p.y-.024)/.035,2.0))*front;
float stubble=beard*front*(1.0-smoothstep(-.040,-.015,p.y))*(1.0-smoothstep(.055,.072,abs(p.x)));
vec2 pore_uv=p.xy*1900.0+p.z*127.0;
float footprint=max(length(dFdx(pore_uv)),length(dFdy(pore_uv)));
float pores=(skin_noise(pore_uv)-.5)/(1.0+footprint*footprint);
float follicle=smoothstep(.70,.88,skin_noise(p.xy*1350.0+p.z*183.0))/(1.0+footprint*footprint);
vec3 complexion=vec3(1.0+warmth*.035,1.0-warmth*.022,1.0-warmth*.032);
complexion*=vec3(1.0,1.0-cheek*.022-nose*.018,1.0-cheek*.032-nose*.025);
complexion*=1.0-temple*.025-stubble*(.035+follicle*.10);
ALBEDO=tone.rgb*COLOR.rgb*complexion*(1.0+pores*.022);
vec3 detail=texture(sculpt_normal,UV).rgb;
// Grazing cage hits around the temples can encode extreme tangent normals.
// Fade those samples rather than turning them into isolated specular facets.
float sculpt_trust=smoothstep(.625,.90,detail.b)*(1.0-temple*.65);
detail=mix(vec3(.5,.5,1.0),detail,sculpt_trust);
detail.xy+=vec2(pores,-pores)*.010;
NORMAL_MAP=detail;NORMAL_MAP_DEPTH=.18;
ROUGHNESS=clamp(.66-nose*.07+pores*.04,.56,.72);SPECULAR=.25;
}"};
        var material=new ShaderMaterial {Shader=_anatomicalSkinShader};
        material.SetShaderParameter("tone",new Color(color));
        material.SetShaderParameter("sculpt_normal",normal);
        material.SetShaderParameter("beard",HumanFamily==2?0f:_faceVariant%4==0?.75f:_faceVariant%4==3?.35f:0f);
        material.SetMeta("UsesSculptNormal",true);
        _residentMaterials[key]=material;return material;
    }
    private Material FaceSurfaceMaterial(string color)
    {
        string key="face-surface:"+color;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _skinGrain??=new NoiseTexture2D {Width=128,Height=128,GenerateMipmaps=true,
            Noise=new FastNoiseLite {Seed=937,Frequency=.24f},
            ColorRamp=new Gradient {Colors=new[]{new Color(.96f,.96f,.96f),Colors.White}}};
        _skinMicroNormal??=new NoiseTexture2D {Width=256,Height=256,GenerateMipmaps=true,AsNormalMap=true,BumpStrength=.8f,
            Noise=new FastNoiseLite {Seed=937,Frequency=.12f}};
        var material=new StandardMaterial3D {AlbedoColor=new Color(color),VertexColorUseAsAlbedo=true,
            Roughness=.72f,MetallicSpecular=.20f,RoughnessTexture=_skinGrain,AlbedoTexture=_skinGrain,Uv1Scale=new(12,12,1),
            NormalEnabled=true,NormalTexture=_skinMicroNormal,NormalScale=.12f};
        _residentMaterials[key]=material;return material;
    }
    private Material HairSurface(string color)
    {
        _hairShader??=new Shader {Code=@"shader_type spatial;render_mode cull_disabled;
uniform vec4 tone:source_color;varying vec3 hair_point;
void vertex(){hair_point=VERTEX;}
void fragment(){
float angle=atan(hair_point.x,hair_point.z);
float line=angle*157.0+hair_point.y*180.0+sin(angle*2.0)*hair_point.y*110.0;
float strand=sin(line)/(1.0+fwidth(line)*fwidth(line));
float lock=sin(angle*8.0+hair_point.y*10.0+sin(angle*3.0)*.6)*0.020;
float breakup=sin(angle*31.0+hair_point.y*71.0)*sin(hair_point.y*93.0+angle*7.0)*.009;
ALBEDO=tone.rgb*COLOR.rgb*(0.89+strand*0.018+lock+breakup);
ROUGHNESS=mix(.68,.52,COLOR.a);SPECULAR=.30;
float edge_noise=fract(sin(dot(hair_point.xy,vec2(127.1,311.7)))*43758.5453);
ALPHA=COLOR.a<.1?1.0-smoothstep(.978+edge_noise*.018,1.006,UV.y):1.0;
ALPHA_SCISSOR_THRESHOLD=.5;
}"};
        var material=new ShaderMaterial {Shader=_hairShader};material.SetShaderParameter("tone",new Color(color));return material;
    }
    private Mesh EarSurface(int side)
    {
        string key="ear-surface:"+side;if(_meshes.TryGetValue(key,out var cached))return cached;
        const int rings=10,columns=32;var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int row,int col)
        {
            float r=row/(float)rings,a=col*MathF.Tau/columns;
            float helix=.004f+.005f*MathF.Exp(-MathF.Pow((r-.84f)/.13f,2));
            float antihelix=.003f*MathF.Exp(-MathF.Pow((r-.49f)/.14f,2))*(.6f+.4f*MathF.Sin(a+.4f));
            float concha=.005f*MathF.Exp(-MathF.Pow(r/.35f,2));
            return new(side*(helix+antihelix-concha),MathF.Cos(a)*r*.023f,MathF.Sin(a)*r*(.010f+.002f*MathF.Cos(a)));
        }
        void Vertex(int row,int col)
        {
            float r=row/(float)rings;surface.SetUV(new(col/(float)columns,r));
            surface.SetColor(new Color(1,1-.055f*(1-r),1-.085f*(1-r)));surface.AddVertex(Point(row,col));
        }
        for(int row=0;row<rings;row++)for(int col=0;col<columns;col++)
        {
            if(side<0){Vertex(row,col);Vertex(row+1,col);Vertex(row,col+1);Vertex(row,col+1);Vertex(row+1,col);Vertex(row+1,col+1);}
            else{Vertex(row,col);Vertex(row,col+1);Vertex(row+1,col);Vertex(row,col+1);Vertex(row+1,col+1);Vertex(row+1,col);}
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
