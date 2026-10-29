using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Art-directed profiles informed by gait research, not a sex classifier.
    // Speed remains independent of appearance; individual variation overlaps.
    internal float GaitPelvisYaw=.030f,GaitPelvisRoll=.012f,GaitArmSwing=.26f,GaitIndividual;
    internal Vector3[] BootSoleCurve=Array.Empty<Vector3>();
    private float _gaitCycle,_gaitSpeed,_locomotionSpeed=1.05f,_turnDemand,_gaitTurn,_sampleDistance,_sampleTime;
    private bool _worldLocomotion;
    private Vector3 _gaitOffset;
    internal Basis GaitPelvisBasis=Basis.Identity;
    internal float GaitCycle=>_gaitCycle;
    internal float GaitSpeed=>_gaitSpeed;
    private float GaitSupport=>.60f+.02f*_workFatigue+(_hasCargo?.025f:0);
    private float GaitCycleLength(float speed)=>(.76f+.10f*MathF.Min(1.7f,speed))*(1-.10f*_workFatigue)*(_hasCargo?.93f:1);

    // Called every rendered world frame, before the distance pose scheduler.
    // Discontinuous tile resynchronisation is not interpreted as a sprint.
    public void ObserveLocomotion(Vector3 displacement,float delta,float headingError,bool paused)
    {
        if(paused||!float.IsFinite(delta)||delta<=0)return;
        _worldLocomotion=true;
        float distance=new Vector2(displacement.X,displacement.Z).Length(),speed=distance/delta;
        // An adjacent tile's ordinary exponential interpolation can have a
        // high instantaneous speed. Only reject a large spatial jump, or its
        // first frames would incorrectly suppress the starting step.
        float sample=float.IsFinite(speed)&&distance<1.25f?Math.Clamp(speed,0,1.70f):0;
        _sampleDistance+=sample*delta;_sampleTime+=delta;
        _turnDemand=float.IsFinite(headingError)?Math.Clamp(headingError,-.65f,.65f):0;
    }

    private void ConsumeLocomotionSamples()
    {
        if(_sampleTime<=0)return;
        _locomotionSpeed=_sampleDistance/_sampleTime;
        _sampleDistance=_sampleTime=0;
    }

    private void AdvanceLocomotion(float dt,bool moving)
    {
        float wanted=moving?(_worldLocomotion?_locomotionSpeed:1.05f*(1-.12f*_workFatigue)):0;
        // Preparatory weight transfer precedes the first full step; use the
        // exact exponential integral so accumulated distant updates keep time.
        float rate=wanted>_gaitSpeed?5:9,decay=MathF.Exp(-rate*dt),before=_gaitSpeed;
        _gaitSpeed=wanted+(before-wanted)*decay;
        float travel=wanted*dt+(before-wanted)*(1-decay)/rate;
        _gaitCycle=Mathf.PosMod(_gaitCycle+travel/GaitCycleLength(wanted),1);
        _activity=Mathf.SmoothStep(0,.80f,_gaitSpeed);
        if(!moving&&_gaitSpeed<.00001f){_gaitSpeed=0;_activity=0;}
        _gaitTurn=Mathf.Lerp(_gaitTurn,moving?_turnDemand:0,1-MathF.Exp(-dt*7));
        float a=_gaitCycle*MathF.Tau,load=_hasCargo?.72f:1;
        _gaitOffset=new(-MathF.Sin(a)*(.011f+GaitPelvisRoll*.20f)*_activity,
            (-.047f-.024f*MathF.Cos(a*2))*_activity,0);
        GaitPelvisBasis=Basis.FromEuler(new(0,-MathF.Cos(a)*GaitPelvisYaw*_activity*load,
            -MathF.Sin(a)*GaitPelvisRoll*_activity));
    }

    private void PoseWalkingLeg(int side,float weight)
    {
        if(weight<=.000001f)return;
        float t=Mathf.PosMod(_gaitCycle+side*.5f,1),support=GaitSupport;
        float span=GaitCycleLength(_gaitSpeed)*support*.5f*
            (1+(side==0?-1:1)*_gaitTurn*.08f);
        float z,lift,pitch;
        if(t<support)
        {
            float u=t/support;z=Mathf.Lerp(-span,span,u);lift=0;
            pitch=.13f*(1-Mathf.SmoothStep(0,.19f,u))-.24f*Mathf.SmoothStep(.68f,1,u);
        }
        else
        {
            float u=(t-support)/(1-support),arc=MathF.Sin(MathF.PI*u);
            z=Mathf.Lerp(span,-span,Smooth(u));lift=.082f*arc*arc;
            pitch=Mathf.Lerp(-.24f,.13f,Smooth(u));
        }
        float x=(side==0?-1:1)*(HipHalfWidth-.012f);
        // Rotate about the actual boot sole's contact, not the ankle in air.
        pitch*=weight;
        float ankleHeight=.065f,sin=MathF.Sin(pitch),cos=MathF.Cos(pitch);
        foreach(var point in BootSoleCurve)ankleHeight=MathF.Max(ankleHeight,-point.Y*cos+point.Z*sin);
        var baseline=Legs[side].Transform*Knees[side].Transform*Feet[side].Transform;
        var ankle=baseline.Origin.Lerp(new Vector3(x,ankleHeight+lift,-.04f+z),weight);
        ankle.Y=MathF.Max(ankle.Y,ankleHeight+lift*weight);
        // Blend the target before IK. Blending solved knee angles instead
        // shortens the chain's vertical reach and pushes slow feet underground.
        SolveLegTo(side,ankle,new Basis(Vector3.Right,pitch));
    }

    private void SolveLegTo(int side,Vector3 ankle,Basis soleBasis)
    {
        var leg=Legs[side];
        var target=ankle-leg.Position;
        float roll=MathF.Atan2(target.X,-target.Y);var lateral=new Basis(Vector3.Back,roll);
        var sagittal=lateral.Inverse()*target;float lower=MathF.Sqrt(ShinLength*ShinLength+.04f*.04f);
        float distance=Math.Clamp(sagittal.Length(),.12f,ThighLength+lower-.0001f);
        sagittal=sagittal.Normalized()*distance;
        float hip=MathF.Atan2(-sagittal.Z,-sagittal.Y)+MathF.Acos(Math.Clamp(
            (ThighLength*ThighLength+distance*distance-lower*lower)/(2*ThighLength*distance),-1,1));
        var solved=lateral*new Basis(Vector3.Right,hip);
        var remainder=sagittal-new Basis(Vector3.Right,hip)*new Vector3(0,-ThighLength,0);
        float knee=MathF.Atan2(-remainder.Z,-remainder.Y)-MathF.Atan2(.04f,ShinLength)-hip;
        leg.Basis=solved;
        Knees[side].Rotation=new(knee,0,0);
        Feet[side].Basis=(solved*new Basis(Vector3.Right,knee)).Inverse()*soleBasis;
    }

    internal void ConformFoot(int side,Vector3 offset,Vector3 groundNormal)
    {
        var foot=Legs[side].Transform*Knees[side].Transform*Feet[side].Transform;
        var normal=groundNormal.Normalized();
        var tangent=foot.Basis.Z.Slide(Vector3.Up).Normalized().Slide(normal).Normalized();
        if(tangent.LengthSquared()<.1f)tangent=Vector3.Back.Slide(normal).Normalized();
        var terrain=new Basis(normal.Cross(tangent).Normalized(),normal,tangent);
        float pitch=Math.Clamp(MathF.Atan2(-foot.Basis.Z.Y,foot.Basis.Z.Slide(Vector3.Up).Length()),-.4f,.4f);
        SolveLegTo(side,foot.Origin+offset,terrain*new Basis(Vector3.Right,pitch));
    }
}
