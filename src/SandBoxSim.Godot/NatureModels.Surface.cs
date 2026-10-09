using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private void InstallCraftMaterials()
    {
        var shader=new Shader { Code=@"shader_type spatial;
uniform vec4 pigment : source_color;
uniform float grain=0.035;
uniform float stone=0.0;
uniform sampler2D detail_atlas : source_color, filter_linear_mipmap;
uniform vec2 cell;
uniform float texture_amount=0.0;
varying vec3 local;
void vertex(){ local=VERTEX; }
void fragment(){
float weather=sin(local.x*2.3+local.z*1.7)*sin(local.y*3.1)*0.075;
float strata=sin((local.y+local.x*.18-local.z*.12)*27.0);
strata/=1.0+pow(fwidth((local.y+local.x*.18-local.z*.12)*27.0),2.0);
float damp=(1.0-smoothstep(-.4,.7,local.y))*.055*stone;
float pores=sin(local.x*117.0+local.y*73.0)*sin(local.z*109.0+local.y*91.0);
float footprint=max(length(dFdx(local)),length(dFdy(local)))*115.0;
pores/=1.0+footprint*footprint;
float joint=pow(1.0-abs(sin(UV.y*3.14159)),16.0)*stone*0.045;
float patina=dot(texture(detail_atlas,(clamp(UV,vec2(0.02),vec2(0.98))+cell)/4.0).rgb,vec3(0.2126,0.7152,0.0722))-0.5;
ALBEDO=pigment.rgb*(0.96+weather+pores*grain-joint+patina*texture_amount+strata*stone*.025-damp);
ROUGHNESS=0.83+pores*0.04+stone*0.07; SPECULAR=0.21;
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float determinant=dot(dx,tx);
float height=pores*.0006+strata*stone*.0012;if(abs(determinant)>.0000000001)NORMAL=normalize(abs(determinant)*NORMAL-sign(determinant)*(dFdx(height)*tx+dFdy(height)*ty));
}" };
        var atlas=ResourceLoader.Exists("res://assets/textures/natural-materials.png") ? GD.Load<Texture2D>("res://assets/textures/natural-materials.png") : null;
        void Assign(int id,string color,float grain,float stone=0)
        {
            var material=new ShaderMaterial { Shader=shader };
            if(atlas!=null) material.SetShaderParameter("detail_atlas",atlas);
            material.SetShaderParameter("cell",new Vector2(id%4,id/4));
            material.SetShaderParameter("texture_amount",atlas==null ? 0f : id is 6 or 7 ? .22f : id is 4 or 5 ? .26f : .06f);
            material.SetShaderParameter("pigment",new Color(color)); material.SetShaderParameter("grain",grain); material.SetShaderParameter("stone",stone); _materials[id]=material;
        }
        var timber=new Shader {Code=@"shader_type spatial;
uniform vec4 pigment:source_color;uniform sampler2D detail_atlas:source_color,filter_linear_mipmap;uniform vec2 cell;
varying vec3 wood_point;varying vec3 wood_normal;
void vertex(){wood_point=VERTEX;wood_normal=NORMAL;}
void fragment(){
float coordinate=(wood_point.x+wood_point.z)*125.0+sin(wood_point.y*7.0+wood_point.x*13.0)*0.7;
float grain=sin(coordinate)/(1.0+pow(fwidth(coordinate),2.0));
float detail=dot(texture(detail_atlas,(clamp(UV,vec2(0.02),vec2(0.98))+cell)/4.0).rgb,vec3(0.2126,0.7152,0.0722))-0.5;
float streak=sin((wood_point.x+wood_point.z)*39.0+sin(wood_point.y*2.0)*.3);
float stain=sin(wood_point.x*5.3+wood_point.y*.7)*sin(wood_point.y*3.7)*.025;
float ring_coordinate=length(wood_point.xz)*155.0+sin(atan(wood_point.z,wood_point.x)*5.0)*.5;
float rings=sin(ring_coordinate)/(1.0+pow(fwidth(ring_coordinate),2.0));
float end_grain=pow(abs(wood_normal.y),8.0);
float knot=exp(-pow((wood_point.y-.12)*7.0,2.0)-pow((wood_point.x+.04)*19.0,2.0));
ALBEDO=pigment.rgb*(0.96+mix(grain*.04+streak*.025,rings*.055,end_grain)+stain+detail*.035-knot*.035);ROUGHNESS=0.83+grain*.035;SPECULAR=0.22;
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float determinant=dot(dx,tx);
float height=mix(grain,rings,end_grain)*.0007;if(abs(determinant)>.0000000001)NORMAL=normalize(abs(determinant)*NORMAL-sign(determinant)*(dFdx(height)*tx+dFdy(height)*ty));
}"};
        foreach(var (id,color) in new[]{(0,"#5e554b"),(1,"#87755e"),(15,"#988961")})
        {
            var material=new ShaderMaterial {Shader=timber};material.SetShaderParameter("pigment",new Color(color));
            if(atlas!=null)material.SetShaderParameter("detail_atlas",atlas);
            material.SetShaderParameter("cell",new Vector2(id%4,id/4));_materials[id]=material;
        }
        Assign(2,"#74665d",.018f,1);
        Assign(3,"#b8b7aa",.014f);
        Assign(6,"#777c77",.04f,1);
        Assign(7,"#626c68",.045f,1);
        Assign(4,"#6c7953",.023f);
        Assign(5,"#526650",.025f);
        _materials[11]=new StandardMaterial3D {AlbedoColor=new("#727c80"),Metallic=.72f,Roughness=.42f,MetallicSpecular=.42f};
        _materials[9]=new StandardMaterial3D {AlbedoColor=new("#354b55"),Roughness=.24f,MetallicSpecular=.52f};
    }
}
