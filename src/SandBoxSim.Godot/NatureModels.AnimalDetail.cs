using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void AddCanineToes(Node3D parent,Vector3 origin,Material fur)
    {
        // Four weight-bearing digits grow into the metacarpal pad. The skin
        // baker unions these roots; claws stay on the same lower-leg joint.
        for(int toe=0;toe<4;toe++)
        {
            float x=(toe-1.5f)*.025f;
            float front=toe is 1 or 2?-.012f:.003f;
            var digit=Loft("canine-digit",new[]{
                new Vector4(-.113f,.025f,.001f,.003f),new(-.107f,.027f,.011f,.012f),
                new(-.091f,.032f,.013f,.019f),new(-.064f,.034f,.016f,.019f),
                new(-.036f,.035f,.010f,.015f),new(-.023f,.035f,.001f,.003f)},24);
            Sculpt(parent,digit,origin+new Vector3(x,0,front),fur);
            var claw=Loft("canine-claw",new[]{
                new Vector4(-.127f,.022f,.0003f,.0005f),new(-.123f,.025f,.0025f,.0025f),
                new(-.114f,.031f,.0038f,.004f),new(-.105f,.033f,.0045f,.005f)},16);
            Sculpt(parent,claw,origin+new Vector3(x,0,front),LeatherSurface("#45413a"));
        }
    }

    private ShaderMaterial? _hoofMaterial;
    private Material HoofMaterial()
    {
        if(_hoofMaterial!=null)return _hoofMaterial;
        _hoofMaterial=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;
varying vec3 p;
void vertex(){p=VERTEX;}
void fragment(){
float ring=sin(p.y*740.0+sin(p.x*31.0)*1.3)/(1.0+pow(fwidth(p.y)*740.0,2.0));
float wear=1.0-smoothstep(.008,.035,p.y);
ALBEDO=tone.rgb*(1.0+wear*.22+ring*.035);
ROUGHNESS=.72-wear*.06;SPECULAR=.22;
}"}};
        _hoofMaterial.SetShaderParameter("tone",new Color("#49463d"));
        return _hoofMaterial;
    }

    private ShaderMaterial? _animalNose;
    private Material AnimalNoseMaterial()
    {
        if(_animalNose!=null)return _animalNose;
        _animalNose=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;
varying vec3 p;
void vertex(){p=VERTEX;}
void fragment(){
float pore=sin(p.x*1800.0+sin(p.y*1100.0))*sin(p.y*1600.0+p.z*700.0);
pore/=1.0+pow(max(length(dFdx(p)),length(dFdy(p)))*1700.0,2.0);
ALBEDO=tone.rgb*(1.0+pore*.12);
ROUGHNESS=.52+pore*.035;SPECULAR=.22;
}"}};
        _animalNose.SetShaderParameter("tone",new Color("#343833"));
        return _animalNose;
    }
}
