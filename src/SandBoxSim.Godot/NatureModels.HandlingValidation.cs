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
            int transfers=0;float travel=0;Vector3 previous=Vector3.Zero;bool blinked=false;
            var bridgeArrays=rig.ShoulderGeometry.SurfaceGetArrays(0);var bridgeVertices=bridgeArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var bridgeWeights=bridgeArrays[(int)Mesh.ArrayType.Color].AsColorArray();
            for(int frame=0;frame<270;frame++)
            {
                var parent=rig.ToolMesh.GetParent();
                rig.PoseAction(1f/60,action,frame<190?ActionPhase.Executing:ActionPhase.Idle,false,0);
                if(rig.Eyes[0].Scale.Y<.08f)blinked=true;
                for(int side=0;side<2;side++)if(MathF.Abs(rig.ShoulderSkeleton.GetBonePoseRotation(side+1).Dot(rig.Arms[side].Basis.GetRotationQuaternion()))<.999f)throw new InvalidOperationException("Sleeve bone does not follow shoulder");
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
                    for(int digit=0;digit<4;digit++)
                    {
                        var tip=rig.Fingers[1,digit].Transform*rig.Fingertips[1,digit].Transform*new Vector3(0,-.015f,0);
                        float gap=(tip-rig.Grip!.Position).Cross(rig.Grip.Basis.Y).Length();
                        if(gap<.014f||gap>.023f)throw new InvalidOperationException("Closed finger does not surround handle");
                    }
                    if(rig.Fingertips[1,1].Rotation.X<1)throw new InvalidOperationException("Hand did not close around handle");
                    var point=InTorso(rig.ToolMesh,rig.Torso).Origin;
                    if(frame>70&&frame<160)travel+=point.DistanceTo(previous);previous=point;
                }
                var vertices=bridgeVertices;var weights=bridgeWeights;
                for(int i=0;i<vertices.Length;i++)
                {
                    var p=vertices[i];int side=weights[i].G<.5f?0:1;
                    var moved=rig.Arms[side].Transform*(p-rig.Arms[side].Position);
                    var deformed=p.Lerp(moved,weights[i].R);
                    if(!deformed.IsFinite())throw new InvalidOperationException("Invalid shoulder deformation");
                    if(weights[i].R==0)
                    {
                        if(deformed.DistanceTo(p)>.001f)throw new InvalidOperationException("Shoulder root leaves torso");
                        float front=frame==0?TorsoSurface(p.X,p.Y,false,0).Z:p.Z,back=frame==0?TorsoSurface(p.X,p.Y,true,0).Z:p.Z;
                        if(p.Z<front-.003f||p.Z>back+.003f)throw new InvalidOperationException("Sleeve root is outside shirt");
                    }
                    if(weights[i].R==1)
                    {
                        var local=rig.Arms[side].Transform.AffineInverse()*deformed;
                        if(MathF.Abs(local.Y+.077f)>.002f||local.X*local.X+local.Z*local.Z>.005f)throw new InvalidOperationException("Sleeve end detached from upper arm");
                    }
                }
                if(!rig.Hands[0].Transform.IsFinite()||!rig.Hands[1].Transform.IsFinite())throw new InvalidOperationException("IK produced invalid wrist");
                if(frame==100)
                {
                    var blink=rig.LidMaterial.GetShaderParameter("blink");
                    var wrist=rig.Hands[1].Transform;var finger=rig.Fingers[1,1].Transform;var tool=rig.ToolMesh.Transform;
                    rig.PoseAction(1,ActionKind.Sleep,ActionPhase.Executing,true,0);
                    if(!rig.LidMaterial.GetShaderParameter("blink").Equals(blink)||rig.Hands[1].Transform!=wrist||rig.Fingers[1,1].Transform!=finger||rig.ToolMesh.Transform!=tool)
                        throw new InvalidOperationException("Paused handling changed pose");
                }
            }
            if(!blinked||transfers!=2||travel<.03f||rig.ToolHeld||rig.CurrentTool!=rig.DefaultTool)
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
        var shortAction=Resident(17,false,JobType.Gatherer);bool visibleStroke=false;int attachments=0;
        for(int frame=0;frame<260;frame++)
        {
            var parent=shortAction.ToolMesh.GetParent();shortAction.PresentAction(1f/60,ActionKind.GatherWood,ActionPhase.Done,false,0);
            if(shortAction.ToolMesh.GetParent()!=parent)attachments++;
            if(frame>70&&frame<120&&shortAction.ToolHeld)visibleStroke=true;
        }
        if(!visibleStroke||attachments!=2||shortAction.ToolHeld||shortAction.CompletionVisible)throw new InvalidOperationException("Single-tick completed action was invisible or repeated");
        shortAction.PresentAction(.016f,ActionKind.GatherWood,ActionPhase.Moving,false,0);
        shortAction.PresentAction(.016f,ActionKind.GatherWood,ActionPhase.Done,false,0);
        if(!shortAction.CompletionVisible)throw new InvalidOperationException("Repeated real completion did not start a new gesture");
        shortAction.PresentAction(.016f,ActionKind.Flee,ActionPhase.Moving,false,0);
        if(shortAction.CompletionVisible)throw new InvalidOperationException("Completed gesture overrides urgent movement");
        shortAction.Free();
        var child=Resident(42,true,JobType.Farmer);
        for(int i=0;i<100;i++)child.PoseAction(1f/60,ActionKind.Farm,ActionPhase.Executing,false,0);
        if(child.ToolHeld||child.ToolMesh.Visible)throw new InvalidOperationException("Child received adult tool");child.Free();
        GD.Print("HANDLING_PASS: take/use/stow continuity, four action-specific tools, closed fingers, IK, switching, cargo, child policy, LOD and pause");
    }
}
