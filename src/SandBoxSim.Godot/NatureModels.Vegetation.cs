using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Mesh Crown(bool pine)
    {
        string key=pine ? "pine-sprays" : "leaf-crown";
        if(_meshes.TryGetValue(key,out var cached)) return cached;
        using var tool=new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=(c-a).Cross(b-a).Normalized();
            foreach(var p in new[]{a,b,c}) { tool.SetNormal(normal); tool.SetUV(new(p.X+.5f,p.Z+.5f)); tool.AddVertex(p); }
            foreach(var p in new[]{c,b,a}) { tool.SetNormal(-normal); tool.SetUV(new(p.X+.5f,p.Z+.5f)); tool.AddVertex(p); }
        }
        void Spray(Vector3 center,Vector3 along,Vector3 across,float fold)
        {
            var ridge=center+Vector3.Up*fold;
            for(int edge=0;edge<6;edge++)
            {
                float a=edge*MathF.Tau/6,b=(edge+1)*MathF.Tau/6;
                Triangle(center+along*MathF.Cos(a)+across*MathF.Sin(a),ridge,center+along*MathF.Cos(b)+across*MathF.Sin(b));
            }
        }
        if(pine)
        {
            for(int layer=0;layer<9;layer++) for(int branch=0;branch<11;branch++)
            {
                float t=layer/9f,a=branch*MathF.Tau/11+layer*.37f,r=.48f*MathF.Pow(1-t,.8f);
                var forward=new Vector3(MathF.Cos(a),0,MathF.Sin(a)); var side=new Vector3(-forward.Z,0,forward.X);
                var start=new Vector3(0,t-.44f,0); var tip=start+forward*r+Vector3.Down*.08f;
                for(int twig=0;twig<3;twig++)
                {
                    float progress=(twig+.6f)/3;
                    var center=start+forward*r*progress+Vector3.Down*.065f*progress;
                    Spray(center,forward*r*.30f,side*r*(.22f-.10f*progress),.014f);
                }
            }
        }
        else
        {
            // Solid small leaf sprays overlap into an irregular crown; silhouette contains no sphere hull.
            for(int i=0;i<110;i++)
            {
                float a=i*2.399963f, y=1-2*(i+.5f)/110, r=MathF.Sqrt(1-y*y);
                var center=new Vector3(MathF.Cos(a)*r,y*.85f,MathF.Sin(a)*r)*(.39f+.035f*MathF.Sin(i*1.7f));
                var along=new Vector3(MathF.Cos(a+.7f),.22f,MathF.Sin(a+.7f)).Normalized()*.13f;
                var across=along.Cross(Vector3.Up).Normalized()*.075f;
                Spray(center,along,across,.026f);
            }
        }
        tool.Index(); var mesh=tool.Commit(); _meshes[key]=mesh; return mesh;
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
        var model=new Node3D(); Part(model,"trunk",new(0,1.7f,0),new(.34f,3.4f,.34f),0);
        if(pine) Part(model,"pine",new(0,3.2f,0),new(3.2f,4.4f,3.2f),5);
        else for(int i=0;i<5;i++)
        {
            float a=i*MathF.Tau/5; var end=new Vector3(MathF.Cos(a),3.1f+(i%2)*.5f,MathF.Sin(a));
            Timber(model,new(0,2.0f,0),end,.095f);
            Part(model,"foliage",end,new(2.1f,1.9f,2.1f),4);
        }
        MergeResidentParts(model,pine ? "tree-pine" : "tree-broadleaf"); return model;
    }
}
