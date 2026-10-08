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
    public Node3D? Headwear,Grip,PackMount;
    public bool IsChild;
    public ResidentTool DefaultTool;
    public ResidentTool CurrentTool {get;private set;}
    public MeshInstance3D ToolMesh=null!;
    public readonly Dictionary<ResidentTool,Mesh> ToolMeshes=new();
    public readonly Node3D[] Arms=new Node3D[2],Elbows=new Node3D[2],Hands=new Node3D[2],Thumbs=new Node3D[2],Legs=new Node3D[2],Knees=new Node3D[2],Feet=new Node3D[2];
    public readonly Node3D[,] Fingers=new Node3D[2,4],FingerMiddles=new Node3D[2,4],Fingertips=new Node3D[2,4];
    public readonly Node3D[] ThumbTips=new Node3D[2];
    public readonly MeshInstance3D[] HandProxies=new MeshInstance3D[2],HandSkins=new MeshInstance3D[2];
    public readonly Mesh[] HandOpen=new Mesh[2],HandClosed=new Mesh[2];
    public MeshInstance3D HeadSkin=null!,HeadProxy=null!,BodySkin=null!,ShoulderBridge=null!;
    public Skeleton3D ShoulderSkeleton=null!;
    public readonly Skeleton3D[] HandSkeletons=new Skeleton3D[2];
    public Mesh ShoulderGeometry=null!,DetailedBodyMesh=null!,DistantBodyMesh=null!;
    public Skin DetailedBodySkin=null!;
    public readonly MeshInstance3D[] Eyelids=new MeshInstance3D[2],ShoulderFlesh=new MeshInstance3D[2];
    public readonly Node3D[] Eyes=new Node3D[2];
    public Node3D Mouth=null!;
    public Vector3 MouthRest;
    public ShaderMaterial EyeMaterial=null!;
    public ShaderMaterial LidMaterial=null!;
    private bool _detailedHead=true,_returning;
    private float _faceTime;
    private Vector3 _returnPosition,_heldPosition;
    private Basis _returnBasis,_heldBasis;
    public void SetHeadDetail(bool detailed)
    {
        if(_detailedHead==detailed)return;_detailedHead=detailed;HeadSkin.Visible=detailed;HeadProxy.Visible=!detailed;Mouth.Visible=detailed;
        for(int i=0;i<2;i++){Eyes[i].Visible=detailed;Eyelids[i].Visible=detailed;}
    }
    private bool _detailedHands=true;
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
        if(action!=_observedAction || (phase==ActionPhase.Moving&&_observedPhase!=ActionPhase.Moving) || (phase==ActionPhase.Executing&&_observedPhase is ActionPhase.Done or ActionPhase.Idle or ActionPhase.Failed)){_observedExecution=0;_completionRemaining=0;}
        bool gesture=ToolFor(action)!=ResidentTool.None||action is ActionKind.GatherFood or ActionKind.Take or ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.Eat or ActionKind.Drink or ActionKind.ShareFood or ActionKind.Trade;
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
    private float _phase,_activity,_reach,_draw,_workTime,_graspTime,_releaseTime,_lean;
    public bool ToolHeld {get;private set;}
    public string Handling => ToolHeld ? _returning?"收回工具":_draw<.99f?"取出 / 抬手":"使用工具" : _releaseTime>0?"松开手指":_reach==1?"合拢握柄":_reach>.01f?"伸手拿取":"工具收纳";
    public static ResidentTool ToolFor(ActionKind action)=>action switch {
        ActionKind.GatherWood=>ResidentTool.Axe,
        ActionKind.GatherStone or ActionKind.GatherIron=>ResidentTool.Pick,
        ActionKind.Farm=>ResidentTool.Hoe,
        ActionKind.BuildHouse or ActionKind.BuildFarm or ActionKind.BuildStorage or ActionKind.BuildMine=>ResidentTool.Hammer,
        ActionKind.Attack or ActionKind.Hunt=>ResidentTool.Spear,_=>ResidentTool.None };
    public void SelectTool(ResidentTool kind)
    {
        CurrentTool=kind;ToolMesh.Visible=kind!=ResidentTool.None;
        if(kind!=ResidentTool.None)ToolMesh.Mesh=ToolMeshes[kind];
    }
    private void AttachTool(Node3D parent)
    {
        ToolMesh.GetParent().RemoveChild(ToolMesh);parent.AddChild(ToolMesh);ToolMesh.Transform=Transform3D.Identity;
        ToolHeld=parent==Grip;
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
        delta=Math.Clamp(delta,0,.1f);
        bool moving=phase==ActionPhase.Moving,executing=phase==ActionPhase.Executing;
        bool resting=executing&&action==ActionKind.Sleep;
        if((_lastAction!=action||_lastPhase!=phase)&&executing)_workTime=0;
        _lastAction=action;_lastPhase=phase;
        var wanted=(executing||moving)&&!IsChild?ToolFor(action):ResidentTool.None;
        bool usingTool=wanted!=ResidentTool.None;
        _phase+=delta*(moving?7.5f:1.8f);_workTime+=delta;
        _activity=Mathf.Lerp(_activity,moving?1:0,1-MathF.Exp(-delta*8));
        float wave=MathF.Sin(_phase+identityPhase),breath=MathF.Sin(_phase*.5f+identityPhase);
        bool pickup=executing&&action is ActionKind.GatherFood or ActionKind.Take;
        float vertical=resting?-.09f:pickup?-.094f:MathF.Abs(wave)*.025f*_activity+breath*.003f;
        float effort=executing&&usingTool?MathF.Sin(_workTime*4)*.035f:0;
        float desiredLean=resting?-.16f:pickup?-.58f:usingTool?-.10f-effort:.025f*_activity;
        _lean=Mathf.Lerp(_lean,desiredLean,1-MathF.Exp(-delta*12));
        var torsoBasis=Basis.FromEuler(new Vector3(_lean,usingTool?effort*.3f:0,wave*.018f*_activity));
        var hip=new Vector3(0,.67f,0);Torso.Transform=new(torsoBasis,hip-torsoBasis*hip+Vector3.Up*vertical);
        Head.Rotation=new(pickup?.15f:usingTool?-.045f-effort*.4f:breath*.018f,MathF.Sin(_phase*.32f+identityPhase)*.045f,0);
        for(int i=0;i<2;i++)
        {
            float stride=wave*(i==0?1:-1)*_activity;
            Legs[i].Position=new(i==0?-.10f:.10f,.67f+(pickup?-.094f:0),0);
            Legs[i].Rotation=new(pickup?.55f:stride*.44f,0,0);
            Knees[i].Rotation=new(pickup?-1.1f:-MathF.Max(0,-stride)*.65f,0,0);
            Feet[i].Rotation=new(-(Legs[i].Rotation.X+Knees[i].Rotation.X),0,0);
            Arms[i].Rotation=new(-stride*.32f,0,i==0?.055f:-.055f);
            Elbows[i].Rotation=new(.12f+MathF.Max(0,stride)*.22f,0,0);Hands[i].Rotation=Vector3.Zero;
            CurlHand(i,0);
        }
        _faceTime+=delta;
        if(_detailedHead)
        {
            float blinkPhase=(_faceTime+1.1f+identityPhase*2.3f)%4.7f;
            float blink=blinkPhase<.28f?MathF.Sin(blinkPhase/.28f*MathF.PI):0;
            LidMaterial.SetShaderParameter("blink",blink);
            for(int eye=0;eye<2;eye++){Eyes[eye].Scale=new(1,MathF.Max(.02f,1-blink),1);Eyes[eye].Rotation=Vector3.Zero;}
            EyeMaterial.SetShaderParameter("gaze",new Vector2(MathF.Sin(_faceTime*.7f+identityPhase)*.035f,0));
            float speech=executing&&action is ActionKind.Eat or ActionKind.Drink or ActionKind.Socialize ? MathF.Abs(MathF.Sin(_faceTime*5)) : 0;
            Mouth.Position=MouthRest+new Vector3(0,-speech*.0007f,0);Mouth.Scale=new(1,1+speech*.35f,1);
        }
        bool wantCurrent=usingTool&&wanted==CurrentTool;
        if(ToolHeld)
        {
            _draw=Mathf.MoveToward(_draw,wantCurrent?1:0,delta/.28f);
            if(!wantCurrent&&!_returning)
            {
                _returning=true;
                _returnPosition=_heldPosition;_returnBasis=_heldBasis;
            }
            if(wantCurrent)_returning=false;
            var (target,basis)=_returning?(_returnPosition,_returnBasis):WorkPose(CurrentTool,executing?_workTime:0);
            SolveHand(1,Holster.Position.Lerp(target,Smooth(_draw)),new Basis(Holster.Basis.GetRotationQuaternion().Slerp(basis.GetRotationQuaternion(),Smooth(_draw)))*Grip!.Basis.Inverse());
            CurlHand(1,1);
            if(CurrentTool is ResidentTool.Axe or ResidentTool.Pick or ResidentTool.Hoe or ResidentTool.Spear)
            {
                float support=Smooth(Math.Clamp((_draw-.15f)/.65f,0,1));
                var heldTransform=Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform*Grip!.Transform;
                var actualGrip=heldTransform.Origin;
                var leftRest=(Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform)*Grip.Position;
                var leftBasis=heldTransform.Basis*new Basis(Vector3.Back,MathF.PI/2);
                var restBasis=(Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform).Basis;
                SolveHand(0,leftRest.Lerp(actualGrip+heldTransform.Basis*new Vector3(0,CurrentTool==ResidentTool.Hoe?.15f:.065f,0),support),new Basis(restBasis.GetRotationQuaternion().Slerp(leftBasis.GetRotationQuaternion(),support)));
                CurlHand(0,support);
            }
            if(!wantCurrent&&_draw==0)AttachTool(Holster);
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
                _reach=Mathf.MoveToward(_reach,usingTool?1:0,delta/.36f);
                if(_reach>0)
                {
                    var rest=(Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform)*Grip!.Position;
                    SolveHand(1,rest.Lerp(Holster.Position,Smooth(_reach)),Holster.Basis*Grip!.Basis.Inverse());
                    CurlHand(1,.12f*Math.Clamp((_reach-.65f)/.35f,0,1));
                    if(usingTool&&_reach==1)
                    {
                        _graspTime+=delta;CurlHand(1,.12f+.88f*Smooth(Math.Clamp(_graspTime/.16f,0,1)));
                        if(_graspTime>=.16f){AttachTool(Grip!);_draw=0;_workTime=0;}
                    }
                    else _graspTime=0;
                }
                else if(!usingTool&&CurrentTool!=DefaultTool)SelectTool(DefaultTool);
            }
        }
        CargoMesh.Visible=_hasCargo&&!usingTool&&!ToolHeld&&!resting&&!pickup&&_reach==0;
        if(_hasCargo&&!usingTool&&!ToolHeld&&!resting&&!pickup&&_reach==0)
        {
            for(int i=0;i<2;i++){SolveBareHand(i,new(i==0?-.12f:.12f,.90f,-.29f),Basis.FromEuler(new Vector3(MathF.PI/2,0,0)));CurlHand(i,.75f);}
        }
        if(executing&&!usingTool&&!ToolHeld&&_reach==0)
        {
            float cycle=(_workTime%1.6f)/1.6f;
            float reach=MathF.Sin(cycle*MathF.PI);
            if(pickup)
                SolveBareHand(1,new(.16f,Mathf.Lerp(1.00f,.76f,reach),Mathf.Lerp(-.20f,-.26f,reach)),Basis.FromEuler(new Vector3(.7f,0,0)));
            else if(action is ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade)
                for(int i=0;i<2;i++)SolveBareHand(i,new(i==0?-.12f:.12f,Mathf.Lerp(.85f,.97f,reach),-.30f),Basis.FromEuler(new Vector3(MathF.PI/2,0,0)));
            else if(action is ActionKind.Eat or ActionKind.Drink)
                SolveBareHand(1,new(.03f,1.31f,-.19f),Basis.FromEuler(new Vector3(.6f,0,.35f)));
            CurlHand(1,pickup?Math.Clamp((cycle-.3f)*3,0,.8f):.3f);
        }
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
        BodySkin.Mesh=detailed?DetailedBodyMesh:DistantBodyMesh;BodySkin.Skin=detailed?DetailedBodySkin:null!;BodySkin.Skeleton=new NodePath(detailed?"../ShoulderSkeleton":"");
        ShoulderSkeleton.ProcessMode=detailed?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;
        if(detailed)for(int side=0;side<2;side++)ShoulderSkeleton.SetBonePoseRotation(side+1,Arms[side].Basis.GetRotationQuaternion());
        for(int side=0;side<2;side++)
        {
            HandSkeletons[side].ProcessMode=detailed?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;
            if(detailed){_skinnedCurl[side]=float.NaN;UpdateHandBones(side);}
            ShoulderFlesh[side].Visible=detailed;HandProxies[side].Visible=!detailed;HandSkins[side].Visible=detailed;Thumbs[side].Visible=detailed;
            for(int digit=0;digit<4;digit++)Fingers[side,digit].Visible=detailed;
        }
    }
}
