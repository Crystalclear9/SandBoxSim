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
void vertex(){ local=VERTEX; }
void fragment(){
float guard=sin(local.x*270.0+local.z*91.0)*sin(local.y*310.0);
float footprint=max(length(dFdx(local)),length(dFdy(local)))*300.0;
guard/=1.0+footprint*footprint;
float belly=1.0-smoothstep(0.78,1.02,local.y);
float saddle=smoothstep(0.98,1.20,local.y)*0.12;
ALBEDO=mix(coat.rgb,pale.rgb,belly*0.36)*(0.98+guard*0.035-saddle);
ROUGHNESS=0.96; SPECULAR=0.18;
}" };
        var material = new ShaderMaterial { Shader = _furShader };
        material.SetShaderParameter("coat", new Color(wolf ? "#77776c" : "#987453"));
        material.SetShaderParameter("pale", new Color(wolf ? "#c8c1ac" : "#d6c4a4"));
        _residentMaterials[key] = material; return material;
    }
    private AnimalRig DetailedAnimal(bool wolf)
    {
        var rig = new AnimalRig(); rig.Body = new Node3D { Name = "Body" }; rig.AddChild(rig.Body);
        var fur = Fur(wolf);
        float back = wolf ? .83f : 1.03f;
        var body = Loft(wolf ? "wolf-body" : "deer-body", new[] {
            new Vector4(-.71f,back+(wolf?.32f:.40f),.015f,.03f), new(-.60f,back+.10f,.13f,.21f),
            new(-.48f,back,.19f,.25f), new(-.3f,back,.235f,.26f),
            new(-.08f,back-.025f,.245f,.28f), new(.18f,back+.015f,.23f,.24f), new(.40f,back-.02f,.235f,.26f),
            new(.55f,back,.19f,.22f), new(.64f,back,.012f,.025f) });
        Sculpt(rig.Body, body, Vector3.Zero, fur);
        // Broad shoulders taper continuously into the chest and rising neck.
        var neck = Loft(wolf ? "wolf-neck" : "deer-neck", new[] {
            new Vector4(-.32f,back,.02f,.02f), new(-.48f,back+.12f,.145f,.19f),
            new(-.62f,back+(wolf?.25f:.33f),wolf?.12f:.12f,.16f), new(-.74f,back+(wolf?.36f:.48f),.10f,.14f), new(-.84f,back+(wolf?.35f:.47f),.02f,.025f) });
        Sculpt(rig.Body, neck, Vector3.Zero, fur);
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0,back+(wolf?.32f:.44f),-.78f) }; rig.Body.AddChild(rig.Head);
        Sculpt(rig.Head, Loft(wolf ? "wolf-head" : "deer-head", new[] {
            new Vector4(.15f,0,.008f,.015f), new(.06f,.025f,wolf?.13f:.115f,.115f), new(-.08f,.020f,wolf?.12f:.105f,.105f),
            new(-.20f,-.015f,.075f,.067f), new(-.36f,-.04f,.055f,.055f), new(-.40f,-.04f,.006f,.012f) }), Vector3.Zero, fur);
        Detail(rig.Head,"sphere",new(0,-.045f,-.382f),new(.09f,.065f,.055f),"#34352e");
        Sculpt(rig.Head,Loft("animal-jaw-"+wolf,new[]{new Vector4(-.04f,-.075f,.078f,.034f),new(-.16f,-.080f,.068f,.033f),new(-.29f,-.072f,.048f,.022f),new(-.36f,-.059f,.021f,.010f)}),Vector3.Zero,ResidentMaterial(wolf?"#8e9183":"#b4a080"));
        foreach (float side in new[] {-1f,1f})
        {
            Detail(rig.Head,"sphere",new(side*(wolf?.109f:.098f),.035f,-.105f),new(.055f,.04f,.027f),"#544b3c");
            Detail(rig.Head,"sphere",new(side*(wolf?.120f:.108f),.038f,-.116f),new(.019f,.023f,.018f),"#171f1b");
            Detail(rig.Head,"sphere",new(side*(wolf?.126f:.114f),.041f,-.125f),Vector3.One*.006f,"#ddd2b9");
            // Flattened leaf-shaped ears, inset inner skin; no cone ears.
            if(wolf) Sculpt(rig.Head,Loft("wolf-ear",new[]{new Vector4(-.25f,0,.003f,.003f),new(-.14f,0,.026f,.015f),new(-.03f,0,.065f,.023f),new(.03f,0,.037f,.019f)}),new(side*.12f,.12f,.04f),fur,new(MathF.PI/2,0,-side*.22f));
            else Detail(rig.Head,"sphere",new(side*.13f,.20f,.015f),new(.12f,.30f,.05f),"#947454",new(0,0,-side*.43f));
            Detail(rig.Head,"sphere",new(side*.13f,.20f,-.009f),new(wolf ? .04f : .071f,wolf ? .12f : .22f,.015f),"#b3a38b",new(0,0,-side*.43f));
            if (!wolf)
            {
                var root=new Vector3(side*.083f,.13f,.06f);
                var top=new Vector3(side*.28f,.66f,.10f);
                FineBeam(rig.Head,root,new(side*.16f,.4f,.08f),.032f,"#6f6150");
                FineBeam(rig.Head,new(side*.16f,.4f,.08f),top,.022f,"#8e7c63");
                for(int i=0;i<3;i++)
                    FineBeam(rig.Head,new(side*(.12f+i*.047f),.3f+i*.12f,.085f),new(side*(.13f+i*.07f),.48f+i*.12f,-.04f),.018f-i*.003f,"#8e7c63");
            }
        }
        for(int i=0;i<4;i++)
        {
            float side=i%2==0 ? -1 : 1; bool rear=i>=2;
            float y=back-.13f, z=rear ? .39f : -.35f;
            var leg=rig.Legs[i]=new Node3D { Name="Leg"+i,Position=new(side*.20f,y,z) }; rig.Body.AddChild(leg);
            Sculpt(leg,Loft("animal-upper-"+wolf+rear,new[]{new Vector4(-.015f,0,rear ? .091f : .070f,.064f),new(.10f,0,rear ? .09f : .063f,.064f),new(.24f,0,.044f,.041f),new(.35f,0,.034f,.033f)}),Vector3.Zero,fur,new(MathF.PI/2,0,0));
            var knee=rig.Knees[i]=new Node3D { Name="Knee", Position=new(0,-.32f,rear ? -.04f : 0) }; leg.AddChild(knee);
            float lower=y-.32f;
            Sculpt(knee,Loft("animal-shin-"+wolf,new[]{new Vector4(-.02f,0,.036f,.035f),new(lower*.2f,0,.036f,.034f),new(lower*.6f,.012f,.026f,.025f),new(lower-.03f,.022f,.028f,.03f)}),Vector3.Zero,fur,new(MathF.PI/2,0,0));
            Detail(knee,"sphere",new(0,-lower+.035f,-.035f),new(wolf ? .11f : .078f,.10f,wolf ? .17f : .12f),wolf ? "#585b53" : "#493e32");
            if(wolf)for(int toe=0;toe<3;toe++)
            {
                Detail(knee,"sphere",new((toe-1)*.030f,-lower+.019f,-.089f),new(.032f,.050f,.060f),"#585b53");
                Detail(knee,"seed",new((toe-1)*.030f,-lower+.018f,-.116f),new(.009f,.012f,.019f),"#34352e");
            }
            if(!wolf) FineBeam(knee,new(0,-lower+.028f,-.094f),new(0,-lower+.075f,-.091f),.008f,"#b19e80");
        }
        rig.Tail=new Node3D { Name="Tail",Position=new(0,back,.55f) }; rig.Body.AddChild(rig.Tail);
        Detail(rig.Tail,"capsule",new(0,wolf ? -.17f : .01f,.13f),new(wolf ? .16f : .095f,wolf ? .26f : .11f,.12f),wolf ? "#62665c" : "#c5b697",new(wolf ? -.5f : -.9f,0,0));
        MergeResidentParts(rig,wolf ? "crafted-wolf" : "crafted-deer"); return rig;
    }
}
