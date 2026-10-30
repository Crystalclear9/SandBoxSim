using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int SleevePoseCount=12;
    private readonly float[] _sleevePoseWeights=new float[SleevePoseCount];
    internal static int SleevePoseIndex(int side,int plane,int height)=>side*6+plane*2+height;
    internal static float SleevePoseWeight(Vector3 direction,int plane,int height)
    {
        float elevation=MathF.Acos(Math.Clamp(direction.Dot(Vector3.Down),-1,1));
        float forward=MathF.Atan2(MathF.Abs(direction.Z),MathF.Abs(direction.X)+.000001f)/(MathF.PI*.5f);
        float high=Mathf.SmoothStep(Mathf.DegToRad(90),Mathf.DegToRad(135),elevation);
        float active=Mathf.SmoothStep(Mathf.DegToRad(15),Mathf.DegToRad(90),elevation);
        // These authored samples cover lateral, scapular-plane and forward raises. Do not
        // apply a forward cap correction to a reach behind the shoulder.
        float posterior=1-Mathf.SmoothStep(.02f,.25f,direction.Z);
        return active*posterior*Mathf.SmoothStep(0,1,MathF.Max(0,1-MathF.Abs(forward*2-plane)))*(height==0?1-high:high);
    }
    private void UpdateSleeveCorrectives(int side,bool force=false)
    {
        if(BodySkin.Mesh is not ArrayMesh mesh||mesh.GetBlendShapeCount()!=SleevePoseCount)return;
        var direction=-Arms[side].Basis.Y;
        for(int plane=0;plane<3;plane++)for(int height=0;height<2;height++)
        {
            int index=SleevePoseIndex(side,plane,height);float weight=SleevePoseWeight(direction,plane,height);
            if(force||MathF.Abs(weight-_sleevePoseWeights[index])>.00001f)
            {BodySkin.SetBlendShapeValue(index,weight);_sleevePoseWeights[index]=weight;}
        }
    }
}
