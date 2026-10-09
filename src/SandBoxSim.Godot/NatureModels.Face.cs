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
uniform vec4 dye:source_color;uniform float blink=0.0;
void vertex(){ float x=UV.x*2.0-1.0;float arc=sqrt(max(0.0,1.0-x*x));VERTEX.y=arc*(0.0050-UV.y*(0.0013+blink*0.0092)); }
void fragment(){ALBEDO=dye.rgb;ROUGHNESS=0.76;SPECULAR=0.18;}"};
        rig.LidMaterial=new ShaderMaterial {Shader=_lidShader};rig.LidMaterial.SetShaderParameter("dye",new Color(skin));
        _eyeShader??=new Shader {Code=@"shader_type spatial;
uniform vec2 gaze=vec2(0.0);
uniform vec4 iris_tone:source_color=vec4(0.18,0.22,0.19,1.0);
void fragment(){
vec2 p=(UV-vec2(0.5)-gaze)*vec2(1.0,0.39);
float iris=1.0-smoothstep(0.19,0.21,length(p));
float pupil=1.0-smoothstep(0.07,0.085,length(p));
vec3 color=mix(vec3(0.72,0.70,0.65),iris_tone.rgb,iris);
color=mix(color,vec3(0.075,0.069,0.056),pupil);
float glint=1.0-smoothstep(0.009,0.020,length(p-vec2(-0.035,0.027)));
ALBEDO=mix(color,vec3(0.85,0.87,0.87),glint*iris*0.45);ROUGHNESS=0.19;SPECULAR=0.40;
}"};
        rig.EyeMaterial=new ShaderMaterial {Shader=_eyeShader};
        rig.HasAnatomicalEyes=true;
        rig.EyeMaterial.SetShaderParameter("iris_tone",new Color(new[]{"#51483a","#59675a","#686e70","#706448"}[_faceVariant%4]));
        for(int eye=0;eye<2;eye++)
        {
            var center=HumanEyeCenter(eye);
            var eyeball=rig.Eyes[eye]=new Node3D {Name="Eye"+eye,Position=center};rig.Head.AddChild(eyeball);
            eyeball.AddChild(new MeshInstance3D {Mesh=HumanMesh("eye",eye),MaterialOverride=rig.EyeMaterial});
            var lid=rig.Eyelids[eye]=new MeshInstance3D {Name="Eyelid"+eye,Mesh=EyeSurface(eye,true),Position=center,MaterialOverride=rig.LidMaterial};rig.Head.AddChild(lid);
        }
        rig.Mouth=new Node3D {Name="Mouth",Position=HumanMouthCenter};rig.Head.AddChild(rig.Mouth);
        rig.Mouth.AddChild(new MeshInstance3D {Mesh=HumanMesh("mouth"),MaterialOverride=FaceSurfaceMaterial(skin)});rig.MouthRest=rig.Mouth.Position;
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
            var dye=new Color(skin).Lerp(new Color("#986d64"),edge*.28f).Darkened(edge*(.025f+.16f*crease));surface.SetColor(dye);
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
        var anchor=HumanEyeCenter(side);float cx=anchor.X,cy=anchor.Y,cz=anchor.Z;
        void Vertex(int col,int row)
        {
            float u=col/20f,t=row/6f,x=u*2-1,arc=MathF.Sqrt(MathF.Max(0,1-x*x));
            float px=x*.014f,py=lid?arc*(.0050f-t*.0013f):arc*(1-t*2)*.0054f;
            surface.SetUV(new(u,t));
            float cornea=lid?0:.0011f*arc*MathF.Sin(t*MathF.PI);
            surface.AddVertex(new(px,py,FaceDepth(cx+px,cy+py)-cz-(lid?.001f:.00065f)-cornea));
        }
        for(int row=0;row<6;row++)for(int col=0;col<20;col++){Vertex(col,row);Vertex(col,row+1);Vertex(col+1,row);Vertex(col+1,row);Vertex(col,row+1);Vertex(col+1,row+1);}
        surface.GenerateNormals();surface.Index();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }

    private Mesh LowerEyelids()
    {
        string key="lower-eyelids:"+_faceVariant;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach(float side in new[]{-1f,1f})
        {
            void Vertex(int col,int row)
            {
                float u=col/24f,x=(u*2-1)*.014f,arc=MathF.Sqrt(MathF.Max(0,1-MathF.Pow(u*2-1,2)));
                float y=.010f-arc*(.0054f+row*.0011f),cx=side*.034f+x;
                surface.SetUV(new(u,row));surface.AddVertex(new(cx,y,FaceDepth(cx,y)-.00085f));
            }
            for(int col=0;col<24;col++){Vertex(col,0);Vertex(col,1);Vertex(col+1,0);Vertex(col+1,0);Vertex(col,1);Vertex(col+1,1);}
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }

    private Mesh Brows()
    {
        string key="brows:"+_faceVariant;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach(float side in new[]{-1f,1f})
        {
            void Vertex(int col,int row)
            {
                float u=col/24f,x=side*(.017f+u*.035f),taper=MathF.Pow(MathF.Max(0,MathF.Sin(u*MathF.PI)),.45f);
                float y=.026f+MathF.Sin(u*MathF.PI)*.0018f-row*.0015f*taper-u*.0005f;
                surface.SetUV(new(u,row));surface.AddVertex(new(x,y,FaceDepth(x,y)-.0010f));
            }
            for(int col=0;col<24;col++)
            {
                if(side>0){Vertex(col,0);Vertex(col,1);Vertex(col+1,0);Vertex(col+1,0);Vertex(col,1);Vertex(col+1,1);}
                else{Vertex(col,0);Vertex(col+1,0);Vertex(col,1);Vertex(col+1,0);Vertex(col+1,1);Vertex(col,1);}
            }
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
