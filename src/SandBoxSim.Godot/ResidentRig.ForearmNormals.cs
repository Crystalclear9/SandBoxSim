using System;
using Godot;

namespace SandBoxSim.Client;

internal partial class ResidentRig
{
    internal const int ForearmNormalBends=8,ForearmNormalRolls=3,ForearmNormalPoseCount=ForearmNormalBends*ForearmNormalRolls;
    internal const float ForearmBendStep=MathF.PI/8;
    private void UpdateForearmNormals(int side,float roll,Quaternion unhinge)
    {
        if(HandSkins[side].Mesh is not ArrayMesh mesh||mesh.GetBlendShapeCount()!=ForearmNormalPoseCount)return;
        float bend=unhinge.GetAngle();if(bend>MathF.PI)bend=MathF.Tau-bend;
        float b=Math.Clamp(bend/ForearmBendStep,0,ForearmNormalBends-1),r=Math.Clamp(roll/(MathF.PI/2)+1,0,2);
        int lowB=Math.Min((int)b,ForearmNormalBends-2),lowR=Math.Min((int)r,1);float tb=b-lowB,tr=r-lowR;
        for(int i=0;i<ForearmNormalBends;i++)for(int j=0;j<ForearmNormalRolls;j++)
        {
            float w=(i==lowB?1-tb:i==lowB+1?tb:0)*(j==lowR?1-tr:j==lowR+1?tr:0);
            HandSkins[side].SetBlendShapeValue(i*ForearmNormalRolls+j,w);
        }
    }
}
