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
    public void Attend(Vector3 localDirection) => _wantedAttention=localDirection.LengthSquared()>.001f?Math.Clamp(MathF.Atan2(-localDirection.X,-localDirection.Z),-.40f,.40f):0;
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
    private ActionKind _observedAction,_completionAction;
    private ActionPhase _observedPhase;
    private float _observedExecution,_completionRemaining,_completionAge;
    public bool CompletionVisible=>_completionRemaining>0;
    public void PresentAction(float delta,ActionKind action,ActionPhase phase,bool paused,float identityPhase)
    {
        if(paused)return;
        delta=PresentationDelta(delta);
        if(action!=_observedAction || (phase==ActionPhase.Moving&&_observedPhase!=ActionPhase.Moving) || (phase==ActionPhase.Executing&&_observedPhase is ActionPhase.Done or ActionPhase.Idle or ActionPhase.Failed)){_observedExecution=0;_completionRemaining=0;}
        bool gesture=ToolFor(action)!=ResidentTool.None||action is ActionKind.GatherFood or ActionKind.Take or ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.Eat or ActionKind.Drink or ActionKind.ShareFood or ActionKind.Trade or ActionKind.Socialize;
        if(phase==ActionPhase.Executing&&GestureEngaged(action))_observedExecution+=delta;
        if(gesture&&phase==ActionPhase.Done&&(_observedPhase!=ActionPhase.Done||_observedAction!=action))
        {
            _completionAction=action;_completionAge=0;
            _completionRemaining=MathF.Max(0,(ToolFor(action)==ResidentTool.None?1.65f:MathF.Max(.9f,WorkPeriod))-_observedExecution);
        }
        if(phase is ActionPhase.Moving or ActionPhase.Failed)_completionRemaining=0;
        _observedAction=action;_observedPhase=phase;
        if(_completionRemaining>0)
        {
            PoseAction(delta,_completionAction,ActionPhase.Executing,false,identityPhase);
            // Reaching/stowing/arrival is preparation, not a visible work
            // stroke. A short simulation action must not expire in that gap.
            if(GestureEngaged(_completionAction))_completionRemaining=MathF.Max(0,_completionRemaining-delta);
            _completionAge+=delta;
            if(_completionAge>=6)_completionRemaining=0;
        }
        else PoseAction(delta,action,phase,false,identityPhase);
    }
    private ActionKind _lastAction;
    private ActionPhase _lastPhase;
    private float _phase,_activity,_reach,_draw,_workTime,_graspTime,_releaseTime,_workBlend;
    private bool _carrying,_takingTool;
    public bool ToolHeld {get;private set;}
    public string Handling => !InteractionSettled&&_lastPhase==ActionPhase.Executing?"接近目的地 / 停稳":ToolHeld ? _returning?"收回工具":_draw<.99f?"取出 / 抬手":_carrying?"手持携带":"使用工具" : _releaseTime>0?"松开手指":_reach>.01f?(_takingTool?(_reach==1?"合拢握柄":"伸手拿取"):"收回手臂"):CargoMesh.Visible?(_bareBlend>.01f?"双手交接":"双手搬运"):_bareBlend>.01f?_bareAction switch {ActionKind.Eat=>"进食 / 收手",ActionKind.Drink=>"饮水 / 收手",ActionKind.Take or ActionKind.GatherFood=>"弯身取物",_=>"徒手交互"}:_cargoBlend>.01f?(_hasCargo?"托起物资":"放下 / 收手"):"双手放松";
    internal bool GestureEngaged(ActionKind action)=>InteractionSettled&&(ToolFor(action)!=ResidentTool.None
        ?ToolHeld&&CurrentTool==ToolFor(action)&&!_returning&&_draw>=.99f&&_workBlend>.9f
        :action==ActionKind.Socialize?_socialBlend>.9f||_hasCargo&&!ToolHeld:_bareAction==action&&_bareBlend>.9f);
    public static ResidentTool ToolFor(ActionKind action)=>action switch {
        ActionKind.GatherWood=>ResidentTool.Axe,
        ActionKind.GatherStone or ActionKind.GatherIron=>ResidentTool.Pick,
        ActionKind.Farm=>ResidentTool.Hoe,
        ActionKind.BuildHouse or ActionKind.BuildFarm or ActionKind.BuildStorage or ActionKind.BuildMine=>ResidentTool.Hammer,
        ActionKind.Attack or ActionKind.Hunt=>ResidentTool.Spear,_=>ResidentTool.None };
    public string HeldObject=>ToolHeld?CurrentTool switch {ResidentTool.Axe=>"斧头",ResidentTool.Pick=>"镐",ResidentTool.Hoe=>"锄头",ResidentTool.Hammer=>"手锤",ResidentTool.Spear=>"长矛",_=>""}
        :CargoMesh.Visible?_cargoKind switch {ResourceKind.Wood=>"木材捆",ResourceKind.Food=>"食物袋",ResourceKind.Stone=>"石料袋",ResourceKind.Iron=>"铁料袋",_=>"物资"}:"";
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
        delta=PresentationDelta(delta);
        // Distant residents arrive with several frame intervals accumulated.
        // Consume that time in bounded steps instead of discarding everything
        // beyond .1 seconds. A long host stall still has a one-second budget.
        bool fineReach=_hasArmReturn||_reach>0||_releaseTime>0||ToolHeld&&(_draw<.99f||_returning)||!ToolHeld&&ToolFor(action)!=ResidentTool.None||_cargoBlend>0||_socialBlend>.001f||action is ActionKind.Socialize or ActionKind.Eat or ActionKind.Drink or ActionKind.Take or ActionKind.GatherFood or ActionKind.Deposit or ActionKind.ShareFood or ActionKind.Trade or ActionKind.StoreInBuilding;
        ConsumeLocomotionSamples();
        // Mouth approaches combine wrist orientation limits and a very short
        // target distance. Resolve those constraints at one shared cadence so
        // render frequency cannot select a different hand/elbow trajectory.
        bool mouthReach=action is ActionKind.Eat or ActionKind.Drink||_bareBlend>0&&_bareAction is ActionKind.Eat or ActionKind.Drink;
        float maxStep=mouthReach?1f/120:fineReach?1f/30:.1f;
        int steps=Math.Max(1,(int)MathF.Ceiling(delta/maxStep));
        for(int step=0;step<steps;step++)PoseActionStep(delta/steps,action,phase,identityPhase);
    }
    private static float PresentationDelta(float delta)=>float.IsFinite(delta)?Math.Clamp(delta,0,1):0;
    private void PoseActionStep(float delta,ActionKind action,ActionPhase phase,float identityPhase)
    {
        _poseStep=delta;
        for(int side=0;side<2;side++)
        {
            _armSolvedThisStep[side]=false;
            _priorArms[side]=Arms[side].Transform;_priorForearms[side]=Elbows[side].Transform;_priorWrists[side]=Hands[side].Transform;
            _previousHands[side]=Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform;
            _previousElbows[side]=Arms[side].Transform*Elbows[side].Position;
        }
        bool moving=phase==ActionPhase.Moving||_worldLocomotion&&_locomotionSpeed>.04f;
        bool executing=phase==ActionPhase.Executing&&InteractionSettled;
        bool resting=executing&&action==ActionKind.Sleep;
        if((_lastAction!=action||_lastPhase!=phase)&&executing){_workTime=0;_strokePhase=0;}
        _lastAction=action;_lastPhase=phase;
        var wanted=(phase is ActionPhase.Executing or ActionPhase.Moving)&&!IsChild?ToolFor(action):ResidentTool.None;
        // A long tool is carried in the hand during idle/wandering, rather than
        // hanging diagonally from an invisible waist socket. Bare-hand actions
        // and cargo retain priority and go through the complete stow transition.
        if(wanted==ResidentTool.None&&!IsChild&&!_hasCargo&&!resting&&
            (action is ActionKind.None or ActionKind.Wander||phase is ActionPhase.Idle or ActionPhase.Done))
            wanted=DefaultTool;
        _carrying=wanted!=ResidentTool.None&&(!executing||ToolFor(action)==ResidentTool.None);
        // Prepare the working pose during the lift after a secure grasp. The
        // stroke clock below still waits for the lift to finish; waiting to
        // prepare until then would make the support hand chase the first hit.
        _workBlend=Mathf.MoveToward(_workBlend,executing&&ToolHeld&&CurrentTool==wanted&&_draw>=.25f&&ToolFor(action)!=ResidentTool.None?1:0,delta/.40f);
        bool usingTool=wanted!=ResidentTool.None;
        _takingTool=usingTool&&!ToolHeld;
        _phase+=delta*(moving?7.5f:1.8f);_workTime+=delta;
        AdvanceLocomotion(delta,moving);
        float wave=MathF.Cos(_gaitCycle*MathF.Tau),breath=MathF.Sin(_phase*.5f+identityPhase);
        bool pickup=executing&&!ToolHeld&&_reach==0&&_releaseTime==0&&(_bareBlend==0||_bareAction is ActionKind.GatherFood or ActionKind.Take)&&action is ActionKind.GatherFood or ActionKind.Take;
        _pickupBlend=Mathf.MoveToward(_pickupBlend,pickup?1:0,delta/.48f);
        float vertical=resting?-.09f:Mathf.Lerp(breath*.003f*(1-_activity),-.094f,Smooth(_pickupBlend));
        float gaitWeight=_activity*(1-Smooth(_pickupBlend))*(resting?0:1);
        if(executing&&usingTool&&ToolHeld&&wanted==CurrentTool&&_draw>=.99f)_strokePhase=Mathf.PosMod(_strokePhase+delta/WorkPeriod,1);
        var stance=WorkSupport(delta,executing&&usingTool&&ToolHeld&&wanted==CurrentTool&&!_carrying,
            (1-_activity)*(1-Smooth(_pickupBlend)));
        float effort=executing&&usingTool?(.5f-WorkLift(_strokePhase+.035f))*.045f*RelativeWorkLoad*Smooth(_workBlend):0;
        float desiredLean=resting?-.16f:Mathf.Lerp(usingTool?-.035f-.065f*Smooth(_workBlend)-effort:-.025f*_activity+.007f*MathF.Sin(_phase*.53f+identityPhase)*(1-_activity),-.58f,Smooth(_pickupBlend));
        var torsoAngles=Torso.Rotation;
        DampMotion(ref torsoAngles,ref _torsoVelocity,new(desiredLean,(usingTool?effort*1.8f:0)+wave*GaitPelvisYaw*.65f*gaitWeight+_gaitTurn*.16f*gaitWeight,MathF.Sin(_gaitCycle*MathF.Tau)*GaitPelvisRoll*.35f*gaitWeight),.060f,delta);
        var torsoBasis=Basis.FromEuler(torsoAngles);
        var restSupport=RestSupport(delta,!moving&&_activity<.001f&&!usingTool&&!resting&&!pickup,identityPhase);
        var gaitOffset=_gaitOffset*(1-Smooth(_pickupBlend))*(resting?0:1);
        var translation=stance.Hip+Vector3.Up*vertical+restSupport+gaitOffset;
        var hip=new Vector3(0,HipHeight,0);Torso.Transform=new(torsoBasis,hip-torsoBasis*hip+translation);
        UpdateBodyBend(stance.Hip+restSupport+gaitOffset-Vector3.Up*(.094f*Smooth(_pickupBlend)),new Basis(Quaternion.Identity.Slerp(GaitPelvisBasis.GetRotationQuaternion(),(1-Smooth(_pickupBlend))*(resting?0:1))));
        bool socialReady=executing&&action==ActionKind.Socialize&&!ToolHeld&&_reach==0&&_releaseTime==0&&!_hasCargo&&_bareBlend==0;
        if(socialReady&&_socialBlend==0)_socialTime=0;
        _socialBlend=Mathf.MoveToward(_socialBlend,socialReady?1:0,delta/.70f);
        if(_socialBlend>0)_socialTime+=delta;
        _attentionYaw=Mathf.Lerp(_attentionYaw,_wantedAttention,1-MathF.Exp(-delta*6));
        float expressionPhase=identityPhase+ExpressionPhase;
        float phrase=Mathf.PosMod(_workTime/3.2f+expressionPhase*.1f,1),phraseEmphasis=ReachPulse(phrase);
        var headTarget=new Vector3(Mathf.Lerp(usingTool?-.045f-effort*.4f:breath*.018f+MathF.Sin(phrase*MathF.Tau)*.025f*phraseEmphasis*_socialBlend,.15f,Smooth(_pickupBlend)),MathF.Sin(_phase*.32f+identityPhase)*.045f*(1-_activity)+_attentionYaw+_gaitTurn*.38f*gaitWeight,MathF.Sin(phrase*MathF.Tau)*.015f*_socialBlend);
        Head.Quaternion=DampRotation(Head.Quaternion,ref _headVelocity,Basis.FromEuler(headTarget).GetRotationQuaternion(),.075f,delta);
        var neckPivot=new Vector3(0,NeckPivotY*Head.Scale.Y,0);
        Head.Position=HeadRest+neckPivot-Basis.FromEuler(Head.Rotation)*neckPivot;
        for(int i=0;i<2;i++)
        {
            Arms[i].Position=ShoulderRest(i);
            var pelvis=LowerGarmentFrame;
            Legs[i].Position=pelvis*new Vector3(i==0?-HipHalfWidth:HipHalfWidth,HipHeight,0);
            Legs[i].Rotation=new(.55f*Smooth(_pickupBlend)+stance.Thigh,0,0);
            Knees[i].Rotation=new(-1.1f*Smooth(_pickupBlend)+stance.Knee,0,0);
            Feet[i].Rotation=new(-(Legs[i].Rotation.X+Knees[i].Rotation.X),0,0);
            if(_restSupport>.00001f)PlantRestLeg(i);
            PlantPickupLeg(i,Smooth(_pickupBlend));
            PoseWalkingLeg(i,gaitWeight);
            PoseFreeArm(i,delta,identityPhase);
        }
        SyncTrousers();
        _faceTime+=delta;
        if(_detailedHead)
        {
            float blink=FaceBlink(_faceTime,expressionPhase);
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
            var work=WorkPose(CurrentTool,_strokePhase);
            var carry=CarryPose(CurrentTool,wave*_activity);
            var active=(carry.Item1.Lerp(work.Item1,Smooth(_workBlend)),new Basis(carry.Item2.GetRotationQuaternion().Slerp(work.Item2.GetRotationQuaternion(),Smooth(_workBlend))));
            var (target,basis)=_returning?(_returnPosition,_returnBasis):active;
            if(!_returning)target=ReachBodyPoint(target);
            var filtered=FilterHeldTool(Holster.Position.Lerp(target,ReachEase(_draw)),new Basis(Holster.Basis.GetRotationQuaternion().Slerp(basis.GetRotationQuaternion(),ReachEase(_draw))));
            SolveHand(1,filtered.Position,filtered.Basis*Grip!.Basis.Inverse(),false,(1-_workBlend)*Smooth(_draw));
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
            if(usingTool&&CurrentTool!=wanted)SelectTool(wanted);
            if(_releaseTime>0)
            {
                SolveHand(1,Holster.Position,Holster.Basis*Grip!.Basis.Inverse());CurlHand(1,_releaseTime/.16f);_releaseTime=MathF.Max(0,_releaseTime-delta);
            }
            else
            {
                _releaseTime=0;
                if(_reach==0)_toolReachRest=_previousHands[1];
                _reach=Mathf.MoveToward(_reach,usingTool?1:0,delta/.48f);
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
                else if(!usingTool&&CurrentTool!=DefaultTool)SelectTool(DefaultTool);
            }
        }
        bool consuming=executing&&action is ActionKind.Eat or ActionKind.Drink;
        bool cargoWanted=_hasCargo&&!usingTool&&!ToolHeld&&!resting&&!pickup&&!consuming&&_reach==0;
        _cargoBlend=Mathf.MoveToward(_cargoBlend,cargoWanted?1:0,delta/.40f);
        bool bareActive=executing&&!usingTool&&!ToolHeld&&_reach==0&&_releaseTime==0&&action is ActionKind.GatherFood or ActionKind.Take or ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade or ActionKind.Eat or ActionKind.Drink;
        if(bareActive&&_bareBlend==0){_bareAction=action;_bareTime=0;}
        // A half-metre lift cannot share the short tool-grasp timing. Give
        // empty-hand entry/return enough time for the whole limb to travel.
        _bareBlend=Mathf.MoveToward(_bareBlend,bareActive&&_bareAction==action?1:0,delta/.70f);
        if(_bareBlend>0)_bareTime+=delta;
        // One controller owns each arm. Transfers move the same carrying
        // targets instead of solving cargo and then overriding both arms.
        bool cargoArms=_cargoBlend>0&&!usingTool&&!ToolHeld&&_reach==0&&!consuming;
        _cargoLift=_bareBlend>0&&_bareAction is ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade
            ?.07f*ReachPulse((_bareTime%1.8f)/1.8f)*Smooth(_bareBlend):0;
        if(cargoArms)
            for(int i=0;i<2;i++)
            {
                BlendBareHand(i,CargoSupportTarget(i),Basis.FromEuler(new Vector3(MathF.PI/2,0,0)),_cargoBlend);
                CurlHand(i,Mathf.Lerp(_handCurl[i],.75f,Smooth(_cargoBlend)));
            }
        if(_bareBlend>0&&!cargoArms&&!usingTool&&!ToolHeld&&_reach==0)
        {
            bool eating=_bareAction is ActionKind.Eat or ActionKind.Drink;
            float period=eating?2.6f:1.8f;
            float cycle=(_bareTime%period)/period;
            float reach=ReachPulse(cycle);
            bool gathering=_bareAction is ActionKind.GatherFood or ActionKind.Take;
            if(gathering)
                BlendBareHand(1,ReachBodyPoint(new(.16f,Mathf.Lerp(1.00f,.76f,reach),Mathf.Lerp(-.20f,-.26f,reach))),Basis.FromEuler(new Vector3(.7f,0,0)));
            else if(!cargoArms&&_bareAction is ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade)
                for(int i=0;i<2;i++)BlendBareHand(i,BodyPoint(new(i==0?-.12f:.12f,Mathf.Lerp(.85f,.97f,reach),-.30f)),Basis.FromEuler(new Vector3(MathF.PI/2,0,0)));
            else if(eating)
            {
                var mouth=Head.Transform*Mouth.Position;
                var chest=ReachBodyPoint(new Vector3(.14f,1.04f,-.22f));
                float desiredCurl=_bareAction==ActionKind.Drink?.40f+.35f*reach:.32f+.18f*reach;
                float curl=Mathf.Lerp(_handCurl[1],desiredCurl,Smooth(_bareBlend));
                // Track the displayed finger pad, not a palm centre several
                // centimetres below it. Planning from the previous displayed
                // orientation also includes the residual wrist limit without
                // solving the elbow a second time in this step.
                var prior=_previousHands[1].Basis;
                var contact=mouth+Head.Basis*new Vector3(.012f,-.004f,-.022f);
                var goal=contact+prior*(Grip!.Position-IndexTipOffset(1,curl));
                var target=chest.Lerp(goal,reach);
                // Supinate the forearm as the fingers approach the mouth;
                // a forward-facing open palm reads as waving at the cheek.
                var low=Basis.FromEuler(new Vector3(1.05f,0,.12f)).GetRotationQuaternion();
                var high=Basis.FromEuler(new Vector3(3.20f,0,.20f)).GetRotationQuaternion();
                BlendBareHand(1,target,new Basis(low.Slerp(high,reach)));
            }
            if(!cargoArms)CurlHand(1,Mathf.Lerp(_handCurl[1],gathering?Math.Clamp((cycle-.3f)*3,0,.8f):eating?(_bareAction==ActionKind.Drink?.40f+.35f*reach:.32f+.18f*reach):.3f,Smooth(_bareBlend)));
        }
        if(_socialBlend>.001f&&!usingTool&&!ToolHeld&&_reach==0&&!_hasCargo&&_bareBlend==0)
        {
            for(int side=0;side<2;side++)
            {
                float timing=_socialTime*2.1f+expressionPhase*.73f+side*2.4f;
                float emphasis=(.5f+.5f*MathF.Sin(timing))*.045f;
                var rest=(Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform)*(side==1?Grip!.Position:new Vector3(0,-.035f,-.0286f));
                var target=new Vector3(side==0?-.23f:.23f,1.00f+emphasis,-.22f-emphasis);
                var restBasis=Arms[side].Basis*Elbows[side].Basis*Hands[side].Basis;
                var open=Basis.FromEuler(new Vector3(.6f,side==0?.3f:-.3f,side==0?-.35f:.35f));
                float gesture=ReachPulse(Mathf.PosMod(_socialTime/3.2f+side*.5f+expressionPhase*.1f,1));
                BlendBareHand(side,ReachBodyPoint(target),open,_socialBlend*gesture);
                CurlHand(side,(.16f+emphasis)*_socialBlend);
            }
        }
        FinishArmReturns();
        // A blend timer may finish before the speed-limited hands arrive.
        // Keep an established load attached, but require both actual palms
        // near their support targets before its first displayed frame.
        bool supported=true;
        for(int side=0;side<2;side++)
        {
            var hand=Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform;
            supported&=(hand*Grip!.Position).DistanceTo(CargoSupportTarget(side))<.025f;
        }
        CargoMesh.Visible=cargoWanted&&_cargoBlend>=.999f&&(CargoMesh.Visible||supported);
        if(CargoMesh.Visible)
        {
            var right=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform;
            var left=Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform;
            var center=((right*Grip!.Position)+(left*Grip.Position))*.5f+new Vector3(0,_cargoKind==ResourceKind.Wood?.05f:.13f,-.04f);
            CargoMesh.Transform=right.AffineInverse()*new Transform3D(Basis.Identity,center);
        }
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
    private static float Smooth(float t)=>t*t*(3-2*t);
    public void ShowCargo(bool hasCargo,ResourceKind resource)
    {
        if(hasCargo&&_hasCargo&&resource!=_cargoKind){CargoMesh.Visible=false;_cargoBlend=0;}
        _hasCargo=hasCargo;_cargoKind=resource;
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
