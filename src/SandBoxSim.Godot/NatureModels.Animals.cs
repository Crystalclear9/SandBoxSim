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
uniform vec4 pale : source_color;uniform float predator=0.0;
varying vec3 local;
varying vec3 coat_normal;
void vertex(){ local=VERTEX;coat_normal=NORMAL; }
float coat_noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);vec2 q=vec2(127.1,311.7);return mix(mix(fract(sin(dot(i,q))*43758.5453),fract(sin(dot(i+vec2(1,0),q))*43758.5453),f.x),mix(fract(sin(dot(i+vec2(0,1),q))*43758.5453),fract(sin(dot(i+vec2(1,1),q))*43758.5453),f.x),f.y);}
void fragment(){
vec2 coat_uv=vec2(local.x*.83+local.y*.21,local.z*.81-local.y*.37);
// Strands have a growth direction and irregular ends. Isotropic noise alone
// looks like mottled plastic even on a detailed continuous skin.
float guard=(coat_noise(coat_uv*vec2(390.0,43.0))+coat_noise(coat_uv*vec2(810.0,91.0))*.35)/1.35-.5;
float footprint=max(length(dFdx(local)),length(dFdy(local)))*300.0;
guard/=1.0+footprint*footprint;
float belly=(1.0-smoothstep(mix(.86,.54,predator),mix(1.03,.72,predator),local.y))*(.65+.35*smoothstep(.10,.75,-coat_normal.y));
float saddle=smoothstep(.68,.95,local.y)*(1.0-smoothstep(.08,.22,abs(local.x)))*predator*.24;
float locks=(coat_noise(coat_uv*vec2(145.0,37.0))-.5)*.035;
float agouti=coat_noise(coat_uv*27.0+vec2(7.0,3.0))-.5;
agouti/=1.0+footprint*footprint*.04;
float flank=(coat_noise(vec2(local.z,local.y)*3.1)-.5)*.035;
float dorsal=smoothstep(mix(1.08,.78,predator),mix(1.25,.98,predator),local.y)*.13;
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX);
vec3 across=cross(dy,NORMAL),along=cross(NORMAL,dx);
float determinant=dot(dx,across);
float fiber_height=guard*mix(.000012,.000025,predator);
if(abs(determinant)>0.0000000001){
NORMAL=normalize(abs(determinant)*NORMAL-sign(determinant)*(dFdx(fiber_height)*across+dFdy(fiber_height)*along));
}
float face_pale=smoothstep(.78,.98,-local.z)*(1.0-smoothstep(.97,1.10,local.y))*predator*.42;
float brow_mask=exp(-pow((abs(local.x)-.092)*21.0,2.0)-pow((local.y-1.035)*22.0,2.0)-pow((local.z+.87)*12.0,2.0))*predator*.20;
float muzzle_shade=smoothstep(.99,1.13,-local.z)*predator*.10;
float throat=(1.0-smoothstep(-.62,-.30,local.z))*(1.0-smoothstep(1.04,1.20,local.y))*predator*.34;
float cheek=exp(-pow((local.z+.90)*6.0,2.0)-pow((local.y-.99)*12.0,2.0))*predator*.18;
float cervid_chin=(1.0-predator)*(1.0-smoothstep(-1.08,-.91,local.z))*(1.0-smoothstep(1.34,1.44,local.y))*.40;
float rump=(1.0-predator)*smoothstep(.43,.58,local.z)*(1.0-smoothstep(.07,.15,abs(local.x)))*.48;
float tear=(1.0-predator)*exp(-pow((abs(local.x)-.085)*41.0,2.0)-pow((local.y-1.474)*45.0,2.0)-pow((local.z+.883)*24.0,2.0))*.12;
vec3 base=mix(coat.rgb,pale.rgb,clamp(belly*.52+face_pale+throat+cheek+cervid_chin+rump,0.0,.78));
// Short, close hair on the face and legs; coarse guard hairs belong on the back.
float short_coat=1.0-smoothstep(mix(.46,.43,predator),mix(.78,.66,predator),local.y);
base=mix(base,coat.rgb*.73,short_coat*.32);
ALBEDO=base*COLOR.rgb*(0.97+guard*.12+agouti*.14+locks+flank-saddle-dorsal-muzzle_shade-brow_mask-tear);
ROUGHNESS=.86+guard*.06;SPECULAR=.17;
}" };
        var material = new ShaderMaterial { Shader = _furShader };
        material.SetShaderParameter("coat", new Color(wolf ? "#777971" : "#8a6d52"));
        material.SetShaderParameter("pale", new Color(wolf ? "#c3c0ae" : "#c8bca3"));
        material.SetShaderParameter("predator",wolf?1f:0f);
        _residentMaterials[key] = material; return material;
    }
    private readonly StandardMaterial3D?[] _animalEyeMaterials=new StandardMaterial3D?[2];
    private Material AnimalEyeMaterial(bool wolf)=>_animalEyeMaterials[wolf?1:0]??=new StandardMaterial3D {
        AlbedoColor=new Color(wolf?"#81754d":"#49392b"),Roughness=.22f,MetallicSpecular=.48f};
    private Mesh AnimalFoot(bool wolf)=>Loft(wolf?"animal-paw":"animal-hoof",wolf?new[]{
        new Vector4(-.125f,.021f,.004f,.005f),new(-.105f,.030f,.034f,.020f),new(-.071f,.032f,.055f,.025f),
        new(-.025f,.035f,.048f,.032f),new(.027f,.040f,.037f,.037f),new(.061f,.038f,.028f,.030f),new(.073f,.025f,.004f,.005f)
    }:new[]{
        new Vector4(-.088f,.027f,.003f,.004f),new(-.072f,.029f,.017f,.025f),new(-.023f,.040f,.019f,.035f),
        new(.024f,.041f,.017f,.036f),new(.050f,.038f,.012f,.025f),new(.061f,.031f,.003f,.004f)
    },32);
    private AnimalRig DetailedAnimal(bool wolf)
    {
        var rig = new AnimalRig {Predator=wolf}; rig.Body = new Node3D { Name = "Body" }; rig.AddChild(rig.Body);
        var fur = Fur(wolf);
        float back = wolf ? .72f : 1.03f;
        // One continuous chest/neck/back surface avoids exposed end caps and the
        // intersecting shoulder bulbs produced by independent closed lofts.
        var body = Loft(wolf ? "wolf-body" : "deer-body", new[] {
            new Vector4(-.90f,back+(wolf?.29f:.42f),.035f,.045f),
            new(-.78f,back+(wolf?.25f:.36f),wolf?.102f:.083f,wolf?.13f:.112f),
            new(-.64f,back+(wolf?.15f:.23f),wolf?.164f:.111f,wolf?.195f:.171f),
            new(-.48f,back+.050f,wolf?.180f:.146f,wolf?.245f:.250f), new(-.30f,back-.008f,wolf?.178f:.170f,wolf?.250f:.250f),
            // A continuous back line over the abdominal tuck avoids the old
            // saddle-shaped dip between rib cage and pelvis.
            new(-.08f,back+.018f,wolf?.161f:.158f,wolf?.210f:.232f), new(.18f,back+.045f,wolf?.139f:.142f,wolf?.162f:.188f), new(.39f,back+.008f,wolf?.160f:.161f,.209f),
            new(.53f,back-.025f,.135f,.175f), new(.64f,back-.04f,.008f,.015f) });
        Sculpt(rig.Body, body, Vector3.Zero, fur);
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0,back+(wolf?.28f:.42f),-.755f), Scale=Vector3.One*(wolf?.84f:.96f) }; rig.Body.AddChild(rig.Head);
        Sculpt(rig.Head, AnimalSkull(wolf), Vector3.Zero, fur);
        Sculpt(rig.Head,Loft("animal-nose:"+wolf,new[]{new Vector4(-.415f,-.039f,.008f,.008f),new(-.410f,-.039f,wolf?.028f:.021f,.018f),new(-.391f,-.038f,wolf?.036f:.028f,.023f),new(-.376f,-.040f,wolf?.031f:.025f,.021f)},24),Vector3.Zero,LeatherSurface("#292e2c"));
        Sculpt(rig.Head,Loft("animal-jaw-"+wolf,new[]{new Vector4(-.04f,-.075f,.078f,.034f),new(-.16f,-.080f,.068f,.033f),new(-.29f,-.072f,.048f,.022f),new(-.36f,-.059f,.021f,.010f)}),Vector3.Zero,fur);
        // The mouth crease follows the lower muzzle; nostrils are inset into the nose.
        foreach(float side in new[]{-1f,1f})
        {
            FineBeam(rig.Head,new(side*.055f,-.061f,-.31f),new(side*.072f,-.058f,-.19f),.003f,"#51493d");
            Detail(rig.Head,"sphere",new(side*(wolf?.021f:.016f),-.037f,-.411f),new(.008f,.005f,.003f),"#181e1a");
        }
        foreach (float side in new[] {-1f,1f})
        {
            // Recessed eye, a continuous almond-shaped lid and a small wet tear duct.
            // The old three concentric spheres gave both species a bulging toy eye.
            float eyeX=AnimalSkullSurfaceX(wolf,.038f,-.105f)-.001f;
            Detail(rig.Head,"sphere",new(side*eyeX,.038f,-.105f),new(.012f,.020f,.030f),wolf?"#83754d":"#302c22").MaterialOverride=AnimalEyeMaterial(wolf);
            Detail(rig.Head,"sphere",new(side*(eyeX+.004f),.038f,-.108f),new(.007f,wolf?.011f:.005f,.016f),"#0d1412");
            Sculpt(rig.Head,AnimalEyelid(wolf,side),Vector3.Zero,LeatherSurface(wolf?"#55594f":"#685340"));
            Detail(rig.Head,"sphere",new(side*(AnimalSkullSurfaceX(wolf,.032f,-.130f)+.001f),.032f,-.130f),new(.004f,.004f,.006f),"#342a24");
            Detail(rig.Head,"sphere",new(side*(eyeX+.007f),.043f,-.114f),Vector3.One*.0017f,"#d8ddd6");
            if(wolf)for(int whisker=0;whisker<4;whisker++)
                FineBeam(rig.Head,new(side*.051f,-.026f+whisker*.008f,-.32f+whisker*.017f),new(side*(.105f+whisker*.008f),-.013f+whisker*.010f,-.34f+whisker*.023f),.0003f,"#82897c");
            var ear=rig.Ears[side<0?0:1]=new Node3D {Name=side<0?"LeftEar":"RightEar",Position=new(side*.098f,.095f,.045f)};rig.Head.AddChild(ear);
            ear.Rotation=new(0,side*.25f,-side*(wolf?.18f:.65f));rig.EarRest[side<0?0:1]=ear.Rotation;
            Sculpt(ear,AnimalEar(wolf),Vector3.Zero,_earMaterial);
            if (!wolf)
            {
                // Swept main beam and unequal tines grow from its actual
                // control points, instead of parallel disconnected twig rows.
                var beam=new[]{new Vector3(side*.073f,.10f,.07f),new(side*.105f,.22f,.105f),
                    new(side*.18f,.39f,.18f),new(side*.255f,.55f,.17f),new(side*.27f,.67f,.08f)};
                Sculpt(rig.Head,AntlerBranch("antler-main:"+side,beam,.022f),Vector3.Zero,ResidentMaterial("#8e7c63"));
                var forks=new[]{
                    new[]{beam[1],new Vector3(side*.080f,.30f,.015f),new(side*.060f,.40f,-.065f)},
                    new[]{beam[2],new Vector3(side*.135f,.47f,.105f),new(side*.120f,.58f,.025f)},
                    new[]{beam[3],new Vector3(side*.215f,.62f,.115f),new(side*.200f,.72f,.065f)}};
                for(int tine=0;tine<forks.Length;tine++)
                    Sculpt(rig.Head,AntlerBranch("antler-tine:"+side+":"+tine,forks[tine],.011f-tine*.0018f),Vector3.Zero,ResidentMaterial("#8e7c63"));

            }
        }
        for(int i=0;i<4;i++)
        {
            float side=i%2==0 ? -1 : 1; bool rear=i>=2;
            float y=back-.13f, z=rear ? .39f : -.35f;
            var leg=rig.Legs[i]=new Node3D { Name="Leg"+i,Position=new(side*(wolf?.135f:.130f),y,z) }; rig.Body.AddChild(leg);
            Sculpt(leg,Loft("animal-upper-"+wolf+rear,new[]{new Vector4(-.12f,0,.006f,.008f),new(-.055f,.008f,rear ? .075f : .052f,.064f),new(.075f,rear ? -.012f : 0,rear ? .080f : .050f,.060f),new(.22f,rear ? -.028f : 0,.037f,.041f),new(.335f,rear ? -.04f : 0,.030f,.031f)}),Vector3.Zero,fur,new(MathF.PI/2,0,0));
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
        rig.Neck=new Node3D {Name="Neck",Position=new(0,back+.025f,-.42f)};rig.Body.AddChild(rig.Neck);
        rig.Body.RemoveChild(rig.Head);rig.Neck.AddChild(rig.Head);rig.Head.Position-=rig.Neck.Position;
        if(!_exportingAnimals)InstallAnimalSurface(rig,wolf);
        MergeResidentParts(rig,wolf ? "crafted-wolf" : "crafted-deer"); return rig;
    }
    private Mesh AnimalSkull(bool wolf)
    {
        string key="anatomical-skull:"+wolf;if(_meshes.TryGetValue(key,out var cached))return cached;
        var baseMesh=Loft(wolf?"wolf-head":"deer-head",new[]{
            new Vector4(.15f,0,.008f,.015f),new(.085f,.023f,wolf?.110f:.092f,wolf?.104f:.098f),
            new(.015f,.030f,wolf?.128f:.105f,wolf?.112f:.102f),new(-.055f,.027f,wolf?.122f:.096f,wolf?.096f:.087f),
            new(-.12f,.001f,wolf?.092f:.077f,wolf?.071f:.069f),new(-.23f,-.023f,wolf?.064f:.054f,wolf?.047f:.046f),
            new(-.33f,-.034f,wolf?.051f:.040f,wolf?.037f:.035f),new(-.38f,-.039f,wolf?.042f:.032f,.029f),new(-.40f,-.04f,.006f,.012f)},48);
        var a=baseMesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for(int i=0;i<p.Length;i++)
        {
            var v=p[i];
            float brow=MathF.Exp(-MathF.Pow((v.Z+.085f)/.050f,2)-MathF.Pow((v.Y-.070f)/.035f,2));
            float socket=MathF.Exp(-MathF.Pow((v.Z+.105f)/.038f,2)-MathF.Pow((v.Y-.038f)/.021f,2));
            float cheek=MathF.Exp(-MathF.Pow((v.Z+.005f)/.060f,2)-MathF.Pow((v.Y+.010f)/.045f,2));
            v.X+=MathF.Sign(v.X)*(brow*.004f-socket*.004f+cheek*(wolf?.009f:.003f));
            // A flatter nasal plane and a narrow bridge distinguish the long
            // cervid face from the canine stop and projecting cheekbones.
            float top=Math.Clamp((v.Y+.015f)/.10f,0,1);
            v.Y-=top*top*MathF.Exp(-MathF.Pow((v.Z+.17f)/.11f,2))*(wolf?.006f:.010f);
            p[i]=v;
        }
        a[(int)Mesh.ArrayType.Vertex]=p;
        var source=new ArrayMesh();source.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,a);
        using var tool=new SurfaceTool();tool.CreateFrom(source,0);tool.GenerateNormals();
        var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private float AnimalSkullSurfaceX(bool wolf,float y,float z)
    {
        // Place the eye and its rim against the actual cheek, rather than a
        // guessed head radius that leaves the far eye visibly floating.
        var arrays=AnimalSkull(wolf).SurfaceGetArrays(0);
        var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();float result=0;
        for(int i=0;i<indices.Length;i+=3)
        {
            var a=points[indices[i]];var b=points[indices[i+1]];var c=points[indices[i+2]];
            float d=(b.Z-c.Z)*(a.Y-c.Y)+(c.Y-b.Y)*(a.Z-c.Z);
            if(MathF.Abs(d)<1e-9f)continue;
            float u=((b.Z-c.Z)*(y-c.Y)+(c.Y-b.Y)*(z-c.Z))/d;
            float v=((c.Z-a.Z)*(y-c.Y)+(a.Y-c.Y)*(z-c.Z))/d;
            if(u>=0&&v>=0&&u+v<=1)result=MathF.Max(result,a.X*u+b.X*v+c.X*(1-u-v));
        }
        if(result<=0)throw new InvalidOperationException("Animal eye lies outside the skull surface");
        return result;
    }
}
