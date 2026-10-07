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
float weather=sin(local.x*6.3+local.z*3.1)*sin(local.y*7.7)*0.035;
float pores=sin(local.x*117.0+local.y*73.0)*sin(local.z*109.0+local.y*91.0);
float footprint=max(length(dFdx(local)),length(dFdy(local)))*115.0;
pores/=1.0+footprint*footprint;
float joint=pow(1.0-abs(sin(UV.y*3.14159)),16.0)*stone*0.045;
float patina=dot(texture(detail_atlas,(clamp(UV,vec2(0.02),vec2(0.98))+cell)/4.0).rgb,vec3(0.2126,0.7152,0.0722))-0.5;
ALBEDO=pigment.rgb*(0.96+weather+pores*grain-joint+patina*texture_amount);
ROUGHNESS=0.92+pores*0.025; SPECULAR=0.18;
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
void fragment(){
float coordinate=UV.x*110.0+sin(UV.y*7.0)*1.6;
float grain=sin(coordinate)/(1.0+pow(fwidth(coordinate),2.0));
float detail=dot(texture(detail_atlas,(clamp(UV,vec2(0.02),vec2(0.98))+cell)/4.0).rgb,vec3(0.2126,0.7152,0.0722))-0.5;
ALBEDO=pigment.rgb*(0.98+grain*0.03+detail*0.06);ROUGHNESS=0.94;SPECULAR=0.18;
}"};
        foreach(var (id,color) in new[]{(0,"#655747"),(1,"#87745b"),(15,"#9c8960")})
        {
            var material=new ShaderMaterial {Shader=timber};material.SetShaderParameter("pigment",new Color(color));
            if(atlas!=null)material.SetShaderParameter("detail_atlas",atlas);
            material.SetShaderParameter("cell",new Vector2(id%4,id/4));_materials[id]=material;
        }
        Assign(2,"#82624e",.018f,1);
        Assign(3,"#b7ad96",.014f);
        Assign(6,"#8f9189",.04f,1);
        Assign(7,"#74776f",.045f,1);
        Assign(4,"#758657",.023f);
        Assign(5,"#566e51",.025f);
    }
}
