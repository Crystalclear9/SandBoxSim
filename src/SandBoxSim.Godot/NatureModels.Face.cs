using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _lidShader,_eyeShader;
    private Vector3[]? _facePoints;
    private int[]? _faceIndices;
    private readonly System.Collections.Generic.Dictionary<(int Variant,float X,float Y),float> _faceDepthSamples = new();
    private float FaceDepth(float x,float y)
    {
        var key=(_faceVariant,x,y);
        if(_faceDepthSamples.TryGetValue(key,out float sampled))return sampled;
        if(_facePoints==null)
        {
            var arrays=Shape("face").SurfaceGetArrays(0);_facePoints=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();_faceIndices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        }
        float localX=x,localY=y;
        x/=.177f;y/=.220f;float depth=0;
        for(int i=0;i<_faceIndices!.Length;i+=3)
        {
            var a=_facePoints[_faceIndices[i]];var b=_facePoints[_faceIndices[i+1]];var c=_facePoints[_faceIndices[i+2]];
            if(x<MathF.Min(a.X,MathF.Min(b.X,c.X))-.000001f||x>MathF.Max(a.X,MathF.Max(b.X,c.X))+.000001f||
                y<MathF.Min(a.Y,MathF.Min(b.Y,c.Y))-.000001f||y>MathF.Max(a.Y,MathF.Max(b.Y,c.Y))+.000001f)continue;
            float determinant=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(MathF.Abs(determinant)<.000001f)continue;
            float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/determinant;
            float v=((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/determinant;
            if(u<0||v<0||u+v>1)continue;
            depth=MathF.Min(depth,(a.Z*u+b.Z*v+c.Z*(1-u-v))*.195f);
        }
        foreach(int side in new[]{0,1})
        {
            var center=HumanEyeCenter(side);
            if(MathF.Abs(localX-center.X)<.019f&&MathF.Abs(localY-center.Y)<.017f)
                depth=MathF.Min(depth,HumanPartDepth("eye",side,localX,localY));
        }
        if(MathF.Abs(localX)<.039f&&MathF.Abs(localY-HumanMouthCenter.Y)<.014f)
            depth=MathF.Min(depth,HumanPartDepth("mouth",0,localX,localY));
        _faceDepthSamples[key]=depth;return depth;
    }
    private void AddResidentFace(ResidentRig rig,string skin)
    {
        _lidShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 dye:source_color;uniform float blink=0.0;uniform float resting_lid=.14;
void vertex(){ VERTEX.yz=mix(VERTEX.yz,UV2,resting_lid+(1.0-resting_lid)*blink); }
void fragment(){ALBEDO=dye.rgb;ROUGHNESS=0.76;SPECULAR=0.18;}"};
        rig.LidMaterial=new ShaderMaterial {Shader=_lidShader};rig.LidMaterial.SetShaderParameter("dye",new Color(skin));
        rig.LidMaterial.SetShaderParameter("resting_lid",ChildHead?.08f:.12f+(FaceArchetype%3)*.015f);
        _eyeShader??=new Shader {Code=@"shader_type spatial;
uniform vec2 gaze=vec2(0.0);
uniform vec4 iris_tone:source_color=vec4(0.18,0.22,0.19,1.0);
void fragment(){
vec2 p=(UV-vec2(0.5)-gaze)*vec2(1.0,0.39);
float iris=1.0-smoothstep(0.215,0.232,length(p));
float pupil=1.0-smoothstep(0.082,0.098,length(p));
float angle=atan(p.y,p.x),radius=length(p);
float fiber=sin(angle*63.0+sin(angle*9.0)*1.4+radius*37.0);
fiber/=1.0+pow(fwidth(angle*63.0),2.0);
float rim=smoothstep(0.185,0.225,radius);
vec3 iris_color=iris_tone.rgb*(0.94+fiber*.065)*(1.0-rim*.38);
float corner=smoothstep(.28,.48,abs(UV.x-.5));
vec3 sclera=mix(vec3(0.69,0.68,0.63),vec3(.61,.48,.44),corner*.38);
vec3 color=mix(sclera,iris_color,iris);
color=mix(color,vec3(0.075,0.069,0.056),pupil);
float glint=1.0-smoothstep(0.009,0.020,length(p-vec2(-0.035,0.027)));
ALBEDO=mix(color,vec3(0.85,0.87,0.87),glint*iris*0.35);ROUGHNESS=0.24;SPECULAR=0.36;
}"};
        rig.EyeMaterial=new ShaderMaterial {Shader=_eyeShader};
        rig.HasAnatomicalEyes=true;
        rig.EyeMaterial.SetShaderParameter("iris_tone",new Color(new[]{"#51483a","#59675a","#686e70","#706448"}[_faceVariant%4]));
        for(int eye=0;eye<2;eye++)
        {
            var center=HumanEyeCenter(eye);
            var eyeball=rig.Eyes[eye]=new Node3D {Name="Eye"+eye,Position=center};rig.Head.AddChild(eyeball);
            eyeball.AddChild(new MeshInstance3D {Mesh=HumanMesh("eye",eye),MaterialOverride=rig.EyeMaterial});
            var lid=rig.Eyelids[eye]=new MeshInstance3D {Name="Eyelid"+eye,Mesh=EyelidSurface(eye),Position=center,MaterialOverride=rig.LidMaterial};rig.Head.AddChild(lid);
        }
        rig.Mouth=new Node3D {Name="Mouth",Position=HumanMouthCenter};rig.Head.AddChild(rig.Mouth);
        rig.MouthMaterial=(ShaderMaterial)LipMaterial(skin).Duplicate();
        rig.Mouth.AddChild(new MeshInstance3D {Mesh=HumanMesh("mouth"),MaterialOverride=rig.MouthMaterial});rig.MouthRest=rig.Mouth.Position;
    }
    private Mesh EyelidSurface(int side)
    {
        string key="anatomical-lid:"+side+":"+_faceVariant;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        var anchor=HumanEyeCenter(side);var asset=HumanData();var upper=asset.UpperLids[side];var lower=asset.LowerLids[side];
        int columns=upper.Length-1;const int rows=8;
        for(int col=0;col<=columns;col++)
        {
            float u=col/(float)columns,edge=MathF.Max(0,MathF.Sin(u*MathF.PI));
            var top=HumanPoint(upper[col]);var bottom=HumanPoint(lower[col]);
            float outer=top.Y+.0013f*edge;
            float outerZ=FaceDepth(top.X,outer);
            for(int row=0;row<=rows;row++)
            {
                float t=row/(float)rows;
                float restY=Mathf.Lerp(outer,top.Y-.00010f*edge,t);
                float closedY=Mathf.Lerp(outer,bottom.Y-.00025f*edge,t);
                float restZ=Mathf.Lerp(outerZ,top.Z,t)-.00022f;
                float globeZ=HumanPartDepth("eye",side,top.X,closedY);
                if(!float.IsFinite(globeZ))globeZ=Mathf.Lerp(top.Z,bottom.Z,t);
                float closedZ=Mathf.Lerp(outerZ,globeZ,Math.Clamp(t*4,0,1))-.00038f;
                surface.SetUV(new(u,t));surface.SetUV2(new(closedY-anchor.Y,closedZ-anchor.Z));
                surface.AddVertex(new(top.X-anchor.X,restY-anchor.Y,restZ-anchor.Z));
            }
        }
        for(int col=0;col<columns;col++)for(int row=0;row<rows;row++)
        {
            int a=col*(rows+1)+row,b=a+1,c=a+rows+1,d=c+1;
            foreach(int index in new[]{a,b,c,c,b,d})surface.AddIndex(index);
        }
        surface.GenerateNormals();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }

    private Mesh Brows()
    {
        string key="brows:"+_faceVariant;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach(float side in new[]{-1f,1f})
        {
            var eye=HumanEyeCenter(side<0?0:1);
            // Separate tapered hairs grow outward from the brow ridge. Both
            // ends are sampled on the actual face; no straight floating ribbon.
            for(int hair=0;hair<72;hair++)
            {
                float u=(hair+.5f)/72,fullness=MathF.Pow(MathF.Sin(u*MathF.PI),.55f);
                float x=eye.X+side*(-.016f+u*.035f),y=eye.Y+.015f+MathF.Sin(u*MathF.PI*.95f)*.004f-u*.003f;
                float length=(.0012f+.0021f*fullness)*(1+(hair%3-1)*.12f);
                Vector3 Point(float t,float across)
                {
                    float px=x+side*t*(.0006f+.0015f*u),py=y+length*(t-.55f);
                    px+=across*.00040f*fullness*MathF.Sin(MathF.PI*t);
                    return new(px,py,FaceDepth(px,py)-.00035f-.00025f*MathF.Sin(MathF.PI*t));
                }
                for(int row=0;row<3;row++)
                {
                    float t=row/3f,v=(row+1)/3f;
                    surface.SetColor(new Color(.76f+(hair%5)*.045f,.76f+(hair%5)*.045f,.76f+(hair%5)*.045f));
                    EquipmentTriangle(surface,Point(t,-1),Point(v,-1),Point(t,1),Vector3.Forward);
                    EquipmentTriangle(surface,Point(t,1),Point(v,-1),Point(v,1),Vector3.Forward);
                }
            }
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
