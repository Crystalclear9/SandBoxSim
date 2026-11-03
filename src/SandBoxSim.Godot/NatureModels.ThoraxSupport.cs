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

    // A broad abdominal/side-waist transition, independent of breast mass.
    // Keep the pelvis and posterior depth fixed; avoid a pinched hourglass.
    private static Vector3 BalancedTorsoPoint(Vector3 p)
    {
        float waist=Mathf.SmoothStep(.91f,.96f,p.Y)*(1-Mathf.SmoothStep(1.025f,1.085f,p.Y));
        float front=Mathf.SmoothStep(.035f,.090f,-p.Z);
        float flank=Mathf.SmoothStep(.045f,.085f,MathF.Abs(p.X));
        return p+new Vector3(MathF.Sign(p.X)*.0015f*waist*flank,0,.003f*waist*front);
    }

    // Continuous anterior volume on the existing adult thorax, not attached
    // cup primitives. Leave the sternum, lateral wall and upper shoulder fixed.
    private static Vector3 ChestVolumePoint(Vector3 p)
    {
        float x=MathF.Abs(p.X);
        float height=Mathf.SmoothStep(1.075f,1.105f,p.Y)*(1-Mathf.SmoothStep(1.195f,1.235f,p.Y));
        float root=Mathf.SmoothStep(.008f,.028f,x)*(1-Mathf.SmoothStep(.085f,.115f,x));
        float vertical=Mathf.Lerp(.048f,.072f,Mathf.SmoothStep(1.13f,1.17f,p.Y));
        float oval=MathF.Exp(-MathF.Pow((x-.047f)/.044f,2)-MathF.Pow((p.Y-1.150f)/vertical,2));
        float front=Mathf.SmoothStep(.040f,.100f,-p.Z);
        return p-new Vector3(0,0,.005f*height*root*oval*front);
    }

    private void ValidateChestVolume()
    {
        float minimum=1,maximum=0;
        for(int row=0;row<=36;row++)for(int col=-14;col<=14;col++)for(int depth=-8;depth<=2;depth++)
        {
            var p=new Vector3(col*.01f,1.06f+row*.005f,depth*.02f);const float h=.0001f;
            var q=ChestVolumePoint(p);maximum=MathF.Max(maximum,p.DistanceTo(q));
            var mirror=ChestVolumePoint(new(-p.X,p.Y,p.Z));
            if(q.X!=p.X||q.Y!=p.Y||MathF.Abs(q.Z-mirror.Z)>1e-6f||p.DistanceTo(q)>.005001f
                ||(MathF.Abs(p.X)<=.008f||MathF.Abs(p.X)>=.115f||p.Z>=-.040f)&&q!=p)
                throw new InvalidOperationException("Anterior chest volume loses symmetry, fixed roots or its 5 mm bound");
            var dx=(ChestVolumePoint(p+Vector3.Right*h)-ChestVolumePoint(p-Vector3.Right*h))/(2*h);
            var dy=(ChestVolumePoint(p+Vector3.Up*h)-ChestVolumePoint(p-Vector3.Up*h))/(2*h);
            var dz=(ChestVolumePoint(p+Vector3.Back*h)-ChestVolumePoint(p-Vector3.Back*h))/(2*h);
            float det=new Basis(dx,dy,dz).Determinant();minimum=MathF.Min(minimum,det);
            if(!float.IsFinite(det)||det<.85f||det>1.25f)throw new InvalidOperationException("Anterior chest volume folds the reference surface");
        }
        GD.Print($"CHEST_VOLUME_FIELD_PASS: continuous adult anterior volume, fixed sternum/lateral roots and height; max={maximum*1000:F2} mm, minimum Jacobian={minimum:F3}");
    }

    private void ValidateBalancedTorso()
    {
        var source=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash,"skinRadii");
        var fitted=FemaleThoraxSupport();
        float front=TrunkSample(source,1.00f,-MathF.PI/2)-TrunkSample(fitted,1.00f,-MathF.PI/2);
        float side=TrunkSample(fitted,1.00f,0)-TrunkSample(source,1.00f,0);
        if(front<.0025f||front>.0031f||side<.001f||side>.0016f)
            throw new InvalidOperationException("Final waist support loses the balanced depth/breadth transition");
        float minimum=1;
        for(int row=0;row<=20;row++)for(int col=-12;col<=12;col++)for(int depth=-8;depth<=8;depth++)
        {
            var p=new Vector3(col*.01f,.89f+row*.01f,depth*.02f);const float h=.0001f;
            var q=BalancedTorsoPoint(p);
            if(p.DistanceTo(q)>.00336f||q.Y!=p.Y||(p.Y<=.91f||p.Y>=1.085f)&&q!=p||p.Z>=0&&q.Z!=p.Z)
                throw new InvalidOperationException("Balanced torso changes a fixed support or exceeds 3.36 mm");
            var dx=(BalancedTorsoPoint(p+Vector3.Right*h)-BalancedTorsoPoint(p-Vector3.Right*h))/(2*h);
            var dy=(BalancedTorsoPoint(p+Vector3.Up*h)-BalancedTorsoPoint(p-Vector3.Up*h))/(2*h);
            var dz=(BalancedTorsoPoint(p+Vector3.Back*h)-BalancedTorsoPoint(p-Vector3.Back*h))/(2*h);
            float determinant=new Basis(dx,dy,dz).Determinant();minimum=MathF.Min(minimum,determinant);
            if(!float.IsFinite(determinant)||determinant<.85f||determinant>1.15f)
                throw new InvalidOperationException("Balanced torso folds its abdominal/side-waist surface");
        }
        GD.Print($"BALANCED_TORSO_PASS: final source waist front retreat={front*1000:F2} mm, side ease={side*1000:F2} mm; fixed pelvis/shoulders/posterior depth, minimum Jacobian={minimum:F3}");
    }

    private float[] FemaleThoraxSupport()
    {
        if(_feminineThoraxSupport!=null)return _feminineThoraxSupport;
        var source=_trunkFeminineSkin??=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash,"skinRadii");
        var result=(float[])source.Clone();var ring=new Vector2[TrunkColumns];
        for(int row=0;row<TrunkRows;row++)
        {
            float y=TrunkLow+row*.005f;
            if(y<=.91f||y>=1.235f)continue;
            for(int col=0;col<TrunkColumns;col++)
            {
                float a=col*Mathf.Tau/TrunkColumns,r=source[row*TrunkColumns+col];
                var p=ChestVolumePoint(BalancedTorsoPoint(MedialThoraxPoint(new(MathF.Cos(a)*r,y,MathF.Sin(a)*r))));
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
        // Round changes of meridian tangent in the fitted body reference itself.
        // Contact recovery would otherwise restore the sharp source crease after
        // every cloth smoothing pass. Keep the neck/waist/back outside this edit.
        var mapped=(float[])result.Clone();
        for(int pass=0;pass<8;pass++)
        {
            var old=(float[])result.Clone();
            for(int row=1;row<TrunkRows-1;row++)for(int col=0;col<TrunkColumns;col++)
            {
                float y=TrunkLow+row*.005f,a=col*Mathf.Tau/TrunkColumns;
                float active=Mathf.SmoothStep(1.055f,1.09f,y)*(1-Mathf.SmoothStep(1.195f,1.235f,y))
                    *MathF.Pow(MathF.Max(0,-MathF.Sin(a)),2);
                int i=row*TrunkColumns+col;
                float target=.25f*old[i-TrunkColumns]+.5f*old[i]+.25f*old[i+TrunkColumns];
                result[i]=Mathf.Lerp(old[i],target,active);
                if(MathF.Abs(result[i]-mapped[i])>.006f)throw new InvalidOperationException("Rounded thorax exceeds its 6 mm meridian bound");
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
