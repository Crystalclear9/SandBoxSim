using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // Seven curved blades share one mesh across spatially culled instances.
    private static ArrayMesh MeadowTuft()
    {
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int blade=0;blade<7;blade++)
        {
            float a=blade*2.399963f,h=.24f+(blade%3)*.055f;
            var side=new Vector3(MathF.Cos(a),0,MathF.Sin(a));
            var lean=new Vector3(-side.Z,0,side.X)*(.09f+(blade%2)*.025f);
            var root=side*.055f;
            Vector3 Point(float t,float edge)=>root+Vector3.Up*h*t+lean*t*t+side*edge*.025f*(1-t);
            void Vertex(float t,float edge){tool.SetUV(new((edge+1)*.5f,t));tool.AddVertex(Point(t,edge));}
            for(int row=0;row<3;row++)
            {
                float t=row/3f,u=(row+1)/3f;
                Vertex(t,-1);Vertex(u,-1);Vertex(t,1);
                Vertex(t,1);Vertex(u,-1);Vertex(u,1);
            }
        }
        tool.GenerateNormals();tool.Index();return tool.Commit();
    }
}
