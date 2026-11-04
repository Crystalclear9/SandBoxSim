using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static Vector3 ElbowSurfacePoint(Vector3 p,float roll,Quaternion unhinge)
    {
        var pivot=new Vector3(0,.255f,.010f);var axis=-pivot.Normalized();
        var v=p-pivot;float along=v.Dot(axis),across=v.Dot(axis.Cross(Vector3.Right));
        float radius=MathF.Sqrt(along*along+across*across);
        float polar=MathF.Abs(MathF.Atan2(across,along))/MathF.PI;
        float near=1-Mathf.SmoothStep(.040f,.085f,radius);
        float bridge=polar*near;
        // The buried opening remains attached to the upper arm. The visible
        // elbow uses angular coordinates, not a narrow axial weight ramp.
        bridge=Mathf.Lerp(bridge,1,Mathf.SmoothStep(.255f,.273f,p.Y));
        var hinged=new Basis(Quaternion.Identity.Slerp(unhinge,bridge))*v;
        // Twist is a separate map whose angle depends on its own axial
        // coordinate. Evaluating it on the unbent Y coordinate can fold the
        // compressed inside of a strongly flexed elbow during pronation.
        float station=Math.Clamp(hinged.Dot(axis)/(.210f/-axis.Y),0,1);
        float pronation=Mathf.SmoothStep(.02f,.85f,station)*(1-Mathf.SmoothStep(.255f,.273f,p.Y));
        return pivot+new Basis(axis,-roll*(1-pronation))*hinged;
    }
    private static Vector3[] RepairElbowSurface(Vector3[] rest,Vector3[] posed,float roll,Quaternion unhinge)
    {
        var result=(Vector3[])posed.Clone();
        for(int i=0;i<rest.Length;i++)if(rest[i].Y>.170f)
        {
            var v=rest[i]-new Vector3(0,.255f,.010f);var axis=new Vector3(0,-.255f,-.010f).Normalized();
            float d=v.Dot(axis),z=v.Dot(axis.Cross(Vector3.Right));
            float active=1-Mathf.SmoothStep(.075f,.085f,MathF.Sqrt(d*d+z*z));
            result[i]=posed[i].Lerp(ElbowSurfacePoint(rest[i],roll,unhinge),active);
        }
        return result;
    }
}
