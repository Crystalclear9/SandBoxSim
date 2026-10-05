using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<string, StandardMaterial3D> _residentMaterials = new();
    private Material ResidentMaterial(string color, float roughness = .88f)
    {
        if (!_residentMaterials.TryGetValue(color, out var material))
        {
            material = new StandardMaterial3D { AlbedoColor = new Color(color), Roughness = roughness };
            _residentMaterials[color] = material;
        }
        return material;
    }
    private MeshInstance3D Detail(Node3D parent, string shape, Vector3 position, Vector3 scale, string color, Vector3 rotation = default)
    {
        var detail = new MeshInstance3D { Mesh = Shape(shape), Position = position, Scale = scale,
            Rotation = rotation, MaterialOverride = ResidentMaterial(color) };
        parent.AddChild(detail); return detail;
    }
    private Mesh Garment()
    {
        if (_meshes.TryGetValue("tunic", out var existing)) { return existing; }
        const int sides = 16;
        float[] height = { .61f, .72f, .99f, 1.18f, 1.28f };
        float[] width = { .245f, .205f, .21f, .25f, .115f };
        float[] depth = { .145f, .13f, .145f, .135f, .08f };
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int ring, int side)
        {
            float angle = side * MathF.Tau / sides;
            float fold = ring < 2 ? 1 + .045f * MathF.Cos(angle * 8) : 1;
            return new Vector3(MathF.Cos(angle) * width[ring] * fold, height[ring], MathF.Sin(angle) * depth[ring] * fold);
        }
        for (int ring = 0; ring < height.Length - 1; ring++) for (int side = 0; side < sides; side++)
        {
            Vector3 a = Point(ring, side), b = Point(ring, side + 1), c = Point(ring + 1, side), d = Point(ring + 1, side + 1);
            foreach (Vector3 vertex in new[] { a, c, b, b, c, d }) { surface.AddVertex(vertex); }
        }
        surface.GenerateNormals(); surface.Index(); var mesh = surface.Commit(); _meshes["tunic"] = mesh; return mesh;
    }
    private void MergeResidentParts(Node3D parent, string key)
    {
        var parts = new List<MeshInstance3D>();
        foreach (Node child in parent.GetChildren())
        {
            if (child is MeshInstance3D part) { parts.Add(part); }
            else if (child is Node3D joint) { MergeResidentParts(joint, key + "/" + joint.Name); }
        }
        if (parts.Count < 2) { return; }
        if (!_meshes.TryGetValue(key, out Mesh? combined))
        {
            var groups = new Dictionary<Material, List<MeshInstance3D>>();
            foreach (var part in parts)
            {
                if (!groups.TryGetValue(part.MaterialOverride, out var group)) { group = new(); groups[part.MaterialOverride] = group; }
                group.Add(part);
            }
            var mesh = new ArrayMesh();
            foreach (var group in groups)
            {
                var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetMaterial(group.Key);
                foreach (var part in group.Value)
                {
                    var arrays = part.Mesh.SurfaceGetArrays(0);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    var normalBasis = part.Transform.Basis.Inverse().Transposed();
                    for (int vertex = 0; vertex < (indices.Length > 0 ? indices.Length : vertices.Length); vertex++)
                    {
                        int index = indices.Length > 0 ? indices[vertex] : vertex;
                        surface.SetNormal((normalBasis * normals[index]).Normalized()); surface.AddVertex(part.Transform * vertices[index]);
                    }
                }
                surface.Index(); surface.Commit(mesh);
            }
            combined = mesh; _meshes[key] = mesh;
        }
        foreach (var part in parts) { part.Free(); }
        parent.AddChild(new MeshInstance3D { Mesh = combined });
    }
    public ResidentRig Resident(int identity, bool child, JobType job)
    {
        uint variation = unchecked((uint)identity * 2654435761u);
        string[] clothColors = { "#536d68", "#b4956a", "#866756", "#6e7881", "#798261", "#a8937d" };
        string[] skinColors = { "#c29a7a", "#aa7c5a", "#d3ad8b", "#946b50" };
        string cloth = clothColors[variation % clothColors.Length], skin = skinColors[(variation >> 4) % skinColors.Length];
        string hair = (variation & 1) == 0 ? "#42352c" : "#695344";
        var rig = new ResidentRig { Name = "Resident" };
        rig.Torso = new Node3D { Name = "Torso" }; rig.AddChild(rig.Torso);
        rig.Torso.AddChild(new MeshInstance3D { Mesh = Garment(), MaterialOverride = ResidentMaterial(cloth) });
        // Collar, placket and the hem distinguish tailored cloth from the body's silhouette.
        foreach (float side in new[] { -1f, 1f })
        {
            Detail(rig.Torso, "box", new Vector3(side * .065f, 1.235f, -.095f), new Vector3(.09f, .027f, .017f), "#b9b099", new Vector3(0, 0, side * .35f));
            Detail(rig.Torso, "box", new Vector3(side * .145f, .67f, -.11f), new Vector3(.012f, .13f, .012f), cloth, new Vector3(0, 0, side * -.10f));
        }
        Detail(rig.Torso, "box", new Vector3(0, 1.11f, -.145f), new Vector3(.016f, .18f, .012f), "#b9b099");
        for (int button = 0; button < 3; button++)
        { Detail(rig.Torso, "sphere", new Vector3(.012f, 1.17f - button * .055f, -.154f), Vector3.One * .015f, "#514237"); }
        Detail(rig.Torso, "cylinder", new Vector3(0, .76f, 0), new Vector3(.425f, .045f, .275f), "#514237");
        Detail(rig.Torso, "box", new Vector3(.015f, .76f, -.144f), new Vector3(.055f, .045f, .013f), "#b8a17b");
        Detail(rig.Torso, "capsule", new Vector3(.23f, .7f, .075f), new Vector3(.075f, .065f, .055f), "#725b40");
        Detail(rig.Torso, "cylinder", new Vector3(0, 1.30f, 0), new Vector3(.105f, .10f, .105f), skin);
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0, 1.46f, 0) }; rig.Torso.AddChild(rig.Head);
        Detail(rig.Head, "sphere", Vector3.Zero, new Vector3(.215f, .285f, .215f), skin);
        Detail(rig.Head, "sphere", new Vector3(0, .07f, .015f), new Vector3(.225f, .18f, .225f), hair);
        Detail(rig.Head, "sphere", new Vector3(0, -.005f, -.108f), new Vector3(.037f, .06f, .043f), skin);
        foreach (float side in new[] { -1f, 1f })
        {
            Detail(rig.Head, "sphere", new Vector3(side * .107f, -.005f, 0), new Vector3(.04f, .073f, .046f), skin);
            Detail(rig.Head, "sphere", new Vector3(side * .042f, .025f, -.097f), new Vector3(.025f, .015f, .011f), "#e5dbc6");
            Detail(rig.Head, "sphere", new Vector3(side * .042f, .025f, -.106f), new Vector3(.01f, .011f, .006f), "#342f28");
            Detail(rig.Head, "box", new Vector3(side * .042f, .049f, -.096f), new Vector3(.039f, .01f, .01f), hair);
        }
        Detail(rig.Head, "box", new Vector3(0, -.053f, -.095f), new Vector3(.045f, .008f, .009f), "#976e5a");
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1 : 1;
            var arm = rig.Arms[i] = new Node3D { Name = i == 0 ? "LeftArm" : "RightArm", Position = new Vector3(side * .235f, 1.18f, 0) }; rig.Torso.AddChild(arm);
            Detail(arm, "capsule", new Vector3(0, -.13f, 0), new Vector3(.12f, .14f, .115f), cloth);
            var elbow = rig.Elbows[i] = new Node3D { Name = "Elbow", Position = new Vector3(0, -.28f, 0) }; arm.AddChild(elbow);
            Detail(elbow, "capsule", new Vector3(0, -.115f, 0), new Vector3(.077f, .115f, .077f), skin);
            Detail(elbow, "sphere", new Vector3(0, -.255f, -.01f), new Vector3(.078f, .105f, .065f), skin);
            var leg = rig.Legs[i] = new Node3D { Name = i == 0 ? "LeftLeg" : "RightLeg", Position = new Vector3(side * .10f, .67f, 0) }; rig.AddChild(leg);
            Detail(leg, "capsule", new Vector3(0, -.15f, 0), new Vector3(.145f, .15f, .14f), "#645e53");
            var knee = rig.Knees[i] = new Node3D { Name = "Knee", Position = new Vector3(0, -.31f, 0) }; leg.AddChild(knee);
            Detail(knee, "sphere", Vector3.Zero, new Vector3(.11f, .115f, .105f), "#645e53");
            Detail(knee, "capsule", new Vector3(0, -.13f, 0), new Vector3(.105f, .13f, .10f), "#514237");
            Detail(knee, "sphere", new Vector3(0, -.295f, -.04f), new Vector3(.14f, .085f, .24f), "#423a32");
        }
        if (job == JobType.Farmer)
        {
            Detail(rig.Head, "cylinder", new Vector3(0, .13f, 0), new Vector3(.43f, .025f, .43f), "#b49a69");
            Detail(rig.Head, "cone", new Vector3(0, .19f, 0), new Vector3(.28f, .12f, .28f), "#b49a69");
        }
        if (job is JobType.Builder or JobType.Miner or JobType.Craftsman)
        {
            Detail(rig.Torso, "box", new Vector3(0, .97f, -.15f), new Vector3(.23f, .40f, .016f), "#75654f");
            Detail(rig.Elbows[1], "cylinder", new Vector3(0, -.23f, -.09f), new Vector3(.022f, .38f, .022f), "#8b6c46");
            Detail(rig.Elbows[1], "box", new Vector3(0, -.07f, -.09f), new Vector3(.17f, .06f, .065f), "#68716f");
        }
        if (job is JobType.Hunter or JobType.Trader or JobType.Gatherer)
        {
            Detail(rig.Torso, "box", new Vector3(0, 1.03f, .15f), new Vector3(.26f, .30f, .11f), "#786346");
        }
        if (job == JobType.Soldier)
        {
            Detail(rig.Head, "sphere", new Vector3(0, .08f, .01f), new Vector3(.25f, .19f, .26f), "#7d8581");
            Detail(rig.Torso, "box", new Vector3(0, 1.05f, -.145f), new Vector3(.32f, .32f, .035f), "#7d8581");
            Detail(rig.Elbows[1], "cylinder", new Vector3(0, -.17f, -.08f), new Vector3(.026f, 1.65f, .026f), "#786346");
            Detail(rig.Elbows[1], "cone", new Vector3(0, .7f, -.08f), new Vector3(.075f, .16f, .065f), "#7d8581");
        }
        MergeResidentParts(rig, "resident:" + cloth + skin + hair + job);
        if (child) { rig.Scale = new Vector3(.72f, .65f, .72f); rig.Head.Scale = Vector3.One * 1.15f; }
        else { rig.Scale = new Vector3(1 + ((variation >> 8) % 5 - 2f) * .025f, .96f + ((variation >> 12) % 5) * .02f, 1); }
        return rig;
    }
    public void ValidateResidentMeshes()
    {
        foreach (JobType job in Enum.GetValues<JobType>()) foreach (bool child in new[] { false, true })
        {
            var resident = Resident(42, child, job);
            var body = resident.Torso.GetChild<MeshInstance3D>(resident.Torso.GetChildCount() - 1).Mesh;
            // Losing the non-indexed garment leaves only collar/fold strips in the cloth surface.
            if (body.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length < 50)
            { throw new InvalidOperationException("Resident garment disappeared during mesh consolidation"); }
            resident.Pose(.1f, true, false, false, false, 0);
            if (!resident.Knees[0].Transform.IsFinite() || !resident.Elbows[1].Transform.IsFinite())
            { throw new InvalidOperationException("Resident pose produced invalid joints"); }
            resident.Free();
        }
    }
}
