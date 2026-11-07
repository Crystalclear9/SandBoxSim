using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
/// <summary>Articulated presentation driven by real actions; no simulation clock or RNG is changed.</summary>
internal partial class ResidentRig : Node3D
{
    public Node3D Torso=null!,Head=null!,Holster=null!;
    internal const float NeckPivotY=-.105f;
    internal Vector3 HeadRest;
    public Node3D? Headwear,Grip,PackMount,SatchelMount;
    public bool IsChild;
    public ResidentTool DefaultTool;
    public ResidentTool CurrentTool {get;private set;}
    public MeshInstance3D ToolMesh=null!;
    public readonly Dictionary<ResidentTool,Mesh> ToolMeshes=new();
    public readonly Node3D[] Arms=new Node3D[2],Elbows=new Node3D[2],Hands=new Node3D[2],Thumbs=new Node3D[2],Legs=new Node3D[2],Knees=new Node3D[2],Feet=new Node3D[2];
    public readonly Node3D[,] Fingers=new Node3D[2,4],FingerMiddles=new Node3D[2,4],Fingertips=new Node3D[2,4];
    public readonly Node3D[] ThumbTips=new Node3D[2];
    public readonly MeshInstance3D[] ForearmProxies=new MeshInstance3D[2];
    public readonly MeshInstance3D[] HandProxies=new MeshInstance3D[2],HandSkins=new MeshInstance3D[2];
    public readonly Mesh[] HandOpen=new Mesh[2],HandClosed=new Mesh[2];
    public MeshInstance3D HeadSkin=null!,HeadProxy=null!,BodySkin=null!,ShoulderBridge=null!;
    public Skeleton3D ShoulderSkeleton=null!;
    internal bool HasShoulderSupports;
    internal const int SleeveSpans=6;
    internal static int SleeveSupportBone(int side,int span,bool corrected=false)=>4+(corrected?2*(SleeveSpans-1):0)+(span-1)*2+side;
    internal const int SupportedBodyBones=6+4*(SleeveSpans-1);
    public readonly Skeleton3D[] HandSkeletons=new Skeleton3D[2];
    public Mesh ShoulderGeometry=null!,DetailedBodyMesh=null!,DistantBodyMesh=null!;
    public Skin DetailedBodySkin=null!;
    public readonly Node3D[] Eyelids=new Node3D[2];
    public readonly Node3D[] Eyes=new Node3D[2];
    public Node3D Mouth=null!;
    public Vector3 MouthRest;
    public bool HasAnatomicalEyes;
    public ShaderMaterial EyeMaterial=null!;
    public ShaderMaterial LidMaterial=null!;
    internal ShaderMaterial? LashMaterial;
    internal MeshInstance3D? Lashes,TearLine;
    internal ShaderMaterial? TearMaterial;
    public ShaderMaterial MouthMaterial=null!;
    private bool _detailedHead=true,_returning;
    private float _faceTime,_mouthAmount;
    internal float ExpressionPhase;
    private Vector2 _gaze;
    private float _socialBlend,_socialTime,_attentionYaw;
    private float _wantedAttention;
    public void Attend(Vector3 localDirection,float weight=1) => _wantedAttention=localDirection.X*localDirection.X+localDirection.Z*localDirection.Z>.001f?Math.Clamp(MathF.Atan2(-localDirection.X,-localDirection.Z),-.40f,.40f)*weight:0;
    private Vector3 _returnPosition,_heldPosition;
    private Basis _returnBasis,_heldBasis;
    public void SetHeadDetail(bool detailed)
    {
        if(_detailedHead==detailed)return;_detailedHead=detailed;HeadSkin.Visible=detailed;HeadProxy.Visible=!detailed;Mouth.Visible=detailed;
        if(Lashes!=null)Lashes.Visible=detailed;if(TearLine!=null)TearLine.Visible=detailed;
        for(int i=0;i<2;i++){Eyes[i].Visible=detailed;Eyelids[i].Visible=detailed;}
    }
    public void UpdateDistanceDetail(float distanceSquared)
    {
        // Different entry/exit radii prevent mesh and skeleton thrashing when a
        // resident or an orbiting camera hovers near the detail boundary.
        SetHeadDetail(distanceSquared<(_detailedHead?20*20:16*16));
        SetHandDetail(distanceSquared<(_detailedHands?20*20:16*16));
    }
    private bool _detailedHands=true;
    private readonly List<(MeshInstance3D Node,Mesh Near,Mesh Far)> _distanceMeshes=new();
    public void RegisterDistanceMeshes(Node parent)
    {
        foreach(Node child in parent.GetChildren())
        {
            if(child is MeshInstance3D mesh&&mesh!=BodySkin&&mesh!=HeadSkin&&mesh.HasMeta("DistantMesh"))
                _distanceMeshes.Add((mesh,mesh.Mesh,(Mesh)mesh.GetMeta("DistantMesh").AsGodotObject()));
            RegisterDistanceMeshes(child);
        }
    }
    public void ValidateDistanceMeshes()
    {
        if(_distanceMeshes.Count==0)throw new InvalidOperationException("Resident has no distance surfaces");
        SetHandDetail(false);
        foreach(var part in _distanceMeshes)
            if(part.Node.Mesh!=part.Far||part.Far.GetSurfaceCount()!=1)throw new InvalidOperationException("Distant resident did not consolidate materials");
        if(BodySkin.Mesh.GetSurfaceCount()!=1)throw new InvalidOperationException("Distant torso retains multiple draw surfaces");
        SetHandDetail(true);
        foreach(var part in _distanceMeshes)
            if(part.Node.Mesh!=part.Near)throw new InvalidOperationException("Near resident lost original detail mesh");
    }
    private readonly float[] _handCurl=new float[2],_skinnedCurl={float.NaN,float.NaN};
    private readonly Quaternion[] _wristSkinRotation=new Quaternion[2];
    public MeshInstance3D CargoMesh=null!;
    public static readonly ResourceKind[] CargoKinds={ResourceKind.Food,ResourceKind.Wood,ResourceKind.Stone,ResourceKind.Iron};
    public readonly Dictionary<ResourceKind,Mesh> CargoMeshes=new();
    private bool _hasCargo;
    private ResourceKind _cargoKind;
    internal ResidentTravel Travel;
    internal WorldView3D.ResidentRoute Route;
    internal Vector2I StationTile;
    internal int StationSlot=-1;
    private ActionKind _lastAction;
    private ActionPhase _lastPhase;
    private float _phase,_activity,_reach,_draw,_workTime,_graspTime,_releaseTime,_workBlend;
    private Vector3 _restPose,_restPoseVelocity;
    internal float RestPoseWeight=>Math.Clamp(_restPose.X,0,1);
    private bool _carrying,_takingTool,_waitingForFacing,_waitingForContact;
    public bool ToolHeld {get;private set;}
    public string Handling => _recoveringPickup?"托住物件 / 收回":HandoffReserved?_handoffReadout:_placingCargo?"双手送稳 / 准备松开":
        _releasedCargo?(_groundPlacement?"物资落地 / 收手":"入库交接 / 收手"):_cargoTuck>0&&SupportedCargo!=ResourceKind.None?"收拢包裹 / 腾手":_placementPending?"抱稳物资 / 等待到位":
        ConsumptionOwnsHand?ConsumptionHandling:!InteractionSettled&&(_observedPhase==ActionPhase.Executing||CompletionVisible)?"接近目的地 / 停稳":
        _waitingForContact?"等待作业条件":_waitingForFacing?"转向目标 / 等待接触":ToolHeld ? _returning?"收回工具":_draw<.99f?"取出 / 抬手":_carrying?"手持携带":ToolStrokeHandling : _releaseTime>0?"松开手指":_reach>.01f?(_takingTool?(_reach==1?"合拢握柄":"伸手拿取"):"收回手臂"):CargoMesh.Visible?(_bareBlend>.01f&&CompatibleBareActions(_bareAction,ActionKind.Deposit)?"双手递出":"双手搬运"):_bareBlend>.01f?_bareAction switch {ActionKind.Take or ActionKind.GatherFood=>ScenePickupHandling,_=>"徒手交互"}:_cargoBlend>.01f?(_hasCargo?"托起物资":"放下 / 收手"):_restPose.X>.08f?"休息 / 起身":"双手放松";
    public static ResidentTool ToolFor(ActionKind action)=>action switch {
        ActionKind.GatherWood=>ResidentTool.Axe,
        ActionKind.GatherStone or ActionKind.GatherIron=>ResidentTool.Pick,
        ActionKind.Farm=>ResidentTool.Hoe,
        ActionKind.BuildHouse or ActionKind.BuildFarm or ActionKind.BuildStorage or ActionKind.BuildMine=>ResidentTool.Hammer,
        ActionKind.Attack or ActionKind.Hunt=>ResidentTool.Spear,_=>ResidentTool.None };
    public string HeldObject=>ToolHeld?CurrentTool switch {ResidentTool.Axe=>"斧头",ResidentTool.Pick=>"镐",ResidentTool.Hoe=>"锄头",ResidentTool.Hammer=>"手锤",ResidentTool.Spear=>"长矛",_=>""}
        :Cup.Visible?"饮水杯":Bite.Visible?"食物":CargoMesh.Visible&&!_releasedCargo?_cargoKind switch {ResourceKind.Wood=>"木材捆",ResourceKind.Food=>"食物袋",ResourceKind.Stone=>"石料袋",ResourceKind.Iron=>"铁料袋",_=>"物资"}:"";
    public void SelectTool(ResidentTool kind)
    {
        CurrentTool=kind;ToolMesh.Visible=kind!=ResidentTool.None;
        if(kind!=ResidentTool.None)ToolMesh.Mesh=ToolMeshes[kind];
    }
    private void AttachTool(Node3D parent)
    {
        ToolMesh.GetParent().RemoveChild(ToolMesh);parent.AddChild(ToolMesh);ToolMesh.Transform=Transform3D.Identity;
        ToolHeld=parent==Grip;
        _toolFilterReady=false;
        if(!ToolHeld){_graspTime=0;_releaseTime=.16f;}
    }
    public void Pose(float delta,bool moving,bool working,bool resting,bool paused,float identityPhase)
    {
        ActionKind action=DefaultTool switch { ResidentTool.Pick=>ActionKind.GatherStone,ResidentTool.Hoe=>ActionKind.Farm,ResidentTool.Spear=>ActionKind.Attack,_=>ActionKind.BuildHouse };
        PoseAction(delta,resting?ActionKind.Sleep:working?action:ActionKind.None,moving?ActionPhase.Moving:working||resting?ActionPhase.Executing:ActionPhase.Idle,paused,identityPhase);
    }
    public void PoseAction(float delta,ActionKind action,ActionPhase phase,bool paused,float identityPhase)
    {
        if(paused)return;
        _waitingForContact=false;
        AdvancePose(PresentationDelta(delta),action,phase,identityPhase,false);
    }
    private void AdvancePose(float delta,ActionKind action,ActionPhase phase,float identityPhase,bool presented,bool targetAligned=true)
    {
        // Distant residents arrive with several frame intervals accumulated.
        // Consume that time in bounded steps instead of discarding everything
        // beyond .1 seconds. A long host stall still has a one-second budget.
        var family=FamilyOf(action);
        bool fineReach=HandoffReserved||_referencePlaying||_referenceWeight>0||_hasArmReturn||_reach>0||_releaseTime>0||ToolHeld&&(_draw<.99f||_returning)||!ToolHeld&&family==ActionFamily.Tool||_cargoBlend>0||_releasedCargo||_socialBlend>.001f||_restPose.X>.001f||UsesBareHands(family)||family is ActionFamily.Social or ActionFamily.Rest;
        ConsumeLocomotionSamples();
        // Mouth approaches combine wrist orientation limits and a very short
        // target distance. Resolve those constraints at one shared cadence so
        // render frequency cannot select a different hand/elbow trajectory.
        bool mouthReach=family==ActionFamily.Consume||ConsumptionOwnsHand;
        bool walking=phase==ActionPhase.Moving||_locomotionSpeed>.04f&&_worldLocomotion||_gaitSpeed>.04f;
        // A sparse distant update still needs intermediate gait targets.
        // A single coarse sample makes the filtered wrists chase the last pose.
        float maxStep=mouthReach?1f/120:fineReach||walking?1f/30:.1f;
        int steps=Math.Max(1,(int)MathF.Ceiling(delta/maxStep));
        for(int step=0;step<steps;step++)
            if(presented)PresentActionStep(delta/steps,action,phase,identityPhase,targetAligned);
            else PoseActionStep(delta/steps,action,phase,identityPhase);
    }
    private static float PresentationDelta(float delta)=>float.IsFinite(delta)?Math.Clamp(delta,0,1):0;
    private void PoseActionStep(float delta,ActionKind action,ActionPhase phase,float identityPhase,bool targetAligned=true,uint? revision=null)
    {
        _poseStep=delta;
        var previousCurl=new Vector2(_handCurl[0],_handCurl[1]);
        for(int side=0;side<2;side++)
        {
            if(!_armSolvedThisStep[side])_elbowSwivelVelocity[side]=0;
            _armSolvedThisStep[side]=false;
            _priorArms[side]=Arms[side].Transform;_priorForearms[side]=Elbows[side].Transform;_priorWrists[side]=Hands[side].Transform;
            _previousHands[side]=Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform;
            _previousElbows[side]=Arms[side].Transform*Elbows[side].Position;
        }
        var family=FamilyOf(action);
        bool moving=phase==ActionPhase.Moving||_worldLocomotion&&_locomotionSpeed>.04f;
        _waitingForFacing=phase==ActionPhase.Executing&&!targetAligned;
        bool executing=phase==ActionPhase.Executing&&InteractionSettled&&targetAligned;
        bool reserved=HandoffReserved;
        bool risingFromPlacement=!_placingCargo&&_floorCrouch>.18f;
        bool restRequested=!reserved&&!risingFromPlacement&&executing&&family==ActionFamily.Rest;
        bool handsFree=!ToolHeld&&_reach==0&&_releaseTime==0&&!ConsumptionOwnsHand&&!_recoveringPickup;
        bool cargoReleased=_cargoBlend<=.001f;
        // Give outgoing hand owners time to release before lowering the
        // pelvis. On exit the legs can start walking while work and reaches
        // wait until the torso is sufficiently upright.
        bool resting=restRequested&&handsFree&&cargoReleased&&_bareBlend<=.001f&&_socialBlend<=.001f;
        DampMotion(ref _restPose,ref _restPoseVelocity,resting?Vector3.Right:Vector3.Zero,.17f,delta);
        float restWeight=RestPoseWeight;
        bool upperReady=!reserved&&!risingFromPlacement&&!_recoveringPickup&&executing&&restWeight<.20f;
        if((_lastAction!=action||_lastPhase!=phase)&&executing)
        {
            _workTime=0;
            if(ToolFor(_lastAction)!=ToolFor(action)||_workBlend<=.01f)_strokePhase=0;
        }
        // Store the visual phase. A logical action may already be executing
        // while its resident is still finishing the approach.
        _lastAction=action;_lastPhase=phase==ActionPhase.Executing&&!executing?ActionPhase.Moving:phase;
        bool toolRequested=!reserved&&!IsChild&&family==ActionFamily.Tool&&(phase==ActionPhase.Moving||phase==ActionPhase.Executing);
        bool gestureReleased=_bareBlend<=.10f&&_socialBlend<=.10f&&!ConsumptionOwnsHand&&!_recoveringPickup;
        var wanted=toolRequested&&!risingFromPlacement&&cargoReleased&&gestureReleased&&restWeight<.20f?ToolFor(action):ResidentTool.None;
        // A long tool is carried in the hand during idle/wandering, rather than
        // hanging diagonally from an invisible waist socket. Bare-hand actions
        // and cargo retain priority and go through the complete stow transition.
        if(!reserved&&!risingFromPlacement&&wanted==ResidentTool.None&&!IsChild&&!_hasCargo&&!_placingCargo&&!_placementPending&&!_releasedCargo&&!restRequested&&cargoReleased&&gestureReleased&&restWeight<.20f&&
            (action is ActionKind.None or ActionKind.Wander||phase is ActionPhase.Idle or ActionPhase.Done))
            wanted=DefaultTool;
        _carrying=wanted!=ResidentTool.None&&(!executing||ToolFor(action)==ResidentTool.None);
        // Prepare the working pose during the lift after a secure grasp. The
        // stroke clock below still waits for the lift to finish; waiting to
        // prepare until then would make the support hand chase the first hit.
        if(_rearmToolStroke&&_workBlend<=.001f){_rearmToolStroke=false;_strokePhase=0;}
        _workBlend=Mathf.MoveToward(_workBlend,executing&&!_rearmToolStroke&&ToolHeld&&CurrentTool==wanted&&_draw>=.25f&&ToolFor(action)!=ResidentTool.None?1:0,delta/.40f);
        bool usingTool=wanted!=ResidentTool.None;
        _takingTool=usingTool&&!ToolHeld;
        _phase+=delta*Mathf.Lerp(1.8f,7.5f,_activity);_workTime+=delta;
        SampleFootMomentum(delta);
        AdvanceLocomotion(delta,moving,phase!=ActionPhase.Moving||Travel.Projecting);
        AdvanceMotionReference(delta,action,phase,identityPhase);
        float gaitPhase=_gaitCycle*Mathf.Tau;
        float breath=MathF.Sin(_phase*.5f+identityPhase);
        bool scenePickup=_sceneContact.HasValue&&family==ActionFamily.Pickup&&revision.HasValue;
        bool pickupRequested=upperReady&&family==ActionFamily.Pickup
            &&(scenePickup?_scenePickupAge<ScenePickupDuration:!_plannedPickup);
        bool pickup=pickupRequested&&handsFree&&cargoReleased&&_socialBlend<=.10f
            &&(_bareBlend==0||FamilyOf(_bareAction)==ActionFamily.Pickup);
        if(pickup)
        {
            if(_pickupBlend==0)_pickupTime=0;
            _pickupTime+=delta;
        }
        _pickupBlend=Mathf.MoveToward(_pickupBlend,pickup?1:0,delta/.48f);
        float pickupWeight=Smooth(_pickupBlend);
        var gatherPose=pickupWeight>0?ResidentActionCapture.Shared.Pickup(Mathf.PosMod(_pickupTime/(2.14f-.16f*Flow),1),Flow):default;
        AdvanceScenePickup(delta,scenePickup,pickup);
        float transfer=Mathf.SmoothStep(0,.16f,gatherPose.Drop);
        var pickupShift=new Vector3(gatherPose.Shift*.019f*transfer,-gatherPose.Drop*(.086f+.010f*Flow),gatherPose.Drop*.019f)*pickupWeight;
        if(scenePickup||_scenePickupBend>0)pickupShift=Vector3.Zero;
        pickupShift+=new Vector3(0,-(.12f+.37f*_scenePickupDepth)*_scenePickupBend,-.055f*_scenePickupBend);
        float vertical=breath*.003f*(1-_activity)*(1-pickupWeight)*(1-restWeight);
        float gaitWeight=_activity*(1-pickupWeight)*(1-restWeight)*(1-.65f*_arrivalWeight*ReachEase(_arrivalTime/(2*LandingDuration)));
        bool working=executing&&!_rearmToolStroke&&usingTool&&ToolHeld&&wanted==CurrentTool&&!_carrying;
        AdvanceWorkStance(delta,working&&UsesStagedStance&&_draw>=.25f);
        if(working&&_draw>=.99f&&(!UsesStagedStance||WorkStanceSettled))AdvanceWorkStroke(delta);
        var stance=WorkSupport(delta,executing&&usingTool&&ToolHeld&&wanted==CurrentTool&&!_carrying,
            (1-_activity)*(1-pickupWeight));
        float workBlend=Smooth(_workBlend);
        float surfaceBend=AdvanceWorkSurfaceBend(delta,working,workBlend);
        stance+=new Vector3(0,-surfaceBend,-surfaceBend*.28f);
        bool socialReady=upperReady&&family==ActionFamily.Social&&handsFree&&!_hasCargo&&_bareBlend==0&&_cargoBlend<=.05f;
        if(socialReady&&_socialBlend==0)_socialTime=0;
        _socialBlend=Mathf.MoveToward(_socialBlend,socialReady?1:0,delta/.70f);
        if(_socialBlend>0)_socialTime+=delta;
        float socialWeight=ReachEase(_socialBlend);
        float expressionPhase=identityPhase+ExpressionPhase;
        float socialPhase=_socialTime/3.2f+expressionPhase*.1f;
        var socialPose=socialWeight>0?ResidentActionCapture.Shared.Social(Mathf.PosMod(socialPhase,1),Flow):default;
        var sip=_consumeAction==ActionKind.Drink&&ConsumptionOwnsHand
            ?ResidentActionCapture.Shared.Drink(.61f*_consumeReach):Vector2.Zero;
        bool relaxed=!moving&&_activity<.001f&&!usingTool&&!ToolHeld&&!_hasCargo&&!restRequested&&!pickup;
        var restSupport=RestSupport(delta,relaxed,identityPhase);
        float idleWeight=_restSupport*(1-_activity)*(1-Smooth(_bareBlend))*(1-socialWeight);
        float effort=executing&&usingTool?(.5f-WorkLift(_strokePhase+WorkTorsoLead))*.067f*StrokeAccent*RelativeWorkLoad*workBlend:0;
        float toolLean=-.035f-(.097f-.041f*Flow)*workBlend-effort;
        float freeLean=-(.033f+.018f*_gaitSpeed)*_activity+.007f*MathF.Sin(_phase*.53f+identityPhase)*(1-_activity);
        float pickupLean=(-.61f+.055f*Flow)*gatherPose.Bend;
        if(scenePickup||_scenePickupBend>0)pickupLean=0;
        float groundPlacement=GroundPlacementBody(delta);
        float placementLean=-_floorLean*groundPlacement+(_placingCargo&&!PlacementUsesSurface?-.14f*ReachEase(_placementAge/.52f):0);
        float desiredLean=Mathf.Lerp(Mathf.Lerp(usingTool?toolLean:freeLean,pickupLean,pickupWeight),-.16f,restWeight)
            +placementLean-(.35f+.55f*_scenePickupDepth)*_scenePickupBend;
        float bareParticipation=Smooth(_bareBlend)*(1-pickupWeight);
        float reachLean=-Math.Clamp(-_previousHands[1].Origin.Z-.12f,0,.26f)*.12f*bareParticipation;
        float reachTurn=Math.Clamp(_previousHands[1].Origin.X,-.22f,.22f)*.09f*bareParticipation;
        var torsoAngles=Torso.Rotation;
        var walkingChest=WalkingChest(gaitPhase)*gaitWeight;
        var workingChest=workBlend>0?WorkingChest(_strokePhase,workBlend):Vector3.Zero;
        bool cultivating=ToolHeld&&CurrentTool==ResidentTool.Hoe&&workBlend>0;
        var dig=cultivating?ResidentActionCapture.Shared.Dig(_strokePhase,Flow):default;
        var pivot=PivotPhrase();float pivotAmount=PivotAmplitude;
        var arrival=ArrivalTransfer();
        if(cultivating)
        {
            workingChest=new Vector3(-.20f+dig.Bend*.22f,dig.Turn*.085f,-.025f)*workBlend;
            stance+=new Vector3(.025f*dig.Turn,-.026f,-.031f+.030f*dig.Shift)*workBlend;
        }
        var socialChest=SocialChest(socialPose)*socialWeight;
        var idleChest=new Vector3(.011f*breath,.013f*MathF.Sin(_phase*.31f+identityPhase+.8f),-1.25f*restSupport.X)*idleWeight;
        var travelShift=_travelWeightShift*(1-pickupWeight)*(1-restWeight);
        var chestTarget=new Vector3(desiredLean+reachLean+sip.Y*.028f+_travelPitch*(1-pickupWeight)-surfaceBend,
            reachTurn+_gaitTurn*TurnChestShare*gaitWeight+.053f*gatherPose.Turn*pickupWeight,0);
        var turningChest=new Vector3(-.018f*MathF.Abs(pivotAmount),.085f*pivot.Chest*pivotAmount+.115f*arrival.Look,
            .065f*pivot.Roll*pivotAmount-.57f*arrival.Shift.X);
        DampMotion(ref torsoAngles,ref _torsoVelocity,chestTarget+walkingChest+workingChest+socialChest+idleChest+turningChest+_referenceChest,TorsoFollowTime,delta);
        var torsoBasis=Basis.FromEuler(torsoAngles);
        var gaitOffset=_gaitOffset*(1-pickupWeight)*(1-restWeight);
        var lowerShift=stance+restSupport+gaitOffset+travelShift+pickupShift+arrival.Shift+(IsPivoting?PivotSupport():Vector3.Zero)
            +new Vector3(0,-.09f*restWeight-_floorDrop*groundPlacement,-.046f*groundPlacement);
        var translation=lowerShift+Vector3.Up*vertical;
        var hip=new Vector3(0,HipHeight,0);Torso.Transform=new(torsoBasis,hip-torsoBasis*hip+translation);
        var pelvisBasis=new Basis(Quaternion.Identity.Slerp(GaitPelvisBasis.GetRotationQuaternion(),(1-pickupWeight)*(1-restWeight)));
        // Share the pickup flexion with the hips while the chest still leads
        // the reach. Use the captured bend so both frames release together.
        if(pickupWeight>0&&!IsChild)
            pelvisBasis=new Basis(Vector3.Right,-(.11f+.10f*Flow)*gatherPose.Bend*pickupWeight)*pelvisBasis;
        UpdateBodyBend(lowerShift,pelvisBasis);
        _attentionYaw=Mathf.Lerp(_attentionYaw,_wantedAttention,1-MathF.Exp(-delta*6));
        float phraseEmphasis=action==ActionKind.Socialize
            ?Math.Clamp(MathF.Max(socialPose.Left.Y,socialPose.Right.Y),0,1)
            :ReachPulse(Mathf.PosMod(_workTime/3.2f+expressionPhase*.1f,1));
        var headTarget=new Vector3(
            Mathf.Lerp(usingTool?-.045f-effort*.4f+(.105f*dig.Head+.12f)*workBlend*(cultivating?1:0):breath*.018f+socialPose.Head*.087f*socialWeight+sip.X*.19f,
                .14f+.095f*gatherPose.Head,gatherPose.Bend*pickupWeight),
            MathF.Sin(_phase*.32f+identityPhase)*.045f*(1-_activity)+Math.Clamp(_attentionYaw+.24f*pivot.Look*pivotAmount+.22f*arrival.Look,-.59f,.59f)+_gaitTurn*TurnHeadShare*gaitWeight-walkingChest.Y*.55f,
            -.36f*socialChest.Z-walkingChest.Z*.55f+restSupport.X*.6f*idleWeight);
        Head.Quaternion=DampRotation(Head.Quaternion,ref _headVelocity,Basis.FromEuler(headTarget).GetRotationQuaternion(),.075f,delta);
        var neckPivot=new Vector3(0,NeckPivotY*Head.Scale.Y,0);
        Head.Position=HeadRest+neckPivot-Basis.FromEuler(Head.Rotation)*neckPivot;
        for(int i=0;i<2;i++)
        {
            Arms[i].Position=ShoulderRest(i);
            var pelvis=LowerGarmentFrame;
            Legs[i].Position=pelvis*new Vector3(i==0?-HipHalfWidth:HipHalfWidth,HipHeight,0);
            Legs[i].Rotation=Knees[i].Rotation=Vector3.Zero;
            SetFootOrientation(i,(Legs[i].Basis*Knees[i].Basis).Inverse());
            if(stance.LengthSquared()+travelShift.LengthSquared()>1e-10f||restWeight>.00001f||groundPlacement>0)SolveLegTo(i,RestFootCenter(i),Basis.Identity);
            if(_restSupport>.00001f)PlantRestLeg(i);
            if(pickupWeight>0||_scenePickupBend>0)SolveLegTo(i,RestFootCenter(i),Basis.Identity);
            PoseWorkFoot(i,gaitWeight);
            PoseWalkingLeg(i,gaitWeight);
            PoseArrivalLeg(i);
            PosePivotLeg(i);
            float turnSwing=((i==0)==(pivotAmount>=0)?pivot.Left:pivot.Right)*.10f*MathF.Abs(pivotAmount)+(i==0?-1:1)*.11f*arrival.Look;
            if(i==0&&CurrentTool==ResidentTool.Hammer&&ToolHeld)
                turnSwing+=(.07f-.16f*WorkLift(_strokePhase+WorkTorsoLead))*workBlend;
            PoseFreeArm(i,delta,identityPhase,idleWeight,restSupport.X,turnSwing);
        }
        SyncTrousers();
        _faceTime+=delta;
        if(_detailedHead)
        {
            float blink=RestLid+(1-RestLid)*FaceBlink(_faceTime,expressionPhase);
            LidMaterial.SetShaderParameter("blink",blink);
            LashMaterial?.SetShaderParameter("blink",blink);TearMaterial?.SetShaderParameter("blink",blink);
            float attention=MathF.Floor(_faceTime*.55f+expressionPhase*.37f);
            var targetGaze=new Vector2(MathF.Sin(attention*12.989f+expressionPhase)*.052f,MathF.Sin(attention*7.137f+expressionPhase*.71f)*.010f);
            if(executing)targetGaze*=.35f;
            _gaze=_gaze.Lerp(targetGaze,1-MathF.Exp(-delta*18));
            // Iris pigment and the corneal dome move as one eye around its
            // authored centre. Sliding only the UV leaves a stationary lens.
            for(int eye=0;eye<2;eye++)
            {
                Eyes[eye].Scale=HasAnatomicalEyes?Vector3.One:new(1,MathF.Max(.02f,1-blink),1);
                Eyes[eye].Rotation=HasAnatomicalEyes?new Vector3(_gaze.Y,_gaze.X,0):Vector3.Zero;
            }
            float speech=executing?MouthActivity(action,phraseEmphasis):0;
            _mouthAmount=Mathf.Lerp(_mouthAmount,speech,1-MathF.Exp(-delta*24));
            Mouth.Position=MouthRest;Mouth.Scale=Vector3.One;MouthMaterial.SetShaderParameter("speech",_mouthAmount);
        }
        bool wantCurrent=usingTool&&wanted==CurrentTool;
        bool supportWanted=wantCurrent&&ToolHeld&&executing&&!_carrying&&(CurrentTool is ResidentTool.Axe or ResidentTool.Pick or ResidentTool.Hoe or ResidentTool.Spear);
        _supportBlend=Mathf.MoveToward(_supportBlend,supportWanted?1:0,delta/.35f);
        if(ToolHeld)
        {
            _draw=Mathf.MoveToward(_draw,wantCurrent?1:0,delta/.48f);
            if(!wantCurrent&&!_returning)
            {
                _returning=true;
                _returnPosition=_heldPosition;_returnBasis=_heldBasis;
            }
            if(wantCurrent)_returning=false;
            var carry=CarryPose(CurrentTool,gaitPhase);
            carry.Item1=ReachBodyPoint(carry.Item1);
            var work=carry;
            if(cultivating)work=CultivationGrip(_strokePhase);
            else if(workBlend>0)
            {
                work=WorkPose(CurrentTool,_strokePhase);
                work.Item1=ReachBodyPoint(work.Item1);
            }
            var active=(carry.Item1.Lerp(work.Item1,workBlend),new Basis(carry.Item2.GetRotationQuaternion().Slerp(work.Item2.GetRotationQuaternion(),workBlend)));
            var (target,basis)=_returning?(_returnPosition,_returnBasis):active;
            if(working&&!cultivating)(target,basis)=GuideWorkTool(target,basis,workBlend);
            var filtered=FilterHeldTool(Holster.Position.Lerp(target,ReachEase(_draw)),new Basis(Holster.Basis.GetRotationQuaternion().Slerp(basis.GetRotationQuaternion(),ReachEase(_draw))));
            if(cultivating&&!_returning&&_draw>=.99f)
                filtered.Position=GuideCultivationEdge(filtered.Position,filtered.Basis,_strokePhase,workBlend);
            SolveHand(1,filtered.Position,filtered.Basis*Grip!.Basis.Inverse(),false,(1-_workBlend)*Smooth(_draw));
            MeasureWorkContact(working);
            CurlHand(1,1);
            if(CurrentTool is ResidentTool.Axe or ResidentTool.Pick or ResidentTool.Hoe or ResidentTool.Spear)
            {
                // The other hand establishes support during the lift, before
                // the work blend begins. Multiplying by that later blend
                // leaves it visibly short of a moving shaft on the first stroke.
                float support=Smooth(Math.Clamp((_draw-.15f)/.65f,0,1))*Smooth(_workBlend)*ReachEase(_supportBlend);
                if(support>.00001f)
                {
                    var heldTransform=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform*Grip!.Transform;
                    var actualGrip=heldTransform.Origin;
                    var leftRest=(Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform)*Grip.Position;
                    var leftBasis=heldTransform.Basis*new Basis(Vector3.Back,MathF.PI/2);
                    if(CurrentTool==ResidentTool.Hoe)leftBasis=AlignHoeSupport(leftBasis,heldTransform.Basis.Y);
                    var restBasis=(Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform).Basis;
                    SolveHand(0,leftRest.Lerp(actualGrip+heldTransform.Basis*new Vector3(0,CurrentTool==ResidentTool.Hoe?.15f:.065f,0),support),new Basis(restBasis.GetRotationQuaternion().Slerp(leftBasis.GetRotationQuaternion(),support)));
                    CurlHand(0,support);
                }
            }
            var actual=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform*Grip!.Transform;
            if(!wantCurrent&&_draw==0&&filtered.Position.DistanceTo(Holster.Position)<.0015f&&_toolVelocity.Length()<.03f
                &&actual.Origin.DistanceTo(Holster.Position)<.004f
                &&MathF.Abs(actual.Basis.GetRotationQuaternion().Dot(Holster.Basis.GetRotationQuaternion()))>MathF.Cos(.04f))AttachTool(Holster);
        }
        else
        {
            if(_releaseTime>0)
            {
                SolveHand(1,Holster.Position,Holster.Basis*Grip!.Basis.Inverse());CurlHand(1,_releaseTime/.16f);_releaseTime=MathF.Max(0,_releaseTime-delta);
            }
            else
            {
                _releaseTime=0;
                // Inventory presentation can change after the fingers have
                // released a holstered tool. Do not wait for the empty arm
                // to reach rest, or replace the tool under a closed hand.
                var available=usingTool?wanted:DefaultTool;
                if(CurrentTool!=available)SelectTool(available);
                if(_reach==0)_toolReachRest=_previousHands[1];
                float reachDuration=usingTool?.48f:Math.Clamp(Holster.Position.DistanceTo(_toolReachRest*Grip!.Position)/.8f,.24f,.48f);
                _reach=Mathf.MoveToward(_reach,usingTool?1:0,delta/reachDuration);
                if(_reach>0)
                {
                    var rest=_toolReachRest;
                    var desired=Holster.Basis*Grip!.Basis.Inverse();
                    float reachWeight=ReachEase(_reach);
                    SolveHand(1,(rest*Grip.Position).Lerp(Holster.Position,reachWeight),
                        new Basis(rest.Basis.GetRotationQuaternion().Slerp(desired.GetRotationQuaternion(),reachWeight)));
                    CurlHand(1,.12f*Math.Clamp((_reach-.65f)/.35f,0,1));
                    var palm=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform*Grip.Transform;
                    bool touching=palm.Origin.DistanceTo(Holster.Position)<.008f
                        &&MathF.Abs(palm.Basis.GetRotationQuaternion().Dot(Holster.Basis.GetRotationQuaternion()))>MathF.Cos(.06f);
                    if(usingTool&&_reach==1&&touching)
                    {
                        _graspTime+=delta;CurlHand(1,.12f+.88f*Smooth(Math.Clamp(_graspTime/.16f,0,1)));
                        if(_graspTime>=.16f){AttachTool(Grip!);_draw=0;_workTime=0;_strokePhase=0;}
                    }
                    else _graspTime=0;
                }
            }
        }
        bool consumeRequested=upperReady&&family==ActionFamily.Consume;
        bool preparingScenePickup=scenePickup&&CompletionVisible&&_scenePickupAge<ScenePickupDuration;
        bool cargoWanted=!reserved&&!_recoveringPickup&&!_releasedCargo&&!WaitingForPlacementParcel&&(_hasCargo||_placingCargo||_placementPending)&&!toolRequested&&!usingTool&&!ToolHeld&&!ConsumptionOwnsHand
            &&!restRequested&&!pickupRequested&&!preparingScenePickup&&!consumeRequested&&_reach==0;
        cargoWanted=RetainCargoDuringTuck(cargoWanted,delta);
        _cargoBlend=Mathf.MoveToward(_cargoBlend,cargoWanted?1:0,delta/.40f);
        bool bareActive=upperReady&&!usingTool&&!ToolHeld&&_reach==0&&_releaseTime==0&&_socialBlend<=.10f&&UsesBareHands(family)
            &&(!ConsumptionOwnsHand||family==ActionFamily.Consume)
            &&(family!=ActionFamily.Pickup||pickup)&&(family!=ActionFamily.Consume||cargoReleased);
        // Compatible actions share hands, targets and cycle duration. Handoff
        // keeps the current trajectory instead of returning to idle first.
        if(bareActive&&CompatibleBareActions(_bareAction,action))_bareAction=action;
        if(bareActive&&_bareBlend==0){_bareAction=action;_bareTime=0;}
        // A half-metre lift cannot share the short tool-grasp timing. Give
        // empty-hand entry/return enough time for the whole limb to travel.
        _bareBlend=Mathf.MoveToward(_bareBlend,bareActive&&_bareAction==action?1:0,delta/.70f);
        if(_bareBlend>0)_bareTime+=delta;
        // One controller owns each arm. Transfers move the same carrying
        // targets instead of solving cargo and then overriding both arms.
        bool cargoArms=_cargoBlend>0&&!usingTool&&!ToolHeld&&_reach==0;
        SetCargoReach(delta);
        if(cargoArms)
            for(int i=0;i<2;i++)
            {
                BlendBareHand(i,CargoSupportTarget(i),PlacementPalmBasis,_cargoBlend);
                float curl=_releasedCargo?Mathf.Lerp(.75f,.10f,ReachEase(_releasedCargoTime/.13f)):.75f;
                CurlHand(i,Mathf.Lerp(_handCurl[i],curl,Smooth(_cargoBlend)));
            }
        bool consuming=_bareBlend>0&&FamilyOf(_bareAction)==ActionFamily.Consume;
        var requestedUse=bareActive&&family==ActionFamily.Consume&&consuming&&(!_finishConsumption||!_observedMouthContact)
            ?action:ActionKind.None;
        PoseConsumption(delta,requestedUse,revision);
        if(_bareBlend>0&&!consuming&&!ConsumptionOwnsHand&&!_recoveringPickup&&!cargoArms&&!usingTool&&!ToolHeld&&_reach==0)
        {
            float cycle=(_bareTime%1.8f)/1.8f;
            float reach=ReachPulse(cycle);
            bool gathering=_bareAction is ActionKind.GatherFood or ActionKind.Take;
            if(gathering)
            {
                if(scenePickup)PoseScenePickupHand(delta,pickup);
                else if(!_plannedPickup)
                {
                    float reachWeight=ReachEase(_bareBlend)*Mathf.SmoothStep(.02f,.50f,gatherPose.Reach);
                    var reachPoint=ReachBodyPoint(new(.16f+.023f*gatherPose.Shift,1.00f-.24f*gatherPose.Reach,-.18f-.10f*gatherPose.Reach));
                    BlendBareHand(1,reachPoint,Basis.FromEuler(new Vector3(.56f+.19f*gatherPose.Reach,0,.10f)),reachWeight,eased:true);
                }
                float balance=gatherPose.Balance;
                BlendBareHand(0,ReachBodyPoint(new(-.235f,.76f+.16f*balance,-.13f-.10f*balance)),
                    Basis.FromEuler(new Vector3(.43f,.18f,-.24f)),ReachEase(_bareBlend)*balance*.68f,eased:true);
            }
            else if(!cargoArms&&_bareAction is ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade)
                for(int i=0;i<2;i++)BlendBareHand(i,BodyPoint(new(i==0?-.12f:.12f,Mathf.Lerp(.85f,.97f,reach),-.30f)),Basis.FromEuler(new Vector3(MathF.PI/2,0,0)));
            float pickupCurl=scenePickup?ScenePickupCurl:Mathf.SmoothStep(.58f,.96f,gatherPose.Reach)*.76f;
            CurlHand(1,Mathf.Lerp(_handCurl[1],gathering?pickupCurl:.3f,Smooth(_bareBlend)));
        }
        if(_socialBlend>.001f&&!usingTool&&!ToolHeld&&_reach==0&&!_hasCargo&&_bareBlend==0)
        {
            for(int side=0;side<2;side++)
            {
                var gesture=SocialPhrase(side,side==0?socialPose.Left:socialPose.Right);
                float weight=socialWeight*gesture.Weight;
                BlendBareHand(side,ReachBodyPoint(gesture.Target),gesture.Palm,weight,eased:true);
                CurlHand(side,Mathf.Lerp(_handCurl[side],.18f,weight));
            }
        }
        ApplyMotionReference();
        PoseHandoff(!ToolHeld&&_reach==0&&_releaseTime==0&&!ConsumptionOwnsHand&&!_recoveringPickup
            &&_cargoBlend<=.001f&&_bareBlend==0&&_socialBlend==0&&restWeight<.20f&&groundPlacement<.15f);
        PosePickupRecovery(delta);
        FinishArmReturns();
        UpdateCarriedObject(delta,cargoWanted);
        // Hand ownership can change in one step. Keep finger flexion
        // continuous across that boundary, while a held tool stays gripped.
        _handCurl[0]=AdvanceFingerCurl(previousCurl.X,_handCurl[0],delta);
        _handCurl[1]=ToolHeld?1:AdvanceFingerCurl(previousCurl.Y,_handCurl[1],delta);
        if(ToolHeld){var held=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform*Grip!.Transform;_heldPosition=held.Origin;_heldBasis=held.Basis;}
        if(_detailedHands)for(int side=0;side<2;side++)
        {
            SyncShoulderBone(side);
            UpdateHandBones(side);
        }
        if(!_detailedHands)for(int side=0;side<2;side++)
        {
            var mesh=_handCurl[side]>.5f?HandClosed[side]:HandOpen[side];
            if(HandProxies[side].Mesh!=mesh)HandProxies[side].Mesh=mesh;
        }
    }
    private static float AdvanceFingerCurl(float previous,float target,float delta)
        =>Mathf.MoveToward(previous,target,delta*(target>previous?10f:7.5f));
    private static float Smooth(float t)=>t*t*(3-2*t);
    public void ShowCargo(bool hasCargo,ResourceKind resource)
    {
        if(!hasCargo){_hasCargo=false;return;}
        if(_releasedCargo&&resource!=_cargoKind)ClearTransientCargo();
        if((_hasCargo||CargoMesh.Visible)&&resource!=_cargoKind){CargoMesh.Visible=false;_cargoBlend=0;}
        _hasCargo=true;_cargoKind=resource;
        if(hasCargo&&CargoMeshes.TryGetValue(resource,out var mesh)&&CargoMesh.Mesh!=mesh)CargoMesh.Mesh=mesh;
    }
    public void SetHandDetail(bool detailed)
    {
        if(_detailedHands==detailed)return;_detailedHands=detailed;
        foreach(var part in _distanceMeshes)part.Node.Mesh=detailed?part.Near:part.Far;
        BodySkin.Mesh=detailed?DetailedBodyMesh:DistantBodyMesh;BodySkin.Skin=detailed?DetailedBodySkin:null!;BodySkin.Skeleton=new NodePath(detailed?"../ShoulderSkeleton":"");
        ShoulderSkeleton.ProcessMode=detailed?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;
        if(detailed)for(int side=0;side<2;side++)
        {
            SyncShoulderBone(side);
            UpdateSleeveCorrectives(side,true);
        }
        for(int side=0;side<2;side++)
        {
            HandSkeletons[side].ProcessMode=detailed?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;
            if(detailed){_skinnedCurl[side]=float.NaN;UpdateHandBones(side);}
            ForearmProxies[side].Visible=!detailed;HandProxies[side].Visible=!detailed;HandSkins[side].Visible=detailed;Thumbs[side].Visible=detailed;
            for(int digit=0;digit<4;digit++)Fingers[side,digit].Visible=detailed;
        }
    }
}
