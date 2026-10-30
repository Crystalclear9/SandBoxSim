using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Opt-in rig study: an anatomical-plane target passed through the same
    // shoulder/elbow/wrist solver and skin as gameplay. Not motion capture.
    internal void StudyArmElevation(float delta,float elevation,float plane)
    {
        PoseAction(delta,ActionKind.None,ActionPhase.Idle,false,0);
        var outward=new Vector3(MathF.Cos(plane),0,-MathF.Sin(plane));
        var axis=Vector3.Down.Cross(outward).Normalized();
        var upper=Vector3.Down.Rotated(axis,elevation);
        var lower=Vector3.Down.Rotated(axis,elevation+.35f);
        var elbow=ShoulderRest(1)+upper*.28f;
        var wrist=elbow+lower*Hands[1].Position.Length();
        var right=axis;var up=-lower;var basis=new Basis(right,up,right.Cross(up)).Orthonormalized();
        SolveHand(1,wrist+basis*Grip!.Position,basis,true,0,elbow);
        CurlHand(1,.22f);SyncShoulderBone(1);UpdateHandBones(1);
    }
}
