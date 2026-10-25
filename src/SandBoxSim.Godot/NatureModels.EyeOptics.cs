using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // A bounded corneal dome on the existing eye, not a second floating sphere.
    // Outside the central 8 mm radius the authored surface remains unchanged.
    private static float CornealHeight(Vector3 p)
    {
        float radius=new Vector2(p.X,p.Y).Length();
        float rim=1-Mathf.SmoothStep(.006f,.008f,radius);
        float front=1-Mathf.SmoothStep(-.006f,-.002f,p.Z);
        return .00035f*MathF.Exp(-MathF.Pow(radius/.0055f,4))*rim*front;
    }
    private static (Vector3 Point,Vector3 Normal) CornealSurface(Vector3 p,Vector3 normal)
    {
        const float step=.00001f;
        float height=CornealHeight(p);
        var gradient=new Vector3(
            (CornealHeight(p+Vector3.Right*step)-CornealHeight(p-Vector3.Right*step))/(2*step),
            (CornealHeight(p+Vector3.Up*step)-CornealHeight(p-Vector3.Up*step))/(2*step),
            (CornealHeight(p+Vector3.Back*step)-CornealHeight(p-Vector3.Back*step))/(2*step));
        // Inverse-transpose of (x,y,z) -> (x,y,z-height(x,y,z)).
        float z=normal.Z/(1-gradient.Z);
        var corrected=new Vector3(normal.X+gradient.X*z,normal.Y+gradient.Y*z,z).Normalized();
        return(p+Vector3.Forward*height,corrected);
    }
}
