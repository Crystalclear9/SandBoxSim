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
        var parent=new int[points.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
        int Root(int v){while(parent[v]!=v){parent[v]=parent[parent[v]];v=parent[v];}return v;}
        for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
        {
            int a=triangles[i+j],b=triangles[i+(j+1)%3];parent[Root(a)]=Root(b);if(a>b)(a,b)=(b,a);long edge=((long)a<<32)|(uint)b;
            edges[edge]=edges.TryGetValue(edge,out int count)?count+1:1;
        }
        foreach(var pair in edges)
        {
            if(pair.Value>2)throw new InvalidOperationException("Anatomical hand has non-manifold edges");
            if(pair.Value==1&&(points[(int)(pair.Key>>32)].Y<.2749f||points[(int)(pair.Key&uint.MaxValue)].Y<.2749f))
                throw new InvalidOperationException("Joined arm has an exposed cut away from the buried elbow opening");
        }
        for(int i=0;i<points.Length;i++)if(Root(i)!=Root(0))throw new InvalidOperationException("Hand and forearm are not one connected skin");
    }
    private static Vector3 ReflectHand(Vector3 point)=>new(-point.X,point.Y,point.Z);
    private void ValidateHandLaterality()
    {
        var rig=Resident(17,false,JobType.Gatherer);
        void CheckMirroredMesh(Mesh left,Mesh right,bool skinned)
        {
            if(left==right)throw new InvalidOperationException("Both hands share the same unmirrored mesh");
            var l=left.SurfaceGetArrays(0);var r=right.SurfaceGetArrays(0);
            var lp=l[(int)Mesh.ArrayType.Vertex].AsVector3Array();var rp=r[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var ln=l[(int)Mesh.ArrayType.Normal].AsVector3Array();var rn=r[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var li=l[(int)Mesh.ArrayType.Index].AsInt32Array();var ri=r[(int)Mesh.ArrayType.Index].AsInt32Array();
            if(lp.Length!=rp.Length||li.Length!=ri.Length)throw new InvalidOperationException("Left/right topology does not correspond");
            for(int i=0;i<lp.Length;i++)
                if(ReflectHand(lp[i]).DistanceTo(rp[i])>.00001f||ReflectHand(ln[i]).DistanceTo(rn[i])>.002f)throw new InvalidOperationException("Hand contour or normal is not mirrored");
            for(int i=0;i<li.Length;i+=3)
                if(li[i]!=ri[i]||li[i+1]!=ri[i+2]||li[i+2]!=ri[i+1])throw new InvalidOperationException("Mirrored hand winding is inverted");
            if(skinned)
            {
                var lb=l[(int)Mesh.ArrayType.Bones].AsInt32Array();var rb=r[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var lw=l[(int)Mesh.ArrayType.Weights].AsFloat32Array();var rw=r[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                for(int i=0;i<lb.Length;i++)if(lb[i]!=rb[i]||MathF.Abs(lw[i]-rw[i])>.0001f)throw new InvalidOperationException("Mirrored skin changed finger identity");
            }
        }
        CheckMirroredMesh(rig.HandSkins[0].Mesh,rig.HandSkins[1].Mesh,true);
        CheckMirroredMesh(rig.HandOpen[0],rig.HandOpen[1],false);CheckMirroredMesh(rig.HandClosed[0],rig.HandClosed[1],false);
        var lod=AnatomicalHandMesh(0,"#aa8b78",true).SurfaceGetArrays(0);
        var lodBones=lod[(int)Mesh.ArrayType.Bones].AsInt32Array();
        foreach(var proxy in new[]{rig.HandOpen[0],rig.HandClosed[0]})
        {
            var arrays=proxy.SurfaceGetArrays(0);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            int triangles=arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length/3;
            if(triangles<1000||triangles>1600||p.Length>=1000)throw new InvalidOperationException("Anatomical hand LOD exceeds its distance budget or loses detail");
            for(int i=0;i<p.Length;i++)
            {
                if(!p[i].IsFinite()||!n[i].IsFinite()||MathF.Abs(n[i].LengthSquared()-1)>.002f)throw new InvalidOperationException("Invalid baked hand LOD");
                if(proxy==rig.HandClosed[0]&&lodBones[i*4] is >0 and <15)
                {
                    float gap=(p[i]-rig.Grip!.Position).Cross(rig.Grip.Basis.Y).Length();
                    if(gap<.0105f)throw new InvalidOperationException("Distant hand penetrates the tool shaft: "+gap);
                }
            }
        }
        var leftBounds=rig.HandOpen[0].GetAabb();var rightBounds=rig.HandOpen[1].GetAabb();
        if(-leftBounds.Position.X<leftBounds.End.X+.006f||rightBounds.End.X<-rightBounds.Position.X+.006f)
            throw new InvalidOperationException("Distant thumb is hidden inside the palm or has the wrong silhouette");
        for(int side=0;side<2;side++)
        {
            var index=rig.Fingers[side,0].Position;var little=rig.Fingers[side,3].Position;var thumb=rig.Thumbs[side].Position;
            if((thumb.X-index.X)*(little.X-index.X)>=0||thumb.X*ResidentRig.HandMirror(side)>=0)
                throw new InvalidOperationException("Thumb is on the little-finger side or the wrong hand");
        }
        foreach(float curl in new[]{0f,.35f,1f})
        {
            rig.PoseHandReference(curl);
            for(int bone=0;bone<16;bone++)
            {
                var left=rig.HandSkeletons[0].GetBoneGlobalPose(bone);var right=rig.HandSkeletons[1].GetBoneGlobalPose(bone);
                if(ReflectHand(left.Origin).DistanceTo(right.Origin)>.00001f||(-ReflectHand(left.Basis.X)).DistanceTo(right.Basis.X)>.00001f||ReflectHand(left.Basis.Y).DistanceTo(right.Basis.Y)>.00001f||ReflectHand(left.Basis.Z).DistanceTo(right.Basis.Z)>.00001f)
                    throw new InvalidOperationException("Left and right articulation are not anatomical mirrors: bone "+bone);
            }
            rig.SetHandDetail(false);rig.PoseHandReference(curl);
            if(rig.HandProxies[0].Mesh==rig.HandProxies[1].Mesh||rig.HandProxies[0].Mesh!=(curl>.5f?rig.HandClosed[0]:rig.HandOpen[0])||rig.HandProxies[1].Mesh!=(curl>.5f?rig.HandClosed[1]:rig.HandOpen[1]))
                throw new InvalidOperationException("Distance switch replaces both hands with a right hand");
            rig.SetHandDetail(true);
        }
        rig.Free();GD.Print("HAND_LATERALITY_PASS: distinct left/right near/far hands, thumb side, normals/winding, finger identity, open/closed binding and LOD switching");
    }
    private void ValidateSocialGesture()
    {
        var rig=Resident(42,false,JobType.Gatherer);Vector3 first=Vector3.Zero;float travel=0;
        for(int frame=0;frame<240;frame++)
        {
            rig.Attend(new Vector3(.2f,0,-1));rig.PoseAction(1f/60,ActionKind.Socialize,ActionPhase.Executing,false,.37f);
            var hand=InTorso(rig.Hands[1],rig.Torso).Origin;
            if(frame>60)travel+=hand.DistanceTo(first);first=hand;
            for(int side=0;side<2;side++)
            {
                if(!rig.Hands[side].Transform.IsFinite()||rig.Hands[side].Basis.GetRotationQuaternion().GetAngle()>.56f)
                    throw new InvalidOperationException("Conversation wrist exceeds its anatomical limit");
                var elbow=InTorso(rig.Elbows[side],rig.Torso).Origin;
                if(elbow.Y>rig.Arms[side].Position.Y+.02f||(side==0?elbow.X>-.08f:elbow.X<.08f))throw new InvalidOperationException("Conversation folds the elbow across the chest or above the shoulder");
                if(rig.Arms[side].Position.DistanceTo(elbow)>.281f)
                    throw new InvalidOperationException("Conversation detached the upper arm");
            }
            if(rig.ToolHeld||MathF.Abs(rig.Head.Rotation.Y)>.46f)throw new InvalidOperationException("Conversation draws a tool or over-rotates the head");
        }
        if(travel<.05f)throw new InvalidOperationException("Conversation has no hand articulation");
        var wrist=rig.Hands[1].Transform;var head=rig.Head.Transform;
        rig.PoseAction(.1f,ActionKind.Socialize,ActionPhase.Executing,true,.37f);
        if(wrist!=rig.Hands[1].Transform||head!=rig.Head.Transform)throw new InvalidOperationException("Paused conversation continues animating");
        rig.Free();GD.Print("SOCIAL_PRESENTATION_PASS: open-hand emphasis, attention, attached limbs, bounded wrists, no tool and pause");
    }
    private void ValidateHandling()
    {
        ValidateForearmSurfaces();ValidateHandLaterality();ValidateSocialGesture();ValidatePresentationCadence();
        foreach(var action in new[]{ActionKind.GatherWood,ActionKind.GatherIron,ActionKind.Farm,ActionKind.BuildHouse})
        {
            var rig=Resident(17,false,JobType.Gatherer);
            if(action==ActionKind.GatherWood)foreach(var hand in rig.HandSkins)ValidateHandSurface(hand.Mesh);
            int transfers=0;float travel=0;Vector3 previous=Vector3.Zero;bool blinked=false;
            var bridgeArrays=rig.ShoulderGeometry.SurfaceGetArrays(0);var bridgeVertices=bridgeArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var bridgeWeights=bridgeArrays[(int)Mesh.ArrayType.Color].AsColorArray();
            var garmentArrays=rig.DetailedBodyMesh.SurfaceGetArrays(0);
            var garmentPoints=garmentArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var garmentBones=garmentArrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var garmentWeights=garmentArrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            for(int frame=0;frame<270;frame++)
            {
                var parent=rig.ToolMesh.GetParent();
                rig.PoseAction(1f/60,action,frame<190?ActionPhase.Executing:ActionPhase.Idle,false,0);
                if(rig.LidMaterial.GetShaderParameter("blink").AsSingle()>.92f)blinked=true;
                if(rig.HasAnatomicalEyes&&(rig.Eyes[0].Scale!=Vector3.One||rig.Eyes[1].Scale!=Vector3.One))
                    throw new InvalidOperationException("Anatomical eyeballs were flattened during blink");
                for(int side=0;side<2;side++)if(MathF.Abs(rig.ShoulderSkeleton.GetBonePoseRotation(side+1).Dot(rig.Arms[side].Basis.GetRotationQuaternion()))<.999f)throw new InvalidOperationException("Sleeve bone does not follow shoulder");
                if(frame%30==0)for(int i=0;i<garmentPoints.Length;i++)if(garmentPoints[i].Y<1.0f)
                {
                    for(int j=0;j<4;j++)
                    {
                        int bone=garmentBones[i*4+j];if(bone==0||garmentWeights[i*4+j]<.99f)continue;
                        var arm=rig.Arms[bone-1];var bind=new Vector3(bone==1?-.202f:.202f,ResidentRig.BodyY(1.18f),0);
                        var point=arm.Transform*(garmentPoints[i]-bind);
                        var cuff=arm.Transform*new Vector3(0,-.253f,0);
                        if(point.DistanceTo(cuff)>.09f)throw new InvalidOperationException("Continuous sleeve separates from moving cuff");
                    }
                }
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
                    for(int hand=action==ActionKind.BuildHouse?1:0;hand<2;hand++)
                    {
                        var mesh=rig.HandSkins[hand].Mesh.SurfaceGetArrays(0);
                        var points=mesh[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var bones=mesh[(int)Mesh.ArrayType.Bones].AsInt32Array();
                        var skinWeights=mesh[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                        // Synchronous out-of-tree rigs do not run Skeleton3D's
                        // deferred update. Compose current local poses, rather
                        // than comparing this frame against a cached global pose.
                        var palette=new Transform3D[16];var global=new Transform3D[16];
                        var skeleton=rig.HandSkeletons[hand];
                        for(int b=0;b<16;b++)
                        {
                            int parentBone=skeleton.GetBoneParent(b);
                            global[b]=parentBone<0?skeleton.GetBonePose(b):global[parentBone]*skeleton.GetBonePose(b);
                            palette[b]=global[b]*skeleton.GetBoneGlobalRest(b).AffineInverse();
                        }
                        var handTransform=InTorso(rig.Hands[hand],rig.Torso);var toolTransform=InTorso(rig.ToolMesh,rig.Torso);var axis=toolTransform.Basis.Y.Normalized();
                        for(int v=0;v<points.Length;v++)
                        {
                            bool wristRing=points[v].Y>.0449f;
                            if(bones[v*4]==0&&!wristRing)continue;
                            var point=Vector3.Zero;
                            for(int b=0;b<4;b++)point+=(palette[bones[v*4+b]]*points[v])*skinWeights[v*4+b];
                            if(points[v].Y>.0451f)
                            {
                                var forearm=InTorso(rig.Elbows[hand],rig.Torso).AffineInverse()*(handTransform*point);
                                var expected=points[v]+new Vector3(0,-.255f,-.010f);
                                if(!forearm.IsFinite()||forearm.DistanceTo(expected)>.001f)
                                    throw new InvalidOperationException("Joined forearm follows the rotating hand instead of the elbow");
                                continue;
                            }
                            if(wristRing)
                            {
                                var wrist=InTorso(rig.Elbows[hand],rig.Torso).AffineInverse()*(handTransform*point);
                                float oval=wrist.X*wrist.X/(.027f*.027f)+wrist.Z*wrist.Z/(.024f*.024f);
                                if(MathF.Abs(wrist.Y+.210f)>.001f||MathF.Abs(oval-1)>.015f)
                                    throw new InvalidOperationException($"Animated wrist leaves forearm: {action}, side={hand}, frame={frame}, y={wrist.Y}, oval={oval}");
                                continue;
                            }
                            float gap=(handTransform*point-toolTransform.Origin).Cross(axis).Length();
                            if(!point.IsFinite()||gap<.0105f)
                                throw new InvalidOperationException("Rendered hand skin penetrates shaft: hand="+hand+" action="+action+" gap="+gap);
                        }
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
                        float front=frame==0?TorsoSurface(p.X,ResidentRig.SourceBodyY(p.Y),false,0).Z:p.Z,back=frame==0?TorsoSurface(p.X,ResidentRig.SourceBodyY(p.Y),true,0).Z:p.Z;
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
            if(rig.Fingers[1,0].Visible||!rig.HandProxies[1].Visible||!rig.ForearmProxies[0].Visible||!rig.ForearmProxies[1].Visible)
                throw new InvalidOperationException("Distant hand/forearm LOD failed");
            rig.SetHandDetail(true);
            if(rig.ForearmProxies[0].Visible||rig.ForearmProxies[1].Visible||!rig.HandSkins[0].Visible||!rig.HandSkins[1].Visible)
                throw new InvalidOperationException("Near joined arm draws a duplicate forearm");
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
