using System;
using Godot;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Material? _distantHeadMaterial;
    private Mesh DistantHead(string skin,string hair)
    {
        string key="head-lod:"+skin+hair+":"+_faceVariant;if(_meshes.TryGetValue(key,out var mesh))return mesh;
        _distantHeadMaterial??=new ShaderMaterial {Shader=new Shader {Code="shader_type spatial;"+SkinColorFunctions+"void fragment(){ ALBEDO=OUTPUT_IS_SRGB?COLOR.rgb:skin_linear(COLOR.rgb); ROUGHNESS=0.88; SPECULAR=0.22; }"}};
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(_distantHeadMaterial);
        void Append(Mesh part,Vector3 position,Vector3 scale,string tint,bool skinPart=false)
        {
            var transform=new Transform3D(Basis.Identity.Scaled(scale),position);
            var arrays=part.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();var normalBasis=transform.Basis.Inverse().Transposed();
            var colors=arrays[(int)Mesh.ArrayType.Color].VariantType==Variant.Type.Nil?Array.Empty<Color>():arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            for(int i=0;i<(indices.Length>0?indices.Length:points.Length);i++)
            {
                int vertex=indices.Length>0?indices[i]:i;var sourceColour=colors.Length>vertex?colors[vertex]:Colors.White;
                // LOD colours are authored relative tints, not atlas samples.
                var colour=skinPart?(new Color(tint).SrgbToLinear()*sourceColour.SrgbToLinear()*.88f).LinearToSrgb():new Color(tint)*sourceColour;
                surface.SetColor(colour);surface.SetNormal((normalBasis*normals[vertex]).Normalized());surface.SetUV(Vector2.Zero);surface.AddVertex(transform*points[vertex]);
            }
        }
        Append(HumanMesh("lod"),Vector3.Zero,new(.177f,.220f,.195f),skin,true);
        Append(GroomedHair(20,12),new(0,.008f,0),new(.181f,.224f,.200f),hair);
        surface.Index();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
