using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    // Art-directed profiles informed by gait research, not a sex classifier.
    // Speed remains independent of appearance; individual variation overlaps.
    internal float GaitPelvisYaw=.030f,GaitPelvisRoll=.012f,GaitArmSwing=.26f,GaitIndividual;
    internal Vector3[] BootSoleCurve=Array.Empty<Vector3>();
    private float _gaitCycle,_gaitSpeed,_locomotionSpeed=1.05f,_turnDemand,_gaitTurn,_sampleDistance,_sampleTime,_locomotionScale=1;
    private bool _worldLocomotion,_groundContactSpeed;
    private float _approachDistance;
    internal bool InteractionSettled=>!_worldLocomotion||(_approachDistance<.035f&&_gaitSpeed<.15f&&!_finishingStep&&!IsPivoting);
    private Vector3 _gaitOffset;
    private Vector3 _capturedChest,_capturedPelvis;
    private readonly Transform3D[] _arrivalFeet=new Transform3D[2];
    private readonly Vector3[] _groundContact=new Vector3[2];
    private readonly bool[] _groundContactValid=new bool[2];
    private bool _finishingStep,_arrivalArmed;
    private int _landingFirst;
    private float _arrivalTime,_arrivalWeight;
    private const float LandingDuration=.22f;
    internal Basis GaitPelvisBasis=Basis.Identity;
    internal float GaitCycle=>_gaitCycle;
    internal float GaitSpeed=>_gaitSpeed;
    internal const float MaximumGaitSpeed=1.70f;
    private float GaitSupport=>.632f-.055f*Flow+.02f*_workFatigue+(_hasCargo?.025f:0);
    private float GaitFootHalfWidth=>MathF.Max(.048f,HipHalfWidth+.026f-.062f*Flow);
    // Phase is distance driven. Scale the foot's stance travel by the very
    // same cycle length so a quicker cadence does not increase ground speed.
    private float GaitCycleLength(float speed)=>(.76f+.10f*MathF.Min(1.7f,speed))*(1.13f-.26f*Flow)
        *(1-.10f*_workFatigue)*(_hasCargo?.93f:1);

    // Called every rendered world frame, before the distance pose scheduler.
    // Discontinuous tile resynchronisation is not interpreted as a sprint.
    public void ObserveLocomotion(Vector3 displacement,float delta,float headingError,bool paused,float remainingDistance=0,float scale=1)
    {
        if(paused||!float.IsFinite(delta)||delta<=0)return;
        _worldLocomotion=true;
        _locomotionScale=scale;
        float distance=new Vector2(displacement.X,displacement.Z).Length(),speed=distance/delta;
        // An adjacent tile's ordinary exponential interpolation can have a
        // high instantaneous speed. Only reject a large spatial jump, or its
        // first frames would incorrectly suppress the starting step.
        float sample=float.IsFinite(speed)&&distance<1.25f?Math.Clamp(speed,0,MaximumGaitSpeed):0;
        _groundContactSpeed=float.IsFinite(speed)&&speed<=MaximumGaitSpeed+.08f;
        if(distance>=1.25f)
        {
            _arrivalArmed=_finishingStep=false;_arrivalWeight=0;
            ResetGroundContacts();
        }
        _sampleDistance+=sample*delta;_sampleTime+=delta;
        _approachDistance=float.IsFinite(remainingDistance)?MathF.Max(0,remainingDistance):0;
        _turnDemand=float.IsFinite(headingError)?Math.Clamp(headingError,-.65f,.65f):0;
    }

    private void ConsumeLocomotionSamples()
    {
        if(_sampleTime<=0){if(_worldLocomotion)_locomotionSpeed=0;return;}
        _locomotionSpeed=_sampleDistance/_sampleTime;
        _sampleDistance=_sampleTime=0;
    }

    private void AdvanceLocomotion(float dt,bool moving,bool canFinishStep)
    {
        // The rendered root can still approach its tile after the simulation
        // enters Executing. Keep stepping until that actual movement settles.
        float wanted=_worldLocomotion?_locomotionSpeed:moving?1.05f*(1-.12f*_workFatigue):0;
        // Preparatory weight transfer precedes the first full step; use the
        // exact exponential integral so accumulated distant updates keep time.
        float rate=wanted>_gaitSpeed?5:9,decay=MathF.Exp(-rate*dt),before=_gaitSpeed;
        _gaitSpeed=wanted+(before-wanted)*decay;
        AdvanceArrival(dt,wanted,canFinishStep);
        float travel=wanted*dt+(before-wanted)*(1-decay)/rate;
        // At cruising speed, use the observed root travel for contact timing.
        // The filtered speed still eases the first step and the final settle.
        float cruise=Mathf.SmoothStep(.20f,.80f,MathF.Min(wanted,_gaitSpeed));
        float distance=Mathf.Lerp(travel,wanted*dt,cruise);
        _gaitCycle=Mathf.PosMod(_gaitCycle+distance/(GaitCycleLength(_gaitSpeed)*_locomotionScale),1);
        _activity=Mathf.SmoothStep(0,.80f,_gaitSpeed);
        if(!moving&&_gaitSpeed<.00001f){_gaitSpeed=0;_activity=0;}
        _gaitTurn=Mathf.Lerp(_gaitTurn,moving?_turnDemand:0,1-MathF.Exp(-dt*7));
        float a=_gaitCycle*MathF.Tau,load=_hasCargo?.72f:1;
        if(_activity<=0)
        {
            _gaitOffset=_capturedChest=_capturedPelvis=Vector3.Zero;
            GaitPelvisBasis=Basis.Identity;
            return;
        }
        (_capturedPelvis,_capturedChest)=ResidentGaitCapture.Shared.Body(_gaitCycle,Flow);
        // The body moves towards the supporting leg just after the weight
        // handoff. The flowing stride carries more motion in the pelvis.
        float support=MathF.Sin(a-(GaitSupport-.5f)*MathF.PI);
        _gaitOffset=new(-support*(.019f+.012f*Flow)*_activity*load,
            WalkingHipHeight()*_activity,0);
        float yaw=Mathf.Lerp(-MathF.Cos(a-.18f*Flow),_capturedPelvis.X,.67f);
        float roll=Mathf.Lerp(-support,_capturedPelvis.Y,.62f);
        GaitPelvisBasis=Basis.FromEuler(new(0,yaw*GaitPelvisYaw*(.76f+.76f*Flow)*_activity*load,
            roll*GaitPelvisRoll*(.78f+1.25f*Flow)*_activity));
    }

    private (float Z,float Lift,float Pitch) WalkingFootPath(int side)
    {
        float t=Mathf.PosMod(_gaitCycle+side*.5f,1),support=GaitSupport;
        float span=GaitCycleLength(_gaitSpeed)*support*.5f*
            (1+(side==0?-1:1)*_gaitTurn*.08f);
        float z,lift,pitch;
        if(t<support)
        {
            float u=t/support;z=Mathf.Lerp(-span,span,u);lift=0;
            pitch=(.16f-.045f*Flow)*(1-ReachEase(u/(.23f-.055f*Flow)))
                -(.22f+.075f*Flow)*ReachEase((u-(.66f-.07f*Flow))/(.34f+.07f*Flow));
        }
        else
        {
            float u=(t-support)/(1-support),arc=MathF.Sin(MathF.PI*u);
            // The swing reverses progressively. Match the stance tangent
            // at toe-off and heel contact instead of braking in one frame.
            const float edge=.145f;
            float near=MathF.Min(u,1-u);
            float v=near/edge;
            float progress=near<edge?edge*v*v*v*(1-.5f*v):(near-.5f*edge);
            progress/=1-edge;
            if(u>.5f)progress=1-progress;
            z=span+2*span/support*((1-support)*u-progress);
            float capture=ResidentGaitCapture.Shared.Lift(side,u,Flow)*ReachEase(u/.15f)*ReachEase((1-u)/.13f);
            lift=(.079f+.025f*Flow)*Mathf.Lerp(arc*arc,capture,.64f);
            pitch=Mathf.Lerp(-(.22f+.075f*Flow),.16f-.045f*Flow,ReachEase(u));
        }
        return (z,lift,pitch);
    }

    private float WalkingHipHeight()
    {
        // Let the planted leg approach extension at mid-stance. A permanent
        // downward sine offset made every step look like a crouched march.
        float height=HipHeight;
        float reach=ThighLength+ShinLength-.004f;
        float width=GaitFootHalfWidth-HipHalfWidth;
        for(int side=0;side<2;side++)
        {
            var foot=WalkingFootPath(side);var offset=new Basis(Vector3.Right,foot.Pitch)*FootCenterOffset;
            float z=foot.Z-.04f-offset.Z;
            float sole=.065f;
            foreach(var p in BootSoleCurve)sole=MathF.Max(sole,-p.Y*MathF.Cos(foot.Pitch)+p.Z*MathF.Sin(foot.Pitch));
            // The approaching swing foot also constrains reach. Waiting for
            // its contact flag caused an abrupt pelvis drop at heel strike.
            height=BlendReachCeiling(height,sole+foot.Lift-offset.Y+MathF.Sqrt(MathF.Max(.05f,reach*reach-z*z-width*width)));
        }
        // A quiet double-support arc avoids transmitting the swing foot's
        // rapid clearance lift into the whole torso. Reach remains a ceiling.
        float rise=Mathf.Lerp(-MathF.Cos(_gaitCycle*MathF.Tau*2),_capturedPelvis.Z,.42f);
        float arc=-.030f+(.026f-.008f*Flow)*rise;
        return BlendReachCeiling(arc,height-HipHeight-.002f);
    }

    private static float BlendReachCeiling(float first,float second)
    {
        const float transition=.018f;
        float overlap=MathF.Max(0,transition-MathF.Abs(first-second));
        return MathF.Min(first,second)-overlap*overlap/(4*transition);
    }

    private Vector3 RestFootCenter(int side)=>new(side==0?-HipHalfWidth:HipHalfWidth,
        HipHeight-ThighLength-ShinLength,-.04f);

    private void PoseWalkingLeg(int side,float weight)
    {
        if(weight<=.000001f)return;
        var (z,lift,pitch)=WalkingFootPath(side);
        float sign=side==0?-1:1;
        float phase=Mathf.PosMod(_gaitCycle+side*.5f,1),support=GaitSupport;
        float steering=phase<support?1-Smooth(phase/support):Smooth((phase-support)/(1-support));
        float stepTurn=_gaitTurn*steering*weight;
        float x=sign*MathF.Max(.038f,GaitFootHalfWidth+sign*stepTurn*.085f);
        // Rotate about the actual boot sole's contact, not the ankle in air.
        pitch*=weight;
        float ankleHeight=.065f,sin=MathF.Sin(pitch),cos=MathF.Cos(pitch);
        foreach(var point in BootSoleCurve)ankleHeight=MathF.Max(ankleHeight,-point.Y*cos+point.Z*sin);
        var baseline=Legs[side].Transform*Knees[side].Transform*Feet[side].Transform;
        var ankle=baseline.Origin.Lerp(new Vector3(x,ankleHeight+lift,-.04f+z),weight);
        ankle.Y=MathF.Max(ankle.Y,ankleHeight+lift*weight);
        // Blend the target before IK. Blending solved knee angles instead
        // shortens the chain's vertical reach and pushes slow feet underground.
        SolveLegTo(side,ankle,new Basis(Vector3.Up,stepTurn*.44f)*new Basis(Vector3.Right,pitch));
    }

    private Transform3D DisplayedFoot(int side)=>Legs[side].Transform*Knees[side].Transform*Feet[side].Transform;

    private void AdvanceArrival(float delta,float wanted,bool canFinishStep)
    {
        if(!_worldLocomotion)return;
        if(wanted>.35f)_arrivalArmed=true;
        if(!canFinishStep||wanted>.22f||_approachDistance>.07f)
        {
            _finishingStep=false;
            _arrivalWeight=Mathf.MoveToward(_arrivalWeight,0,delta/.14f);
            return;
        }
        if(_arrivalArmed&&!_finishingStep&&wanted<.135f)
        {
            _arrivalArmed=false;
            _arrivalFeet[0]=DisplayedFoot(0);
            _arrivalFeet[1]=DisplayedFoot(1);
            _landingFirst=_arrivalFeet[0].Origin.Y>=_arrivalFeet[1].Origin.Y?0:1;
            _arrivalWeight=1;_arrivalTime=0;_finishingStep=true;
        }
        if(!_finishingStep)return;
        _arrivalTime=MathF.Min(2*LandingDuration,_arrivalTime+delta);
        if(_arrivalTime>=2*LandingDuration)
        {
            _finishingStep=false;
            _arrivalWeight=0;
        }
    }

    private void PoseArrivalLeg(int side)
    {
        if(_arrivalWeight<=0)return;
        float u=Math.Clamp(_arrivalTime/LandingDuration-(side==_landingFirst?0:1),0,1);
        var rest=RestFootCenter(side);
        var from=_arrivalFeet[side];
        float clearance=Math.Clamp((rest-from.Origin).Length()*.40f,0,.045f);
        float arc=MathF.Sin(MathF.PI*u);
        float progress=ReachEase(u);
        var step=from.Origin.Lerp(rest,progress)+Vector3.Up*(clearance*arc*arc);
        var foot=DisplayedFoot(side);
        var rotation=from.Basis.GetRotationQuaternion().Slerp(Quaternion.Identity,progress);
        var basis=new Basis(foot.Basis.GetRotationQuaternion().Slerp(rotation,_arrivalWeight));
        SolveLegTo(side,foot.Origin.Lerp(step,_arrivalWeight),basis);
    }

    internal void ResetGroundContacts()=>Array.Clear(_groundContactValid);

    internal Vector3 GroundedFootPoint(int side,Vector3 position)
    {
        float phase=Mathf.PosMod(_gaitCycle+side*.5f,1),support=GaitSupport;
        if(!_groundContactSpeed||_activity<.58f||_arrivalWeight>0||phase>=support)
        {
            _groundContactValid[side]=false;
            return position;
        }
        if(!_groundContactValid[side])
        {
            _groundContactValid[side]=true;
            _groundContact[side]=position;
        }
        var pull=_groundContact[side]-position;
        pull.Y=0;
        float distance=pull.Length();
        // Retain a planted contact across a small steering correction. Large
        // errors are released progressively so the leg never chases a stale pin.
        float weight=ReachEase(phase/.14f)*ReachEase((support-phase)/.16f)*
            (1-Mathf.SmoothStep(.045f,.13f,distance));
        return position+pull*weight;
    }

    private void SolveLegTo(int side,Vector3 footCenter,Basis soleBasis)
    {
        var leg=Legs[side];
        var target=footCenter-soleBasis*FootCenterOffset-leg.Position;
        float roll=MathF.Atan2(target.X,-target.Y);var lateral=new Basis(Vector3.Back,roll);
        var sagittal=lateral.Inverse()*target;float lower=ShinLength;
        float distance=Math.Clamp(sagittal.Length(),.12f,ThighLength+lower);
        sagittal=sagittal.Normalized()*distance;
        float hip=MathF.Atan2(-sagittal.Z,-sagittal.Y)+MathF.Acos(Math.Clamp(
            (ThighLength*ThighLength+distance*distance-lower*lower)/(2*ThighLength*distance),-1,1));
        var solved=lateral*new Basis(Vector3.Right,hip);
        var remainder=sagittal-new Basis(Vector3.Right,hip)*new Vector3(0,-ThighLength,0);
        float knee=MathF.Atan2(-remainder.Z,-remainder.Y)-hip;
        leg.Basis=solved;
        Knees[side].Rotation=new(knee,0,0);
        SetFootOrientation(side,(solved*new Basis(Vector3.Right,knee)).Inverse()*soleBasis);
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
