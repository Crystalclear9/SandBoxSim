using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Mesh FabricFacing(string key,Vector3[] profile)
    {
        if(_meshes.TryGetValue(key,out var cached))return cached;
        const int sides=48;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        var center=Vector3.Zero;foreach(var p in profile)center+=p;center/=profile.Length;
        Vector3 Point(int row,int column)
        {
            var p=profile[row%profile.Length];float a=column*MathF.Tau/sides;
            return new(MathF.Cos(a)*p.X,p.Y,MathF.Sin(a)*p.Z);
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
        for(int row=0;row<profile.Length;row++)for(int col=0;col<sides;col++)
        {Triangle(row,col,row+1,col,row,col+1);Triangle(row,col+1,row+1,col,row+1,col+1);}
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private Mesh RolledCuff()=>FabricFacing("rolled-cuff",new[]{
        new Vector3(.047f,-.270f,.045f),new(.052f,-.263f,.050f),new(.054f,-.252f,.052f),
        new(.050f,-.241f,.048f),new(.046f,-.241f,.044f),new(.044f,-.253f,.042f),new(.044f,-.266f,.042f)});
    private Mesh TurnedBootRim()=>FabricFacing("turned-boot-rim",new[]{
        new Vector3(.058f,-.047f,.055f),new(.061f,-.039f,.058f),new(.059f,-.028f,.056f),
        new(.055f,-.024f,.052f),new(.053f,-.028f,.050f),new(.054f,-.042f,.051f)});
    private Mesh TurnedNeckline()=>FabricFacing("turned-neckline",new[]{
        new Vector3(.061f,1.276f,.071f),new(.064f,1.280f,.074f),new(.061f,1.285f,.068f),
        new(.057f,1.286f,.061f),new(.055f,1.282f,.060f),new(.057f,1.278f,.064f)});
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
        foreach(float side in new[]{-1f,1f})
        {
            var middle=TorsoSurface(side*.015f,1.19f,false,.005f);
            FineBeam(rig.Torso,TorsoSurface(side*.011f,1.225f,false,.005f),middle,.0035f,"#c2b69c");
            FineBeam(rig.Torso,middle,TorsoSurface(side*.018f,1.16f,false,.005f),.0035f,"#c2b69c");
        }
        if(job is JobType.Gatherer or JobType.Hunter or JobType.Trader)
        {
            Sculpt(rig.Torso,FittedRibbon("shoulder-front",.81f,1.20f),Vector3.Zero,ResidentMaterial("#786346"));
            Sculpt(rig.Torso,FittedRibbon("shoulder-back",.81f,1.20f,true),Vector3.Zero,ResidentMaterial("#786346"));
            var pouch=new Vector3(.23f,.80f,-.008f);
            Sculpt(rig.Torso,SatchelBody(),pouch,LeatherSurface("#786346"),new(-MathF.PI/2,0,0));
            Sculpt(rig.Torso,SatchelFlap(),pouch,LeatherSurface("#786346"));
            Detail(rig.Torso,"sphere",pouch+new Vector3(0,.030f,SatchelDepth(0,.030f)-.005f),new(.010f,.010f,.008f),"#514237");
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
                var layer=FittedRibbon(key,key=="belt"?.748f:key=="armor"?.91f:key=="apron"?.79f:key=="placket"?1.03f:.81f,
                    key=="belt"?.775f:key=="armor"||key=="apron"?1.17f:key=="placket"?1.205f:1.20f,rear);
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
        SelectGarmentCut(previous);
        GD.Print("GARMENT_CUTS_PASS: three cached silhouettes, sewn hems, folded cuffs/neckline and fitted layers per cut");
    }
}
