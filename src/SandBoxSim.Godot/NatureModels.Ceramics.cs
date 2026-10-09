using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // A closed radial profile includes the lip and inner wall; the mouth remains open.
    private Mesh VesselMesh(string key,Vector2[] profile,int sides=32)
    {
        if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int ring,int side){float angle=side*MathF.Tau/sides;return new(profile[ring].X*MathF.Sin(angle),profile[ring].Y,profile[ring].X*MathF.Cos(angle));}
        void Vertex(int ring,int side){surface.SetUV(new(side/(float)sides,ring/(float)(profile.Length-1)));surface.AddVertex(Point(ring,side));}
        for(int ring=0;ring<profile.Length-1;ring++)for(int side=0;side<sides;side++)
        {Vertex(ring,side);Vertex(ring,side+1);Vertex(ring+1,side);Vertex(ring,side+1);Vertex(ring+1,side+1);Vertex(ring+1,side);}
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private void Pottery(Node3D parent,Vector3 position)
    {
        var profile=new[]{new Vector2(0,.008f),new(.032f,.008f),new(.048f,.027f),new(.060f,.065f),new(.052f,.105f),new(.033f,.130f),new(.035f,.144f),new(.027f,.144f),new(.025f,.129f),new(.043f,.101f),new(.048f,.063f),new(.035f,.029f),new(0,.020f)};
        Sculpt(parent,VesselMesh("hollow-pottery",profile),position,Material(8));
    }
    private void StavedBarrel(Node3D parent,Vector3 position)
    {
        var profile=new[]{new Vector2(0,0),new(.108f,0),new(.122f,.035f),new(.139f,.17f),new(.139f,.23f),new(.122f,.365f),new(.108f,.4f),new(0,.4f)};
        Sculpt(parent,VesselMesh("coopered-barrel",profile,40),position,Material(0));
        foreach(float height in new[]{.07f,.33f})
        {
            var hoop=new[]{new Vector2(.125f,height),new(.133f,height),new(.134f,height+.022f),new(.125f,height+.022f),new(.125f,height)};
            Sculpt(parent,VesselMesh("barrel-hoop:"+height,hoop,40),position,Material(11));
        }
        for(int i=0;i<14;i++)
        {
            float angle=i*MathF.Tau/14;
            Vector3 At(float radius,float y)=>position+new Vector3(MathF.Sin(angle)*radius,y,MathF.Cos(angle)*radius);
            FineBeam(parent,At(.123f,.038f),At(.140f,.17f),.0025f,"#514435");
            FineBeam(parent,At(.140f,.17f),At(.140f,.23f),.0025f,"#514435");
            FineBeam(parent,At(.140f,.23f),At(.123f,.363f),.0025f,"#514435");
        }
        Part(parent,"box",position+new Vector3(0,.403f,0),new(.16f,.007f,.09f),1);
    }
    private void ValidateCeramics()
    {
        var root=new Node3D();
        try
        {
            Pottery(root,Vector3.Zero);Pottery(root,Vector3.Zero);
            var mesh=root.GetChild<MeshInstance3D>(0).Mesh;
            if(mesh!=root.GetChild<MeshInstance3D>(1).Mesh)throw new InvalidOperationException("Pottery mesh not shared");
            var arrays=mesh.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            bool inner=false,outer=false;
            for(int i=0;i<points.Length;i++)
            {
                if(!points[i].IsFinite()||!normals[i].IsFinite())throw new InvalidOperationException("Nonfinite pottery geometry");
                if(points[i].Y>.12f&&points[i].Y<.14f)
                {float facing=points[i].X*normals[i].X+points[i].Z*normals[i].Z;inner|=facing<-.005f;outer|=facing>.005f;}
            }
            if(!inner||!outer)throw new InvalidOperationException("Pottery lip lost inner or outer wall");
        }
        finally{root.Free();}
    }
}
