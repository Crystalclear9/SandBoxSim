using System;
using Godot;

namespace SandBoxSim.Client;
internal partial class AnimalRig : Node3D
{
    public Node3D Body = null!, Head = null!, Tail = null!,Neck=null!;
    public bool Predator;
    public readonly Node3D[] Legs = new Node3D[4], Knees = new Node3D[4];
    public readonly Node3D[] Ears=new Node3D[2];
    public readonly Vector3[] EarRest=new Vector3[2];
    public Skeleton3D? FurSkeleton;
    public MeshInstance3D? FurSkin;
    private float _phase, _stride,_graze,_bodyBob;
    // Solve the four contact chains against the rendered surface. The body and
    // the fur skeleton use the same rotations; moving only the paw mesh would
    // tear it away from the continuous skin.
    public void FitFeet(Func<Vector3,float> surfaceHeight)
    {
        float lowest=0;
        for(int i=0;i<4;i++)lowest=MathF.Min(lowest,surfaceHeight(new Vector3(Legs[i].Position.X,0,Legs[i].Position.Z)));
        Body.Position=new(0,_bodyBob+Math.Clamp(lowest,-.10f,0)-.006f,0);
        for(int i=0;i<4;i++)
        {
            var hip=Legs[i].Position;
            var upper=Knees[i].Position;
            float lowerLength=hip.Y-.32f;
            var sole=new Vector3(0,-lowerLength+.007f,-.025f);
            float phase=_phase+(i is 0 or 3?0:MathF.PI);
            float swing=MathF.Max(0,MathF.Sin(phase))*.075f*_stride;
            var target=Body.Transform*(hip+upper+sole+new Vector3(0,0,MathF.Cos(phase)*.14f*_stride));
            target.Y=surfaceHeight(target)+.007f+swing;
            target=Body.Transform.AffineInverse()*target;
            // Limit slope adaptation to 10 cm so steep cliffs never stretch limbs.
            target.Y=Math.Clamp(target.Y,.007f-.10f,.007f+.10f+swing);
            var delta=target-hip;float a=upper.Length(),b=sole.Length();
            float length=Math.Clamp(delta.Length(),MathF.Abs(a-b)+.002f,a+b-.0001f);
            var axis=delta.Normalized();target=hip+axis*length;
            var pole=i<2?Vector3.Back:Vector3.Forward;
            var bend=(pole-axis*pole.Dot(axis)).Normalized();
            float along=(a*a-b*b+length*length)/(2*length);
            var knee=hip+axis*along+bend*MathF.Sqrt(MathF.Max(0,a*a-along*along));
            var thighBasis=new Basis(new Quaternion(upper.Normalized(),(knee-hip).Normalized()));
            var shinBasis=new Basis(new Quaternion(sole.Normalized(),(target-knee).Normalized()));
            Legs[i].Basis=thighBasis;Knees[i].Basis=thighBasis.Inverse()*shinBasis;
            FurSkeleton?.SetBonePoseRotation(3+i*2,Legs[i].Basis.GetRotationQuaternion());
            FurSkeleton?.SetBonePoseRotation(4+i*2,Knees[i].Basis.GetRotationQuaternion());
        }
    }
    public void SetIdentity(int identity)=>_phase=(identity%97)*.371f;
    public void Pose(float delta, bool moving, bool paused,bool grazing=false,Func<Vector3,float>? ground=null)
    {
        if (paused) return;
        _phase += delta * (moving ? 8 : 1.6f);
        _stride = Mathf.Lerp(_stride, moving ? 1 : 0, 1 - MathF.Exp(-delta * 9));
        _bodyBob=MathF.Abs(MathF.Sin(_phase)) * .022f * _stride;
        Body.Position = new Vector3(0,_bodyBob,0);
        float breath=MathF.Sin(_phase*.65f)*.004f*(1-_stride);
        Body.Scale=new Vector3(1+breath,1+breath*.5f,1);
        _graze=Mathf.Lerp(_graze,grazing&&!Predator?1:0,1-MathF.Exp(-delta*2.4f));
        Neck.Rotation=new(-1.55f*_graze+MathF.Sin(_phase*.37f)*.018f*(1-_stride),0,0);
        FurSkeleton?.SetBonePoseRotation(11,Neck.Basis.GetRotationQuaternion());
        Head.Rotation = new Vector3(.20f*_graze+MathF.Sin(_phase * .45f) * .025f, MathF.Sin(_phase * .29f) * .065f * (1 - _stride), 0);
        Tail.Rotation = new Vector3(0, 0, MathF.Sin(_phase * .6f) * .12f);
        FurSkeleton?.SetBonePoseRotation(1,Head.Basis.GetRotationQuaternion());
        FurSkeleton?.SetBonePoseRotation(2,Tail.Basis.GetRotationQuaternion());
        for(int side=0;side<2;side++)
            Ears[side].Rotation=EarRest[side]+new Vector3(MathF.Sin(_phase*.61f+side*1.7f)*.06f,MathF.Sin(_phase*.39f+side*2.1f)*.10f*(1-_stride),0);
        FitFeet(ground??FlatGround);
    }
    private static float FlatGround(Vector3 point)=>0;
}
