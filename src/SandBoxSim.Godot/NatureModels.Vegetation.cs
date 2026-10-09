using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Mesh Crown(bool pine)
    {
        string key=pine ? "pine-sprays" : "leaf-crown";
        if(_meshes.TryGetValue(key,out var cached)) return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        if(pine)
        {
            for(int stem=0;stem<4;stem++)BotanicalStem(tool,new(0,-.46f+stem*.25f,0),new(0,-.46f+(stem+1)*.25f,0),.025f*MathF.Pow(.5f,stem));
            for(int layer=0;layer<10;layer++)for(int branch=0;branch<11;branch++)
            {
                float t=layer/10f,a=branch*MathF.Tau/11+layer*.61f,r=.49f*MathF.Pow(1-t,.78f);
                var forward=new Vector3(MathF.Cos(a),-.12f,MathF.Sin(a)).Normalized();
                var across=new Vector3(-forward.Z,0,forward.X).Normalized();
                var root=new Vector3(0,t-.46f,0);
                BotanicalStem(tool,root,root+forward*r,.008f*(1-t)+.002f);
                for(int twig=0;twig<4;twig++)for(int side=-1;side<=1;side+=2)
                {
                    float u=(twig+.45f)/4;
                    var stem=root+forward*r*u;
                    var axis=(forward*.5f+across*side*.8f+Vector3.Up*.15f).Normalized();
                    BotanicalStem(tool,stem,stem+axis*r*.10f,.002f);
                    BotanicalLeaf(tool,stem,axis,across,r*(.32f-.09f*u),r*.069f,.003f,.80f+.12f*MathF.Sin(branch*3+twig));
                }
                BotanicalLeaf(tool,root+forward*r*.82f,forward,across,r*.24f,r*.04f,.007f,.90f);
            }
        }
        else
        {
            // Small curved leaves on distinct sprigs replace broad hexagonal plates.
            for(int i=0;i<144;i++)
            {
                float a=i*2.399963f,y=1-2*(i+.5f)/144,r=MathF.Sqrt(1-y*y);
                var center=new Vector3(MathF.Cos(a)*r,y*.85f,MathF.Sin(a)*r)*(.37f+.045f*MathF.Sin(i*1.7f));
                var forward=new Vector3(MathF.Cos(a+.7f),MathF.Sin(i*.83f)*.45f,MathF.Sin(a+.7f)).Normalized();
                var across=forward.Cross(Vector3.Up).Normalized();
                BotanicalStem(tool,Vector3.Zero,center,.0035f);
                BotanicalStem(tool,center,center+forward*.065f,.002f);
                for(int leaf=0;leaf<4;leaf++)
                {
                    float sign=leaf%2==0?-1:1;
                    var axis=(forward*.65f+across*sign*.75f+Vector3.Up*.20f).Normalized();
                    var root=center+forward*(leaf/2)*.035f;
                    BotanicalLeaf(tool,root,axis,axis.Cross(Vector3.Up).Normalized(),.082f+(i%5)*.004f,.027f,.004f,.78f+(i%7)*.055f);
                }
                BotanicalLeaf(tool,center+forward*.065f,forward,across,.086f,.028f,.004f,1.02f);
            }
        }
        tool.GenerateNormals();tool.Index();var mesh=WithScreenLods(tool.Commit());_meshes[key]=mesh;return mesh;
    }
    private Mesh BentTrunk()
    {
        var raw=Loft("trunk-profile",new[]{new Vector4(-.5f,0,.20f,.19f),new(-.25f,.018f,.25f,.24f),new(0,.027f,.32f,.30f),new(.3f,-.018f,.38f,.36f),new(.5f,0,.5f,.47f)},20);
        var arrays=raw.SurfaceGetArrays(0); var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(); var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for(int i=0;i<points.Length;i++) { var p=points[i]; points[i]=new(p.X,-p.Z,p.Y); var n=normals[i]; normals[i]=new(n.X,-n.Z,n.Y); }
        arrays[(int)Mesh.ArrayType.Vertex]=points; arrays[(int)Mesh.ArrayType.Normal]=normals;
        var mesh=new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays); return mesh;
    }
    public Node3D Tree(bool pine)
    {
        var model=new Node3D();model.AddChild(new MeshInstance3D {Mesh=Shape(pine?"conifer-tree":"broad-tree")});return model;
    }
}
