using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly float[] _adultShoulderWidths=new float[2];
    private float AdultShoulderWidth
    {
        get
        {
            int frame=BodyFrame==1?1:0;
            if(_adultShoulderWidths[frame]>0)return _adultShoulderWidths[frame];
            string file=frame==1?"trunk-feminine-cage.json":"trunk-masculine-cage.json";
            _=FrameRadii(file,frame==1?FemaleTrunkHash:MaleTrunkHash);
            using var doc=JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/models/human/"+file));
            var root=doc.RootElement;
            // Both bodies use the baseline male coordinate scale; recover
            // each shaped source's actual joint, not its nominal reference.
            float width=root.GetProperty("sourceShoulder")[0].GetSingle()*root.GetProperty("scale")[0].GetSingle();
            if(!float.IsFinite(width)||width<.14f||width>.22f)throw new InvalidOperationException("Invalid source shoulder centre");
            return _adultShoulderWidths[frame]=width;
        }
    }
    private float ArmWidthScale=>AdultShoulderWidth/.202f;

    private float TailoredPanelHeight(Vector3 point)
    {
        float hem=ResidentRig.BodyY(GarmentBottom),panel=point.Y;
        if(_garmentCut==2&&panel<ResidentRig.BodyY(.80f))
        {
            float angle=MathF.Atan2(point.Z,point.X);
            // The sewn mesh's radial angle differs from its original pattern
            // parameter. Recover the actual hem instead of re-evaluating the
            // pattern's cosine here, which left a raised scalloped front.
            float vent=ResidentRig.SourceBodyY(SourceHemHeight(angle))-GarmentBottom;
            float k=vent/(.80f-GarmentBottom);
            panel=ResidentRig.BodyY((ResidentRig.SourceBodyY(panel)-.80f*k)/(1-k));
        }
        // Three actual clothing cuts, not a common thigh-length tunic for men
        // and an identical cropped panel for every woman. The work shirt ends
        // above the full thigh; the travelling coat retains a longer skirt.
        float finished=FinishedGarmentHem;
        // Carry hem shortening through the whole trunk. Combining a narrow
        // anatomical waist with a short 1.15 m height ramp over-compressed the
        // lower panels, even though the radial map remained invertible.
        return panel+(finished-hem)*(1-Mathf.SmoothStep(hem,1.23f,panel));
    }
    private float FinishedGarmentHem=>_garmentCut==2?(BodyFrame==1?.78f:.745f)
        :_garmentCut==1?(BodyFrame==1?.86f:.825f):(BodyFrame==1?.85f:.805f);

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
