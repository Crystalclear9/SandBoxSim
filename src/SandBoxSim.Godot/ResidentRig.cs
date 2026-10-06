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
    public readonly Node3D[,] Fingers=new Node3D[2,4],Fingertips=new Node3D[2,4];
    public readonly MeshInstance3D[] HandProxies=new MeshInstance3D[2],HandSkins=new MeshInstance3D[2];
    public readonly Mesh[] HandOpen=new Mesh[2],HandClosed=new Mesh[2];
    public MeshInstance3D HeadSkin=null!,HeadProxy=null!;
    public void SetHeadDetail(bool detailed){if(HeadSkin.Visible!=detailed){HeadSkin.Visible=detailed;HeadProxy.Visible=!detailed;}}
    private bool _detailedHands=true;
    private readonly float[] _handCurl=new float[2];
    public MeshInstance3D CargoMesh=null!;
    public static readonly ResourceKind[] CargoKinds={ResourceKind.Food,ResourceKind.Wood,ResourceKind.Stone,ResourceKind.Iron};
    public readonly Dictionary<ResourceKind,Mesh> CargoMeshes=new();
    private bool _hasCargo;
    private ActionKind _lastAction;
    private ActionPhase _lastPhase;
    private float _phase,_activity,_reach,_draw,_workTime;
    public bool ToolHeld {get;private set;}
    public string Handling => ToolHeld ? _draw<.99f?"抬手 / 收回":"使用工具" : _reach>.01f?"伸手拿取":"工具收纳";
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
        var torsoBasis=Basis.FromEuler(new Vector3(resting?-.16f:pickup?-.58f:usingTool?-.10f:.025f*_activity,0,wave*.018f*_activity));
        var hip=new Vector3(0,.67f,0);Torso.Transform=new(torsoBasis,hip-torsoBasis*hip+Vector3.Up*vertical);
        Head.Rotation=new(pickup?.15f:breath*.018f,MathF.Sin(_phase*.32f+identityPhase)*.045f,0);
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
        bool wantCurrent=usingTool&&wanted==CurrentTool;
        if(ToolHeld)
        {
            _draw=Mathf.MoveToward(_draw,wantCurrent?1:0,delta/.28f);
            var (target,basis)=WorkPose(CurrentTool,executing?_workTime:0);
            SolveHand(1,Holster.Position.Lerp(target,Smooth(_draw)),new Basis(Quaternion.Identity.Slerp(basis.GetRotationQuaternion(),Smooth(_draw)))*Grip!.Basis.Inverse());
            CurlHand(1,1);
            if(CurrentTool is ResidentTool.Axe or ResidentTool.Pick or ResidentTool.Hoe or ResidentTool.Spear)
            {
                float support=Smooth(Math.Clamp((_draw-.15f)/.65f,0,1));
                var actualGrip=(Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform)*Grip!.Position;
                var leftRest=(Arms[0].Transform*Elbows[0].Transform*Hands[0].Transform)*Grip.Position;
                SolveHand(0,leftRest.Lerp(actualGrip+basis*new Vector3(0,.055f,0),support),new Basis(Quaternion.Identity.Slerp(basis.GetRotationQuaternion(),support))*Grip.Basis.Inverse());
                CurlHand(0,support);
            }
            if(!wantCurrent&&_draw==0)AttachTool(Holster);
        }
        else
        {
            if(usingTool&&CurrentTool!=wanted)SelectTool(wanted);
            _reach=Mathf.MoveToward(_reach,usingTool?1:0,delta/.36f);
            if(_reach>0)
            {
                var rest=(Arms[1].Transform*Elbows[1].Transform*Hands[1].Transform)*Grip!.Position;
                SolveHand(1,rest.Lerp(Holster.Position,Smooth(_reach)),Grip!.Basis.Inverse());
                CurlHand(1,Math.Clamp((_reach-.65f)/.35f,0,1));
                if(usingTool&&_reach==1){AttachTool(Grip!);_draw=0;_workTime=0;}
            }
            else if(!usingTool&&CurrentTool!=DefaultTool)SelectTool(DefaultTool);
        }
        CargoMesh.Visible=_hasCargo&&!usingTool&&!ToolHeld&&!resting;
        if(_hasCargo&&!usingTool&&!ToolHeld&&!resting&&_reach==0)
        {
            for(int i=0;i<2;i++){SolveHand(i,new(i==0?-.12f:.12f,.90f,-.29f),Basis.Identity);CurlHand(i,.75f);}
        }
        if(executing&&!usingTool&&!ToolHeld&&_reach==0)
        {
            float cycle=(_workTime%1.6f)/1.6f;
            float reach=MathF.Sin(cycle*MathF.PI);
            if(pickup)
                SolveHand(1,new(.16f,Mathf.Lerp(1.00f,.70f,reach),Mathf.Lerp(-.20f,-.32f,reach)),Basis.FromEuler(new Vector3(.4f,0,0)));
            else if(action is ActionKind.Deposit or ActionKind.StoreInBuilding or ActionKind.ShareFood or ActionKind.Trade)
                for(int i=0;i<2;i++)SolveHand(i,new(i==0?-.15f:.15f,Mathf.Lerp(.85f,.97f,reach),-.30f),Basis.Identity);
            else if(action is ActionKind.Eat or ActionKind.Drink)
                SolveHand(1,new(.03f,1.31f,-.19f),Basis.FromEuler(new Vector3(-.3f,0,0)));
            CurlHand(1,pickup?Math.Clamp((cycle-.3f)*3,0,.8f):.3f);
        }
        if(!_detailedHands)for(int side=0;side<2;side++)
        {
            var mesh=_handCurl[side]>.5f?HandClosed[side]:HandOpen[side];
            if(HandProxies[side].Mesh!=mesh)HandProxies[side].Mesh=mesh;
        }
    }
    private static float Smooth(float t)=>t*t*(3-2*t);
    private static (Vector3,Basis) WorkPose(ResidentTool kind,float time)
    {
        float period=kind==ResidentTool.Hoe?1.55f:kind==ResidentTool.Pick?1.35f:kind==ResidentTool.Axe?1.25f:1.05f;
        float t=(time%period)/period;
        // Slow preparation, fast contact and a short recovery, instead of a continuous pendulum.
        float lift=t<.55f?Smooth(t/.55f):t<.72f?1-Smooth((t-.55f)/.17f):0;
        if(kind==ResidentTool.Hoe)return (new(.10f,.76f+lift*.18f,-.33f+lift*.08f),Basis.FromEuler(new Vector3(.25f+lift*.55f,0,-.12f)));
        if(kind==ResidentTool.Spear)return (new(.10f,.94f,-.28f-lift*.15f),Basis.FromEuler(new Vector3(-1.1f,0,0)));
        return (new(kind==ResidentTool.Hammer?.20f:.10f,.92f+lift*.32f,-.36f+lift*.16f),Basis.FromEuler(new Vector3(.55f+lift*1.65f,0,kind==ResidentTool.Axe?lift*-.35f:0)));
    }
    public void ShowCargo(bool hasCargo,ResourceKind resource)
    {
        _hasCargo=hasCargo;
        if(hasCargo&&CargoMeshes.TryGetValue(resource,out var mesh)&&CargoMesh.Mesh!=mesh)CargoMesh.Mesh=mesh;
    }
    public void SetHandDetail(bool detailed)
    {
        if(_detailedHands==detailed)return;_detailedHands=detailed;
        for(int side=0;side<2;side++)
        {
            HandProxies[side].Visible=!detailed;HandSkins[side].Visible=detailed;Thumbs[side].Visible=detailed;
            for(int digit=0;digit<4;digit++)Fingers[side,digit].Visible=detailed;
        }
    }
    private void CurlHand(int side,float curl)
    {
        if(!_detailedHands)
        {
            _handCurl[side]=curl;return;
        }
        for(int digit=0;digit<4;digit++)
        {
            Fingers[side,digit].Rotation=new(curl*1.05f,0,(digit-1.5f)*.035f*(1-curl));
            Fingertips[side,digit].Rotation=new(curl*1.35f,0,0);
        }
        Thumbs[side].Rotation=new(curl*.55f,0,(side==0?1:-1)*(.3f+curl*.65f));
    }
    private void SolveHand(int side,Vector3 gripTarget,Basis handBasis)
    {
        Vector3 gripOffset=side==1?Grip!.Position:new(0,-.025f,-.026f);
        Vector3 wrist=gripTarget-handBasis*gripOffset,shoulder=Arms[side].Position;
        float upper=.28f,lower=Hands[side].Position.Length();
        Vector3 delta=wrist-shoulder;float distance=Math.Clamp(delta.Length(),.08f,upper+lower-.002f);var direction=delta.Normalized();
        wrist=shoulder+direction*distance;
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        var bend=new Vector3(side==0?-.4f:.4f,0,.8f);bend=(bend-direction*bend.Dot(direction)).Normalized();
        Vector3 elbow=shoulder+direction*along+bend*MathF.Sqrt(MathF.Max(0,upper*upper-along*along));
        var upperBasis=new Basis(new Quaternion(Vector3.Down,(elbow-shoulder).Normalized()));
        var lowerBasis=new Basis(new Quaternion(Hands[side].Position.Normalized(),(wrist-elbow).Normalized()));
        Arms[side].Basis=upperBasis;Elbows[side].Basis=upperBasis.Inverse()*lowerBasis;Hands[side].Basis=lowerBasis.Inverse()*handBasis;
    }
}
