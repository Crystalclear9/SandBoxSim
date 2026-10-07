using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh RoofCourse(bool thatch,bool detailed)
    {
        string key=(thatch?"thatch-course":"tile-course")+(detailed?"":"-far");if(_meshes.TryGetValue(key,out var cached))return cached;
        int columns=detailed?4:1,rows=detailed?4:2;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Point(int x,int z,bool top)
        {
            float u=x/(float)columns-.5f,v=z/(float)rows-.5f;
            float edge=MathF.Pow(MathF.Abs(u)*2,8);
            float fray=thatch?MathF.Sin(v*33)*edge*.028f:MathF.Sin(v*14)*edge*.007f;
            float crown=thatch?.42f+.065f*MathF.Sin(v*44+u*.7f):.34f+.17f*MathF.Cos(v*MathF.PI);
            return new(u+fray,top?crown:-.4f,v);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
        {
            if((b-a).Cross(c-a).Dot(outward)>0)(b,c)=(c,b);
            foreach(var p in new[]{a,b,c}){surface.SetUV(new(p.X+.5f,p.Z+.5f));surface.AddVertex(p);}
        }
        foreach(bool top in new[]{false,true})for(int x=0;x<columns;x++)for(int z=0;z<rows;z++)
        {
            var a=Point(x,z,top);var b=Point(x+1,z,top);var c=Point(x,z+1,top);var d=Point(x+1,z+1,top);
            var outward=top?Vector3.Up:Vector3.Down;Triangle(a,b,c,outward);Triangle(b,d,c,outward);
        }
        foreach(int x in new[]{0,columns})for(int z=0;z<rows;z++)
        {
            var a=Point(x,z,false);var b=Point(x,z+1,false);var c=Point(x,z,true);var d=Point(x,z+1,true);
            var outward=x==0?Vector3.Left:Vector3.Right;Triangle(a,b,c,outward);Triangle(b,d,c,outward);
        }
        foreach(int z in new[]{0,rows})for(int x=0;x<columns;x++)
        {
            var a=Point(x,z,false);var b=Point(x+1,z,false);var c=Point(x,z,true);var d=Point(x+1,z,true);
            var outward=z==0?Vector3.Forward:Vector3.Back;Triangle(a,b,c,outward);Triangle(b,d,c,outward);
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
}
