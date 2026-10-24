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
float coat_fiber(vec2 p){
// Continuous stochastic strands: cell boundaries in a per-cell filament
// pattern formed a visible woven grid and unstable derivative normals.
float bend=coat_noise(p*.045)*1.7;
float shaft=coat_noise(vec2(p.x+bend,p.y*.16));
float broken=coat_noise(vec2(p.x*1.9+bend,p.y*.42));
return (shaft-.5)*.75+(broken-.5)*.25;
}
void fragment(){
// Blend three projections: the former single XY/Z projection stretched
// coat noise into broad parallel bands on the chest and legs.
vec3 cw=pow(abs(normalize(coat_normal)),vec3(4.0));cw/=max(.001,cw.x+cw.y+cw.z);
vec3 cp=local*vec3(1.0,1.0,1.0);
float guard=dot(vec3(coat_noise(cp.yz*vec2(410,180)),coat_noise(cp.xz*vec2(380,165)),coat_noise(cp.xy*vec2(430,190))),cw)-.5;
float collar_flow=exp(-pow((local.z+.58)/.26,2.0));
float side_fiber=mix(coat_fiber(vec2((cp.y+cp.z*.30)*490.0,cp.z*105.0)),coat_fiber(vec2((cp.z+cp.y*.15)*490.0,cp.y*105.0)),collar_flow);
float filaments=dot(vec3(side_fiber,coat_fiber(cp.xz*vec2(510,110)),coat_fiber(cp.xy*vec2(510,110))),cw);
float footprint=max(length(dFdx(local)),length(dFdy(local)))*340.0;
guard/=1.0+footprint*footprint;
filaments/=1.0+footprint*footprint*.4;
float belly=(1.0-smoothstep(mix(.86,.54,predator),mix(1.03,.72,predator),local.y))*(.65+.35*smoothstep(.10,.75,-coat_normal.y));
float saddle=smoothstep(.68,.95,local.y)*(1.0-smoothstep(.08,.22,abs(local.x)))*smoothstep(-.68,-.36,local.z)*predator*.24;
float locks=(coat_noise(local.zy*vec2(98.0,143.0))-.5)*.025;
float agouti=dot(vec3(coat_noise(cp.yz*62.0),coat_noise(cp.xz*62.0),coat_noise(cp.xy*62.0)),cw)-.5;
agouti/=1.0+footprint*footprint*.04;
float flank=(coat_noise(vec2(local.z,local.y)*3.1)-.5)*.035;
float dorsal=smoothstep(mix(1.08,.78,predator),mix(1.25,.98,predator),local.y)*.09;
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX);
vec3 across=cross(dy,NORMAL),along=cross(NORMAL,dx);
float determinant=dot(dx,across);
float fiber_height=guard*mix(.00004,.00006,predator);
if(abs(determinant)>0.0000000001){
NORMAL=normalize(abs(determinant)*NORMAL-sign(determinant)*(dFdx(fiber_height)*across+dFdy(fiber_height)*along));
}
float face_pale=smoothstep(.78,.98,-local.z)*(1.0-smoothstep(.99,1.045,local.y))*predator*.30;
float brow_mask=exp(-pow((abs(local.x)-.092)*21.0,2.0)-pow((local.y-1.035)*22.0,2.0)-pow((local.z+.87)*12.0,2.0))*predator*.20;
float muzzle_shade=smoothstep(.99,1.13,-local.z)*predator*.10;
float throat=(1.0-smoothstep(-.62,-.30,local.z))*(1.0-smoothstep(.93,1.035,local.y))*predator*.27;
float cheek=exp(-pow((local.z+.90)*6.0,2.0)-pow((local.y-.99)*12.0,2.0))*predator*.24;
float cervid_chin=(1.0-predator)*(1.0-smoothstep(-1.08,-.91,local.z))*(1.0-smoothstep(1.34,1.44,local.y))*.40;
float rump=(1.0-predator)*smoothstep(.43,.58,local.z)*(1.0-smoothstep(.07,.15,abs(local.x)))*.48;
float crown=exp(-pow((local.z+.82)*6.0,2.0))*smoothstep(1.00,1.10,local.y)*predator*.10;
float mask=exp(-pow((local.z+.88)*5.0,2.0)-pow((local.y-1.025)*12.0,2.0))*predator;
float ruff=exp(-pow((local.z+.55)*5.0,2.0)-pow((local.y-.88)*7.0,2.0))*predator;
float tail_tip=smoothstep(.73,.91,local.z)*predator;
float cervid_mane=(1.0-predator)*exp(-pow((local.z+.62)*5.0,2.0)-pow((local.y-1.18)*6.0,2.0));
float tear=(1.0-predator)*exp(-pow((abs(local.x)-.085)*41.0,2.0)-pow((local.y-1.474)*45.0,2.0)-pow((local.z+.883)*24.0,2.0))*.12;
vec3 base=mix(coat.rgb,pale.rgb,clamp(belly*.52+face_pale+throat+cheek+cervid_chin+rump,0.0,.78));
// Short, close hair on the face and legs; coarse guard hairs belong on the back.
float short_coat=1.0-smoothstep(mix(.46,.43,predator),mix(.78,.66,predator),local.y);
base=mix(base,coat.rgb*.73,short_coat*.32);
base=mix(base,pale.rgb,ruff*.12);
float warm_guard=predator*exp(-pow((local.y-.78)*5.0,2.0))*(.5+.5*coat_noise(local.zy*8.0));
base=mix(base,coat.rgb*vec3(1.14,1.02,.84),warm_guard*.22);
base*=1.0-mask*.10-tail_tip*.27-cervid_mane*.20-crown;
ALBEDO=base*COLOR.rgb*(0.96+filaments*.055+guard*.07+agouti*.10+locks*.45+flank*.45-saddle-dorsal-muzzle_shade-brow_mask-tear);
ROUGHNESS=.93+guard*.04;SPECULAR=.13;
}" };
        var material = new ShaderMaterial { Shader = _furShader };
        material.SetShaderParameter("coat", new Color(wolf ? "#777971" : "#8a6d52"));
        material.SetShaderParameter("pale", new Color(wolf ? "#c3c0ae" : "#c8bca3"));
        material.SetShaderParameter("predator",wolf?1f:0f);
        _residentMaterials[key] = material; return material;
    }
    private readonly ShaderMaterial?[] _animalEyeMaterials=new ShaderMaterial?[2];
    private Material AnimalEyeMaterial(bool wolf)
    {
        int index=wolf?1:0;if(_animalEyeMaterials[index]!=null)return _animalEyeMaterials[index]!;
        var material=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform vec4 iris:source_color;
uniform float predator=0.0;
uniform float blink=0.0;
void vertex(){VERTEX.xy+=UV2*blink;}
void fragment(){
if(blink>.995)discard;
vec2 p=(UV-vec2(.5))*vec2(.044,.026);
float radius=length(p),angle=atan(p.y,p.x);
float fiber=sin(angle*53.0+sin(radius*2400.0)*.7)*sin(angle*97.0+radius*800.0);
fiber/=1.0+pow(fwidth(angle)*75.0,2.0);
float iris_radius=mix(.017,.0145,predator);
float iris_edge=1.0-smoothstep(iris_radius-.001,iris_radius,radius);
float pupil=1.0-smoothstep(.85,1.05,length(p/mix(vec2(.0085,.0028),vec2(.0065),predator)));
vec3 pigment=iris.rgb*(.78+fiber*.10+smoothstep(.004,.008,radius)*.22);
ALBEDO=mix(iris.rgb*.35,pigment,iris_edge);
ALBEDO=mix(ALBEDO,vec3(.0025,.003,.002),pupil);
ALBEDO*=1.0-smoothstep(.001,.011,p.y)*.35;
ROUGHNESS=.13;SPECULAR=.23*pow(1.0-blink,2.0);
}"}};
        material.SetMeta("UsesAnimalEyeMorph",true);material.SetMeta("AnimalEyePart","cornea");
        material.SetShaderParameter("iris",new Color(wolf?"#6f643f":"#685038"));
        material.SetShaderParameter("predator",wolf?1f:0f);return _animalEyeMaterials[index]=material;
    }
    private Mesh AnimalFoot(bool wolf)=>Loft(wolf?"animal-paw":"animal-hoof",wolf?new[]{
        new Vector4(-.093f,.030f,.004f,.005f),new(-.081f,.032f,.028f,.017f),new(-.056f,.034f,.039f,.025f),
        new(-.025f,.035f,.041f,.032f),new(.027f,.040f,.037f,.037f),new(.061f,.038f,.028f,.030f),new(.073f,.025f,.004f,.005f)
    }:new[]{
        new Vector4(-.076f,.013f,.004f,.006f),new(-.067f,.018f,.018f,.012f),new(-.033f,.026f,.020f,.020f),
        new(.004f,.034f,.017f,.028f),new(.027f,.040f,.011f,.026f),new(.039f,.042f,.003f,.010f)
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
            new(-.64f,back+(wolf?.13f:.23f),wolf?.142f:.133f,wolf?.153f:.190f),
            new(-.48f,back+.040f,wolf?.153f:.161f,wolf?.211f:.272f), new(-.30f,back-.008f,wolf?.149f:.176f,wolf?.211f:.263f),
            // A continuous back line over the abdominal tuck avoids the old
            // saddle-shaped dip between rib cage and pelvis.
            new(-.08f,back+.018f,wolf?.141f:.158f,wolf?.185f:.232f), new(.18f,back+.045f,wolf?.124f:.143f,wolf?.142f:.192f), new(.39f,back+.008f,wolf?.153f:.170f,wolf?.190f:.220f),
            new(.53f,back-.025f,wolf?.124f:.144f,wolf?.159f:.180f), new(.64f,back-.04f,.008f,.015f) });
        Sculpt(rig.Body, body, Vector3.Zero, fur);
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0,back+(wolf?.28f:.42f),-.755f), Scale=wolf?new Vector3(.96f,1.02f,.90f):new Vector3(.98f,.96f,1.08f) }; rig.Body.AddChild(rig.Head);
        Sculpt(rig.Head, AnimalSkull(wolf), Vector3.Zero, fur);
        Sculpt(rig.Head,AnimalNoseMesh(wolf),Vector3.Zero,AnimalNoseMaterial());
        // The mouth crease follows the lower muzzle; nostrils are inset into the nose.
        foreach(float side in new[]{-1f,1f})
        {
            // Fit the lip crease to the skull surface rather than drawing a
            // straight stick through the lower jaw.
            Vector3 Lip(int sample)
            {
                float t=sample/10f;var point=AnimalMuzzlePoint(wolf,new(0,-.043f+.004f*t,Mathf.Lerp(-.355f,-.155f,t)));float z=point.Z,y=point.Y;
                return new(side*(AnimalSkullSurfaceX(wolf,y,z)+.0007f),y,z);
            }
            for(int lip=0;lip<10;lip++)FineBeam(rig.Head,Lip(lip),Lip(lip+1),.0009f,"#493e34");
            Detail(rig.Head,"sphere",AnimalMuzzlePoint(wolf,new(side*(wolf?.021f:.016f),-.037f,-.411f)),new(.008f,.005f,.003f),"#181e1a");
        }
        foreach (float side in new[] {-1f,1f})
        {
            // Recessed eye, a continuous almond-shaped lid and a small wet tear duct.
            // The old three concentric spheres gave both species a bulging toy eye.
            Sculpt(rig.Head,AnimalEyeSurface(wolf,side),Vector3.Zero,AnimalEyeMaterial(wolf));
            Sculpt(rig.Head,AnimalEyelid(wolf,side),Vector3.Zero,AnimalLidMaterial(wolf));
            if(wolf)for(int whisker=0;whisker<4;whisker++)
            {
                var root=AnimalMuzzlePoint(wolf,new(0,-.026f+whisker*.008f,-.32f+whisker*.017f));
                root.X=side*(AnimalSkullSurfaceX(wolf,root.Y,root.Z)+.0006f);
                Vector3 Point(float t)=>root+new Vector3(side*(.043f+whisker*.005f)*t,.013f*t*t,-.018f*t);
                for(int segment=0;segment<3;segment++)FineBeam(rig.Head,Point(segment/3f),Point((segment+1)/3f),.00018f*(1-segment*.22f),"#383d34");
            }
            float earY=wolf?.040f:.050f,earZ=wolf?.045f:.105f;
            float earX=AnimalSkullSurfaceX(wolf,earY,earZ)*.78f;
            var ear=rig.Ears[side<0?0:1]=new Node3D {Name=side<0?"LeftEar":"RightEar",Position=new(side*earX,earY,earZ)};rig.Head.AddChild(ear);
            ear.Rotation=new(0,side*.25f,-side*(wolf?.18f:.92f));rig.EarRest[side<0?0:1]=ear.Rotation;
            Sculpt(ear,AnimalEar(wolf),Vector3.Zero,AnimalEarMaterial(wolf));
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
            var leg=rig.Legs[i]=new Node3D { Name="Leg"+i,Position=new(side*(wolf?.108f:.130f),y,z) }; rig.Body.AddChild(leg);
            Sculpt(leg,Loft("animal-upper-"+wolf+rear,new[]{new Vector4(-.12f,0,.006f,.008f),new(-.055f,.008f,rear ? .086f : .058f,rear?.082f:.072f),
                new(.075f,rear ? -.025f : .004f,rear ? .089f : .052f,rear?.081f:.066f),
                new(.17f,rear ? -.046f : .009f,rear?.062f:.041f,rear?.060f:.047f),
                new(.265f,rear ? -.049f : .007f,rear?.035f:.030f,.032f),new(.335f,rear ? -.04f : 0,.026f,.028f)}),Vector3.Zero,fur,new(MathF.PI/2,0,0));
            var knee=rig.Knees[i]=new Node3D { Name="Knee", Position=new(0,-.32f,rear ? -.04f : 0) }; leg.AddChild(knee);
            float lower=y-.32f;
            Sculpt(knee,Loft("animal-shin-"+wolf+rear,new[]{new Vector4(-.025f,0,.036f,.035f),new(lower*.20f,rear ? .046f : .010f,rear?.030f:.025f,rear?.034f:.029f),
                new(lower*.48f,rear ? .076f : .015f,rear?.024f:.019f,rear?.029f:.022f),
                new(lower*.68f,rear ? .065f : .008f,.018f,.021f),
                new(lower*.87f,rear ? .017f : -.012f,.019f,.022f),new(lower-.025f,-.025f,.024f,.027f)}),Vector3.Zero,fur,new(MathF.PI/2,0,0));
            if(wolf)
            {
                Sculpt(knee,AnimalFoot(true),new(0,-lower+.004f,-.025f),fur);
                AddCanineToes(knee,new(0,-lower+.004f,-.025f),fur);
            }
            else foreach(float hoofSide in new[]{-1f,1f})
            {
                Sculpt(knee,AnimalFoot(false),new(hoofSide*.019f,-lower+.004f,-.020f),HoofMaterial());
                Sculpt(knee,Loft("cervid-dewclaw",new[]{new Vector4(-.005f,0,.004f,.007f),new(.006f,-.002f,.006f,.010f),new(.018f,-.009f,.001f,.003f)},16),
                    new(hoofSide*.019f,-lower+.090f,.017f),HoofMaterial());
            }

        }
        rig.Tail=new Node3D { Name="Tail",Position=new(0,back,.55f) }; rig.Body.AddChild(rig.Tail);
        Sculpt(rig.Tail,Loft("animal-tail:"+wolf,wolf?new[]{new Vector4(0,0,.065f,.07f),new(.09f,-.05f,.080f,.095f),new(.20f,-.15f,.085f,.11f),new(.32f,-.27f,.062f,.09f),new(.39f,-.34f,.005f,.009f)}:new[]{new Vector4(0,0,.040f,.045f),new(.07f,.02f,.054f,.060f),new(.14f,.00f,.040f,.045f),new(.19f,-.025f,.003f,.006f)}),Vector3.Zero,fur);
        rig.Neck=new Node3D {Name="Neck",Position=new(0,back+.025f,-.42f)};rig.Body.AddChild(rig.Neck);
        rig.Body.RemoveChild(rig.Head);rig.Neck.AddChild(rig.Head);rig.Head.Position-=rig.Neck.Position;
        if(!_exportingAnimals)InstallAnimalSurface(rig,wolf);
        MergeResidentParts(rig,wolf ? "crafted-wolf" : "crafted-deer");
        if(!_exportingAnimals)InstallAnimalBlink(rig);return rig;
    }
    // Keep every muzzle attachment in the same anatomical coordinate mapping.
    // Eyes, ears and the head pivot lie behind the compression boundary.
    private static Vector3 AnimalMuzzlePoint(bool wolf,Vector3 point)
    {
        // Cervid facial bones slope down to the nose; their jaw is not a
        // horizontal beak. Apply this to skin and every muzzle attachment.
        if(!wolf&&point.Z<-.095f)point.Y+=(point.Z+.095f)*.28f;
        float boundary=wolf?-.16f:-.13f;
        if(point.Z<boundary)point.Z=boundary+(point.Z-boundary)*(wolf?.96f:.76f);
        return point;
    }
    private Mesh AnimalSkull(bool wolf)
    {
        string key="anatomical-skull:"+wolf;if(_meshes.TryGetValue(key,out var cached))return cached;
        var baseMesh=Loft(wolf?"wolf-head":"deer-head",new[]{
            new Vector4(.15f,0,.008f,.015f),new(.085f,.015f,wolf?.103f:.084f,wolf?.092f:.080f),
            new(.015f,.025f,wolf?.122f:.095f,wolf?.098f:.093f),new(-.055f,.024f,wolf?.118f:.087f,wolf?.093f:.088f),
            new(-.12f,wolf?.004f:.002f,wolf?.083f:.073f,wolf?.069f:.063f),
            new(-.175f,wolf?-.012f:-.011f,wolf?.071f:.063f,wolf?.055f:.051f),
            new(-.23f,wolf?-.022f:-.017f,wolf?.077f:.056f,wolf?.052f:.052f),
            new(-.33f,-.029f,wolf?.067f:.045f,wolf?.043f:.042f),new(-.38f,-.039f,wolf?.051f:.035f,.034f),new(-.40f,-.04f,.006f,.012f)},48);
        var a=baseMesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for(int i=0;i<p.Length;i++)
        {
            var v=p[i];
            float brow=MathF.Exp(-MathF.Pow((v.Z+.085f)/.050f,2)-MathF.Pow((v.Y-.070f)/.035f,2));
            float socket=MathF.Exp(-MathF.Pow((v.Z-AnimalEyeCenterZ(wolf))/.047f,2)-MathF.Pow((v.Y-.038f)/.026f,2));
            float cheek=MathF.Exp(-MathF.Pow((v.Z+.005f)/.060f,2)-MathF.Pow((v.Y+.010f)/.045f,2));
            v.X+=MathF.Sign(v.X)*(brow*(wolf?.008f:.006f)-socket*(wolf?.010f:.008f)+cheek*(wolf?.009f:.003f));
            // A flatter nasal plane and a narrow bridge distinguish the long
            // cervid face from the canine stop and projecting cheekbones.
            float top=Math.Clamp((v.Y+.015f)/.10f,0,1);
            v.Y-=top*top*MathF.Exp(-MathF.Pow((v.Z+.17f)/.11f,2))*(wolf?.006f:.010f);
            // Preserve a visible frontal stop above the canine nasal plane.
            // Flatten only the rear crown, without erasing the orbital brow.
            if(wolf && v.Y>.055f)
            {
                float crown=Math.Clamp((v.Y-.055f)/.07f,0,1);
                v.Y-=.020f*crown*crown*(3-2*crown);
            }
            float masseter=MathF.Exp(-MathF.Pow((v.Z-.005f)/.070f,2)-MathF.Pow((v.Y+.035f)/.034f,2));
            v.X+=MathF.Sign(v.X)*masseter*(wolf?.007f:.002f);
            // Supraorbital ridge and nasal planes remain part of the skull.
            float bridge=MathF.Exp(-MathF.Pow((v.Z+.235f)/.085f,2))*top;
            v.Y+=bridge*(wolf?.003f:.002f);
            float cheekPlane=MathF.Exp(-MathF.Pow((v.Z+.035f)/.065f,2))*MathF.Exp(-MathF.Pow((v.Y+.028f)/.04f,2));
            v.X+=MathF.Sign(v.X)*cheekPlane*(wolf?.006f:.002f);
            // Shape the mandible into the closed skull surface. A second
            // overlapping jaw loft created the projecting chin and cap highlights.
            float lower=Math.Clamp(-v.Y/.075f,0,1);
            v.Y-=lower*MathF.Exp(-MathF.Pow((v.Z+.20f)/.16f,2))*.004f;
            p[i]=AnimalMuzzlePoint(wolf,v);
        }
        a[(int)Mesh.ArrayType.Vertex]=p;
        var source=new ArrayMesh();source.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,a);
        using var tool=new SurfaceTool();tool.CreateFrom(source,0);tool.GenerateNormals();
        var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private readonly (Vector3[] Points,int[] Indices)?[] _animalSkullSamples=new (Vector3[] Points,int[] Indices)?[2];
    private float AnimalSkullSurfaceX(bool wolf,float y,float z)
    {
        // Place the eye and its rim against the actual cheek, rather than a
        // guessed head radius that leaves the far eye visibly floating.
        // Both lids and corneas sample this immutable mesh hundreds of times.
        // Reuse its arrays instead of copying packed vertex/index buffers per sample.
        int index=wolf?1:0;
        if(!_animalSkullSamples[index].HasValue)
        {
            var arrays=AnimalSkull(wolf).SurfaceGetArrays(0);
            _animalSkullSamples[index]=(arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(),arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
        }
        var (points,indices)=_animalSkullSamples[index]!.Value;float result=0;
        for(int i=0;i<indices.Length;i+=3)
        {
            var a=points[indices[i]];var b=points[indices[i+1]];var c=points[indices[i+2]];
            if(y<MathF.Min(a.Y,MathF.Min(b.Y,c.Y))||y>MathF.Max(a.Y,MathF.Max(b.Y,c.Y))||
                z<MathF.Min(a.Z,MathF.Min(b.Z,c.Z))||z>MathF.Max(a.Z,MathF.Max(b.Z,c.Z)))continue;
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
