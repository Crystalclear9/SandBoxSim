using System;
using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
/// <summary>Opt-in developer preview of actual reusable meshes; never instantiates simulation entities.</summary>
internal partial class ModelGallery : PanelContainer
{
    public string Collection { get; set; } = "architecture";
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        OffsetLeft=20; OffsetRight=-20; OffsetTop=90; OffsetBottom=-110;
        AddThemeStyleboxOverride("panel",HudStyle.Box(new Color("#1b2521"),3,16));
        var body=new VBoxContainer(); body.AddThemeConstantOverride("separation",12); AddChild(body);
        body.AddChild(HudStyle.Heading(Collection=="architecture" ? "木作与砌筑 · 实际建筑模型" : Collection=="characters" ? "人物与动物 · 实际角色模型" : "地表与植被 · 实际场景模型",22));
        var grid=new GridContainer { Columns=3,SizeFlagsVertical=SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation",12); grid.AddThemeConstantOverride("v_separation",12); body.AddChild(grid);
        var models=new NatureModels();
        if(Collection=="architecture")
        {
            string[] names={"原木屋 · 屋面与檐柱","灰泥屋 · 门窗与烟囱","石屋 · 基座与侧墙","高山墙屋 · 木架与砌筑","单坡屋 · 柱廊与木作"};
            for(uint i=0;i<5;i++) Card(grid,names[i],models.Building(BuildingKind.House,true,i),i==3 ? 4.5f : 3.8f,false);
            Card(grid,"仓库 · 木门、铁箍与物料",models.Building(BuildingKind.Storage,true,1),3.2f,false);
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
    private static void Card(GridContainer grid,string title,Node3D model,float size,bool animal)
    {
        var card=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill };
        card.AddThemeConstantOverride("separation",7); grid.AddChild(card);
        var view=new SubViewportContainer { Stretch=true,CustomMinimumSize=new(0,120),SizeFlagsVertical=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore }; card.AddChild(view);
        var viewport=new SubViewport { OwnWorld3D=true,Size=new(420,280),Msaa3D=Viewport.Msaa.Msaa4X,RenderTargetUpdateMode=SubViewport.UpdateMode.Once }; view.AddChild(viewport);
        var stage=new Node3D(); viewport.AddChild(stage);
        stage.AddChild(new WorldEnvironment { Environment=new Godot.Environment { BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new("#35423a"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new("#c3ceca"),AmbientLightEnergy=.32f,TonemapMode=Godot.Environment.ToneMapper.Aces } });
        stage.AddChild(new DirectionalLight3D { RotationDegrees=new(-40,-35,0),LightColor=new("#fff0d7"),LightEnergy=.75f,ShadowEnabled=true });
        stage.AddChild(new DirectionalLight3D { RotationDegrees=new(-20,140,0),LightColor=new("#bacbd1"),LightEnergy=.3f });
        stage.AddChild(new MeshInstance3D { Mesh=new PlaneMesh { Size=new(40,40) },MaterialOverride=new StandardMaterial3D { AlbedoColor=new("#4c5847"),Roughness=1 } });
        stage.AddChild(model); model.Rotation=new(0,(animal ? Mathf.Pi : 0)-.60f,0);
        var camera=new Camera3D { Current=true,Projection=Camera3D.ProjectionType.Orthogonal,Size=size,Position=new(0,size*(animal ? .90f : 1.4f),size*2) }; stage.AddChild(camera);
        camera.LookAt(new Vector3(0,size*.42f,0));
        var label=HudStyle.Label(title,13); label.HorizontalAlignment=HorizontalAlignment.Center; card.AddChild(label);
    }
}
