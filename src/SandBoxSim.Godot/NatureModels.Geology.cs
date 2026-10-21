using Godot;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Shader? _rockShader;
    private readonly ShaderMaterial?[] _rockSurfaces=new ShaderMaterial?[2];
    public Material RockSurface(bool ore)
    {
        int index=ore?1:0;if(_rockSurfaces[index]!=null)return _rockSurfaces[index]!;
        _rockShader??=new Shader {Code=@"shader_type spatial;
uniform bool ore=false;
varying vec3 p;
void vertex(){p=VERTEX;}
float grain(vec3 q){return sin(q.x*1.71+q.z*.93)*sin(q.y*2.13-q.x*.61)+sin(q.z*2.71+q.y*.49)*.45;}
void fragment(){
float footprint=max(length(dFdx(p)),length(dFdy(p)));
float broad=grain(p*7.0),fine=grain(p*117.0)*grain(p*79.0+vec3(2.1,4.7,1.3))/(1.0+pow(footprint*117.0,2.0));
float layer=p.y*53.0+p.x*8.0+sin(p.z*11.0)*.42;
float strata=sin(layer)/(1.0+pow(fwidth(layer),2.0));
float vein=1.0-smoothstep(.04,.13,abs(sin(p.y*19.0+p.x*7.0+grain(p*5.0)*.7)));
vec3 stone=mix(vec3(.255,.267,.251),vec3(.40,.379,.327),smoothstep(-.7,.8,broad));
stone*=.96+strata*.045+fine*.035;
stone=mix(stone,ore?vec3(.33,.19,.10):vec3(.49,.47,.40),vein*(ore?.35:.13));
ALBEDO=stone;ROUGHNESS=ore?.83:.94;SPECULAR=.16;
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);
float det=dot(dx,tx),height=(fine*.00008+strata*.00005);
if(abs(det)>.0000000001)NORMAL=normalize(abs(det)*NORMAL-sign(det)*(dFdx(height)*tx+dFdy(height)*ty));
}"};
        var material=new ShaderMaterial {Shader=_rockShader};material.SetShaderParameter("ore",ore);_rockSurfaces[index]=material;return material;
    }
}
