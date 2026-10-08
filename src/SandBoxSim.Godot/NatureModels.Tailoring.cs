using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Material CollarMaterial(string cloth)
    {
        string key="folded-collar:"+cloth;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        var material=Fabric("#"+new Color(cloth).Darkened(.12f).ToHtml(false));_residentMaterials[key]=material;return material;
    }
    private Mesh CollarLeaf(float side)
    {
        string key="fitted-collar:"+side;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Point(int row,int column)
        {
            float v=row/8f,u=column/8f;
            float inner=Mathf.Lerp(.017f,.028f,v),outer=Mathf.Lerp(.075f,.033f,v)+MathF.Sin(v*MathF.PI)*.012f;
            float x=side*Mathf.Lerp(inner,outer,u),y=Mathf.Lerp(1.255f,1.185f,v)+u*.007f*(1-v);
            return TorsoSurface(x,y,false,.003f+.0025f*MathF.Sin(u*MathF.PI)*MathF.Sin(v*MathF.PI));
        }
        for(int r=0;r<8;r++)for(int c=0;c<8;c++)
        {
            var a=Point(r,c);var b=Point(r,c+1);var d=Point(r+1,c);var e=Point(r+1,c+1);
            EquipmentTriangle(surface,a,b,d,Vector3.Forward);EquipmentTriangle(surface,b,e,d,Vector3.Forward);
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private void AddResidentTailoring(ResidentRig rig,string cloth,string skin,JobType job)
    {
        // Curved hem stitches follow the existing garment, with a back vent and belt fastening.
        for(int i=0;i<24;i++)
        {
            float a=i*MathF.Tau/24;
            Detail(rig.Torso,"sphere",new(MathF.Cos(a)*.201f,.625f,MathF.Sin(a)*.146f),new(.004f,.005f,.004f),cloth);
        }
        Detail(rig.Torso,"box",TorsoSurface(.021f,.737f,false,.006f),new(.033f,.075f,.012f),"#514237");
        foreach(float side in new[]{-1f,1f})
            FineBeam(rig.Torso,new(side*.012f,1.21f,-.123f),new(side*.023f,1.12f,-.161f),.007f,"#c2b69c");
        if(job is JobType.Gatherer or JobType.Hunter or JobType.Trader)
        {
            Sculpt(rig.Torso,FittedRibbon("shoulder-front",.81f,1.20f),Vector3.Zero,ResidentMaterial("#786346"));
            Sculpt(rig.Torso,FittedRibbon("shoulder-back",.81f,1.20f,true),Vector3.Zero,ResidentMaterial("#786346"));
            Detail(rig.Torso,"sphere",new(.23f,.80f,-.008f),new(.15f,.19f,.095f),"#786346");
            Detail(rig.Torso,"box",new(.23f,.875f,-.055f),new(.125f,.048f,.016f),"#a18c68");
        }
    }
}
