using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // Profiles run along local Z: z, center height, half width, half height.
    private Mesh Loft(string key, Vector4[] profiles, int sides = 32)
    {
        if (_meshes.TryGetValue(key, out var cached)) return cached;
        if (profiles[0].X > profiles[^1].X) Array.Reverse(profiles);
        bool animal=key.StartsWith("wolf",StringComparison.Ordinal)||key.StartsWith("deer",StringComparison.Ordinal)||key.StartsWith("animal",StringComparison.Ordinal);
        int subdivisions=animal?6:key is "tailored-sleeve" or "resident-trouser" or "resident-boot"?8:4;if(animal)sides=Math.Max(sides,40);
        // Smooth profile interpolation avoids a string of visibly separate barrel sections.
        var dense = new Vector4[(profiles.Length - 1) * subdivisions + 1];
        for (int r=0;r<dense.Length;r++)
        {
            int i=Math.Min(r/subdivisions,profiles.Length-2); float t=(r-i*subdivisions)/(float)subdivisions;
            var a=profiles[Math.Max(0,i-1)]; var b=profiles[i]; var c=profiles[i+1]; var d=profiles[Math.Min(profiles.Length-1,i+2)];
            var v=(b*2+(c-a)*t+(a*2-b*5+c*4-d)*t*t+(-a+b*3-c*3+d)*t*t*t)*.5f;
            v.Z=MathF.Max(.002f,v.Z); v.W=MathF.Max(.002f,v.W); dense[r]=v;
        }
        profiles=dense;
        using var tool = new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles); tool.SetSmoothGroup(0);
        Vector3 Point(int ring, int side)
        {
            float angle = side * MathF.Tau / sides; var p = profiles[ring];
            float crease=0;
            if(key=="tailored-sleeve")crease=.0025f*MathF.Sin(p.X*125+MathF.Sin(angle)*2)*MathF.Exp(-MathF.Pow((p.X-.23f)*18,2));
            else if(key=="resident-trouser")crease=.002f*MathF.Sin(p.X*115+MathF.Cos(angle)*3)*MathF.Exp(-MathF.Pow((p.X-.28f)*17,2));
            else if(key=="resident-boot")crease=.0015f*MathF.Sin(p.X*150+MathF.Cos(angle)*2)*MathF.Exp(-MathF.Pow((p.X-.23f)*20,2));
            float sole=p.Y+MathF.Sin(angle)*(p.W+crease);
            if(key is "animal-paw" or "animal-hoof")sole=MathF.Max(.003f,sole);
            if(key=="animal-paw"&&MathF.Sin(angle)>0&&p.X<-.035f)
                sole-=.0018f*MathF.Exp(-MathF.Pow((p.X+.074f)*18,2))*MathF.Pow(MathF.Cos(MathF.Cos(angle)*p.Z*95),8);
            return new Vector3(MathF.Cos(angle) * (p.Z+crease),sole,p.X);
        }
        void Vertex(int ring, int side)
        { tool.SetUV(new Vector2(side / (float)sides, ring / (float)(profiles.Length - 1))); tool.AddVertex(Point(ring, side)); }
        for (int r = 0; r < profiles.Length - 1; r++) for (int c = 0; c < sides; c++)
        { Vertex(r,c); Vertex(r+1,c); Vertex(r,c+1); Vertex(r,c+1); Vertex(r+1,c); Vertex(r+1,c+1); }
        for(int side=0;side<sides;side++)
        {
            tool.SetUV(new(.5f,.5f)); tool.AddVertex(new Vector3(0,profiles[0].Y,profiles[0].X)); Vertex(0,side+1); Vertex(0,side);
            tool.SetUV(new(.5f,.5f)); tool.AddVertex(new Vector3(0,profiles[^1].Y,profiles[^1].X)); Vertex(profiles.Length-1,side); Vertex(profiles.Length-1,side+1);
        }
        tool.GenerateNormals(); tool.Index(); var mesh = tool.Commit(); _meshes[key] = mesh; return mesh;
    }
    private Mesh SoftBlock()
    {
        using var tool=new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach(var normal in new[]{Vector3.Right,Vector3.Left,Vector3.Up,Vector3.Down,Vector3.Forward,Vector3.Back})
        {
            var u=MathF.Abs(normal.Y)>.5f ? Vector3.Right : Vector3.Up;
            var v=normal.Cross(u);
            void Vertex(int x,int y)
            {
                float[] span={-.5f,-.455f,.455f,.5f};
                var p=normal*.5f+u*span[x]+v*span[y];
                var core=new Vector3(Math.Clamp(p.X,-.455f,.455f),Math.Clamp(p.Y,-.455f,.455f),Math.Clamp(p.Z,-.455f,.455f));
                var round=(p-core).Normalized();
                tool.SetNormal(round); tool.SetUV(new(x/3f,y/3f)); tool.AddVertex(core+round*.045f);
            }
            for(int y=0;y<3;y++) for(int x=0;x<3;x++)
            { Vertex(x,y); Vertex(x,y+1); Vertex(x+1,y); Vertex(x+1,y); Vertex(x,y+1); Vertex(x+1,y+1); }
        }
        tool.Index(); return tool.Commit();
    }
    private MeshInstance3D Sculpt(Node3D parent, Mesh mesh, Vector3 position, Material material, Vector3 rotation = default)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = position, Rotation = rotation, MaterialOverride = material };
        parent.AddChild(node); return node;
    }
    private void Timber(Node3D parent, Vector3 start, Vector3 end, float thickness, int material = 0)
    {
        var direction = end - start;
        var beam = Part(parent, "cylinder", (start + end) / 2, new Vector3(thickness, direction.Length(), thickness), material);
        beam.Quaternion = new Quaternion(Vector3.Up, direction.Normalized());
    }
    private void FineBeam(Node3D parent, Vector3 start, Vector3 end, float thickness, string color)
    {
        var direction = end - start;
        var beam = Detail(parent, "cylinder", (start + end) / 2, new Vector3(thickness, direction.Length(), thickness), color);
        beam.Quaternion = new Quaternion(Vector3.Up, direction.Normalized());
    }
}
