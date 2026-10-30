using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    private readonly Vector3[] _freeShoulder=new Vector3[2],_freeShoulderVelocity=new Vector3[2];
    private readonly Vector3[] _freeForearm=new Vector3[2],_freeForearmVelocity=new Vector3[2];

    private void PoseFreeArm(int side,float dt,float identityPhase)
    {
        float mirror=side==0?1:-1;
        // Opposite arm and leg, with a small forearm lag. Elbow flexion grows
        // during the forward arm excursion, not while the arm is behind us.
        float phase=_gaitCycle*MathF.Tau+side*MathF.PI;
        float forward=-MathF.Cos(phase-.10f),lag=-MathF.Cos(phase-.38f);
        float speedScale=.74f+.24f*MathF.Min(_gaitSpeed,1.7f);
        float amplitude=(GaitArmSwing*.74f+GaitIndividual*.012f)*(1-.12f*_workFatigue)*speedScale;
        float outside=.040f+ArmRestClearance*.50f+.010f*_activity;
        var shoulderTarget=new Vector3(-.025f+forward*amplitude*_activity,-mirror*.055f,-mirror*outside);
        float follow=.045f+.090f*lag+.014f*MathF.Cos(phase*2-.80f);
        var forearmTarget=new Vector3(.17f+follow*_activity*speedScale,mirror*1.40f,0);
        // Filter free-motion targets independently of solved interaction joints.
        // This retains momentum without pulling an attached tool off its grip.
        DampMotion(ref _freeShoulder[side],ref _freeShoulderVelocity[side],shoulderTarget,.055f,dt);
        DampMotion(ref _freeForearm[side],ref _freeForearmVelocity[side],forearmTarget,.065f,dt);
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
