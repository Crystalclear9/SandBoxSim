using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int BodyPoseCount=SleevePoseCount+2;
    internal void StudyForearmRotation(int side,Basis neutral,float roll)
    {
        // Isolate axial rotation without asking wrist-limited IK to solve a
        // different grip. The wrist and elbow centre positions stay fixed.
        Elbows[side].Basis=neutral*new Basis(Hands[side].Position.Normalized(),roll);
        _forearmRoll[side]=float.NaN;
        UpdateHandBones(side);
    }
    private void UpdateUpperArmFlex(int side,bool force=false)
    {
        if(BodySkin.Mesh is not ArrayMesh mesh||mesh.GetBlendShapeCount()!=BodyPoseCount)return;
        var bind=Hands[side].Position.Normalized();var direction=Elbows[side].Basis*bind;
        float bend=MathF.Acos(Math.Clamp(Elbows[side].Position.Normalized().Dot(direction),-1,1));
        float weight=Mathf.SmoothStep(.10f,1.90f,bend);int index=SleevePoseCount+side;
        if(force||MathF.Abs(weight-_sleevePoseWeights[index])>.00001f)
        {BodySkin.SetBlendShapeValue(index,weight);_sleevePoseWeights[index]=weight;}
    }
}
