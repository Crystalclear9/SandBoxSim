using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Mesh ReedClump()
    {
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int stem=0;stem<7;stem++)
        {
            float angle=stem*2.399963f,height=.64f+(stem%4)*.095f;
            var radial=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
            var root=radial*(.025f+(stem%3)*.035f);
            Vector3 Center(float t)=>root+Vector3.Up*height*t+radial*.075f*t*t;
            for(int joint=0;joint<5;joint++)BotanicalStem(tool,Center(joint/5f),Center((joint+1)/5f),.0038f);
            for(int leaf=0;leaf<4;leaf++)
            {
                float t=.18f+leaf*.16f,sign=leaf%2==0?-1:1;
                var axis=(radial*sign+Vector3.Up*.24f).Normalized();
                BotanicalLeaf(tool,Center(t),axis,axis.Cross(Vector3.Up).Normalized(),.25f-leaf*.025f,.014f,.018f,.72f+leaf*.075f);
            }
            // Seed heads are tapered volumes, readable from every camera angle.
            for(int ring=0;ring<5;ring++)
            {
                for(int edge=0;edge<8;edge++)
                {
                    Vector3 P(int row,int col)
                    {float v=row/4f,rad=.012f*MathF.Pow(MathF.Max(0,MathF.Sin(v*MathF.PI)),.5f)+.001f,a=col*MathF.Tau/8;return Center(1)+Vector3.Up*(.12f*v)+new Vector3(MathF.Cos(a),0,MathF.Sin(a))*rad;}
                    if(ring==4)continue;
                    tool.SetColor(new(1.30f,.74f,.42f));
                    var a=P(ring,edge);var b=P(ring+1,edge);var c=P(ring,edge+1);var d=P(ring+1,edge+1);
                    var n=new Vector3(a.X-Center(1).X,0,a.Z-Center(1).Z);
                    EquipmentTriangle(tool,a,b,c,n);EquipmentTriangle(tool,c,b,d,n);
                }
            }
        }
        tool.GenerateNormals();tool.Index();return WithScreenLods(tool.Commit());
    }
    // Eleven tapered blades share one mesh across spatially culled instances.
    private static ArrayMesh MeadowTuft()
    {
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int blade=0;blade<11;blade++)
        {
            float a=blade*2.399963f,h=.19f+(blade%5)*.041f;
            var side=new Vector3(MathF.Cos(a),0,MathF.Sin(a));
            var lean=new Vector3(-side.Z,0,side.X)*(.09f+(blade%2)*.025f);
            var root=side*(.035f+(blade%3)*.022f);
            Vector3 Point(float t,float edge)=>root+Vector3.Up*h*t+lean*t*t+side*edge*.013f*MathF.Pow(1-t,.72f);
            void Vertex(float t,float edge){tool.SetUV(new((edge+1)*.5f,t));tool.AddVertex(Point(t,edge));}
            for(int row=0;row<5;row++)
            {
                float t=row/5f,u=(row+1)/5f;
                Vertex(t,-1);Vertex(u,-1);Vertex(t,1);
                Vertex(t,1);Vertex(u,-1);Vertex(u,1);
            }
        }
        tool.GenerateNormals();tool.Index();return tool.Commit();
    }
}
