using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static (Vector3 Point,Vector3 Normal) RefineForearmProfile(Vector3 point,Vector3 normal,int frame)
        =>FitForearmSupport(point,normal,frame);

    private static void FitFemalePalm(Vector3[] points,Vector3[] normals,int[] indices)
    {
        // In canonical hand space +X is the little-finger side and +Z the
        // back of the hand. Shape the proximal palm across both surfaces
        // before mirroring, with the palmar grip and thumb base pinned.
        var rest=(Vector3[])points.Clone();
        var moved=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var p=rest[i];
            float proximal=TorsoRamp(-.040f,-.024f,p.Y)*(1-TorsoRamp(-.015f,.008f,p.Y));
            float ulnar=TorsoRamp(.012f,.025f,p.X)*TorsoRamp(-.012f,-.002f,p.Z);
            float dorsalHeight=TorsoRamp(-.035f,-.022f,p.Y)*(1-TorsoRamp(-.012f,.019f,p.Y));
            float dorsal=TorsoRamp(.003f,.011f,p.Z)*TorsoRamp(-.028f,-.010f,p.X);
            var offset=new Vector3(-.0038f*proximal*ulnar,0,-.0032f*dorsalHeight*dorsal);
            if(offset==Vector3.Zero)continue;
            points[i]+=offset;
            moved[i]=1;
        }
        TransportFairingNormals(rest,points,normals,indices,moved);
    }

    private static void ValidateFemalePalmGeometry(ResidentRig male,ResidentRig female)
    {
        // Use a proximal palm plane outside the independent forearm fit.
        // Slice the triangles of both gameplay representations.
        Vector2 Section(Mesh mesh)
        {
            const float y=-.024f;
            var arrays=mesh.SurfaceGetArrays(0);
            var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            float low=float.PositiveInfinity,high=float.NegativeInfinity,back=high;
            for(int t=0;t<ids.Length;t+=3)for(int edge=0;edge<3;edge++)
            {
                var a=p[ids[t+edge]];var b=p[ids[t+(edge+1)%3]];
                if((a.Y-y)*(b.Y-y)>0||MathF.Abs(b.Y-a.Y)<1e-9f)continue;
                var hit=a.Lerp(b,(y-a.Y)/(b.Y-a.Y));
                low=MathF.Min(low,hit.X);high=MathF.Max(high,hit.X);
                if(MathF.Abs(hit.X)<.020f)back=MathF.Max(back,hit.Z);
            }
            return new(high-low,back);
        }
        for(int side=0;side<2;side++)
        foreach(bool distant in new[]{false,true})
        {
            var original=distant?male.HandOpen[side]:male.HandSkins[side].Mesh;
            var fitted=distant?female.HandOpen[side]:female.HandSkins[side].Mesh;
            var change=Section(original)-Section(fitted);
            float minimumDepth=distant?.0017f:.0026f;
            if(!change.IsFinite()||change.X<.001f||change.X>.0041f||change.Y<minimumDepth||change.Y>.0035f)
                throw new InvalidOperationException($"Female palm profile loses its contour: side={side}, distant={distant}, width/depth={change}");
            if(original.GetSurfaceCount()!=fitted.GetSurfaceCount())
                throw new InvalidOperationException("Female palm fit changes the hand material partitions");
            for(int surface=0;surface<original.GetSurfaceCount();surface++)
            {
                var before=original.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var after=fitted.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if(before.Length!=after.Length)
                    throw new InvalidOperationException("Female palm fit changes the hand vertex count");
                for(int i=0;i<before.Length;i++)
                    if((surface==1||before[i].Y<-.047f)&&before[i].DistanceTo(after[i])>.000001f)
                        throw new InvalidOperationException("Female palm fit moves the fingertips or nails");
            }
            GD.Print($"FEMALE_PALM_PASS: side={side}, distant={distant}, width reduction={change.X*1000:F2} mm, dorsal reduction={change.Y*1000:F2} mm; fixed fingertips and nails");
        }
    }

    private static void ValidateWristContour()
    {
        ValidateForearmProfileContinuity();
        int samples=0;
        for(int frame=0;frame<2;frame++)
        {
            float lower=frame==1?-.014f:.005f;
            foreach(float y in new[]{-.10f,-.021f,lower,.290f,.30f})
            {
                var p=new Vector3(-.019f,y,.026f);var n=new Vector3(.3f,.4f,.8660254f).Normalized();
                var fitted=RefineForearmProfile(p,n,frame);
                if(fitted.Point!=p||fitted.Normal!=n)throw new InvalidOperationException("Wrist fit changes fingers, grip or buried elbow");
            }
            foreach(float y in new[]{lower,.005f,.035f,.245f,.275f,.290f})
            {
                const float e=.000001f;var p=new Vector3(.025f,y,.028f);
                var a=RefineForearmProfile(p-Vector3.Up*e,Vector3.Back,frame);
                var b=RefineForearmProfile(p+Vector3.Up*e,Vector3.Back,frame);
                if(a.Point.DistanceTo(b.Point)>.00001f||a.Normal.DistanceTo(b.Normal)>.003f)
                    throw new InvalidOperationException("Wrist support has a discontinuous profile/tangent");
            }
            foreach(float y in new[]{-.01f,0f,.005f,.012f,.027f,.045f,.060f,.090f,.120f,.145f,.19f,.26f})
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
