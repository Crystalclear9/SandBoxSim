using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _hairShader,_lipShader;
    private string HeadAlbedoPath=>ChildHead?"res://assets/models/human/human-feminine_albedo.png":HumanData().AlbedoPath;
    private Material LipMaterial(string color)
    {
        string key="lips:"+color+":"+HumanFamily+":"+ChildHead;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _lipShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;uniform sampler2D skin_albedo:source_color,filter_linear_mipmap_anisotropic;uniform float speech=0.0;varying vec3 lip_point;
void vertex(){lip_point=VERTEX;
float weight=exp(-pow(abs(VERTEX.x)/.023,4.0)-pow(abs(VERTEX.y)/.008,4.0))*(1.0-smoothstep(-.006,.006,VERTEX.z))*COLOR.a;
VERTEX.y+=(VERTEX.y>=0.0?1.0:-1.0)*speech*.0012*weight;VERTEX.z-=speech*.0002*weight;}
void fragment(){
ALBEDO=texture(skin_albedo,UV).rgb*.72*(tone.rgb/vec3(.667,.545,.471));
float crease=sin(lip_point.x*1900.0+sin(lip_point.y*700.0));
crease/=1.0+pow(max(length(dFdx(lip_point)),length(dFdy(lip_point)))*1900.0,2.0);
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float det=dot(dx,tx),height=crease*.000007;
if(abs(det)>.0000000001)NORMAL=normalize(abs(det)*NORMAL-sign(det)*(dFdx(height)*tx+dFdy(height)*ty));
ROUGHNESS=.50+crease*.018;SPECULAR=.30;}"};
        var material=new ShaderMaterial {Shader=_lipShader};material.SetShaderParameter("tone",new Color(color));material.SetShaderParameter("speech",0f);material.SetShaderParameter("skin_albedo",GD.Load<Texture2D>(HeadAlbedoPath));
        return _residentMaterials[key]=material;
    }
    private Shader? _anatomicalSkinShader;
    private NoiseTexture2D? _skinGrain,_skinMicroNormal;
    private Material SkinSurface(string color)=>FaceSurfaceMaterial(color);
    private Material AnatomicalFaceMaterial(string color)
    {
        string key="anatomical-face:"+color+":"+_faceVariant;
        if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _anatomicalSkinShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;
uniform sampler2D skin_albedo:source_color,filter_linear_mipmap_anisotropic;
uniform vec3 nose_center=vec3(0.0);uniform vec3 mouth_center=vec3(0.0);
uniform float blink=0.0;
varying vec3 skin_point;
void vertex(){VERTEX.yz+=UV2*blink;skin_point=VERTEX;}
void fragment(){
vec3 colour=texture(skin_albedo,UV).rgb;
float front=1.0-smoothstep(-.05,-.015,skin_point.z);
float nose=exp(-pow(skin_point.x/.018,2.0)-pow((skin_point.y-nose_center.y)/.032,2.0))*front;
float forehead=exp(-pow(skin_point.x/.048,2.0)-pow((skin_point.y-nose_center.y-.070)/.035,2.0))*front;
float lip_margin=exp(-pow((skin_point.y-mouth_center.y)/.010,2.0)-pow(skin_point.x/.030,4.0))*front;
ALBEDO=colour*.72*(tone.rgb/vec3(.667,.545,.471));
// Sub-millimetre skin grain is filtered by pixel footprint. It belongs
// in shading, not swollen geometry around the eyes and mouth.
float pore=sin(skin_point.x*2600.0+sin(skin_point.y*1100.0))*sin(skin_point.y*2300.0+skin_point.z*1700.0);
pore/=1.0+pow(max(length(dFdx(skin_point)),length(dFdy(skin_point)))*2300.0,2.0);
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float det=dot(dx,tx);
float height=pore*.000018;
if(abs(det)>.0000000001)NORMAL=normalize(abs(det)*NORMAL-sign(det)*(dFdx(height)*tx+dFdy(height)*ty));
ROUGHNESS=.63-nose*.13-forehead*.055-lip_margin*.07+pore*.025;SPECULAR=.26;
// Ear shells sit around 8 cm from the centre, not 30 cm. Keep the
// transmission off the cheek and jaw and retain opaque facial shading.
float thin=smoothstep(.070,.086,abs(skin_point.x))*(1.0-smoothstep(.035,.075,abs(skin_point.y-nose_center.y)));
BACKLIGHT=ALBEDO*vec3(.13,.038,.025)*thin;
}"};
        var material=new ShaderMaterial {Shader=_anatomicalSkinShader};
        material.SetShaderParameter("tone",new Color(color));
        material.SetShaderParameter("skin_albedo",GD.Load<Texture2D>(HeadAlbedoPath));
        material.SetShaderParameter("blink",0f);
        material.SetShaderParameter("nose_center",new Vector3(0,HumanData().NoseY,0));
        material.SetShaderParameter("mouth_center",HumanData().Mouth);
        material.SetMeta("UsesSkinAtlas",true);
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
    private Shader? _authoredHairShader;
    private Material AuthoredHairMaterial(string color)
    {
        string key="authored-hair:"+HumanFamily+":"+color;
        if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _authoredHairShader??=new Shader {Code=@"shader_type spatial;render_mode cull_disabled;
uniform sampler2D hair_albedo:source_color,filter_linear_mipmap_anisotropic;
uniform vec4 tone:source_color;uniform float atlas_mean=.2;
void fragment(){if(!FRONT_FACING){NORMAL=-NORMAL;}vec4 strand=texture(hair_albedo,UV);float fibre=dot(strand.rgb,vec3(.2126,.7152,.0722));
ALBEDO=tone.rgb*clamp(fibre/max(.025,atlas_mean),.25,1.6)*.55;
ALPHA=strand.a;ALPHA_SCISSOR_THRESHOLD=.38;ROUGHNESS=.78;SPECULAR=.16;}"};
        var material=new ShaderMaterial {Shader=_authoredHairShader};
        material.SetShaderParameter("hair_albedo",GD.Load<Texture2D>(HumanData().HairPath));
        material.SetShaderParameter("atlas_mean",HumanData().HairLuminance);
        material.SetShaderParameter("tone",new Color(color));material.SetMeta("UsesHairAtlas",true);
        return _residentMaterials[key]=material;
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
