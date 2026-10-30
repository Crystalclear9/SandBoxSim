using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateMotionReference()
    {
        int poses=0;float maximumDirectionError=0,maximumWristError=0,maximumReturnStep=0,maximumSurfaceStretch=0;
        var clip=ResidentMotionReference.Shared.Value;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                for(int frame=0;frame<370;frame++)
                {
                    float time=frame/120f;var source=clip.At(time);rig.StudyCapturedStretch(1f/120,time);
                    for(int side=0;side<2;side++)
                    {
                        var upper=-rig.Arms[side].Basis.Y;var lower=(rig.Arms[side].Basis*rig.Elbows[side].Basis*rig.Hands[side].Position).Normalized();
                        float error=MathF.Max(upper.DistanceTo(side==0?source.LeftUpper:source.RightUpper),lower.DistanceTo(side==0?source.LeftLower:source.RightLower));
                        maximumDirectionError=MathF.Max(maximumDirectionError,error);
                        if(error>.00001f)throw new InvalidOperationException($"Captured arm timing/directions are replaced by procedural IK lag: frame={frame}, side={side}, error={error}");
                        var hand=rig.Arms[side].Basis*rig.Elbows[side].Basis*rig.Hands[side].Basis;var wrist=new Basis(side==0?source.LeftWrist:source.RightWrist);
                        float wristError=hand.X.DistanceTo(wrist.X);maximumWristError=MathF.Max(maximumWristError,wristError);
                        if(wristError>.00001f)throw new InvalidOperationException("Captured wrist pronation is lost during retargeting");
                        if(MathF.Abs((rig.Elbows[side].Basis*rig.Hands[side].Position.Normalized()).X)>.00001f)
                            throw new InvalidOperationException("Captured forearm bends outside the elbow hinge plane");
                        if(frame%30==0)ValidateSleeveSupport(rig,side);
                    }
                    if(frame==150)
                    {
                        var a=rig.Arms[0].Transform;var b=rig.Arms[1].Transform;var torso=rig.Torso.Transform;
                        rig.PoseAction(1,ActionKind.None,ActionPhase.Idle,true,0);
                        if(a!=rig.Arms[0].Transform||b!=rig.Arms[1].Transform||torso!=rig.Torso.Transform)throw new InvalidOperationException("Paused capture keeps advancing");
                    }
                    poses++;
                    if(frame%30==0)maximumSurfaceStretch=MathF.Max(maximumSurfaceStretch,ValidateCapturedSurface(rig));
                }
                rig.StudyCapturedStretch(1f/60,1.5f);var previous=InTorso(rig.Hands[1],rig.Torso).Origin;
                for(int frame=0;frame<90;frame++)
                {
                    rig.PoseAction(1f/60,ActionKind.None,ActionPhase.Moving,false,0);
                    var next=InTorso(rig.Hands[1],rig.Torso).Origin;maximumReturnStep=MathF.Max(maximumReturnStep,next.DistanceTo(previous));previous=next;
                    if(maximumReturnStep>.045f)throw new InvalidOperationException("Interrupting captured motion snaps the returning hand");
                }
                rig.SetHandDetail(false);rig.StudyCapturedStretch(1f/60,1.4f);rig.SetHandDetail(true);
                for(int side=0;side<2;side++)ValidateSleeveSupport(rig,side);
                var idle=Resident(identity,false,JobType.None);
                try
                {
                    float raised=0;
                    for(int frame=0;frame<1200;frame++)
                    {
                        idle.PoseAction(1f/30,ActionKind.None,ActionPhase.Idle,false,0);
                        raised=MathF.Max(raised,MathF.Acos(Math.Clamp((-idle.Arms[1].Basis.Y).Dot(Vector3.Down),-1,1)));
                    }
                    if(raised<Mathf.DegToRad(70))throw new InvalidOperationException("Captured motion is a gallery-only demonstration, absent from idle gameplay");
                }
                finally{idle.Free();}
            }
            finally{rig.Free();}
        }
        var held=Resident(17,false,JobType.Builder);var control=Resident(17,false,JobType.Builder);control.AmbientMotionEnabled=false;
        try
        {
            for(int frame=0;frame<1200;frame++)
            {
                held.PoseAction(1f/30,ActionKind.None,ActionPhase.Idle,false,0);control.PoseAction(1f/30,ActionKind.None,ActionPhase.Idle,false,0);
                if(held.Arms[1].Transform!=control.Arms[1].Transform||held.ToolHeld!=control.ToolHeld)throw new InvalidOperationException("Idle capture overrides an occupied tool hand");
            }
        }
        finally{held.Free();control.Free();}
        GD.Print($"CAPTURED_ARM_MOTION_PASS: source=CMU-143-30, poses={poses}, body frames=2, measured upper/lower/wrist frames, maximum direction/wrist error={maximumDirectionError:F6}/{maximumWristError:F6}, return step={maximumReturnStep:F4} m, actual sleeve stretch={maximumSurfaceStretch:F3}; real idle playback, occupied-hand priority, pause and LOD");
    }
    private float ValidateCapturedSurface(ResidentRig rig)
    {
        var arrays=rig.DetailedBodyMesh.SurfaceGetArrays(0);var source=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();var pattern=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var corrected=CorrectedSleeveSource(rig,source);var posed=new Vector3[source.Length];var palette=new Transform3D[ResidentRig.SupportedBodyBones];
        for(int i=0;i<palette.Length;i++)palette[i]=rig.ShoulderSkeleton.GetBonePose(i)*rig.DetailedBodySkin.GetBindPose(i);
        for(int i=0;i<source.Length;i++)
        {
            for(int j=0;j<4;j++)posed[i]+=palette[bones[i*4+j]]*corrected[i]*weights[i*4+j];
            if(!posed[i].IsFinite())throw new InvalidOperationException("Captured action creates non-finite skin");
            if(pattern[i].X>.2629f&&posed[i].DistanceTo(palette[source[i].X<0?1:2]*source[i])>.000002f)throw new InvalidOperationException("Captured action separates the cuff");
        }
        float maximum=0;int worstA=0,worstB=0;
        for(int i=0;i<indices.Length;i+=3)for(int j=0;j<3;j++)
        {
            int a=indices[i+j],b=indices[i+(j+1)%3];float length=source[a].DistanceTo(source[b]);if(source[a].Y<1||source[b].Y<1||length<.00001f)continue;
            float ratio=posed[a].DistanceTo(posed[b])/length;if(ratio>maximum){maximum=ratio;worstA=a;worstB=b;}
        }
        if(maximum>4.5f)
        {
            Vector3 Raw(int i){var p=Vector3.Zero;for(int j=0;j<4;j++)p+=palette[bones[i*4+j]]*source[i]*weights[i*4+j];return p;}
            throw new InvalidOperationException($"Captured motion tears the actual cloth: stretch={maximum}, raw={Raw(worstA).DistanceTo(Raw(worstB))/source[worstA].DistanceTo(source[worstB])}, seam={pattern[worstA]}/{pattern[worstB]}");
        }
        return maximum;
    }
}
