using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _fabricShader;
    private Material Fabric(string color)
    {
        _fabricShader ??= new Shader { Code = @"shader_type spatial;
uniform vec4 dye : source_color;
uniform sampler2D weave_map:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D fibre_map:filter_linear_mipmap_anisotropic,repeat_enable;
uniform bool sleeve_details=false;
varying vec3 cloth_position;varying vec3 cloth_normal;
varying vec3 cloth_axis_x;varying vec3 cloth_axis_y;varying vec3 cloth_axis_z;
void vertex(){ cloth_position = VERTEX;cloth_normal=NORMAL;
cloth_axis_x=MODELVIEW_NORMAL_MATRIX[0];cloth_axis_y=MODELVIEW_NORMAL_MATRIX[1];cloth_axis_z=MODELVIEW_NORMAL_MATRIX[2]; }
void fragment(){
vec2 weave=vec2(cloth_position.x+cloth_position.z,cloth_position.y)*920.0;
float footprint=max(length(dFdx(weave)),length(dFdy(weave)));
float yarn=(sin(weave.x)+sin(weave.y+sin(weave.x)*.22))/(2.0+footprint*footprint);
float twill=sin((weave.x-weave.y)*.25)/(1.0+footprint*footprint*.15);
float slub=sin(cloth_position.y*91.0+sin(cloth_position.x*23.0)*2.0)*.006;
float dye_variation=sin(cloth_position.x*13.0+sin(cloth_position.y*19.0)) * sin(cloth_position.z*17.0+cloth_position.y*11.0)*.035;
vec3 w=pow(abs(normalize(cloth_normal)),vec3(5.0));w/=max(.001,w.x+w.y+w.z);
vec3 base=normalize(cloth_normal),side=sign(base+vec3(.000001));
vec3 p=cloth_position/.27;
vec3 cx=texture(weave_map,vec2(p.z,p.y*side.x)).rgb,cy=texture(weave_map,p.xz).rgb,cz=texture(weave_map,vec2(p.x,-p.y*side.z)).rgb;
vec3 nx=texture(fibre_map,vec2(p.z,p.y*side.x)).rgb,ny=texture(fibre_map,p.xz).rgb,nz=texture(fibre_map,vec2(p.x,-p.y*side.z)).rgb;
float thread=dot(cx*w.x+cy*w.y+cz*w.z,vec3(.2126,.7152,.0722));
// The pinned linen map has mean linear luminance .3962. Normalize in the
// same colour space as texture sampling, retaining its photographed weave.
float textile=clamp(1.0+(thread-.3962)*2.0/.3962,.65,1.25);
ALBEDO=dye.rgb*.68*(textile+yarn*.018+twill*.008+slub+dye_variation);
// On the actual sewn sleeve UV2 stores rest-space metric distances. These
// subdued stitch lines follow skinning, rather than floating above the cloth.
// Filter in metric space so they disappear cleanly at world-camera distances.
if(sleeve_details){
float aa=max(fwidth(UV2.x),fwidth(UV2.y));
float detail_fade=1.0-smoothstep(.0015,.007,aa);
float cap_line=(1.0-smoothstep(.0004,.0011+aa,abs(UV2.x-.006)))+(1.0-smoothstep(.0004,.0011+aa,abs(UV2.x-.009)));
float underarm=1.0-smoothstep(.0004,.0011+aa,abs(UV2.y-.003));
float stitch=.6+.4*smoothstep(-.2,.4,sin(UV2.x*2094.395));
float sewn=clamp(cap_line+underarm*stitch,0.0,1.0)*step(0.0,UV2.x)*detail_fade;
ALBEDO*=1.0-sewn*.075;
}
vec3 grain=vec3(0,(nx.g*2.0-1.0)*side.x,nx.r*2.0-1.0)*w.x+vec3(ny.r*2.0-1.0,0,ny.g*2.0-1.0)*w.y+vec3(nz.r*2.0-1.0,(nz.g*2.0-1.0)*-side.z,0)*w.z;
grain-=base*dot(grain,base);
NORMAL=normalize(NORMAL+(cloth_axis_x*grain.x+cloth_axis_y*grain.y+cloth_axis_z*grain.z)*.35);
ROUGHNESS=clamp(nx.b*w.x+ny.b*w.y+nz.b*w.z,.78,.96);SPECULAR=.21;RIM=.10;RIM_TINT=.72;
}" };
        var material = new ShaderMaterial { Shader = _fabricShader };material.SetShaderParameter("dye",new Color(color));
        material.SetShaderParameter("weave_map",ScannedSurfaces.Map("rough_linen","diffuse"));
        material.SetShaderParameter("fibre_map",ScannedSurfaces.NormalRoughness(ScannedSurfaces.Map("rough_linen","normal"),ScannedSurfaces.Map("rough_linen","roughness")));
        return material;
    }
}
