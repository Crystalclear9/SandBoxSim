using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly record struct ShoulderBinding(int Triangle,Vector3 Barycentric,Vector3 Offset);
    private static Basis ShoulderTriangleFrame(Vector3 a,Vector3 b,Vector3 c)
    {
        var x=(b-a).Normalized();var z=(c-a).Cross(b-a).Normalized();
        return new(x,z.Cross(x),z);
    }
    private ShoulderBinding[] BindShoulderSurface(Vector3[] rest,int[] triangles,Vector2[] uv)
    {
        var cage=ShoulderSurface();var normals=ClothSurfaceNormals(rest,triangles);var result=new ShoulderBinding[rest.Length];
        for(int i=0;i<rest.Length;i++)
        {
            result[i]=new(-1,Vector3.Zero,Vector3.Zero);
            if(uv[i].X>.17f||MathF.Abs(rest[i].X)<.06f||rest[i].Y<1.04f||rest[i].Y>1.34f)continue;
            float sign=rest[i].X<0?-1:1;var p=new Vector3(MathF.Abs(rest[i].X),rest[i].Y,rest[i].Z);
            var normal=new Vector3(normals[i].X*sign,normals[i].Y,normals[i].Z);
            float nearest=.08f*.08f;int face=-1;var hit=Vector3.Zero;
            for(int t=0;t<cage.Triangles.Length;t+=3)
            {
                var a=cage.Points[cage.Triangles[t]];var b=cage.Points[cage.Triangles[t+1]];var c=cage.Points[cage.Triangles[t+2]];
                if((c-a).Cross(b-a).Normalized().Dot(normal)<.1f)continue;
                var q=ClosestShoulderPoint(p,a,b,c);float distance=q.DistanceSquaredTo(p);
                if(distance<nearest){nearest=distance;face=t;hit=q;}
            }
            if(face<0)continue;
            var origin=cage.Points[cage.Triangles[face]];var e0=cage.Points[cage.Triangles[face+1]]-origin;var e1=cage.Points[cage.Triangles[face+2]]-origin;var d=hit-origin;
            float aa=e0.Dot(e0),ab=e0.Dot(e1),bb=e1.Dot(e1),denominator=aa*bb-ab*ab;
            if(denominator<1e-15f)continue;
            float v=(bb*d.Dot(e0)-ab*d.Dot(e1))/denominator,w=(aa*d.Dot(e1)-ab*d.Dot(e0))/denominator;
            var frame=ShoulderTriangleFrame(origin,origin+e0,origin+e1);var offset=frame.Inverse()*(p-hit);
            offset.Z=Math.Clamp(offset.Z,.006f,.018f);
            result[i]=new(face,new(1-v-w,v,w),offset);
        }
        return result;
    }
    internal static Vector3 BlendRigidShoulder(Vector3 point,Transform3D[] transforms,Vector4 weights)
    {
        // Dual-quaternion transport of the anatomical support, baked once.
        // The renderer still uses its normal LBS and cached corrective shapes.
        int reference=0;for(int i=1;i<4;i++)if(weights[i]>weights[reference])reference=i;
        var anchor=transforms[reference].Basis.GetRotationQuaternion();var real=new Quaternion(0,0,0,0);var dual=real;
        for(int i=0;i<4;i++)if(weights[i]>0)
        {
            var r=transforms[i].Basis.GetRotationQuaternion();var p=transforms[i].Origin;
            var d=new Quaternion(p.X,p.Y,p.Z,0)*r*.5f;
            float amount=weights[i]*(r.Dot(anchor)<0?-1:1);real+=r*amount;dual+=d*amount;
        }
        float length=MathF.Sqrt(real.Dot(real));real*=1/length;dual*=1/length;dual-=real*real.Dot(dual);
        var translation=dual*real.Inverse()*2;
        return real*point+new Vector3(translation.X,translation.Y,translation.Z);
    }
    private Vector3[] PoseShoulderSupport(ResidentRig rig,int side,Quaternion rotation,Transform3D girdle,bool torso=false)
    {
        var cage=ShoulderSurface();Quaternion Mirror(Quaternion q)=>side==0?new(q.X,-q.Y,-q.Z,q.W):q;
        var arm=Mirror(rotation);var platform=Mirror(girdle.Basis.GetRotationQuaternion());
        var clavicle=Mirror(ResidentRig.ClavicleRotation(side,-new Basis(rotation).Y).GetRotationQuaternion());
        var shoulder=new Vector3(MathF.Abs(girdle.Origin.X),girdle.Origin.Y,girdle.Origin.Z);
        Transform3D At(Quaternion q)=>new(new Basis(q),shoulder-q*cage.Shoulder);
        // Shoulder01 is a shoulder-girdle segment, not half of a humeral
        // rotation. Carry its rest-fitted surface with the scapular platform.
        // Compose each target with its authored A-pose bind. The source
        // surface is never skinned a second time from an LBS-adducted mesh.
        var bind=cage.ArmBind;var helperBind=Quaternion.Identity.Slerp(bind,.5f);
        var fixedArm=new Transform3D(new Basis(bind),cage.Shoulder-bind*cage.Shoulder);
        var transforms=new[]{Transform3D.Identity,torso?fixedArm:At(arm*bind),At(platform*helperBind),At(clavicle)};
        var result=new Vector3[cage.Points.Length];
        for(int i=0;i<result.Length;i++)
        {
            var w=cage.Weights[i];
            result[i]=BlendRigidShoulder(cage.Source[i],transforms,w);
        }
        return result;
    }
}
