using System;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private static Transform3D InTorso(Node3D node,Node3D torso)
    {
        if(node==torso)return Transform3D.Identity;
        return InTorso((Node3D)node.GetParent(),torso)*node.Transform;
    }
    private void ValidateHandling()
    {
        foreach(var action in new[]{ActionKind.GatherWood,ActionKind.GatherIron,ActionKind.Farm,ActionKind.BuildHouse})
        {
            var rig=Resident(17,false,JobType.Gatherer);
            int transfers=0;float travel=0;Vector3 previous=Vector3.Zero;
            for(int frame=0;frame<270;frame++)
            {
                var parent=rig.ToolMesh.GetParent();
                rig.PoseAction(1f/60,action,frame<190?ActionPhase.Executing:ActionPhase.Idle,false,0);
                if(rig.ToolMesh.GetParent()!=parent)
                {
                    transfers++;
                    if(InTorso(rig.ToolMesh,rig.Torso).Origin.DistanceTo(rig.Holster.Position)>.008f)
                        throw new InvalidOperationException("Tool jumps during hand/belt transfer");
                }
                if(rig.ToolHeld)
                {
                    if(rig.ToolMesh.GetParent()!=rig.Grip || rig.ToolMesh.Transform!=Transform3D.Identity)
                        throw new InvalidOperationException("Held tool detached from hand");
                    if(rig.Fingertips[1,1].Rotation.X<1)throw new InvalidOperationException("Hand did not close around handle");
                    var point=InTorso(rig.ToolMesh,rig.Torso).Origin;
                    if(frame>70&&frame<160)travel+=point.DistanceTo(previous);previous=point;
                }
                if(!rig.Hands[0].Transform.IsFinite()||!rig.Hands[1].Transform.IsFinite())throw new InvalidOperationException("IK produced invalid wrist");
                if(frame==100)
                {
                    var wrist=rig.Hands[1].Transform;var finger=rig.Fingers[1,1].Transform;var tool=rig.ToolMesh.Transform;
                    rig.PoseAction(1,ActionKind.Sleep,ActionPhase.Executing,true,0);
                    if(rig.Hands[1].Transform!=wrist||rig.Fingers[1,1].Transform!=finger||rig.ToolMesh.Transform!=tool)
                        throw new InvalidOperationException("Paused handling changed pose");
                }
            }
            if(transfers!=2||travel<.03f||rig.ToolHeld||rig.CurrentTool!=rig.DefaultTool)
                throw new InvalidOperationException("Incomplete take/use/stow cycle: "+action);
            // A different work action must replace the tool after stowing, rather than deadlocking the draw state.
            foreach(var next in new[]{ActionKind.GatherWood,ActionKind.GatherIron,ActionKind.Farm})
            {
                for(int i=0;i<100;i++)rig.PoseAction(1f/60,next,ActionPhase.Executing,false,0);
                if(rig.CurrentTool!=ResidentRig.ToolFor(next)||!rig.ToolHeld)throw new InvalidOperationException("Tool switch failed");
            }
            rig.ShowCargo(true,ResourceKind.Wood);
            for(int i=0;i<90;i++)rig.PoseAction(1f/60,ActionKind.Deposit,ActionPhase.Executing,false,0);
            if(!rig.CargoMesh.Visible||rig.ToolHeld)throw new InvalidOperationException("Cargo and tool handling overlap");
            rig.SetHeadDetail(false);
            if(rig.HeadSkin.Visible||!rig.HeadProxy.Visible||rig.HeadProxy.Mesh.GetSurfaceCount()!=1)throw new InvalidOperationException("Distant head LOD failed");
            rig.SetHeadDetail(true);
            rig.SetHandDetail(false);rig.PoseAction(.016f,ActionKind.Wander,ActionPhase.Moving,false,0);
            if(rig.Fingers[1,0].Visible||!rig.HandProxies[1].Visible)throw new InvalidOperationException("Distant hand LOD failed");
            rig.Free();
        }
        var child=Resident(42,true,JobType.Farmer);
        for(int i=0;i<100;i++)child.PoseAction(1f/60,ActionKind.Farm,ActionPhase.Executing,false,0);
        if(child.ToolHeld||child.ToolMesh.Visible)throw new InvalidOperationException("Child received adult tool");child.Free();
        GD.Print("HANDLING_PASS: take/use/stow continuity, four action-specific tools, closed fingers, IK, switching, cargo, child policy, LOD and pause");
    }
}
