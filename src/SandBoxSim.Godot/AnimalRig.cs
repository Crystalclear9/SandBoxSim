using System;
using Godot;

namespace SandBoxSim.Client;
internal partial class AnimalRig : Node3D
{
    public Node3D Body = null!, Head = null!, Tail = null!,Neck=null!;
    public bool Predator;
    internal Vector3[]? RestSoles;
    internal Node3D? Jaw;
    internal Node3D? TailMiddle,TailTip;
    internal ShaderMaterial? EyeMaterial,LidMaterial;
    internal MeshInstance3D? AuthoredEyelids;
    internal float Blink {get;private set;}
    private float _blinkClock,_blinkDelay=2.8f;
    private int _blinkSequence;
    public readonly Node3D[] Legs = new Node3D[4], Knees = new Node3D[4];
    internal readonly Node3D?[] Paws=new Node3D?[4];
    internal readonly float[] PawLift=new float[4];
    internal static (float Travel,float Lift) GaitContact(float phase,bool wolf)
    {
        float cycle=Mathf.PosMod(phase/Mathf.Tau,1),duty=wolf?.58f:.74f;
        if(cycle<duty)return(Mathf.Lerp(-.14f,.14f,cycle/duty),0);
        float t=(cycle-duty)/(1-duty),smooth=t*t*(3-2*t);
        return(Mathf.Lerp(.14f,-.14f,smooth),.075f*MathF.Sin(MathF.PI*t));
    }
    internal Vector3 ContactSole(int i)
    {
        var sole=RestSoles![i];var chain=Legs[i].Transform*Knees[i].Transform;
        if(Paws[i] is { } paw)return Body.Transform*(chain*paw.Transform*(sole-paw.Position));
        return Body.Transform*(chain*sole);
    }
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
            var sole=RestSoles==null?new Vector3(0,-lowerLength+.007f,-.025f):RestSoles[i];
            float phase=_phase+(i is 0 or 3?0:MathF.PI);
            float swing=MathF.Max(0,MathF.Sin(phase))*.075f*_stride;
            float travel=MathF.Cos(phase)*.14f*_stride;
            if(Paws[i]!=null)
            {
                if(!Predator)phase=_phase+(i==0?0:i==1?MathF.PI:i==2?MathF.PI/2:MathF.PI*1.5f);
                var contact=GaitContact(phase,Predator);swing=contact.Lift*_stride;travel=contact.Travel*_stride;
            }
            PawLift[i]=swing;
            var target=Body.Transform*(hip+upper+sole+new Vector3(0,0,travel));
            target.Y=surfaceHeight(target)+.007f+swing;
            target=Body.Transform.AffineInverse()*target;
            // Authored quadrupeds have different rest-foot spacing. A wider
            // vertical window accommodates both feet after body compensation;
            // the subsequent segment-length clamp still forbids stretching.
            float slopeLimit=RestSoles==null?.10f:.18f;
            target.Y=Math.Clamp(target.Y,.007f-slopeLimit,.007f+slopeLimit+swing);
            var paw=Paws[i];var desiredPaw=Basis.Identity;
            var lower=sole;
            if(paw!=null)
            {
                var sample=Body.Transform*target;const float h=.02f;
                float dx=(surfaceHeight(sample+Vector3.Right*h)-surfaceHeight(sample-Vector3.Right*h))/(2*h);
                float dz=(surfaceHeight(sample+Vector3.Back*h)-surfaceHeight(sample-Vector3.Back*h))/(2*h);
                var normal=new Vector3(-dx,1,-dz).Normalized();
                var forward=(Vector3.Forward-normal*Vector3.Forward.Dot(normal)).Normalized();
                var surfaceFrame=new Basis(forward.Cross(normal).Normalized(),normal,-forward);
                desiredPaw=Body.Basis.Orthonormalized().Inverse()*surfaceFrame;
                float lift=swing/.075f;desiredPaw*=new Basis(Vector3.Right,.18f*lift);
                lower=paw.Position;target-=desiredPaw*(sole-lower);
            }
            var delta=target-hip;float a=upper.Length(),b=lower.Length();
            float length=Math.Clamp(delta.Length(),MathF.Abs(a-b)+.002f,a+b-.0001f);
            var axis=delta.Normalized();target=hip+axis*length;
            var pole=i<2?Vector3.Back:Vector3.Forward;
            if(RestSoles!=null)
            {
                var restAxis=(upper+lower).Normalized();
                var authoredBend=upper-restAxis*upper.Dot(restAxis);
                if(authoredBend.LengthSquared()>.000001f)pole=authoredBend.Normalized();
            }
            var bend=(pole-axis*pole.Dot(axis)).Normalized();
            float along=(a*a-b*b+length*length)/(2*length);
            var knee=hip+axis*along+bend*MathF.Sqrt(MathF.Max(0,a*a-along*along));
            var thighBasis=new Basis(new Quaternion(upper.Normalized(),(knee-hip).Normalized()));
            var shinBasis=new Basis(new Quaternion(lower.Normalized(),(target-knee).Normalized()));
            Legs[i].Basis=thighBasis;Knees[i].Basis=thighBasis.Inverse()*shinBasis;
            if(paw!=null)
            {
                paw.Basis=shinBasis.Inverse()*desiredPaw;
                FurSkeleton?.SetBonePoseRotation((Predator?15:14)+i,paw.Basis.GetRotationQuaternion());
            }
            FurSkeleton?.SetBonePoseRotation(3+i*2,Legs[i].Basis.GetRotationQuaternion());
            FurSkeleton?.SetBonePoseRotation(4+i*2,Knees[i].Basis.GetRotationQuaternion());
        }
    }
    public void SetIdentity(int identity)
    {
        _phase=(identity%97)*.371f;_blinkClock=(identity%31)*.071f;
        _blinkSequence=identity%29;_blinkDelay=2.8f+(identity%11)*.13f;
    }
    private void UpdateBlink(float delta)
    {
        _blinkClock+=delta;float next=0;
        if(_blinkClock>=_blinkDelay)
        {
            float t=(_blinkClock-_blinkDelay)/.22f;
            if(t>=1){_blinkClock=0;_blinkDelay=2.8f+(++_blinkSequence%17)*.13f;}
            else next=MathF.Sin(t*MathF.PI);
        }
        if(MathF.Abs(next-Blink)<.00001f)return;
        Blink=next;EyeMaterial?.SetShaderParameter("blink",Blink);LidMaterial?.SetShaderParameter("blink",Blink);
        if(AuthoredEyelids!=null)AuthoredEyelids.Visible=Blink>.001f;
    }
    public void Pose(float delta, bool moving, bool paused,bool grazing=false,Func<Vector3,float>? ground=null)
    {
        if (paused) return;
        UpdateBlink(delta);
        _phase += delta * (moving ? 8 : 1.6f);
        _stride = Mathf.Lerp(_stride, moving ? 1 : 0, 1 - MathF.Exp(-delta * 9));
        _bodyBob=MathF.Abs(MathF.Sin(_phase)) * .022f * _stride;
        Body.Position = new Vector3(0,_bodyBob,0);
        float breath=MathF.Sin(_phase*.65f)*.004f*(1-_stride);
        Body.Scale=new Vector3(1+breath,1+breath*.5f,1);
        // Locomotion owns the neck while travelling. A simulator Eat state
        // can precede the rendered root's arrival at its grazing location.
        _graze=Mathf.Lerp(_graze,grazing&&!Predator&&!moving?1:0,1-MathF.Exp(-delta*2.4f));
        Neck.Rotation=new(-1.55f*_graze+MathF.Sin(_phase*.37f)*.018f*(1-_stride),0,0);
        FurSkeleton?.SetBonePoseRotation(11,Neck.Basis.GetRotationQuaternion());
        Head.Rotation = new Vector3(.20f*_graze+MathF.Sin(_phase * .45f) * .025f, MathF.Sin(_phase * .29f) * .065f * (1 - _stride), 0);
        Tail.Rotation = new Vector3(0, 0, MathF.Sin(_phase * .6f) * .12f);
        FurSkeleton?.SetBonePoseRotation(1,Head.Basis.GetRotationQuaternion());
        FurSkeleton?.SetBonePoseRotation(2,Tail.Basis.GetRotationQuaternion());
        if(TailMiddle!=null)
        {
            TailMiddle.Rotation=new(0,MathF.Sin(_phase*.6f-.35f)*.025f,MathF.Sin(_phase*.6f-.45f)*.09f);
            TailTip!.Rotation=new(0,MathF.Sin(_phase*.6f-.7f)*.025f,MathF.Sin(_phase*.6f-.9f)*.08f);
            FurSkeleton?.SetBonePoseRotation(19,TailMiddle.Basis.GetRotationQuaternion());FurSkeleton?.SetBonePoseRotation(20,TailTip.Basis.GetRotationQuaternion());
        }
        if(Jaw!=null)
        {
            Jaw.Rotation=new Vector3(Mathf.Lerp(.16f,.07f+MathF.Sin(_phase*.5f)*.025f,_stride),0,0);
            FurSkeleton?.SetBonePoseRotation(14,Jaw.Basis.GetRotationQuaternion());
        }
        for(int side=0;side<2;side++)
        {
            Ears[side].Rotation=EarRest[side]+new Vector3(MathF.Sin(_phase*.61f+side*1.7f)*.06f,MathF.Sin(_phase*.39f+side*2.1f)*.10f*(1-_stride),0);
            if(RestSoles!=null)FurSkeleton?.SetBonePoseRotation(12+side,Ears[side].Basis.GetRotationQuaternion());
        }
        FitFeet(ground??FlatGround);
    }
    private static float FlatGround(Vector3 point)=>0;
}
