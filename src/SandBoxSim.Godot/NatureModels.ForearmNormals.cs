using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<(int Frame,int Side),Godot.Collections.Array<Godot.Collections.Array>> _forearmNormalPoses=new();
    private static float ForearmNormalFreedom(Vector3 p)=>Mathf.SmoothStep(.055f,.085f,p.Y)*(1-Mathf.SmoothStep(.235f,.260f,p.Y));
    private static Vector3[] SkinSurfaceNormals(Vector3[] points,int[] triangles,Vector3[] rest)
    {
        var area=new Vector3[points.Length];
        for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
            var n=(points[c]-points[a]).Cross(points[b]-points[a]);area[a]+=n;area[b]+=n;area[c]+=n;
        }
        static (int,int,int) Key(Vector3 p)=>((int)MathF.Round(p.X*1000000),(int)MathF.Round(p.Y*1000000),(int)MathF.Round(p.Z*1000000));
        var sums=new Dictionary<(int,int,int),Vector3>();
        for(int i=0;i<rest.Length;i++){var key=Key(rest[i]);sums.TryGetValue(key,out var n);sums[key]=n+area[i];}
        for(int i=0;i<points.Length;i++){var n=sums[Key(rest[i])];area[i]=n.LengthSquared()>1e-20f?n.Normalized():Vector3.Zero;}
        return area;
    }
    private ArrayMesh ForearmNormalMesh(ArrayMesh source,int side)
    {
        var result=new ArrayMesh {BlendShapeMode=Mesh.BlendShapeMode.Normalized};
        for(int i=0;i<ResidentRig.ForearmNormalPoseCount;i++)result.AddBlendShape("ForearmNormal"+i);
        for(int surface=0;surface<source.GetSurfaceCount();surface++)
        {
            var arrays=source.SurfaceGetArrays(surface);
            var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            Godot.Collections.Array<Godot.Collections.Array> shapes;
            if(surface==0)
            {
                var key=(BodyFrame,side);
                if(!_forearmNormalPoses.TryGetValue(key,out shapes!))
                    _forearmNormalPoses[key]=shapes=BakeForearmNormals(arrays);
            }
            else
            {
                shapes=new();
                for(int i=0;i<ResidentRig.ForearmNormalPoseCount;i++)
                {
                    var shape=new Godot.Collections.Array();shape.Resize((int)Mesh.ArrayType.Max);
                    shape[(int)Mesh.ArrayType.Vertex]=points;shape[(int)Mesh.ArrayType.Normal]=normals;shapes.Add(shape);
                }
            }
            arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays,shapes);
            result.SurfaceSetMaterial(surface,source.SurfaceGetMaterial(surface));
        }
        return result;
    }
    private static Godot.Collections.Array<Godot.Collections.Array> BakeForearmNormals(Godot.Collections.Array arrays)
    {
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var restNormals=SkinSurfaceNormals(p,ids,p);var axis=new Vector3(0,-.255f,-.010f).Normalized();var pivot=new Vector3(0,.255f,.010f);
        var shapes=new Godot.Collections.Array<Godot.Collections.Array>();
        for(int bend=0;bend<ResidentRig.ForearmNormalBends;bend++)for(int rollIndex=0;rollIndex<ResidentRig.ForearmNormalRolls;rollIndex++)
        {
            float flex=bend*MathF.PI/4,roll=(rollIndex-1)*MathF.PI/2;
            var palette=new Transform3D[ResidentRig.HandBoneCount];Array.Fill(palette,Transform3D.Identity);
            var unhinge=new Basis(Vector3.Right,-flex).GetRotationQuaternion();
            for(int station=0;station<=ResidentRig.ForearmIntervals;station++)
                palette[ResidentRig.ForearmBone(station)]=ResidentRig.ForearmSection((float)station/ResidentRig.ForearmIntervals,Basis.Identity,axis,pivot,roll,unhinge);
            var posed=new Vector3[p.Length];var blends=new Basis[p.Length];
            for(int i=0;i<p.Length;i++)
            {
                var basis=new Basis(Vector3.Zero,Vector3.Zero,Vector3.Zero);Vector3 origin=Vector3.Zero;
                for(int j=0;j<4;j++){var pose=palette[bones[i*4+j]];float w=weights[i*4+j];basis.X+=pose.Basis.X*w;basis.Y+=pose.Basis.Y*w;basis.Z+=pose.Basis.Z*w;origin+=pose.Origin*w;}
                blends[i]=basis;posed[i]=basis*p[i]+origin;
            }
            var actual=SkinSurfaceNormals(posed,ids,p);var corrected=(Vector3[])n.Clone();
            for(int i=0;i<p.Length;i++)
            {
                float free=ForearmNormalFreedom(p[i]);if(free<=0||restNormals[i].LengthSquared()<.5f||actual[i].LengthSquared()<.5f)continue;
                var expected=(blends[i]*restNormals[i]).Normalized();var original=(blends[i]*n[i]).Normalized();
                var target=new Quaternion(expected,actual[i])*original;
                var bind=(blends[i].Inverse()*target).Normalized();
                corrected[i]=n[i].Slerp(bind,free).Normalized();
                if(!corrected[i].IsFinite())throw new InvalidOperationException("Nonfinite forearm normal corrective");
            }
            var shape=new Godot.Collections.Array();shape.Resize((int)Mesh.ArrayType.Max);
            shape[(int)Mesh.ArrayType.Vertex]=p;shape[(int)Mesh.ArrayType.Normal]=corrected;shapes.Add(shape);
        }
        return shapes;
    }
}
