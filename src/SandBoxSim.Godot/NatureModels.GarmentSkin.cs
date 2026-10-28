using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Same metre-space support field as tools/garment_pattern.py. The baked
    // surface is checked against it below. Apply it to the attached clothing
    // too, so a moving side-chest cannot leave a rigid shoulder strap behind.
    private static float GarmentTorsoShoulderWeight(Vector3 point)
    {
        float along=ResidentRig.BodyY(1.18f)-point.Y;
        return .38f*Mathf.SmoothStep(.075f,.205f,MathF.Abs(point.X))*Mathf.SmoothStep(-.020f,.035f,along)
            *(1-Mathf.SmoothStep(.045f,.175f,along));
    }
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
        var p=Floats("positions");var n=Floats("normals");var uv=Floats("uv");var detail=Floats("detailUv");int count=p.Length/3;
        var b=Ints("bones");var w=Floats("weights");var index=Ints("indices");
        if(data.GetProperty("schemaVersion").GetInt32()!=1||count==0||p.Length!=count*3||n.Length!=p.Length||uv.Length!=count*2||detail.Length!=uv.Length||b.Length!=count*4||w.Length!=b.Length||index.Length%3!=0)
            throw new InvalidOperationException("Invalid garment attributes");
        var points=new Vector3[count];var normals=new Vector3[count];var coords=new Vector2[count];var details=new Vector2[count];
        for(int i=0;i<count;i++)
        {
            points[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);coords[i]=new(uv[i*2],uv[i*2+1]);details[i]=new(detail[i*2],detail[i*2+1]);
            float total=0;for(int j=0;j<4;j++){int k=i*4+j;if(b[k]<0||b[k]>2||!float.IsFinite(w[k])||w[k]<0)throw new InvalidOperationException("Invalid garment skin weight");total+=w[k];}
            if(!points[i].IsFinite()||!normals[i].IsFinite()||!details[i].IsFinite()||details[i].X< -1||details[i].X>.264f||details[i].Y<0||details[i].Y>.2f||MathF.Abs(normals[i].Length()-1)>.01f||MathF.Abs(total-1)>.001f)throw new InvalidOperationException("Invalid garment geometry");
        }
        foreach(int i in index)if(i<0||i>=count)throw new InvalidOperationException("Invalid garment index");
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=coords;arrays[(int)Mesh.ArrayType.TexUV2]=details;
        arrays[(int)Mesh.ArrayType.Bones]=b;arrays[(int)Mesh.ArrayType.Weights]=w;arrays[(int)Mesh.ArrayType.Index]=index;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);_meshes[key]=mesh;return mesh;
    }
    private static Vector4[] SleeveProfile()=>new[]{new Vector4(.040f,0,.043f,.040f),new(.085f,0,.049f,.046f),new(.17f,0,.047f,.044f),new(.24f,0,.045f,.043f),new(.263f,0,.043f,.042f)};
    private void ValidateContinuousGarments()
    {
        int previous=_garmentCut;
        for(int cut=0;cut<3;cut++)
        {
            SelectGarmentCut(cut);var mesh=ContinuousGarment();var a=mesh.SurfaceGetArrays(0);
            var points=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var indices=a[(int)Mesh.ArrayType.Index].AsInt32Array();
            var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var seam=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            if(indices.Length>30000||ContinuousGarment()!=mesh)throw new InvalidOperationException("Garment budget/cache regression");
            // Rays through the actual throat opening must not encounter a
            // shoulder panel or cap near the collar, for any of the three cuts.
            foreach(var ray in new[]{new Vector2(0,-.088f),new(.020f,-.084f),new(-.020f,-.084f)})
            for(int i=0;i<indices.Length;i+=3)
            {
                var pa=points[indices[i]];var pb=points[indices[i+1]];var pc=points[indices[i+2]];
                if(MathF.Max(pa.Y,MathF.Max(pb.Y,pc.Y))<ResidentRig.BodyY(1.255f))continue;
                var ab=new Vector2(pb.X-pa.X,pb.Z-pa.Z);var ac=new Vector2(pc.X-pa.X,pc.Z-pa.Z);float det=ab.Cross(ac);if(MathF.Abs(det)<1e-10f)continue;
                var hit=ray-new Vector2(pa.X,pa.Z);float v=hit.Cross(ac)/det,w=ab.Cross(hit)/det;
                if(v<0||w<0||v+w>1)continue;float y=pa.Y*(1-v-w)+pb.Y*v+pc.Y*w;
                if(y>=ResidentRig.BodyY(1.255f))throw new InvalidOperationException("Cloth crosses the anatomical throat opening");
            }
            float throatFront=float.PositiveInfinity;
            for(int i=0;i<indices.Length;i+=3)
            {
                var pa=points[indices[i]];var pb=points[indices[i+1]];var pc=points[indices[i+2]];
                var ab=new Vector2(pb.X-pa.X,pb.Y-pa.Y);var ac=new Vector2(pc.X-pa.X,pc.Y-pa.Y);float det=ab.Cross(ac);if(MathF.Abs(det)<1e-10f)continue;
                var ray=new Vector2(-pa.X,1.31f-pa.Y);float v=ray.Cross(ac)/det,w=ab.Cross(ray)/det;
                if(v>=0&&w>=0&&v+w<=1)throatFront=MathF.Min(throatFront,pa.Z*(1-v-w)+pb.Z*v+pc.Z*w);
            }
            if(!float.IsFinite(throatFront)||throatFront>-.092f)throw new InvalidOperationException("Front yoke is too shallow for the source throat at 1.31 m");
            var parent=new int[points.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            for(int i=0;i<indices.Length;i+=3){parent[Root(indices[i+1])]=Root(indices[i]);parent[Root(indices[i+2])]=Root(indices[i]);}
            int root=Root(0);var influence=new int[3];
            for(int i=0;i<points.Length;i++)
            {
                if(Root(i)!=root)throw new InvalidOperationException("Disconnected sewn garment");
                float ring=Math.Clamp((seam[i].X/.263f-.12f)/.48f,0,1);ring=ring*ring*(3-2*ring);
                float local=GarmentTorsoShoulderWeight(points[i]);
                float expected=seam[i].X>0?1-(1-ring)*(1-local):local;
                float actual=weights[i*4+1]+weights[i*4+2];
                if(MathF.Abs(expected-actual)>.00002f)
                    throw new InvalidOperationException("Baked garment and attached clothing use different shoulder support fields");
                for(int j=0;j<4;j++)if(weights[i*4+j]>.9f)influence[bones[i*4+j]]++;
            }
            if(influence[0]<100||influence[1]<50||influence[2]<50||mesh.GetAabb().Size.X<.48f)
                throw new InvalidOperationException("Garment lost torso or mirrored sleeve binding");
        }
        var resident=Resident(17,false,SandBoxSim.Core.Agents.JobType.Farmer);
        try
        {
            var fabric=(ShaderMaterial)resident.BodySkin.Mesh.SurfaceGetMaterial(0);
            if(!fabric.GetShaderParameter("sleeve_details").AsBool())
                throw new InvalidOperationException("Skinned garment lost sleeve seam material");
            var finalArrays=resident.BodySkin.Mesh.SurfaceGetArrays(0);
            var finalDetails=finalArrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            var sourceDetails=ContinuousGarment().SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            var coordinates=new HashSet<Vector2>(sourceDetails);
            int detailed=0;
            foreach(var value in finalDetails)
            {
                if(!value.IsFinite()||!coordinates.Contains(value))throw new InvalidOperationException("Merged garment alters metric seam coordinates");
                if(value.X>0)detailed++;
            }
            if(detailed<500)throw new InvalidOperationException("Skinned garment lost its sewn sleeve surface");
            foreach(string sampler in new[]{"weave_map","fibre_map"})
                if(fabric.GetShaderParameter(sampler).AsGodotObject() is not Texture2D texture||texture.GetWidth()<512)
                    throw new InvalidOperationException("Rendered skinned garment lost its scanned material: "+sampler);
        }
        finally{resident.Free();SelectGarmentCut(previous);}
        ValidateGarmentMotion();SelectGarmentCut(previous);
        GD.Print("CONTINUOUS_GARMENT_PASS: connected surfaces, mirrored weights, hashes, budget and scanned textures on final skinned body");
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
