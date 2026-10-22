using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public void ExportGarmentSources(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        for(int cut=0;cut<3;cut++)
        {
            SelectGarmentCut(cut);
            const int rings=40,sides=64;
            var positions=new List<float>();
            for(int r=0;r<=rings;r++)for(int c=0;c<sides;c++)
            {
                var p=ResidentRig.BodyPoint(GarmentPoint(r/(float)rings,c*MathF.Tau/sides));
                positions.AddRange(new[]{p.X,p.Y,p.Z});
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder,$"garment-{cut}-source.json"),
                JsonSerializer.Serialize(new{schemaVersion=2,cut,rings,sides,hem=ResidentRig.BodyY(GarmentBottom),shoulder=ResidentRig.BodyY(1.18f),positions}));
        }
        GD.Print("GARMENT_SOURCES_EXPORTED");
    }
    private Mesh ContinuousGarment()
    {
        string key="continuous-garment:"+_garmentCut;if(_meshes.TryGetValue(key,out var cached))return cached;
        const string root="res://assets/models/garments/";
        using var manifest=JsonDocument.Parse(FileAccess.GetFileAsString(root+"manifest.json"));
        string name=$"garment-{_garmentCut}.skin.json",raw=FileAccess.GetFileAsString(root+name);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=manifest.RootElement.GetProperty("sha256").GetProperty(name).GetString())throw new InvalidOperationException("Garment surface hash mismatch");
        using var doc=JsonDocument.Parse(raw);var data=doc.RootElement;
        float[] Floats(string field){var a=new float[data.GetProperty(field).GetArrayLength()];int i=0;foreach(var v in data.GetProperty(field).EnumerateArray())a[i++]=v.GetSingle();return a;}
        int[] Ints(string field){var a=new int[data.GetProperty(field).GetArrayLength()];int i=0;foreach(var v in data.GetProperty(field).EnumerateArray())a[i++]=v.GetInt32();return a;}
        var p=Floats("positions");var n=Floats("normals");var uv=Floats("uv");int count=p.Length/3;
        var b=Ints("bones");var w=Floats("weights");var index=Ints("indices");
        if(data.GetProperty("schemaVersion").GetInt32()!=1||count==0||p.Length!=count*3||n.Length!=p.Length||uv.Length!=count*2||b.Length!=count*4||w.Length!=b.Length||index.Length%3!=0)
            throw new InvalidOperationException("Invalid garment attributes");
        var points=new Vector3[count];var normals=new Vector3[count];var coords=new Vector2[count];
        for(int i=0;i<count;i++)
        {
            points[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);coords[i]=new(uv[i*2],uv[i*2+1]);
            float total=0;for(int j=0;j<4;j++){int k=i*4+j;if(b[k]<0||b[k]>2||!float.IsFinite(w[k])||w[k]<0)throw new InvalidOperationException("Invalid garment skin weight");total+=w[k];}
            if(!points[i].IsFinite()||!normals[i].IsFinite()||MathF.Abs(normals[i].Length()-1)>.01f||MathF.Abs(total-1)>.001f)throw new InvalidOperationException("Invalid garment geometry");
        }
        foreach(int i in index)if(i<0||i>=count)throw new InvalidOperationException("Invalid garment index");
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=coords;
        arrays[(int)Mesh.ArrayType.Bones]=b;arrays[(int)Mesh.ArrayType.Weights]=w;arrays[(int)Mesh.ArrayType.Index]=index;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);_meshes[key]=mesh;return mesh;
    }
    private static Vector4[] SleeveProfile()=>new[]{new Vector4(.040f,0,.045f,.040f),new(.085f,0,.056f,.051f),new(.17f,0,.053f,.049f),new(.24f,0,.047f,.044f),new(.263f,0,.043f,.042f)};
    private void ValidateContinuousGarments()
    {
        int previous=_garmentCut;
        for(int cut=0;cut<3;cut++)
        {
            SelectGarmentCut(cut);var mesh=ContinuousGarment();var a=mesh.SurfaceGetArrays(0);
            var points=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var indices=a[(int)Mesh.ArrayType.Index].AsInt32Array();
            var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            if(indices.Length>30000||ContinuousGarment()!=mesh)throw new InvalidOperationException("Garment budget/cache regression");
            var parent=new int[points.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            for(int i=0;i<indices.Length;i+=3){parent[Root(indices[i+1])]=Root(indices[i]);parent[Root(indices[i+2])]=Root(indices[i]);}
            int root=Root(0);var influence=new int[3];
            for(int i=0;i<points.Length;i++)
            {
                if(Root(i)!=root)throw new InvalidOperationException("Disconnected sewn garment");
                for(int j=0;j<4;j++)if(weights[i*4+j]>.9f)influence[bones[i*4+j]]++;
            }
            if(influence[0]<100||influence[1]<50||influence[2]<50||mesh.GetAabb().Size.X<.48f)
                throw new InvalidOperationException("Garment lost torso or mirrored sleeve binding");
        }
        SelectGarmentCut(previous);GD.Print("CONTINUOUS_GARMENT_PASS: three connected surfaces, mirrored sleeve weights, finite normals, hashes and 10k triangle budget");
    }
    private void RemoveSeparateSleeves(ResidentRig rig,string cloth)
    {
        foreach(var arm in rig.Arms)foreach(Node child in arm.GetChildren())if(child is MeshInstance3D part)
        {
            var original=part.Mesh;string key="sleeveless:"+original.GetInstanceId();
            if(!_meshes.TryGetValue(key,out var filtered))
            {
                var mesh=new ArrayMesh();
                for(int s=0;s<original.GetSurfaceCount();s++)if(original.SurfaceGetMaterial(s)!=ResidentMaterial(cloth))
                {mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,original.SurfaceGetArrays(s));mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,original.SurfaceGetMaterial(s));}
                _meshes[key]=filtered=mesh;
            }
            part.Mesh=filtered;part.SetMeta("DistantMesh",DistantColors(original));
        }
    }
}
