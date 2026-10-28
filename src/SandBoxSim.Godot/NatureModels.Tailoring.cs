using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Mesh SewnButton()=>Loft("sewn-button",new[]{
        new Vector4(-.0038f,0,.0001f,.0001f),new(-.0032f,0,.0038f,.0038f),
        new(-.0020f,0,.0048f,.0048f),new(0,0,.0048f,.0048f),
        new(.0010f,0,.0038f,.0038f),new(.0012f,0,.0001f,.0001f)},24);
    private Mesh FabricFacing(string key,Vector3[] profile)
    {
        if(_meshes.TryGetValue(key,out var cached))return cached;
        const int sides=48;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        var center=Vector3.Zero;foreach(var p in profile)center+=p;center/=profile.Length;
        Vector3 Point(int row,int column)
        {
            float v=row/4f;int i=(int)v%profile.Length;float t=v-MathF.Floor(v);
            var previous=profile[(i+profile.Length-1)%profile.Length];var start=profile[i];
            var end=profile[(i+1)%profile.Length];var next=profile[(i+2)%profile.Length];
            var p=start.CubicInterpolate(end,previous,next,t);float a=column*MathF.Tau/sides;
            float neckline=key=="turned-neckline"?.009f*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),4):0;
            float throat=key=="turned-neckline"?.021f*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),4):0;
            return new(MathF.Cos(a)*p.X,p.Y-neckline,MathF.Sin(a)*p.Z-throat);
        }
        void Triangle(int ra,int ca,int rb,int cb,int rc,int cc)
        {
            var a=Point(ra,ca);var b=Point(rb,cb);var c=Point(rc,cc);
            Vector3 Outward(int row,int column)
            {
                float angle=column*MathF.Tau/sides;
                return Point(row,column)-new Vector3(MathF.Cos(angle)*center.X,center.Y,MathF.Sin(angle)*center.Z);
            }
            EquipmentTriangle(surface,a,b,c,Outward(ra,ca)+Outward(rb,cb)+Outward(rc,cc));
        }
        for(int row=0;row<profile.Length*4;row++)for(int col=0;col<sides;col++)
        {Triangle(row,col,row+1,col,row,col+1);Triangle(row,col+1,row+1,col,row+1,col+1);}
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private Mesh RolledCuff()=>FabricFacing("rolled-cuff",new[]{
        new Vector3(.047f,-.270f,.045f),new(.052f,-.263f,.050f),new(.054f,-.252f,.052f),
        new(.050f,-.241f,.048f),new(.046f,-.241f,.044f),new(.044f,-.253f,.042f),new(.044f,-.266f,.042f)});
    private Mesh TurnedBootRim()=>FabricFacing("turned-boot-rim",new[]{
        new Vector3(.050f,-.047f,.047f),new(.053f,-.039f,.050f),new(.051f,-.028f,.048f),
        new(.047f,-.024f,.044f),new(.045f,-.028f,.042f),new(.046f,-.042f,.043f)});
    private Mesh TurnedNeckline()
    {
        string key="sewn-neck-binding:"+_garmentCut;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        // A narrow folded facing follows the actual shirt opening. The old
        // independently sized elliptical ring overhung the front and sides.
        // This closed cross-section has a short opening at the placket;
        // neither its end caps nor its inner wall span the wearer's throat.
        const int sides=96;
        var section=new Vector2[]{new(0,-.003f),new(.0018f,-.0015f),new(.0024f,.002f),
            new(.0014f,.005f),new(-.0008f,.0052f),new(-.0014f,.001f)};
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Point(int row,int column)
        {
            float angle=-MathF.PI/2+.045f+(MathF.Tau-.090f)*column/sides;
            var seam=GarmentPoint(1,angle);var radial=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
            var profile=section[row%section.Length];
            return seam+radial*profile.X+Vector3.Up*profile.Y;
        }
        for(int row=0;row<section.Length;row++)for(int col=0;col<sides;col++)
        {
            var a=Point(row,col);var b=Point(row+1,col);var c=Point(row,col+1);var d=Point(row+1,col+1);
            var outward=((a+b+c+d)*.25f-(GarmentPoint(1,-MathF.PI/2+.045f+(MathF.Tau-.090f)*(col+.5f)/sides)+Vector3.Up*.001f)).Normalized();
            EquipmentTriangle(surface,a,b,c,outward);EquipmentTriangle(surface,c,b,d,outward);
        }
        foreach(int col in new[]{0,sides})
        {
            Vector3 center=Vector3.Zero;for(int row=0;row<section.Length;row++)center+=Point(row,col);center/=section.Length;
            for(int row=0;row<section.Length;row++)EquipmentTriangle(surface,center,Point(row,col),Point(row+1,col),col==0?Vector3.Left:Vector3.Right);
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private Material CollarMaterial(string cloth)
    {
        string key="folded-collar:"+cloth;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        var material=Fabric("#"+new Color(cloth).Darkened(.12f).ToHtml(false));_residentMaterials[key]=material;return material;
    }
    private Mesh CollarLeaf(float side)
    {
        string key="fitted-collar:"+side+":"+_garmentCut;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Point(int row,int column,bool back=false)
        {
            float v=row/8f,u=column/8f;
            float inner=Mathf.Lerp(.017f,.028f,v),outer=Mathf.Lerp(.075f,.033f,v)+MathF.Sin(v*MathF.PI)*.012f;
            float x=side*Mathf.Lerp(inner,outer,u),y=Mathf.Lerp(1.255f,1.185f,v)+u*.007f*(1-v);
            return TorsoSurface(x,y,false,(back?.001f:.003f)+.0025f*MathF.Sin(u*MathF.PI)*MathF.Sin(v*MathF.PI));
        }
        foreach(bool back in new[]{false,true})for(int r=0;r<8;r++)for(int c=0;c<8;c++)
        {
            var a=Point(r,c,back);var b=Point(r,c+1,back);var d=Point(r+1,c,back);var e=Point(r+1,c+1,back);
            EquipmentTriangle(surface,a,b,d,back?Vector3.Back:Vector3.Forward);EquipmentTriangle(surface,b,e,d,back?Vector3.Back:Vector3.Forward);
        }
        foreach(int r in new[]{0,8})for(int c=0;c<8;c++)
        {
            var a=Point(r,c);var b=Point(r,c+1);var d=Point(r,c,true);var e=Point(r,c+1,true);
            EquipmentTriangle(surface,a,b,d,r==0?Vector3.Up:Vector3.Down);EquipmentTriangle(surface,b,e,d,r==0?Vector3.Up:Vector3.Down);
        }
        foreach(int c in new[]{0,8})for(int r=0;r<8;r++)
        {
            var a=Point(r,c);var b=Point(r+1,c);var d=Point(r,c,true);var e=Point(r+1,c,true);
            var outward=new Vector3((c==0?-1:1)*side,0,0);
            EquipmentTriangle(surface,a,b,d,outward);EquipmentTriangle(surface,b,e,d,outward);
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private void AddResidentTailoring(ResidentRig rig,string cloth,string skin,JobType job)
    {
        Detail(rig.Torso,"box",TorsoSurface(.021f,.737f,false,.006f),new(.033f,.075f,.012f),"#514237");
        // Short stitches follow the actual folded surface instead of spanning
        // the chest as two loose, contrasting cords.
        string thread="#"+new Color(cloth).Darkened(.14f).ToHtml(false);
        // A subdued rear-yoke stitch follows the same shirt surface used by
        // the backpack and shoulder skin; no detached trim across the back.
        for(int stitch=0;stitch<48;stitch++)
        {
            float x0=Mathf.Lerp(-.15f,.15f,stitch/48f),x1=x0+.0038f;
            float Y(float x)=>1.155f+.018f*(1-MathF.Pow(x/.15f,2));
            FineBeam(rig.Torso,TorsoSurface(x0,Y(x0),true,.0016f),
                TorsoSurface(x1,Y(x1),true,.0016f),.00045f,thread);
        }
        foreach(float side in new[]{-1f,1f})for(int stitch=0;stitch<24;stitch++)
        {
            float y=Mathf.Lerp(PlacketBottom+.006f,PlacketTop-.006f,stitch/23f);
            FineBeam(rig.Torso,TorsoSurface(side*.007f,y,false,.0047f),
                TorsoSurface(side*.007f,y+.0035f,false,.0047f),.00055f,thread);
        }
        if(job is JobType.Gatherer or JobType.Hunter or JobType.Trader)
        {
            Sculpt(rig.Torso,FittedRibbon("shoulder-front",.81f,1.20f),Vector3.Zero,LeatherSurface("#665141"));
            Sculpt(rig.Torso,FittedRibbon("shoulder-back",.81f,1.20f,true),Vector3.Zero,LeatherSurface("#665141"));
            var pouch=rig.SatchelMount=new Node3D {Name="SatchelMount",Position=new(.23f,.80f,-.008f)};
            rig.Torso.AddChild(pouch);
            Sculpt(pouch,SatchelBody(),Vector3.Zero,LeatherSurface("#786346"),new(-MathF.PI/2,0,0));
            Sculpt(pouch,SatchelFlap(),Vector3.Zero,LeatherSurface("#786346"));
            Detail(pouch,"sphere",new(0,.030f,SatchelDepth(0,.030f)-.005f),new(.010f,.010f,.008f),"#514237");
        }
    }
    private void ValidateGarmentCuts()
    {
        int previous=_garmentCut;
        var meshes=new System.Collections.Generic.HashSet<Mesh>();
        for(int cut=0;cut<3;cut++)
        {
            SelectGarmentCut(cut);var mesh=Garment();meshes.Add(mesh);
            if(MathF.Abs(mesh.GetAabb().Position.Y-GarmentBottom)>.00001f||mesh!=Garment())
                throw new InvalidOperationException("Garment cut loses hem length or cache");
            var arrays=mesh.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<points.Length;i++)if(!points[i].IsFinite()||!normals[i].IsFinite())
                throw new InvalidOperationException("Garment facing has invalid geometry");
            foreach(var key in new[]{"belt","apron","armor","placket","shoulder-front","shoulder-back"})
            {
                bool rear=key=="shoulder-back";
                var layer=FittedRibbon(key,key=="belt"?.748f:key=="armor"?.91f:key=="apron"?.79f:key=="placket"?PlacketBottom:.81f,
                    key=="belt"?.775f:key=="armor"||key=="apron"?1.17f:key=="placket"?PlacketTop:1.20f,rear);
                foreach(var p in layer.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    float gap=MathF.Abs(p.Z-TorsoSurface(p.X,p.Y,key=="belt"?p.Z>0:rear,0).Z);
                    if(gap<.001f||gap>.012f)throw new InvalidOperationException("Clothing layer uses a different garment cut");
                }
            }
        }
        if(meshes.Count!=3)throw new InvalidOperationException("Garment cuts share one silhouette");
        foreach(var mesh in new[]{RolledCuff(),TurnedNeckline()})
        {
            var data=mesh.SurfaceGetArrays(0);var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=data[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<points.Length;i++)if(!points[i].IsFinite()||!normals[i].IsFinite())
                throw new InvalidOperationException("Folded neckline or cuff has invalid geometry");
        }
        ValidateNeckBinding();
        SelectGarmentCut(previous);
        GD.Print("GARMENT_CUTS_PASS: three cached silhouettes, sewn hems, folded cuffs/neckline and fitted layers per cut");
    }
    private void ValidateNeckBinding()
    {
        // Measure the final triangles against the same seam that the baked
        // shirt exposes. This catches detached trim and accidental throat caps.
        var mesh=TurnedNeckline();var arrays=mesh.SurfaceGetArrays(0);
        var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        foreach(var point in points)
        {
            float best=float.PositiveInfinity;
            for(int sample=0;sample<=384;sample++)best=MathF.Min(best,point.DistanceTo(GarmentPoint(1,sample*MathF.Tau/384)));
            if(best>.0065f||point.X*point.X+point.Z*point.Z<.003f)
                throw new InvalidOperationException("Neck binding detaches from the shirt or caps the throat");
        }
        foreach(var normal in normals)if(!normal.IsFinite()||MathF.Abs(normal.Length()-1)>.01f)
            throw new InvalidOperationException("Neck binding has invalid normals");
        if(mesh!=TurnedNeckline()||arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length/3>1200)
            throw new InvalidOperationException("Neck binding loses its cache or detail budget");
        GD.Print("NECK_BINDING_PASS: actual open shirt seam, <=6.5 mm support, open throat, finite normals and cached detail budget");
    }
}
