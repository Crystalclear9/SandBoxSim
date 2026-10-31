using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Shared authored reference for free reaches and shoulder correctives.
    // Axial rotation is independent of elbow flexion. These presentation
    // angles are not clinical limits or a fitted population motion model.
    internal static Basis PreferredArmFrame(int side,Vector3 upper)
    {
        var direction=upper.Normalized();
        float elevation=MathF.Acos(Math.Clamp(direction.Dot(Vector3.Down),-1,1));
        var horizontal=new Vector3(direction.X,0,direction.Z);
        float forward=horizontal.LengthSquared()>.000001f?MathF.Max(0,-horizontal.Normalized().Z):0;
        float turn=Mathf.DegToRad(30-25*forward)*Mathf.SmoothStep(Mathf.DegToRad(15),Mathf.DegToRad(130),elevation);
        var swing=new Basis(new Quaternion(Vector3.Down,direction));
        return new Basis(direction,(side==0?-1:1)*turn)*swing;
    }
}
