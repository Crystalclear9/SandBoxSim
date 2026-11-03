using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float[]? _feminineThoraxSupport;

    // An adult character-design adjustment in canonical metres. Move the
    // anterior mass medially, not the rib cage, arm roots or whole actor.
    private static Vector3 MedialThoraxPoint(Vector3 p)
    {
        float lateral=MathF.Abs(p.X);
        float active=Mathf.SmoothStep(1.07f,1.11f,p.Y)*(1-Mathf.SmoothStep(1.18f,1.23f,p.Y))
            *Mathf.SmoothStep(.035f,.090f,-p.Z);
        float shift=.010f*Mathf.SmoothStep(.005f,.075f,lateral)*(1-Mathf.SmoothStep(.130f,.170f,lateral))*active;
        return p+new Vector3(-MathF.Sign(p.X)*shift,0,.0025f*active*Mathf.SmoothStep(.045f,.090f,lateral));
    }

    private float[] FemaleThoraxSupport()
    {
        if(_feminineThoraxSupport!=null)return _feminineThoraxSupport;
        var source=_trunkFeminineSkin??=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash,"skinRadii");
        var result=(float[])source.Clone();var ring=new Vector2[TrunkColumns];
        for(int row=0;row<TrunkRows;row++)
        {
            float y=TrunkLow+row*.005f;
            if(y<=1.07f||y>=1.23f)continue;
            for(int col=0;col<TrunkColumns;col++)
            {
                float a=col*Mathf.Tau/TrunkColumns,r=source[row*TrunkColumns+col];
                var p=MedialThoraxPoint(new(MathF.Cos(a)*r,y,MathF.Sin(a)*r));
                ring[col]=new(p.X,p.Z);
            }
            // Re-intersect the mapped polygon. Scaling radii directly would
            // also pull the sternum/back inward and would change the wrong axis.
            for(int col=0;col<TrunkColumns;col++)
            {
                float a=col*Mathf.Tau/TrunkColumns;var ray=new Vector2(MathF.Cos(a),MathF.Sin(a));
                float radius=float.PositiveInfinity;
                for(int i=0;i<TrunkColumns;i++)
                {
                    var p=ring[i];var edge=ring[(i+1)%TrunkColumns]-p;
                    float denominator=ray.Cross(edge);if(MathF.Abs(denominator)<1e-9f)continue;
                    float t=p.Cross(edge)/denominator,u=p.Cross(ray)/denominator;
                    if(t>0&&u>=-.00001f&&u<=1.00001f)radius=MathF.Min(radius,t);
                }
                if(!float.IsFinite(radius)||radius<.025f||radius>.34f)
                    throw new InvalidOperationException("Medial thorax loses its anatomical radial section");
                result[row*TrunkColumns+col]=radius;
            }
        }
        return _feminineThoraxSupport=result;
    }

    private void ValidateMedialThorax()
    {
        float maximum=0,minimumJacobian=float.PositiveInfinity;
        for(int row=0;row<=36;row++)for(int col=0;col<=36;col++)for(int depth=0;depth<=8;depth++)
        {
            var p=new Vector3(-.18f+col*.01f,1.04f+row*.006f,-.16f+depth*.02f);
            var fitted=MedialThoraxPoint(p);maximum=MathF.Max(maximum,p.DistanceTo(fitted));
            var mirrored=MedialThoraxPoint(new(-p.X,p.Y,p.Z));
            if(MathF.Abs(fitted.X+mirrored.X)>1e-6f||MathF.Abs(fitted.Z-mirrored.Z)>1e-6f||fitted.Y!=p.Y)
                throw new InvalidOperationException("Thorax map loses bilateral symmetry or vertical support");
            const float h=.0001f;
            var dx=(MedialThoraxPoint(p+Vector3.Right*h)-MedialThoraxPoint(p-Vector3.Right*h))/(2*h);
            var dy=(MedialThoraxPoint(p+Vector3.Up*h)-MedialThoraxPoint(p-Vector3.Up*h))/(2*h);
            var dz=(MedialThoraxPoint(p+Vector3.Back*h)-MedialThoraxPoint(p-Vector3.Back*h))/(2*h);
            float determinant=new Basis(dx,dy,dz).Determinant();minimumJacobian=MathF.Min(minimumJacobian,determinant);
            if(!float.IsFinite(determinant)||determinant<.60f||determinant>1.50f||p.DistanceTo(fitted)>.01031f)
                throw new InvalidOperationException("Thorax map folds or exceeds its local source bound");
            if((p.Z>=-.035f||p.Y<=1.07f||p.Y>=1.23f)&&p!=fitted)
                throw new InvalidOperationException("Thorax map moves the back, waist or upper shoulder support");
        }
        GD.Print($"MEDIAL_THORAX_PASS: anterior-only bilateral map, fixed height/back/waist/shoulder; max displacement={maximum*1000:F2} mm, minimum Jacobian={minimumJacobian:F3}");
    }
}
