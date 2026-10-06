using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Node3D Architecture(BuildingKind kind, bool complete, uint identity)
    {
        identity %= 140;
        int variant = (int)(identity % 5);
        var model = new Node3D { Scale = new Vector3(.82f, 1.02f + (identity % 7) * .025f, .82f) };
        float width = 1.85f + (identity % 4) * .12f, depth = 1.8f + ((identity / 7) % 4) * .13f;
        if (!complete)
        {
            Part(model, "box", new(0, .09f, 0), new(width, .18f, depth), 6);
            foreach (float x in new[] { -width / 2, width / 2 }) foreach (float z in new[] { -depth / 2, depth / 2 })
                Part(model, "box", new(x, .85f, z), new(.1f, 1.7f, .1f), 0);
            Part(model, "box", new(0, 1.65f, depth / 2), new(width + .2f, .1f, .1f), 0);
            for (int i = 0; i < 3; i++) Part(model, "box", new(-.3f, .2f + i * .12f, .2f), new(.9f, .09f, .18f), 1);
            return FinishArchitecture(model, kind, complete, identity, width, depth);
        }
        if (kind == BuildingKind.Farm)
        {
            Part(model, "box", new(0, .06f, 0), new(2.2f, .12f, 2.2f), 1);
            for (int row = 0; row < 4; row++) for (int col = 0; col < 5; col++)
            {
                float h = .4f + ((identity + (uint)row + (uint)col) % 4) * .08f;
                var p = new Vector3(-.8f + row * .5f, h / 2, -.8f + col * .4f);
                Part(model, "cylinder", p, new(.025f, h, .025f), 15);
                Part(model, "capsule", p + Vector3.Up * h / 2, new(.09f, .1f, .09f), 15);
            }
            Part(model, "box", new(1.05f, .3f, 0), new(.06f, .06f, 2.2f), 0); return FinishArchitecture(model, kind, complete, identity, width, depth);
        }
        if (kind == BuildingKind.Mine)
        {
            // Build around an open portal; a full ellipsoid would protrude through the tunnel entrance.
            Part(model,"rock",new(0,.9f,-.9f),new(2.8f,2.1f,1.0f),6);
            foreach(float side in new[]{-1f,1f}) Part(model,"rock",new(side*1.05f,.85f,-.15f),new(.85f,1.8f,2.1f),6);
            Part(model,"rock",new(0,1.9f,-.35f),new(1.5f,.9f,1.8f),6);
            Detail(model, "box", new(0, .7f, .8f), new(1.05f, 1.4f, .12f), "#151b18");
            foreach (float x in new[] { -.64f, .64f }) Part(model, "box", new(x, .8f, 1.05f), new(.17f, 1.6f, .2f), 0);
            Part(model, "box", new(0, 1.55f, 1.05f), new(1.45f, .2f, .2f), 0);
            for (int i = 0; i < 4; i++) Part(model, "box", new(0, .1f, .8f + i * .22f), new(.8f, .08f, .07f), 1);
            return FinishArchitecture(model, kind, complete, identity, width, depth);
        }
        if (kind == BuildingKind.Storage)
        {
            Part(model, "box", new(0, .7f, 0), new(width, 1.4f, depth), 1);
            Gable(model, width, depth, 1.4f, 2);
            Part(model, "box", new(0, .6f, depth / 2 + .04f), new(.85f, 1.2f, .08f), 10);
            for (int i = 0; i < 3; i++) Part(model, "box", new(-.65f + i * .52f, .18f, depth / 2 + .28f), new(.4f, .36f, .4f), 1);
            return FinishArchitecture(model, kind, complete, identity, width, depth);
        }
        // Five silhouettes: log cabin, plaster cottage, stone cottage, tall gable, lean-to cottage.
        float height = variant == 3 ? 2.6f : variant == 0 ? 1.8f : 2.15f;
        int walls = variant == 0 ? 1 : variant == 2 ? 6 : 3;
        Part(model, "box", new(0, .12f, 0), new(width + .1f, .24f, depth + .1f), 6);
        Part(model, "box", new(0, height / 2 + .18f, 0), new(width, height, depth), walls);
        if (variant == 4)
            Part(model, "box", new(0, height + .4f, 0), new(width + .4f, .16f, depth + .4f), 1, new(0, 0, .18f));
        else
        {
            Gable(model, width, depth, height + .18f, variant == 0 ? 15 : 2);
            GableWalls(model, width, depth, height + .18f, walls);
        }
        float doorX = variant % 2 == 0 ? -.28f : .28f;
        Part(model, "box", new(doorX, .95f, depth / 2 + .04f), new(.62f, 1.55f, .1f), 10);
        Part(model, "box", new(doorX, .13f, depth / 2 + .18f), new(.64f, .14f, .3f), 6);
        Window(model, variant % 2 == 0 ? .57f : -.57f, 1.27f, depth / 2 + .07f);
        if (variant == 3) Window(model, 0, 2.12f, depth / 2 + .07f);
        foreach (float side in new[] { -1f, 1f })
            Part(model, "box", new(side * width / 2, height / 2 + .18f, depth / 2 + .04f), new(.09f, height, .08f), 0);
        if (variant == 1 || variant == 3)
            Part(model, "box", new(-.5f, height + .5f, -.4f), new(.25f, .7f, .28f), 6);
        if (variant == 0 || variant == 4)
        {
            Part(model, "box", new(0, 1.83f, depth / 2 + .27f), new(width * .8f, .1f, .55f), 1, new(.12f, 0, 0));
            foreach (float side in new[] { -1f, 1f }) Part(model, "cylinder", new(side * width * .35f, .89f, depth / 2 + .46f), new(.06f, 1.78f, .06f), 0);
        }
        Part(model, "cylinder", new(width / 2 - .13f, .2f, -depth / 2 - .07f), new(.25f, .4f, .25f), 1);
        return FinishArchitecture(model, kind, complete, identity, width, depth);
    }
    private void Gable(Node3D model, float width, float depth, float height, int material)
    {
        float half = (width + .4f) / 2;
        foreach (float side in new[] { -1f, 1f })
            Part(model, "box", new(side * half / 2, height + half * .25f, 0), new(half * 1.12f, .13f, depth + .4f), material, new(0, 0, -side * .46f));
        RoofCourses(model, width, depth, height, material);
        Part(model, "box", new(0, height + half * .48f, 0), new(.12f, .12f, depth + .4f), 0);
    }
    private void Window(Node3D model, float x, float y, float z)
    {
        Part(model, "box", new(x, y, z), new(.32f, .4f, .08f), 9);
        Part(model, "box", new(x, y, z + .05f), new(.035f, .44f, .06f), 1);
        Part(model, "box", new(x, y, z + .05f), new(.36f, .035f, .06f), 1);
        foreach(float side in new[] {-1f,1f})
        {
            Part(model,"box",new(x+side*.19f,y,z+.06f),new(.045f,.46f,.09f),0);
            Part(model,"box",new(x+side*.27f,y,z+.06f),new(.105f,.39f,.04f),1,new(0,side*.18f,0));
        }
        Part(model,"box",new(x,y-.23f,z+.095f),new(.48f,.055f,.16f),0);
        Part(model,"box",new(x,y+.23f,z+.04f),new(.44f,.055f,.10f),0);
    }
    private void GableWalls(Node3D model, float width, float depth, float height, int material)
    {
        string key = $"gable:{width}:{depth}:{height}";
        if (!_meshes.TryGetValue(key, out Mesh? mesh))
        {
            using var tool = new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles);
            foreach (float side in new[] { 1f, -1f })
            {
                float z = side * depth / 2;
                tool.SetNormal(new Vector3(0, 0, side));
                tool.SetUV(new Vector2(0, 1)); tool.AddVertex(new Vector3(-side * width / 2, height, z));
                tool.SetUV(new Vector2(.5f, 0)); tool.AddVertex(new Vector3(0, height + (width + .4f) * .24f, z));
                tool.SetUV(new Vector2(1, 1)); tool.AddVertex(new Vector3(side * width / 2, height, z));
                // Both windings keep the thin gable visible from outside and inside the overhang.
                tool.SetUV(new Vector2(0, 1)); tool.AddVertex(new Vector3(-side * width / 2, height, z));
                tool.SetUV(new Vector2(1, 1)); tool.AddVertex(new Vector3(side * width / 2, height, z));
                tool.SetUV(new Vector2(.5f, 0)); tool.AddVertex(new Vector3(0, height + (width + .4f) * .24f, z));
            }
            mesh = tool.Commit(); _meshes[key] = mesh;
        }
        model.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = Material(material) });
    }
}
