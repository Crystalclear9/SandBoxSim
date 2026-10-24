using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static float AnimalEyeCenterZ(bool wolf)=>wolf?-.125f:-.105f;
    private static Vector2 AnimalEyeOpening(bool wolf,float angle,float radius)
    {
        float z=AnimalEyeCenterZ(wolf)+MathF.Cos(angle)*(wolf?.027f:.024f)*radius;
        float vertical=MathF.Sin(angle);
        float y=.038f+vertical*MathF.Pow(MathF.Abs(vertical),.28f)*(wolf?.0125f:.011f)*radius;
        y+=MathF.Max(0,MathF.Sin(angle))*.0015f*radius+(z-AnimalEyeCenterZ(wolf))*.13f;
        return new(y,z);
    }
    private Mesh AnimalEyeSurface(bool wolf,float side)
    {
        string key=$"corneal-eye:{wolf}:{side}";if(_meshes.TryGetValue(key,out var cached))return cached;
        const int rings=12,sectors=48;
        var points=new Vector3[rings+1,sectors+1];var tex=new Vector2[rings+1,sectors+1];var closure=new Vector2[rings+1,sectors+1];
        for(int ring=0;ring<=rings;ring++)for(int sector=0;sector<=sectors;sector++)
        {
            float r=ring/(float)rings,t=sector*MathF.Tau/sectors;
            var opening=AnimalEyeOpening(wolf,t,r);float y=opening.X,z=opening.Y;
            // The wet surface blends into the same aperture as the lid, rather
            // than leaving a spherical eye protruding through the head.
            points[ring,sector]=new(side*(AnimalSkullSurfaceX(wolf,y,z)+.0006f+.0052f*(1-r*r)),y,z);
            float closedY=.038f+(z-AnimalEyeCenterZ(wolf))*.13f;
            closure[ring,sector]=new(side*(AnimalSkullSurfaceX(wolf,closedY,z)+.0005f)-points[ring,sector].X,closedY-y);
            tex[ring,sector]=new(.5f+(z-AnimalEyeCenterZ(wolf))/.044f,.5f+(y-.038f)/.026f);
        }
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle((int R,int S) a,(int R,int S) b,(int R,int S) c)
        {
            if((points[b.R,b.S]-points[a.R,a.S]).Cross(points[c.R,c.S]-points[a.R,a.S]).X*side>0)(b,c)=(c,b);
            foreach(var v in new[]{a,b,c}){tool.SetUV(tex[v.R,v.S]);tool.SetUV2(closure[v.R,v.S]);tool.AddVertex(points[v.R,v.S]);}
        }
        for(int sector=0;sector<sectors;sector++)Triangle((0,sector),(1,sector),(1,sector+1));
        for(int ring=1;ring<rings;ring++)for(int sector=0;sector<sectors;sector++)
        {Triangle((ring,sector),(ring+1,sector),(ring,sector+1));Triangle((ring,sector+1),(ring+1,sector),(ring+1,sector+1));}
        tool.GenerateNormals();tool.Index();var result=tool.Commit();_meshes[key]=result;return result;
    }

    private static void InstallAnimalBlink(AnimalRig rig)
    {
        void Visit(Node3D node)
        {
            if(node is MeshInstance3D part)
            for(int surface=0;surface<part.Mesh.GetSurfaceCount();surface++)
            {
                if(part.GetActiveMaterial(surface) is not ShaderMaterial material || !material.HasMeta("AnimalEyePart"))continue;
                bool lid=material.GetMeta("AnimalEyePart").AsString()=="lid";
                var instance=lid?rig.LidMaterial:rig.EyeMaterial;
                if(instance==null)
                {
                    instance=(ShaderMaterial)material.Duplicate(false);
                    if(lid)rig.LidMaterial=instance;else rig.EyeMaterial=instance;
                }
                part.SetSurfaceOverrideMaterial(surface,instance);
            }
            foreach(Node child in node.GetChildren())if(child is Node3D model)Visit(model);
        }
        Visit(rig.Head);
    }

    private void AddCanineToes(Node3D parent,Vector3 origin,Material fur)
    {
        // Four weight-bearing digits grow into the metacarpal pad. The skin
        // baker unions these roots; claws stay on the same lower-leg joint.
        for(int toe=0;toe<4;toe++)
        {
            float x=(toe-1.5f)*.021f;
            float front=toe is 1 or 2?-.012f:.003f;
            var digit=Loft("canine-digit",new[]{
                new Vector4(-.113f,.025f,.001f,.003f),new(-.107f,.027f,.011f,.012f),
                new(-.091f,.032f,.013f,.019f),new(-.064f,.034f,.016f,.019f),
                new(-.036f,.035f,.010f,.015f),new(-.023f,.035f,.001f,.003f)},24);
            Sculpt(parent,digit,origin+new Vector3(x,0,front),fur);
            var claw=Loft("canine-claw",new[]{
                new Vector4(-.122f,.015f,.0003f,.0005f),new(-.119f,.020f,.0020f,.0020f),
                new(-.111f,.029f,.0035f,.0038f),new(-.103f,.033f,.0034f,.0040f)},16);
            Sculpt(parent,claw,origin+new Vector3(x,0,front),LeatherSurface("#777267"));
        }
    }

    private Mesh AnimalNoseMesh(bool wolf)
    {
        string key="anatomical-nose:"+wolf;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        // Broad upper rhinarium, divided lower lobes and recessed lateral nares.
        var mesh=Loft(key+":base",new[]{
            new Vector4(-.416f,-.040f,.007f,.006f),new(-.412f,-.037f,wolf?.029f:.022f,.016f),
            new(-.399f,-.036f,wolf?.040f:.029f,.024f),new(-.383f,-.038f,wolf?.037f:.026f,.023f),
            new(-.375f,-.040f,wolf?.027f:.021f,.016f)},48);
        var arrays=mesh.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for(int i=0;i<points.Length;i++)
        {
            var v=points[i];float width=wolf?.029f:.022f;
            float nostril=MathF.Exp(-MathF.Pow((MathF.Abs(v.X)-width)/.007f,2)-MathF.Pow((v.Y+.034f)/.007f,2)-MathF.Pow((v.Z+.404f)/.012f,2));
            v.X-=MathF.Sign(v.X)*nostril*.005f;
            v.Z+=nostril*.003f;
            v.Y+=MathF.Exp(-MathF.Pow(v.X/.006f,2))*Math.Clamp((-v.Y-.038f)/.016f,0,1)*.004f;
            points[i]=AnimalMuzzlePoint(wolf,v);
        }
        arrays[(int)Mesh.ArrayType.Vertex]=points;
        var source=new ArrayMesh();source.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        using var tool=new SurfaceTool();tool.CreateFrom(source,0);tool.GenerateNormals();
        var result=tool.Commit();_meshes[key]=result;return result;
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
