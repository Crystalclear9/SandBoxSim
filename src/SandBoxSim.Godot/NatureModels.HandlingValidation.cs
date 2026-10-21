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
    private static void ValidateHandSurface(Mesh mesh)
    {
        var arrays=mesh.SurfaceGetArrays(0);
        var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        if(points.Length<6000)throw new InvalidOperationException("Anatomical hand detail missing");
        for(int i=0;i<points.Length;i++)
            if(!points[i].IsFinite()||!normals[i].IsFinite()||normals[i].LengthSquared()<.98f)throw new InvalidOperationException("Invalid anatomical hand vertex or normal");
        var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        for(int i=0;i<points.Length;i++)
        {
            float sum=0;for(int b=0;b<4;b++){sum+=weights[i*4+b];if(bones[i*4+b]<0||bones[i*4+b]>15)throw new InvalidOperationException("Hand binding outside bone palette");}
            if(MathF.Abs(sum-1)>4f/65535f)throw new InvalidOperationException("Hand weights do not sum to one");
        }
        var nailPoints=mesh.SurfaceGetArrays(1)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var nailIndices=mesh.SurfaceGetArrays(1)[(int)Mesh.ArrayType.Index].AsInt32Array();
        var sourceLookup=new System.Collections.Generic.Dictionary<Vector3,int>();
        for(int i=0;i<points.Length;i++)sourceLookup[points[i]]=i;
        var combined=new System.Collections.Generic.List<int>(arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
        foreach(int nailIndex in nailIndices)
        {
            if(!sourceLookup.TryGetValue(nailPoints[nailIndex],out int source))throw new InvalidOperationException("Nail does not share the anatomical skin surface");
            combined.Add(source);
        }
        var triangles=combined.ToArray();var edges=new System.Collections.Generic.Dictionary<long,int>();
        for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
        {
            int a=triangles[i+j],b=triangles[i+(j+1)%3];if(a>b)(a,b)=(b,a);long edge=((long)a<<32)|(uint)b;
            edges[edge]=edges.TryGetValue(edge,out int count)?count+1:1;
        }
        foreach(var pair in edges)
        {
            if(pair.Value>2)throw new InvalidOperationException("Anatomical hand has non-manifold edges");
            if(pair.Value==1&&(points[(int)(pair.Key>>32)].Y<.0449f||points[(int)(pair.Key&uint.MaxValue)].Y<.0449f))
                throw new InvalidOperationException("Anatomical hand has an exposed cut away from the buried wrist ring");
        }

    }
    private void ValidateHandling()
    {
        foreach(var action in new[]{ActionKind.GatherWood,ActionKind.GatherIron,ActionKind.Farm,ActionKind.BuildHouse})
        {
            var rig=Resident(17,false,JobType.Gatherer);
            if(action==ActionKind.GatherWood)foreach(var hand in rig.HandSkins)ValidateHandSurface(hand.Mesh);
            int transfers=0;float travel=0;Vector3 previous=Vector3.Zero;bool blinked=false;
            var bridgeArrays=rig.ShoulderGeometry.SurfaceGetArrays(0);var bridgeVertices=bridgeArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var bridgeWeights=bridgeArrays[(int)Mesh.ArrayType.Color].AsColorArray();
            for(int frame=0;frame<270;frame++)
            {
                var parent=rig.ToolMesh.GetParent();
                rig.PoseAction(1f/60,action,frame<190?ActionPhase.Executing:ActionPhase.Idle,false,0);
                if(rig.LidMaterial.GetShaderParameter("blink").AsSingle()>.92f)blinked=true;
                if(rig.HasAnatomicalEyes&&(rig.Eyes[0].Scale!=Vector3.One||rig.Eyes[1].Scale!=Vector3.One))
                    throw new InvalidOperationException("Anatomical eyeballs were flattened during blink");
                for(int side=0;side<2;side++)if(MathF.Abs(rig.ShoulderSkeleton.GetBonePoseRotation(side+1).Dot(rig.Arms[side].Basis.GetRotationQuaternion()))<.999f)throw new InvalidOperationException("Sleeve bone does not follow shoulder");
                if(rig.ToolMesh.GetParent()!=parent)
                {
                    transfers++;
                    var toolPose=InTorso(rig.ToolMesh,rig.Torso);
                    if(toolPose.Basis.GetRotationQuaternion().AngleTo(rig.Holster.Basis.GetRotationQuaternion())>.025f)
                        throw new InvalidOperationException("Tool twists at hand/belt transfer");
                    if(InTorso(rig.ToolMesh,rig.Torso).Origin.DistanceTo(rig.Holster.Position)>.008f)
                        throw new InvalidOperationException("Tool jumps during hand/belt transfer");
                }
                if(rig.ToolHeld)
                {
                    if(rig.ToolMesh.GetParent()!=rig.Grip || rig.ToolMesh.Transform!=Transform3D.Identity)
                        throw new InvalidOperationException("Held tool detached from hand");
                    for(int digit=0;digit<4;digit++)
                    {
                        var tip=rig.Fingers[1,digit].Transform*rig.FingerMiddles[1,digit].Transform*rig.Fingertips[1,digit].Transform*new Vector3(0,-.007f*(digit==0?.92f:digit==1?1:digit==2?.96f:.86f),0);
                        float gap=(tip-rig.Grip!.Position).Cross(rig.Grip.Basis.Y).Length();
                        if(gap<.013f||gap>.026f)throw new InvalidOperationException("Closed finger does not surround handle: digit="+digit+" gap="+gap);
                    }
                    // Fingertip-only checks miss the proximal and middle bones passing through wood.
                    for(int digit=0;digit<4;digit++)
                    {
                        float scale=digit==0?.92f:digit==1?1:digit==2?.96f:.86f;
                        var proximal=rig.Fingers[1,digit].Transform;
                        var middle=proximal*rig.FingerMiddles[1,digit].Transform;
                        foreach(var center in new[]{proximal*new Vector3(0,-.013f*scale,0),middle*new Vector3(0,-.009f*scale,0)})
                        {
                            float gap=(center-rig.Grip!.Position).Cross(rig.Grip.Basis.Y).Length();
                            if(gap<.017f||gap>.029f)throw new InvalidOperationException("Finger bone crosses handle: "+digit+" gap="+gap);
                        }
                    }
                    if(rig.FingerMiddles[1,1].Rotation.X<1||rig.Fingertips[1,1].Rotation.X>1.05f)throw new InvalidOperationException("Hand did not close around handle");
                    var point=InTorso(rig.ToolMesh,rig.Torso).Origin;
                    if(frame>70&&frame<160)travel+=point.DistanceTo(previous);previous=point;
                }
                if(rig.ToolHeld&&frame>85&&frame<185&&action!=ActionKind.BuildHouse)
                {
                    var left=InTorso(rig.Hands[0],rig.Torso);
                    var tool=InTorso(rig.ToolMesh,rig.Torso);
                    var contact=left*new Vector3(0,-.035f,-.0286f);
                    var target=tool*new Vector3(0,action==ActionKind.Farm?.15f:.065f,0);
                    if(contact.DistanceTo(target)>.012f)throw new InvalidOperationException("Support hand misses moving shaft: "+action+" "+contact.DistanceTo(target));
                    if(rig.Hands[0].Basis.Y.AngleTo(Vector3.Up)>Mathf.DegToRad(48))throw new InvalidOperationException("Support wrist folds: "+action+" "+Mathf.RadToDeg(rig.Hands[0].Basis.Y.AngleTo(Vector3.Up)));
                }
                if(rig.ToolHeld&&frame is 90 or 120 or 150)
                {
                    var mesh=rig.HandSkins[1].Mesh.SurfaceGetArrays(0);
                    var points=mesh[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var bones=mesh[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    var skinWeights=mesh[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    var palette=new Transform3D[16];
                    for(int b=0;b<16;b++)palette[b]=rig.HandSkeletons[1].GetBoneGlobalPose(b)*rig.HandSkeletons[1].GetBoneGlobalRest(b).AffineInverse();
                    for(int v=0;v<points.Length;v++)
                    {
                        if(bones[v*4]==0)continue;
                        var point=Vector3.Zero;
                        for(int b=0;b<4;b++)point+=(palette[bones[v*4+b]]*points[v])*skinWeights[v*4+b];
                        if(!point.IsFinite()||(point-rig.Grip!.Position).Cross(rig.Grip.Basis.Y).Length()<.0105f)
                            throw new InvalidOperationException("Rendered hand skin penetrates shaft");
                    }
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
                if(rig.ToolHeld&&rig.Hands[1].Basis.Y.AngleTo(Vector3.Up)>Mathf.DegToRad(48))throw new InvalidOperationException("Wrist folds beyond working range: "+action+" "+Mathf.RadToDeg(rig.Hands[1].Basis.Y.AngleTo(Vector3.Up)));
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
            foreach(var bareAction in new[]{ActionKind.GatherFood,ActionKind.Take,ActionKind.Deposit,ActionKind.Eat,ActionKind.Drink})
            {
                for(int frame=0;frame<120;frame++)
                {
                    rig.PoseAction(1f/60,bareAction,ActionPhase.Executing,false,0);
                    if(frame>45&&rig.Hands[1].Basis.GetRotationQuaternion().GetAngle()>.72f)
                        throw new InvalidOperationException("Bare hand wrist folds: "+bareAction);
                }
            }
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
        GD.Print("HANDLING_PASS: take/use/stow continuity, action-specific tools, three phalanges, shaft clearance, wrist limits, mirrored support, IK, switching, cargo, child policy, LOD and pause");
    }
}
