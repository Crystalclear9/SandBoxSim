using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<string, Material> _residentMaterials = new();
    private Material ResidentMaterial(string color, float roughness = .88f)
    {
        if (!_residentMaterials.TryGetValue(color, out var material))
        {
            bool fabric = color is "#536d68" or "#b4956a" or "#866756" or "#6e7881" or "#798261" or "#a8937d" or "#75654f" or "#645e53";
            material = fabric ? Fabric(color) : new StandardMaterial3D { AlbedoColor = new Color(color), Roughness = roughness, MetallicSpecular = .22f };
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
        const int sides = 48;
        float[] height = { .60f, .65f, .73f, .82f, .97f, 1.10f, 1.19f, 1.24f, 1.28f };
        float[] width = { .20f, .208f, .17f, .165f, .188f, .210f, .220f, .174f, .060f };
        float[] depth = { .145f, .15f, .125f, .125f, .145f, .155f, .13f, .10f, .07f };
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetSmoothGroup(0);
        Vector3 Point(int ring, int side)
        {
            float angle = side * MathF.Tau / sides;
            float fold = 1 + .025f * MathF.Cos(angle * 10 + ring * .6f) * (ring is 2 or 8 ? .2f : 1);
            return new Vector3(MathF.Cos(angle) * width[ring] * fold, height[ring], MathF.Sin(angle) * depth[ring] * fold);
        }
        for (int ring = 0; ring < height.Length - 1; ring++) for (int side = 0; side < sides; side++)
        {
            Vector3 a = Point(ring, side), b = Point(ring, side + 1), c = Point(ring + 1, side), d = Point(ring + 1, side + 1);
            foreach (Vector3 vertex in new[] { a, b, c, b, d, c }) { surface.AddVertex(vertex); }
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
                    var uv = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil
                        ? System.Array.Empty<Vector2>() : arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                    var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    var normalBasis = part.Transform.Basis.Inverse().Transposed();
                    for (int vertex = 0; vertex < (indices.Length > 0 ? indices.Length : vertices.Length); vertex++)
                    {
                        int index = indices.Length > 0 ? indices[vertex] : vertex;
                        surface.SetNormal((normalBasis * normals[index]).Normalized());
                        surface.SetUV(uv.Length > index ? uv[index] : Vector2.Zero);
                        surface.AddVertex(part.Transform * vertices[index]);
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
        Sculpt(rig.Torso, FittedRibbon("belt", .748f, .775f), Vector3.Zero, ResidentMaterial("#514237"));
        Detail(rig.Torso, "box", TorsoSurface(.015f, .76f, false, .009f), new Vector3(.055f, .045f, .013f), "#b8a17b");
        Detail(rig.Torso, "capsule", new Vector3(.23f, .7f, .075f), new Vector3(.075f, .065f, .055f), "#725b40");
        Sculpt(rig.Torso,Loft("resident-neck",new[]{new Vector4(0,0,.051f,.043f),new(.04f,-.006f,.041f,.039f),new(.095f,-.012f,.047f,.040f)}),new(0,1.265f,.016f),ResidentMaterial(skin),new(-MathF.PI/2,0,0));
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0, 1.445f, 0) }; rig.Torso.AddChild(rig.Head);
        Detail(rig.Head, "face", Vector3.Zero, new Vector3(.177f, .220f, .195f), skin);
        Detail(rig.Head, "hair", new Vector3(0, .008f, .006f), new Vector3(.181f, .224f, .200f), hair);
        foreach (float side in new[] { -1f, 1f })
        {
            Detail(rig.Head,"sphere",new(side*.087f,-.005f,0),new(.024f,.043f,.026f),skin);
            Detail(rig.Head,"seed",new(side*.095f,-.003f,-.008f),new(.006f,.026f,.014f),"#a58065");
            Detail(rig.Head, "sphere", new Vector3(side * .034f, .019f, -.083f), new Vector3(.024f, .010f, .007f), "#e5dbc6");
            Detail(rig.Head, "sphere", new Vector3(side * .034f, .019f, -.087f), new Vector3(.009f, .008f, .004f), "#342f28");
            Detail(rig.Head, "capsule", new Vector3(side * .034f, .034f, -.085f), new Vector3(.005f, .024f, .005f), hair, new Vector3(0, 0, MathF.PI * .5f));
        }
        Detail(rig.Head, "seed", new Vector3(0, -.055f, -.092f), new Vector3(.032f, .004f, .006f), "#a37862");
        Detail(rig.Head, "seed", new Vector3(0, -.060f, -.092f), new Vector3(.029f, .005f, .005f), "#b98c73");
        FineBeam(rig.Head,new(-.013f,-.058f,-.095f),new(.013f,-.058f,-.095f),.0013f,"#785b4c");
        foreach(float side in new[]{-1f,1f})
        {
            Detail(rig.Head,"sphere",new(side*.006f,-.028f,-.101f),new(.006f,.003f,.003f),"#805e4c");
            Detail(rig.Head,"capsule",new(side*.034f,.014f,-.085f),new(.004f,.023f,.005f),skin,new(0,0,MathF.PI/2));
            Detail(rig.Head,"sphere",new(side*.034f+.001f,.020f,-.090f),Vector3.One*.003f,"#eee4d3");
        }
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1 : 1;
            var arm = rig.Arms[i] = new Node3D { Name = i == 0 ? "LeftArm" : "RightArm", Position = new Vector3(side * .218f, 1.18f, 0) }; rig.Torso.AddChild(arm);
            Sculpt(arm,Loft("tailored-sleeve",new[]{new Vector4(0,0,.053f,.048f),new(.06f,0,.066f,.060f),new(.17f,0,.057f,.053f),new(.27f,0,.047f,.044f),new(.29f,0,.043f,.042f)}),Vector3.Zero,ResidentMaterial(cloth),new(MathF.PI/2,0,0));
            var elbow = rig.Elbows[i] = new Node3D { Name = "Elbow", Position = new Vector3(0, -.28f, 0) }; arm.AddChild(elbow);
            Sculpt(elbow,Loft("resident-forearm",new[]{new Vector4(-.015f,0,.039f,.037f),new(.06f,0,.042f,.040f),new(.14f,0,.034f,.033f),new(.24f,0,.029f,.028f)}),Vector3.Zero,ResidentMaterial(skin),new(MathF.PI/2,0,0));
            Sculpt(elbow,Loft("resident-palm",new[]{new Vector4(-.05f,0,.008f,.01f),new(-.03f,0,.031f,.025f),new(.016f,0,.035f,.019f),new(.045f,0,.022f,.012f)}),new(0,-.255f,-.01f),ResidentMaterial(skin),new(MathF.PI/2,0,0));
            bool gripping = i == 1 && (job is JobType.Builder or JobType.Miner or JobType.Craftsman or JobType.Soldier);
            for(int digit=0;digit<4;digit++)
                Detail(elbow,"capsule",new(-.023f+digit*.015f,gripping ? -.282f : -.303f,gripping ? -.023f : -.012f),new(.012f,.032f-(digit%3)*.003f,.012f),skin,new(gripping ? -.9f : 0,0,(digit-1.5f)*.035f));
            Detail(elbow,"capsule",new(side*.04f,-.264f,-.022f),new(.016f,.023f,.017f),skin,new(0,0,side*.45f));
            Detail(arm,"cylinder",new(0,-.252f,0),new(.102f,.025f,.098f),"#b9b099");
            foreach(float seam in new[]{-1f,1f})
                FineBeam(arm,new(seam*.052f,-.05f,-.025f),new(seam*.048f,-.23f,-.025f),.006f,"#b9b099");
            var leg = rig.Legs[i] = new Node3D { Name = i == 0 ? "LeftLeg" : "RightLeg", Position = new Vector3(side * .10f, .67f, 0) }; rig.AddChild(leg);
            Sculpt(leg,Loft("resident-trouser",new[]{new Vector4(-.01f,0,.065f,.058f),new(.08f,0,.077f,.073f),new(.20f,0,.070f,.060f),new(.33f,0,.051f,.05f)}),Vector3.Zero,ResidentMaterial("#645e53"),new(MathF.PI/2,0,0));
            var knee = rig.Knees[i] = new Node3D { Name = "Knee", Position = new Vector3(0, -.31f, 0) }; leg.AddChild(knee);
            Sculpt(knee,Loft("resident-boot",new[]{new Vector4(0,0,.055f,.052f),new(.07f,0,.060f,.058f),new(.19f,0,.05f,.048f),new(.28f,-.012f,.052f,.051f)}),Vector3.Zero,ResidentMaterial("#514237"),new(MathF.PI/2,0,0));
            Detail(knee,"cylinder",new(0,-.04f,0),new(.122f,.019f,.118f),"#a48d6d");
            Detail(knee, "sphere", new Vector3(0, -.295f, -.04f), new Vector3(.14f, .085f, .24f), "#423a32");
            Detail(knee,"sphere",new(0,-.326f,-.044f),new(.145f,.025f,.235f),"#302e28");
            for(int lace=0;lace<3;lace++) FineBeam(knee,new(-.032f,-.26f,-.04f-lace*.023f),new(.032f,-.26f,-.054f-lace*.023f),.005f,"#a48d6d");
        }
        AddResidentEquipment(rig, job);
        AddResidentTailoring(rig, cloth, skin, job);
        MergeResidentParts(rig, "resident:" + cloth + skin + hair + job);
        rig.Head.Scale=new Vector3(.96f+((variation>>15)%4)*.025f,.98f+((variation>>18)%3)*.025f,1);
        if (child) { rig.Scale = new Vector3(.72f, .65f, .72f); rig.Head.Scale = Vector3.One * 1.15f; }
        else { rig.Scale = new Vector3(1 + ((variation >> 8) % 5 - 2f) * .025f, 1.055f + ((variation >> 12) % 5) * .015f, 1); }
        return rig;
    }
    public void ValidateResidentMeshes()
    {
        foreach (var key in new[]{"belt","apron","armor","shoulder-front","shoulder-back"})
        {
            bool back=key=="shoulder-back";
            var mesh=FittedRibbon(key,key=="belt"?.748f:key=="armor"?.91f:key=="apron"?.79f:.81f,key=="belt"?.775f:key=="armor"||key=="apron"?1.17f:1.20f,back);
            var arrays=mesh.SurfaceGetArrays(0);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<vertices.Length;i++)
            {
                var p=vertices[i];bool rear=key=="belt"?p.Z>0:back;
                float gap=MathF.Abs(p.Z-TorsoSurface(p.X,p.Y,rear,0).Z);
                if(gap<.001f || gap>.012f || !normals[i].IsFinite())
                    throw new InvalidOperationException("Clothing layer floats or intersects garment: "+key);
            }
        }
        foreach (JobType job in Enum.GetValues<JobType>()) foreach (bool child in new[] { false, true })
        {
            var resident = Resident(42, child, job);
            var body = resident.Torso.GetChild<MeshInstance3D>(resident.Torso.GetChildCount() - 1).Mesh;
            // Losing the non-indexed garment leaves only collar/fold strips in the cloth surface.
            if (body.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length < 50)
            { throw new InvalidOperationException("Resident garment disappeared during mesh consolidation"); }
            if (job is JobType.Farmer or JobType.Soldier)
            {
                if (resident.Headwear?.GetParent() != resident.Head) throw new InvalidOperationException("Headwear detached from head joint");
                var hat = HeadwearShell(job == JobType.Soldier).GetAabb();
                if (hat.Position.Y > .04f || hat.End.Y < .13f) throw new InvalidOperationException("Headwear does not enclose scalp");
            }
            if (job is JobType.Builder or JobType.Miner or JobType.Craftsman or JobType.Soldier)
            {
                if (resident.Grip?.GetParent() != resident.Elbows[1] || resident.Grip.Position.DistanceTo(new Vector3(0,-.273f,-.027f))>.001f)
                    throw new InvalidOperationException("Equipment does not pass through hand grip");
            }
            foreach (var pose in new[]{(true,false,false),(false,true,false),(false,false,true)})
            {
                var grip = resident.Grip?.Transform; var headwear = resident.Headwear?.Transform;
                resident.Pose(.2f, pose.Item1, pose.Item2, pose.Item3, false, 0);
                if (!resident.Knees[0].Transform.IsFinite() || !resident.Elbows[1].Transform.IsFinite())
                    throw new InvalidOperationException("Resident pose produced invalid joints");
                if (pose.Item2 && resident.Grip != null)
                {
                    var elbowToTorso=resident.Arms[1].Transform*resident.Elbows[1].Transform;
                    if ((elbowToTorso*resident.Grip.Position).Z>=0)
                        throw new InvalidOperationException("Working grip bends behind the body");
                }
                if (resident.Grip?.Transform != grip || resident.Headwear?.Transform != headwear)
                    throw new InvalidOperationException("Equipment moved relative to attachment joint");
            }
            resident.Free();
        }
        GD.Print("RESIDENT_FIT_PASS: scalp enclosure, fitted clothing gaps, hand grips and forward work poses across jobs/ages");
    }
}
