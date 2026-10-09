using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private readonly StandardMaterial3D _distantColorMaterial=new() {VertexColorUseAsAlbedo=true,Roughness=.9f,MetallicSpecular=.16f};
    private Mesh DistantColors(Mesh source)
    {
        string key="distant-colors:"+source.GetInstanceId();
        if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetMaterial(_distantColorMaterial);
        for(int s=0;s<source.GetSurfaceCount();s++)
        {
            var material=source.SurfaceGetMaterial(s);Color tint=new("#8c806e");
            if(material is StandardMaterial3D standard)tint=standard.AlbedoColor;
            else if(material is ShaderMaterial shader)
            {
                foreach(var uniform in shader.Shader.GetShaderUniformList())
                {
                    string name=uniform.AsGodotDictionary()["name"].AsString();
                    if(name is not ("dye" or "skin" or "skin_color" or "tone" or "coat" or "tint" or "pigment"))continue;
                    var value=shader.GetShaderParameter(name);if(value.VariantType==Variant.Type.Color){tint=value.AsColor();break;}
                }
            }
            var arrays=source.SurfaceGetArrays(s);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var colors=arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            int count=indices.Length>0?indices.Length:points.Length;
            for(int i=0;i<count;i++)
            {
                int v=indices.Length>0?indices[i]:i;tool.SetColor(colors.Length==points.Length?tint*colors[v]:tint);
                tool.SetNormal(normals[v]);tool.AddVertex(points[v]);
            }
        }
        tool.Index();var result=WithScreenLods(tool.Commit());_meshes[key]=result;return result;
    }
    // Preserve the full-resolution surface. The renderer chooses reduced index buffers
    // by projected size, so joint transforms and close-up anatomy remain unchanged.
    private Mesh WithScreenLods(Mesh source)
    {
        string key="screen-lods:"+source.GetInstanceId();
        if(_meshes.TryGetValue(key,out var cached))return cached;
        using var importer=new ImporterMesh();
        for(int s=0;s<source.GetSurfaceCount();s++)
            importer.AddSurface(Mesh.PrimitiveType.Triangles,source.SurfaceGetArrays(s),material:source.SurfaceGetMaterial(s));
        importer.GenerateLods(25,60,new Godot.Collections.Array());
        var result=importer.GetMesh();_meshes[key]=result;return result;
    }
}
