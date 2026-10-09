using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Shader? _hairShader;
    private NoiseTexture2D? _skinGrain;
    private Material SkinSurface(string color)=>new StandardMaterial3D {AlbedoColor=new Color(color),Roughness=.64f,MetallicSpecular=.26f};
    private Material FaceSurfaceMaterial(string color)
    {
        string key="face-surface:"+color;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        _skinGrain??=new NoiseTexture2D {Width=128,Height=128,GenerateMipmaps=true,
            Noise=new FastNoiseLite {Seed=937,Frequency=.24f},
            ColorRamp=new Gradient {Colors=new[]{new Color(.96f,.96f,.96f),Colors.White}}};
        var material=new StandardMaterial3D {AlbedoColor=new Color(color),VertexColorUseAsAlbedo=true,
            Roughness=.64f,MetallicSpecular=.26f,RoughnessTexture=_skinGrain,AlbedoTexture=_skinGrain,Uv1Scale=new(12,12,1)};
        _residentMaterials[key]=material;return material;
    }
    private Material HairSurface(string color)
    {
        _hairShader??=new Shader {Code=@"shader_type spatial;
uniform vec4 tone:source_color;varying vec3 hair_point;
void vertex(){hair_point=VERTEX;}
void fragment(){
float angle=atan(hair_point.x,hair_point.z);
float line=angle*57.0+hair_point.y*100.0+sin(angle*2.0)*hair_point.y*75.0;
float strand=sin(line)/(1.0+fwidth(line)*fwidth(line));
float lock=sin(angle*12.0+hair_point.y*18.0+sin(angle*3.0)*.6)*0.024;
float breakup=sin(angle*31.0+hair_point.y*71.0)*sin(hair_point.y*93.0+angle*7.0)*.012;
ALBEDO=tone.rgb*(0.96+strand*0.032+lock+breakup);ROUGHNESS=0.76;SPECULAR=0.22;
}"};
        var material=new ShaderMaterial {Shader=_hairShader};material.SetShaderParameter("tone",new Color(color));return material;
    }
    private Mesh EarSurface(int side)
    {
        string key="ear-surface:"+side;if(_meshes.TryGetValue(key,out var cached))return cached;
        const int rings=10,columns=32;var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int row,int col)
        {
            float r=row/(float)rings,a=col*MathF.Tau/columns;
            float helix=.004f+.005f*MathF.Exp(-MathF.Pow((r-.84f)/.13f,2));
            float antihelix=.003f*MathF.Exp(-MathF.Pow((r-.49f)/.14f,2))*(.6f+.4f*MathF.Sin(a+.4f));
            float concha=.005f*MathF.Exp(-MathF.Pow(r/.35f,2));
            return new(side*(helix+antihelix-concha),MathF.Cos(a)*r*.023f,MathF.Sin(a)*r*(.010f+.002f*MathF.Cos(a)));
        }
        void Vertex(int row,int col)
        {
            float r=row/(float)rings;surface.SetUV(new(col/(float)columns,r));
            surface.SetColor(new Color(1,1-.055f*(1-r),1-.085f*(1-r)));surface.AddVertex(Point(row,col));
        }
        for(int row=0;row<rings;row++)for(int col=0;col<columns;col++)
        {
            if(side<0){Vertex(row,col);Vertex(row+1,col);Vertex(row,col+1);Vertex(row,col+1);Vertex(row+1,col);Vertex(row+1,col+1);}
            else{Vertex(row,col);Vertex(row,col+1);Vertex(row+1,col);Vertex(row,col+1);Vertex(row+1,col+1);Vertex(row+1,col);}
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
