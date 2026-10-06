using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private TabContainer _drawer = null!;
    private PanelContainer _settingsPanel = null!, _journalPanel = null!, _brushPanel = null!;
    private PanelContainer _timePanel = null!, _cameraPanel = null!, _dockPanel = null!;
    private Label _toolBadge = null!, _dayLabel = null!, _populationLabel = null!, _settlementLabel = null!, _buildingLabel = null!;
    private Label _brushTitle = null!, _brushHint = null!;
    private bool _settingsOpen;
    private OptionButton _scenarioPicker = null!;
    private Godot.Button _settingsButton = null!, _journalButton = null!;
    private HBoxContainer _paletteRow = null!;
    private PanelContainer _commandPanel = null!;
    private Godot.Button _toolsMenuButton = null!;
    private Label _foodLabel = null!, _woodLabel = null!, _stoneLabel = null!;
    private readonly Dictionary<PlayerTool, Godot.Button> _toolButtons = new();
    private readonly Dictionary<int, Godot.Button> _speedButtons = new();
    private readonly Dictionary<string, Godot.Button> _categoryButtons = new();
    private string _category = "生命";
    private bool _decisionDetails;
    private VBoxContainer _residentVitals = null!;
    private HBoxContainer _residentControls = null!, _residentNameControls = null!;
    private ResidentPortrait _residentPortrait = null!;
    private Label _residentTitle = null!, _residentSubtitle = null!;
    private readonly Label[] _vitalLabels = new Label[4];
    private readonly ProgressBar[] _vitalBars = new ProgressBar[4];
    private Tween? _vitalTransition;

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
        SandBoxSim.Core.Agents.ActionPhase.Moving => "前往目标", SandBoxSim.Core.Agents.ActionPhase.Executing => "进行中",
        SandBoxSim.Core.Agents.ActionPhase.Done => "已完成", SandBoxSim.Core.Agents.ActionPhase.Failed => "受阻", _ => "休息中"
    };
    private string FormatInspector(string plain)
    {
        var result = new System.Text.StringBuilder(); bool first = !_residentVitals.Visible, hidden = false;
        string[] lines = plain.Replace("\r", "").Split('\n');
        for (int i = _residentVitals.Visible ? 2 : 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (_residentVitals.Visible && (line.StartsWith("生命 ") || line.StartsWith("精力 "))) { continue; }
            bool section = line is "身体与需求" or "随身物资" or "性格倾向" or "居所与关系" or "个人历史" or "上次决策 / 效用" or "聚落历史";
            if (section) { hidden = !_decisionDetails && (line is "性格倾向" or "上次决策 / 效用"); }
            if (hidden) { continue; }
            string escaped = EscapeMarkup(line);
            if (first && line.Length > 0) { result.AppendLine("[font_size=23][b]" + escaped + "[/b][/font_size]"); first = false; }
            else if (section) { result.AppendLine("[color=#bda572][b]" + escaped + "[/b][/color]"); }
            else { result.AppendLine(escaped); }
        }
        return result.ToString();
    }

    private void BuildInterface()
    {
        // The world occupies the whole window. No opaque rail or layout container resizes its viewport.
        _map = new WorldView3D { Game = this }; _map.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_map);
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(overlay);
        var header = Surface(overlay, Vector2.Zero, new Vector2(16, 16), new Vector2(1264, 60), 0);
        header.AnchorRight = 1; header.OffsetRight = -16;
        header.Visible = true;
        var worldCard = Surface(overlay, new Vector2(0, 0), new Vector2(28, 19), new Vector2(270, 54), 4);
        ClearSurface(worldCard);
        var worldRow = new HBoxContainer(); worldRow.AddThemeConstantOverride("separation", 12); worldCard.AddChild(worldRow);
        worldRow.AddChild(new TextureRect { Texture = HudSymbols.For("crest"), CustomMinimumSize = new Vector2(28, 38), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        var wordmark = HudStyle.Heading("河山", 26); worldRow.AddChild(wordmark);
        var worldInfo = new VBoxContainer(); worldInfo.AddThemeConstantOverride("separation", 0); worldRow.AddChild(worldInfo);
        worldInfo.AddChild(HudStyle.Label("自然 · 聚落 · 众生", 10, true));
        var scenario = new OptionButton { Flat = true, CustomMinimumSize = new Vector2(137, 31) }; _scenarioPicker = scenario; HudStyle.Button(scenario);
        foreach (string name in SandboxScenarios.Names) { scenario.AddItem(name); } worldInfo.AddChild(scenario);
        scenario.ItemSelected += index => { Scenario = (int)index; NewWorld((int)_seed.Value); _map.Focus(43, 50, 16); _checkpoint = ""; _discovery.Refresh(); };

        var stats = Surface(overlay, new Vector2(.5f, 0), new Vector2(-310, 20), new Vector2(620, 52), 4);
        ClearSurface(stats);
        var statsRow = new HBoxContainer(); statsRow.AddThemeConstantOverride("separation", 12); stats.AddChild(statsRow);
        _populationLabel = Metric(statsRow, "居民", "40", 20); _settlementLabel = Metric(statsRow, "聚落", "0", 20); _buildingLabel = Metric(statsRow, "建筑", "0", 20);
        _foodLabel = Metric(statsRow, "食物储备", "0", 20); _woodLabel = Metric(statsRow, "木材储备", "0", 20); _stoneLabel = Metric(statsRow, "石料储备", "0", 20);
        foreach (var resource in new[] { _foodLabel, _woodLabel, _stoneLabel })
            resource.TooltipText = "居民携带 + 地面物资堆 + 仓库；不包含尚未采集的地表资源，也不保证所有居民都可到达。";
        _summary = new Label { Visible = false }; overlay.AddChild(_summary);
        var topActions = Surface(overlay, new Vector2(1, 0), new Vector2(-238, 24), new Vector2(210, 44), 4);
        ClearSurface(topActions);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 6); topActions.AddChild(actions);
        _settingsButton = ActionButton(actions, "世界设置", () => ShowSettings(!_settingsOpen)); _settingsButton.ToggleMode = true;
        _journalButton = ActionButton(actions, "世界手记", () => ShowJournal(!_journalPanel.Visible)); _journalButton.ToggleMode = true;

        _journalPanel = Surface(overlay, new Vector2(1, 0), new Vector2(-372, 92), new Vector2(348, 600), 18);
        var journal = new VBoxContainer(); journal.AddThemeConstantOverride("separation", 12); _journalPanel.AddChild(journal);
        var journalHeader = new HBoxContainer(); journal.AddChild(journalHeader);
        var journalTitle = HudStyle.Heading("世界手记", 22); journalTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; journalHeader.AddChild(journalTitle);
        ActionButton(journalHeader, "关闭 ×", () => ShowJournal(false));
        _drawer = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; journal.AddChild(_drawer);
        _drawer.AddThemeStyleboxOverride("panel", HudStyle.Box(new Color(0, 0, 0, 0), 0, 0, false));
        _discovery = new DiscoveryPanel { Name = "现场", Game = this }; _drawer.AddChild(_discovery);
        var personPanel = new VBoxContainer { Name = "人物" }; personPanel.AddThemeConstantOverride("separation", 12); _drawer.AddChild(personPanel);
        BuildResidentVitals(personPanel);
        var detailControls = new HBoxContainer(); _residentControls = detailControls; personPanel.AddChild(detailControls);
        var details = ActionButton(detailControls, "显示决策与性格", () => { _decisionDetails = !_decisionDetails; RefreshPanels(); }); details.ToggleMode = true;
        _pinButton = ActionButton(detailControls, "关注这个居民", TogglePin);
        var nameControls = new HBoxContainer(); _residentNameControls = nameControls; personPanel.AddChild(nameControls);
        _residentName = new LineEdit { PlaceholderText = "给这个居民起个名字", MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nameControls.AddChild(_residentName); ActionButton(nameControls, "命名", RenameResident);
        _inspector = TextPanel("人物详情"); personPanel.AddChild(_inspector); _history = TextPanel("历史"); _drawer.AddChild(_history);
        _chart = new StatisticsView { Name = "曲线", Game = this }; _drawer.AddChild(_chart);
        BuildTrialPanel(); _drawer.SetTabHidden(4, true);
        BuildProjectPanel();

        _brushPanel = Surface(overlay, new Vector2(0, 0), new Vector2(24, 104), new Vector2(245, 230), 18);
        var brush = new VBoxContainer(); brush.AddThemeConstantOverride("separation", 10); _brushPanel.AddChild(brush);
        _brushTitle = HudStyle.Label("自由观察", 21); brush.AddChild(_brushTitle);
        _brushHint = HudStyle.Label("靠近一个人，看看他为什么\n做出自己的选择。", 14, true); _brushHint.AutowrapMode = TextServer.AutowrapMode.WordSmart; brush.AddChild(_brushHint);
        _radius = Spin(brush, "作用范围", 0, 20, 2); _strength = Spin(brush, "数量 / 强度", 1, 100, 10);
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
        tools.AddChild(new HSeparator()); tools.AddChild(HudStyle.Label("世界规则", 17));
        Rule(tools, "禁止死亡", () => Sim.Config.Rules.NoDeath, v => Sim.Config.Rules.NoDeath = v);
        Rule(tools, "高出生率", () => Sim.Config.Rules.HighBirthRate, v => Sim.Config.Rules.HighBirthRate = v);
        Rule(tools, "快速衰老", () => Sim.Config.Rules.FastAging, v => Sim.Config.Rules.FastAging = v);
        Rule(tools, "资源双倍恢复", () => Sim.Config.Rules.DoubleResource, v => Sim.Config.Rules.DoubleResource = v);
        Rule(tools, "和平模式", () => Sim.Config.Rules.PeaceMode, v => Sim.Config.Rules.PeaceMode = v);
        Rule(tools, "禁止聚落战争", () => Sim.Config.Rules.DisableWar, v => Sim.Config.Rules.DisableWar = v);
        Rule(tools, "自然住房布局", () => Sim.Config.Buildings.OrganicHousing, v => Sim.Config.Buildings.OrganicHousing = v);
        var weather = new HBoxContainer(); tools.AddChild(weather); ActionButton(weather, "降雨", () => TrialWeather(WeatherKind.Rain)); ActionButton(weather, "旱季", () => TrialWeather(WeatherKind.Drought));
        tools.AddChild(new HSeparator()); tools.AddChild(HudStyle.Label("观察图层", 17));
        var layer = new OptionButton(); foreach (string text in new[] { "自然地形", "聚落领土", "资源储量", "湿度", "肥力", "食物分布", "人口密度", "AI 状态", "行动路径" }) { layer.AddItem(text); }
        tools.AddChild(layer); layer.ItemSelected += i => _map.Overlay = (int)i;
        _birth = Spin(tools, "出生概率", 0, 1, Sim.Config.Birth.BaseChancePerDay, .005); _birth.ValueChanged += v => { Sim.Config.Birth.BaseChancePerDay = (float)v; Trial.MarkAssisted(); };
        _seed = Spin(tools, "随机种子", 0, int.MaxValue, Sim.World.Seed);
        ActionButton(tools, "重新生成世界", () => { NewWorld((int)_seed.Value); _checkpoint = ""; _map.Focus(43, 50, 16); });
        ActionButton(tools, "保存世界…", () => _save.PopupCenteredRatio(.65f)); ActionButton(tools, "载入世界…", () => _load.PopupCenteredRatio(.65f));
        ActionButton(tools, "记录实验起点", () => { _checkpoint = EncodeClientWorld(); _experimentLabel = $"第 {Sim.World.Calendar.Day} 天 · {Sim.Agents.LiveCount} 人"; _status.Text = "已记录 " + _experimentLabel; });
        ActionButton(tools, "回到实验起点", RestoreCheckpoint);

        var footer = Surface(overlay, new Vector2(0, 1), new Vector2(16, -100), new Vector2(1264, 84), 0);
        footer.AnchorRight = 1; footer.OffsetRight = -16;
        footer.Visible = false;
        var dock = Surface(overlay, new Vector2(.5f, 1), new Vector2(-250, -252), new Vector2(500, 160), 6); _dockPanel = dock;
        var dockContents = new VBoxContainer(); dockContents.AddThemeConstantOverride("separation", 4); dock.AddChild(dockContents);
        var categories = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; categories.AddThemeConstantOverride("separation", 6); dockContents.AddChild(categories);
        foreach (string category in new[] { "生命", "地貌", "资源", "灾害", "祝福" })
        { string chosen = category; var button = ActionButton(categories, category, () => SetCategory(chosen)); button.ToggleMode = true; _categoryButtons[category] = button; }
        _paletteRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; _paletteRow.AddThemeConstantOverride("separation", 6); dockContents.AddChild(_paletteRow); SetCategory("生命");
        _dockPanel.Visible = false;
        _commandPanel = Surface(overlay, new Vector2(.5f, 1), new Vector2(-250, -98), new Vector2(500, 80), 6);

        var commands = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; commands.AddThemeConstantOverride("separation", 6); _commandPanel.AddChild(commands);
        ActionButton(commands, "观察", () => { SelectTool(PlayerTool.Inspect); ShowTools(false); });
        _toolsMenuButton = ActionButton(commands, "创造", () => ShowTools(!_dockPanel.Visible)); _toolsMenuButton.ToggleMode = true;
        ActionButton(commands, "营造", () => { ShowTools(false); OpenConstruction(); });
        ActionButton(commands, "生态", () => { ShowTools(false); OpenProjects(); });
        ActionButton(commands, "手记", () => ShowJournal(!_journalPanel.Visible));
        foreach (Godot.Button command in commands.GetChildren())
        {
            command.Icon = HudSymbols.For(command.Text); command.ExpandIcon = true;
            command.AddThemeConstantOverride("icon_max_width", 27); command.AddThemeConstantOverride("h_separation", 8);
            command.CustomMinimumSize = new Vector2(88, 60);
        }

        var timePanel = Surface(overlay, new Vector2(0, 1), new Vector2(24, -90), new Vector2(320, 68), 8); _timePanel = timePanel;

        var timeBox = new VBoxContainer(); timeBox.AddThemeConstantOverride("separation", 4); timePanel.AddChild(timeBox); _dayLabel = HudStyle.Label("第 1 天", 14); timeBox.AddChild(_dayLabel);
        var timeRow = new HBoxContainer(); timeBox.AddChild(timeRow);
        foreach (int speed in new[] { 0, 1, 4, 8, 16, 32 })
        { int chosen = speed; var button = ActionButton(timeRow, speed == 0 ? "暂停" : speed + "×", () => SetSpeed(chosen)); button.ToggleMode = true; _speedButtons[speed] = button; } SetSpeed(_speed);
        var cameraPanel = Surface(overlay, new Vector2(1, 1), new Vector2(-320, -90), new Vector2(296, 68), 8); _cameraPanel = cameraPanel;

        var cameraBox = new VBoxContainer(); cameraBox.AddThemeConstantOverride("separation", 4); cameraPanel.AddChild(cameraBox); cameraBox.AddChild(HudStyle.Label("观察视角", 11, true));
        var views = new HBoxContainer(); cameraBox.AddChild(views); ActionButton(views, "全景", () => _map.Center());
        foreach (string label in new[] { "斜视", "俯视", "近景" }) { string view = label; ActionButton(views, label, () => ((WorldView3D)_map).SetPerspective(view)); }
        ActionButton(views, "跟随", () => { if (SelectedSlot >= 0) { _map.Follow(SelectedSlot); } else { _status.Text = "选择一个人物后，跟随他的故事"; } });
        _toolBadge = HudStyle.Label("自由观察", 13); HudStyle.Float(_toolBadge, new Vector2(.5f, 1), new Vector2(-200, -180), new Vector2(400, 26)); _toolBadge.HorizontalAlignment = HorizontalAlignment.Center; _toolBadge.AddThemeColorOverride("font_color", new Color("#f3efe3")); _toolBadge.AddThemeColorOverride("font_shadow_color", new Color("#17241e")); _toolBadge.AddThemeConstantOverride("shadow_offset_y", 1); overlay.AddChild(_toolBadge);
        _status = HudStyle.Label("观察模式：左键拖动平移 · 右键拖动旋转/俯仰 · 滚轮缩放 · 单击选择", 11, true); HudStyle.Float(_status, new Vector2(.5f, 1), new Vector2(-430, -126), new Vector2(860, 18)); _status.HorizontalAlignment = HorizontalAlignment.Center; _status.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f)); _status.AddThemeConstantOverride("shadow_offset_y", 1); overlay.AddChild(_status);
        BuildPlanningCard(overlay);
        _save = Dialog(FileDialog.FileModeEnum.SaveFile); _save.FileSelected += SaveWorld; _load = Dialog(FileDialog.FileModeEnum.OpenFile); _load.FileSelected += LoadWorld;
        Resized += FitHud; FitHud();
        RefreshPanels();
        _drawer.CurrentTab = 0;
        BuildWorldPulse(overlay);
        ShowJournal(false);
        if (_capture.Length > 0)
        {
            var captureShield = new Control { MouseFilter = MouseFilterEnum.Stop };
            captureShield.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); overlay.AddChild(captureShield);
        }
    }
    private void FitHud() => FitHudFor(Size);
    private void FitHudFor(Vector2 size)
    {
        HudStyle.Float(_timePanel, new Vector2(0, 1), new Vector2(24, -90), new Vector2(320, 68));
        HudStyle.Float(_cameraPanel, new Vector2(1, 1), new Vector2(-320, -90), new Vector2(296, 68));
        _journalPanel.OffsetBottom = 92 + Math.Min(600, size.Y - 214);
        _settingsPanel.OffsetBottom = Math.Min(size.Y - 280, 744);
        _toolBadge.OffsetTop = -149; _toolBadge.OffsetBottom = -129;
    }
    private void ValidateHudLayout()
    {
        try
        {
            foreach (Vector2 size in new[] { new Vector2(1280, 800), new Vector2(1600, 1000), new Vector2(1920, 1080) })
            {
                FitHudFor(size);
                // Anchors plus offsets are the authored rectangles, independent of one deferred layout pass.
                Rect2 Bounds(Control c) => new(new Vector2(size.X * c.AnchorLeft + c.OffsetLeft, size.Y * c.AnchorTop + c.OffsetTop),
                    new Vector2(c.OffsetRight - c.OffsetLeft, c.OffsetBottom - c.OffsetTop));
                Rect2 dock = Bounds(_dockPanel), time = Bounds(_timePanel), camera = Bounds(_cameraPanel);
                if (dock.Intersects(time) || dock.Intersects(camera) || time.Intersects(camera)) { throw new InvalidOperationException("HUD controls overlap at " + size); }
                foreach (var auxiliary in new[] { _brushPanel, _settingsPanel, _planPanel })
                    if (Bounds(auxiliary).Intersects(dock)) { throw new InvalidOperationException("Tool rail obscures an auxiliary panel at " + size); }
                foreach (var panel in new[] { _dockPanel, _commandPanel, _timePanel, _cameraPanel, _journalPanel, _settingsPanel })
                {
                    Rect2 bounds = Bounds(panel);
                    if (bounds.Position.X < 0 || bounds.Position.Y < 0 || bounds.End.X > size.X || bounds.End.Y > size.Y)
                        { throw new InvalidOperationException("HUD outside window at " + size); }
                }
            }
        }
        finally { FitHud(); }
    }
    private static PanelContainer Surface(Control parent, Vector2 anchor, Vector2 offset, Vector2 size, int padding = 16)
    {
        var panel = new PanelContainer { Material = HudStyle.FrameMaterial }; panel.AddThemeStyleboxOverride("panel", HudStyle.Frame(padding));
        HudStyle.Float(panel, anchor, offset, size); parent.AddChild(panel); panel.AddChild(new HudBevel { Margin = padding }); return panel;
    }
    private static void ClearSurface(PanelContainer panel)
    { panel.AddThemeStyleboxOverride("panel", HudStyle.Box(new Color(0, 0, 0, 0), 0, 8, false)); panel.GetChild<HudBevel>(0).Visible = false; }
    private void BuildResidentVitals(VBoxContainer parent)
    {
        _residentVitals = new VBoxContainer { Visible = false }; _residentVitals.AddThemeConstantOverride("separation", 8); parent.AddChild(_residentVitals);
        _residentTitle = HudStyle.Heading("", 28); _residentSubtitle = HudStyle.Label("", 12, true);
        var identity = new HBoxContainer(); identity.AddThemeConstantOverride("separation", 12); _residentVitals.AddChild(identity);
        _residentPortrait = new ResidentPortrait { Compact = true }; identity.AddChild(_residentPortrait);
        var bio = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; bio.AddThemeConstantOverride("separation", 10); identity.AddChild(bio);
        bio.AddChild(HudStyle.Label("居民档案", 11, true)); bio.AddChild(_residentTitle); bio.AddChild(_residentSubtitle);
        _residentTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart; _residentSubtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 8); _residentVitals.AddChild(grid);
        for (int i = 0; i < 4; i++)
        {
            var item = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; item.AddThemeConstantOverride("separation", 4); grid.AddChild(item);
            _vitalLabels[i] = HudStyle.Label("", 12); item.AddChild(_vitalLabels[i]);
            var bar = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(125, 5), MouseFilter = MouseFilterEnum.Ignore };
            bar.AddThemeStyleboxOverride("background", HudStyle.Box(HudStyle.Border, 1, 0, false));
            bar.AddThemeStyleboxOverride("fill", HudStyle.Box(HudStyle.Accent, 1, 0, false)); item.AddChild(bar); _vitalBars[i] = bar;
        }
        _residentVitals.AddChild(new HSeparator());
    }
    private void RefreshResidentVitals()
    {
        _vitalTransition?.Kill();
        int slot = SelectedSlot; _residentVitals.Visible = slot >= 0;
        _residentControls.Visible = _residentNameControls.Visible = _selectedPersonId != 0;
        if (slot < 0) { return; }
        var a = Sim.Agents;
        _residentPortrait.ShowResident(Sim.Society.Identity(slot), slot, a.LifeStageOf(slot) == SandBoxSim.Core.Agents.LifeStage.Child, a.JobOf(slot));
        _residentTitle.Text = a.NameOrOverride(slot);
        _residentSubtitle.Text = $"{a.AgeDaysOf(slot)} 天  /  {LifeStageName(a.LifeStageOf(slot))}  /  {JobName(a.JobOf(slot))}  ·  {a.PositionOf(slot)}";
        float[] values = { a.HealthOf(slot), a.HungerOf(slot), a.ThirstOf(slot), 1 - a.FatigueOf(slot) };
        string[] names = { "生命", "饥饿", "干渴", "精力" };
        _vitalTransition = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        for (int i = 0; i < values.Length; i++)
        {
            bool danger = i is 1 or 2 ? values[i] > .7f : values[i] < .35f;
            _vitalLabels[i].Text = names[i] + "   " + values[i].ToString("P0");
            _vitalTransition.TweenProperty(_vitalBars[i], "value", values[i], .24);
            ((StyleBoxFlat)_vitalBars[i].GetThemeStylebox("fill")).BgColor = danger ? new Color("#a84e32") : HudStyle.Accent;
        }
    }
    private static Label Metric(HBoxContainer row, string title, string value, int size)
    {
        var item = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; item.AddThemeConstantOverride("separation", 7); row.AddChild(item);
        item.AddChild(new TextureRect { Texture = HudSymbols.For(title), CustomMinimumSize = new Vector2(24, 30), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Modulate = HudStyle.Accent });
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); item.AddChild(stack); stack.AddChild(HudStyle.Label(title.Replace("储备", ""), 10, true));
        var metric = HudStyle.Label(value, size); stack.AddChild(metric); return metric;
    }
    private static Godot.Button ActionButton(Container parent, string label, Action action)
    { var button = new Godot.Button { Text = label }; HudStyle.Button(button); parent.AddChild(button); button.Pressed += action; return button; }
    private void SetSpeed(int speed) { _speed = speed; foreach (var pair in _speedButtons) { pair.Value.SetPressedNoSignal(pair.Key == speed); } }
    private void ShowTools(bool visible)
    { _dockPanel.Visible = visible; _toolsMenuButton.SetPressedNoSignal(visible); _toolBadge.Visible = !visible; _status.Visible = !visible; if (visible) { FadeIn(_dockPanel); } }
    private void ShowJournal(bool visible) { _journalPanel.Visible = visible; _journalButton.SetPressedNoSignal(visible); if (_pulseButton != null) { _pulseButton.Visible = !visible; } if (visible) { FadeIn(_journalPanel); } }
    private void ShowSettings(bool visible)
    { _settingsOpen = visible; _settingsButton.SetPressedNoSignal(visible); _settingsPanel.Visible = visible; _brushPanel.Visible = !visible && Tool != PlayerTool.Inspect; if (visible) { FadeIn(_settingsPanel); } }
    private readonly Dictionary<Control, Tween> _panelTransitions = new();
    private void FadeIn(Control panel)
    {
        if (_panelTransitions.TryGetValue(panel, out var previous)) { previous.Kill(); }
        panel.PivotOffset = new Vector2(panel.Size.X, 0); panel.Scale = Vector2.One * .985f; panel.Modulate = new Color(1, 1, 1, 0);
        var transition = panel.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        transition.TweenProperty(panel, "modulate:a", 1f, .22); transition.TweenProperty(panel, "scale", Vector2.One, .22); _panelTransitions[panel] = transition;
    }
    private void SetCategory(string category)
    {
        _category = category; foreach (var pair in _categoryButtons) { pair.Value.SetPressedNoSignal(pair.Key == category); }
        foreach (Node child in _paletteRow.GetChildren()) { _paletteRow.RemoveChild(child); child.QueueFree(); } _toolButtons.Clear();
        PlayerTool[] tools = category switch
        {
            "地貌" => new[] { PlayerTool.Grass, PlayerTool.River, PlayerTool.Mountain, PlayerTool.Sand, PlayerTool.Raise, PlayerTool.Lower, PlayerTool.RemoveWater },
            "资源" => new[] { PlayerTool.Forest, PlayerTool.Food, PlayerTool.Wood, PlayerTool.Stone, PlayerTool.Iron, PlayerTool.Fertility },
            "灾害" => new[] { PlayerTool.Fire, PlayerTool.Lightning, PlayerTool.Flood, PlayerTool.Drought, PlayerTool.Plague, PlayerTool.Meteor },
            "祝福" => new[] { PlayerTool.Heal, PlayerTool.Rain, PlayerTool.BirthBlessing, PlayerTool.Production, PlayerTool.Fertility },
            _ => new[] { PlayerTool.Inspect, PlayerTool.Human, PlayerTool.Animal, PlayerTool.Wolf, PlayerTool.Forest, PlayerTool.Food }
        };

        foreach (PlayerTool tool in tools)
        {
            var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 4); _paletteRow.AddChild(stack);
            var button = new Godot.Button { CustomMinimumSize = new Vector2(54, 42), ExpandIcon = true, ToggleMode = true, TooltipText = ToolNames[(int)tool] };
            button.AddThemeConstantOverride("icon_max_width", 26);
            HudStyle.Button(button);
            button.Icon = ToolGlyphs.For(tool);
            button.AddThemeColorOverride("icon_pressed_color", HudStyle.Accent);
            var chosen = tool; button.Pressed += () => SelectTool(chosen); stack.AddChild(button);
            var label = HudStyle.Label(ToolNames[(int)tool].Replace("创造", "").Replace("添加", "").Replace("生长", "").Replace(" / 选择", "").Replace("画笔", ""), 12, true); label.HorizontalAlignment = HorizontalAlignment.Center; stack.AddChild(label);
            _toolButtons[tool] = button; button.SetPressedNoSignal(Tool == tool);
        }
    }
    private void UpdateHudSelection(string hint)
    {
        foreach (var pair in _toolButtons) { pair.Value.SetPressedNoSignal(pair.Key == Tool); }
        _brushTitle.Text = ToolNames[(int)Tool]; _brushHint.Text = hint;
        _brushPanel.Visible = Tool != PlayerTool.Inspect && !_settingsOpen;
        if (Tool == PlayerTool.Inspect && _toolsMenuButton != null) { ShowTools(false); }
    }
}
