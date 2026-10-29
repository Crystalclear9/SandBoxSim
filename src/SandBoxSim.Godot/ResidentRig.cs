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
    private float _socialBlend,_attentionYaw;
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
    private float _observedExecution,_completionRemaining;
    public bool CompletionVisible=>_completionRemaining>0;
    public void PresentAction(float delta,ActionKind action,ActionPhase phase,bool paused,float identityPhase)
    {
        if(paused)return;
        delta=PresentationDelta(delta);
        if(action!=_observedAction || (phase==ActionPhase.Moving&&_observedPhase!=ActionPhase.Moving) || (phase==ActionPhase.Executing&&_observedPhase is ActionPhase.Done or ActionPhase.Idle or ActionPhase.Failed)){_observedExecution=0;_completionRemaining=0;}
        bool gesture=ToolFor(action)!=ResidentTool.None||action is ActionKind.GatherFood or ActionKind.Take or ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.Eat or ActionKind.Drink or ActionKind.ShareFood or ActionKind.Trade or ActionKind.Socialize;
        if(phase==ActionPhase.Executing)_observedExecution+=delta;
        if(gesture&&phase==ActionPhase.Done&&(_observedPhase!=ActionPhase.Done||_observedAction!=action))
        {
            _completionAction=action;_completionRemaining=MathF.Max(0,(ToolFor(action)==ResidentTool.None?1.65f:2.1f)-_observedExecution);
        }
        if(phase is ActionPhase.Moving or ActionPhase.Failed)_completionRemaining=0;
        _observedAction=action;_observedPhase=phase;
        if(_completionRemaining>0)
        {
            _completionRemaining=MathF.Max(0,_completionRemaining-delta);
            PoseAction(delta,_completionAction,ActionPhase.Executing,false,identityPhase);
        }
        else PoseAction(delta,action,phase,false,identityPhase);
    }
    private ActionKind _lastAction;
    private ActionPhase _lastPhase;
    private float _phase,_activity,_reach,_draw,_workTime,_graspTime,_releaseTime,_workBlend;
    private bool _carrying;
    public bool ToolHeld {get;private set;}
    public string Handling => ToolHeld ? _returning?"收回工具":_draw<.99f?"取出 / 抬手":_carrying?"手持携带":"使用工具" : _releaseTime>0?"松开手指":_reach==1?"合拢握柄":_reach>.01f?"伸手拿取":_bareBlend>.01f?"徒手交互":CargoMesh.Visible?"双手搬运":_cargoBlend>.01f?(_hasCargo?"托起物资":"放下 / 收手"):"双手放松";
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
        bool fineReach=_hadBarePose||_cargoBlend>0||_socialBlend>.001f||action is ActionKind.Socialize or ActionKind.Eat or ActionKind.Drink or ActionKind.Take or ActionKind.GatherFood or ActionKind.Deposit or ActionKind.ShareFood or ActionKind.Trade or ActionKind.StoreInBuilding;
        float maxStep=fineReach?1f/30:.1f;
        int steps=Math.Max(1,(int)MathF.Ceiling(delta/maxStep));
        for(int step=0;step<steps;step++)PoseActionStep(delta/steps,action,phase,identityPhase);
    }
    private static float PresentationDelta(float delta)=>float.IsFinite(delta)?Math.Clamp(delta,0,1):0;
    private void PoseActionStep(float delta,ActionKind action,ActionPhase phase,float identityPhase)
    {
        _poseStep=delta;
        _bareThisFrame=false;
        for(int side=0;side<2;side++)
        {
            _priorArms[side]=Arms[side].Transform;_priorForearms[side]=Elbows[side].Transform;_priorWrists[side]=Hands[side].Transform;
            _previousHands[side]=Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform;
            _previousElbows[side]=Arms[side].Transform*Elbows[side].Position;
        }
        bool moving=phase==ActionPhase.Moving,executing=phase==ActionPhase.Executing;
        bool resting=executing&&action==ActionKind.Sleep;
        if((_lastAction!=action||_lastPhase!=phase)&&executing){_workTime=0;_strokePhase=0;}
        _lastAction=action;_lastPhase=phase;
        var wanted=(executing||moving)&&!IsChild?ToolFor(action):ResidentTool.None;
        // A long tool is carried in the hand during idle/wandering, rather than
        // hanging diagonally from an invisible waist socket. Bare-hand actions
        // and cargo retain priority and go through the complete stow transition.
        if(wanted==ResidentTool.None&&!IsChild&&!_hasCargo&&!resting&&
            (action is ActionKind.None or ActionKind.Wander||phase is ActionPhase.Idle or ActionPhase.Done))
            wanted=DefaultTool;
        _carrying=wanted!=ResidentTool.None&&(!executing||ToolFor(action)==ResidentTool.None);
        _workBlend=Mathf.MoveToward(_workBlend,executing&&ToolFor(action)!=ResidentTool.None?1:0,delta/.25f);
        bool usingTool=wanted!=ResidentTool.None;
        _phase+=delta*(moving?7.5f:1.8f);_workTime+=delta;
        _activity=Mathf.Lerp(_activity,moving?1:0,1-MathF.Exp(-delta*8));
        float wave=MathF.Sin(_phase+identityPhase),breath=MathF.Sin(_phase*.5f+identityPhase);
        bool pickup=executing&&action is ActionKind.GatherFood or ActionKind.Take;
        _pickupBlend=Mathf.MoveToward(_pickupBlend,pickup?1:0,delta/.24f);
        float vertical=resting?-.09f:Mathf.Lerp(MathF.Abs(wave)*.025f*_activity+breath*.003f,-.094f,Smooth(_pickupBlend));
        if(executing&&usingTool&&ToolHeld&&wanted==CurrentTool&&_draw>=.99f)_strokePhase=Mathf.PosMod(_strokePhase+delta/WorkPeriod,1);
        var stance=WorkSupport(delta,executing&&usingTool&&ToolHeld&&wanted==CurrentTool&&!_carrying,
            (1-_activity)*(1-Smooth(_pickupBlend)));
        float effort=executing&&usingTool?(.5f-WorkLift(_strokePhase+.035f))*.045f*RelativeWorkLoad*Smooth(_workBlend):0;
        float desiredLean=resting?-.16f:pickup?-.58f:usingTool?-.035f-.065f*Smooth(_workBlend)-effort:.025f*_activity;
        var torsoAngles=Torso.Rotation;
        DampMotion(ref torsoAngles,ref _torsoVelocity,new(desiredLean,usingTool?effort*1.8f:0,wave*.018f*_activity),.060f,delta);
        var torsoBasis=Basis.FromEuler(torsoAngles);
        var hip=new Vector3(0,HipHeight,0);Torso.Transform=new(torsoBasis,hip-torsoBasis*hip+stance.Hip+Vector3.Up*vertical+Vector3.Right*(resting||pickup||usingTool?0:MathF.Sin(_phase*.33f+identityPhase)*.008f*(1-_activity)));
        _socialBlend=Mathf.MoveToward(_socialBlend,executing&&action==ActionKind.Socialize?1:0,delta/.70f);
        _attentionYaw=Mathf.Lerp(_attentionYaw,_wantedAttention,1-MathF.Exp(-delta*6));
        float expressionPhase=identityPhase+ExpressionPhase;
        float phrase=Mathf.PosMod(_workTime/3.2f+expressionPhase*.1f,1),phraseEmphasis=ReachPulse(phrase);
        var headTarget=new Vector3(pickup?.15f:usingTool?-.045f-effort*.4f:breath*.018f+MathF.Sin(phrase*MathF.Tau)*.025f*phraseEmphasis*_socialBlend,MathF.Sin(_phase*.32f+identityPhase)*.045f+_attentionYaw,MathF.Sin(phrase*MathF.Tau)*.015f*_socialBlend);
        Head.Quaternion=DampRotation(Head.Quaternion,ref _headVelocity,Basis.FromEuler(headTarget).GetRotationQuaternion(),.075f,delta);
        var neckPivot=new Vector3(0,NeckPivotY*Head.Scale.Y,0);
        Head.Position=HeadRest+neckPivot-Basis.FromEuler(Head.Rotation)*neckPivot;
        for(int i=0;i<2;i++)
        {
            Arms[i].Position=ShoulderRest(i);
            float stride=wave*(i==0?1:-1)*_activity;
            Legs[i].Position=new Vector3(i==0?-HipHalfWidth:HipHalfWidth,HipHeight-.094f*Smooth(_pickupBlend),0)+stance.Hip;
            Legs[i].Rotation=new(Mathf.Lerp(stride*.44f,.55f,Smooth(_pickupBlend))+stance.Thigh,0,0);
            Knees[i].Rotation=new(Mathf.Lerp(-MathF.Max(0,-stride)*.65f,-1.1f,Smooth(_pickupBlend))+stance.Knee,0,0);
            Feet[i].Rotation=new(-(Legs[i].Rotation.X+Knees[i].Rotation.X),0,0);
            float relaxed=(1-_activity)*(usingTool||pickup||resting?0:1);
            float side=i==0?1:-1;
            Arms[i].Rotation=new(-stride*.32f-.045f*relaxed,side*.025f*relaxed,side*(.035f-ArmRestClearance*(usingTool||pickup||resting?0:1)));
            Elbows[i].Rotation=new(.12f+.09f*relaxed+MathF.Max(0,stride)*.22f,0,0);Hands[i].Rotation=Vector3.Zero;
            CurlHand(i,.24f+.045f*MathF.Sin(_phase*.4f+identityPhase+i));
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
            if(!_returning)target=BodyPoint(target);
            var filtered=FilterHeldTool(Holster.Position.Lerp(target,ReachEase(_draw)),new Basis(Holster.Basis.GetRotationQuaternion().Slerp(basis.GetRotationQuaternion(),ReachEase(_draw))));
            SolveHand(1,filtered.Position,filtered.Basis*Grip!.Basis.Inverse(),false,(1-_workBlend)*Smooth(_draw));
            CurlHand(1,1);
            if(CurrentTool is ResidentTool.Axe or ResidentTool.Pick or ResidentTool.Hoe or ResidentTool.Spear)
            {
                float support=Smooth(Math.Clamp((_draw-.15f)/.65f,0,1))*Smooth(_workBlend);
                var heldTransform=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform*Grip!.Transform;
                var actualGrip=heldTransform.Origin;
                var leftRest=(Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform)*Grip.Position;
                var leftBasis=heldTransform.Basis*new Basis(Vector3.Back,MathF.PI/2);
                var restBasis=(Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform).Basis;
                SolveHand(0,leftRest.Lerp(actualGrip+heldTransform.Basis*new Vector3(0,CurrentTool==ResidentTool.Hoe?.15f:.065f,0),support),new Basis(restBasis.GetRotationQuaternion().Slerp(leftBasis.GetRotationQuaternion(),support)));
                CurlHand(0,support);
            }
            if(!wantCurrent&&_draw==0&&filtered.Position.DistanceTo(Holster.Position)<.0015f&&_toolVelocity.Length()<.03f&&MathF.Abs(filtered.Basis.GetRotationQuaternion().Dot(Holster.Basis.GetRotationQuaternion()))>MathF.Cos(.01f))AttachTool(Holster);
        }
        else
        {
            if(usingTool&&CurrentTool!=wanted)SelectTool(wanted);
            if(_releaseTime>0&&!usingTool)
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
                    if(usingTool&&_reach==1)
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
        // Display cargo only after both hands have reached their support pose.
        // Eating and drinking have priority over carrying the full bundle.
        CargoMesh.Visible=cargoWanted&&_cargoBlend>=.999f;
        if(_cargoBlend>0&&!usingTool&&!ToolHeld&&_reach==0)
        {
            for(int i=0;i<2;i++){BlendBareHand(i,BodyPoint(new(i==0?-.12f:.12f,.90f,-.29f)),Basis.FromEuler(new Vector3(MathF.PI/2,0,0)),_cargoBlend);CurlHand(i,Mathf.Lerp(_handCurl[i],.75f,Smooth(_cargoBlend)));}
        }
        bool bareActive=executing&&!usingTool&&!ToolHeld&&_reach==0&&action is ActionKind.GatherFood or ActionKind.Take or ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade or ActionKind.Eat or ActionKind.Drink;
        if(bareActive&&_bareBlend==0){_bareAction=action;_bareTime=0;}
        // A half-metre lift cannot share the short tool-grasp timing. Give
        // empty-hand entry/return enough time for the whole limb to travel.
        _bareBlend=Mathf.MoveToward(_bareBlend,bareActive&&_bareAction==action?1:0,delta/.70f);
        if(_bareBlend>0)_bareTime+=delta;
        if(_bareBlend>0&&!usingTool&&!ToolHeld&&_reach==0)
        {
            bool eating=_bareAction is ActionKind.Eat or ActionKind.Drink;
            float period=eating?2.6f:1.8f;
            float cycle=(_bareTime%period)/period;
            float reach=ReachPulse(cycle);
            bool gathering=_bareAction is ActionKind.GatherFood or ActionKind.Take;
            if(gathering)
                BlendBareHand(1,BodyPoint(new(.16f,Mathf.Lerp(1.00f,.76f,reach),Mathf.Lerp(-.20f,-.26f,reach))),Basis.FromEuler(new Vector3(.7f,0,0)));
            else if(_bareAction is ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade)
                for(int i=0;i<2;i++)BlendBareHand(i,BodyPoint(new(i==0?-.12f:.12f,Mathf.Lerp(.85f,.97f,reach),-.30f)),Basis.FromEuler(new Vector3(MathF.PI/2,0,0)));
            else if(eating)
            {
                var mouth=Head.Transform*Mouth.Position;
                var chest=BodyPoint(new Vector3(.14f,1.04f,-.22f));
                var target=chest.Lerp(mouth+new Vector3(.025f,-.025f,-.075f),reach);
                // Supinate the forearm as the fingers approach the mouth;
                // a forward-facing open palm reads as waving at the cheek.
                var low=Basis.FromEuler(new Vector3(1.05f,0,.12f)).GetRotationQuaternion();
                var high=Basis.FromEuler(new Vector3(2.50f,0,.20f)).GetRotationQuaternion();
                BlendBareHand(1,target,new Basis(low.Slerp(high,reach)));
            }
            CurlHand(1,Mathf.Lerp(_handCurl[1],gathering?Math.Clamp((cycle-.3f)*3,0,.8f):eating?.35f+.40f*reach:.3f,Smooth(_bareBlend)));
        }
        if(_socialBlend>.001f&&!usingTool&&!ToolHeld&&_reach==0&&!_hasCargo)
        {
            for(int side=0;side<2;side++)
            {
                float timing=_workTime*2.1f+expressionPhase*.73f+side*2.4f;
                float emphasis=(.5f+.5f*MathF.Sin(timing))*.045f;
                var rest=(Arms[side].Transform*Elbows[side].Transform*Hands[side].Transform)*(side==1?Grip!.Position:new Vector3(0,-.035f,-.0286f));
                var target=new Vector3(side==0?-.23f:.23f,1.00f+emphasis,-.22f-emphasis);
                var restBasis=Arms[side].Basis*Elbows[side].Basis*Hands[side].Basis;
                var open=Basis.FromEuler(new Vector3(.6f,side==0?.3f:-.3f,side==0?-.35f:.35f));
                float gesture=ReachPulse(Mathf.PosMod(_workTime/3.2f+side*.5f+expressionPhase*.1f,1));
                BlendBareHand(side,BodyPoint(target),open,_socialBlend*gesture);
                CurlHand(side,(.16f+emphasis)*_socialBlend);
            }
        }
        FinishBareReturn(usingTool||ToolHeld||_reach>0||_releaseTime>0);
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
            ShoulderSkeleton.SetBonePoseRotation(side+1,Arms[side].Basis.GetRotationQuaternion());
            ShoulderSkeleton.SetBonePosePosition(side+1,Arms[side].Position);
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
            ShoulderSkeleton.SetBonePoseRotation(side+1,Arms[side].Basis.GetRotationQuaternion());
            ShoulderSkeleton.SetBonePosePosition(side+1,Arms[side].Position);
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
