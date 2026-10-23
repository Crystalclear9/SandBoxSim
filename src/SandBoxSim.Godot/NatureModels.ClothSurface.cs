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
varying vec3 cloth_position;
void vertex(){ cloth_position = VERTEX; }
void fragment(){
vec2 weave=vec2(cloth_position.x+cloth_position.z,cloth_position.y)*920.0;
float footprint=max(length(dFdx(weave)),length(dFdy(weave)));
float yarn=(sin(weave.x)+sin(weave.y+sin(weave.x)*.22))/(2.0+footprint*footprint);
float twill=sin((weave.x-weave.y)*.25)/(1.0+footprint*footprint*.15);
float slub=sin(cloth_position.y*91.0+sin(cloth_position.x*23.0)*2.0)*.006;
float dye_variation=sin(cloth_position.x*13.0+sin(cloth_position.y*19.0)) * sin(cloth_position.z*17.0+cloth_position.y*11.0)*.017;
ALBEDO=dye.rgb*(.985+yarn*.014+twill*.004+slub+dye_variation);
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float determinant=dot(dx,tx);
float fiber_height=yarn*.000035+slub*.0002;
if(abs(determinant)>.0000000001)NORMAL=normalize(abs(determinant)*NORMAL-sign(determinant)*(dFdx(fiber_height)*tx+dFdy(fiber_height)*ty));
ROUGHNESS = 0.94; SPECULAR = 0.16;
}" };
        var material = new ShaderMaterial { Shader = _fabricShader }; material.SetShaderParameter("dye", new Color(color)); return material;
    }
}
