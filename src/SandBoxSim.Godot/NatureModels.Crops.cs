using System;
using Godot;
using SandBoxSim.Core.Systems;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    public Node3D CropField(CropKind kind,int phase,bool detailed=true)
    {
        var root=new Node3D {Name="SeasonalCrops"};string key=$"crop:{kind}:{phase}:{detailed}";
        if(_meshes.TryGetValue(key,out var cached)){root.AddChild(new MeshInstance3D {Mesh=cached});return root;}
        float height=phase switch {0=>.20f,1=>.45f,2=>.62f,_=>.09f};
        for(int row=0;row<4;row++)for(int col=0;col<5;col++)
        {
            var p=new Vector3(-.8f+row*.5f,.12f,-.8f+col*.4f);
            if(kind==CropKind.Grain)
            {
                Part(root,detailed?"cylinder":"cylinder-low",p+Vector3.Up*height/2,new(.017f,height,.017f),4);
                if(phase is 1 or 2)
                {
                    Part(root,detailed?"capsule":"capsule-low",p+Vector3.Up*height,new(.055f,.12f,.055f),phase==2?15:4);
                    if(detailed)for(int awn=0;awn<3;awn++)FineBeam(root,p+Vector3.Up*(height+.02f+awn*.025f),p+new Vector3(.045f,.10f+height+awn*.025f,0),.0017f,phase==2?"#bba276":"#718354");
                }
            }
            else
            {
                for(int leaf=0;leaf<(detailed?4:2);leaf++)
                {
                    float angle=leaf*MathF.PI/2;
                    Part(root,detailed?"sphere":"seed",p+new Vector3(MathF.Sin(angle)*.07f,height*.35f,MathF.Cos(angle)*.07f),new(.18f,height*.15f,.07f),4,new(.25f,angle,.22f));
                }
                if(phase==2&&kind==CropKind.Roots)Part(root,detailed?"sphere":"seed",p+Vector3.Up*.04f,new(.14f,.10f,.14f),8);
                if(phase is 1 or 2&&kind==CropKind.Legumes)
                    foreach(float side in new[]{-1f,1f})Part(root,detailed?"capsule":"capsule-low",p+new Vector3(side*.08f,height*.45f,0),new(.04f,height*.35f,.04f),4,new(0,0,side*.25f));
            }
        }
        MergeResidentParts(root,key);return root;
    }
    private void ValidateCropModels()
    {
        foreach(bool detailed in new[]{true,false})foreach(CropKind kind in Enum.GetValues<CropKind>())for(int phase=0;phase<4;phase++)
        {
            var model=CropField(kind,phase,detailed);
            try
            {
                var mesh=model.GetChild<MeshInstance3D>(0).Mesh;
                var repeated=CropField(kind,phase,detailed);
                try{if(mesh!=repeated.GetChild<MeshInstance3D>(0).Mesh)throw new InvalidOperationException("Crop mesh not shared");}
                finally{repeated.Free();}
                for(int s=0;s<mesh.GetSurfaceCount();s++)
                {
                    var arrays=mesh.SurfaceGetArrays(s);
                    foreach(var point in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())if(!point.IsFinite())throw new InvalidOperationException("Invalid crop geometry");
                    foreach(var normal in arrays[(int)Mesh.ArrayType.Normal].AsVector3Array())if(!normal.IsFinite())throw new InvalidOperationException("Invalid crop normal");
                }
            }
            finally{model.Free();}
        }
        GD.Print("CROP_MODELS_PASS: three crops, four phases, near/far detail, finite geometry/normals and shared meshes");
    }
}
