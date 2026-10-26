using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;
using SandBoxSim.Core;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private TabContainer _drawer = null!;
    private PanelContainer _settingsPanel = null!, _journalPanel = null!, _brushPanel = null!;
    private PanelContainer _worldPanel=null!,_statsPanel=null!;
    private PanelContainer _timePanel = null!, _cameraPanel = null!, _dockPanel = null!;
    private PanelContainer _contextPanel=null!;
    private Label _cameraReadout=null!;
    private Label _playStateLabel=null!;
    private Button _contextFocus=null!,_contextDetails=null!;
    private Label _contextFacts=null!;
    private string _contextStatus="";private ulong _contextStatusAt;
    private Control _hudOverlay=null!;private Button _immersiveHint=null!;private bool _immersive;
    internal bool InterfaceVisible=>!_immersive;
    private Godot.Button _followCameraButton=null!;
    private Label _toolBadge = null!, _dayLabel = null!, _populationLabel = null!, _settlementLabel = null!, _buildingLabel = null!;
    private VBoxContainer _brushParameters=null!;
    private HSlider _radiusSlider=null!,_strengthSlider=null!;
    private TextureRect _paletteGlyph=null!;
    private Label _ecologyFootprint=null!,_brushControlsHint=null!;
    private Label _brushTitle = null!, _brushHint = null!;
    private bool _settingsOpen;
    private OptionButton _scenarioPicker = null!;
    private Godot.Button _settingsButton = null!, _journalButton = null!;
    private Godot.Button _inspectDockButton=null!,_journalDockButton=null!;
    private GridContainer _paletteRow = null!;
    private Label _paletteTitle=null!, _paletteCaption=null!;
    private LineEdit _toolSearch=null!;
    private ConfirmationDialog _scenarioConfirm=null!;
    private int _pendingScenario;
    private Label _emptyTools=null!;
    private ScrollContainer _toolScroll=null!;
    private PanelContainer _commandPanel = null!;
    private Godot.Button _toolsMenuButton = null!;
    private Label _foodLabel = null!, _woodLabel = null!, _stoneLabel = null!;
    private readonly Dictionary<PlayerTool, Godot.Button> _toolButtons = new();
    private readonly Dictionary<int, Godot.Button> _speedButtons = new();
    private readonly Dictionary<HudMetricTrail,Control> _historyHoverOwners=new();
    private readonly Dictionary<HudMetricTrail,Tween> _historyHoverTransitions=new();
    private int _resumeSpeed=1;
    private readonly Dictionary<string, Godot.Button> _categoryButtons = new();
    private string _category = "生命";
    private bool _decisionDetails;
    private int _residentPage;
    private HBoxContainer _residentPages=null!;
    private VBoxContainer _residentSummary=null!;
    private Button _residentPortraitLink=null!;
    private readonly Button[] _residentPageButtons=new Button[3];
    private readonly Label[] _residentStock=new Label[4];
    private VBoxContainer _residentVitals = null!;
    private bool _editingResidentName;
    private HBoxContainer _residentControls = null!, _residentNameControls = null!;
    private ResidentPortrait _residentPortrait = null!;
    private Label _residentTitle = null!, _residentSubtitle = null!, _residentAction = null!, _residentActionPhase = null!;
    private readonly Label[] _vitalLabels = new Label[4];
    private readonly Label[] _vitalValues = new Label[4];
    private readonly ProgressBar[] _vitalBars = new ProgressBar[4];
    private VBoxContainer _residentInventory=null!;
    private Button _residentInventoryToggle=null!;
    private bool _inventoryExpanded;
    private Tween? _vitalTransition, _landTransition;
    private VBoxContainer _landVitals = null!;
    private Label _landTitle = null!, _landSubtitle = null!;
    private readonly Label[] _landLabels = new Label[3];
    private readonly ProgressBar[] _landBars = new ProgressBar[3];

    private static string EscapeMarkup(string value) => value.Replace("[", "[lb]");
    private static string JobName(SandBoxSim.Core.Agents.JobType job) => job switch
    {
        SandBoxSim.Core.Agents.JobType.Gatherer => "采集者", SandBoxSim.Core.Agents.JobType.Farmer => "农夫",
        SandBoxSim.Core.Agents.JobType.Builder => "建造者", SandBoxSim.Core.Agents.JobType.Miner => "矿工",
        SandBoxSim.Core.Agents.JobType.Hunter => "猎人", SandBoxSim.Core.Agents.JobType.Soldier => "士兵",
        SandBoxSim.Core.Agents.JobType.Trader => "商人", SandBoxSim.Core.Agents.JobType.Craftsman => "工匠",
        SandBoxSim.Core.Agents.JobType.Leader => "领袖", _ => "居民"
    };
    private static string LifeStageName(SandBoxSim.Core.Agents.LifeStage stage) => stage switch
    { SandBoxSim.Core.Agents.LifeStage.Child => "儿童", SandBoxSim.Core.Agents.LifeStage.Elder => "长者", _ => "成人" };
    private static string PhaseName(SandBoxSim.Core.Agents.ActionPhase phase) => phase switch
    {
        SandBoxSim.Core.Agents.ActionPhase.Moving => "前往目的地", SandBoxSim.Core.Agents.ActionPhase.Executing => "进行中",
        SandBoxSim.Core.Agents.ActionPhase.Done => "已完成", SandBoxSim.Core.Agents.ActionPhase.Failed => "受阻", _ => "休息中"
    };
    private string FormatInspector(string plain)
    {
        var result = new System.Text.StringBuilder(); bool first = !_residentVitals.Visible && !_landVitals.Visible, hidden = _residentVitals.Visible&&_residentPage==2;
        string[] lines = plain.Replace("\r", "").Split('\n');
        for (int i = _residentVitals.Visible || _landVitals.Visible ? 2 : 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (_residentVitals.Visible && (line.StartsWith("生命 ") || line.StartsWith("精力 ") || line.StartsWith("正在") || line.StartsWith("目的地 ") || line=="身体与需求" || line=="原地活动")) { continue; }
            bool section = line is "身体与需求" or "随身物资" or "性格倾向" or "居所与关系" or "个人历史" or "上次决策 / 效用" or "聚落历史";
            if (section) { hidden = !_decisionDetails && (line is "性格倾向" or "上次决策 / 效用"); }
            if(section&&_residentVitals.Visible&&_residentPage==2)hidden=line is not ("性格倾向" or "上次决策 / 效用");
            if (hidden) { continue; }
            string escaped = EscapeMarkup(line);
            if (first && line.Length > 0) { result.AppendLine("[font_size=23][b]" + escaped + "[/b][/font_size]"); first = false; }
            else if (section) { result.AppendLine("[color=#d6c294][b]" + escaped + "[/b][/color]"); }
            else { result.AppendLine(escaped); }
        }
        return result.ToString().TrimStart('\r','\n');
    }

    private void BuildInterface()
    {
        Theme=HudStyle.InterfaceTheme();
        // The world occupies the whole window. No opaque rail or layout container resizes its viewport.
        _map = new WorldView3D { Game = this, WorldPresentationEnabled = !IsModelGallery }; _map.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_map);
        var overlay = _hudOverlay = new Control { MouseFilter = MouseFilterEnum.Ignore,TextureFilter=TextureFilterEnum.LinearWithMipmaps }; overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(overlay);
        var headerShade=new ColorRect {MouseFilter=MouseFilterEnum.Ignore,Material=new ShaderMaterial {Shader=new Shader {Code=@"shader_type canvas_item;
void fragment(){float opacity=.93*(1.0-smoothstep(.48,1.0,UV.y));COLOR=vec4(.065,.080,.067,opacity);}"}}};
        headerShade.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);headerShade.OffsetBottom=104;overlay.AddChild(headerShade);
        // A single quiet header keeps the world readable between the controls.
        var worldCard = Surface(overlay, new Vector2(0, 0), new Vector2(18, 6), new Vector2(250, 64), 4);

        _worldPanel=worldCard;
        HudStyle.HeaderSurface(worldCard,6);
        var worldRow = new HBoxContainer(); worldRow.AddThemeConstantOverride("separation", 9); worldCard.AddChild(worldRow);
        worldRow.AddChild(HudStyle.IconWell(HudSymbols.For("crest"),28));
        var wordmark = HudStyle.Heading("SandBoxSim", 18);
        var worldInfo = new VBoxContainer(); worldInfo.AddThemeConstantOverride("separation", 0); worldRow.AddChild(worldInfo);
        worldInfo.AddChild(wordmark);
        var scenario = new OptionButton { Flat = true, CustomMinimumSize = new Vector2(174, 24) }; _scenarioPicker = scenario; HudStyle.Button(scenario);HudStyle.QuietButton(scenario,12);
        foreach (string name in SandboxScenarios.Names) { scenario.AddItem(name); } worldInfo.AddChild(scenario);
        _scenarioConfirm=new ConfirmationDialog {Title="切换世界",DialogText="切换场景会替换当前世界。未保存的进展将丢失。",OkButtonText="切换世界",CancelButtonText="保留当前世界"};AddChild(_scenarioConfirm);
        scenario.ItemSelected+=index=>{_pendingScenario=(int)index;scenario.Select(Scenario);if(_pendingScenario!=Scenario)_scenarioConfirm.PopupCentered(new Vector2I(440,160));};
        _scenarioConfirm.Confirmed+=()=>{Scenario=_pendingScenario;scenario.Select(Scenario);NewWorld((int)_seed.Value);_map.Focus(43,50,16);_checkpoint="";_discovery.Refresh();};

        var stats = Surface(overlay, new Vector2(.5f, 0), new Vector2(-310, 20), new Vector2(620, 62), 4);

        _statsPanel=stats;
        HudStyle.HeaderSurface(stats,6);
        var statsRow = new HBoxContainer(); statsRow.AddThemeConstantOverride("separation", 12); stats.AddChild(statsRow);
        _populationLabel = Metric(statsRow, "居民", "40", 18); _settlementLabel = Metric(statsRow, "聚落", "0", 18); _buildingLabel = Metric(statsRow, "建筑", "0", 18);
        _foodLabel = Metric(statsRow, "食物储备", "0", 18); _woodLabel = Metric(statsRow, "木材储备", "0", 18); _stoneLabel = Metric(statsRow, "石料储备", "0", 18);
        var resourceDivider=new VSeparator {SizeFlagsVertical=SizeFlags.ShrinkCenter,CustomMinimumSize=new(1,30),MouseFilter=MouseFilterEnum.Ignore};
        statsRow.AddChild(resourceDivider);statsRow.MoveChild(resourceDivider,3);
        foreach (var resource in new[] { _foodLabel, _woodLabel, _stoneLabel })
            resource.TooltipText = "居民携带 + 地面物资堆 + 仓库；不包含尚未采集的地表资源，也不保证所有居民都可到达。";
        _summary = new Label { Visible = false }; overlay.AddChild(_summary);
        var topActions = Surface(overlay, new Vector2(1, 0), new Vector2(-228, 18), new Vector2(210, 40), 4);
        HudStyle.HeaderSurface(topActions,2);

        var actions = new HBoxContainer {Alignment=BoxContainer.AlignmentMode.End}; actions.AddThemeConstantOverride("separation", 6); topActions.AddChild(actions);
        _settingsButton = ActionButton(actions, "世界设置", () => ShowSettings(!_settingsOpen)); _settingsButton.ToggleMode = true;
        _journalButton = ActionButton(actions, "世界手记", () => ShowJournal(!_journalPanel.Visible)); _journalButton.ToggleMode = true;HudStyle.QuietButton(_settingsButton);HudStyle.QuietButton(_journalButton);
        var immersive=ActionButton(actions,"沉浸",()=>SetImmersive(true));HudStyle.QuietButton(immersive);immersive.Visible=!IsModelGallery;immersive.TooltipText="沉浸观察 · F10 隐藏界面，F10 / Esc 返回；鼠标视角与空格暂停继续可用。";

        _journalPanel = Surface(overlay, new Vector2(1, 0), new Vector2(-372, 92), new Vector2(348, 600), 18);
        _journalPanel.Name="WorldNotebook";
        var journal = new VBoxContainer(); journal.AddThemeConstantOverride("separation", 10); _journalPanel.AddChild(journal);
        var journalHeading = new PanelContainer(); journalHeading.SetMeta("journal_heading",true);
        var headingBox=HudStyle.Box(new Color(0,0,0,0),0,0,false);journalHeading.AddThemeStyleboxOverride("panel",headingBox);journal.AddChild(journalHeading);
        var headingBody=new VBoxContainer();headingBody.AddThemeConstantOverride("separation",5);journalHeading.AddChild(headingBody);
        var journalHeader = new HBoxContainer();journalHeader.AddThemeConstantOverride("separation",12);headingBody.AddChild(journalHeader);journalHeader.AddChild(HudStyle.IconWell(HudSymbols.For("手记"),34));
        var journalTitle = HudStyle.Heading("世界手记", 19); journalTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; journalHeader.AddChild(journalTitle);
        var closeJournal=ActionButton(journalHeader, "×", () => ShowJournal(false));closeJournal.TooltipText="收起世界手记 · Esc";closeJournal.CustomMinimumSize=new(32,32);
        journal.AddChild(new HSeparator());
        _drawer = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; journal.AddChild(_drawer);
        _drawer.AddThemeStyleboxOverride("panel", HudStyle.Box(new Color(0, 0, 0, 0), 0, 0, false));
        _discovery = new DiscoveryPanel { Name = "现场", Game = this }; _drawer.AddChild(_discovery);
        var personPage=new VBoxContainer {Name="人物",SizeFlagsVertical=SizeFlags.ExpandFill};personPage.AddThemeConstantOverride("separation",8);_drawer.AddChild(personPage);
        var personScroll=new ScrollContainer {HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true,SizeFlagsVertical=SizeFlags.ExpandFill};personPage.AddChild(personScroll);
        var personPanel=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};personPanel.AddThemeConstantOverride("separation",8);personScroll.AddChild(personPanel);
        _residentPages=new HBoxContainer();_residentPages.AddThemeConstantOverride("separation",4);personPage.AddChild(_residentPages);personPage.MoveChild(_residentPages,0);
        for(int i=0;i<3;i++)
        {
            int page=i;var button=ActionButton(_residentPages,new[]{"概览","生活","决策"}[i],()=>SetResidentPage(page));
            button.ToggleMode=true;button.SizeFlagsHorizontal=SizeFlags.ExpandFill;_residentPageButtons[i]=button;
            HudStyle.QuietButton(button,13);
            button.TooltipText=new[]{"肖像、当前行为、身体需求与随身物资","居所、关系与个人历史","性格倾向与上次决策效用"}[i];
        }
        BuildResidentVitals(personPanel); BuildLandVitals(personPanel);
        var detailControls = new HBoxContainer(); _residentControls = detailControls; personPage.AddChild(detailControls);
        var personalActions=HudStyle.Label("个人记录",12,true);personalActions.SizeFlagsHorizontal=SizeFlags.ExpandFill;personalActions.SizeFlagsVertical=SizeFlags.ShrinkCenter;detailControls.AddChild(personalActions);
        _pinButton = ActionButton(detailControls, "关注", TogglePin);
        _renameButton=ActionButton(detailControls, "改名", () => SetNameEditing(!_editingResidentName));
        var nameControls = new HBoxContainer(); _residentNameControls = nameControls; personPage.AddChild(nameControls);
        _residentName = new LineEdit { Name="ResidentNameEditor",PlaceholderText = "给这个居民起个名字", MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _residentName.TooltipText="Enter 保存 · Esc 取消草稿";
        _residentName.TextSubmitted+=_=>RenameResident();
        _residentName.GuiInput+=input=>{if(input is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape}){SetNameEditing(false);_residentName.AcceptEvent();}};
        nameControls.AddChild(_residentName); ActionButton(nameControls, "保存", RenameResident);ActionButton(nameControls,"取消",()=>SetNameEditing(false));
        _inspector = TextPanel("人物详情"); _inspector.FitContent=true;_inspector.ScrollActive=false;_inspector.SizeFlagsVertical=SizeFlags.Fill;personPanel.AddChild(_inspector); _history = TextPanel("历史"); _drawer.AddChild(_history);
        _chart = new StatisticsView { Name = "曲线", Game = this }; _drawer.AddChild(_chart);
        foreach(var (tab,glyph) in new[]{(0,"观察"),(1,"居民"),(2,"手记"),(3,"曲线")})_drawer.SetTabIcon(tab,HudSymbols.For(glyph,16,"#c5c4b6"));
        _drawer.TabChanged += _ => {RevealJournalPage();RefreshPresentationControls();};

        _brushPanel = Surface(overlay, new Vector2(0, 0), new Vector2(24, 104), new Vector2(245, 230), 18);
        var brush = new VBoxContainer(); brush.AddThemeConstantOverride("separation", 4); _brushPanel.AddChild(brush);
        var brushHeading=new HBoxContainer();brush.AddChild(brushHeading);
        _brushTitle = HudStyle.Heading("自由观察", 21);_brushTitle.SizeFlagsHorizontal=SizeFlags.ExpandFill;brushHeading.AddChild(_brushTitle);
        var closeBrush=ActionButton(brushHeading,"×",()=>{SelectTool(PlayerTool.Inspect);ShowTools(false);});
        closeBrush.TooltipText="结束干预，回到自由观察";closeBrush.CustomMinimumSize=new(32,32);
        _brushHint = HudStyle.Label("靠近一个人，看看他为什么\n做出自己的选择。", 14, true); _brushHint.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis; brush.AddChild(_brushHint);
        _brushParameters=new VBoxContainer();brush.AddChild(_brushParameters);
        var parameterFields=new HBoxContainer();parameterFields.AddThemeConstantOverride("separation",12);_brushParameters.AddChild(parameterFields);
        var radiusField=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};var strengthField=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};parameterFields.AddChild(radiusField);parameterFields.AddChild(strengthField);
        _radius = BrushField(radiusField, "范围·格", 0, 20, 2,out _radiusSlider); _strength = BrushField(strengthField, "数量/强度", 1, 100, 10,out _strengthSlider);
        _ecologyFootprint=HudStyle.Label("生态地块 · 周围 3 格\n可连续栽植，水土决定后续生长。",12,true);brush.AddChild(_ecologyFootprint);_ecologyFootprint.Visible=false;
        _brushPanel.Visible = false;

        _settingsPanel = Surface(overlay, new Vector2(0, 0), new Vector2(24, 104), new Vector2(300, 640), 18); _settingsPanel.Visible = false;
        var settings = new VBoxContainer(); settings.AddThemeConstantOverride("separation", 16); _settingsPanel.AddChild(settings);
        var settingsHead = new HBoxContainer(); settings.AddChild(settingsHead); var settingsTitle = HudStyle.Label("世界设置", 21); settingsTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; settingsHead.AddChild(settingsTitle);
        ActionButton(settingsHead, "收起", () => ShowSettings(false));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; settings.AddChild(scroll);
        var tools = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; tools.AddThemeConstantOverride("separation", 10); scroll.AddChild(tools);
        tools.AddChild(HudStyle.Label("全部干预", 14, true));
        _toolPicker = new OptionButton(); foreach (string name in ToolNames) { _toolPicker.AddItem(name); } tools.AddChild(_toolPicker); _toolPicker.ItemSelected += index => SelectTool((PlayerTool)index);
        _toolDescription = HudStyle.Label("选择工具，再改变这个世界。", 14, true); _toolDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart; tools.AddChild(_toolDescription);
        tools.AddChild(new HSeparator()); tools.AddChild(HudStyle.Label("界面表现", 17));
        var motion = new CheckBox { Text = "柔和界面动效", ButtonPressed = HudStyle.MotionEnabled };
        HudStyle.Rule(motion); tools.AddChild(motion);
        motion.TooltipText = "控制按钮、面板和数值的过渡；关闭后立即显示结果。";
        motion.Toggled += enabled =>
        {
            HudStyle.MotionEnabled = enabled;
            if (!enabled) { ResetHudMotion(); }
            var preferences = new ConfigFile(); preferences.SetValue("interface", "motion", enabled);
            preferences.Save("user://interface.cfg");
        };
        tools.AddChild(new HSeparator()); tools.AddChild(HudStyle.Label("世界规则", 17));
        Rule(tools, "禁止死亡", () => Sim.Config.Rules.NoDeath, v => Sim.Config.Rules.NoDeath = v);
        Rule(tools, "高出生率", () => Sim.Config.Rules.HighBirthRate, v => Sim.Config.Rules.HighBirthRate = v);
        Rule(tools, "快速衰老", () => Sim.Config.Rules.FastAging, v => Sim.Config.Rules.FastAging = v);
        Rule(tools, "资源双倍恢复", () => Sim.Config.Rules.DoubleResource, v => Sim.Config.Rules.DoubleResource = v);
        Rule(tools, "和平模式", () => Sim.Config.Rules.PeaceMode, v => Sim.Config.Rules.PeaceMode = v);
        Rule(tools, "禁止聚落战争", () => Sim.Config.Rules.DisableWar, v => Sim.Config.Rules.DisableWar = v);
        Rule(tools, "自然住房布局", () => Sim.Config.Buildings.OrganicHousing, v => Sim.Config.Buildings.OrganicHousing = v);
        var weather = new HBoxContainer(); tools.AddChild(weather); ActionButton(weather, "降雨", () => ChangeWeather(WeatherKind.Rain)); ActionButton(weather, "旱季", () => ChangeWeather(WeatherKind.Drought));
        tools.AddChild(new HSeparator()); tools.AddChild(HudStyle.Label("观察图层", 17));
        var layer = new OptionButton(); foreach (string text in new[] { "自然地形", "聚落领土", "资源储量", "湿度", "肥力", "食物分布", "人口密度", "AI 状态", "行动路径" }) { layer.AddItem(text); }
        tools.AddChild(layer); layer.ItemSelected += i => _map.Overlay = (int)i;
        _birth = Spin(tools, "出生概率", 0, 1, Sim.Config.Birth.BaseChancePerDay, .005); _birth.ValueChanged += v => { Sim.Config.Birth.BaseChancePerDay = (float)v; };
        _seed = Spin(tools, "随机种子", 0, int.MaxValue, Sim.World.Seed);
        ActionButton(tools, "重新生成世界", () => { NewWorld((int)_seed.Value); _checkpoint = ""; _map.Focus(43, 50, 16); });
        ActionButton(tools, "保存世界…", () => _save.PopupCenteredRatio(.65f)); ActionButton(tools, "载入世界…", () => _load.PopupCenteredRatio(.65f));
        ActionButton(tools, "记录实验起点", () => { _checkpoint = EncodeClientWorld(); _experimentLabel = $"第 {Sim.World.Calendar.Day} 天 · {Sim.Agents.LiveCount} 人"; _status.Text = "已记录 " + _experimentLabel; });
        ActionButton(tools, "回到实验起点", RestoreCheckpoint);

        var dock = Surface(overlay, Vector2.Zero, new Vector2(16, 96), new Vector2(368, 624), 14); _dockPanel = dock;dock.Name="ToolLibrary";
        var dockContents = new VBoxContainer(); dockContents.AddThemeConstantOverride("separation", 8); dock.AddChild(dockContents);
        var paletteHeading=new HBoxContainer();paletteHeading.AddThemeConstantOverride("separation",12);dockContents.AddChild(paletteHeading);
        var paletteWell=HudStyle.IconWell(ToolGlyphs.For(PlayerTool.Human),36);_paletteGlyph=paletteWell.GetChild<TextureRect>(0);paletteHeading.AddChild(paletteWell);
        dock.GetChild<HudBevel>(0).Visible=true;
        _paletteTitle=HudStyle.Heading("创造",22);_paletteTitle.SizeFlagsHorizontal=SizeFlags.ExpandFill;paletteHeading.AddChild(_paletteTitle);
        var closeTools=ActionButton(paletteHeading,"×",()=>{SelectTool(PlayerTool.Inspect);ShowTools(false);});closeTools.TooltipText="结束干预并收起 · Esc";closeTools.CustomMinimumSize=new(40,40);
        _paletteCaption=HudStyle.Label("生命",12,true);dockContents.AddChild(_paletteCaption);
        var searchRow=new HBoxContainer();searchRow.AddThemeConstantOverride("separation",8);dockContents.AddChild(searchRow);
        _toolSearch=new LineEdit {PlaceholderText="搜索工具与用途…",ClearButtonEnabled=true,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new(0,38)};searchRow.AddChild(_toolSearch);searchRow.AddChild(HudStyle.Keycap(OS.GetName()=="macOS"?"⌘ K":"Ctrl K"));
        _toolSearch.TextChanged+=_=>PopulateTools();
        _toolSearch.GuiInput+=HandleToolSearchInput;
        var browser=new HBoxContainer {SizeFlagsVertical=SizeFlags.ExpandFill};browser.AddThemeConstantOverride("separation",10);dockContents.AddChild(browser);
        var categories=new VBoxContainer();categories.AddThemeConstantOverride("separation",2);browser.AddChild(categories);
        foreach (string category in new[] { "生命", "生态", "地貌", "资源", "灾害", "祝福" })
        {
            string chosen=category;var button=ActionButton(categories,category,()=>SetCategory(chosen));
            button.ToggleMode=true;HudStyle.QuietButton(button,12);button.CustomMinimumSize=new(70,38);
            button.Icon=ToolGlyphs.For(CategoryGlyph(category));button.ExpandIcon=true;button.AddThemeConstantOverride("icon_max_width",16);button.AddThemeConstantOverride("h_separation",5);button.Alignment=HorizontalAlignment.Left;
            _categoryButtons[category]=button;
        }
        browser.AddChild(new VSeparator {MouseFilter=MouseFilterEnum.Ignore});
        _toolScroll=new ScrollContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,CustomMinimumSize=new(0,238)};browser.AddChild(_toolScroll);
        var library=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};_toolScroll.AddChild(library);
        _paletteRow = new GridContainer { Columns = 1,SizeFlagsHorizontal=SizeFlags.ExpandFill };library.AddChild(_paletteRow);
        _emptyTools=HudStyle.Label("没有匹配的工具，试试其他关键词。",13,true);_emptyTools.AutowrapMode=TextServer.AutowrapMode.WordSmart;library.AddChild(_emptyTools);
        _brushPanel.Reparent(dockContents);_brushPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);_brushPanel.CustomMinimumSize=Vector2.Zero;
        var brushSurface=HudStyle.Box(HudStyle.Wash,6,8,false);_brushPanel.AddThemeStyleboxOverride("panel",brushSurface);_brushPanel.GetChild<HudBevel>(0).Visible=false;
        _brushTitle.AddThemeFontSizeOverride("font_size",14);_brushHint.AddThemeFontSizeOverride("font_size",12);
        _brushControlsHint=HudStyle.Label("左键施加  ·  Shift + 滚轮调范围  ·  Esc 退出",11,true);brush.AddChild(_brushControlsHint);
        SetCategory("生命");
        _dockPanel.Visible = false;
        _commandPanel = Surface(overlay, new Vector2(.5f, 1), new Vector2(-180, -65), new Vector2(360, 50), 6);
        var commandSurface=HudStyle.Box(new Color(HudStyle.Ink,.96f),5,5);commandSurface.BorderColor=new Color(HudStyle.Border,.5f);
        _commandPanel.AddThemeStyleboxOverride("panel",commandSurface);_commandPanel.GetChild<HudBevel>(0).Visible=false;

        var commands = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; commands.AddThemeConstantOverride("separation", 6); _commandPanel.AddChild(commands);
        _inspectDockButton=ActionButton(commands, "观察", () => { SelectTool(PlayerTool.Inspect); ShowTools(false); });_inspectDockButton.ToggleMode=true;
        _toolsMenuButton=ActionButton(commands,"创造",()=>{if(_dockPanel.Visible){SelectTool(PlayerTool.Inspect);ShowTools(false);}else ShowTools(true);});_toolsMenuButton.ToggleMode=true;
        _journalDockButton=ActionButton(commands, "手记", () => ShowJournal(!_journalPanel.Visible));_journalDockButton.ToggleMode=true;
        foreach (Godot.Button command in commands.GetChildren())
        {
            command.TooltipText=command.Text switch {"观察"=>"选择人物或地块，查看正在发生的变化。","创造"=>"打开工具库 · Ctrl / ⌘ K 搜索；再次点击结束干预。",_=>"现场、居民、历史与世界曲线。"};
            command.Icon = HudSymbols.For(command.Text); command.ExpandIcon = true;
            command.IconAlignment=HorizontalAlignment.Left;command.VerticalIconAlignment=VerticalAlignment.Center;command.SizeFlagsHorizontal=SizeFlags.ExpandFill;
            command.AddThemeFontSizeOverride("font_size",14); command.AddThemeConstantOverride("icon_max_width", 22); command.AddThemeConstantOverride("h_separation", 8);
            command.CustomMinimumSize = new Vector2(102, 40);
            command.AddThemeConstantOverride("outline_size",0);
        }
        _inspectDockButton.SetPressedNoSignal(Tool==PlayerTool.Inspect);

        var timePanel = Surface(overlay, new Vector2(0, 1), new Vector2(16, -72), new Vector2(272, 64), 8); _timePanel = timePanel;
        timePanel.AddThemeStyleboxOverride("panel",HudStyle.Frame(10));

        var timeBox = new VBoxContainer(); timeBox.AddThemeConstantOverride("separation", 4); timePanel.AddChild(timeBox);
        var timeHeading=new HBoxContainer();timeBox.AddChild(timeHeading);
        _dayLabel=HudStyle.Label("第 1 天",14);_dayLabel.SizeFlagsHorizontal=SizeFlags.ExpandFill;timeHeading.AddChild(_dayLabel);
        _playStateLabel=HudStyle.Label("",11,true);_playStateLabel.SizeFlagsVertical=SizeFlags.ShrinkCenter;timeHeading.AddChild(_playStateLabel);
        var timeRow = new HBoxContainer(); timeBox.AddChild(timeRow);
        foreach (int speed in new[] { 0, 1, 4, 8, 16, 32 })
        { int chosen = speed; var button = ActionButton(timeRow, speed == 0 ? "暂停" : speed + "×", () => SetSpeed(chosen)); button.ToggleMode = true; _speedButtons[speed] = button; } SetSpeed(_speed);
        var cameraPanel = Surface(overlay, new Vector2(1, 1), new Vector2(-280, -72), new Vector2(264, 64), 8); _cameraPanel = cameraPanel;
        cameraPanel.AddThemeStyleboxOverride("panel",HudStyle.Frame(10));

        var cameraBox = new VBoxContainer(); cameraBox.AddThemeConstantOverride("separation", 4); cameraPanel.AddChild(cameraBox);
        var cameraHeading = new HBoxContainer(); cameraBox.AddChild(cameraHeading);
        var cameraTitle = _cameraReadout=HudStyle.Label("观察视角", 11, true); cameraTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; cameraHeading.AddChild(cameraTitle);
        cameraHeading.TooltipText = "罗盘铜色尖端指向地图北方，随鼠标旋转视角同步变化。";
        cameraHeading.AddChild(new HudCompass { View = (WorldView3D)_map });
        var views = new HBoxContainer(); cameraBox.AddChild(views); ActionButton(views, "全景", () => _map.Center());
        foreach (string label in new[] { "斜视", "俯视", "近景" }) { string view = label; ActionButton(views, label, () => ((WorldView3D)_map).SetPerspective(view)); }
        _followCameraButton=ActionButton(views, "跟随", () => {
            var world=(WorldView3D)_map;
            if(world.IsFollowing(SelectedSlot))world.StopFollowing();else if(SelectedSlot>=0)_map.Follow(SelectedSlot);
            RefreshPresentationControls();
        });
        _followCameraButton.ToggleMode=true;
        _toolBadge = HudStyle.Label("自由观察", 13); HudStyle.Float(_toolBadge, new Vector2(.5f, 1), new Vector2(-200, -180), new Vector2(400, 26)); _toolBadge.HorizontalAlignment = HorizontalAlignment.Center; _toolBadge.AddThemeColorOverride("font_color", new Color("#f3efe3")); _toolBadge.AddThemeColorOverride("font_shadow_color", new Color("#17241e")); _toolBadge.AddThemeConstantOverride("shadow_offset_y", 1); overlay.AddChild(_toolBadge);
        _status = HudStyle.Label("观察模式：左键拖动平移 · 右键拖动旋转/俯仰 · 滚轮缩放 · 单击选择", 11, true); HudStyle.Float(_status, new Vector2(.5f, 1), new Vector2(-430, -126), new Vector2(860, 18)); _status.HorizontalAlignment = HorizontalAlignment.Center; _status.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f)); _status.AddThemeConstantOverride("shadow_offset_y", 1); overlay.AddChild(_status);
        _contextPanel=Surface(overlay,new Vector2(0,1),new Vector2(490,-184),new Vector2(620,88),8);
        _contextPanel.Name="SelectionContext";
        _contextPanel.Visible=!IsModelGallery;
        var contextRow=new HBoxContainer();contextRow.AddThemeConstantOverride("separation",14);_contextPanel.AddChild(contextRow);
        contextRow.AddChild(HudStyle.IconWell(HudSymbols.For("观察"),36));
        var context=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};context.AddThemeConstantOverride("separation",3);contextRow.AddChild(context);
        foreach(var label in new[]{_toolBadge,_status})
        {
            label.Reparent(context);label.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;
            label.CustomMinimumSize=Vector2.Zero;label.ClipText=true;label.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            label.HorizontalAlignment=HorizontalAlignment.Left;
            label.AddThemeColorOverride("font_shadow_color",Colors.Transparent);
        }
        _status.AutowrapMode=TextServer.AutowrapMode.WordSmart;_status.MaxLinesVisible=2;_status.MouseFilter=MouseFilterEnum.Pass;
        _status.AddThemeFontSizeOverride("font_size",12);
        float twoLines=Mathf.Ceil(_status.GetThemeFont("font").GetHeight(12)*2+_status.GetThemeConstant("line_spacing"));
        _toolBadge.CustomMinimumSize=new Vector2(0,20);_status.CustomMinimumSize=new Vector2(0,twoLines);
        _contextFacts=HudStyle.Label("",12,true);_contextFacts.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        _contextFacts.CustomMinimumSize=new(0,twoLines);_contextFacts.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _contextFacts.MaxLinesVisible=2;_contextFacts.ClipText=true;_contextFacts.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        _contextFacts.MouseFilter=MouseFilterEnum.Pass;_contextFacts.Visible=false;context.AddChild(_contextFacts);
        var contextActions=new VBoxContainer {SizeFlagsVertical=SizeFlags.ShrinkCenter};contextActions.AddThemeConstantOverride("separation",2);contextRow.AddChild(contextActions);
        _contextFocus=ActionButton(contextActions,"靠近",FocusContextSelection);
        _contextDetails=ActionButton(contextActions,"档案",OpenContextDetails);
        _contextFocus.Icon=HudSymbols.For("观察",12);_contextDetails.Icon=HudSymbols.For("手记",12);
        foreach(var button in new[]{_contextFocus,_contextDetails})
        {
            button.CustomMinimumSize=new(64,26);button.AddThemeFontSizeOverride("font_size",12);
            foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled"})
            {var style=(StyleBoxFlat)button.GetThemeStylebox(state).Duplicate();style.ContentMarginTop=style.ContentMarginBottom=3;button.AddThemeStyleboxOverride(state,style);}
            var normal=(StyleBoxFlat)button.GetThemeStylebox("normal").Duplicate();normal.BgColor=new Color(HudStyle.Wash,.60f);normal.ShadowSize=0;button.AddThemeStyleboxOverride("normal",normal);
        }
        _contextFocus.TooltipText="靠近所选居民，保持鼠标旋转和缩放";_contextDetails.TooltipText="直接打开所选居民的独立观察窗";
        _save = Dialog(FileDialog.FileModeEnum.SaveFile); _save.FileSelected += SaveWorld; _load = Dialog(FileDialog.FileModeEnum.OpenFile); _load.FileSelected += LoadWorld;
        Resized += FitHud; FitHud();
        StyleNotebook();
        RefreshPanels();
        _drawer.CurrentTab = 0;
        BuildWorldPulse(overlay);
        BuildPortraitWindow(overlay);
        _immersiveHint=new Button {Text="返回界面 · F10 / Esc",Visible=false,Alignment=HorizontalAlignment.Left};HudStyle.Button(_immersiveHint);HudStyle.QuietButton(_immersiveHint,12);_immersiveHint.Pressed+=()=>SetImmersive(false);
        _immersiveHint.AddThemeColorOverride("font_shadow_color",Colors.Black);_immersiveHint.AddThemeConstantOverride("shadow_offset_y",1);
        HudStyle.Float(_immersiveHint,new(0,1),new(20,-40),new(210,26));AddChild(_immersiveHint);
        ShowJournal(false);
        if (_capture.Length > 0)
        {
            var captureShield = new Control { MouseFilter = MouseFilterEnum.Stop };
            captureShield.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); overlay.AddChild(captureShield);
        }
    }
    private void SetImmersive(bool enabled)
    {
        if(enabled==_immersive||enabled&&(IsModelGallery||_scenarioConfirm.Visible))return;
        if(enabled)
        {
            ShowPortraitWindow(false);ShowSettings(false);ShowTools(false);SelectTool(PlayerTool.Inspect);
            if(_map is WorldView3D world)world.CancelPointerGesture();GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        }
        _immersive=enabled;_hudOverlay.Visible=!enabled;_immersiveHint.Visible=enabled;
    }
    private void FitHud() => FitHudFor(Size);
    private void FitHudFor(Vector2 size)
    {
        FitToolLibrary(size);
        var timeSize=_timePanel.GetCombinedMinimumSize().Max(new Vector2(286,72));
        var cameraSize=_cameraPanel.GetCombinedMinimumSize().Max(new Vector2(280,72));
        var commandSize=_commandPanel.GetCombinedMinimumSize().Max(new Vector2(360,50));
        HudStyle.Float(_timePanel,new(0,1),new(20,-16-timeSize.Y),timeSize);
        HudStyle.Float(_cameraPanel,new(1,1),new(-16-cameraSize.X,-16-cameraSize.Y),cameraSize);
        HudStyle.Float(_commandPanel,new(.5f,1),new(-commandSize.X/2,-16-commandSize.Y),commandSize);
        float statsWidth=Math.Clamp(size.X-600,600,740);
        HudStyle.Float(_statsPanel,new Vector2(.5f,0),new Vector2(-statsWidth/2,8),new Vector2(statsWidth,Mathf.Max(62,_statsPanel.GetCombinedMinimumSize().Y)));
        float journalWidth=Math.Clamp(size.X*.25f,348,396);
        _journalPanel.OffsetLeft=-journalWidth-16;_journalPanel.OffsetRight=-16;
        _journalPanel.OffsetBottom = size.Y - 100;
        _settingsPanel.OffsetBottom = Math.Min(size.Y - 280, 744);
        FitPortraitWindow(size);
        FitContextCard(size);
    }
    private void FitContextCard(Vector2 size)
    {
        float left=_dockPanel.Visible?_dockPanel.OffsetRight+16:322;
        float right=_journalPanel.Visible?size.X+_journalPanel.OffsetLeft-16:size.X-316;
        float width=MathF.Min(620,right-left),x=Math.Clamp((size.X-width)*.5f,left,right-width);
        float height=Mathf.Max(88,_contextPanel.GetCombinedMinimumSize().Y);
        float aboveControls=Mathf.Max(-_timePanel.OffsetTop,Mathf.Max(-_cameraPanel.OffsetTop,-_commandPanel.OffsetTop))+8;
        HudStyle.Float(_contextPanel,new Vector2(0,1),new Vector2(x,-aboveControls-height),new Vector2(width,height));
    }
    private void RefreshPresentationControls()=>RefreshPresentationControlsAt(Time.GetTicksMsec());
    private void RefreshPresentationControlsAt(ulong now)
    {
        if(_contextPanel==null)return;
        var world=(WorldView3D)_map;
        _cameraReadout.Text=world.CameraReadout;
        _followCameraButton.Disabled=SelectedSlot<0;
        _followCameraButton.SetPressedNoSignal(world.IsFollowing(SelectedSlot));
        _followCameraButton.TooltipText=SelectedSlot<0?"先选择一位居民，再跟随观察":world.IsFollowing(SelectedSlot)?"停止跟随，保留当前镜头":"跟随当前居民；再次点击停止";
        _status.TooltipText=_status.Text;
        int slot=SelectedSlot;
        bool target=ContextTarget(out int x,out int y,out string title);
        if(_contextStatus!=_status.Text){_contextStatus=_status.Text;_contextStatusAt=now;}
        bool generic=_status.Text.StartsWith("观察模式：",StringComparison.Ordinal)||_status.Text.StartsWith(ToolNames[(int)PlayerTool.Inspect]+" · ",StringComparison.Ordinal);
        bool freshFeedback=!generic&&now-_contextStatusAt<6000;
        bool notebookShowsSelection=_journalPanel.Visible&&_drawer.CurrentTab==1&&target;
        // The notebook owns these facts while its selection page is open. Keep
        // fresh action feedback, and retain the card on history/chart pages.
        _contextPanel.Visible=!_dockPanel.Visible&&!IsModelGallery&&(!notebookShowsSelection||freshFeedback);
        _contextFacts.Visible=target&&Tool==PlayerTool.Inspect&&(generic||now-_contextStatusAt>=6000);_status.Visible=!_contextFacts.Visible;
        if(_contextFacts.Visible)
        {
            if(slot>=0)_contextFacts.Text=JobName(Sim.Agents.JobOf(slot))+" · "+PhaseName(Sim.Agents.PhaseOf(slot))+"\n"+$"生命 {Sim.Agents.HealthOf(slot):P0} · 饥饿 {Sim.Agents.HungerOf(slot):P0} · 干渴 {Sim.Agents.ThirstOf(slot):P0}";
            else if(_selectedAnimal>=0)_contextFacts.Text=$"年龄 {Sim.Wildlife.AgeDaysOf(_selectedAnimal)} 天 · "+ActionRegistry.DisplayNameOf(Sim.Wildlife.ActionOf(_selectedAnimal))+"\n"+$"能量 {Sim.Wildlife.EnergyOf(_selectedAnimal):P0} · 干渴 {Sim.Wildlife.ThirstOf(_selectedAnimal):P0}";
            else if(_selectedWolf>=0){foreach(var wolf in Sim.Predators.Wolves)if(wolf.Id==_selectedWolf)_contextFacts.Text=$"年龄 {wolf.AgeDays} 天 · 狼\n能量 {wolf.Energy:P0} · 干渴 {wolf.Thirst:P0}";}
            else{var tile=Sim.World.TileAt(x,y);_contextFacts.Text=$"湿度 {tile.Moisture:P0} · 肥力 {tile.Fertility:P0}\n植被 {tile.Vegetation:P0} · 踩踏 {tile.FootTraffic:P0}";}
            _contextFacts.TooltipText=_contextFacts.Text+"\n左键拖动平移 · 右键旋转/俯仰 · 滚轮缩放"+(generic?"":"\n上次操作："+_status.Text);
        }
        _contextFocus.Visible=_contextDetails.Visible=target&&Tool==PlayerTool.Inspect;
        _contextFocus.GetParent<Control>().Visible=_contextFocus.Visible;
        _contextDetails.Text=slot>=0?"档案":"手记";
        _contextFocus.TooltipText="靠近当前选择，保留鼠标旋转与缩放";
        _contextDetails.TooltipText=slot>=0?"展开居民观察窗":"查看所选动物或土地的详细手记";
        if(Tool==PlayerTool.Inspect)
        {
            _toolBadge.Text=slot>=0?title+" · "+ActionRegistry.DisplayNameOf(Sim.Agents.ActionOf(slot)):target?title+" · "+x+", "+y:"自由观察";
            _toolBadge.TooltipText=slot>=0?JobName(Sim.Agents.JobOf(slot))+" · "+PhaseName(Sim.Agents.PhaseOf(slot)):target?"近看或打开手记；观察不会干预世界":"选择人物、动物或土地，观察世界变化";
        }
        FitContextCard(Size);
    }
    private bool ContextTarget(out int x,out int y,out string title)
    {
        x=_selectedX;y=_selectedY;title="";int slot=SelectedSlot;
        if(slot>=0){x=Sim.Agents.XOf(slot);y=Sim.Agents.YOf(slot);title=Sim.Agents.NameOrOverride(slot);return true;}
        if(_selected>=0)return false;
        if(_selectedAnimal>=0)
        {
            if(!Sim.Wildlife.IsAlive(_selectedAnimal)||Sim.Wildlife.GenerationOf(_selectedAnimal)!=_selectedAnimalGeneration)return false;
            x=Sim.Wildlife.XOf(_selectedAnimal);y=Sim.Wildlife.YOf(_selectedAnimal);title="鹿";return true;
        }
        if(_selectedWolf>=0)
        {foreach(var wolf in Sim.Predators.Wolves)if(wolf.Id==_selectedWolf){x=wolf.X;y=wolf.Y;title="狼";return true;}return false;}
        if(!Sim.World.IsInBounds(x,y))return false;
        title=Wild.At(x,y)?.Name??TerrainLabel(Sim.World.TerrainAt(x,y));return true;
    }
    private void FocusContextSelection()
    {
        if(ContextTarget(out int x,out int y,out _))_map.Focus(x,y,60);
        RefreshPresentationControls();
    }
    private void OpenContextDetails()
    {
        if(!ContextTarget(out _,out _,out _))return;
        ShowJournal(true);_drawer.CurrentTab=1;RefreshPanels();if(SelectedSlot>=0)ShowPortraitWindow(true);
    }
    private void FitToolLibrary(Vector2 size)
    {
        // Keep categories and parameters stationary while only the tool list scrolls.
        float controls=Mathf.Max(72,Mathf.Max(_timePanel?.GetCombinedMinimumSize().Y??0,_cameraPanel?.GetCombinedMinimumSize().Y??0));
        controls=Mathf.Max(controls,_commandPanel?.GetCombinedMinimumSize().Y??0);
        float height=Math.Min(size.Y-96-16-controls-12,704);
        HudStyle.Float(_dockPanel,Vector2.Zero,new Vector2(20,96),new Vector2(416,height));
    }
    private void ValidateHudLayout()
    {
        double Luminance(Color c)
        {
            double Linear(double x)=>x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);
            return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);
        }
        foreach(var foreground in new[]{HudStyle.Text,HudStyle.Muted,HudStyle.Accent})
        foreach(var background in new[]{HudStyle.Surface,HudStyle.Wash})
        {
            double a=Luminance(foreground),b=Luminance(background);
            if((Math.Max(a,b)+.05)/(Math.Min(a,b)+.05)<4.5)
                throw new InvalidOperationException("HUD text contrast below 4.5:1");
        }
        try
        {
            foreach (Vector2 size in new[] { new Vector2(1280, 800), new Vector2(1600, 1000), new Vector2(1920, 1080) })
            {
                FitHudFor(size);
                // Anchors plus offsets are the authored rectangles, independent of one deferred layout pass.
                Rect2 Bounds(Control c) => new(new Vector2(size.X * c.AnchorLeft + c.OffsetLeft, size.Y * c.AnchorTop + c.OffsetTop),
                    new Vector2(c.OffsetRight - c.OffsetLeft, c.OffsetBottom - c.OffsetTop));
                Rect2 dock = Bounds(_dockPanel), time = Bounds(_timePanel), camera = Bounds(_cameraPanel);
                if(Bounds(_worldPanel).Intersects(Bounds(_statsPanel)))throw new InvalidOperationException("Top world and resource panels overlap at "+size);
                if (dock.Intersects(time) || dock.Intersects(camera) || time.Intersects(camera) || Bounds(_commandPanel).Intersects(time) || Bounds(_commandPanel).Intersects(camera)) { throw new InvalidOperationException("HUD controls overlap at " + size); }
                if(_brushPanel.GetParent()!=_paletteRow.GetParent().GetParent().GetParent().GetParent())
                    throw new InvalidOperationException("Tool parameters are detached from the tool library");
                foreach (var panel in new[] { _dockPanel, _commandPanel, _timePanel, _cameraPanel, _journalPanel, _settingsPanel, _portraitWindow })
                {
                    Rect2 bounds = Bounds(panel);
                    if (bounds.Position.X < 0 || bounds.Position.Y < 0 || bounds.End.X > size.X || bounds.End.Y > size.Y)
                        { throw new InvalidOperationException("HUD outside window at " + size); }
                }
                bool tools=_dockPanel.Visible,journal=_journalPanel.Visible;
                try
                {
                    for(int state=0;state<4;state++)
                    {
                        _dockPanel.Visible=(state&1)!=0;_journalPanel.Visible=(state&2)!=0;FitContextCard(size);
                        var context=Bounds(_contextPanel);
                        if(context.Size.X<320||context.Position.X<0||context.End.X>size.X||context.End.Y>size.Y||context.Intersects(time)||context.Intersects(camera)||
                            (_dockPanel.Visible&&context.Intersects(Bounds(_dockPanel)))||(_journalPanel.Visible&&context.Intersects(Bounds(_journalPanel))))
                            throw new InvalidOperationException("Context card overlaps an open panel at "+size);
                    }
                }
                finally {_dockPanel.Visible=tools;_journalPanel.Visible=journal;FitContextCard(size);}
            }
        }
        finally { FitHud(); }
    }
    private static PanelContainer Surface(Control parent, Vector2 anchor, Vector2 offset, Vector2 size, int padding = 16)
    {
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", HudStyle.Frame(padding));
        HudStyle.Float(panel, anchor, offset, size); parent.AddChild(panel); panel.AddChild(new HudBevel { Margin = padding, Visible=false }); return panel;
    }
    private static void ClearSurface(PanelContainer panel)
    { panel.AddThemeStyleboxOverride("panel", HudStyle.Box(new Color(0, 0, 0, 0), 0, 8, false)); panel.GetChild<HudBevel>(0).Visible = false; }
    private void BuildResidentVitals(VBoxContainer parent)
    {
        _residentVitals = new VBoxContainer { Visible = false }; _residentVitals.AddThemeConstantOverride("separation", 12); parent.AddChild(_residentVitals);
        var residentHeader=new HBoxContainer();residentHeader.AddThemeConstantOverride("separation",14);_residentVitals.AddChild(residentHeader);
        var portraitFrame=new PanelContainer {CustomMinimumSize=new(108,138),SizeFlagsHorizontal=SizeFlags.Fill,SizeFlagsVertical=SizeFlags.ShrinkBegin};_portraitHome=portraitFrame;
        portraitFrame.AddThemeStyleboxOverride("panel",HudStyle.Box(HudStyle.Ink,5,0,false));residentHeader.AddChild(portraitFrame);
        _residentPortrait = new ResidentPortrait { Compact = true,NotebookThumbnail=true, SizeFlagsHorizontal = SizeFlags.ExpandFill };portraitFrame.AddChild(_residentPortrait);
        _residentPortraitLink=new Button {Name="ResidentPortraitLink",Text="近看居民  ↗",CustomMinimumSize=new(108,138),TooltipText="点击近看居民 · 展开后可拖动旋转、滚轮缩放；Enter / 空格也可打开"};
        HudStyle.Button(_residentPortraitLink);_residentPortraitLink.SetMeta("portrait_link",true);_residentPortraitLink.AddThemeFontSizeOverride("font_size",12);
        _residentPortraitLink.AddThemeColorOverride("font_shadow_color",HudStyle.Ink);_residentPortraitLink.AddThemeConstantOverride("shadow_offset_y",1);_residentPortraitLink.AddThemeConstantOverride("outline_size",2);
        foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled"})
        {
            var box=HudStyle.Box(state is "hover" or "pressed"?new Color(HudStyle.Accent,.07f):Colors.Transparent,5,4,false);
            box.ContentMarginTop=108;box.ContentMarginBottom=8;box.BorderColor=new Color(HudStyle.Accent,state=="normal"?.25f:.8f);box.BorderWidthLeft=box.BorderWidthRight=box.BorderWidthTop=box.BorderWidthBottom=1;
            _residentPortraitLink.AddThemeStyleboxOverride(state,box);
        }
        portraitFrame.AddChild(_residentPortraitLink);_residentPortraitLink.Pressed+=()=>ShowPortraitWindow(true);
        _residentTitle = HudStyle.Heading("", 22); _residentSubtitle = HudStyle.Label("", 11, true);
        var identity = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; identity.AddThemeConstantOverride("separation", 4); residentHeader.AddChild(identity);
        identity.AddChild(_residentTitle); identity.AddChild(_residentSubtitle);
        _residentTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;_residentTitle.MaxLinesVisible=2;_residentTitle.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        _residentSubtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var activityBody=new VBoxContainer();activityBody.AddThemeConstantOverride("separation",2);identity.AddChild(activityBody);
        activityBody.AddChild(HudStyle.Label("当前行为",11,true));
        _residentAction=HudStyle.Heading("",14);_residentActionPhase=HudStyle.Label("",11,true);
        _residentAction.AutowrapMode=_residentActionPhase.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        activityBody.AddChild(_residentAction);activityBody.AddChild(_residentActionPhase);
        _residentSummary=new VBoxContainer();_residentSummary.AddThemeConstantOverride("separation",8);_residentVitals.AddChild(_residentSummary);
        var needs=new VBoxContainer();needs.AddThemeConstantOverride("separation",3);_residentSummary.AddChild(needs);
        var needsHeading=HudStyle.Label("身体状态 · 越高越好",11,true);needs.AddChild(needsHeading);
        foreach(int i in new[]{0,3,1,2})
        {
            if(i==1)needs.AddChild(HudStyle.Label("生理需求 · 越低越好",11,true));
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",8);needs.AddChild(row);
            _vitalLabels[i]=HudStyle.Label("",13);_vitalLabels[i].CustomMinimumSize=new(40,22);row.AddChild(_vitalLabels[i]);
            var bar = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(56, 4), SizeFlagsHorizontal=SizeFlags.ExpandFill, SizeFlagsVertical=SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
            bar.AddThemeStyleboxOverride("background", HudStyle.Box(HudStyle.Border, 2, 0, false));
            bar.AddThemeStyleboxOverride("fill", HudStyle.Box(HudStyle.Accent, 2, 0, false)); row.AddChild(bar); _vitalBars[i] = bar;
            var state=_vitalStates[i]=HudStyle.Label("",11,true);state.CustomMinimumSize=new(48,22);state.HorizontalAlignment=HorizontalAlignment.Right;row.AddChild(state);
            var value=_vitalValues[i]=HudStyle.Label("",14);value.AddThemeFontOverride("font",HudStyle.NumericFont);value.CustomMinimumSize=new(48,22);value.HorizontalAlignment=HorizontalAlignment.Right;row.AddChild(value);
        }
        _residentSummary.AddChild(new HSeparator());
        _residentInventoryToggle=ActionButton(_residentSummary,"随身物资",()=>SetInventoryExpanded(!_inventoryExpanded));
        _residentInventoryToggle.Alignment=HorizontalAlignment.Left;HudStyle.QuietButton(_residentInventoryToggle,13);
        _residentInventory=new VBoxContainer {Visible=false};_residentInventory.AddThemeConstantOverride("separation",6);_residentSummary.AddChild(_residentInventory);
        var inventory=new GridContainer {Columns=2};inventory.AddThemeConstantOverride("h_separation",12);inventory.AddThemeConstantOverride("v_separation",4);_residentInventory.AddChild(inventory);
        string[] names={"食物","木材","石料","铁矿"};
        for(int i=0;i<4;i++)
        {
            var row=new HBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};inventory.AddChild(row);
            var label=HudStyle.Label(names[i],12,true);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(label);
            var amount=_residentStock[i]=HudStyle.Label("",14);amount.AddThemeFontOverride("font",HudStyle.NumericFont);row.AddChild(amount);
        }
    }
    private void SetInventoryExpanded(bool expanded)
    {
        _inventoryExpanded=expanded;RefreshInventoryDisclosure();
    }
    private void RefreshInventoryDisclosure()
    {
        int slot=SelectedSlot;if(slot<0)return;
        var carried=new List<string>();
        string[] names={"食物","木材","石料","铁矿"};
        for(int i=0;i<4;i++)
        {
            float amount=Sim.Agents.InventoryOf(slot,PortraitResources[i]);
            if(amount>.01f)carried.Add(names[i]+" "+amount.ToString("0.#"));
        }
        string summary=carried.Count==0?"未携带物资":string.Join(" · ",carried);
        string label="随身物资 · "+(carried.Count==0?"空":carried.Count+" 类")+(_inventoryExpanded?"   收起":"   展开");
        _residentInventoryToggle.Text=label;_residentInventoryToggle.TooltipText=summary;
        _residentInventory.Visible=_inventoryExpanded;
        if(_portraitInventoryToggle!=null)
        {
            _portraitInventoryToggle.Text=label;_portraitInventoryToggle.TooltipText=summary;
            _portraitInventory.Visible=_inventoryExpanded;
        }
    }
    private void SetResidentPage(int page)
    {
        _residentPage=Math.Clamp(page,0,2);_decisionDetails=_residentPage==2;
        RefreshPanels();if(_inspector.Visible)FadeIn(_inspector);
    }
    private void BuildLandVitals(VBoxContainer parent)
    {
        _landVitals = new VBoxContainer { Visible = false };
        _landVitals.AddThemeConstantOverride("separation", 9); parent.AddChild(_landVitals);
        _landTitle = HudStyle.Heading("土地", 24); _landTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _landSubtitle = HudStyle.Label("", 12, true);
        _landVitals.AddChild(_landTitle); _landVitals.AddChild(_landSubtitle);
        var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", HudStyle.Box(new Color(.24f, .26f, .22f, .32f), 3, 12, false)); _landVitals.AddChild(card);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 8); card.AddChild(body);
        body.AddChild(HudStyle.Label("水土与生长", 11, true));
        Color[] colors = { new("#8daeb1"), new("#baab7e"), new("#91a781") };
        for (int i = 0; i < 3; i++)
        {
            _landLabels[i] = HudStyle.Label("", 12); body.AddChild(_landLabels[i]);
            var bar = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4), MouseFilter = MouseFilterEnum.Ignore };
            bar.AddThemeStyleboxOverride("background", HudStyle.Box(new Color(0, 0, 0, .25f), 1, 0, false));
            bar.AddThemeStyleboxOverride("fill", HudStyle.Box(colors[i], 1, 0, false)); body.AddChild(bar); _landBars[i] = bar;
        }
    }
    private void RefreshLandVitals()
    {
        _landTransition?.Kill();
        _landVitals.Visible = _drawer.GetTabTitle(1) == "土地";
        if (!_landVitals.Visible) { return; }
        var tile = Sim.World.TileAtClamped(_selectedX, _selectedY);
        _landTitle.Text = Wild.At(_selectedX, _selectedY)?.Name ?? "土地";
        _landSubtitle.Text = $"{TerrainLabel(tile.Terrain)}  ·  {_selectedX}, {_selectedY}";
        float[] values = { tile.Moisture, tile.Fertility, tile.Vegetation };
        string[] names = { "土壤湿度", "土地肥力", "植被覆盖" };
        _landTransition = HudStyle.MotionEnabled ? CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out) : null;
        for (int i = 0; i < 3; i++)
        {
            _landLabels[i].Text = names[i] + "   " + values[i].ToString("P0");
            if (HudStyle.MotionEnabled) { _landTransition!.TweenProperty(_landBars[i], "value", values[i], .18); }
            else { _landBars[i].Value = values[i]; }
        }
    }
    private void RefreshResidentVitals()
    {
        if(_editingResidentName&&_nameEditingPersonId!=_selectedPersonId)SetNameEditing(false,false);
        _vitalTransition?.Kill();
        RefreshLandVitals();
        int slot = SelectedSlot; _residentVitals.Visible = slot >= 0;
        _residentPages.Visible=slot>=0;_residentSummary.Visible=_residentPage==0;
        _inspector.Visible=slot<0||_residentPage!=0;
        for(int i=0;i<3;i++)_residentPageButtons[i].SetPressedNoSignal(_residentPage==i);
        _residentControls.Visible = _selectedPersonId != 0;
        _residentNameControls.Visible = _selectedPersonId != 0 && _editingResidentName;
        if (slot < 0) { ShowPortraitWindow(false);return; }
        var a = Sim.Agents;
        for(int i=0;i<4;i++)_residentStock[i].Text=a.InventoryOf(slot,PortraitResources[i]).ToString("F1");
        RefreshInventoryDisclosure();
        _residentPortrait.ShowResident(Sim.Society.Identity(slot), slot, a.LifeStageOf(slot) == SandBoxSim.Core.Agents.LifeStage.Child, a.JobOf(slot));
        _residentPortrait.ShowActivity(a.ActionOf(slot),a.PhaseOf(slot),VisualPaused);
        ResourceKind cargoKind=ResourceKind.Food;float cargoAmount=0;
        foreach(var resource in ResidentRig.CargoKinds){float amount=a.InventoryOf(slot,resource);if(amount>cargoAmount){cargoAmount=amount;cargoKind=resource;}}
        _residentPortrait.ShowCargo(cargoAmount>.01f,cargoKind);
        _residentAction.Text = a.ActionOf(slot)==ActionKind.None?"休息与观察":ActionRegistry.DisplayNameOf(a.ActionOf(slot));
        _residentActionPhase.Text = (a.PhaseOf(slot)==ActionPhase.Moving?"行进中":a.PhaseOf(slot)==ActionPhase.Done&&_residentPortrait.FinishingGesture?"已完成 · 动作收尾":PhaseName(a.PhaseOf(slot)))+(a.PhaseOf(slot) is ActionPhase.Moving or ActionPhase.Executing ? "  ·  目的地 "+a.TargetOf(slot):"");
        _residentTitle.Text = a.NameOrOverride(slot);
        _residentTitle.TooltipText=_residentTitle.Text;
        _residentSubtitle.Text = $"{a.AgeDaysOf(slot)} 天  /  {LifeStageName(a.LifeStageOf(slot))}  /  {JobName(a.JobOf(slot))}  ·  {a.PositionOf(slot)}";
        if(_portraitOverlay?.Visible==true)RefreshPortraitFacts();
        float[] values = { a.HealthOf(slot), a.HungerOf(slot), a.ThirstOf(slot), 1 - a.FatigueOf(slot) };
        string[] names = { "生命", "饥饿", "干渴", "精力" };
        _vitalTransition = HudStyle.MotionEnabled ? CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out) : null;
        for (int i = 0; i < values.Length; i++)
        {
            var readout=DescribeVital(i,values[i]);
            _vitalLabels[i].Text = names[i];_vitalValues[i].Text=values[i].ToString("P0");
            _vitalLabels[i].GetParent<Control>().TooltipText=readout.Hint;
            _vitalStates[i].Text=readout.State;_vitalStates[i].AddThemeColorOverride("font_color",readout.Tint);
            if (HudStyle.MotionEnabled) { _vitalTransition!.TweenProperty(_vitalBars[i], "value", values[i], .18); }
            else { _vitalBars[i].Value = values[i]; }
            ((StyleBoxFlat)_vitalBars[i].GetThemeStylebox("fill")).BgColor = readout.Tint;
        }
    }
    private static PlayerTool CategoryGlyph(string category)=>category switch
    {"生态"=>PlayerTool.MeadowSeeds,"地貌"=>PlayerTool.Mountain,"资源"=>PlayerTool.Wood,"灾害"=>PlayerTool.Fire,"祝福"=>PlayerTool.Rain,_=>PlayerTool.Human};
    private static SpinBox BrushField(VBoxContainer parent,string title,int min,int max,int value,out HSlider slider)
    {
        var heading=new HBoxContainer();parent.AddChild(heading);
        var label=HudStyle.Label(title,12,true);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;heading.AddChild(label);
        var spin=new SpinBox {MinValue=min,MaxValue=max,Value=value,Step=1,CustomMinimumSize=new(66,0)};heading.AddChild(spin);
        slider=new HSlider {MinValue=min,MaxValue=max,Value=value,Step=1,CustomMinimumSize=new(0,20),SizeFlagsHorizontal=SizeFlags.ExpandFill,TooltipText=title+" · 拖动调整，也可在上方输入精确数值",MouseDefaultCursorShape=CursorShape.PointingHand};parent.AddChild(slider);
        var range=slider;range.ValueChanged+=v=>spin.Value=v;spin.ValueChanged+=v=>range.SetValueNoSignal(v);
        return spin;
    }
    private Label Metric(HBoxContainer row, string title, string value, int size)
    {
        var item = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; item.AddThemeConstantOverride("separation", 7); row.AddChild(item);
        item.AddChild(new TextureRect { Texture = HudSymbols.For(title), CustomMinimumSize = new Vector2(18, 28), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Modulate = Colors.White });
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); item.AddChild(stack); stack.AddChild(HudStyle.Label(title.Replace("储备", ""), 11, true));
        var metric = HudStyle.Label(value, size); metric.AddThemeFontOverride("font",HudStyle.NumericFont); stack.AddChild(metric);
        var trail=new HudMetricTrail {Modulate=Colors.Transparent};stack.AddChild(trail);trail.Sample(value);metric.SetMeta("trend",trail);
        _historyHoverOwners[trail]=item;
        // Keep actual history available on hover without six competing traces
        // permanently cutting through the quiet world header.
        void RevealHistory(bool visible)
        {
            if(_historyHoverTransitions.Remove(trail,out var prior))prior.Kill();
            if(!HudStyle.MotionEnabled){trail.Modulate=visible?Colors.White:Colors.Transparent;return;}
            var historyFade=trail.CreateTween();_historyHoverTransitions[trail]=historyFade;
            historyFade.TweenProperty(trail,"modulate",visible?Colors.White:Colors.Transparent,.14);
        }
        item.MouseEntered+=()=>RevealHistory(true);item.MouseExited+=()=>RevealHistory(false);
        item.TooltipText=title+" · 悬停查看最近 32 次实际数值变化；完整趋势见手记的曲线页。";return metric;
    }
    private static Godot.Button ActionButton(Container parent, string label, Action action,bool prominent=false)
    { var button = new Godot.Button { Text = label }; HudStyle.Button(button,prominent); parent.AddChild(button); button.Pressed += action; return button; }
    private void SetSpeed(int speed)
    {
        if(speed>0)_resumeSpeed=speed;_speed=speed;
        foreach(var pair in _speedButtons)pair.Value.SetPressedNoSignal(pair.Key==speed);
        if(_playStateLabel!=null)
        {
            _playStateLabel.Text=speed==0?"已暂停":speed+"× 流逝";
            _playStateLabel.AddThemeColorOverride("font_color",speed==0?HudStyle.Accent:HudStyle.Muted);
            _playStateLabel.TooltipText=speed==0?"模拟已暂停 · 空格继续":"世界以 "+speed+" 倍速演化 · 空格暂停";
        }
    }
    private void ShowCategory(string category)
    { bool close=_dockPanel.Visible && _category==category;SetCategory(category);ShowTools(!close); }
    private void ShowTools(bool visible)
    {
        if(visible)SetImmersive(false);
        if(visible&&_journalPanel.Visible)ShowJournal(false);
        if(visible&&_settingsOpen)ShowSettings(false);
        _dockPanel.Visible=visible;
        if(!visible)_toolSearch.ReleaseFocus();
        _inspectDockButton.SetPressedNoSignal(!visible&&Tool==PlayerTool.Inspect);
        _toolsMenuButton.SetPressedNoSignal(visible);
        _toolBadge.Visible=!visible;_status.Visible=!visible;
        if(_contextPanel!=null)_contextPanel.Visible=!visible&&!IsModelGallery;
        if(visible){((WorldView3D)_map).CancelPointerGesture();FadeIn(_dockPanel);}
        RefreshPresentationControls();
    }
    private void ShowJournal(bool visible) { if(visible)SetImmersive(false);if(visible&&Tool!=PlayerTool.Inspect)SelectTool(PlayerTool.Inspect);if(visible&&_dockPanel.Visible)ShowTools(false);_journalPanel.Visible = visible; _journalButton.SetPressedNoSignal(visible);_journalDockButton.SetPressedNoSignal(visible); if (_pulseButton != null) { _pulseButton.Visible = !visible; } if (visible) { FadeIn(_journalPanel); } RefreshPresentationControls(); }
    private void ShowSettings(bool visible)
    { if(visible)SetImmersive(false); if(visible&&_dockPanel.Visible)ShowTools(false);_settingsOpen = visible; _settingsButton.SetPressedNoSignal(visible); _settingsPanel.Visible = visible; _brushPanel.Visible = !visible && Tool != PlayerTool.Inspect; if (visible) { FadeIn(_settingsPanel); } }
    private readonly Dictionary<Control, Tween> _panelTransitions = new();
    private void FadeIn(Control panel)
    {
        if (_panelTransitions.TryGetValue(panel, out var previous)) { previous.Kill(); }
        panel.PivotOffset = new Vector2(panel.Size.X * (panel.AnchorLeft > .5f ? 1 : .5f), panel.Size.Y);
        if (!HudStyle.MotionEnabled) { panel.Scale = Vector2.One; panel.Modulate = Colors.White; return; }
        panel.Scale = new Vector2(.99f, .99f); panel.Modulate = new Color(1, 1, 1, 0);
        var transition = panel.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        transition.TweenProperty(panel, "modulate:a", 1f, .18); transition.TweenProperty(panel, "scale", Vector2.One, .32); _panelTransitions[panel] = transition;
    }
    private Tween? _pageTransition;
    private void RevealJournalPage()
    {
        _pageTransition?.Kill();
        foreach (Node child in _drawer.GetChildren()) { if (child is Control content) { content.Modulate = Colors.White; } }
        var page = _drawer.GetCurrentTabControl();
        if (page == null || !HudStyle.MotionEnabled || !_journalPanel.Visible) { return; }
        page.Modulate = new Color(1, 1, 1, .35f);
        _pageTransition = page.CreateTween();
        _pageTransition.TweenProperty(page, "modulate:a", 1f, .18).SetTrans(Tween.TransitionType.Sine);
    }
    private readonly Dictionary<Label, Tween> _metricTransitions = new();
    private void ResetMetricHistory()
    {
        foreach(var pair in _metricTransitions){pair.Value.Kill();pair.Key.Modulate=Colors.White;}
        _metricTransitions.Clear();
        foreach(var label in new[]{_populationLabel,_settlementLabel,_buildingLabel,_foodLabel,_woodLabel,_stoneLabel})
            if(label!=null&&label.HasMeta("trend"))((HudMetricTrail)label.GetMeta("trend").AsGodotObject()).ResetHistory();
    }
    private void ValidateMetricWorldReplacement()
    {
        string snapshot=EncodeClientWorld(),digest=StateHash.ComputeDigest(Sim),previousCheckpoint=_checkpoint,previousLabel=_experimentLabel;
        string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"sandboxsim-metric-"+Guid.NewGuid().ToString("N")+".simsave");
        var trail=(HudMetricTrail)_populationLabel.GetMeta("trend").AsGodotObject();
        try
        {
            _checkpoint=snapshot;trail.Sample("901");trail.Sample("902");RestoreCheckpoint();
            if(StateHash.ComputeDigest(Sim)!=digest||trail.SampleCount!=1)throw new InvalidOperationException("Checkpoint retains a different world's metric history");
            trail.Sample("903");System.IO.File.WriteAllText(path,snapshot);LoadWorld(path);
            if(StateHash.ComputeDigest(Sim)!=digest||trail.SampleCount!=1)throw new InvalidOperationException("Loaded world retains a previous metric trajectory");
        }
        finally
        {
            _checkpoint=previousCheckpoint;_experimentLabel=previousLabel;
            if(System.IO.File.Exists(path))System.IO.File.Delete(path);
        }
        GD.Print("METRIC_RESTORE_PASS: checkpoint/load clear prior trajectories without changing restored simulation");
    }
    private void UpdateMetric(Label label, string value)
    {
        if(label.HasMeta("trend"))((HudMetricTrail)label.GetMeta("trend").AsGodotObject()).Sample(value);
        if (label.Text == value) { return; }
        label.Text = value;
        if (_metricTransitions.TryGetValue(label, out var previous)) { previous.Kill(); }
        if (!HudStyle.MotionEnabled || !label.IsVisibleInTree()) { label.Modulate = Colors.White; return; }
        label.Modulate = new Color(1.18f, 1.12f, .88f);
        var transition = label.CreateTween();
        transition.TweenProperty(label, "modulate", Colors.White, .6).SetTrans(Tween.TransitionType.Sine);
        _metricTransitions[label] = transition;
    }
    private void ResetHudMotion()
    {
        _residentPortrait.SettleMotion();
        foreach(var node in GetTree().GetNodesInGroup("hud_button_motion"))if(node is HudButtonDetail detail)detail.Settle();
        foreach (var pair in _panelTransitions) { pair.Value.Kill(); pair.Key.Scale = Vector2.One; pair.Key.Modulate = Colors.White; }
        foreach (var pair in _metricTransitions) { pair.Value.Kill(); pair.Key.Modulate = Colors.White; }
        foreach(var transition in _historyHoverTransitions.Values)transition.Kill();_historyHoverTransitions.Clear();
        foreach(var pair in _historyHoverOwners)
            pair.Key.Modulate=pair.Value.IsVisibleInTree()&&pair.Value.GetGlobalRect().HasPoint(pair.Value.GetGlobalMousePosition())?Colors.White:Colors.Transparent;
        _pageTransition?.Kill();
        _vitalTransition?.Kill(); _landTransition?.Kill();
        foreach (Node child in _drawer.GetChildren()) { if (child is Control content) { content.Modulate = Colors.White; } }
        RefreshResidentVitals();
    }
    private void ValidateHudMotion()
    {
        InterfaceFonts.Validate();
        bool enabled = HudStyle.MotionEnabled;
        try
        {
            int page=_residentPage;string digest=StateHash.ComputeDigest(Sim);
            if(SelectedSlot>=0)
            {
                _residentPageButtons[1].EmitSignal(BaseButton.SignalName.Pressed);
                if(!_inspector.Visible||_residentSummary.Visible||!_inspector.Text.Contains("居所与关系"))throw new InvalidOperationException("Resident life page loses actual relationship records");
                _residentPageButtons[2].EmitSignal(BaseButton.SignalName.Pressed);
                if(!_inspector.Visible||_residentSummary.Visible||!_inspector.Text.Contains("性格倾向")||_inspector.Text.Contains("居所与关系"))throw new InvalidOperationException("Resident decision page mixes in unrelated biography");
                _residentPageButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
                if(_inspector.Visible||!_residentSummary.Visible)throw new InvalidOperationException("Resident overview repeats long inspector prose");
                bool inventoryExpanded=_inventoryExpanded;
                try
                {
                    SetInventoryExpanded(false);_residentInventoryToggle.EmitSignal(BaseButton.SignalName.Pressed);
                    if(!_inventoryExpanded||!_residentInventory.Visible||!_portraitInventory.Visible||_residentInventoryToggle.Text!=_portraitInventoryToggle.Text)
                        throw new InvalidOperationException("Resident inventory disclosure differs between notebook and portrait");
                    _residentPageButtons[1].EmitSignal(BaseButton.SignalName.Pressed);_residentPageButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
                    if(!_residentInventory.Visible)throw new InvalidOperationException("Page navigation loses expanded carried inventory");
                    _portraitInventoryToggle.EmitSignal(BaseButton.SignalName.Pressed);
                    if(_inventoryExpanded||_residentInventory.Visible||_portraitInventory.Visible)throw new InvalidOperationException("Portrait inventory does not collapse its notebook counterpart");
                }
                finally {SetInventoryExpanded(inventoryExpanded);}
                SetResidentPage(page);
                if(StateHash.ComputeDigest(Sim)!=digest)throw new InvalidOperationException("Resident page navigation changes world state");
            }
            HudStyle.MotionEnabled = true;
            ShowJournal(true);ShowSettings(false);
            ShowCategory("生态");_toolButtons[PlayerTool.MeadowSeeds].EmitSignal(BaseButton.SignalName.Pressed);
            if(_journalPanel.Visible)throw new InvalidOperationException("Tool palette obscures an open notebook");
            if(Tool!=PlayerTool.MeadowSeeds||!_toolButtons[PlayerTool.MeadowSeeds].ButtonPressed||!_brushPanel.Visible)
                throw new InvalidOperationException("Tool card did not select the real ecological brush");
            SetCategory("地貌");
            if(_dockPanel.GetCombinedMinimumSize().Y>_dockPanel.OffsetBottom-_dockPanel.OffsetTop+.5f)
                throw new InvalidOperationException($"Tool library minimum {_dockPanel.GetCombinedMinimumSize()} exceeds height {_dockPanel.OffsetBottom-_dockPanel.OffsetTop}; viewport {Size}; brush {_brushPanel.GetCombinedMinimumSize()}; scroll {_toolScroll.GetCombinedMinimumSize()}");
            ShowJournal(true);
            if(_dockPanel.Visible)throw new InvalidOperationException("Notebook did not close the tool palette");
            SelectTool(PlayerTool.Inspect);ShowSettings(true);
            _UnhandledKeyInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
            if(_settingsPanel.Visible||!_journalPanel.Visible)throw new InvalidOperationException("Escape did not close topmost settings");
            _UnhandledKeyInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
            if(_journalPanel.Visible)throw new InvalidOperationException("Escape did not close notebook");
            ShowJournal(true); ShowJournal(false); ShowJournal(true);
            if(!_journalButton.ButtonPressed||!_journalDockButton.ButtonPressed||!_inspectDockButton.ButtonPressed)
                throw new InvalidOperationException("Dock and header do not reflect the active journal/inspection mode");
            _panelTransitions[_journalPanel].CustomStep(1);
            if (_journalPanel.Scale != Vector2.One || _journalPanel.Modulate.A != 1)
                { throw new InvalidOperationException("Interrupted journal transition did not settle"); }
            string immersiveDigest=StateHash.ComputeDigest(Sim);var mapBounds=_map.GetRect();
            _UnhandledKeyInput(new InputEventKey {Keycode=Key.F10,Pressed=true});
            if(!_immersive||_hudOverlay.Visible||!_immersiveHint.Visible||_map.GetRect()!=mapBounds||Tool!=PlayerTool.Inspect)throw new InvalidOperationException("Immersive view changes the world rectangle or retains intervention UI");
            _UnhandledKeyInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
            if(_immersive||!_hudOverlay.Visible||_immersiveHint.Visible||!_journalPanel.Visible)throw new InvalidOperationException("Escape loses the previous notebook when returning from immersive view");
            SetImmersive(true);_immersiveHint.EmitSignal(BaseButton.SignalName.Pressed);
            if(_immersive||!_hudOverlay.Visible)throw new InvalidOperationException("Immersive return control cannot restore the mouse-driven interface");
            SetImmersive(true);_Input(new InputEventKey {Keycode=Key.K,CtrlPressed=true,Pressed=true});
            if(_immersive||!_hudOverlay.Visible||!_dockPanel.Visible||GetViewport().GuiGetFocusOwner()!=_toolSearch||StateHash.ComputeDigest(Sim)!=immersiveDigest)throw new InvalidOperationException("Immersive search fails to restore visible, focused controls or changes the simulation");
            ShowTools(false);SelectTool(PlayerTool.Inspect);ShowJournal(true);
            _drawer.CurrentTab = 1; RevealJournalPage(); _pageTransition?.CustomStep(1);
            if (_drawer.GetCurrentTabControl().Modulate.A != 1)
                { throw new InvalidOperationException("Journal page remained transparent"); }
            FadeIn(_settingsPanel);
            var history=(HudMetricTrail)_populationLabel.GetMeta("trend").AsGodotObject();
            _historyHoverOwners[history].EmitSignal(Control.SignalName.MouseEntered);
            if(!_historyHoverTransitions.TryGetValue(history,out var interruptedHistory))throw new InvalidOperationException("Metric history does not respond to pointer entry");
            HudStyle.MotionEnabled = false; ResetHudMotion();
            if(_historyHoverTransitions.Count!=0||interruptedHistory.IsRunning())throw new InvalidOperationException("Reduced motion leaves a metric hover fade running");
            Color expectedHistory=_historyHoverOwners[history].IsVisibleInTree()&&_historyHoverOwners[history].GetGlobalRect().HasPoint(_historyHoverOwners[history].GetGlobalMousePosition())?Colors.White:Colors.Transparent;
            if(history.Modulate!=expectedHistory)throw new InvalidOperationException("Interrupted metric hover does not settle to its actual pointer state");
            if (_settingsPanel.Scale != Vector2.One || _settingsPanel.Modulate.A != 1)
                { throw new InvalidOperationException("Reduced motion failed to restore panel"); }
            UpdateMetric(_populationLabel, Sim.Agents.LiveCount.ToString());
            if (_populationLabel.Modulate != Colors.White)
                { throw new InvalidOperationException("Reduced motion left a metric highlighted"); }
            if (_journalButton.GetChild<HudButtonDetail>(0).MouseFilter != MouseFilterEnum.Ignore)
                { throw new InvalidOperationException("Decorative feedback intercepts input"); }
            ValidateContextActions();ValidateResidentEditing();
        }
        finally { HudStyle.MotionEnabled = enabled; }
    }
    private void ValidateContextActions()
    {
        var selection=(_selected,_selectedGeneration,_selectedAnimal,_selectedAnimalGeneration,_selectedWolf,_selectedX,_selectedY,_selectedPersonId);
        string digest=StateHash.ComputeDigest(Sim),status=_status.Text;
        try
        {
            _status.Text="观察模式：测试";ClickTile(0,0,true);RefreshPresentationControls();
            ShowJournal(false);
            if(!_contextFocus.Visible||!_contextDetails.Visible||_contextDetails.Text!="手记"||!ContextTarget(out int x,out int y,out _)||x!=0||y!=0)
                throw new InvalidOperationException("Land selection has no accurate context actions");
            if(!_contextFacts.Visible||_status.Visible||!_contextFacts.Text.Contains("湿度"))throw new InvalidOperationException("Land context lacks live facts");
            _status.Text="已保存测试世界";RefreshPresentationControls();
            if(_contextFacts.Visible||!_status.Visible)throw new InvalidOperationException("Context facts hide action feedback");
            RefreshPresentationControlsAt(_contextStatusAt+6000);
            if(!_contextFacts.Visible||!_contextFacts.TooltipText.Contains("已保存测试世界")||_status.Text!="已保存测试世界")throw new InvalidOperationException("Transient feedback blocks live facts or loses its accessible history");
            _status.Text="观察模式：测试";RefreshPresentationControls();
            FocusContextSelection();OpenContextDetails();
            if(!_journalPanel.Visible||_drawer.CurrentTab!=1||_portraitOverlay.Visible)throw new InvalidOperationException("Land context opens the wrong observation surface");
            foreach(int animal in Sim.Wildlife.AliveIndices())
            {
                InspectActor(-1,animal);RefreshPresentationControls();
                if(!ContextTarget(out x,out y,out _)||x!=Sim.Wildlife.XOf(animal)||y!=Sim.Wildlife.YOf(animal))throw new InvalidOperationException("Animal context position is stale");
                _selectedAnimalGeneration++;RefreshPresentationControls();
                if(_contextFocus.Visible||_contextDetails.Visible)throw new InvalidOperationException("Stale animal identity retains context actions");break;
            }
            if(digest!=StateHash.ComputeDigest(Sim))throw new InvalidOperationException("Context actions modified simulation state");
        }
        finally
        {
            (_selected,_selectedGeneration,_selectedAnimal,_selectedAnimalGeneration,_selectedWolf,_selectedX,_selectedY,_selectedPersonId)=selection;
            _status.Text=status;
            RefreshPanels();
        }
        GD.Print("CONTEXT_ACTIONS_PASS: resident/animal/land targets, stale generation rejection, land notebook, camera proximity and read-only observation");
    }
    private void ValidateToolLibrary()
    {
        string digest=StateHash.ComputeDigest(Sim);double radius=_radius.Value,strength=_strength.Value;int scenario=Scenario,speed=_speed,resume=_resumeSpeed;
        try
        {
            ShowSettings(false);SetCategory("生命");ShowTools(false);
            _Input(new InputEventKey {Pressed=true,Keycode=Key.K,CtrlPressed=true});
            if(!_dockPanel.Visible||!_toolSearch.HasFocus()||!_toolsMenuButton.ButtonPressed)
                throw new InvalidOperationException("Tool search shortcut did not open and focus the library");
            _toolSearch.Text="河流";_toolSearch.EmitSignal(LineEdit.SignalName.TextChanged,_toolSearch.Text);
            if(!_toolButtons.ContainsKey(PlayerTool.River)||_emptyTools.Visible)
                throw new InvalidOperationException("Tool search cannot find a different category");
            foreach(var category in _categoryButtons.Values)
                if(category.ButtonPressed)throw new InvalidOperationException("Cross-category search misleadingly highlights a category");
            HandleToolSearchInput(new InputEventKey {Pressed=true,Keycode=Key.Enter});
            if(Tool!=PlayerTool.River||_toolSearch.HasFocus())throw new InvalidOperationException("Search Enter did not activate the result");
            _toolsMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            if(Tool!=PlayerTool.Inspect||_dockPanel.Visible)throw new InvalidOperationException("Closing Create leaves a hidden active brush");
            ShowTools(true);_toolSearch.GrabFocus();
            _toolSearch.Text="__no_match__";_toolSearch.EmitSignal(LineEdit.SignalName.TextChanged,_toolSearch.Text);
            if(_toolButtons.Count!=0||!_emptyTools.Visible)throw new InvalidOperationException("Tool search lacks an empty state");
            HandleToolSearchInput(new InputEventKey {Pressed=true,Keycode=Key.Escape});
            if(_toolSearch.Text!=""||!_dockPanel.Visible||!_categoryButtons["生命"].ButtonPressed)
                throw new InvalidOperationException("Search Escape did not reset the filter in place");
            SetCategory("地貌");
            if(_toolSearch.Text!=""||_toolButtons.Count!=13)throw new InvalidOperationException("Category selection leaves a stale search filter");
            _toolButtons[PlayerTool.River].EmitSignal(BaseButton.SignalName.Pressed);
            if(Tool!=PlayerTool.River||!_brushPanel.IsVisibleInTree())throw new InvalidOperationException("Selected brush has no visible parameter editor");
            var minimum=_dockPanel.GetCombinedMinimumSize();
            if(minimum.X>368.5f||minimum.Y>_dockPanel.OffsetBottom-_dockPanel.OffsetTop+.5f)
                throw new InvalidOperationException("Brush controls exceed the authored library size: "+minimum);
            _radiusSlider.Value=7;_strengthSlider.Value=31;
            if(_radius.Value!=7||_strength.Value!=31)throw new InvalidOperationException("Brush slider and numeric fields diverge");
            _radius.Value=3;_strength.Value=18;
            if(_radiusSlider.Value!=3||_strengthSlider.Value!=18)throw new InvalidOperationException("Brush numeric edits leave stale sliders");
            AdjustBrushRadius(100);if(_radius.Value!=_radius.MaxValue)throw new InvalidOperationException("Brush size exceeds its maximum");
            AdjustBrushRadius(-100);if(_radius.Value!=_radius.MinValue)throw new InvalidOperationException("Brush size exceeds its minimum");
            _UnhandledKeyInput(new InputEventKey {Pressed=true,Keycode=Key.Escape});
            if(Tool!=PlayerTool.Inspect||_dockPanel.Visible)throw new InvalidOperationException("Escape leaves a hidden active brush");
            _scenarioPicker.EmitSignal(OptionButton.SignalName.ItemSelected,(scenario+1)%SandboxScenarios.Names.Length);
            if(Scenario!=scenario||StateHash.ComputeDigest(Sim)!=digest||!_scenarioConfirm.Visible)
                throw new InvalidOperationException("Scenario picker replaces the world before confirmation");
            _scenarioConfirm.Hide();
            SetSpeed(8);_UnhandledKeyInput(new InputEventKey {Pressed=true,Keycode=Key.Space});
            if(_speed!=0||_playStateLabel.Text!="已暂停")throw new InvalidOperationException("Space does not pause or update the time indicator");
            _UnhandledKeyInput(new InputEventKey {Pressed=true,Keycode=Key.Space});
            if(_speed!=8||_playStateLabel.Text!="8× 流逝")throw new InvalidOperationException("Resume loses the selected time multiplier or time indicator");
        }
        finally{_radius.Value=radius;_strength.Value=strength;SetSpeed(speed);_resumeSpeed=resume;_scenarioConfirm.Hide();SelectTool(PlayerTool.Inspect);SetCategory("生命");}
        if(StateHash.ComputeDigest(Sim)!=digest)throw new InvalidOperationException("Browsing tools mutated the world");
        GD.Print("TOOL_LIBRARY_PASS: keyboard search/activation, category and mode states, empty/filter reset, attached parameters, slider/numeric synchronization, radius bounds, safe close, Escape and scenario replacement confirmation");
    }
    private void SetCategory(string category)
    {
        _category = category;_paletteGlyph.Texture=ToolGlyphs.For(CategoryGlyph(category));
        foreach (var pair in _categoryButtons) { pair.Value.SetPressedNoSignal(pair.Key == category); }
        _toolSearch.Text="";
        PopulateTools();
    }
    private static PlayerTool[] CategoryTools(string category) => category switch
    {
        "生态" => new[] { PlayerTool.MeadowSeeds, PlayerTool.BerrySeeds, PlayerTool.WetlandGarden, PlayerTool.Deadwood, PlayerTool.Forest, PlayerTool.Rain },
        "地貌" => new[] { PlayerTool.Grass, PlayerTool.River, PlayerTool.Mountain, PlayerTool.Sand, PlayerTool.Raise, PlayerTool.Lower, PlayerTool.RemoveWater, PlayerTool.Swamp, PlayerTool.Snow, PlayerTool.Desert, PlayerTool.Lava, PlayerTool.Farmland, PlayerTool.Road },
        "资源" => new[] { PlayerTool.Forest, PlayerTool.Food, PlayerTool.Wood, PlayerTool.Stone, PlayerTool.Iron, PlayerTool.Fertility },
        "灾害" => new[] { PlayerTool.Fire, PlayerTool.Lightning, PlayerTool.Flood, PlayerTool.Drought, PlayerTool.Plague, PlayerTool.Meteor },
        "祝福" => new[] { PlayerTool.Heal, PlayerTool.Rain, PlayerTool.BirthBlessing, PlayerTool.Production, PlayerTool.Fertility },
        _ => new[] { PlayerTool.Inspect, PlayerTool.Human, PlayerTool.Animal, PlayerTool.Wolf, PlayerTool.Forest, PlayerTool.Food }
    };
    private void PopulateTools()
    {
        if(_paletteRow==null)return;
        foreach (Node child in _paletteRow.GetChildren()) { _paletteRow.RemoveChild(child); child.QueueFree(); } _toolButtons.Clear();
        string query=_toolSearch.Text.Trim();
        foreach(var pair in _categoryButtons)pair.Value.SetPressedNoSignal(query.Length==0&&pair.Key==_category);
        _paletteCaption.Text=query.Length>0?"全部分类 / 搜索结果":_category+" / 选择工具后在世界中施加";
        _paletteRow.Columns=1;_paletteRow.AddThemeConstantOverride("v_separation",3);
        var seen=new HashSet<PlayerTool>();
        foreach(string category in query.Length>0?new[]{"生命","生态","地貌","资源","灾害","祝福"}:new[]{_category})
        foreach (PlayerTool tool in CategoryTools(category))
        {
            string title=ToolNames[(int)tool].Replace("创造", "").Replace("添加", "").Replace("生长", "").Replace(" / 选择", "").Replace("画笔", "");
            string description=tool switch
            {
                PlayerTool.Inspect=>"靠近，了解每个生命",PlayerTool.Human=>"让居民开始自己的生活",PlayerTool.Animal=>"让食草动物融入生态",PlayerTool.Wolf=>"引入捕食者的变化",
                PlayerTool.MeadowSeeds=>"花草随水土渐次生长",PlayerTool.BerrySeeds=>"为动物与居民留下食物",PlayerTool.WetlandGarden=>"在湿润土地恢复植被",PlayerTool.Deadwood=>"为土壤留下腐殖养分",
                PlayerTool.Grass=>"铺开草地，恢复生长空间",PlayerTool.River=>"画出水道，改变水分与通行",PlayerTool.Mountain=>"铺设山地，形成地貌屏障",PlayerTool.Sand=>"铺设干燥的砂质地表",
                PlayerTool.RemoveWater=>"移除水面，恢复陆地",PlayerTool.Swamp=>"形成湿地，保留水分",PlayerTool.Snow=>"覆盖积雪，改变地表条件",PlayerTool.Desert=>"铺设干旱荒地",PlayerTool.Lava=>"铺设熔岩，威胁附近生命",PlayerTool.Farmland=>"将土地转为农田",
                PlayerTool.Forest=>"让林地自然扩展",PlayerTool.Rain=>"润泽土地，改变水分",PlayerTool.Raise=>"渐渐抬升地表",PlayerTool.Lower=>"逐步压低地势",PlayerTool.Road=>"连接土地与生活痕迹",
                PlayerTool.Food=>"补充可采集的食物",PlayerTool.Heal=>"恢复附近生命的状态",PlayerTool.Fertility=>"改善土地的生长条件",
                _=>category=="灾害"?"局部改变，观察连锁反应":category=="地貌"?"拖动绘制，改变地表":"改变条件，观察自然回应"
            };
            if(query.Length>0&&!(title+description+category).Contains(query,StringComparison.OrdinalIgnoreCase))continue;
            if(!seen.Add(tool))continue;
            var button=new HudToolCard {Title=title,Description=description,Glyph=ToolGlyphs.For(tool),CustomMinimumSize=new(230,66),SizeFlagsHorizontal=SizeFlags.ExpandFill,ToggleMode=true,TooltipText=title+" · "+description};
            HudStyle.Button(button);button.CustomMinimumSize=new(230,66);button.AddThemeStyleboxOverride("normal",HudStyle.Box(Colors.Transparent,6,9,false));
            var selectedStyle=HudStyle.Box(HudStyle.Wash,6,9,false);selectedStyle.BorderWidthLeft=2;selectedStyle.BorderColor=HudStyle.Accent;button.AddThemeStyleboxOverride("pressed",selectedStyle);button.AddThemeStyleboxOverride("hover_pressed",selectedStyle);button.SetMeta("tool_row",true);
            var chosen=tool;button.Pressed+=()=>{_toolSearch.ReleaseFocus();SelectTool(chosen);};_paletteRow.AddChild(button);
            _toolButtons[tool]=button;button.SetPressedNoSignal(Tool==tool);
        }
        _emptyTools.Visible=_toolButtons.Count==0;
        _paletteCaption.Text=(query.Length>0?"搜索结果":_category)+"  /  "+_toolButtons.Count+" 种工具";
        _toolScroll.ScrollVertical=0;
        FitToolLibrary(Size);
    }

    private void HandleToolSearchInput(InputEvent input)
    {
        if(input is not InputEventKey {Pressed:true,Echo:false} key)return;
        if(key.Keycode==Key.Escape)
        {
            if(_toolSearch.Text.Length>0){_toolSearch.Text="";PopulateTools();}
            else{SelectTool(PlayerTool.Inspect);ShowTools(false);}
            _toolSearch.AcceptEvent();
        }
        else if(key.Keycode is Key.Enter or Key.KpEnter or Key.Down)
        {
            foreach(var pair in _toolButtons)
            {
                if(key.Keycode==Key.Down)pair.Value.GrabFocus();
                else{_toolSearch.ReleaseFocus();SelectTool(pair.Key);}
                break;
            }
            _toolSearch.AcceptEvent();
        }
    }

    private void UpdateHudSelection(string hint)
    {
        foreach (var pair in _toolButtons) { pair.Value.SetPressedNoSignal(pair.Key == Tool); }
        _brushTitle.Text = ToolNames[(int)Tool];
        // Keep a single-line summary visible; the tooltip retains the full explanation.
        _brushHint.Text = hint;_brushHint.TooltipText=hint;_brushTitle.TooltipText=hint;
        bool ecology=IsEcosystemTool(Tool);_brushParameters.Visible=!ecology;_ecologyFootprint.Visible=ecology;
        _brushControlsHint.Text=ecology?"左键栽植  ·  Alt + 左键平移  ·  Esc 退出":"左键施加  ·  Shift + 滚轮调范围  ·  Esc 退出";
        _brushPanel.Visible = Tool != PlayerTool.Inspect && !_settingsOpen;
        if (Tool == PlayerTool.Inspect && _toolsMenuButton != null) { ShowTools(false); }
        else if(_toolsMenuButton!=null&&!_settingsOpen&&!_dockPanel.Visible)ShowTools(true);
        FitToolLibrary(Size);
    }
}
