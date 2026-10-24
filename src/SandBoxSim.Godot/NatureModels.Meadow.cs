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
            // Open panicles branch from the stem; solid clubs read as cattails.
            var crown=Center(1);
            for(int branch=0;branch<15;branch++)
            {
                float t=(branch+.5f)/15,azimuth=branch*2.399963f+angle;
                var direction=new Vector3(MathF.Cos(azimuth),0,MathF.Sin(azimuth));
                var start=crown+Vector3.Up*(t*.16f);
                float spread=.065f*MathF.Sin(t*MathF.PI)*(.85f+(branch%3)*.12f);
                var tip=start+direction*spread+Vector3.Up*.045f;
                BotanicalStem(tool,start,tip,.0007f);
                for(int floret=0;floret<4;floret++)
                {
                    var rootlet=start.Lerp(tip,(floret+1)/4f);
                    var up=(direction*.35f+Vector3.Up).Normalized();
                    var across=new Vector3(-direction.Z,0,direction.X);
                    BotanicalLeaf(tool,rootlet,up,across,.018f,.0035f,.0015f,1.1f,3,new Color(2.0f,1.0f,.85f));
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
