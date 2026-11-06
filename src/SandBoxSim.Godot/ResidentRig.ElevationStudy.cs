using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Unloaded joint-angle reference on the gameplay skeleton and skin.
    // Set the angles directly so a prior grip cannot steer the reference.
    internal void StudyArmElevation(float delta,float elevation,float plane,int side=1,float pronation=0,float flexion=.35f)
    {
        bool ambient=AmbientMotionEnabled;AmbientMotionEnabled=false;
        try{PoseAction(delta,ActionKind.None,ActionPhase.Idle,false,0);}
        finally{AmbientMotionEnabled=ambient;}
        var outward=new Vector3((side==0?-1:1)*MathF.Cos(plane),0,-MathF.Sin(plane));
        var axis=Vector3.Down.Cross(outward).Normalized();
        var upper=Vector3.Down.Rotated(axis,elevation);
        Arms[side].Position=ShoulderGirdle(side,upper).Origin;
        Arms[side].Basis=PreferredArmFrame(side,upper);
        Hands[side].Basis=Basis.Identity;
        CurlHand(side,.22f);
        SyncShoulderBone(side);
        StudyForearmRotation(side,new Basis(Vector3.Right,flexion),pronation);
    }
}
