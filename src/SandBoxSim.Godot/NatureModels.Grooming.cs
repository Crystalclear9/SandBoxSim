using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // A cut follows forehead, temples and nape independently. Large swept locks
    // alter the silhouette; fine strands remain shading, not thousands of nodes.
    private Mesh GroomedHair(int sides,int rings)
    {
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetColor(new Color(.78f,.78f,.78f,0));
        int style=_faceVariant%3;float direction=_faceVariant%2==0?1:-1;
        Vector3 Point(float row,float column)
        {
            float angle=column*MathF.Tau/sides,front=MathF.Max(0,-MathF.Sin(angle));
            float side=MathF.Abs(MathF.Cos(angle)),back=MathF.Max(0,MathF.Sin(angle));
            float t=row/(float)rings;
            float hairline=.023f+(.043f+(style==1?.012f:style==2?.002f:0))*MathF.Pow(front,2)-.045f*back;
            // Unequal temple/fringe heights make the part read in silhouette,
            // while retaining the source scalp and the fitted hat clearance.
            hairline+=direction*MathF.Cos(angle)*front*(style==1?.007f:.003f);
            // Temple recession and short sideburns break the old bowl-cut edge.
            hairline+=.015f*MathF.Exp(-MathF.Pow((side-.78f)/.17f,2))*front;
            hairline+=.012f*MathF.Pow(side,10);
            hairline-=.020f*MathF.Pow(side,30)+.006f*MathF.Pow(front,4)*(.5f+.5f*direction*MathF.Cos(angle));
            hairline+=.0008f*MathF.Sin(angle*23+.6f)+.0005f*MathF.Sin(angle*41);
            float top=HumanData().High;
            float y=Mathf.Lerp(top+.005f,hairline,1-MathF.Cos(t*MathF.PI*.5f));
            var wrapped=WrapHumanHair(new(0,(y-.008f)/.224f,0),angle);
            var world=new Vector3(wrapped.X*.181f,wrapped.Y*.224f+.008f,wrapped.Z*.200f);
            float crown=MathF.Sin(t*MathF.PI);
            float swept=angle+direction*(1-t)*(style==1?1.4f:.75f);
            float partAngle=-MathF.PI*.5f+direction*.38f;
            float part=MathF.Atan2(MathF.Sin(angle-partAngle),MathF.Cos(angle-partAngle));
            float groove=MathF.Exp(-MathF.Pow(part/.10f,2))*crown;
            float locks=MathF.Pow(.5f+.5f*MathF.Sin(swept*(style==2?11:8)+t*3),2);
            float volume=(style==2?.0010f:style==1?.007f:.0045f)*crown*(.35f+.65f*front);
            float radial=volume+locks*.0004f*crown-groove*.0014f;
            world+=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle))*radial;
            float forelock=MathF.Exp(-MathF.Pow((angle+MathF.PI*.5f-direction*.30f)/.65f,2))*MathF.Sin(t*MathF.PI);
            world.Y+=(style==2?.001f:.004f)*crown*front+locks*.0013f*crown+(style==1?.022f:.012f)*forelock;
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
        // Fine curved fibers cover a quiet fitted undercoat. Millimetre-wide
        // raised locks read as carved clay in a portrait; these strands are
        // 0.2–0.4 mm wide and follow the scalp through the part and hairline.
        int count=sides>=64?2400:0;
        uint state=72913u+(uint)_faceVariant*5197u;
        float Random(){state^=state<<13;state^=state>>17;state^=state<<5;return (state&0xffffff)/16777216f;}
        for(int strand=0;strand<count;strand++)
        {
            float column=Random()*sides;
            float start=.08f+MathF.Sqrt(Random())*.90f;
            float length=(style==2?.10f:.16f)+Random()*.14f;
            float end=MathF.Min(1.014f,start+length);
            float width=(.00019f+Random()*.00017f)/.19f;
            float lift=(.0004f+Random()*.0007f)/.20f;
            float value=.68f+Random()*.40f;
            surface.SetColor(new Color(value,value,value,1));
            Vector3 LockPoint(int row,int cross)
            {
                float t=row/3f,r=start+(end-start)*t;
                float c=column+direction*MathF.Sin(t*MathF.PI*.65f)*(style==2?.7f:2.4f);
                float angle=c*MathF.Tau/sides;
                var p=Point(r*rings,c);
                var radial=new Vector3(MathF.Cos(angle),.25f,MathF.Sin(angle)).Normalized();
                var tangent=new Vector3(-MathF.Sin(angle),0,MathF.Cos(angle));
                float across=cross*2-1;
                float taper=.90f*(1-t)+.04f;
                return p+tangent*(across*width*taper)+radial*(lift*(.25f+MathF.Sin(t*MathF.PI)));
            }
            for(int row=0;row<3;row++)
            {
                var a=LockPoint(row,0);var b=LockPoint(row+1,0);
                var c=LockPoint(row,1);var d=LockPoint(row+1,1);
                float angle=column*MathF.Tau/sides;
                var outward=new Vector3(MathF.Cos(angle),.4f,MathF.Sin(angle));
                surface.SetUV(new(0,row/3f));
                EquipmentTriangle(surface,a,b,c,outward);EquipmentTriangle(surface,c,b,d,outward);
            }
        }
        if(HumanFamily==2&&style!=2)
        {
            // A tied nape style adds a distinct practical silhouette. It is
            // part of the head mesh and follows the same head joint and LOD.
            int rows=sides>=64?16:8,columns=sides>=64?32:16;
            Vector3 Bun(int row,int column)
            {
                float u=row*MathF.PI/rows,a=column*MathF.Tau/columns;
                float radius=MathF.Sin(u),groove=1+.018f*MathF.Sin(a*19+u*7);
                var p=new Vector3(.030f*radius*MathF.Cos(a)*groove,-.028f+.032f*MathF.Cos(u),.071f+.028f*radius*MathF.Sin(a)*groove);
                return new(p.X/.181f,(p.Y-.008f)/.224f,p.Z/.200f);
            }
            surface.SetColor(new Color(.85f,.85f,.85f,.8f));
            for(int row=0;row<rows;row++)for(int column=0;column<columns;column++)
            {
                var a=Bun(row,column);var b=Bun(row+1,column);var c=Bun(row,column+1);var d=Bun(row+1,column+1);
                var center=new Vector3(0,(-.028f-.008f)/.224f,.071f/.200f);
                var outward=(a+b+c+d)*.25f-center;
                EquipmentTriangle(surface,a,b,c,outward);EquipmentTriangle(surface,c,b,d,outward);
            }
        }
        surface.GenerateNormals();surface.Index();return surface.Commit();
    }
}
