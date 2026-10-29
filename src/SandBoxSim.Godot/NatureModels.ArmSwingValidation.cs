using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateWalkingArms()
    {
        float maxStep=0,minPalm=1,minThumb=1,maxWrist=0;double worstCorrelation=-1;int samples=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var palmNormals=new Vector3[2];
                for(int side=0;side<2;side++)
                {
                    var a=rig.HandSkins[side].Mesh.SurfaceGetArrays(0);
                    var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var mask=a[(int)Mesh.ArrayType.Color].AsColorArray();
                    for(int i=0;i<p.Length;i++)if(p[i].Y>-.025f&&p[i].Y<0&&mask[i].R<.10f)palmNormals[side]+=n[i];
                    if(palmNormals[side].LengthSquared()<1)throw new InvalidOperationException("Walking test cannot find the actual palmar surface");
                    palmNormals[side]=palmNormals[side].Normalized();
                }
                var previous=new[]{InTorso(rig.Hands[0],rig),InTorso(rig.Hands[1],rig)};
                double sx=0,sy=0,sxx=0,syy=0,sxy=0;int count=0;
                for(int frame=0;frame<720;frame++)
                {
                    bool moving=frame>=60&&frame<600;
                    float speed=frame<240?.55f:frame<420?1.05f:1.4f;
                    rig.ObserveLocomotion(moving?new(0,0,-speed/60):Vector3.Zero,1f/60,0,false);
                    rig.PoseAction(1f/60,ActionKind.Wander,moving?ActionPhase.Moving:ActionPhase.Idle,false,0);
                    var palms=new Transform3D[2];
                    for(int side=0;side<2;side++)
                    {
                        var hand=InTorso(rig.Hands[side],rig);palms[side]=hand;
                        float step=hand.Origin.DistanceTo(previous[side].Origin);maxStep=MathF.Max(maxStep,step);
                        if(frame>0&&step>.025f)throw new InvalidOperationException($"Walking/stop hand jumps: {identity}/{frame}/{side}, {step}");
                        previous[side]=hand;
                        if(frame<120)continue;
                        var medial=side==0?Vector3.Right:Vector3.Left;
                        float facing=(hand.Basis*palmNormals[side]).Normalized().Dot(medial);
                        float thumb=(hand.Basis*new Vector3(-ResidentRig.HandMirror(side),0,0)).Dot(Vector3.Forward);
                        float wrist=rig.Hands[side].Quaternion.GetAngle();
                        minPalm=MathF.Min(minPalm,facing);minThumb=MathF.Min(minThumb,thumb);maxWrist=MathF.Max(maxWrist,wrist);
                        var upper=InTorso(rig.Elbows[side],rig).Origin-InTorso(rig.Arms[side],rig).Origin;
                        var lower=hand.Origin-InTorso(rig.Elbows[side],rig).Origin;
                        float elbow=MathF.Acos(Math.Clamp(upper.Normalized().Dot(lower.Normalized()),-1,1));
                        if(facing<.70f||thumb<.70f||wrist>.10f||elbow<.10f||elbow>.60f||
                            hand.Basis.Y.Dot(Vector3.Up)<.75f||MathF.Abs(hand.Origin.X)<rig.HipHalfWidth+.045f)
                            throw new InvalidOperationException($"Unnatural free arm orientation: {identity}/{frame}/{side}, palm={facing}, thumb={thumb}, wrist={wrist}, elbow={elbow}, hand={hand.Origin}");
                        samples++;
                    }
                    if(frame>=180&&frame<600)
                    {
                        double x=InTorso(rig.Feet[0],rig).Origin.Z-InTorso(rig.Feet[1],rig).Origin.Z;
                        double y=palms[0].Origin.Z-palms[1].Origin.Z;
                        sx+=x;sy+=y;sxx+=x*x;syy+=y*y;sxy+=x*y;count++;
                    }
                }
                double correlation=(count*sxy-sx*sy)/Math.Sqrt((count*sxx-sx*sx)*(count*syy-sy*sy));
                if(!double.IsFinite(correlation)||correlation>-.65)throw new InvalidOperationException("Walking arms no longer oppose the ipsilateral feet: "+correlation);
                worstCorrelation=Math.Max(worstCorrelation,correlation);
                var frozen=rig.Elbows[0].Transform;var handFrozen=rig.Hands[1].Transform;
                rig.PoseAction(1,ActionKind.Wander,ActionPhase.Moving,true,0);
                if(frozen!=rig.Elbows[0].Transform||handFrozen!=rig.Hands[1].Transform)throw new InvalidOperationException("Paused free arms keep moving");
            }
            finally{rig.Free();}
        }
        GD.Print($"WALKING_ARMS_PASS: two adult frames, 3 speeds, start/stop/pause, {samples} actual palmar surfaces/thumb axes; palm/thumb dot minimum={minPalm:F3}/{minThumb:F3}, wrist angle maximum={maxWrist:F3} rad, hand step={maxStep:F4} m, arm/ipsilateral-foot correlation={worstCorrelation:F3}");
    }
}
