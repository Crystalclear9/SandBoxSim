using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    // Uses the garment's actual triangle surface, so folds and tapered shoulders are shared by clothing layers.
    private Vector3 TorsoSurface(float x, float y, bool back, float offset = .004f)
    {
        var arrays = Garment().SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        float z = back ? float.NegativeInfinity : float.PositiveInfinity;
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]]; var b = vertices[indices[i+1]]; var c = vertices[indices[i+2]];
            float divisor = (b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);
            if (MathF.Abs(divisor) < .0000001f) continue;
            float u = ((b.Y-c.Y)*(x-c.X)+(c.X-b.X)*(y-c.Y))/divisor;
            float v = ((c.Y-a.Y)*(x-c.X)+(a.X-c.X)*(y-c.Y))/divisor;
            if (u < -.00001f || v < -.00001f || u+v > 1.00001f) continue;
            float hit = u*a.Z+v*b.Z+(1-u-v)*c.Z;
            z = back ? MathF.Max(z,hit) : MathF.Min(z,hit);
        }
        if (!float.IsFinite(z)) throw new InvalidOperationException("Clothing layer outside garment surface");
        return new Vector3(x,y,z+(back ? offset : -offset));
    }
    private static void EquipmentTriangle(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        // Godot's front faces use clockwise winding.
        if ((b-a).Cross(c-a).Dot(outward)>0) (b,c)=(c,b);
        foreach (var p in new[]{a,b,c}) { surface.SetUV(new Vector2(p.X*5,p.Y*5+p.Z*5)); surface.AddVertex(p); }
    }
    private Mesh FittedRibbon(string key,float bottom,float top,bool back=false)
    {
        string cache="fitted:"+key;
        if (_meshes.TryGetValue(cache,out var mesh)) return mesh;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        if(key=="belt")
        {
            // Two surface-following half ribbons join at the sides without a filled cylinder through the waist.
            foreach(bool rear in new[]{false,true}) for(int i=0;i<48;i++)
            {
                float x0=-.165f+i*.33f/48,x1=-.165f+(i+1)*.33f/48;
                var a=TorsoSurface(x0,bottom,rear);var b=TorsoSurface(x1,bottom,rear);
                var c=TorsoSurface(x0,top,rear);var d=TorsoSurface(x1,top,rear);
                EquipmentTriangle(surface,a,b,c,new(0,0,rear?1:-1));EquipmentTriangle(surface,b,d,c,new(0,0,rear?1:-1));
            }
        }
        else
        {
            bool apron=key=="apron" || key=="armor";
            int columns=apron?12:1, rows=24;
            Vector3 Point(int row,int column)
            {
                float t=(float)row/rows,y=Mathf.Lerp(bottom,top,t);
                float width=key=="armor" ? .14f : apron ? Mathf.Lerp(.135f,.088f,t) : .009f;
                float center=apron?0:Mathf.Lerp(.14f,-.145f,t);
                return TorsoSurface(center+Mathf.Lerp(-width,width,(float)column/columns),y,back,apron?.006f:.004f);
            }
            for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
            {
                var a=Point(row,col);var b=Point(row,col+1);var c=Point(row+1,col);var d=Point(row+1,col+1);
                EquipmentTriangle(surface,a,b,c,new(0,0,back?1:-1));EquipmentTriangle(surface,b,d,c,new(0,0,back?1:-1));
            }
        }
        surface.GenerateNormals();surface.Index();mesh=surface.Commit();_meshes[cache]=mesh;return mesh;
    }
    private Mesh HeadwearShell(bool helmet)
    {
        string key=helmet?"fitted-helmet":"woven-hat";
        if(_meshes.TryGetValue(key,out var mesh))return mesh;
        const int sides=64;
        float[] height={.031f,.045f,.076f,.105f,.126f,.137f,.138f};
        float[] width={.103f,.102f,.100f,.094f,.080f,.045f,0};
        float[] depth={.118f,.117f,.115f,.109f,.094f,.053f,0};
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Crown(int ring,int side,bool inside)
        {
            float angle=side*MathF.Tau/sides;
            float inset=inside?.003f:0;
            float y=height[ring]-(inside?.003f:0);
            // Helmet sides and neck skirt descend behind the ears; the forehead remains above the eyes.
            if(helmet && ring<3) y-=MathF.Max(0,MathF.Sin(angle)+.3f)*.035f*(1-ring/3f);
            return new(MathF.Cos(angle)*MathF.Max(0,width[ring]-inset),y,MathF.Sin(angle)*MathF.Max(0,depth[ring]-inset));
        }
        foreach(bool inside in new[]{false,true})for(int ring=0;ring<height.Length-1;ring++)for(int side=0;side<sides;side++)
        {
            var a=Crown(ring,side,inside);var b=Crown(ring,side+1,inside);var c=Crown(ring+1,side,inside);var d=Crown(ring+1,side+1,inside);
            var normal=new Vector3(a.X,.06f,a.Z)*(inside?-1:1);
            EquipmentTriangle(surface,a,b,c,normal);EquipmentTriangle(surface,b,d,c,normal);
        }
        if(!helmet)
        {
            float[] radiusX={.103f,.142f,.196f},radiusZ={.118f,.153f,.192f},brimY={.031f,.035f,.018f};
            Vector3 Brim(int ring,int side,bool underside)
            {
                float angle=side*MathF.Tau/sides;
                return new(MathF.Cos(angle)*radiusX[ring],brimY[ring]+MathF.Sin(angle*3)*.003f*(ring/2f)-(underside?.005f:0),MathF.Sin(angle)*radiusZ[ring]);
            }
            foreach(bool underside in new[]{false,true})for(int ring=0;ring<2;ring++)for(int side=0;side<sides;side++)
            {
                var a=Brim(ring,side,underside);var b=Brim(ring,side+1,underside);var c=Brim(ring+1,side,underside);var d=Brim(ring+1,side+1,underside);
                EquipmentTriangle(surface,a,b,c,underside?Vector3.Down:Vector3.Up);EquipmentTriangle(surface,b,d,c,underside?Vector3.Down:Vector3.Up);
            }
            for(int side=0;side<sides;side++)
            {
                var a=Brim(2,side,false);var b=Brim(2,side+1,false);var c=Brim(2,side,true);var d=Brim(2,side+1,true);
                EquipmentTriangle(surface,a,b,c,new(a.X,0,a.Z));EquipmentTriangle(surface,b,d,c,new(a.X,0,a.Z));
            }
        }
        surface.GenerateNormals();surface.Index();mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private Material? _wovenStraw;
    private Material WovenStraw()
    {
        if (_wovenStraw != null) return _wovenStraw;
        var shader = new Shader { Code = @"shader_type spatial;
varying vec3 straw_position;
void vertex(){ straw_position = VERTEX; }
void fragment(){
float radial = length(straw_position.xz);
float angle = atan(straw_position.z,straw_position.x);
float weave = sin(radial*2100.0 + straw_position.y*1600.0)*sin(angle*160.0);
float footprint = length(fwidth(straw_position))*1600.0;
weave /= 1.0 + footprint*footprint;
float fiber = sin(angle*53.0 + straw_position.y*190.0)*0.035;
ALBEDO = vec3(0.55,0.43,0.27)*(1.0+weave*0.12+fiber);
ROUGHNESS = 0.93; SPECULAR = 0.18;
}" };
        _wovenStraw=new ShaderMaterial { Shader=shader }; return _wovenStraw;
    }
    private void AddResidentEquipment(ResidentRig rig,JobType job)
    {
        if(job is JobType.Farmer or JobType.Soldier)
        {
            rig.Headwear=new Node3D {Name="Headwear"};rig.Head.AddChild(rig.Headwear);
            Sculpt(rig.Headwear,HeadwearShell(job==JobType.Soldier),Vector3.Zero,job==JobType.Soldier ? ResidentMaterial("#7d8581",.48f) : WovenStraw());
            if(job==JobType.Farmer)
            {
                // Fine curved stitching follows the crown; no disk or independent floating ring.
                for(int i=0;i<64;i++)
                {
                    float a=i*MathF.Tau/64,b=(i+1)*MathF.Tau/64;
                    FineBeam(rig.Headwear,new(MathF.Cos(a)*.102f,.056f,MathF.Sin(a)*.116f),new(MathF.Cos(b)*.102f,.056f,MathF.Sin(b)*.116f),.009f,"#695b43");
                    if(i%2==0)FineBeam(rig.Headwear,new(MathF.Cos(a)*.142f,.039f,MathF.Sin(a)*.153f),new(MathF.Cos(a)*.189f,.024f,MathF.Sin(a)*.186f),.0018f,"#9e8963");
                }
            }
        }
        if(job is JobType.Builder or JobType.Miner or JobType.Craftsman or JobType.Soldier)
        {
            Sculpt(rig.Torso,FittedRibbon(job==JobType.Soldier?"armor":"apron",job==JobType.Soldier?.91f:.79f,1.17f),Vector3.Zero,ResidentMaterial(job==JobType.Soldier?"#7d8581":"#75654f"));
            rig.Grip=new Node3D {Name="RightGrip",Position=new(0,-.273f,-.027f)};rig.Elbows[1].AddChild(rig.Grip);
            bool soldier=job==JobType.Soldier;
            Detail(rig.Grip,"cylinder",new(0,soldier?.12f:-.10f,0),new(.020f,soldier?1.6f:.42f,.020f),"#8b6c46");
            if(soldier) Detail(rig.Grip,"cone",new(0,.98f,0),new(.055f,.15f,.038f),"#7d8581");
            else
            {
                Detail(rig.Grip,"masonry",new(0,-.30f,0),new(.13f,.052f,.046f),"#68716f");
                Detail(rig.Grip,"cylinder",new(0,-.26f,0),new(.025f,.026f,.025f),"#514237");
            }
        }
        if(job is JobType.Gatherer or JobType.Hunter or JobType.Trader)
        {
            rig.PackMount=new Node3D {Name="PackMount",Position=new(0,1.02f,TorsoSurface(0,1.02f,true).Z+.066f)};rig.Torso.AddChild(rig.PackMount);
            Sculpt(rig.PackMount,Loft("leather-pack",new[]{new Vector4(-.145f,0,.10f,.045f),new(-.12f,0,.13f,.065f),new(.10f,0,.12f,.060f),new(.15f,0,.09f,.047f)}),Vector3.Zero,ResidentMaterial("#786346"),new(-MathF.PI/2,0,0));
            Detail(rig.PackMount,"masonry",new(0,.11f,.053f),new(.22f,.09f,.025f),"#a18c68");
            foreach(float x in new[]{-.075f,.075f})FineBeam(rig.PackMount,new(x,.10f,.065f),new(x,-.10f,.065f),.015f,"#514237");
        }
    }
}
