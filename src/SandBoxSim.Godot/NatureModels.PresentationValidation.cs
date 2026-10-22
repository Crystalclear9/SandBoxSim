using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidatePresentationCadence()
    {
        // Compare visible motion after the same elapsed time at close-up 60 Hz,
        // 60 FPS / stride 8 and 30 FPS / stride 8, not just a clock counter.
        foreach(var action in new[]{ActionKind.None,ActionKind.GatherWood,ActionKind.Socialize})
        {
            var phase=action==ActionKind.None?ActionPhase.Moving:ActionPhase.Executing;
            var rigs=new[]{Resident(42,false,JobType.Gatherer),Resident(42,false,JobType.Gatherer),Resident(42,false,JobType.Gatherer)};
            try
            {
                foreach(var rig in rigs)for(int frame=0;frame<120;frame++)rig.PresentAction(1f/60,action,phase,false,.37f);
                rigs[1].UpdateDistanceDetail(22*22);rigs[2].UpdateDistanceDetail(22*22);
                for(int sample=0;sample<5;sample++)
                {
                    int[] counts={48,6,3};
                    for(int cadence=0;cadence<rigs.Length;cadence++)for(int frame=0;frame<counts[cadence];frame++)
                        rigs[cadence].PresentAction(.8f/counts[cadence],action,phase,false,.37f);
                    for(int cadence=1;cadence<rigs.Length;cadence++)
                    {
                        var near=rigs[0];var far=rigs[cadence];
                        if(near.ToolHeld!=far.ToolHeld||near.Head.Rotation.DistanceTo(far.Head.Rotation)>.02f
                            ||near.Legs[0].Rotation.DistanceTo(far.Legs[0].Rotation)>.005f
                            ||near.Knees[1].Rotation.DistanceTo(far.Knees[1].Rotation)>.005f)
                            throw new InvalidOperationException("Resident motion loses time at a distant update cadence: "+action);
                        for(int side=0;side<2;side++)
                            if(InTorso(near.Hands[side],near.Torso).Origin.DistanceTo(InTorso(far.Hands[side],far.Torso).Origin)>.025f)
                                throw new InvalidOperationException("Distant tool/social hand falls behind the close-up pose: "+action);
                    }
                }
                foreach(var rig in rigs)
                {
                    rig.PresentAction(.8f,action,ActionPhase.Done,false,.37f);
                    if(rig.ToolHeld)throw new InvalidOperationException("Elapsed stow time did not release the tool");
                    var hand=rig.Hands[1].Transform;var head=rig.Head.Transform;
                    rig.PresentAction(.8f,action,phase,true,.37f);
                    if(hand!=rig.Hands[1].Transform||head!=rig.Head.Transform)throw new InvalidOperationException("Paused accumulated pose continues moving");
                }
            }
            finally{foreach(var rig in rigs)rig.Free();}
        }
        var detail=Resident(42,false,JobType.Gatherer);
        try
        {
            void Check(bool near)
            {
                if(detail.HeadSkin.Visible!=near||detail.HandSkins[0].Visible!=near||detail.HandProxies[1].Visible==near
                    ||detail.BodySkin.Mesh!=(near?detail.DetailedBodyMesh:detail.DistantBodyMesh)
                    ||(detail.ShoulderSkeleton.ProcessMode==Node.ProcessModeEnum.Disabled)==near)
                    throw new InvalidOperationException("Resident distance bands disagree about meshes or skeleton state");
            }
            detail.UpdateDistanceDetail(22*22);Check(false);
            foreach(float distance in new[]{18.1f,17.9f,18.2f,17.8f}){detail.UpdateDistanceDetail(distance*distance);Check(false);}
            detail.UpdateDistanceDetail(15.9f*15.9f);Check(true);
            foreach(float distance in new[]{17.9f,18.1f,19.9f,18.2f}){detail.UpdateDistanceDetail(distance*distance);Check(true);}
            detail.UpdateDistanceDetail(20.1f*20.1f);Check(false);
        }
        finally{detail.Free();}
        GD.Print("RESIDENT_CADENCE_PASS: near/far elapsed-time gait, tool and social poses, stow, pause and stable detail bands");
    }
}
