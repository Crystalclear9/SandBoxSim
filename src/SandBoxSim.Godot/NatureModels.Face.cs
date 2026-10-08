using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _lidShader,_eyeShader;
    private Vector3[]? _facePoints;
    private int[]? _faceIndices;
    private float FaceDepth(float x,float y)
    {
        if(_facePoints==null)
        {
            var arrays=Shape("face").SurfaceGetArrays(0);_facePoints=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();_faceIndices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        }
        x/=.177f;y/=.220f;float depth=0;
        for(int i=0;i<_faceIndices!.Length;i+=3)
        {
            var a=_facePoints[_faceIndices[i]];var b=_facePoints[_faceIndices[i+1]];var c=_facePoints[_faceIndices[i+2]];
            float determinant=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(MathF.Abs(determinant)<.000001f)continue;
            float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/determinant;
            float v=((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/determinant;
            if(u<0||v<0||u+v>1)continue;
            depth=MathF.Min(depth,(a.Z*u+b.Z*v+c.Z*(1-u-v))*.195f);
        }
        return depth;
    }
    private void AddResidentFace(ResidentRig rig,string skin)
    {
        _lidShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 dye:source_color;uniform float blink=0.0;
void vertex(){ float x=UV.x*2.0-1.0;float arc=sqrt(max(0.0,1.0-x*x));VERTEX.y=arc*(0.0058-UV.y*(0.0018+blink*0.010)); }
void fragment(){ALBEDO=dye.rgb;ROUGHNESS=0.76;SPECULAR=0.18;}"};
        rig.LidMaterial=new ShaderMaterial {Shader=_lidShader};rig.LidMaterial.SetShaderParameter("dye",new Color(skin));
        _eyeShader??=new Shader {Code=@"shader_type spatial;
uniform vec2 gaze=vec2(0.0);
uniform vec4 iris_tone:source_color=vec4(0.18,0.22,0.19,1.0);
void fragment(){
vec2 p=(UV-vec2(0.5)-gaze)*vec2(1.0,0.43);
float iris=1.0-smoothstep(0.135,0.15,length(p));
float pupil=1.0-smoothstep(0.054,0.068,length(p));
vec3 color=mix(vec3(0.50,0.48,0.43),iris_tone.rgb,iris);
color=mix(color,vec3(0.075,0.069,0.056),pupil);
float glint=1.0-smoothstep(0.009,0.020,length(p-vec2(-0.035,0.027)));
ALBEDO=mix(color,vec3(0.76,0.73,0.65),glint*iris*0.6);ROUGHNESS=0.62;SPECULAR=0.14;
}"};
        rig.EyeMaterial=new ShaderMaterial {Shader=_eyeShader};
        rig.EyeMaterial.SetShaderParameter("iris_tone",new Color(new[]{"#51483a","#59675a","#686e70","#706448"}[_faceVariant%4]));
        for(int eye=0;eye<2;eye++)
        {
            float side=eye==0?-1:1;
            var center=new Vector3(side*.034f,.010f,FaceDepth(side*.034f,.010f));
            var eyeball=rig.Eyes[eye]=new Node3D {Name="Eye"+eye,Position=center};rig.Head.AddChild(eyeball);
            eyeball.AddChild(new MeshInstance3D {Mesh=EyeSurface(eye,false),MaterialOverride=rig.EyeMaterial});
            var lid=rig.Eyelids[eye]=new MeshInstance3D {Name="Eyelid"+eye,Mesh=EyeSurface(eye,true),Position=center,MaterialOverride=rig.LidMaterial};rig.Head.AddChild(lid);
        }
        rig.Mouth=new Node3D {Name="Mouth",Position=new(0,-.058f,FaceDepth(0,-.058f)-.0004f)};rig.Head.AddChild(rig.Mouth);
        rig.Mouth.AddChild(new MeshInstance3D {Mesh=LipSurface(skin)});rig.MouthRest=rig.Mouth.Position;
    }
    private Mesh LipSurface(string skin)
    {
        string key="lip-surface:"+skin+_faceVariant;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(new StandardMaterial3D {VertexColorUseAsAlbedo=true,Roughness=.83f,MetallicSpecular=.12f});
        float z=FaceDepth(0,-.058f);
        void Vertex(int column,int row)
        {
            float u=column/24f,v=row/8f,x=(u*2-1)*.020f,edge=MathF.Sqrt(MathF.Max(0,1-MathF.Pow(u*2-1,2)));
            float y=(.003f-.0065f*v)*edge+.0006f*MathF.Cos((u-.5f)*MathF.Tau*2)*edge;
            float crease=MathF.Exp(-MathF.Pow((v-.45f)*12,2));
            var dye=new Color(skin).Darkened(edge*(.035f+.075f*crease));dye.G*=1-edge*.075f;dye.B*=1-edge*.04f;surface.SetColor(dye);
            surface.SetNormal(Vector3.Forward);surface.SetUV(new(u,v));
            surface.AddVertex(new(x,y,FaceDepth(x,-.058f+y)-z-.0004f-.0011f*MathF.Sin(v*MathF.PI)*edge));
        }
        for(int row=0;row<8;row++)for(int col=0;col<24;col++){Vertex(col,row);Vertex(col,row+1);Vertex(col+1,row);Vertex(col+1,row);Vertex(col,row+1);Vertex(col+1,row+1);}
        surface.Index();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }

    private Mesh EyeSurface(int side,bool lid)
    {
        string key="eye-surface:"+side+lid+_faceVariant;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        float cx=side==0?-.034f:.034f,cy=.010f,cz=FaceDepth(cx,cy);
        void Vertex(int col,int row)
        {
            float u=col/20f,t=row/6f,x=u*2-1,arc=MathF.Sqrt(MathF.Max(0,1-x*x));
            float px=x*.014f,py=lid?arc*(.0058f-t*.0018f):arc*(1-t*2)*.006f;
            surface.SetNormal(Vector3.Forward);surface.SetUV(new(u,t));
            surface.AddVertex(new(px,py,FaceDepth(cx+px,cy+py)-cz-(lid?.001f:.00065f)));
        }
        for(int row=0;row<6;row++)for(int col=0;col<20;col++){Vertex(col,row);Vertex(col,row+1);Vertex(col+1,row);Vertex(col+1,row);Vertex(col,row+1);Vertex(col+1,row+1);}
        surface.Index();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
