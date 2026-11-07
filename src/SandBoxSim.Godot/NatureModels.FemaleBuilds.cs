using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using MeshArrays = Godot.Collections.Array;
using MeshShapes = Godot.Collections.Array<Godot.Collections.Array>;

namespace SandBoxSim.Client;

internal enum FemaleBuild { Reference, StraighterWaist, RoundedHips }

internal sealed partial class NatureModels
{
    internal static FemaleBuild FemaleBuildFor(long identity)
    {
        // Mix the slot and generation bits before choosing the visual build.
        ulong bits=unchecked((ulong)identity);
        bits=unchecked((bits^(bits>>33))*0xff51afd7ed558ccdUL);
        return (FemaleBuild)((bits^(bits>>33))%3);
    }

    private static bool HasFemaleBuildOffset(Vector3 point)=>point.Y>.57f&&point.Y<1.207f;

    private static Vector3 FemaleBuildPoint(Vector3 point,FemaleBuild build,float influence=1)
    {
        if(build==FemaleBuild.Reference||!HasFemaleBuildOffset(point))return point;
        float x=MathF.Abs(point.X),y=point.Y,z=point.Z;
        float waist=TorsoRamp(.865f,.981f,y)*(1-TorsoRamp(1.013f,1.125f,y));
        float hip=TorsoRamp(.62f,.765f,y)*(1-TorsoRamp(.845f,.957f,y));
        float thigh=TorsoRamp(.57f,.666f,y)*(1-TorsoRamp(.715f,.80f,y));
        float back=TorsoRamp(-.024f,.060f,z);
        float ribs=TorsoRamp(1.013f,1.083f,y)*(1-TorsoRamp(1.125f,1.207f,y));
        float anterior=TorsoRamp(.014f,.061f,-z);
        // Spread the lateral fade over the flank so the wider waist does
        // not form a compressed strip at the outer edge of the edit.
        influence*=1-TorsoRamp(.895f,.975f,y)*TorsoRamp(.10f,.25f,x);
        // Vary the lower thorax in both planes. A straighter waist pairs
        // with broader, shallower ribs; rounded hips pair with a narrower
        // rib section and more depth toward the front. Fade below the yoke.
        float breadth=build==FemaleBuild.StraighterWaist?.13f*waist-.032f*hip+.049f*ribs
            :-.042f*waist+.077f*hip+.046f*thigh-.027f*ribs;
        float depth=build==FemaleBuild.StraighterWaist?(.051f-.02f*back)*waist-.028f*back*hip-.048f*ribs*anterior
            :-.035f*waist+(.035f+.057f*back)*hip+.023f*thigh+.046f*ribs*anterior;
        return new(point.X*(1+breadth*influence),y,z*(1+depth*influence));
    }

    private static (Vector3 Point,Vector3 Normal) FemaleBuildVertex(Vector3 point,Vector3 normal,FemaleBuild build,float influence)
    {
        if(influence==0||!HasFemaleBuildOffset(point))return (point,normal);
        var jacobian=FemaleBuildJacobian(point,build,influence);
        return (FemaleBuildPoint(point,build,influence),(jacobian.Inverse().Transposed()*normal).Normalized());
    }

    private static Basis FemaleBuildJacobian(Vector3 point,FemaleBuild build,float influence)
    {
        const float step=.0004f;
        Vector3 Derivative(Vector3 axis)=>(FemaleBuildPoint(point+axis*step,build,influence)-FemaleBuildPoint(point-axis*step,build,influence))/(2*step);
        return new Basis(Derivative(Vector3.Right),Derivative(Vector3.Up),Derivative(Vector3.Back));
    }

    private static MeshArrays ShapeFemaleArrays(MeshArrays arrays,FemaleBuild build,Vector2[]? sleeve=null,
        Vector3[]? rest=null,Vector3[]? restNormals=null,Vector3[]? fitted=null,Vector3[]? fittedNormals=null)
    {
        var result=arrays.Duplicate();
        var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for(int i=0;i<points.Length;i++)
        {
            if(rest!=null&&points[i]==rest[i]&&normals[i]==restNormals![i])
                (points[i],normals[i])=(fitted![i],fittedNormals![i]);
            else
            {
                // The sewn pattern supplies the distance into the sleeve.
                // Keep the cuff and its skin opening on the original bind.
                float influence=sleeve?.Length==points.Length?1-TorsoRamp(0,.060f,sleeve[i].X):1;
                (points[i],normals[i])=FemaleBuildVertex(points[i],normals[i],build,influence);
            }
        }
        result[(int)Mesh.ArrayType.Vertex]=points;
        result[(int)Mesh.ArrayType.Normal]=normals;
        result[(int)Mesh.ArrayType.Tangent]=default(Variant);
        return result;
    }

    private static MeshShapes ShapeFemalePoses(MeshShapes poses,MeshArrays original,MeshArrays shaped,FemaleBuild build,Vector2[]? sleeve=null)
    {
        var rest=original[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var restNormals=original[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var fitted=shaped[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var fittedNormals=shaped[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var result=new MeshShapes();
        // These are absolute (normalized mode) targets. Remap them so
        // movement never blends a fitted resident back toward the base build.
        foreach(var pose in poses)result.Add(ShapeFemaleArrays(pose,build,sleeve,rest,restNormals,fitted,fittedNormals));
        return result;
    }

    private Mesh ShapeFemaleMesh(Mesh source,FemaleBuild build,bool useSleevePattern=false)
    {
        string key="female-build:"+source.GetInstanceId()+":"+build;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var original=(ArrayMesh)source;
        var result=new ArrayMesh {BlendShapeMode=original.BlendShapeMode};
        int shapeCount=original.GetBlendShapeCount();
        for(int shape=0;shape<shapeCount;shape++)result.AddBlendShape(original.GetBlendShapeName(shape));
        using var importer=ImporterMesh.FromMesh(source);
        for(int surface=0;surface<source.GetSurfaceCount();surface++)
        {
            var originalArrays=source.SurfaceGetArrays(surface);
            var sleeve=useSleevePattern?originalArrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array():null;
            var arrays=ShapeFemaleArrays(originalArrays,build,sleeve);
            var poses=shapeCount==0?null:ShapeFemalePoses(source.SurfaceGetBlendShapeArrays(surface),originalArrays,arrays,build);
            var lods=new Godot.Collections.Dictionary();
            for(int lod=0;lod<importer.GetSurfaceLodCount(surface);lod++)
                lods[importer.GetSurfaceLodSize(surface,lod)]=importer.GetSurfaceLodIndices(surface,lod);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays,poses,lods);
            result.SurfaceSetMaterial(surface,source.SurfaceGetMaterial(surface));
        }
        if(source.HasMeta("BodyFrameFit"))result.SetMeta("BodyFrameFit",true);
        return _meshes[key]=result;
    }

    private void ValidateFemaleBuilds()
    {
        float minimumJacobian=1;
        foreach(var build in new[]{FemaleBuild.StraighterWaist,FemaleBuild.RoundedHips})
            for(int row=0;row<=32;row++)for(int col=0;col<=25;col++)for(int depth=-16;depth<=16;depth+=2)
            {
                var point=new Vector3(col*.01f,.57f+row*.02f,depth*.01f);
                float jacobian=FemaleBuildJacobian(point,build,1).Determinant();
                if(!float.IsFinite(jacobian)||jacobian<.70f||jacobian>1.35f)
                    throw new InvalidOperationException($"Female build {build} overcompresses the surface at {point}: {jacobian:F3}");
                minimumJacobian=MathF.Min(jacobian,minimumJacobian);
            }
        var reference=Resident(55,false,JobType.None);
        var straight=Resident(55,false,JobType.None,FemaleBuild.StraighterWaist);
        var rounded=Resident(55,false,JobType.None,FemaleBuild.RoundedHips);
        try
        {
            float Waist(Mesh mesh)=>ClothSectionWidth(mesh,1.003f);
            float Hip(ResidentRig rig)=>ClothSectionWidth(rig.Trousers!.Mesh,.785f);
            float waistSpread=Waist(straight.DetailedBodyMesh)-Waist(rounded.DetailedBodyMesh);
            float hipSpread=Hip(rounded)-Hip(straight);
            float RibWidth(Mesh mesh)=>ClothSectionWidth(mesh,1.105f);
            float RibDepth(Mesh mesh)
            {
                var arrays=mesh.SurfaceGetArrays(0);
                var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                return GarmentDepth(vertices,indices,0,1.105f,true)-GarmentDepth(vertices,indices,0,1.105f,false);
            }
            float ribWidthSpread=RibWidth(straight.DetailedBodyMesh)-RibWidth(rounded.DetailedBodyMesh);
            float ribDepthSpread=RibDepth(rounded.DetailedBodyMesh)-RibDepth(straight.DetailedBodyMesh);
            if(waistSpread<.025f||hipSpread<.025f
                ||Hip(rounded)<=Hip(reference)||Waist(straight.DetailedBodyMesh)<=Waist(reference.DetailedBodyMesh))
                throw new InvalidOperationException("Female builds lost their distinct waist and hip proportions");
            if(ribWidthSpread<.013f||!float.IsFinite(ribDepthSpread)||ribDepthSpread<.008f)
                throw new InvalidOperationException($"Female builds lose the orthogonal rib proportions: width={ribWidthSpread:F4}, depth={ribDepthSpread:F4}");
            foreach(var rig in new[]{straight,rounded})
            {
                if(rig.Scale!=reference.Scale||rig.Head.Position!=reference.Head.Position||rig.ShoulderRest(1)!=reference.ShoulderRest(1)
                    ||((ArrayMesh)rig.Trousers!.Mesh).GetBlendShapeCount()!=((ArrayMesh)reference.Trousers!.Mesh).GetBlendShapeCount()
                    ||((ArrayMesh)rig.DetailedBodyMesh).GetBlendShapeCount()!=((ArrayMesh)reference.DetailedBodyMesh).GetBlendShapeCount()
                    ||MathF.Sign(Waist(rig.DistantBodyMesh)-Waist(reference.DistantBodyMesh))
                        !=MathF.Sign(Waist(rig.DetailedBodyMesh)-Waist(reference.DetailedBodyMesh)))
                    throw new InvalidOperationException("Female build changed a bind or lost its silhouette during detail switching");
                var rest=reference.Trousers.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var fitted=rig.Trousers.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if(rest.Length!=fitted.Length)throw new InvalidOperationException("Female build changed trouser topology");
                for(int i=0;i<rest.Length;i++)
                    if(rest[i].Y<=.57f&&rest[i]!=fitted[i])
                        throw new InvalidOperationException("Female build moved the lower leg or boot join");

                var baseCloth=reference.DetailedBodyMesh.SurfaceGetArrays(0);
                var basePoints=baseCloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var sleeve=baseCloth[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                var cloth=rig.DetailedBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var distant=new HashSet<Vector3>(rig.DistantBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
                for(int i=0;i<cloth.Length;i++)
                    if(((sleeve[i].X>=.060f||basePoints[i].Y>=1.207f)&&cloth[i]!=basePoints[i])||!distant.Contains(cloth[i]))
                        throw new InvalidOperationException("Female build moved the collar or cuff, or desynchronized near and distant clothing");
                ValidateRenderedBelt(rig);
            }
            long identity=(3L<<32)|55;
            var world=Human(identity,55,false,JobType.None);
            try
            {
                var expected=FemaleBuildFor(identity) switch
                {
                    FemaleBuild.StraighterWaist=>straight,
                    FemaleBuild.RoundedHips=>rounded,
                    _=>reference
                };
                if(expected==reference||world.DetailedBodyMesh!=expected.DetailedBodyMesh||world.Trousers!.Mesh!=expected.Trousers!.Mesh)
                    throw new InvalidOperationException("Gameplay does not select and reuse the resident's stable build");
            }
            finally { world.Free(); }
            GD.Print($"FEMALE_BUILDS_PASS: waist/hip spread={waistSpread*1000:F1}/{hipSpread*1000:F1} mm, rib width/depth spread={ribWidthSpread*1000:F1}/{ribDepthSpread*1000:F1} mm, minimum Jacobian={minimumJacobian:F3}; stable collar, cuff, belt and distant geometry");
        }
        finally { reference.Free();straight.Free();rounded.Free(); }
    }
}
