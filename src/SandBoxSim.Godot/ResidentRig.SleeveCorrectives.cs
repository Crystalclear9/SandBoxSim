using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int SleevePlaneCount=4,SleeveHeightCount=3,SleeveRollCount=5,SleevePosesPerSide=SleevePlaneCount*SleeveHeightCount*SleeveRollCount,SleevePoseCount=2*SleevePosesPerSide;
    private readonly float[] _sleevePoseWeights=new float[SleevePoseCount];
    internal static int SleevePoseIndex(int side,int plane,int height,int roll)=>side*SleevePosesPerSide+plane*SleeveHeightCount*SleeveRollCount+height*SleeveRollCount+roll;
    internal static float SleeveNeutralRoll(Vector3 direction,int side)
    {
        var swing=new Basis(new Quaternion(Vector3.Down,direction));
        return Math.Clamp(swing.X.SignedAngleTo(PreferredArmFrame(side,direction).X,direction),-SleeveRollSampleLimit,SleeveRollSampleLimit);
    }
    private static float SleeveRollAt(float centre,int roll)=>roll switch {0=>-SleeveRollSampleLimit,1=>centre-.60f,2=>centre,3=>centre+.60f,_=>SleeveRollSampleLimit};
    internal static float SleeveRollSample(Vector3 direction,int side,int roll)=>SleeveRollAt(SleeveNeutralRoll(direction,side),roll);
    internal static float SleevePoseWeight(Basis arm,int side,int plane,int height,int roll)
    {
        Span<float> weights=stackalloc float[SleevePosesPerSide];FillSleevePoseWeights(arm,side,weights);
        return weights[plane*SleeveHeightCount*SleeveRollCount+height*SleeveRollCount+roll];
    }
    private static void FillSleevePoseWeights(Basis arm,int side,Span<float> weights)
    {
        var direction=-arm.Y;
        float elevation=MathF.Acos(Math.Clamp(direction.Dot(Vector3.Down),-1,1));
        float planeAngle=MathF.Atan2(-direction.Z,MathF.Abs(direction.X)+.000001f);
        float planarIndex=Math.Clamp(planeAngle/(MathF.PI/4)+1,0,SleevePlaneCount-1);
        float heightIndex=Math.Clamp((elevation-Mathf.DegToRad(45))/Mathf.DegToRad(45),0,SleeveHeightCount-1);
        float active=Mathf.SmoothStep(Mathf.DegToRad(15),Mathf.DegToRad(45),elevation);
        // Samples include a 45-degree posterior plane. Beyond that plane,
        // fade out rather than reusing it for a deep reach behind the torso.
        float posterior=1-Mathf.SmoothStep(Mathf.DegToRad(45),Mathf.DegToRad(75),-planeAngle);
        var swing=new Basis(new Quaternion(Vector3.Down,direction));
        float current=swing.X.SignedAngleTo(arm.X,direction),centre=SleeveNeutralRoll(direction,side);
        current=Math.Clamp(current,-SleeveRollSampleLimit,SleeveRollSampleLimit);
        int high=1;while(high<SleeveRollCount-1&&current>SleeveRollAt(centre,high))high++;
        float lowAngle=SleeveRollAt(centre,high-1),highAngle=SleeveRollAt(centre,high);
        float turn=Mathf.SmoothStep(0,1,(current-lowAngle)/(highAngle-lowAngle));
        for(int plane=0;plane<SleevePlaneCount;plane++)
        {
            float planar=active*posterior*Mathf.SmoothStep(0,1,MathF.Max(0,1-MathF.Abs(planarIndex-plane)));
            for(int height=0;height<SleeveHeightCount;height++)for(int roll=0;roll<SleeveRollCount;roll++)
            {
                float twist=roll==high?turn:roll==high-1?1-turn:0;
                weights[plane*SleeveHeightCount*SleeveRollCount+height*SleeveRollCount+roll]=planar*Mathf.SmoothStep(0,1,MathF.Max(0,1-MathF.Abs(heightIndex-height)))*twist;
            }
        }
    }
    private void UpdateSleeveCorrectives(int side,bool force=false)
    {
        if(BodySkin.Mesh is not ArrayMesh mesh||mesh.GetBlendShapeCount()!=SleevePoseCount)return;
        var arm=Arms[side].Basis;
        Span<float> weights=stackalloc float[SleevePosesPerSide];FillSleevePoseWeights(arm,side,weights);
        for(int plane=0;plane<SleevePlaneCount;plane++)for(int height=0;height<SleeveHeightCount;height++)for(int roll=0;roll<SleeveRollCount;roll++)
        {
            int index=SleevePoseIndex(side,plane,height,roll);float weight=weights[plane*SleeveHeightCount*SleeveRollCount+height*SleeveRollCount+roll];
            if(force||MathF.Abs(weight-_sleevePoseWeights[index])>.00001f)
            {BodySkin.SetBlendShapeValue(index,weight);_sleevePoseWeights[index]=weight;}
        }
    }
}
