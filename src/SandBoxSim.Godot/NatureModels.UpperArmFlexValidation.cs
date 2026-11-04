using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public void ValidateUpperArmFlex()
    {
        ValidateElbowSurface();
        float maxRollStep=0,maxVertexStep=0,maxIdleStep=0;int samples=0,gameplaySamples=0,gameplayFolds=0;float gameplayMinimum=1,maxArmholeCrease=0;int armholeEdges=0;
        foreach(int identity in new[]{17,55})foreach(var job in new[]{JobType.Farmer,JobType.Gatherer,JobType.Trader})
        {
            var rig=Resident(identity,false,job);rig.AmbientMotionEnabled=false;
            try
            {
                var mesh=(ArrayMesh)rig.DetailedBodyMesh;
                if(mesh.GetBlendShapeCount()!=ResidentRig.SleevePoseCount)throw new InvalidOperationException("Experimental muscle-volume shapes remain in the gameplay mesh");
                foreach(int studySide in new[]{0,1})foreach(int degrees in new[]{0,45,90,135})
                {
                    for(int frame=0;frame<180;frame++)rig.StudyArmElevation(1f/60,Mathf.DegToRad(degrees),Mathf.DegToRad(30),studySide);
                    var check=ArmholePoseCreases(rig);armholeEdges+=check.Edges;
                    float fraction=check.Sharp/(float)check.Edges;maxArmholeCrease=MathF.Max(maxArmholeCrease,fraction);
                    if(fraction>.015f)throw new InvalidOperationException($"Folded posed armhole: identity={identity}, job={job}, side={studySide}, elevation={degrees}, sharp={check.Sharp}/{check.Edges}");
                }
                for(int side=0;side<2;side++)
                {
                    float prior=float.NaN,first=0;
                    var arrays=rig.HandSkins[side].Mesh.SurfaceGetArrays(0);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    var last=new Vector3[p.Length];
                    for(int tick=0;tick<480;tick++)
                    {
                        float phase=tick/480f,bend=.08f+1.75f*(.5f-.5f*MathF.Cos(MathF.Tau*phase));
                        rig.StudyUpperArmReference(side,bend,.7f*MathF.Sin(MathF.Tau*phase));
                        float roll=rig.ForearmRoll(side);if(tick==0)first=roll;
                        if(float.IsFinite(prior))maxRollStep=MathF.Max(maxRollStep,MathF.Abs(roll-prior));prior=roll;
                        if(!float.IsFinite(roll)||MathF.Abs(roll-first)>1.6f||maxRollStep>.05f)throw new InvalidOperationException($"Forearm reference jumps: identity={identity}, side={side}, tick={tick}, roll={roll}, maxStep={maxRollStep}");
                        var local=CorrectedForearmSource(rig,side,p);
                        for(int i=0;i<p.Length;i+=13)if(p[i].Y>.075f)
                        {
                            Vector3 v=Vector3.Zero;
                            for(int k=0;k<4;k++)v+=(rig.HandSkeletons[side].GetBonePose(bones[i*4+k])*local[i])*weights[i*4+k];
                            v=rig.Arms[side].Transform*(rig.Elbows[side].Transform*(rig.Hands[side].Transform*v));
                            if(!v.IsFinite())throw new InvalidOperationException("Nonfinite displayed forearm");
                            if(tick>0)maxVertexStep=MathF.Max(maxVertexStep,v.DistanceTo(last[i]));last[i]=v;samples++;
                        }
                    }
                }
                for(int tick=0;tick<240;tick++)rig.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);
                var body=mesh.SurfaceGetArrays(0);var bp=body[(int)Mesh.ArrayType.Vertex].AsVector3Array();var bb=body[(int)Mesh.ArrayType.Bones].AsInt32Array();var bw=body[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var previousBody=new Vector3[bp.Length];
                for(int tick=0;tick<120;tick++)
                {
                    rig.PoseAction(1f/60,ActionKind.None,ActionPhase.Idle,false,0);var current=CorrectedSleeveSource(rig,bp);
                    for(int i=0;i<bp.Length;i+=17)
                    {
                        Vector3 v=Vector3.Zero;
                        for(int k=0;k<4;k++){int bone=bb[i*4+k];v+=(rig.ShoulderSkeleton.GetBonePose(bone)*rig.DetailedBodySkin.GetBindPose(bone)*current[i])*bw[i*4+k];}
                        v=rig.Torso.Transform*v;
                        if(tick>0)maxIdleStep=MathF.Max(maxIdleStep,v.DistanceTo(previousBody[i]));previousBody[i]=v;
                    }
                }
                foreach(var action in new[]{ActionKind.None,ActionKind.GatherWood,ActionKind.Farm,ActionKind.BuildHouse,ActionKind.Eat})
                {
                    for(int tick=0;tick<150;tick++)
                    {
                        rig.PoseAction(1f/60,action,action==ActionKind.None?ActionPhase.Idle:ActionPhase.Executing,false,0);
                        if(tick%15==0)for(int side=0;side<2;side++)
                        {
                            var check=GameplayElbowSurface(rig,side);gameplaySamples+=check.Samples;gameplayFolds+=check.Folds;gameplayMinimum=MathF.Min(gameplayMinimum,check.Minimum);
                        }
                    }
                    if(((ArrayMesh)rig.BodySkin.Mesh).GetBlendShapeCount()!=ResidentRig.SleevePoseCount)throw new InvalidOperationException("Gameplay reintroduces muscle-volume targets");
                    var held=new float[ResidentRig.SleevePoseCount];for(int i=0;i<held.Length;i++)held[i]=rig.BodySkin.GetBlendShapeValue(i);
                    rig.PoseAction(1,action,ActionPhase.Executing,true,0);
                    for(int i=0;i<held.Length;i++)if(held[i]!=rig.BodySkin.GetBlendShapeValue(i))throw new InvalidOperationException("Paused gameplay cloth keeps changing");
                }
            }
            finally{rig.Free();}
        }
        if(gameplaySamples<1000||gameplayFolds!=0||gameplayMinimum<=0)throw new InvalidOperationException($"Gameplay elbow folds: samples={gameplaySamples}, folds={gameplayFolds}, minimum={gameplayMinimum}");
        GD.Print($"ARMHOLE_MOTION_FLOW_PASS: {armholeEdges} actual posed edge samples, adults/three cuts/both arms, 0/45/90/135 degrees in the 30-degree plane, maximum >100-degree crease fraction={maxArmholeCrease:F5}");
        GD.Print($"GAMEPLAY_ELBOW_PASS: samples={gameplaySamples}, adults/both arms/three garment cuts/five action families, nonpositive={gameplayFolds}, minimum signed area={gameplayMinimum:F4}");
        if(samples<1000||maxVertexStep>.008f||maxIdleStep>.0005f)throw new InvalidOperationException($"Displayed forearm has a temporal discontinuity: samples={samples}, step={maxVertexStep}, idleStep={maxIdleStep}");
        GD.Print($"UPPER_ARM_FLEX_PASS: samples={samples}, adults/both arms/three cuts, 480-frame continuous reference, maximum roll/vertex step={maxRollStep:F5} rad/{maxVertexStep:F6} m, gameplay idle surface step={maxIdleStep:F7} m; gameplay uses 120 shoulder shapes without muscle-volume targets, five action families and pause");
    }
}
