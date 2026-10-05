using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

/// <summary>Reusable volumetric geometry, textured with original material assets. All geometry survives camera rotation.</summary>
internal sealed partial class NatureModels
{
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
    }
    public Material Material(int id) => _materials[id];
    public Mesh Shape(string kind)
    {
        if (_meshes.TryGetValue(kind, out Mesh? mesh)) { return mesh; }
        mesh = kind switch
        {
            "box" => new BoxMesh(),
            "face" => SculptedHead(false),
            "hair" => SculptedHead(true),
            "cone" => new CylinderMesh { TopRadius = 0, BottomRadius = .5f, Height = 1, RadialSegments = 16 },
            "cylinder" => new CylinderMesh { TopRadius = .5f, BottomRadius = .5f, Height = 1, RadialSegments = 16 },
            "capsule" => new CapsuleMesh { Radius = .5f, Height = 2, RadialSegments = 12, Rings = 6 },
            "foliage" => IrregularSphere(.16f),
            "rock" => IrregularSphere(.22f),
            _ => new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 20, Rings = 12 }
        };
        _meshes[kind] = mesh; return mesh;
    }
    private static ArrayMesh IrregularSphere(float irregularity)
    {
        const int rings = 16, sides = 24;
        var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
        for (int row = 0; row <= rings; row++) for (int col = 0; col <= sides; col++)
        {
            float latitude = row * MathF.PI / rings, angle = col * MathF.Tau / sides;
            var normal = new Vector3(MathF.Sin(latitude) * MathF.Cos(angle), MathF.Cos(latitude), MathF.Sin(latitude) * MathF.Sin(angle));
            float noise = MathF.Sin(normal.X * 19 + normal.Y * 13) * MathF.Cos(normal.Z * 17 - normal.Y * 11);
            verts.Add(normal * (.5f + noise * irregularity * .5f)); normals.Add(normal); uv.Add(new Vector2(col / (float)sides, row / (float)rings));
        }
        for (int row = 0; row < rings; row++) for (int col = 0; col < sides; col++)
        { int a = row * (sides + 1) + col, b = a + sides + 1; indices.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); }
        var data = new Godot.Collections.Array(); data.Resize((int)Mesh.ArrayType.Max); data[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        data[(int)Mesh.ArrayType.Normal] = normals.ToArray(); data[(int)Mesh.ArrayType.TexUV] = uv.ToArray(); data[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data); return mesh;
    }
    public MeshInstance3D Part(Node3D parent, string shape, Vector3 position, Vector3 scale, int material, Vector3? rotation = null)
    {
        var part = new MeshInstance3D { Mesh = Shape(shape), Position = position, Scale = scale, MaterialOverride = Material(material),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        if (rotation.HasValue) { part.Rotation = rotation.Value; }
        parent.AddChild(part); return part;
    }
    public static Transform3D Transform(Vector3 position, Vector3 scale, Vector3? rotation = null)
        => new(new Basis(Quaternion.FromEuler(rotation ?? Vector3.Zero)).Scaled(scale), position);
    public void Batch(Node3D parent, string shape, int material, List<Transform3D> transforms)
    {
        if (transforms.Count == 0) { return; }
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = Shape(shape), InstanceCount = transforms.Count };
        for (int i = 0; i < transforms.Count; i++) { multi.SetInstanceTransform(i, transforms[i]); }
        parent.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = Material(material) });
    }
    public Node3D Building(BuildingKind kind, bool complete, uint identity = 0) => Architecture(kind, complete, identity);
    public Node3D Human(int slot, bool child, JobType job) => Resident(slot, child, job);
    public Node3D Animal(bool wolf)
    {
        var model = new Node3D(); int mat = wolf ? 13 : 12;
        Part(model, "sphere", new Vector3(0, .7f, 0), new Vector3(.62f, .68f, 1.1f), mat);
        Part(model, "capsule", new Vector3(0, 1.0f, -.47f), new Vector3(.26f, .27f, .28f), mat, new Vector3(-.4f, 0, 0));
        Part(model, "sphere", new Vector3(0, 1.2f, -.7f), new Vector3(.35f, .36f, .43f), mat);
        Part(model, "sphere", new Vector3(0, 1.15f, -.92f), new Vector3(.20f, .15f, .27f), mat);
        foreach (float side in new[] { -1f, 1f })
        {
            Part(model, "cone", new Vector3(side * .13f, 1.42f, -.66f), new Vector3(.13f, .23f, .15f), mat);
            Part(model, "sphere", new Vector3(side * .16f, 1.23f, -.82f), new Vector3(.04f, .04f, .04f), 10);
            foreach (float z in new[] { -.35f, .35f })
                { Part(model, "cylinder", new Vector3(side * .2f, .32f, z), new Vector3(.09f, .62f, .09f), mat); }
            if (!wolf)
                for (int branch = 0; branch < 3; branch++)
                    { Part(model, "cylinder", new Vector3(side * (.12f + branch * .09f), 1.62f + branch * .12f, -.55f), new Vector3(.04f, .43f, .04f), 0, new Vector3(.12f, 0, -side * (.2f + branch * .3f))); }
        }
        Part(model, "capsule", new Vector3(0, .7f, .7f), new Vector3(.14f, .25f, .15f), mat, new Vector3(.9f, 0, 0));
        return model;
    }
}
