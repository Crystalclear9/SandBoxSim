using Godot;
using System.Collections.Generic;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // Shared by the shell, domestic props and terrain skirt. Openings stay human-sized.
    internal static Vector2 BuildingFootprint(uint identity)
    {
        identity %= 140;
        return new(3.65f + (identity % 4) * .16f, 3.05f + ((identity / 7) % 4) * .18f);
    }
    private Node3D Architecture(BuildingKind kind, bool complete, uint identity)
    {
        identity %= 140;
        int variant = (int)(identity % 5);
        var model = new Node3D { Scale = Vector3.One * (.96f + (identity % 7) * .008f) };
        // The merged mesh already contains all static details; do not rebuild temporary parts on a cache hit.
        if(_meshes.TryGetValue($"architecture:{kind}:{complete}:{identity}:{_fineArchitecture}",out var cached))
        {model.AddChild(new MeshInstance3D {Mesh=cached});return model;}
        var footprint = BuildingFootprint(identity);
        float width = footprint.X, depth = footprint.Y;
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
            Part(model, "box", new(0, 1.10f, 0), new(width, 2.2f, depth), 1);
            Gable(model, width, depth, 2.2f, 2);
            Part(model, "box", new(0, 1.105f, depth / 2 + .04f), new(.95f, 1.85f, .08f), 10);
            for (int i = 0; i < 3; i++) Part(model, "box", new(-.65f + i * .52f, .18f, depth / 2 + .28f), new(.4f, .36f, .4f), 1);
            return FinishArchitecture(model, kind, complete, identity, width, depth);
        }
        // Five silhouettes: log cabin, plaster cottage, stone cottage, tall gable, lean-to cottage.
        float height = variant == 3 ? 2.6f : variant == 0 ? 2.05f : 2.15f;
        int walls = variant == 0 ? 1 : variant == 2 ? 6 : 3;
        Part(model, "box", new(0, .12f, 0), new(width + .1f, .24f, depth + .1f), 6);
        HouseWallShell(model,width,depth,height,variant,walls);
        if(variant==1 || variant==3)
        {
            foreach(float x in new[]{-width*.28f,0f,width*.28f})
                Part(model,"box",new(x,height/2+.18f,depth/2+.033f),new(.08f,height,.075f),0);
            Part(model,"box",new(0,height*.66f+.18f,depth/2+.036f),new(width,.09f,.075f),0);
        }
        if (variant == 4)
            Part(model, "box", new(0, height + .4f, 0), new(width + .4f, .16f, depth + .4f), 1, new(0, 0, .18f));
        else
        {
            Gable(model, width, depth, height + .18f, variant == 0 ? 15 : 2);
            GableWalls(model, width, depth, height + .18f, walls);
        }
        float doorX = variant is 0 or 4 ? -.28f : 0;
        Part(model, "box", new(doorX, 1.055f, depth / 2 + .04f), new(.82f, 1.75f, .1f), 10);
        Part(model, "box", new(doorX, .13f, depth / 2 + .18f), new(.86f, .14f, .3f), 6);
        Window(model, width*.355f, 1.27f, depth / 2 + .07f);
        if(variant is 1 or 2 or 3)Window(model,-width*.355f,1.27f,depth/2+.07f);
        if (variant == 3) Window(model, 0, 2.45f, depth / 2 + .07f);
        foreach (float side in new[] { -1f, 1f })
            Part(model, "box", new(side * width / 2, height / 2 + .18f, depth / 2 + .04f), new(.09f, height, .08f), 0);
        if (variant == 1 || variant == 3)
            Part(model, "box", new(-.5f, height + .72f, -.4f), new(.32f, 1.14f, .36f), 6);
        if (variant == 0 || variant == 4)
        {
            Part(model, "box", new(0, 2.09f, depth / 2 + .27f), new(width * .8f, .1f, .55f), 1, new(.12f, 0, 0));
            foreach (float side in new[] { -1f, 1f }) Part(model, "cylinder", new(side * width * .35f, 1.02f, depth / 2 + .46f), new(.06f, 2.04f, .06f), 0);
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
        int first = model.GetChildCount();
        var shutters=new List<(int First,int End,float Side)>();
        // Recessed glazing and a dark reveal sit inside a real wall opening.
        // The mullions and shutters remain proud of the facade.
        Detail(model,"box",new(x,y,z-.155f),new(.32f,.40f,.008f),"#202923");
        Part(model, "box", new(x, y, z-.092f), new(.30f, .38f, .008f), 9);
        Part(model, "box", new(x, y, z-.015f), new(.022f, .42f, .045f), 1);
        Part(model, "box", new(x, y, z-.015f), new(.34f, .022f, .045f), 1);
        foreach(float side in new[] {-1f,1f})
        {
            Part(model,"box",new(x+side*.19f,y,z+.06f),new(.045f,.46f,.09f),0);
            int shutterFirst=model.GetChildCount();
            CraftBoard(model,new(x+side*.27f,y,z+.06f),new(.105f,.39f,.04f),1);
            if(_fineArchitecture)
            {
                for(int slat=0;slat<4;slat++)Part(model,"box",new(x+side*.27f,y-.145f+slat*.096f,z+.085f),new(.10f,.078f,.015f),0);
                foreach(float hingeY in new[]{y-.13f,y+.13f})Part(model,"box",new(x+side*.27f,hingeY,z+.10f),new(.11f,.015f,.008f),11);
            }
            shutters.Add((shutterFirst,model.GetChildCount(),side));
        }
        if(_fineArchitecture)Sculpt(model,DripSill(),new(x,y-.23f,z+.095f),Material(6));
        else Part(model,"box",new(x,y-.23f,z+.095f),new(.48f,.055f,.16f),0);
        Part(model,"box",new(x,y+.23f,z+.04f),new(.44f,.055f,.10f),0);
        // Enlarge the opening and joinery together, preserving the depth of the reveal.
        for(int i=first;i<model.GetChildCount();i++)
        {
            var part=model.GetChild<Node3D>(i);
            var delta=part.Position-new Vector3(x,y,z);
            part.Position=new Vector3(x,y,z)+new Vector3(delta.X*1.5f,delta.Y*1.5f,delta.Z);
            part.Scale*=new Vector3(1.5f,1.5f,1);
        }
        // Rotate the complete shutter around its jamb after sizing. Previously
        // only the boards were angled while their ironwork floated separately.
        foreach(var shutter in shutters)
        {
            var hinge=new Vector3(x+shutter.Side*.315f,y,z+.06f);
            var rotate=new Transform3D(new Basis(Vector3.Up,-shutter.Side*.65f),hinge)*new Transform3D(Basis.Identity,-hinge);
            for(int i=shutter.First;i<shutter.End;i++){var part=model.GetChild<Node3D>(i);part.Transform=rotate*part.Transform;}
        }
    }
    private static Rect2[] HouseOpenings(float width,int variant)
    {
        float doorX=variant is 0 or 4?-.28f:0;
        var openings=new List<Rect2> {new(doorX-.41f,.18f,.82f,1.75f),new(width*.355f-.24f,.97f,.48f,.60f)};
        if(variant is 1 or 2 or 3)openings.Add(new(-width*.355f-.24f,.97f,.48f,.60f));
        if(variant==3)openings.Add(new(-.24f,2.15f,.48f,.60f));
        return openings.ToArray();
    }
    private static IEnumerable<Rect2> WallPatches(Rect2 wall,Rect2[] openings)
    {
        var patches=new List<Rect2> {wall};
        foreach(var opening in openings)
        {
            var next=new List<Rect2>();
            foreach(var p in patches)
            {
                var cut=p.Intersection(opening);
                if(!cut.HasArea()){next.Add(p);continue;}
                void Add(float x,float y,float w,float h){if(w>.0001f&&h>.0001f)next.Add(new(x,y,w,h));}
                Add(p.Position.X,p.Position.Y,cut.Position.X-p.Position.X,p.Size.Y);
                Add(cut.End.X,p.Position.Y,p.End.X-cut.End.X,p.Size.Y);
                Add(cut.Position.X,p.Position.Y,cut.Size.X,cut.Position.Y-p.Position.Y);
                Add(cut.Position.X,cut.End.Y,cut.Size.X,p.End.Y-cut.End.Y);
            }
            patches=next;
        }
        return patches;
    }
    private void HouseWallShell(Node3D model,float width,float depth,float height,int variant,int material)
    {
        foreach(var p in WallPatches(new(-width/2,.18f,width,height),HouseOpenings(width,variant)))
            Part(model,"box",new(p.GetCenter().X,p.GetCenter().Y,depth/2-.07f),new(p.Size.X,p.Size.Y,.14f),material);
        Part(model,"box",new(0,height/2+.18f,-depth/2+.07f),new(width,height,.14f),material);
        foreach(float side in new[]{-1f,1f})
        {
            var openings=_fineArchitecture?new[]{new Rect2(depth*.13f-.24f,.95f,.48f,.60f)}:System.Array.Empty<Rect2>();
            foreach(var p in WallPatches(new(-depth/2,.18f,depth,height),openings))
                Part(model,"box",new(side*(width/2-.07f),p.GetCenter().Y,p.GetCenter().X),new(.14f,p.Size.Y,p.Size.X),material);
        }
        Part(model,"box",new(0,.205f,0),new(width-.28f,.05f,depth-.28f),0);
    }
    private void ValidateWindowRecesses()
    {
        for(uint variant=0;variant<5;variant++)
        {
            var model=Building(BuildingKind.House,true,variant);
            try
            {
                var footprint=BuildingFootprint(variant);float width=footprint.X,depth=footprint.Y;
                var mesh=model.GetChild<MeshInstance3D>(0).Mesh;
                float x=width*.355f+.08f,y=1.35f,nearest=float.NegativeInfinity;
                Material? hit=null;
                for(int s=0;s<mesh.GetSurfaceCount();s++)
                {
                    var a=mesh.SurfaceGetArrays(s);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
                    for(int i=0;i<ids.Length;i+=3)
                    {
                        var a0=p[ids[i]];var b=p[ids[i+1]];var c=p[ids[i+2]];
                        float d=(b.Y-c.Y)*(a0.X-c.X)+(c.X-b.X)*(a0.Y-c.Y);
                        if(Godot.Mathf.Abs(d)<.00000001f)continue;
                        float u=((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/d;
                        float v=((c.Y-a0.Y)*(x-c.X)+(a0.X-c.X)*(y-c.Y))/d;
                        if(u<0||v<0||u+v>1)continue;
                        float z=u*a0.Z+v*b.Z+(1-u-v)*c.Z;
                        if(z>nearest){nearest=z;hit=mesh.SurfaceGetMaterial(s);}
                    }
                }
                if(hit!=Material(9)||nearest>=depth/2-.008f||nearest<depth/2-.05f)
                    throw new System.InvalidOperationException("Window reveal is covered by facade geometry or sits outside its opening: "+variant);
            }
            finally{model.Free();}
        }
        GD.Print("WINDOW_RECESSES_PASS: actual front-window rays reach recessed glazing in all five house variants");
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
