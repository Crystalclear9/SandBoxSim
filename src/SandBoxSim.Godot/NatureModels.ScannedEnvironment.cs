using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private const string EnvironmentRoot="res://assets/models/environment/";
    private static readonly Dictionary<string,string> EnvironmentHashes=new()
    {
        ["tree_small_02"]="3db6ca4ad0da789ebdc0558ba3133f5746ec8ad04218e4306de803d10da051fd",
        ["fir_sapling"]="2a5e64f8a29838b3e1155c73b293bc6f322a518deeb76d89d39df327236ab5da",
        ["fern_02"]="36c4a69a1d8031ec026428a483fdf8c2931bf304578db8dc208d53b8ee3a670b",
        ["grass_medium_01"]="dce2289cad815e16524b33597bd7ad627a45281fa26609e969a3d28595138e65",
        ["shrub_sorrel_01"]="793aaddbe1890cf4b25ba4204beb797539931fd107a3e649e7d35365a34d178e"
    };
    private Shader? _sourceVegetationShader;
    private readonly Dictionary<string,ShaderMaterial> _sourceVegetationMaterials=new();
    private ShaderMaterial SourceVegetationMaterial(string asset,string name,float height)
    {
        string key=asset+":"+name;if(_sourceVegetationMaterials.TryGetValue(key,out var cached))return cached;
        using var manifest=JsonDocument.Parse(FileAccess.GetFileAsString(EnvironmentRoot+"manifest.json"));
        var maps=manifest.RootElement.GetProperty("assets").GetProperty(asset).GetProperty("materials").GetProperty(name);
        _sourceVegetationShader??=new Shader {Code=@"shader_type spatial;render_mode cull_disabled;
uniform sampler2D diffuse_map:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D normal_map:hint_normal,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D roughness_map:filter_linear_mipmap,repeat_enable;
uniform sampler2D alpha_map:filter_linear_mipmap,repeat_enable;
uniform vec2 uv_scale=vec2(1.0);uniform vec2 uv_offset=vec2(0.0);
uniform bool cutout=false;uniform float plant_height=1.0;uniform bool grass=false;
uniform bool soil_bound=false;uniform sampler2D soil:filter_nearest,repeat_disable;uniform vec2 map_size=vec2(1.0);
void vertex(){
vec3 root=(MODEL_MATRIX*vec4(0,0,0,1)).xyz;float t=clamp(VERTEX.y/plant_height,0.0,1.0);
float fade=grass?1.0-smoothstep(24.0,32.0,distance(root,CAMERA_POSITION_WORLD)):1.0;
float wear=grass&&soil_bound?texture(soil,clamp(root.xz*.5/map_size,vec2(0),vec2(1))).g:0.0;
VERTEX.y*=fade*(1.0-wear*.85);
VERTEX.x+=sin(TIME*.85+root.x*.31+root.z*.23+VERTEX.y*2.0)*.016*t*t*fade;
}
void fragment(){
if(!FRONT_FACING){NORMAL=-NORMAL;}
vec2 tex_uv=UV*uv_scale+uv_offset;
ALBEDO=texture(diffuse_map,tex_uv).rgb;
NORMAL_MAP=texture(normal_map,tex_uv).rgb;NORMAL_MAP_DEPTH=.65;
ROUGHNESS=clamp(texture(roughness_map,tex_uv).r,.45,1.0);SPECULAR=.24;
ALPHA=cutout?texture(alpha_map,tex_uv).r:1.0;ALPHA_SCISSOR_THRESHOLD=mix(.45,.18,smoothstep(2.0,8.0,max(length(dFdx(UV)),length(dFdy(UV)))*1024.0));
}"};
        var material=new ShaderMaterial {Shader=_sourceVegetationShader};
        foreach(string kind in new[]{"diffuse","normal","roughness","alpha"})
        {
            if(!maps.TryGetProperty(kind,out var path))continue;
            string resource=EnvironmentRoot+asset+"/"+path.GetString();
            if(!ResourceLoader.Exists(resource))throw new InvalidOperationException("Missing environment texture: "+resource);
            material.SetShaderParameter(kind=="diffuse"?"diffuse_map":kind=="normal"?"normal_map":kind=="roughness"?"roughness_map":"alpha_map",GD.Load<Texture2D>(resource));
        }
        material.SetShaderParameter("cutout",maps.TryGetProperty("alpha",out _));
        if(name=="tree_small_02_branches"){material.SetShaderParameter("uv_scale",new Vector2(3,.6f));material.SetShaderParameter("uv_offset",new Vector2(0,.4f));}
        material.SetShaderParameter("plant_height",height);material.SetShaderParameter("grass",asset=="grass_medium_01");
        _sourceVegetationMaterials[key]=material;return material;
    }
    private Mesh SourceEnvironment(string asset)
    {
        string key="source-environment:"+asset;if(_meshes.TryGetValue(key,out var cached))return cached;
        string path=EnvironmentRoot+asset+"/"+asset+".mesh.json";
        if(!FileAccess.FileExists(path)||!FileAccess.FileExists(EnvironmentRoot+"LICENSE.txt"))throw new InvalidOperationException("Missing licensed environment data");
        string raw=FileAccess.GetFileAsString(path);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=EnvironmentHashes[asset])throw new InvalidOperationException("Environment mesh source hash mismatch: "+asset);
        using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("source").GetProperty("license").GetString()!="CC0-1.0")throw new InvalidOperationException("Invalid environment source record");
        float[] Floats(JsonElement e){var a=new float[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())a[i++]=v.GetSingle();return a;}
        var result=new ArrayMesh();
        foreach(var surface in root.GetProperty("surfaces").EnumerateArray())
        {
            var p=Floats(surface.GetProperty("positions"));var n=Floats(surface.GetProperty("normals"));var t=Floats(surface.GetProperty("uv"));
            if(p.Length%3!=0||p.Length!=n.Length||t.Length!=p.Length/3*2)throw new InvalidOperationException("Environment attributes do not match");
            var index=surface.GetProperty("indices");if(index.GetArrayLength()%3!=0)throw new InvalidOperationException("Environment triangle count is invalid");
            using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
            tool.SetMaterial(SourceVegetationMaterial(asset,surface.GetProperty("material").GetString()!,root.GetProperty("height").GetSingle()));
            foreach(var item in index.EnumerateArray())
            {
                int v=item.GetInt32();if(v<0||v>=p.Length/3)throw new InvalidOperationException("Environment index out of range");
                var point=new Vector3(p[v*3],p[v*3+1],p[v*3+2]);var normal=new Vector3(n[v*3],n[v*3+1],n[v*3+2]);
                if(!point.IsFinite()||!normal.IsFinite()||normal.LengthSquared()<.8f)throw new InvalidOperationException("Invalid environment geometry");
                tool.SetNormal(normal.Normalized());tool.SetUV(new(t[v*2],t[v*2+1]));tool.AddVertex(point);
            }
            tool.Index();tool.GenerateTangents();tool.Commit(result);
        }
        // Alpha-tested individual leaves must retain their authoring topology.
        // Generic collapse LODs can erase projected leaf area even with stable bounds.
        var mesh=asset is "tree_small_02" or "fir_sapling"?result:WithScreenLods(result);_meshes[key]=mesh;return mesh;
    }
    private void ValidateEnvironmentSources()
    {
        foreach(var asset in EnvironmentHashes.Keys)
        {
            var mesh=SourceEnvironment(asset);if(mesh!=SourceEnvironment(asset))throw new InvalidOperationException("Environment source cache is unstable");
            int count=0;
            for(int s=0;s<mesh.GetSurfaceCount();s++)
            {
                if(mesh.SurfaceGetMaterial(s) is not ShaderMaterial)throw new InvalidOperationException("Environment source lost its material");
                var data=mesh.SurfaceGetArrays(s);var p=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=data[(int)Mesh.ArrayType.Normal].AsVector3Array();var uv=data[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                if(p.Length!=n.Length||p.Length!=uv.Length)throw new InvalidOperationException("Source environment attributes are incomplete");
                count+=p.Length;foreach(var v in p)if(!v.IsFinite())throw new InvalidOperationException("Invalid source environment position");
                foreach(var v in n)if(!v.IsFinite()||v.LengthSquared()<.8f)throw new InvalidOperationException("Invalid source environment normal");
            }
            int vertexBudget=asset=="tree_small_02"?220000:asset=="fir_sapling"?190000:150000;
            if(count>vertexBudget||count<30)throw new InvalidOperationException("Source environment mesh is outside budget");
        }
        GD.Print("ENVIRONMENT_SOURCES_PASS: five licensed meshes, source hashes, PBR/alpha resources, finite attributes, material/cache preservation and budgets");
    }
    internal void BindEnvironmentGround(Texture2D soil,Vector2 size)
    {
        SourceEnvironment("grass_medium_01");
        foreach(var material in _sourceVegetationMaterials.Values)
        {material.SetShaderParameter("soil_bound",true);material.SetShaderParameter("soil",soil);material.SetShaderParameter("map_size",size);}
    }
}
