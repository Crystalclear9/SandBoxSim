using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh ResidentBootShaft()
    {
        var profile=FittedFigure
            ?new[]{new Vector4(.019f,0,.047f,.044f),new(.051f,0,.051f,.048f),new(.102f,-.003f,.055f,.052f),
                new(.155f,-.006f,.050f,.048f),new(.215f,-.010f,.044f,.045f),new(.25f,-.015f,.043f,.044f),new(.28f,-.012f,.047f,.048f)}
            :new[]{new Vector4(0,0,.047f,.044f),new(.035f,0,.052f,.048f),new(.085f,-.002f,.057f,.052f),
                new(.15f,-.005f,.052f,.048f),new(.21f,-.010f,.045f,.045f),new(.25f,-.015f,.043f,.044f),new(.28f,-.012f,.047f,.048f)};
        return Loft("resident-boot:"+FittedFigure,profile);
    }

    private static Vector3 FemaleBootPoint(Vector3 point,bool shoe)
    {
        if(shoe)
        {
            // Shorten the forepart progressively into the instep, leaving
            // the heel and the vertical contact profile in the same place.
            float toe=.014f*(1-Mathf.SmoothStep(-.170f,.052f,point.Z));
            return new(point.X*.90f,point.Y,point.Z+toe);
        }
        // Keep the rim and trouser overlap. Shape the ankle independently
        // of the calf so the boot shaft follows the lower leg's taper.
        float calf=Mathf.SmoothStep(.045f,.115f,-point.Y);
        float ankle=Mathf.SmoothStep(.11f,.32f,-point.Y);
        return new(point.X*(1-.07f*calf-.07f*ankle),point.Y,point.Z*(1-.05f*calf));
    }

    private void FitFemaleLowerLegs(ResidentRig rig)
    {
        if(BodyFrame!=1||rig.IsChild)return;
        Mesh Fit(Mesh source,Transform3D space,bool shoe)
        {
            string key="female-lower-leg-v2:"+source.GetInstanceId()+":"+space+":"+shoe;
            if(_meshes.TryGetValue(key,out var cached))return cached;
            var mesh=new ArrayMesh();var inverse=space.AffineInverse();
            Vector3 Map(Vector3 p)=>FemaleBootPoint(p,shoe);
            for(int s=0;s<source.GetSurfaceCount();s++)
            {
                var arrays=source.SurfaceGetArrays(s);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for(int i=0;i<p.Length;i++)
                {
                    var q=space*p[i];const float h=.0001f;
                    var jacobian=new Basis((Map(q+Vector3.Right*h)-Map(q-Vector3.Right*h))/(2*h),
                        (Map(q+Vector3.Up*h)-Map(q-Vector3.Up*h))/(2*h),(Map(q+Vector3.Back*h)-Map(q-Vector3.Back*h))/(2*h));
                    if(jacobian.Determinant()<.80f)throw new InvalidOperationException("Lower-leg tailoring folds its source");
                    var normal=space.Basis.Inverse().Transposed()*n[i];
                    n[i]=(space.Basis.Transposed()*jacobian.Inverse().Transposed()*normal).Normalized();p[i]=inverse*Map(q);
                    if(!p[i].IsFinite()||!n[i].IsFinite())throw new InvalidOperationException("Invalid lower-leg tailoring");
                }
                arrays[(int)Mesh.ArrayType.Vertex]=p;arrays[(int)Mesh.ArrayType.Normal]=n;arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(s,source.SurfaceGetMaterial(s));
            }
            return _meshes[key]=WithScreenLods(mesh);
        }
        for(int side=0;side<2;side++)
        {
            foreach(Node child in rig.Knees[side].GetChildren())if(child is MeshInstance3D part&&part.Mesh!=null)part.Mesh=Fit(part.Mesh,part.Transform,false);
            foreach(Node child in rig.Feet[side].GetChildren())if(child is MeshInstance3D part&&part.Mesh!=null)part.Mesh=Fit(part.Mesh,part.Transform,true);
        }
    }

    private static void ValidateFemaleBootFit(ResidentRig male,ResidentRig female)
    {
        var source=male.BootSoleCurve;
        var fitted=female.BootSoleCurve;
        if(source.Length!=25||fitted.Length!=source.Length||MathF.Abs(fitted[0].Z-source[0].Z-.014f)>.0001f
            ||fitted[^1]!=source[^1])throw new InvalidOperationException("Female forepart or heel contact dimensions are wrong");
        float maximum=0;
        foreach(var foot in female.Feet)
        {
            var heights=new float[fitted.Length];Array.Fill(heights,float.PositiveInfinity);
            foreach(Node child in foot.GetChildren())if(child is MeshInstance3D part&&part.Mesh!=null)
                for(int s=0;s<part.Mesh.GetSurfaceCount();s++)
                    foreach(var vertex in part.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    {
                        var point=part.Transform*vertex;
                        for(int i=0;i<fitted.Length;i++)
                            if(MathF.Abs(point.Z-fitted[i].Z)<.0001f)heights[i]=MathF.Min(heights[i],point.Y);
                    }
            for(int i=0;i<fitted.Length;i++)
            {
                maximum=MathF.Max(maximum,MathF.Abs(heights[i]-fitted[i].Y));
                if(fitted[i].Y!=source[i].Y||maximum>.001f)
                    throw new InvalidOperationException($"Female boot contact sample {i} differs from the rendered outsole: {heights[i]}/{fitted[i].Y}");
            }
        }
        var trousers=female.Trousers!.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        float overlap=float.PositiveInfinity;
        for(int side=0;side<2;side++)
        {
            float bottom=float.PositiveInfinity,top=float.NegativeInfinity;
            foreach(var point in trousers)if((point.X<0)==(side==0))bottom=MathF.Min(bottom,point.Y);
            var knee=female.Knees[side];
            foreach(Node child in knee.GetChildren())if(child is MeshInstance3D part&&part.Mesh!=null)
                for(int s=0;s<part.Mesh.GetSurfaceCount();s++)
                    foreach(var point in part.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                        top=MathF.Max(top,(part.Transform*point).Y);
            overlap=MathF.Min(overlap,ResidentRig.HipHeight-ResidentRig.ThighLength+top-bottom);
        }
        if(!float.IsFinite(overlap)||overlap<.008f||overlap>.025f)throw new InvalidOperationException($"Female boot shaft loses the fitted trouser overlap: {overlap}");
        GD.Print($"FEMALE_BOOT_FIT_PASS: 14 mm shorter forepart, same heel and vertical contact profile, two actual soles within {maximum*1000:F2} mm, trouser overlap={overlap*1000:F1} mm");
    }
}
