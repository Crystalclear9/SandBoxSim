using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static (Vector3 Point,Vector3 Normal) RefineForearmProfile(Vector3 point,Vector3 normal,int frame)
        =>FitForearmSupport(point,normal,frame);

    private static void ValidateWristContour()
    {
        int samples=0;
        for(int frame=0;frame<2;frame++)
        {
            foreach(float y in new[]{-.10f,-.02f,0f,.005f,.275f,.30f})
            {
                var p=new Vector3(-.019f,y,.026f);var n=new Vector3(.3f,.4f,.8660254f).Normalized();
                var fitted=RefineForearmProfile(p,n,frame);
                if(fitted.Point!=p||fitted.Normal!=n)throw new InvalidOperationException("Wrist fit changes fingers, grip or buried elbow");
            }
            foreach(float y in new[]{.005f,.035f,.245f,.275f})
            {
                const float e=.000001f;var p=new Vector3(.025f,y,.028f);
                var a=RefineForearmProfile(p-Vector3.Up*e,Vector3.Back,frame);
                var b=RefineForearmProfile(p+Vector3.Up*e,Vector3.Back,frame);
                if(a.Point.DistanceTo(b.Point)>.00001f||a.Normal.DistanceTo(b.Normal)>.003f)
                    throw new InvalidOperationException("Wrist support has a discontinuous profile/tangent");
            }
            foreach(float y in new[]{.012f,.027f,.045f,.060f,.090f,.120f,.145f,.19f,.26f})
            for(int sector=0;sector<24;sector++)
            {
                float a=sector*MathF.Tau/24;var p=new Vector3(.027f*MathF.Cos(a),y,.010f+.024f*MathF.Sin(a));
                var n=new Vector3(MathF.Cos(a),.15f,MathF.Sin(a)).Normalized();var fitted=RefineForearmProfile(p,n,frame);
                if(!fitted.Point.IsFinite()||!fitted.Normal.IsFinite()||MathF.Abs(fitted.Normal.Length()-1)>.0001f||fitted.Point.Y!=p.Y)
                    throw new InvalidOperationException("Invalid continuous wrist geometry");
                const float step=.00001f;
                var dx=(RefineForearmProfile(p+Vector3.Right*step,n,frame).Point-RefineForearmProfile(p-Vector3.Right*step,n,frame).Point)/(2*step);
                var dy=(RefineForearmProfile(p+Vector3.Up*step,n,frame).Point-RefineForearmProfile(p-Vector3.Up*step,n,frame).Point)/(2*step);
                var dz=(RefineForearmProfile(p+Vector3.Back*step,n,frame).Point-RefineForearmProfile(p-Vector3.Back*step,n,frame).Point)/(2*step);
                var jacobian=new Basis(dx,dy,dz);
                if(jacobian.Determinant()<.12f)throw new InvalidOperationException("Wrist support folds its source volume");
                var reference=(jacobian.Inverse().Transposed()*n).Normalized();
                if(reference.DistanceTo(fitted.Normal)>.001f)throw new InvalidOperationException("Wrist normals do not follow their geometric Jacobian");
                samples++;
            }
        }
        GD.Print($"WRIST_CONTOUR_PASS: {samples} two-frame source width/depth/centre samples, exact fingers/grip, continuous boundaries, positive Jacobian and analytic normals");
    }
}
