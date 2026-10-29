using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float? _femaleShoulderWidth;
    private float FemaleShoulderWidth
    {
        get
        {
            if(_femaleShoulderWidth.HasValue)return _femaleShoulderWidth.Value;
            const string file="trunk-feminine-cage.json";
            _trunkFeminine??=FrameRadii(file,"0868f6499f700995e3fb0eaea1ed7aea623406fbbfa5486b38a35e5d74d3fcad");
            using var doc=JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/models/human/"+file));
            var root=doc.RootElement;
            // The cage uses the male reference's X scale for both bodies.
            // Its nominal 'shoulder' field is an export reference, not the
            // female joint centre. Recover that centre from the source macro.
            float width=root.GetProperty("sourceShoulder")[0].GetSingle()*root.GetProperty("scale")[0].GetSingle();
            if(!float.IsFinite(width)||width<.14f||width>.19f)throw new InvalidOperationException("Invalid source shoulder centre");
            return (_femaleShoulderWidth=width).Value;
        }
    }
    private float ArmWidthScale=>BodyFrame==1?FemaleShoulderWidth/.202f:1.04f;

    private float TailoredPanelHeight(Vector3 point)
    {
        float hem=ResidentRig.BodyY(GarmentBottom),panel=point.Y;
        if(_garmentCut==2&&panel<ResidentRig.BodyY(.80f))
        {
            float angle=MathF.Atan2(point.Z,point.X);
            float vent=.075f*MathF.Pow(MathF.Max(0,-MathF.Sin(angle)),16)+.040f*MathF.Pow(MathF.Max(0,MathF.Sin(angle)),16);
            float k=vent/(.80f-GarmentBottom);
            panel=ResidentRig.BodyY((ResidentRig.SourceBodyY(panel)-.80f*k)/(1-k));
        }
        return panel+(.852f-hem)*(1-Mathf.SmoothStep(hem,1.15f,panel));
    }

    // The sewn sleeve, separate rolled facing and far arm proxy must use the
    // same local cross-section. A smaller arm inside the common cuff leaves
    // an oversized ring even when the torso itself has distinct proportions.
    private void FitArmClothing(ResidentRig rig)
    {
        Vector3 scale=new(ArmWidthScale,1,BodyFrame==1?.90f:1);
        Mesh Fit(Mesh source)
        {
            string key="adult-arm-clothing:"+BodyFrame+":"+source.GetInstanceId();
            if(_meshes.TryGetValue(key,out var cached))return cached;
            var result=new ArrayMesh();
            for(int s=0;s<source.GetSurfaceCount();s++)
            {
                var a=source.SurfaceGetArrays(s);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for(int i=0;i<p.Length;i++){p[i]*=scale;n[i]=(n[i]/scale).Normalized();}
                a[(int)Mesh.ArrayType.Vertex]=p;a[(int)Mesh.ArrayType.Normal]=n;a[(int)Mesh.ArrayType.Tangent]=default(Variant);
                result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,a);result.SurfaceSetMaterial(s,source.SurfaceGetMaterial(s));
            }
            return _meshes[key]=WithScreenLods(result);
        }
        foreach(var arm in rig.Arms)foreach(Node child in arm.GetChildren())
            if(child is MeshInstance3D mesh&&mesh.Mesh!=null)
            {
                mesh.Mesh=Fit(mesh.Mesh);
                if(mesh.HasMeta("DistantMesh"))mesh.SetMeta("DistantMesh",Fit((Mesh)mesh.GetMeta("DistantMesh").AsGodotObject()));
            }
    }

    private static void ValidateArmClothingFit(ResidentRig rig)
    {
        var arrays=rig.DetailedBodyMesh.SurfaceGetArrays(0);
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var detail=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        for(int side=0;side<2;side++)
        {
            var inverse=rig.Arms[side].Transform.AffineInverse();
            Vector3 low=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),high=-low;
            int count=0;
            for(int i=0;i<p.Length;i++)if(detail[i].X>.2628f&&(p[i].X<0)==(side==0))
            {
                var q=inverse*p[i];low=low.Min(q);high=high.Max(q);count++;
            }
            if(count==0)throw new InvalidOperationException("Missing actual sewn cuff ring");
            var sleeve=new Aabb(low,high-low);
            Aabb Facing(bool distant)
            {
                Vector3 lo=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),hi=-lo;int n=0;
                foreach(Node node in rig.Arms[side].GetChildren())if(node is MeshInstance3D mesh&&mesh.Mesh!=null)
                {
                    var source=distant?(Mesh)mesh.GetMeta("DistantMesh").AsGodotObject():mesh.Mesh;
                    for(int s=0;s<source.GetSurfaceCount();s++)foreach(var point in source.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    {
                        var q=mesh.Transform*point;if(q.Y>-.240f)continue;
                        lo=lo.Min(q);hi=hi.Max(q);n++;
                    }
                }
                if(n==0)throw new InvalidOperationException("Missing rolled cuff geometry");
                return new(lo,hi-lo);
            }
            var near=Facing(false);var far=Facing(true);var margin=near.Size-sleeve.Size;
            if(margin.X<.005f||margin.X>.020f||margin.Z<.004f||margin.Z>.020f||
                near.GetCenter().DistanceTo(sleeve.GetCenter())>.020f||
                near.Size.DistanceTo(far.Size)>.001f||near.GetCenter().DistanceTo(far.GetCenter())>.001f)
                throw new InvalidOperationException($"Adult sleeve/facing or distance fit mismatch: sleeve={sleeve}, near={near}, far={far}");
        }
    }
}
