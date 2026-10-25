using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _handSurfaceShader,_nailSurfaceShader;
    private Material HandSurfaceMaterial(string skin,bool lowDetail)
    {
        string key="hand-surface:"+skin+":"+lowDetail;
        if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _handSurfaceShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;uniform bool detailed=true;
float line(float distance,float width){float w=max(width,fwidth(distance)*.70);return exp(-pow(distance/w,2.0));}
void fragment(){
// UV2 is the canonical, unposed source hand in metres. It follows skinning
// and the rigid distant poses without sliding or changing handedness.
vec2 p=UV2;float dorsal=smoothstep(.35,.70,COLOR.r);
float palmar=1.0-smoothstep(.30,.65,COLOR.r);
float hand=1.0-smoothstep(.025,.070,p.y);
float crease=0.0,knuckle=0.0,pad=0.0;
if(detailed&&hand>.001){
 for(int digit=0;digit<4;digit++){
  float x=-.022+float(digit)*.014;
  float scale=digit==0?.92:(digit==1?1.0:(digit==2?.96:.86));
  float across=exp(-pow((p.x-x)/.0054,4.0));
  float mcp=p.y+.040,pip=p.y+.040+.026*scale,dip=p.y+.040+.044*scale;
  float folds=line(pip,.00030)+line(dip,.00025)+line(mcp,.00028)*.65;
  // Broad dorsal joint colour, fine palmar flexion folds. Avoid dark rings
  // around the complete digit: both are gated by the original surface side.
  crease+=across*folds*(palmar+dorsal*.22);
  knuckle+=across*(exp(-pow(mcp/.0045,2.0))+exp(-pow(pip/.0034,2.0))*.7)*dorsal;
  pad+=across*exp(-pow((p.y+.040+.053*scale)/.0055,2.0))*palmar;
 }
 float transverse_a=p.y+.025-.18*p.x-2.8*p.x*p.x;
 float transverse_b=p.y+.008+.30*p.x-3.4*p.x*p.x;
 float span=1.0-smoothstep(.025,.035,abs(p.x));
 float thenar=abs(length((p-vec2(-.030,.004))/vec2(.028,.034))-1.0)*.028;
 float thenar_gate=smoothstep(-.026,-.018,p.x)*(1.0-smoothstep(.012,.023,p.y))*smoothstep(-.037,-.029,p.y);
 float wrist=line(p.y-.033-p.x*.08,.00028)*(1.0-smoothstep(.021,.029,abs(p.x)));
 crease+=(line(transverse_a,.00028)+line(transverse_b,.00024))*.75*span*palmar;
 crease+=line(thenar,.00029)*thenar_gate*palmar*.75+wrist*palmar*.55;
}
crease=clamp(crease,0.0,1.35)*hand;knuckle=clamp(knuckle,0.0,1.0)*hand;
vec3 palmar_tone=vec3(1.075,1.018,.985);
vec3 colour=tone.rgb*mix(vec3(1.0),palmar_tone,palmar*hand*.65);
colour*=1.0-crease*.12;
colour=mix(colour,colour*vec3(1.075,.952,.938),clamp(knuckle*.40+pad*.22,0.0,.5));
ALBEDO=colour;ROUGHNESS=.65+palmar*hand*.065+crease*.025-knuckle*.025;SPECULAR=.25;
// Only the measured flexion lines contribute relief; no uniform noisy bump.
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float det=dot(dx,tx),height=-crease*.000045;
if(detailed&&abs(det)>.0000000001)NORMAL=normalize(abs(det)*NORMAL-sign(det)*(dFdx(height)*tx+dFdy(height)*ty));
}"};
        var material=new ShaderMaterial {Shader=_handSurfaceShader};material.SetShaderParameter("tone",new Color(skin));material.SetShaderParameter("detailed",!lowDetail);
        material.SetMeta("HandSurfaceProfile","anatomical-regions-v1");material.SetMeta("HandSurfaceDetail",!lowDetail);
        return _residentMaterials[key]=material;
    }
    private Material NailSurfaceMaterial(string skin,bool lowDetail)
    {
        string key="nail-surface:"+skin+":"+lowDetail;
        if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _nailSurfaceShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 bed:source_color;uniform bool detailed=true;
void fragment(){
vec2 p=UV2;
float edge=smoothstep(.76,.96,p.y);
float crescent=1.0-smoothstep(.94,1.08,length(vec2(p.x/.72,(p.y+1.05)/.55)));
float cuticle=exp(-pow((p.y+.91)/.10,2.0))*(1.0-smoothstep(.60,1.0,abs(p.x)));
// Keep short natural nails in the bed's linear colour range. A fixed pale
// linear RGB edge reads as white paint under the gallery's directional light.
vec3 ivory=bed.rgb*vec3(1.08,1.055,1.02);
vec3 colour=mix(bed.rgb,ivory,edge*.62);
colour=mix(colour,ivory,crescent*.22);
ALBEDO=colour*(1.0-cuticle*.10);ROUGHNESS=.56+cuticle*.08+edge*.04;SPECULAR=.18;
if(detailed){
 float phase=p.x*47.0,shaft=sin(phase)/(1.0+pow(fwidth(phase),2.0));
 vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float det=dot(dx,tx),height=shaft*.0000008;
 if(abs(det)>.0000000001)NORMAL=normalize(abs(det)*NORMAL-sign(det)*(dFdx(height)*tx+dFdy(height)*ty));
}
}"};
        var material=new ShaderMaterial {Shader=_nailSurfaceShader};material.SetShaderParameter("bed",new Color(skin).Lerp(new Color("#c29a91"),.28f));material.SetShaderParameter("detailed",!lowDetail);
        material.SetMeta("HandSurfaceProfile","nail-bed-edge-v1");return _residentMaterials[key]=material;
    }

    private static Vector2 HandNailCoordinates(Vector3 point)
    {
        float best=float.PositiveInfinity;var result=Vector2.Zero;
        for(int digit=0;digit<5;digit++)
        {
            bool thumb=digit==4;float scale=digit==0?.92f:digit==1?1:digit==2?.96f:digit==3?.86f:1;
            float x=thumb?-.034f:-.022f+digit*.014f,origin=thumb?-.021f:-.040f,total=(thumb?.034f:.058f)*scale;
            var q=new Vector2((point.X-x)/(thumb?.0043f:.0038f),(origin-point.Y-(total-.007f*scale))/(.006f*scale));
            if(q.LengthSquared()<best){best=q.LengthSquared();result=q;}
        }
        return result;
    }
}
