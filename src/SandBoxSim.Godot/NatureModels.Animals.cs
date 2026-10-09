using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Shader? _furShader;
    private Material Fur(bool wolf)
    {
        string key = wolf ? "wolf-fur" : "deer-fur";
        if (_residentMaterials.TryGetValue(key, out var cached)) return cached;
        _furShader ??= new Shader { Code = @"shader_type spatial;
uniform vec4 coat : source_color;
uniform vec4 pale : source_color;
varying vec3 local;
varying vec3 coat_normal;
void vertex(){ local=VERTEX;coat_normal=NORMAL; }
float coat_noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);vec2 q=vec2(127.1,311.7);return mix(mix(fract(sin(dot(i,q))*43758.5453),fract(sin(dot(i+vec2(1,0),q))*43758.5453),f.x),mix(fract(sin(dot(i+vec2(0,1),q))*43758.5453),fract(sin(dot(i+vec2(1,1),q))*43758.5453),f.x),f.y);}
void fragment(){
float guard=sin(local.x*270.0+local.z*91.0)*sin(local.y*310.0);
float footprint=max(length(dFdx(local)),length(dFdy(local)))*300.0;
guard/=1.0+footprint*footprint;
float belly=smoothstep(0.20,0.85,-coat_normal.y);
float saddle=smoothstep(0.25,0.90,coat_normal.y)*0.055;
float locks=0.0;
float agouti=coat_noise(vec2(local.x+local.z*.71,local.y)*75.0)-.5;
agouti/=1.0+footprint*footprint*.04;
float flank=(coat_noise(vec2(local.z,local.y)*3.1)-.5)*.025;
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX);
vec3 across=cross(dy,NORMAL),along=cross(NORMAL,dx);
float determinant=dot(dx,across);
float fiber_height=guard*0.00006;
if(abs(determinant)>0.0000000001){
NORMAL=normalize(abs(determinant)*NORMAL-sign(determinant)*(dFdx(fiber_height)*across+dFdy(fiber_height)*along));
}
ALBEDO=mix(coat.rgb,pale.rgb,belly*0.42)*(0.97+guard*0.045+agouti*.025+locks+flank-saddle);
ROUGHNESS=0.81+guard*0.025; SPECULAR=0.20;
}" };
        var material = new ShaderMaterial { Shader = _furShader };
        material.SetShaderParameter("coat", new Color(wolf ? "#747b7b" : "#8c6445"));
        material.SetShaderParameter("pale", new Color(wolf ? "#c1c4bb" : "#d7c6ad"));
        _residentMaterials[key] = material; return material;
    }
    private Mesh AnimalFoot(bool wolf)=>Loft(wolf?"animal-paw":"animal-hoof",wolf?new[]{
        new Vector4(-.125f,.021f,.004f,.005f),new(-.105f,.030f,.034f,.020f),new(-.071f,.032f,.055f,.025f),
        new(-.025f,.035f,.048f,.032f),new(.027f,.040f,.037f,.037f),new(.061f,.038f,.028f,.030f),new(.073f,.025f,.004f,.005f)
    }:new[]{
        new Vector4(-.088f,.027f,.003f,.004f),new(-.072f,.029f,.017f,.025f),new(-.023f,.040f,.019f,.035f),
        new(.024f,.041f,.017f,.036f),new(.050f,.038f,.012f,.025f),new(.061f,.031f,.003f,.004f)
    },32);
    private AnimalRig DetailedAnimal(bool wolf)
    {
        var rig = new AnimalRig(); rig.Body = new Node3D { Name = "Body" }; rig.AddChild(rig.Body);
        var fur = Fur(wolf);
        float back = wolf ? .72f : 1.03f;
        // One continuous chest/neck/back surface avoids exposed end caps and the
        // intersecting shoulder bulbs produced by independent closed lofts.
        var body = Loft(wolf ? "wolf-body" : "deer-body", new[] {
            new Vector4(-.90f,back+(wolf?.29f:.42f),.035f,.045f),
            new(-.78f,back+(wolf?.27f:.36f),.09f,.12f),
            new(-.64f,back+(wolf?.15f:.23f),.125f,.18f),
            new(-.48f,back+.05f,.18f,.25f), new(-.30f,back,.215f,.255f),
            new(-.08f,back-.025f,.215f,.245f), new(.18f,back-.025f,.19f,.22f), new(.39f,back-.03f,.20f,.24f),
            new(.53f,back-.04f,.16f,.20f), new(.64f,back-.04f,.008f,.015f) });
        Sculpt(rig.Body, body, Vector3.Zero, fur);
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0,back+(wolf?.28f:.42f),-.755f), Scale=Vector3.One*(wolf?.84f:.96f) }; rig.Body.AddChild(rig.Head);
        Sculpt(rig.Head, Loft(wolf ? "wolf-head" : "deer-head", new[] {
            new Vector4(.15f,0,.008f,.015f), new(.06f,.025f,wolf?.135f:.108f,wolf?.123f:.119f), new(-.055f,.030f,wolf?.126f:.100f,.116f), new(-.12f,.010f,wolf?.102f:.085f,.087f),
            new(-.23f,-.021f,wolf?.074f:.059f,wolf?.067f:.060f), new(-.36f,-.04f,wolf?.052f:.043f,.044f), new(-.40f,-.04f,.006f,.012f) }), Vector3.Zero, fur);
        Sculpt(rig.Head,Loft("animal-nose:"+wolf,new[]{new Vector4(-.415f,-.039f,.008f,.008f),new(-.410f,-.039f,.035f,.023f),new(-.391f,-.038f,.046f,.030f),new(-.376f,-.040f,.037f,.025f)},24),Vector3.Zero,LeatherSurface("#292e2c"));
        Sculpt(rig.Head,Loft("animal-jaw-"+wolf,new[]{new Vector4(-.04f,-.075f,.078f,.034f),new(-.16f,-.080f,.068f,.033f),new(-.29f,-.072f,.048f,.022f),new(-.36f,-.059f,.021f,.010f)}),Vector3.Zero,fur);
        // The mouth crease follows the lower muzzle; nostrils are inset into the nose.
        foreach(float side in new[]{-1f,1f})
        {
            FineBeam(rig.Head,new(side*.055f,-.061f,-.31f),new(side*.072f,-.058f,-.19f),.003f,"#51493d");
            Detail(rig.Head,"sphere",new(side*.026f,-.037f,-.415f),new(.012f,.007f,.004f),"#181e1a");
        }
        foreach (float side in new[] {-1f,1f})
        {
            Detail(rig.Head,"sphere",new(side*(wolf?.122f:.100f),.037f,-.102f),new(.009f,.025f,.036f),wolf?"#696d69":"#8b725d");
            Detail(rig.Head,"sphere",new(side*(wolf?.125f:.103f),.038f,-.105f),new(.008f,.017f,.026f),wolf?"#8b825d":"#473a2c");
            Detail(rig.Head,"sphere",new(side*(wolf?.129f:.107f),.038f,-.106f),new(.005f,.014f,.012f),"#111917");
            Detail(rig.Head,"sphere",new(side*(wolf?.132f:.110f),.042f,-.110f),Vector3.One*.0025f,"#e7ebe5");
            var ear=rig.Ears[side<0?0:1]=new Node3D {Name=side<0?"LeftEar":"RightEar",Position=new(side*.098f,.095f,.045f)};rig.Head.AddChild(ear);
            ear.Rotation=new(0,side*.25f,-side*(wolf?.18f:.65f));rig.EarRest[side<0?0:1]=ear.Rotation;
            Sculpt(ear,AnimalEar(wolf),Vector3.Zero,_earMaterial);
            if (!wolf)
            {
                Sculpt(rig.Head,AntlerBranch("antler-main:"+side,new[]{new Vector3(side*.073f,.10f,.07f),new(side*.14f,.27f,.12f),new(side*.22f,.47f,.16f),new(side*.29f,.64f,.10f)},.023f),Vector3.Zero,ResidentMaterial("#8e7c63"));
                for(int i=0;i<3;i++)
                    Sculpt(rig.Head,AntlerBranch("antler-tine:"+side+":"+i,new[]{new Vector3(side*(.12f+i*.047f),.24f+i*.12f,.10f),new(side*(.13f+i*.06f),.34f+i*.12f,.025f),new(side*(.15f+i*.07f),.44f+i*.12f,-.06f)},.012f-i*.002f),Vector3.Zero,ResidentMaterial("#8e7c63"));
            }
        }
        for(int i=0;i<4;i++)
        {
            float side=i%2==0 ? -1 : 1; bool rear=i>=2;
            float y=back-.13f, z=rear ? .39f : -.35f;
            var leg=rig.Legs[i]=new Node3D { Name="Leg"+i,Position=new(side*.17f,y,z) }; rig.Body.AddChild(leg);
            Sculpt(leg,Loft("animal-upper-"+wolf+rear,new[]{new Vector4(-.12f,0,.006f,.008f),new(-.055f,.008f,rear ? .095f : .065f,.073f),new(.075f,rear ? -.012f : 0,rear ? .10f : .061f,.073f),new(.22f,rear ? -.028f : 0,.043f,.047f),new(.335f,rear ? -.04f : 0,.034f,.034f)}),Vector3.Zero,fur,new(MathF.PI/2,0,0));
            var knee=rig.Knees[i]=new Node3D { Name="Knee", Position=new(0,-.32f,rear ? -.04f : 0) }; leg.AddChild(knee);
            float lower=y-.32f;
            Sculpt(knee,Loft("animal-shin-"+wolf+rear,new[]{new Vector4(-.025f,0,.036f,.035f),new(lower*.22f,rear ? .040f : .012f,.031f,.031f),new(lower*.58f,rear ? .055f : .017f,.021f,.023f),new(lower-.025f,-.025f,.025f,.028f)}),Vector3.Zero,fur,new(MathF.PI/2,0,0));
            if(wolf)
                Sculpt(knee,AnimalFoot(true),new(0,-lower+.004f,-.025f),fur);
            else foreach(float hoofSide in new[]{-1f,1f})
                Sculpt(knee,AnimalFoot(false),new(hoofSide*.018f,-lower+.004f,-.020f),LeatherSurface("#393a33"));

        }
        rig.Tail=new Node3D { Name="Tail",Position=new(0,back,.55f) }; rig.Body.AddChild(rig.Tail);
        Sculpt(rig.Tail,Loft("animal-tail:"+wolf,wolf?new[]{new Vector4(0,0,.065f,.07f),new(.09f,-.05f,.080f,.095f),new(.20f,-.15f,.085f,.11f),new(.32f,-.27f,.062f,.09f),new(.39f,-.34f,.005f,.009f)}:new[]{new Vector4(0,0,.040f,.045f),new(.07f,.02f,.054f,.060f),new(.14f,.00f,.040f,.045f),new(.19f,-.025f,.003f,.006f)}),Vector3.Zero,fur);
        MergeResidentParts(rig,wolf ? "crafted-wolf" : "crafted-deer"); return rig;
    }
}
