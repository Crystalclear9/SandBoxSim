using System;
using System.Collections.Generic;
using Godot;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // Transform each indexed vertex once on the CPU instead of issuing native calls for every triangle corner.
    private static void AppendMergedSurface(ArrayMesh mesh,Material material,List<MeshInstance3D> parts)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
        List<Color>? colors=material is StandardMaterial3D pigment&&pigment.VertexColorUseAsAlbedo?new():null;
        foreach(var part in parts)
        {
            var arrays=part.Mesh.SurfaceGetArrays(0);
            var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var sourceNormals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var sourceUv=arrays[(int)Mesh.ArrayType.TexUV].VariantType==Variant.Type.Nil?Array.Empty<Vector2>():arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var sourceColors=colors!=null&&arrays[(int)Mesh.ArrayType.Color].VariantType!=Variant.Type.Nil?arrays[(int)Mesh.ArrayType.Color].AsColorArray():Array.Empty<Color>();
            var sourceIndices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var transform=part.Transform;var normalBasis=transform.Basis.Inverse().Transposed();int offset=vertices.Count;
            for(int i=0;i<points.Length;i++)
            {
                vertices.Add(transform*points[i]);normals.Add((normalBasis*sourceNormals[i]).Normalized());uv.Add(sourceUv.Length>i?sourceUv[i]:Vector2.Zero);
                colors?.Add(sourceColors.Length>i?sourceColors[i]:Colors.White);
            }
            if(sourceIndices.Length>0)foreach(int i in sourceIndices)indices.Add(offset+i);
            else for(int i=0;i<points.Length;i++)indices.Add(offset+i);
        }
        var data=new Godot.Collections.Array();data.Resize((int)Mesh.ArrayType.Max);
        data[(int)Mesh.ArrayType.Vertex]=vertices.ToArray();data[(int)Mesh.ArrayType.Normal]=normals.ToArray();data[(int)Mesh.ArrayType.TexUV]=uv.ToArray();data[(int)Mesh.ArrayType.Index]=indices.ToArray();
        if(colors!=null)data[(int)Mesh.ArrayType.Color]=colors.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,data);mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,material);
    }
}
