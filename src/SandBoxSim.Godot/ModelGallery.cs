using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
/// <summary>Opt-in developer preview of actual reusable meshes; never instantiates simulation entities.</summary>
internal partial class ModelGallery : PanelContainer
{
    private readonly List<(SubViewportContainer View,string Label)> _evaluationRegions=new();
    internal IEnumerable<(int Index,string Label,Rect2 Bounds)> EvaluationRegions()
    {
        for(int i=0;i<_evaluationRegions.Count;i++)yield return(i,_evaluationRegions[i].Label,_evaluationRegions[i].View.GetGlobalRect());
    }
    private readonly List<(ResidentRig Rig,ActionKind Action)> _animated=new();
    private float _elapsed;
    private readonly List<(ResidentRig Rig,Camera3D Camera)> _handViews=new();
    private Label? _handlingLabel;
    public string Collection { get; set; } = "architecture";
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        OffsetLeft=20; OffsetRight=-20; OffsetTop=90; OffsetBottom=-110;
        AddThemeStyleboxOverride("panel",HudStyle.Box(new Color("#1b2521"),3,16));
        var body=new VBoxContainer(); body.AddThemeConstantOverride("separation",12); AddChild(body);
        body.AddChild(HudStyle.Heading(Collection=="hands" ? "手掌与握柄 · 实际指节与接触" : Collection is "actions" or "motion" or "grip" ? "居民动作 · 取出、握持、使用与收回" : Collection=="equipment" ? "配件与衣装 · 实际挂点与姿态" : Collection=="architecture" ? "木作与砌筑 · 实际建筑模型" : Collection is "characters" or "faces" ? Collection=="faces" ? "面部与衣领 · 实际人物模型" : "人物与动物 · 实际角色模型" : "地表与植被 · 实际场景模型",22));
        var grid=new GridContainer { Columns=3,SizeFlagsVertical=SizeFlags.ExpandFill };
        if(Collection=="model-details")body.GetChild<Label>(0).Text="造型与材质近景 · 世界实际网格";
        if(Collection=="wardrobe")body.GetChild<Label>(0).Text="衣装剪裁 · 实际职业模型的正面与背面";
        grid.AddThemeConstantOverride("h_separation",12); grid.AddThemeConstantOverride("v_separation",12); body.AddChild(grid);
        var models=new NatureModels();
        if(Collection=="wardrobe")
        {
            var jobs=new[]{JobType.Farmer,JobType.Gatherer,JobType.Trader};
            var names=new[]{"工作长衫 · 农夫","短上衣 · 采集者","较长外衣 · 商人"};
            for(int row=0;row<2;row++)for(int i=0;i<3;i++)
                Closeup(grid,names[i]+(row==0?" · 正面":" · 背面"),models.Resident(17,false,jobs[i]),2.0f,new(0,.85f,0),new(.55f,.10f,3),row==0);
        }
        else if(Collection=="ecology-details")
        {
            body.GetChild<Label>(0).Text="植物与岩石 · 世界共享几何";
            Card(grid,"阔叶树 · 树皮与自然枝叶",models.Tree(false),6.1f,false);
            Card(grid,"针叶树 · 真实枝梢与针叶",models.Tree(true),6.1f,false);
            var fern=new Node3D();fern.AddChild(new MeshInstance3D {Mesh=models.Shape("fern")});
            Closeup(grid,"林下蕨类 · 扫描叶片与弯曲叶轴",fern,.72f,new(0,.17f,0),new(.8f,.4f,2),false);
            var grass=new Node3D();grass.AddChild(new MeshInstance3D {Mesh=models.Shape("grass")});
            Closeup(grid,"草叶 · 自然叶形与色泽层次",grass,.52f,new(0,.15f,0),new(.4f,.2f,2),false);
            var rock=new Node3D();models.Part(rock,"rock",new(0,.38f,0),Vector3.One,6);
            Closeup(grid,"岩石 · 断裂平面与风化表面",rock,1.15f,new(0,.38f,0),new(.9f,.5f,2),false);
            var shrub=new Node3D();models.Part(shrub,"foliage",new(0,.28f,0),new(.9f,.7f,.9f),4);
            Closeup(grid,"灌木 · 枝叶与花瓣细节",shrub,1.0f,new(0,.27f,0),new(.9f,.4f,2),false);
        }
        else if(Collection=="model-details")
        {
            var resident=models.Resident(17,false,JobType.Gatherer);
            Closeup(grid,"衣身 · 腰部受力褶皱与领口",resident,.92f,new(0,1.04f,0),new(.10f,.10f,2),true);
            Closeup(grid,"靴子 · 鞋楦、脚背、交叉鞋带与鞋底",models.Resident(17,false,JobType.Gatherer),.39f,new(0,.14f,-.025f),new(.55f,.30f,2),true);
            Closeup(grid,"鹿 · 连续胸颈、耳窝与渐细分枝角",models.Animal(false),1.3f,new(0,1.70f,-.78f),new(.9f,.15f,-2),false);
            Closeup(grid,"狼 · 口鼻、尖耳与胸颈曲面",models.Animal(true),.85f,new(0,1.03f,-.80f),new(.9f,.12f,-2),false);
            Closeup(grid,"原木屋 · 端面年轮、檐边与门窗",models.Building(BuildingKind.House,true,0),2.9f,new(0,1.40f,0),new(3,1.2f,4),false);
            Closeup(grid,"石屋 · 错缝砌筑与屋面搭接",models.Building(BuildingKind.House,true,2),2.9f,new(0,1.50f,0),new(3,1.2f,4),false);
        }
        else if(Collection=="architecture")
        {
            string[] names={"原木屋 · 屋面与檐柱","灰泥屋 · 门窗与烟囱","石屋 · 基座与侧墙","高山墙屋 · 木架与砌筑","单坡屋 · 柱廊与木作"};
            for(uint i=0;i<5;i++) Card(grid,names[i],models.Building(BuildingKind.House,true,i),i==3 ? 4.5f : 3.8f,false);
            Card(grid,"仓库 · 木门、铁箍与物料",models.Building(BuildingKind.Storage,true,1),3.2f,false);
        }
        else if(Collection is "motion" or "grip")
        {
            grid.Columns=1;ActionCard(grid,models.Resident(17,false,JobType.Builder),ActionKind.BuildHouse,"从腰侧取出工具、握持、锤击、放回");
            var viewport=grid.GetChild<VBoxContainer>(0).GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0);
            var stage=viewport.GetChild<Node3D>(0);var camera=stage.GetChild<Camera3D>(stage.GetChildCount()-1);
            _handlingLabel=grid.GetChild<VBoxContainer>(0).GetChild<Label>(1);
            camera.Size=Collection=="grip"?.90f:1.4f;var target=new Vector3(0,1.04f,0);camera.Position=target+new Vector3(0,.07f,4);camera.LookAt(target);
        }
        else if(Collection=="hands")
        {
            var jobs=new[]{JobType.Builder,JobType.Gatherer,JobType.Farmer};
            var actions=new[]{ActionKind.BuildHouse,ActionKind.GatherWood,ActionKind.Farm};
            var names=new[]{"锤柄 · 拇指与三段指节","斧柄 · 双手分别握持","锄柄 · 握持与推拉"};
            for(int i=0;i<3;i++)
            {
                var rig=models.Resident(17+i,false,jobs[i]);ActionCard(grid,rig,actions[i],names[i]);
                var view=grid.GetChild<VBoxContainer>(i).GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0);
                var stage=view.GetChild<Node3D>(0);var camera=stage.GetChild<Camera3D>(stage.GetChildCount()-1);
                camera.Size=.30f;_handViews.Add((rig,camera));
            }
        }
        else if(Collection=="actions")
        {
            ActionCard(grid,models.Resident(42,false,JobType.Farmer),ActionKind.Farm,"耕作 · 锄头与腕部");
            ActionCard(grid,models.Resident(17,false,JobType.Gatherer),ActionKind.GatherWood,"伐木 · 斧头与挥击");
            ActionCard(grid,models.Resident(19,false,JobType.Miner),ActionKind.GatherIron,"采矿 · 镐头与蓄力");
            ActionCard(grid,models.Resident(17,false,JobType.Builder),ActionKind.BuildHouse,"施工 · 锤击与收纳");
            ActionCard(grid,models.Resident(42,false,JobType.Gatherer),ActionKind.GatherFood,"采集 · 俯身与拿取");
            ActionCard(grid,models.Resident(19,false,JobType.Trader),ActionKind.StoreInBuilding,"存放 · 双手递送");
        }
        else if(Collection=="equipment")
        {
            Equipment(grid,models.Resident(42,false,JobType.Farmer),"草帽 · 正面与额头贴合",Mathf.Pi,false);
            Equipment(grid,models.Resident(42,false,JobType.Farmer),"草帽 · 侧面帽腔与帽檐",Mathf.Pi*.5f,false);
            Equipment(grid,models.Resident(17,false,JobType.Craftsman),"工具 · 工作姿态与握柄",Mathf.Pi-.5f,true);
            Equipment(grid,models.Resident(19,false,JobType.Gatherer),"行囊 · 后背与肩带",-.4f,false);
            Equipment(grid,models.Resident(17,false,JobType.Soldier),"头盔 · 额头与颈侧",Mathf.Pi-.45f,false);
            Equipment(grid,models.Resident(42,true,JobType.Farmer),"儿童 · 配件随头部缩放",Mathf.Pi-.25f,false);
        }
        else if(Collection=="faces")
        {
            for(int i=0;i<6;i++)Face(grid,models.Resident(17+i,false,i%2==0 ? JobType.Gatherer : JobType.Farmer),"面部与衣领 · "+(i+1),i%2!=0,(i%3)*.45f);
        }
        else if(Collection=="characters")
        {
            Card(grid,"农夫 · 草帽、领口与袖口",models.Resident(42,false,JobType.Farmer),2.1f,true);
            Card(grid,"工匠 · 围裙、双手与工具",models.Resident(17,false,JobType.Craftsman),2.1f,true);
            Card(grid,"采集者 · 背带与行囊",models.Resident(19,false,JobType.Gatherer),2.1f,true);
            Card(grid,"儿童 · 头身比例与衣装",models.Resident(42,true,JobType.Gatherer),1.7f,true);
            Card(grid,"鹿 · 胸腹曲面、蹄与分枝角",models.Animal(false),2.7f,true);
            Card(grid,"狼 · 肩背、口鼻与尾",models.Animal(true),2.5f,true);
        }
        else
        {
            Card(grid,"阔叶树 · 分枝叶簇",models.Tree(false),6.1f,false);
            Card(grid,"针叶树 · 错层枝叶",models.Tree(true),6.1f,false);
            Card(grid,"农田 · 作物叶与围栏",models.Building(BuildingKind.Farm,true,1),2.8f,false);
            Card(grid,"矿场 · 洞口木架与矿车",models.Building(BuildingKind.Mine,true,1),3.4f,false);
            Card(grid,"遗迹 · 拱券与零散砌石",models.WildPlace(SandBoxSim.Core.Systems.WildPlaceKind.Ruins,1,true),5.6f,false);
            Card(grid,"古树林 · 根系与大树冠",models.WildPlace(SandBoxSim.Core.Systems.WildPlaceKind.OldGrove,1,true),9.6f,false);
        }
    }
    public override void _Process(double delta)
    {
        delta=EvaluationClock.Delta(delta);
        _elapsed+=(float)delta;
        float cycle=_elapsed%6.8f;
        foreach(var (rig,action) in _animated)rig.PoseAction((float)delta,action,cycle>.65f&&cycle<4.5f?ActionPhase.Executing:ActionPhase.Idle,false,0);
        foreach(var (rig,camera) in _handViews)
        {
            var target=rig.Hands[1].GlobalPosition+new Vector3(0,.015f,0);
            camera.Position=target+new Vector3(.10f,.10f,2);camera.LookAt(target);
        }
        if(_handlingLabel!=null&&_animated.Count>0)_handlingLabel.Text=_animated[0].Rig.Handling;
    }
    private void ActionCard(GridContainer grid,ResidentRig rig,ActionKind action,string title)
    {
        Card(grid,title,rig,2.05f,true);
        var view=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1).GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0);
        view.RenderTargetUpdateMode=SubViewport.UpdateMode.WhenVisible;
        var stage=view.GetChild<Node3D>(0);var camera=stage.GetChild<Camera3D>(stage.GetChildCount()-1);
        var target=new Vector3(0,.92f,0);camera.Position=target+new Vector3(.03f,.08f,4);camera.LookAt(target);
        if(action==ActionKind.StoreInBuilding)rig.ShowCargo(true,ResourceKind.Wood);
        _animated.Add((rig,action));
    }
    private void Closeup(GridContainer grid,string title,Node3D model,float size,Vector3 target,Vector3 direction,bool faceFront)
    {
        Card(grid,title,model,size,faceFront);
        var view=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1).GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0);
        var stage=view.GetChild<Node3D>(0);var camera=stage.GetChild<Camera3D>(stage.GetChildCount()-1);
        model.Rotation=faceFront?new Vector3(0,Mathf.Pi,0):Vector3.Zero;
        camera.Position=target+direction;camera.LookAt(target);
        // Scale the close-up, never the model: these are the exact world meshes.
        camera.Size=size;
    }
    private void Equipment(GridContainer grid,ResidentRig model,string title,float yaw,bool working)
    {
        Card(grid,title,model,.85f,true);
        var card=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1);
        var viewport=card.GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0);
        var stage=viewport.GetChild<Node3D>(0);var camera=stage.GetChild<Camera3D>(stage.GetChildCount()-1);
        model.Rotation=new(0,yaw,0);for(int i=0;i<(working?90:1);i++)model.Pose(1f/60,false,working,false,false,0);
        bool pack=yaw<0;
        var target=working ? model.Grip!.GlobalPosition+new Vector3(0,.05f,0) : pack ? model.Torso.GlobalPosition+new Vector3(0,1.10f,0) : model.Head.GlobalPosition-new Vector3(0,.03f,0);
        camera.Size=working?.68f:pack?.75f:.55f;camera.Position=target+new Vector3(.02f,.025f,2);camera.LookAt(target);
    }
    private void Face(GridContainer grid,Node3D model,string title,bool hat,float yaw)
    {
        Card(grid,title,model,.48f,true);
        var card=grid.GetChild<VBoxContainer>(grid.GetChildCount()-1);
        var view=card.GetChild<SubViewportContainer>(0).GetChild<SubViewport>(0);var stage=view.GetChild<Node3D>(0);
        var camera=stage.GetChild<Camera3D>(stage.GetChildCount()-1);
        model.Rotation=new Vector3(0,Mathf.Pi+yaw,0);
        var target=((ResidentRig)model).Head.GlobalPosition;camera.Size=hat ? .64f : .46f;camera.Position=target+new Vector3(.03f,.02f,2);camera.LookAt(target);
        view.RenderTargetUpdateMode=SubViewport.UpdateMode.WhenVisible;_animated.Add(((ResidentRig)model,ActionKind.None));
    }
    private void Card(GridContainer grid,string title,Node3D model,float size,bool animal)
    {
        var card=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill };
        card.AddThemeConstantOverride("separation",7); grid.AddChild(card);
        var view=new SubViewportContainer { Stretch=true,CustomMinimumSize=new(0,120),SizeFlagsVertical=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore }; card.AddChild(view);
        int index=grid.GetChildCount()-1;
        string semanticLabel=Collection=="ecology-details"?new[]{"tree","tree","fern","grass","rock","shrub"}[index]:model is ResidentRig?"resident":Collection=="model-details"?(index==2?"deer":index==3?"wolf":"house"):Collection=="architecture"?(index<5?"house":"storage"):Collection=="characters"?(index==4?"deer":"wolf"):index<2?"tree":index==2?"farm":index==3?"mine":index==4?"ruins":"grove";
        _evaluationRegions.Add((view,semanticLabel));
        var viewport=new SubViewport { OwnWorld3D=true,Size=new(420,280),Msaa3D=Viewport.Msaa.Msaa4X,RenderTargetUpdateMode=SubViewport.UpdateMode.Once }; view.AddChild(viewport);
        var stage=new Node3D(); viewport.AddChild(stage);
        stage.AddChild(new WorldEnvironment { Environment=new Godot.Environment { BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new("#252d36"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new("#d3dbe4"),AmbientLightEnergy=.36f,TonemapMode=Godot.Environment.ToneMapper.Aces } });
        stage.AddChild(new DirectionalLight3D { RotationDegrees=new(-40,-35,0),LightColor=new("#f2eee5"),LightEnergy=.95f,ShadowEnabled=true });
        stage.AddChild(new DirectionalLight3D { RotationDegrees=new(-20,140,0),LightColor=new("#bacbd1"),LightEnergy=.32f });
        stage.AddChild(new MeshInstance3D { Mesh=new PlaneMesh { Size=new(40,40) },MaterialOverride=new StandardMaterial3D { AlbedoColor=new("#39434d"),Roughness=1 } });
        stage.AddChild(model); model.Rotation=new(0,(animal ? Mathf.Pi : 0)-.60f,0);
        var camera=new Camera3D { Current=true,Projection=Camera3D.ProjectionType.Orthogonal,Size=size,Position=new(0,size*(animal ? .90f : 1.4f),size*2) }; stage.AddChild(camera);
        camera.LookAt(new Vector3(0,size*.42f,0));
        var label=HudStyle.Label(title,13); label.HorizontalAlignment=HorizontalAlignment.Center; card.AddChild(label);
    }
}
