using System;
using Godot;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Material? _distantHeadMaterial;
    private Mesh DistantHead(string skin,string hair)
    {
        string key="head-lod:"+skin+hair;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        _distantHeadMaterial??=new ShaderMaterial {Shader=new Shader {Code="shader_type spatial; void fragment(){ ALBEDO=pow(COLOR.rgb,vec3(2.2)); ROUGHNESS=0.88; SPECULAR=0.22; }"}};
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(_distantHeadMaterial);
        void Append(Mesh part,Vector3 position,Vector3 scale,string tint)
        {
            var transform=new Transform3D(Basis.Identity.Scaled(scale),position);
            var arrays=part.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();var normalBasis=transform.Basis.Inverse().Transposed();
            for(int i=0;i<(indices.Length>0?indices.Length:points.Length);i++)
            {
                int vertex=indices.Length>0?indices[i]:i;surface.SetColor(new Color(tint));surface.SetNormal((normalBasis*normals[vertex]).Normalized());surface.SetUV(Vector2.Zero);surface.AddVertex(transform*points[vertex]);
            }
        }
        Append(SculptedHead(false,20,12),Vector3.Zero,new(.177f,.220f,.195f),skin);
        Append(SculptedHead(true,20,12),new(0,.008f,.006f),new(.181f,.224f,.200f),hair);
        foreach(float side in new[]{-1f,1f})
        {
            Append(Shape("finger-low"),new(side*.087f,-.005f,0),new(.024f,.043f,.026f),skin);
            Append(Shape("finger-low"),new(side*.034f,.019f,-.087f),new(.014f,.006f,.005f),"#342f28");
        }
        surface.Index();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
