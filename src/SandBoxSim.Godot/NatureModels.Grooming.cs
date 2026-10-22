using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // A cut follows forehead, temples and nape independently. Large swept locks
    // alter the silhouette; fine strands remain shading, not thousands of nodes.
    private Mesh GroomedHair(int sides,int rings)
    {
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetColor(Colors.White);
        int style=_faceVariant%3;float direction=_faceVariant%2==0?1:-1;
        Vector3 Point(float row,float column)
        {
            float angle=column*MathF.Tau/sides,front=MathF.Max(0,-MathF.Sin(angle));
            float side=MathF.Abs(MathF.Cos(angle)),back=MathF.Max(0,MathF.Sin(angle));
            float t=row/(float)rings;
            float hairline=.019f+(.039f+(style==1?.012f:style==2?-.004f:0))*MathF.Pow(front,2)-.045f*back;
            // Unequal temple/fringe heights make the part read in silhouette,
            // while retaining the source scalp and the fitted hat clearance.
            hairline+=direction*MathF.Cos(angle)*front*(style==1?.007f:.003f);
            // Temple recession and short sideburns break the old bowl-cut edge.
            hairline+=.015f*MathF.Exp(-MathF.Pow((side-.78f)/.17f,2))*front;
            hairline+=.012f*MathF.Pow(side,10);
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
            float volume=(style==2?.0010f:.0035f)*crown*(.35f+.65f*front);
            float radial=volume+locks*.0011f*crown-groove*.0024f;
            world+=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle))*radial;
            float forelock=MathF.Exp(-MathF.Pow((angle+MathF.PI*.5f-direction*.30f)/.65f,2))*MathF.Sin(t*MathF.PI);
            world.Y+=(style==2?.001f:.004f)*crown*front+locks*.0013f*crown+(style==1?.015f:.007f)*forelock;
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
        // Overlapping tapered locks follow the fitted scalp. Their curved
        // cross section and lifted tips create actual silhouette and self-shadow,
        // rather than drawing strand noise on one opaque helmet-shaped shell.
        int count=sides>=64?180:0;
        for(int strand=0;strand<count;strand++)
        {
            float seed=strand*.61803398875f;seed-=MathF.Floor(seed);
            float column=seed*sides;
            float start=.06f+(strand%7)*.085f;
            float length=(style==2?.22f:.38f)+(strand%5)*.025f;
            float end=MathF.Min(1.025f,start+length);
            float width=(.0018f+(strand%4)*.0004f)/.19f;
            Vector3 LockPoint(int row,int cross)
            {
                float t=row/8f,r=start+(end-start)*t;
                float c=column+direction*MathF.Sin(t*MathF.PI*.65f)*(style==2?1.5f:5.5f);
                float angle=c*MathF.Tau/sides;
                var p=Point(r*rings,c);
                var radial=new Vector3(MathF.Cos(angle),.25f,MathF.Sin(angle)).Normalized();
                var tangent=new Vector3(-MathF.Sin(angle),0,MathF.Cos(angle));
                float across=cross/3f*2-1;
                float taper=MathF.Pow(MathF.Max(0,MathF.Sin(t*MathF.PI)),.55f);
                float lift=(.0006f+MathF.Sin(t*MathF.PI)*.0012f)/.20f;
                return p+tangent*(across*width*taper)+radial*(lift*(1-across*across)*taper);
            }
            for(int row=0;row<8;row++)for(int cross=0;cross<3;cross++)
            {
                var a=LockPoint(row,cross);var b=LockPoint(row+1,cross);
                var c=LockPoint(row,cross+1);var d=LockPoint(row+1,cross+1);
                float angle=column*MathF.Tau/sides;
                var outward=new Vector3(MathF.Cos(angle),.4f,MathF.Sin(angle));
                surface.SetColor(new Color(.90f+(strand%7)*.025f,.90f+(strand%7)*.025f,.90f+(strand%7)*.025f));
                EquipmentTriangle(surface,a,b,c,outward);EquipmentTriangle(surface,c,b,d,outward);
            }
        }
        surface.GenerateNormals();surface.Index();return surface.Commit();
    }
}
