using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh ResidentBootShaft()
    {
        var profile=FittedFigure
            ?new[]{new Vector4(.019f,0,.047f,.044f),new(.040f,.001f,.048f,.045f),
                new(.084f,.008f,.051f,.050f),new(.115f,.010f,.051f,.051f),new(.150f,.007f,.046f,.046f),
                new(.200f,-.001f,.039f,.038f),new(.246f,-.010f,.034f,.032f),new(.270f,-.017f,.034f,.035f),
                new(.293f,-.024f,.032f,.037f),new(.309f,-.031f,.018f,.025f)}
            :new[]{new Vector4(0,0,.047f,.044f),new(.035f,0,.052f,.048f),new(.085f,-.002f,.057f,.052f),
                new(.15f,-.005f,.052f,.048f),new(.21f,-.010f,.045f,.045f),new(.25f,-.015f,.043f,.044f),new(.28f,-.012f,.047f,.048f)};
        return FittedFigure?Loft("contoured-resident-boot",profile,40):Loft("resident-boot:False",profile);
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
        // The shaped opening rises at the shin and drops behind the knee.
        // The same height field carries the rim and outer leather.
        float opening=1-Mathf.SmoothStep(.025f,.090f,-point.Y);
        float slope=.003f-.008f*Mathf.SmoothStep(-.038f,.042f,point.Z);
        return new(point.X,point.Y+opening*slope,point.Z);
    }

    private void FitFemaleLowerLegs(ResidentRig rig)
    {
        if(BodyFrame!=1||rig.IsChild)return;
        Mesh Fit(Mesh source,Transform3D space,bool shoe)
        {
            string key="female-lower-leg-v3:"+source.GetInstanceId()+":"+space+":"+shoe;
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
        ValidateFemaleBootShaft(female);
    }

    private static void ValidateFemaleBootShaft(ResidentRig female)
    {
        float smallestTaper=float.PositiveInfinity,smallestCover=float.PositiveInfinity;
        for(int side=0;side<2;side++)
        {
            var shaft=female.Knees[side].GetChild<MeshInstance3D>(-1);
            var boot=shaft.Mesh.SurfaceGetArrays(0);
            var points=Array.ConvertAll(boot[(int)Mesh.ArrayType.Vertex].AsVector3Array(),p=>shaft.Transform*p);
            var triangles=boot[(int)Mesh.ArrayType.Index].AsInt32Array();
            float calf=GarmentDepth(points,triangles,0,-.123f,true);
            float ankle=GarmentDepth(points,triangles,0,-.298f,true);
            smallestTaper=MathF.Min(smallestTaper,calf-ankle);
            float end=float.PositiveInfinity;
            foreach(var point in points)end=MathF.Min(end,point.Y);
            var distal=new System.Collections.Generic.HashSet<Vector3>();
            foreach(var point in points)
                if(point.Y<end+.0001f)distal.Add(point);
            var center=Vector3.Zero;
            foreach(var point in distal)center+=point;
            center/=distal.Count;
            var upper=female.Feet[side].GetChild<MeshInstance3D>(-1).Mesh.SurfaceGetArrays(0);
            var vamp=Array.ConvertAll(upper[(int)Mesh.ArrayType.Vertex].AsVector3Array(),p=>new Vector3(p.X,p.Z,p.Y));
            var indices=upper[(int)Mesh.ArrayType.Index].AsInt32Array();
            foreach(float pitch in new[]{-.40f,0,.40f})
            foreach(float roll in new[]{-.15f,0,.15f})
            {
                var basis=new Basis(Vector3.Right,pitch)*new Basis(Vector3.Back,roll);
                var inverse=basis.Inverse();
                foreach(var point in distal)
                {
                    // Probe the buried half of the closing ring in actual
                    // last space while the foot pivots about the ankle.
                    var q=inverse*(center.Lerp(point,.5f)+Vector3.Up*ResidentRig.ShinLength)-ResidentRig.FootCenterOffset;
                    float top=GarmentDepth(vamp,indices,q.X,q.Z,true);
                    float bottom=GarmentDepth(vamp,indices,q.X,q.Z,false);
                    smallestCover=MathF.Min(smallestCover,MathF.Min(top-q.Y,q.Y-bottom));
                }
            }
        }
        if(!float.IsFinite(smallestTaper)||smallestTaper<.026f||smallestTaper>.050f||
            !float.IsFinite(smallestCover)||smallestCover<.001f)
            throw new InvalidOperationException($"Female boot shaft loses calf contour or ankle overlap: taper={smallestTaper:F4}, cover={smallestCover:F4}");
        GD.Print($"FEMALE_SHAFT_PASS: bilateral rendered calf-to-ankle taper={smallestTaper*1000:F1} mm; distal overlap through nine ankle orientations, minimum cover={smallestCover*1000:F1} mm");
    }
}
