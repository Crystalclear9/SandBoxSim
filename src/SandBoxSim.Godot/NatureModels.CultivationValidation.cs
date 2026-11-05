using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateCultivation()
    {
        foreach(var (identity,far,near) in new[]{(17,0f,0f),(55,.045f,.005f)})
        {
            var rig=Resident(identity,false,identity==17?JobType.Gatherer:JobType.Farmer);
            try
            {
                rig.SetFurrowSurface(far,near);
                float before=float.PositiveInfinity,after=float.NegativeInfinity,lift=0,penetration=0;
                float pitchMin=float.PositiveInfinity,pitchMax=float.NegativeInfinity;
                int contacts=0;
                float upperGripGap=0;
                for(int frame=0;frame<660;frame++)
                {
                    rig.PoseAction(1f/60,ActionKind.Farm,ActionPhase.Executing,false,0);
                    if(frame>=85&&rig.GestureEngaged(ActionKind.Farm))
                    {
                        var left=InTorso(rig.Hands[0],rig.Torso)*new Vector3(0,-.035f,-.0286f);
                        var upperGrip=InTorso(rig.ToolMesh,rig.Torso)*new Vector3(0,.15f,0);
                        upperGripGap=MathF.Max(upperGripGap,left.DistanceTo(upperGrip));
                    }
                    if(frame<210||!rig.GestureEngaged(ActionKind.Farm))continue;
                    var edge=InTorso(rig.ToolMesh,rig)*ResidentRig.HoeCuttingEdge;
                    float expected=Mathf.Lerp(far,near,Math.Clamp((edge.Z-ResidentRig.FurrowStart)/(ResidentRig.FurrowEnd-ResidentRig.FurrowStart),0,1));
                    float gap=edge.Y-expected;
                    lift=MathF.Max(lift,gap);penetration=MathF.Min(penetration,gap);
                    pitchMin=MathF.Min(pitchMin,rig.Torso.Rotation.X);pitchMax=MathF.Max(pitchMax,rig.Torso.Rotation.X);
                    if(MathF.Abs(gap)>.023f)continue;
                    contacts++;before=MathF.Min(before,edge.Z);after=MathF.Max(after,edge.Z);
                }
                if(contacts<36||after-before<.16f||lift<.17f||penetration<-.027f||pitchMax-pitchMin<.20f||upperGripGap>.012f)
                    throw new InvalidOperationException($"Cultivation edge path: resident={identity}, contacts={contacts}, pull={after-before}, clearance={lift}, minimum gap={penetration}, chest={pitchMax-pitchMin}, upper grip={upperGripGap}");
                GD.Print($"CULTIVATION_PASS: resident={identity}, actual blade edge, {contacts} soil contacts, pull={after-before:F3} m, lift={lift:F3} m, minimum gap={penetration:F3} m, chest excursion={Mathf.RadToDeg(pitchMax-pitchMin):F1} degrees");
            }
            finally{rig.Free();}
        }
    }
}
