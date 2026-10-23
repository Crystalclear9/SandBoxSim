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
            // Staggered narrow masonry courses give the stack a readable scale.
            for(int row=0;row<6;row++)foreach(float side in new[]{-1f,1f})
            {
                float y=height+.29f+row*.16f;
                Part(model,"masonry",new(-.5f+side*.084f,y,-.588f),new(.154f,.145f,.027f),6);
                Part(model,"masonry",new(-.676f,y,-.4f+side*(row%2==0?.093f:.082f)),new(.027f,.145f,.173f),6);
            }
        }
        if(variant is 1 or 2 or 3)
        {
            foreach(float side in new[]{-1f,1f})
            {
                // Open carved wooden troughs, seated under the eaves, with
                // iron supports. Both skins are modelled, not an opaque box.
                Sculpt(model,EaveTrough(depth+.50f),new(side*(width/2+.23f),height+.10f,0),Material(0));
                for(int bracket=0;bracket<4;bracket++)
                {
                    float z=-depth/2+(bracket+.5f)*depth/4;
                    Timber(model,new(side*(width/2+.04f),height+.09f,z),new(side*(width/2+.23f),height+.03f,z),.018f,11);
                }
            }
        }
    }
    private Mesh EaveTrough(float length)
    {
        string key="eave-trough:"+length;if(_meshes.TryGetValue(key,out var cached))return cached;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 P(int arc,int end,bool outer)
        {float a=arc*MathF.PI/16,r=outer?.073f:.056f;return new(MathF.Cos(a)*r,-MathF.Sin(a)*r,end==0?-length/2:length/2);}
        for(int arc=0;arc<16;arc++)
        {
            var normal=new Vector3(MathF.Cos((arc+.5f)*MathF.PI/16),-MathF.Sin((arc+.5f)*MathF.PI/16),0);
            foreach(bool outer in new[]{true,false})
            {var a=P(arc,0,outer);var b=P(arc+1,0,outer);var c=P(arc,1,outer);var d=P(arc+1,1,outer);EquipmentTriangle(tool,a,b,c,outer?normal:-normal);EquipmentTriangle(tool,b,d,c,outer?normal:-normal);}
            foreach(int end in new[]{0,1})
            {var a=P(arc,end,false);var b=P(arc+1,end,false);var c=P(arc,end,true);var d=P(arc+1,end,true);EquipmentTriangle(tool,a,b,c,new(0,0,end==0?-1:1));EquipmentTriangle(tool,b,d,c,new(0,0,end==0?-1:1));}
        }
        foreach(int edge in new[]{0,16})
        {EquipmentTriangle(tool,P(edge,0,false),P(edge,1,false),P(edge,0,true),Vector3.Up);EquipmentTriangle(tool,P(edge,1,false),P(edge,1,true),P(edge,0,true),Vector3.Up);}
        tool.GenerateNormals();tool.Index();var mesh=tool.Commit();_meshes[key]=mesh;return mesh;
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
