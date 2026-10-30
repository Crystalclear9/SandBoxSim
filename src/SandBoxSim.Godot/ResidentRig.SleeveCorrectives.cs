using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int SleevePosesPerSide=18,SleevePoseCount=2*SleevePosesPerSide;
    private readonly float[] _sleevePoseWeights=new float[SleevePoseCount];
    internal static int SleevePoseIndex(int side,int plane,int height,int roll)=>side*SleevePosesPerSide+plane*6+height*3+roll;
    internal static float SleeveNeutralRoll(Vector3 direction,int side)
    {
        var horizontal=new Vector3(direction.X,0,direction.Z);
        if(horizontal.LengthSquared()<.000001f)horizontal=new Vector3(side==0?-1:1,0,0);
        var axis=Vector3.Down.Cross(horizontal.Normalized());
        var swing=new Basis(new Quaternion(Vector3.Down,direction));
        return Math.Clamp(swing.X.SignedAngleTo(axis,direction),-SleeveRollSampleLimit,SleeveRollSampleLimit);
    }
    internal static float SleeveRollSample(Vector3 direction,int side,int roll)=>Math.Clamp(SleeveNeutralRoll(direction,side)+(roll-1)*.60f,-SleeveRollSampleLimit,SleeveRollSampleLimit);
    internal static float SleevePoseWeight(Basis arm,int side,int plane,int height,int roll)
    {
        Span<float> weights=stackalloc float[SleevePosesPerSide];FillSleevePoseWeights(arm,side,weights);
        return weights[plane*6+height*3+roll];
    }
    private static void FillSleevePoseWeights(Basis arm,int side,Span<float> weights)
    {
        var direction=-arm.Y;
        float elevation=MathF.Acos(Math.Clamp(direction.Dot(Vector3.Down),-1,1));
        float forward=MathF.Atan2(MathF.Abs(direction.Z),MathF.Abs(direction.X)+.000001f)/(MathF.PI*.5f);
        float high=Mathf.SmoothStep(Mathf.DegToRad(90),Mathf.DegToRad(135),elevation);
        float active=Mathf.SmoothStep(Mathf.DegToRad(15),Mathf.DegToRad(90),elevation);
        // These authored samples cover lateral, scapular-plane and forward raises. Do not
        // apply a forward cap correction to a reach behind the shoulder.
        float posterior=1-Mathf.SmoothStep(.02f,.25f,direction.Z);
        var swing=new Basis(new Quaternion(Vector3.Down,direction));
        float current=swing.X.SignedAngleTo(arm.X,direction),centre=SleeveNeutralRoll(direction,side);
        float t=Math.Clamp((current-centre)/MathF.Max(.00001f,MathF.Abs(SleeveRollSample(direction,side,current<centre?0:2)-centre)),-1,1);
        float turn=Mathf.SmoothStep(0,1,MathF.Abs(t));
        for(int plane=0;plane<3;plane++)
        {
            float planar=active*posterior*Mathf.SmoothStep(0,1,MathF.Max(0,1-MathF.Abs(forward*2-plane)));
            for(int height=0;height<2;height++)for(int roll=0;roll<3;roll++)
            {
                float twist=roll==1?1-turn:((roll==0&&t<0)||(roll==2&&t>0))?turn:0;
                weights[plane*6+height*3+roll]=planar*(height==0?1-high:high)*twist;
            }
        }
    }
    private void UpdateSleeveCorrectives(int side,bool force=false)
    {
        if(BodySkin.Mesh is not ArrayMesh mesh||mesh.GetBlendShapeCount()!=SleevePoseCount)return;
        var arm=Arms[side].Basis;
        Span<float> weights=stackalloc float[SleevePosesPerSide];FillSleevePoseWeights(arm,side,weights);
        for(int plane=0;plane<3;plane++)for(int height=0;height<2;height++)for(int roll=0;roll<3;roll++)
        {
            int index=SleevePoseIndex(side,plane,height,roll);float weight=weights[plane*6+height*3+roll];
            if(force||MathF.Abs(weight-_sleevePoseWeights[index])>.00001f)
            {BodySkin.SetBlendShapeValue(index,weight);_sleevePoseWeights[index]=weight;}
        }
    }
}
