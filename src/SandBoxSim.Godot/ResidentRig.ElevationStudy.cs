using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Opt-in rig study: an anatomical-plane target passed through the same
    // shoulder/elbow/wrist solver and skin as gameplay. Not motion capture.
    internal void StudyArmElevation(float delta,float elevation,float plane,int side=1)
    {
        PoseAction(delta,ActionKind.None,ActionPhase.Idle,false,0);
        var outward=new Vector3((side==0?-1:1)*MathF.Cos(plane),0,-MathF.Sin(plane));
        var axis=Vector3.Down.Cross(outward).Normalized();
        var upper=Vector3.Down.Rotated(axis,elevation);
        var lower=Vector3.Down.Rotated(axis,elevation+.35f);
        var elbow=ShoulderRest(side)+upper*.28f;
        var wrist=elbow+lower*Hands[side].Position.Length();
        var right=axis;var up=-lower;var basis=new Basis(right,up,right.Cross(up)).Orthonormalized();
        var grip=side==1?Grip!.Position:new Vector3(0,-.035f,-.0286f);
        SolveHand(side,wrist+basis*grip,basis,true,0,elbow);
        CurlHand(side,.22f);SyncShoulderBone(side);UpdateHandBones(side);
    }
}
