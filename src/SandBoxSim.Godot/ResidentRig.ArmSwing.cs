using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private readonly Vector3[] _freeShoulder=new Vector3[2],_freeShoulderVelocity=new Vector3[2];
    private readonly Vector3[] _freeForearm=new Vector3[2],_freeForearmVelocity=new Vector3[2];

    private void PoseFreeArm(int side,float dt,float identityPhase,float idleWeight,float hipShift,float turnSwing)
    {
        float mirror=side==0?1:-1;
        // Opposite arm and leg, with a small forearm lag. Elbow flexion grows
        // during the forward arm excursion, not while the arm is behind us.
        float phase=_gaitCycle*MathF.Tau+side*MathF.PI;
        float forward=-MathF.Cos(phase-(.03f+.22f*Flow)),lag=-MathF.Cos(phase-(.23f+.36f*Flow));
        var capture=_activity>0?ResidentGaitCapture.Shared.Arm(side,Mathf.PosMod(_gaitCycle+side*.5f,1),GaitSupport,Flow):Vector2.Zero;
        float speedScale=.74f+.24f*MathF.Min(_gaitSpeed,1.7f);
        float amplitude=(GaitArmSwing*(1.10f-.35f*Flow)+GaitIndividual*.012f)*(1-.12f*_workFatigue)*speedScale;
        float outside=.040f+ArmRestClearance*.50f+.010f*_activity;
        // A wider direct stroke and a narrower oval are separate spatial
        // paths, not just playback rates of one sagittal arm animation.
        outside+=(.052f*(1-Flow)+.025f*Flow*(.5f+.5f*MathF.Sin(phase-.46f)))*_activity;
        float sweep=Mathf.Lerp(forward,capture.X,.76f)*amplitude+(.027f-.015f*Flow)*MathF.Cos(2*phase-.24f);
        var shoulderTarget=new Vector3(-.025f+sweep*_activity,-mirror*.055f,-mirror*outside);
        float follow=.045f+(.065f+.080f*Flow)*Mathf.Lerp(lag,capture.Y,.70f)+.014f*MathF.Cos(phase*2-.80f);
        var forearmTarget=new Vector3(.17f+.057f*Flow+follow*_activity*speedScale,
            mirror*(1.40f+(.037f+.032f*Flow)*lag*_activity),0);
        float drift=MathF.Sin(_phase*.37f+identityPhase+side*1.24f)*idleWeight;
        shoulderTarget.X+=.034f*drift+turnSwing;
        shoulderTarget.Z-=.82f*hipShift*idleWeight;
        forearmTarget.X+=.027f*drift;
        // Filter free-motion targets independently of solved interaction joints.
        // This retains momentum without pulling an attached tool off its grip.
        float response=Mathf.Lerp(ArmFollowTime,.027f+.008f*Flow,_activity);
        DampMotion(ref _freeShoulder[side],ref _freeShoulderVelocity[side],shoulderTarget,response,dt);
        DampMotion(ref _freeForearm[side],ref _freeForearmVelocity[side],forearmTarget,response+.010f,dt);
        Arms[side].Rotation=_freeShoulder[side];
        // Axial roll belongs to the forearm. Rotating only the wrist produces
        // an artificial twist through the hand/forearm connection. This axis
        // passes through the actual wrist centre, so roll cannot displace it.
        Elbows[side].Basis=new Basis(Vector3.Right,_freeForearm[side].X)*
            new Basis(Hands[side].Position.Normalized(),_freeForearm[side].Y);
        Hands[side].Rotation=new(.012f+.010f*MathF.Sin(phase-.35f)*_activity,0,mirror*.018f);
        CurlHand(side,.30f+.020f*MathF.Sin(_phase*.4f+identityPhase+side));
    }
}
