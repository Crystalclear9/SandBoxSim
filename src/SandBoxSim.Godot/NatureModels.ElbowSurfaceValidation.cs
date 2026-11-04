using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateElbowSurface()
    {
        int samples=0,foldedBefore=0,foldedAfter=0;float beforeMin=1,afterMin=1,maxRadiusError=0,maxOpeningError=0,minJacobian=1,minAreaMagnitude=1;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                for(int side=0;side<2;side++)
                {
                    var a=rig.HandSkins[side].Mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
                    var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    foreach(float bend in new[]{0f,.45f,.8f,1.1f,1.4f,1.7f,1.9f,2.2f,2.5f})foreach(float roll in new[]{-1.5f,-.9f,0,.9f,1.5f})
                    {
                        rig.StudyUpperArmReference(side,bend,roll);var corrected=CorrectedForearmSource(rig,side,p);
                        var q=new Vector3[p.Length];var raw=new Vector3[p.Length];
                        for(int i=0;i<p.Length;i++)
                        {
                                                        for(int k=0;k<4;k++){var pose=rig.HandSkeletons[side].GetBonePose(bones[i*4+k]);float w=weights[i*4+k];raw[i]+=(pose*p[i])*w;q[i]+=(pose*corrected[i])*w;}
                        }
                        var pivot=-rig.Hands[side].Position;
                        for(int i=0;i<p.Length;i++)
                        {
                            if(p[i].Y>.205f&&p[i].Y<=.255f)maxRadiusError=MathF.Max(maxRadiusError,MathF.Abs((q[i]-pivot).Length()-(p[i]-pivot).Length()));
                            if(p[i].Y>=.273f)maxOpeningError=MathF.Max(maxOpeningError,(rig.Elbows[side].Basis*(rig.Hands[side].Position+rig.Hands[side].Basis*q[i])).DistanceTo(p[i]+rig.Hands[side].Position));
                        }
                        for(int t=0;t<ids.Length;t+=3)
                        {
                            int aa=ids[t],bb=ids[t+1],cc=ids[t+2];
                            if(p[aa].Y<.205f||p[bb].Y<.205f||p[cc].Y<.205f||p[aa].Y>.255f||p[bb].Y>.255f||p[cc].Y>.255f)continue;
                            var n=(p[cc]-p[aa]).Cross(p[bb]-p[aa]);float area=n.Length();if(area<1e-10f)continue;
                            var centre=(p[aa]+p[bb]+p[cc])/3;
                            const float h=.00001f;
                            var hinge=new Basis(Vector3.Right,-bend).GetRotationQuaternion();
                            Vector3 At(Vector3 v)=>ElbowSurfacePoint(v,roll,hinge);
                            var jac=new Basis((At(centre+Vector3.Right*h)-At(centre-Vector3.Right*h))/(2*h),(At(centre+Vector3.Up*h)-At(centre-Vector3.Up*h))/(2*h),(At(centre+Vector3.Back*h)-At(centre-Vector3.Back*h))/(2*h));
                            minJacobian=MathF.Min(minJacobian,jac.Determinant());
                            var expected=(jac.Inverse().Transposed()*n).Normalized();
                            float before=expected.Dot((raw[cc]-raw[aa]).Cross(raw[bb]-raw[aa]))/area;
                            float after=expected.Dot((q[cc]-q[aa]).Cross(q[bb]-q[aa]))/area;
                            minAreaMagnitude=MathF.Min(minAreaMagnitude,(q[cc]-q[aa]).Cross(q[bb]-q[aa]).Length()/area);
                            beforeMin=MathF.Min(beforeMin,before);afterMin=MathF.Min(afterMin,after);if(before<=0)foldedBefore++;if(after<=0)foldedAfter++;samples++;
                        }
                    }
                }
            }
            finally{rig.Free();}
        }
        if(foldedAfter!=0||afterMin<=0||minJacobian<.05f||minAreaMagnitude<.10f||maxRadiusError>.001f||maxOpeningError>.0001f)throw new InvalidOperationException($"Elbow surface invalid: folds={foldedAfter}, area={afterMin}, Jacobian={minJacobian}, areaMagnitude={minAreaMagnitude}, radius={maxRadiusError}, opening={maxOpeningError}");
        GD.Print($"ELBOW_SURFACE_METRICS: samples={samples}, nonpositive before/after={foldedBefore}/{foldedAfter}, minimum signed area ratio={beforeMin:F4}/{afterMin:F4}, minimum analytic Jacobian/area magnitude={minJacobian:F4}/{minAreaMagnitude:F4}, max radius/opening error={maxRadiusError:F6}/{maxOpeningError:F6} m");
    }
    private (int Samples,int Folds,float Minimum) GameplayElbowSurface(ResidentRig rig,int side)
    {
        var a=rig.HandSkins[side].Mesh.SurfaceGetArrays(0);
        var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
        var bones=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var corrected=CorrectedForearmSource(rig,side,p);var q=new Vector3[p.Length];
        for(int i=0;i<p.Length;i++)if(p[i].Y>=.205f&&p[i].Y<=.255f)
            for(int k=0;k<4;k++)q[i]+=(rig.HandSkeletons[side].GetBonePose(bones[i*4+k])*corrected[i])*weights[i*4+k];
        float roll=rig.ForearmRoll(side);var axis=rig.Hands[side].Position.Normalized();
        var unhinge=(rig.Elbows[side].Basis*new Basis(axis,-roll)).Inverse().GetRotationQuaternion();
        var inverse=rig.Hands[side].Basis.Inverse();
        Vector3 At(Vector3 v)=>inverse*ElbowSurfacePoint(v,roll,unhinge);
        int samples=0,folds=0;float minimum=1;
        for(int t=0;t<ids.Length;t+=3)
        {
            int aa=ids[t],bb=ids[t+1],cc=ids[t+2];
            if(p[aa].Y<.205f||p[bb].Y<.205f||p[cc].Y<.205f||p[aa].Y>.255f||p[bb].Y>.255f||p[cc].Y>.255f)continue;
            var n=(p[cc]-p[aa]).Cross(p[bb]-p[aa]);float area=n.Length();if(area<1e-10f)continue;
            var centre=(p[aa]+p[bb]+p[cc])/3;const float h=.00001f;
            var jac=new Basis((At(centre+Vector3.Right*h)-At(centre-Vector3.Right*h))/(2*h),(At(centre+Vector3.Up*h)-At(centre-Vector3.Up*h))/(2*h),(At(centre+Vector3.Back*h)-At(centre-Vector3.Back*h))/(2*h));
            var expected=(jac.Inverse().Transposed()*n).Normalized();
            float ratio=expected.Dot((q[cc]-q[aa]).Cross(q[bb]-q[aa]))/area;
            minimum=MathF.Min(minimum,ratio);if(ratio<=0)folds++;samples++;
        }
        return(samples,folds,minimum);
    }

}
