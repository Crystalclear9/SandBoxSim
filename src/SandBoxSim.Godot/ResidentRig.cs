using System;
using Godot;

namespace SandBoxSim.Client;

/// <summary>Visual joints only. Poses never consume the simulation's time or random streams.</summary>
internal partial class ResidentRig : Node3D
{
    public Node3D Torso = null!, Head = null!;
    public readonly Node3D[] Arms = new Node3D[2], Elbows = new Node3D[2], Legs = new Node3D[2], Knees = new Node3D[2];
    private float _phase, _activity;
    public void Pose(float delta, bool moving, bool working, bool resting, bool paused, float identityPhase)
    {
        if (paused) { return; }
        _phase += delta * (moving ? 7.5f : working ? 4f : 1.8f);
        _activity = Mathf.Lerp(_activity, moving ? 1 : 0, 1 - MathF.Exp(-delta * 8));
        float wave = MathF.Sin(_phase + identityPhase), breath = MathF.Sin(_phase * .5f + identityPhase);
        Torso.Position = new Vector3(0, resting ? -.09f : MathF.Abs(wave) * .025f * _activity + breath * .003f, 0);
        Torso.Rotation = new Vector3(resting ? .16f : working ? .12f : .025f * _activity, 0, wave * .018f * _activity);
        Head.Rotation = new Vector3(breath * .018f, MathF.Sin(_phase * .32f + identityPhase) * .045f, 0);
        for (int i = 0; i < 2; i++)
        {
            float stride = wave * (i == 0 ? 1 : -1) * _activity;
            Legs[i].Rotation = new Vector3(stride * .44f, 0, 0);
            Knees[i].Rotation = new Vector3(MathF.Max(0, -stride) * .65f, 0, 0);
            Arms[i].Rotation = new Vector3(working ? -.65f + wave * .18f : -stride * .32f, 0, i == 0 ? .055f : -.055f);
            Elbows[i].Rotation = new Vector3(working ? -.75f : -.12f - MathF.Max(0, stride) * .22f, 0, 0);
        }
    }
}
