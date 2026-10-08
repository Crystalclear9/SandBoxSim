using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

/// <summary>Reusable volumetric geometry, textured with original material assets. All geometry survives camera rotation.</summary>
internal sealed partial class NatureModels
{
    private bool _fineArchitecture=true;
    private readonly Material[] _materials = new Material[16];
    private readonly Dictionary<string, Mesh> _meshes = new();
    public NatureModels()
    {
        Texture2D? atlas = ResourceLoader.Exists("res://assets/textures/natural-materials.png") ? GD.Load<Texture2D>("res://assets/textures/natural-materials.png") : null;
        if (atlas != null)
        {
            var shader = new Shader { Code = "shader_type spatial; uniform sampler2D atlas : source_color, filter_linear_mipmap; uniform vec2 cell; uniform vec4 tint : source_color = vec4(1.0); uniform float roughness = 0.92; uniform float repeat_scale = 1.0; void fragment(){ vec2 uv = (clamp(fract(UV*repeat_scale), vec2(0.01), vec2(0.99)) + cell) / 4.0; ALBEDO = texture(atlas, uv).rgb * tint.rgb; ROUGHNESS = roughness; }" };
            for (int i = 0; i < 16; i++)
            {
                var material = new ShaderMaterial { Shader = shader }; material.SetShaderParameter("atlas", atlas);
                material.SetShaderParameter("cell", new Vector2(i % 4, i / 4));
                if (i == 4 || i == 5) { material.SetShaderParameter("tint", new Color(.8f, .88f, .7f)); }
                if (i == 11) { material.SetShaderParameter("roughness", .38f); }
                if (i is 8 or 9 or 12 or 13 or 14) { material.SetShaderParameter("repeat_scale", 4f); }
                else if (i is 0 or 1 or 2 or 3) { material.SetShaderParameter("repeat_scale", 2f); }
                _materials[i] = material;
            }
        }
        else
        {
            string[] colors = { "#655044", "#887151", "#8c5a43", "#c9bea4", "#49633c", "#354f3a", "#8a8c80", "#635d53",
                "#b7a988", "#4e6570", "#493c31", "#7f8e91", "#ad865a", "#858b81", "#bd9676", "#b09a59" };
            for (int i = 0; i < 16; i++) { _materials[i] = new StandardMaterial3D { AlbedoColor = new Color(colors[i]), Roughness = .9f }; }
        }
        InstallCraftMaterials();
    }
    public Material Material(int id) => _materials[id];
    public Mesh Shape(string kind)
    {
        string key=kind is "face" or "hair" ? kind+":"+_faceVariant : kind;
        if (_meshes.TryGetValue(key, out Mesh? mesh)) { return mesh; }
        mesh = kind switch
        {
            "box" => new BoxMesh(),
            "thatch-course" => RoofCourse(true,true),
            "thatch-course-far" => RoofCourse(true,false),
            "tile-course" => RoofCourse(false,true),
            "tile-course-far" => RoofCourse(false,false),
            "cylinder-low" => new CylinderMesh {TopRadius=.5f,BottomRadius=.5f,Height=1,RadialSegments=8,Rings=1},
            "capsule-low" => new CapsuleMesh {Radius=.5f,Height=2,RadialSegments=8,Rings=3},
            "finger-low" => new SphereMesh { Radius=.5f,Height=1,RadialSegments=8,Rings=4 },
            "seed" => new SphereMesh { Radius=.5f,Height=1,RadialSegments=12,Rings=6 },
            "masonry" => SoftBlock(),
            "face" => SculptedHead(false,96,56),
            "hair" => SculptedHead(true),
            "cone" => new CylinderMesh { TopRadius = 0, BottomRadius = .5f, Height = 1, RadialSegments = 16 },
            "cylinder" => new CylinderMesh { TopRadius = .5f, BottomRadius = .5f, Height = 1, RadialSegments = 16 },
            "capsule" => new CapsuleMesh { Radius = .5f, Height = 2, RadialSegments = 12, Rings = 6 },
            "foliage" => Crown(false),
            "pine" => Crown(true),
            "trunk" => BentTrunk(),
            "rock" => IrregularSphere(.22f),
            "grass" => MeadowTuft(),
            _ => new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 20, Rings = 12 }
        };
        _meshes[key] = mesh; return mesh;
    }
    private static ArrayMesh IrregularSphere(float irregularity)
    {
        const int rings = 24, sides = 40;
        var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
        for (int row = 0; row <= rings; row++) for (int col = 0; col <= sides; col++)
        {
            float latitude = row * MathF.PI / rings, angle = col * MathF.Tau / sides;
            var normal = new Vector3(MathF.Sin(latitude) * MathF.Cos(angle), MathF.Cos(latitude), MathF.Sin(latitude) * MathF.Sin(angle));
            float noise = MathF.Sin(normal.X * 4 + normal.Y * 3) * MathF.Cos(normal.Z * 4 - normal.Y * 2) + .18f * MathF.Sin(normal.X * 9 - normal.Z * 7);
            // Rounded fracture planes give stone a broken silhouette rather than a soft sphere.
            float radius=.5f+noise*irregularity*.32f;
            var point=new Vector3(MathF.CopySign(MathF.Pow(MathF.Abs(normal.X),.78f),normal.X),
                MathF.CopySign(MathF.Pow(MathF.Abs(normal.Y),.82f),normal.Y),
                MathF.CopySign(MathF.Pow(MathF.Abs(normal.Z),.76f),normal.Z))*radius;
            point.Y+=point.X*.11f-point.Z*.07f;
            verts.Add(point); normals.Add(Vector3.Zero); uv.Add(new Vector2(col / (float)sides, row / (float)rings));
        }
        for (int row = 0; row < rings; row++) for (int col = 0; col < sides; col++)
        { int a = row * (sides + 1) + col, b = a + sides + 1; indices.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); }
        // Derive shading normals from the deformed surface, including the wrapped seam.
        for(int i=0;i<indices.Count;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2];
            var n=(verts[b]-verts[a]).Cross(verts[c]-verts[a]);
            if(n.Dot(verts[a]+verts[b]+verts[c])<0)n=-n;
            normals[a]+=n;normals[b]+=n;normals[c]+=n;
        }
        for(int row=0;row<=rings;row++)
        {
            int a=row*(sides+1),b=a+sides;var n=normals[a]+normals[b];normals[a]=n;normals[b]=n;
        }
        for(int i=0;i<normals.Count;i++)normals[i]=normals[i].LengthSquared()>1e-12f?normals[i].Normalized():Vector3.Up;
        var data = new Godot.Collections.Array(); data.Resize((int)Mesh.ArrayType.Max); data[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        data[(int)Mesh.ArrayType.Normal] = normals.ToArray(); data[(int)Mesh.ArrayType.TexUV] = uv.ToArray(); data[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data); return mesh;
    }
    public MeshInstance3D Part(Node3D parent, string shape, Vector3 position, Vector3 scale, int material, Vector3? rotation = null)
    {
        if(!_fineArchitecture)shape=shape switch {"sphere"=>"finger-low","cylinder"=>"cylinder-low","capsule"=>"capsule-low",_=>shape};
        var part = new MeshInstance3D { Mesh = Shape(shape == "box" && _fineArchitecture && material is 0 or 1 or 2 or 6 ? "masonry" : shape), Position = position, Scale = scale, MaterialOverride = Material(material),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        if (rotation.HasValue) { part.Rotation = rotation.Value; }
        parent.AddChild(part); return part;
    }
    public static Transform3D Transform(Vector3 position, Vector3 scale, Vector3? rotation = null)
        => new(new Basis(Quaternion.FromEuler(rotation ?? Vector3.Zero)).Scaled(scale), position);
    private static Dictionary<(int X,int Z),List<Transform3D>> PartitionInstances(List<Transform3D> transforms)
    {
        const float cell=24;
        var chunks=new Dictionary<(int X,int Z),List<Transform3D>>();
        foreach(var transform in transforms)
        {
            var key=((int)MathF.Floor(transform.Origin.X/cell),(int)MathF.Floor(transform.Origin.Z/cell));
            if(!chunks.TryGetValue(key,out var group)){group=new();chunks[key]=group;}
            group.Add(transform);
        }
        return chunks;
    }
    private static Aabb InstanceBounds(List<Transform3D> group,Aabb meshBounds)
    {
        var bounds=group[0]*meshBounds;
        for(int i=1;i<group.Count;i++)bounds=bounds.Merge(group[i]*meshBounds);
        return bounds.Grow(.01f);
    }
    public void Batch(Node3D parent, string shape, int material, List<Transform3D> transforms, Material? surface=null, float range=0)
    {
        // Each chunk has its own bounds: a visible patch no longer draws the whole forest.
        foreach(var group in PartitionInstances(transforms).Values)
        {
            var multi=new MultiMesh {TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=Shape(shape),InstanceCount=group.Count};
            for(int i=0;i<group.Count;i++)multi.SetInstanceTransform(i,group[i]);
            multi.CustomAabb=InstanceBounds(group,multi.Mesh.GetAabb()).Grow(shape=="grass"?.03f:0);
            parent.AddChild(new MultiMeshInstance3D {Multimesh=multi,MaterialOverride=surface??Material(material), VisibilityRangeEnd=range, CastShadow=shape=="grass"?GeometryInstance3D.ShadowCastingSetting.Off:GeometryInstance3D.ShadowCastingSetting.On});
        }
    }

    public Node3D Building(BuildingKind kind, bool complete, uint identity = 0) => Architecture(kind, complete, identity);
    public Mesh DistantBuilding(BuildingKind kind,bool complete,uint identity)
    {
        _fineArchitecture=false;
        try{var node=Architecture(kind,complete,identity);var mesh=node.GetChild<MeshInstance3D>(0).Mesh;node.Free();return mesh;}
        finally{_fineArchitecture=true;}
    }
    public Node3D Human(int slot, bool child, JobType job) => Resident(slot, child, job);
    public Node3D Animal(bool wolf) => DetailedAnimal(wolf);
}
