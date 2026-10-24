using System;
using System.Collections.Generic;
using Godot;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // Transform each indexed vertex once on the CPU instead of issuing native calls for every triangle corner.
    private static void AppendMergedSurface(ArrayMesh mesh,Material material,List<MeshInstance3D> parts)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var uv2=new List<Vector2>();var indices=new List<int>();
        // Shader materials also consume COLOR (skin regions, hair roots and
        // individual fibers). Dropping it here erased their authored variation.
        var colors=new List<Color>();
        bool animalEyeMorph=material.HasMeta("UsesAnimalEyeMorph");
        foreach(var part in parts)
        {
            var arrays=part.Mesh.SurfaceGetArrays(0);
            var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var sourceNormals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var sourceUv=arrays[(int)Mesh.ArrayType.TexUV].VariantType==Variant.Type.Nil?Array.Empty<Vector2>():arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var sourceUv2=arrays[(int)Mesh.ArrayType.TexUV2].VariantType==Variant.Type.Nil?Array.Empty<Vector2>():arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            var sourceColors=arrays[(int)Mesh.ArrayType.Color].VariantType!=Variant.Type.Nil?arrays[(int)Mesh.ArrayType.Color].AsColorArray():Array.Empty<Color>();
            var sourceIndices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var transform=part.Transform;var normalBasis=transform.Basis.Inverse().Transposed();int offset=vertices.Count;
            for(int i=0;i<points.Length;i++)
            {
                vertices.Add(transform*points[i]);normals.Add((normalBasis*sourceNormals[i]).Normalized());uv.Add(sourceUv.Length>i?sourceUv[i]:Vector2.Zero);
                colors.Add(sourceColors.Length>i?sourceColors[i]:Colors.White);
                var morph=sourceUv2.Length>i?transform.Basis*(animalEyeMorph?new Vector3(sourceUv2[i].X,sourceUv2[i].Y,0):new Vector3(0,sourceUv2[i].X,sourceUv2[i].Y)):Vector3.Zero;
                uv2.Add(animalEyeMorph?new(morph.X,morph.Y):new(morph.Y,morph.Z));
            }
            if(sourceIndices.Length>0)foreach(int i in sourceIndices)indices.Add(offset+i);
            else for(int i=0;i<points.Length;i++)indices.Add(offset+i);
        }
        var data=new Godot.Collections.Array();data.Resize((int)Mesh.ArrayType.Max);
        data[(int)Mesh.ArrayType.Vertex]=vertices.ToArray();data[(int)Mesh.ArrayType.Normal]=normals.ToArray();data[(int)Mesh.ArrayType.TexUV]=uv.ToArray();data[(int)Mesh.ArrayType.Index]=indices.ToArray();
        data[(int)Mesh.ArrayType.Color]=colors.ToArray();
        data[(int)Mesh.ArrayType.TexUV2]=uv2.ToArray();
        if(material.HasMeta("UsesSkinAtlas"))
        {
            // Generate after transforms and UV seam splitting, in the same
            // coordinate space in which the merged material is evaluated.
            var source=new ArrayMesh();source.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,data);
            using var builder=new SurfaceTool();builder.CreateFrom(source,0);builder.GenerateTangents();builder.Commit(mesh);
        }
        else mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,data);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,material);
    }
}
