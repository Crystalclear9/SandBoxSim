using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // A cut follows forehead, temples and nape independently. Large swept locks
    // alter the silhouette; fine strands remain shading, not thousands of nodes.
    private Mesh GroomedHair(int sides,int rings)
    {
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        int style=_faceVariant%3;float direction=_faceVariant%2==0?1:-1;
        Vector3 Point(int row,int column)
        {
            float angle=column*MathF.Tau/sides,front=MathF.Max(0,-MathF.Sin(angle));
            float side=MathF.Abs(MathF.Cos(angle)),back=MathF.Max(0,MathF.Sin(angle));
            float t=row/(float)rings;
            float hairline=.019f+(.039f+(style==1?.012f:style==2?-.004f:0))*MathF.Pow(front,2)-.045f*back;
            // Temple recession and short sideburns break the old bowl-cut edge.
            hairline+=.015f*MathF.Exp(-MathF.Pow((side-.78f)/.17f,2))*front;
            hairline-=.012f*MathF.Pow(side,18);
            hairline+=.0008f*MathF.Sin(angle*23+.6f)+.0005f*MathF.Sin(angle*41);
            float top=HumanData().High;
            float y=Mathf.Lerp(top+.005f,hairline,1-MathF.Cos(t*MathF.PI*.5f));
            var wrapped=WrapHumanHair(new(0,(y-.008f)/.224f,0),angle);
            var world=new Vector3(wrapped.X*.181f,wrapped.Y*.224f+.008f,wrapped.Z*.200f);
            float crown=MathF.Sin(t*MathF.PI);
            float swept=angle+direction*(1-t)*(style==1?1.4f:.75f);
            float partAngle=-MathF.PI*.5f+direction*.38f;
            float part=MathF.Atan2(MathF.Sin(angle-partAngle),MathF.Cos(angle-partAngle));
            float groove=MathF.Exp(-MathF.Pow(part/.065f,2))*crown;
            float locks=MathF.Pow(.5f+.5f*MathF.Sin(swept*(style==2?11:8)+t*3),2);
            float volume=(style==2?.0020f:.0050f)*crown*(.35f+.65f*front);
            float radial=volume+locks*.0018f*crown-groove*.0018f;
            world+=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle))*radial;
            float forelock=MathF.Exp(-MathF.Pow((angle+MathF.PI*.5f-direction*.30f)/.65f,2))*MathF.Sin(t*MathF.PI);
            world.Y+=(style==2?.003f:.010f)*crown*front+locks*.0013f*crown+(style==1?.012f:.005f)*forelock;
            world.X+=direction*forelock*(style==2?.001f:.004f);
            return new(world.X/.181f,(world.Y-.008f)/.224f,world.Z/.200f);
        }
        void Vertex(int row,int column)
        {
            surface.SetUV(new(column/(float)sides,row/(float)rings));
            surface.AddVertex(Point(row,column));
        }
        for(int row=0;row<rings;row++)for(int column=0;column<sides;column++)
        {
            Vertex(row,column);Vertex(row+1,column);Vertex(row,column+1);
            Vertex(row,column+1);Vertex(row+1,column);Vertex(row+1,column+1);
        }
        for(int column=0;column<sides;column++)
        {
            var center=WrapHumanHair(new(0,(HumanData().High+.005f-.008f)/.224f,0),0,true);
            surface.SetUV(new(.5f,0));surface.AddVertex(center);
            Vertex(0,column);Vertex(0,column+1);
        }
        surface.GenerateNormals();surface.Index();return surface.Commit();
    }
}
