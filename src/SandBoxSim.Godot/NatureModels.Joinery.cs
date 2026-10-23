using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Mesh RidgeTile()
    {
        const string key="hollow-ridge-tile";if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 P(int ring,int end,bool outer)
        {
            float a=ring*MathF.PI/12,r=outer?.085f:.073f;
            return new(MathF.Cos(a)*r,MathF.Sin(a)*r,end==0?-.11f:.11f);
        }
        void Tri(Vector3 a,Vector3 b,Vector3 c,Vector3 normal)=>EquipmentTriangle(tool,a,b,c,normal);
        for(int i=0;i<12;i++)
        {
            var normal=new Vector3(MathF.Cos((i+.5f)*MathF.PI/12),MathF.Sin((i+.5f)*MathF.PI/12),0);
            foreach(bool outer in new[]{false,true})
            {
                var a=P(i,0,outer);var b=P(i+1,0,outer);var c=P(i,1,outer);var d=P(i+1,1,outer);
                Tri(a,b,c,outer?normal:-normal);Tri(b,d,c,outer?normal:-normal);
            }
            foreach(int end in new[]{0,1})
            {
                var a=P(i,end,false);var b=P(i+1,end,false);var c=P(i,end,true);var d=P(i+1,end,true);
                Tri(a,b,c,new(0,0,end==0?-1:1));Tri(b,d,c,new(0,0,end==0?-1:1));
            }
        }
        foreach(int edge in new[]{0,12})
        {Tri(P(edge,0,false),P(edge,0,true),P(edge,1,false),Vector3.Down);Tri(P(edge,0,true),P(edge,1,true),P(edge,1,false),Vector3.Down);}
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
    private void AddEaveJoinery(Node3D model,float width,float depth,float height,int variant)
    {
        foreach(float side in new[]{-1f,1f})
        {
            // Individual rafter tails, mortise pegs and brackets remain in the static batch.
            for(int bay=0;bay<6;bay++)
            {
                float z=-depth/2+(bay+.5f)*depth/6;
                Part(model,"box",new(side*(width/2+.045f),height+.095f,z),new(.31f,.065f,.075f),0,new(0,0,-side*.46f));
                Part(model,"cylinder",new(side*(width/2+.082f),height+.040f,z),new(.016f,.022f,.016f),1,new(0,0,MathF.PI/2));
            }
            if(variant is 1 or 3)
                foreach(float end in new[]{-1f,1f})
                    Timber(model,new(side*(width/2+.045f),height-.18f,end*(depth/2-.11f)),new(side*(width/2+.20f),height+.08f,end*(depth/2-.11f)),.032f);
        }
        if(variant is 1 or 3)
        {
            // The chimney has a ceramic pot with a genuinely open mouth.
            var position=new Vector3(-.5f,height+1.31f,-.4f);
            Sculpt(model,ChimneyPot(),position,Material(2));
            Part(model,"cylinder",position+new Vector3(0,.025f,0),new(.105f,.008f,.105f),10);
        }
    }
    private Mesh ChimneyPot()
    {
        const string key="chimney-pot";if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        const int sides=24;
        // Outer and inner skins meet at the rolled top lip; the bore stays hollow.
        var profile=new[]{new Vector2(.083f,0),new(.072f,.025f),new(.065f,.18f),new(.080f,.19f),new(.080f,.215f),new(.052f,.215f),new(.052f,.025f)};
        Vector3 P(int r,int c){var p=profile[r];float a=c*MathF.Tau/sides;return new(MathF.Cos(a)*p.X,p.Y,MathF.Sin(a)*p.X);}
        for(int r=0;r<profile.Length-1;r++)for(int c=0;c<sides;c++)
        {
            var a=P(r,c);var b=P(r+1,c);var d=P(r,c+1);var e=P(r+1,c+1);
            var n=new Vector3(a.X,0,a.Z)*(r>=5?-1:1);if(r==4)n=Vector3.Up;
            EquipmentTriangle(tool,a,b,d,n);EquipmentTriangle(tool,b,e,d,n);
        }
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
    }
}
