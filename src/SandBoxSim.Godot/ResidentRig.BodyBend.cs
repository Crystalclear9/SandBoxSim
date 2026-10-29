using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal static float PelvicClothWeight(float y)=>1-Mathf.SmoothStep(.86f,1.12f,y);
    internal Transform3D PelvicClothPose=Transform3D.Identity;
    internal Transform3D LowerGarmentFrame=Transform3D.Identity;
    private readonly List<(Node3D Mount,Transform3D Bind)> _bodyMounts=new();
    private float _restSupport;
    private Vector3 RestSupport(float delta,bool enabled,float identityPhase)
    {
        _restSupport=Mathf.MoveToward(_restSupport,enabled&&!IsChild?1:0,delta/.35f);
        return new(.010f*MathF.Sin(_phase*.33f+identityPhase)*_restSupport,-.003f*_restSupport,0);
    }
    private void PlantRestLeg(int side)
    {
        // Solve the small relaxed stance against the original ankle anchors.
        // Pelvic sway alone would make rigid legs slide through the ground.
        var leg=Legs[side];var prior=leg.Basis;var priorKnee=Knees[side].Rotation.X;
        var anchor=new Vector3(side==0?-HipHalfWidth:HipHalfWidth,HipHeight-ThighLength-ShinLength,-.04f);
        var target=anchor-leg.Position;
        float roll=MathF.Atan2(target.X,-target.Y);var rollBasis=new Basis(Vector3.Back,roll);
        var sagittal=rollBasis.Inverse()*target;float lower=MathF.Sqrt(ShinLength*ShinLength+.04f*.04f),distance=sagittal.Length();
        float angle=MathF.Atan2(-sagittal.Z,-sagittal.Y)+MathF.Acos(Math.Clamp((ThighLength*ThighLength+distance*distance-lower*lower)/(2*ThighLength*distance),-1,1));
        var solved=rollBasis*new Basis(Vector3.Right,angle);
        leg.Basis=new Basis(prior.GetRotationQuaternion().Slerp(solved.GetRotationQuaternion(),_restSupport));
        var remainder=sagittal-new Basis(Vector3.Right,angle)*new Vector3(0,-ThighLength,0);
        float shin=MathF.Atan2(-remainder.Z,-remainder.Y);
        Knees[side].Rotation=new(Mathf.Lerp(priorKnee,shin-MathF.Atan2(.04f,ShinLength)-angle,_restSupport),0,0);
        Feet[side].Basis=(leg.Basis*Knees[side].Basis).Inverse();
    }
    internal void CaptureBodyMounts()
    {
        foreach(var mount in new[]{Holster,PackMount,SatchelMount})
            if(mount!=null)_bodyMounts.Add((mount,mount.Transform));
    }
    private void UpdateBodyBend(Vector3 translation,Basis pelvis)
    {
        // Torso is the upper-body frame. The lower shirt follows the pelvis,
        // with a smooth lumbar transition in the shared skinned surface.
        var hip=new Vector3(0,HipHeight,0);
        LowerGarmentFrame=new(pelvis,hip-pelvis*hip+translation);
        if(IsChild)return;
        PelvicClothPose=Torso.Transform.AffineInverse()*LowerGarmentFrame;
        ShoulderSkeleton.SetBonePose(3,PelvicClothPose);
        foreach(var (mount,rest) in _bodyMounts)
        {
            float w=PelvicClothWeight(rest.Origin.Y);
            var moved=PelvicClothPose*rest;
            mount.Transform=new(new Basis(rest.Basis.GetRotationQuaternion().Slerp(moved.Basis.GetRotationQuaternion(),w)),rest.Origin.Lerp(moved.Origin,w));
        }
    }
}
