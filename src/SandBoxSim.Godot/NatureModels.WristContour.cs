using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private const float WristHalfWidth=.027f,WristHalfDepth=.0185f;
    private const float SourceWristHalfDepth=.024f;
    private static (Vector3 Point,Vector3 Normal) RefineForearmProfile(Vector3 point,Vector3 normal,int frame)
    {
        var wrist=RefineWristContour(point,normal);point=wrist.Point;normal=wrist.Normal;
        if(point.Y<=.075f||point.Y>=.275f)return wrist;
        // Preserve the exact grip, wrist ring and buried elbow. Smoothly
        // redistribute the source muscle volume through the visible forearm.
        float Smooth(float t)=>t*t*t*(10+t*(-15+6*t));
        (float V,float D) Ramp(float a,float b)
        {
            float t=Math.Clamp((point.Y-a)/(b-a),0,1);
            return(Smooth(t),30*t*t*(1-t)*(1-t)/(b-a));
        }
        var rise=Ramp(.075f,.145f);var fall=Ramp(.255f,.275f);
        float weight=rise.V*(1-fall.V),derivative=rise.D*(1-fall.V)-rise.V*fall.D;
        float rx=frame==1?.18f:.10f,rz=frame==1?.26f:.18f;
        float sx=1-rx*weight,sz=1-rz*weight,z=point.Z-.010f;
        return(new(point.X*sx,point.Y,.010f+z*sz),
            new Vector3(normal.X/sx,normal.Y+normal.X*point.X*rx*derivative/sx+normal.Z*z*rz*derivative/sz,normal.Z/sz).Normalized());
    }

    private static (float Value,float Derivative) WristContourBlend(float y)
    {
        (float V,float D) Ramp(float low,float high)
        {
            float t=Math.Clamp((y-low)/(high-low),0,1);
            // Quintic endpoints retain both tangent and curvature continuity.
            return(t*t*t*(10+t*(-15+6*t)),30*t*t*(1-t)*(1-t)/(high-low));
        }
        var rise=Ramp(.018f,.038f);var fall=Ramp(.070f,.150f);
        return(rise.V*(1-fall.V),rise.D*(1-fall.V)-rise.V*fall.D);
    }

    private static (Vector3 Point,Vector3 Normal) RefineWristContour(Vector3 point,Vector3 normal)
    {
        // Source hand coordinates are shared by the joined forearm, the hand
        // and the distant hand. The source fit's 54 x 48 mm near-circular cuff
        // becomes 54 x 37 mm. This is a local art fit, not an anatomical norm.
        // Keep its asymmetry and wrist bone detail; fingers and grip stay exact.
        if(point.Y<=.018f||point.Y>=.150f)return(point,normal);
        var blend=WristContourBlend(point.Y);float reduction=1-WristHalfDepth/SourceWristHalfDepth;
        float scale=1-reduction*blend.Value,derivative=-reduction*blend.Derivative;
        float z=point.Z-.010f;
        var fitted=new Vector3(point.X,point.Y,.010f+z*scale);
        // Inverse transpose of z'=.010+(z-.010)*scale(y), including its y
        // derivative. Simply scaling old normals leaves a hard shaded cuff.
        var fittedNormal=new Vector3(normal.X,normal.Y-normal.Z*z*derivative/scale,normal.Z/scale).Normalized();
        return(fitted,fittedNormal);
    }

    private static void ValidateWristContour()
    {
        foreach(float y in new[]{-.10f,-.02f,0f,.018f,.150f,.24f})
        {
            var p=new Vector3(-.019f,y,.026f);var n=new Vector3(.3f,.4f,.8660254f).Normalized();var fitted=RefineWristContour(p,n);
            if(fitted.Point!=p||fitted.Normal!=n)throw new InvalidOperationException("Wrist fit changes fingers, grip or proximal forearm");
        }
        foreach(float y in new[]{.018f,.038f,.070f,.150f})
        {
            const float epsilon=.000001f;var left=WristContourBlend(y-epsilon);var right=WristContourBlend(y+epsilon);
            if(MathF.Abs(left.Value-right.Value)>.0002f||MathF.Abs(left.Derivative-right.Derivative)>.04f)
                throw new InvalidOperationException("Wrist contour has a discontinuous profile or tangent");
        }
        foreach(float y in new[]{.020f,.030f,.045f,.055f,.080f,.120f,.145f})
        for(int sector=0;sector<24;sector++)
        {
            float angle=sector*MathF.Tau/24;var p=new Vector3(WristHalfWidth*MathF.Cos(angle),y,.010f+SourceWristHalfDepth*MathF.Sin(angle));
            var normal=new Vector3(MathF.Cos(angle),.15f,MathF.Sin(angle)).Normalized();var fitted=RefineWristContour(p,normal);
            if(!fitted.Point.IsFinite()||!fitted.Normal.IsFinite()||MathF.Abs(fitted.Normal.Length()-1)>.0001f||fitted.Point.DistanceTo(p)>.005501f||fitted.Point.X!=p.X||fitted.Point.Y!=p.Y)
                throw new InvalidOperationException("Wrist contour exceeds its bounded depth-only fit");
            const float step=.00001f;
            var dx=(RefineWristContour(p+Vector3.Right*step,normal).Point-RefineWristContour(p-Vector3.Right*step,normal).Point)/(2*step);
            var dy=(RefineWristContour(p+Vector3.Up*step,normal).Point-RefineWristContour(p-Vector3.Up*step,normal).Point)/(2*step);
            var dz=(RefineWristContour(p+Vector3.Back*step,normal).Point-RefineWristContour(p-Vector3.Back*step,normal).Point)/(2*step);
            var reference=(new Basis(dx,dy,dz).Inverse().Transposed()*normal).Normalized();
            if(reference.DistanceTo(fitted.Normal)>.0005f)throw new InvalidOperationException("Wrist normals do not follow their geometric Jacobian");
            if(y is .045f or .055f)
            {
                float oval=fitted.Point.X*fitted.Point.X/(WristHalfWidth*WristHalfWidth)+(fitted.Point.Z-.010f)*(fitted.Point.Z-.010f)/(WristHalfDepth*WristHalfDepth);
                if(MathF.Abs(oval-1)>.0001f)throw new InvalidOperationException("Wrist fit misses its shared forearm/hand cross section");
            }
        }
        GD.Print("WRIST_CONTOUR_PASS: source-bound 54x37 mm fit, C2 transitions, exact fingers/grip, bounded depth, inverse-transpose normals and shared wrist section");
    }
}
