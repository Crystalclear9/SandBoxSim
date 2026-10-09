using System;
using Godot;

namespace SandBoxSim.Client;
internal partial class AnimalRig : Node3D
{
    public Node3D Body = null!, Head = null!, Tail = null!;
    public readonly Node3D[] Legs = new Node3D[4], Knees = new Node3D[4];
    public readonly Node3D[] Ears=new Node3D[2];
    public readonly Vector3[] EarRest=new Vector3[2];
    private float _phase, _stride;
    public void SetIdentity(int identity)=>_phase=(identity%97)*.371f;
    public void Pose(float delta, bool moving, bool paused)
    {
        if (paused) return;
        _phase += delta * (moving ? 8 : 1.6f);
        _stride = Mathf.Lerp(_stride, moving ? 1 : 0, 1 - MathF.Exp(-delta * 9));
        Body.Position = new Vector3(0, MathF.Abs(MathF.Sin(_phase)) * .022f * _stride, 0);
        float breath=MathF.Sin(_phase*.65f)*.004f*(1-_stride);
        Body.Scale=new Vector3(1+breath,1+breath*.5f,1);
        Head.Rotation = new Vector3(MathF.Sin(_phase * .45f) * .025f, MathF.Sin(_phase * .29f) * .065f * (1 - _stride), 0);
        Tail.Rotation = new Vector3(0, 0, MathF.Sin(_phase * .6f) * .12f);
        for(int side=0;side<2;side++)
            Ears[side].Rotation=EarRest[side]+new Vector3(MathF.Sin(_phase*.61f+side*1.7f)*.06f,MathF.Sin(_phase*.39f+side*2.1f)*.10f*(1-_stride),0);
        for (int i = 0; i < 4; i++)
        {
            float wave = MathF.Sin(_phase + (i is 0 or 3 ? 0 : MathF.PI)) * _stride;
            Legs[i].Rotation = new Vector3(wave * .36f, 0, 0);
            Knees[i].Rotation = new Vector3(MathF.Max(0, -wave) * .5f, 0, 0);
        }
    }
}
