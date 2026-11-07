using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float[]? _feminineThoraxSupport;

    // C2 joins keep the curvature continuous where a regional body edit
    // returns to the rib cage. Sharing this ramp also keeps skin supports
    // and their fitted clothing on the same smooth anatomical envelope.
    private static float TorsoRamp(float low,float high,float value)=>ResidentRig.ReachEase((value-low)/(high-low));

    // An adult character-design adjustment in canonical metres. Move the
    // anterior mass medially, not the rib cage, arm roots or whole actor.
    private static Vector3 MedialThoraxPoint(Vector3 p)
    {
        float lateral=MathF.Abs(p.X);
        float active=TorsoRamp(1.07f,1.11f,p.Y)*(1-TorsoRamp(1.18f,1.23f,p.Y))
            *TorsoRamp(.035f,.090f,-p.Z);
        float shift=.010f*TorsoRamp(.005f,.075f,lateral)*(1-TorsoRamp(.130f,.170f,lateral))*active;
        return p+new Vector3(-MathF.Sign(p.X)*shift,0,.0025f*active*TorsoRamp(.045f,.090f,lateral));
    }

    // Coordinate the abdominal, oblique and lumbar planes as one soft torso.
    // The lateral waist opens gently while the lower abdomen returns toward
    // the pelvis. Carry a broad rear surface from the upper seat into the
    // lower ribs so the sagittal curve continues across the waist.
    private static Vector3 BalancedTorsoPoint(Vector3 p)
    {
        float waist=TorsoRamp(.89f,.96f,p.Y)*(1-TorsoRamp(1.025f,1.10f,p.Y));
        float front=TorsoRamp(.027f,.095f,-p.Z);
        float flank=TorsoRamp(.012f,.105f,MathF.Abs(p.X));
        float abdomen=1-TorsoRamp(.95f,1.015f,p.Y);
        float lumbar=TorsoRamp(.915f,.984f,p.Y)*(1-TorsoRamp(1.011f,1.133f,p.Y));
        float back=TorsoRamp(-.020f,.045f,p.Z)*(1-TorsoRamp(.035f,.120f,MathF.Abs(p.X)));
        float sideWaist=TorsoRamp(.875f,.965f,p.Y)*(1-TorsoRamp(1.032f,1.115f,p.Y));
        float lowerFront=TorsoRamp(.882f,.916f,p.Y)*(1-TorsoRamp(.924f,.965f,p.Y));
        float frontDepth=MathF.Max(0,-p.Z)*TorsoRamp(0,.04f,-p.Z);
        return p+new Vector3(MathF.Sign(p.X)*.0035f*sideWaist*flank,0,
            waist*(.0015f+.003f*abdomen)*front+.012f*lumbar*back+.05f*lowerFront*frontDepth);
    }

    private static Vector3 FemalePelvicPoint(Vector3 point)
    {
        // Rebalance the sagittal contour between the upper seat and the
        // rear thigh. Keep hip breadth, waistband and the inner leg fixed.
        float rear=TorsoRamp(.026f,.076f,point.Z);
        float upper=TorsoRamp(.774f,.819f,point.Y)*(1-TorsoRamp(.839f,.900f,point.Y));
        float lower=TorsoRamp(.659f,.713f,point.Y)*(1-TorsoRamp(.740f,.794f,point.Y))
            *TorsoRamp(.033f,.068f,MathF.Abs(point.X));
        point.Z+=rear*(.005f*lower-.009f*upper);
        return FemaleFlankPoint(point);
    }

    private static Vector3 FemaleFlankPoint(Vector3 point)
    {
        float x=MathF.Abs(point.X),y=point.Y,z=point.Z;
        float lateral=TorsoRamp(.075f,.151f,x);
        float side=1-TorsoRamp(.035f,.105f,MathF.Abs(z));
        // Let the oblique plane lead into the pelvis over a wider span.
        // The top of the outer hip rolls into the thigh below the crest.
        float oblique=TorsoRamp(.868f,.918f,y)*(1-TorsoRamp(.940f,.995f,y));
        float crest=TorsoRamp(.786f,.818f,y)*(1-TorsoRamp(.837f,.908f,y));
        float thigh=TorsoRamp(.650f,.695f,y)*(1-TorsoRamp(.725f,.777f,y));
        float width=lateral*side*(.006f*oblique-.007f*crest-.006f*thigh);
        // Roll the diagonal quadrants toward the side, with the abdomen,
        // central back and inseam outside the depth edit.
        float quarter=TorsoRamp(.018f,.042f,MathF.Abs(z))*(1-TorsoRamp(.077f,.111f,MathF.Abs(z)));
        return new(point.X+MathF.Sign(point.X)*width,y,z*(1-.045f*lateral*crest*quarter));
    }

    private void ValidateFemaleFlankSupport()
    {
        var original=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash,"skinRadii");
        var fitted=FemaleThoraxSupport();
        float waist=TrunkSample(fitted,.935f,0)-TrunkSample(original,.935f,0);
        float crest=TrunkSample(original,.820f,0)-TrunkSample(fitted,.820f,0);
        if(waist<.0035f||waist>.0095f||crest<.0055f||crest>.0075f)
            throw new InvalidOperationException($"Female flank loses the waist-to-hip transition: {waist:F4}/{crest:F4} m");

        float minimum=1,maximum=0;
        for(int row=0;row<=36;row++)for(int col=0;col<=19;col++)for(int depth=-8;depth<=8;depth++)
        {
            const float h=.0002f;
            var p=new Vector3(col*.01f,.645f+row*.01f,depth*.02f);
            var q=FemaleFlankPoint(p);
            var mirror=FemaleFlankPoint(new(-p.X,p.Y,p.Z));
            maximum=MathF.Max(maximum,q.DistanceTo(p));
            if(q.Y!=p.Y||!mirror.IsEqualApprox(new Vector3(-q.X,q.Y,q.Z))
                ||(p.X<=.075f||p.Y<=.650f||p.Y>=.995f)&&q!=p)
                throw new InvalidOperationException("Female flank moves a fixed support or loses symmetry");
            var dx=(FemaleFlankPoint(p+Vector3.Right*h)-FemaleFlankPoint(p-Vector3.Right*h))/(2*h);
            var dz=(FemaleFlankPoint(p+Vector3.Back*h)-FemaleFlankPoint(p-Vector3.Back*h))/(2*h);
            minimum=MathF.Min(minimum,dx.X*dz.Z-dx.Z*dz.X);
        }
        if(!float.IsFinite(minimum)||minimum<.78f||maximum>.0073f)
            throw new InvalidOperationException($"Female flank exceeds the fitted support bound: Jacobian={minimum:F3}, edit={maximum:F4} m");
        GD.Print($"FEMALE_FLANK_SUPPORT_PASS: lower waist {waist*1000:F2} mm fuller, outer crest {crest*1000:F2} mm inward; fixed rise, symmetric support, minimum Jacobian {minimum:F3}");
    }

    // Continuous anterior volume on the existing adult thorax, not attached
    // cup primitives. Leave the sternum, lateral wall and upper shoulder fixed.
    private static Vector3 ChestVolumePoint(Vector3 p)
    {
        float x=MathF.Abs(p.X);
        float height=TorsoRamp(1.075f,1.105f,p.Y)*(1-TorsoRamp(1.195f,1.235f,p.Y));
        float root=TorsoRamp(.008f,.028f,x)*(1-TorsoRamp(.085f,.115f,x));
        float upper=MathF.Exp(-MathF.Pow((x-.047f)/.056f,2)-MathF.Pow((p.Y-1.165f)/.053f,2));
        float lower=MathF.Exp(-MathF.Pow((x-.071f)/.043f,2)-MathF.Pow((p.Y-1.113f)/.029f,2));
        float front=TorsoRamp(.040f,.100f,-p.Z);
        // Retain the upper thoracic slope while blending the lower, outer
        // surface back into the ribs. Both lobes use the same bound mesh.
        return p+new Vector3(0,0,height*root*front*(.0055f*lower-.0040f*upper));
    }

    private void ValidateChestVolume()
    {
        float minimum=1,maximum=0;
        for(int row=0;row<=36;row++)for(int col=-14;col<=14;col++)for(int depth=-8;depth<=2;depth++)
        {
            var p=new Vector3(col*.01f,1.06f+row*.005f,depth*.02f);const float h=.0001f;
            var q=ChestVolumePoint(p);maximum=MathF.Max(maximum,p.DistanceTo(q));
            var mirror=ChestVolumePoint(new(-p.X,p.Y,p.Z));
            if(q.X!=p.X||q.Y!=p.Y||MathF.Abs(q.Z-mirror.Z)>1e-6f||p.DistanceTo(q)>.005501f
                ||(MathF.Abs(p.X)<=.008f||MathF.Abs(p.X)>=.115f||p.Z>=-.040f)&&q!=p)
                throw new InvalidOperationException("Anterior chest volume loses symmetry, fixed roots or its 5.5 mm bound");
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
        float lumbar=TrunkSample(fitted,1.00f,MathF.PI/2)-TrunkSample(source,1.00f,MathF.PI/2);
        if(front<.001f||front>.002f||side<.0032f||side>.0037f||lumbar<.010f||lumbar>.0121f)
            throw new InvalidOperationException($"Final waist support loses the balanced depth/breadth transition: front={front:F5}, side={side:F5}, lumbar={lumbar:F5}");
        float minimum=1,maximum=1;
        for(int row=0;row<=28;row++)for(int col=-12;col<=12;col++)for(int depth=-8;depth<=8;depth++)
        {
            var p=new Vector3(col*.01f,.86f+row*.01f,depth*.02f);const float h=.0001f;
            var q=BalancedTorsoPoint(p);
            var mirror=BalancedTorsoPoint(new(-p.X,p.Y,p.Z));
            if(p.DistanceTo(q)>.013f||q.Y!=p.Y||(p.Y<=.875f||p.Y>=1.133f)&&q!=p
                ||!mirror.IsEqualApprox(new Vector3(-q.X,q.Y,q.Z)))
                throw new InvalidOperationException("Balanced torso loses symmetry, a fixed support or its 13 mm bound");
            var dx=(BalancedTorsoPoint(p+Vector3.Right*h)-BalancedTorsoPoint(p-Vector3.Right*h))/(2*h);
            var dy=(BalancedTorsoPoint(p+Vector3.Up*h)-BalancedTorsoPoint(p-Vector3.Up*h))/(2*h);
            var dz=(BalancedTorsoPoint(p+Vector3.Back*h)-BalancedTorsoPoint(p-Vector3.Back*h))/(2*h);
            float determinant=new Basis(dx,dy,dz).Determinant();minimum=MathF.Min(minimum,determinant);maximum=MathF.Max(maximum,determinant);
            if(!float.IsFinite(determinant)||determinant<.85f||determinant>1.50f)
                throw new InvalidOperationException($"Balanced torso has an out-of-range local volume ratio: {determinant:F3} at {p}");
        }
        GD.Print($"BALANCED_TORSO_PASS: final source waist front retreat={front*1000:F2} mm, side ease={side*1000:F2} mm, lumbar fill={lumbar*1000:F2} mm; fixed pelvis/shoulders, symmetric continuous abdominal/lumbar field, Jacobian={minimum:F3}..{maximum:F3}");
    }

    private float[] FemaleThoraxSupport()
    {
        if(_feminineThoraxSupport!=null)return _feminineThoraxSupport;
        var source=_trunkFeminineSkin??=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash,"skinRadii");
        var result=(float[])source.Clone();var ring=new Vector2[TrunkColumns];
        for(int row=0;row<TrunkRows;row++)
        {
            float y=TrunkLow+row*.005f;
            if(y>=1.235f)continue;
            for(int col=0;col<TrunkColumns;col++)
            {
                float a=col*Mathf.Tau/TrunkColumns,r=source[row*TrunkColumns+col];
                var p=FemalePelvicPoint(ChestVolumePoint(BalancedTorsoPoint(MedialThoraxPoint(new(MathF.Cos(a)*r,y,MathF.Sin(a)*r)))));
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
