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
    private readonly Dictionary<PlayerTool, Godot.Button> _toolButtons = new();
    private readonly Dictionary<int, Godot.Button> _speedButtons = new();
    private readonly Dictionary<string, Godot.Button> _categoryButtons = new();
    private string _category = "生命";
    private bool _decisionDetails;

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
        var result = new System.Text.StringBuilder(); bool first = true, hidden = false;
        foreach (string line in plain.Replace("\r", "").Split('\n'))
        {
            bool section = line is "身体与需求" or "随身物资" or "性格倾向" or "居所与关系" or "个人历史" or "上次决策 / 效用" or "聚落历史";
            if (section) { hidden = !_decisionDetails && (line is "性格倾向" or "上次决策 / 效用"); }
            if (hidden) { continue; }
            string escaped = EscapeMarkup(line);
            if (first && line.Length > 0) { result.AppendLine("[font_size=23][b]" + escaped + "[/b][/font_size]"); first = false; }
            else if (section) { result.AppendLine("[color=#d8bb84][b]" + escaped + "[/b][/color]"); }
            else { result.AppendLine(escaped); }
        }
        return result.ToString();
    }

    private void BuildInterface()
    {
        // The world occupies the whole window. No opaque rail or layout container resizes its viewport.
        _map = new WorldView3D { Game = this }; _map.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_map);
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(overlay);
        var worldCard = Surface(overlay, new Vector2(0, 0), new Vector2(24, 24), new Vector2(245, 72));
        var worldRow = new HBoxContainer(); worldRow.AddThemeConstantOverride("separation", 18); worldCard.AddChild(worldRow);
        var wordmark = HudStyle.Label("河山", 27); worldRow.AddChild(wordmark);
        var worldInfo = new VBoxContainer(); worldInfo.AddThemeConstantOverride("separation", 0); worldRow.AddChild(worldInfo);
        worldInfo.AddChild(HudStyle.Label("你的世界", 11, true));
        var scenario = new OptionButton { Flat = true, CustomMinimumSize = new Vector2(137, 31) }; _scenarioPicker = scenario; HudStyle.Button(scenario);
        foreach (string name in SandboxScenarios.Names) { scenario.AddItem(name); } worldInfo.AddChild(scenario);
        scenario.ItemSelected += index => { Scenario = (int)index; NewWorld((int)_seed.Value); _map.Focus(43, 50, 16); _checkpoint = ""; _discovery.Refresh(); };

        var stats = Surface(overlay, new Vector2(.5f, 0), new Vector2(-237, 24), new Vector2(474, 72));
        var statsRow = new HBoxContainer(); statsRow.AddThemeConstantOverride("separation", 26); stats.AddChild(statsRow);
        _dayLabel = Metric(statsRow, "时光", "第 1 天", 22);
        _populationLabel = Metric(statsRow, "居民", "40", 24); _settlementLabel = Metric(statsRow, "聚落", "0", 24); _buildingLabel = Metric(statsRow, "建筑", "0", 24);
        _summary = new Label { Visible = false }; overlay.AddChild(_summary);
        var topActions = Surface(overlay, new Vector2(1, 0), new Vector2(-234, 24), new Vector2(210, 72), 8);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 6); topActions.AddChild(actions);
        _settingsButton = ActionButton(actions, "世界设置", () => ShowSettings(!_settingsOpen)); _settingsButton.ToggleMode = true;
        _journalButton = ActionButton(actions, "世界脉搏", () => ShowJournal(!_journalPanel.Visible)); _journalButton.ToggleMode = true; _journalButton.SetPressedNoSignal(true);

        _journalPanel = Surface(overlay, new Vector2(1, 0), new Vector2(-404, 116), new Vector2(380, 664), 20);
        var journal = new VBoxContainer(); journal.AddThemeConstantOverride("separation", 16); _journalPanel.AddChild(journal);
        var journalHeader = new HBoxContainer(); journal.AddChild(journalHeader);
        var journalTitle = HudStyle.Label("世界脉搏", 21); journalTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; journalHeader.AddChild(journalTitle);
        ActionButton(journalHeader, "收起", () => ShowJournal(false));
        _drawer = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; journal.AddChild(_drawer);
        _drawer.AddThemeStyleboxOverride("panel", HudStyle.Box(new Color(0, 0, 0, 0), 0, 0, false));
        _discovery = new DiscoveryPanel { Name = "现场", Game = this }; _drawer.AddChild(_discovery);
        var personPanel = new VBoxContainer { Name = "人物" }; personPanel.AddThemeConstantOverride("separation", 12); _drawer.AddChild(personPanel);
        var detailControls = new HBoxContainer(); personPanel.AddChild(detailControls);
        var details = ActionButton(detailControls, "显示决策与性格", () => { _decisionDetails = !_decisionDetails; RefreshPanels(); }); details.ToggleMode = true;
        _pinButton = ActionButton(detailControls, "关注这个居民", TogglePin);
        var nameControls = new HBoxContainer(); personPanel.AddChild(nameControls);
        _residentName = new LineEdit { PlaceholderText = "给这个居民起个名字", MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nameControls.AddChild(_residentName); ActionButton(nameControls, "命名", RenameResident);
        _inspector = TextPanel("人物详情"); personPanel.AddChild(_inspector); _history = TextPanel("历史"); _drawer.AddChild(_history);
        _chart = new StatisticsView { Name = "曲线", Game = this }; _drawer.AddChild(_chart);
        BuildTrialPanel();
        BuildProjectPanel();

        _brushPanel = Surface(overlay, new Vector2(0, 0), new Vector2(24, 116), new Vector2(245, 230), 20);
        var brush = new VBoxContainer(); brush.AddThemeConstantOverride("separation", 10); _brushPanel.AddChild(brush);
        _brushTitle = HudStyle.Label("自由观察", 21); brush.AddChild(_brushTitle);
        _brushHint = HudStyle.Label("靠近一个人，看看他为什么\n做出自己的选择。", 14, true); _brushHint.AutowrapMode = TextServer.AutowrapMode.WordSmart; brush.AddChild(_brushHint);
        _radius = Spin(brush, "作用范围", 0, 20, 2); _strength = Spin(brush, "数量 / 强度", 1, 100, 10);
        _brushPanel.Visible = false;

        _settingsPanel = Surface(overlay, new Vector2(0, 0), new Vector2(24, 116), new Vector2(300, 640), 20); _settingsPanel.Visible = false;
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

        var dock = Surface(overlay, new Vector2(.5f, 1), new Vector2(-365, -154), new Vector2(730, 126), 12); _dockPanel = dock;
        var dockContents = new VBoxContainer(); dockContents.AddThemeConstantOverride("separation", 8); dock.AddChild(dockContents);
        var categories = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; categories.AddThemeConstantOverride("separation", 12); dockContents.AddChild(categories);
        foreach (string category in new[] { "生命", "地貌", "资源", "灾害", "祝福" })
        { string chosen = category; var button = ActionButton(categories, category, () => SetCategory(chosen)); button.ToggleMode = true; _categoryButtons[category] = button; }
        _paletteRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; _paletteRow.AddThemeConstantOverride("separation", 10); dockContents.AddChild(_paletteRow); SetCategory("生命");

        var timePanel = Surface(overlay, new Vector2(0, 1), new Vector2(24, -104), new Vector2(292, 76), 10); _timePanel = timePanel;
        var timeBox = new VBoxContainer(); timeBox.AddThemeConstantOverride("separation", 4); timePanel.AddChild(timeBox); timeBox.AddChild(HudStyle.Label("时间流速", 11, true));
        var timeRow = new HBoxContainer(); timeBox.AddChild(timeRow);
        foreach (int speed in new[] { 0, 1, 4, 8, 16, 32 })
        { int chosen = speed; var button = ActionButton(timeRow, speed == 0 ? "暂停" : speed + "×", () => SetSpeed(chosen)); button.ToggleMode = true; _speedButtons[speed] = button; } SetSpeed(_speed);
        var cameraPanel = Surface(overlay, new Vector2(1, 1), new Vector2(-316, -104), new Vector2(292, 76), 10); _cameraPanel = cameraPanel;
        var cameraBox = new VBoxContainer(); cameraBox.AddThemeConstantOverride("separation", 4); cameraPanel.AddChild(cameraBox); cameraBox.AddChild(HudStyle.Label("观察视角", 11, true));
        var views = new HBoxContainer(); cameraBox.AddChild(views); ActionButton(views, "全景", () => _map.Center());
        foreach (string label in new[] { "斜视", "俯视", "近景" }) { string view = label; ActionButton(views, label, () => ((WorldView3D)_map).SetPerspective(view)); }
        ActionButton(views, "跟随", () => { if (SelectedSlot >= 0) { _map.Follow(SelectedSlot); } else { _status.Text = "选择一个人物后，跟随他的故事"; } });
        _toolBadge = HudStyle.Label("自由观察", 13); HudStyle.Float(_toolBadge, new Vector2(.5f, 1), new Vector2(-200, -192), new Vector2(400, 26)); _toolBadge.HorizontalAlignment = HorizontalAlignment.Center; overlay.AddChild(_toolBadge);
        _status = HudStyle.Label("右键旋转  ·  滚轮缩放  ·  中键平移  ·  点击人物查看细节", 12, true); HudStyle.Float(_status, new Vector2(0, 1), new Vector2(24, -24), new Vector2(1000, 20)); overlay.AddChild(_status);
        BuildPlanningCard(overlay);
        _save = Dialog(FileDialog.FileModeEnum.SaveFile); _save.FileSelected += SaveWorld; _load = Dialog(FileDialog.FileModeEnum.OpenFile); _load.FileSelected += LoadWorld;
        Resized += FitHud; FitHud();
        RefreshPanels();
        _drawer.CurrentTab = 0;
    }
    private void FitHud() => FitHudFor(Size);
    private void FitHudFor(Vector2 size)
    {
        bool compact = size.X < 1380;
        HudStyle.Float(_timePanel, compact ? new Vector2(.5f, 1) : new Vector2(0, 1), compact ? new Vector2(-365, -244) : new Vector2(24, -104), new Vector2(292, 76));
        HudStyle.Float(_cameraPanel, compact ? new Vector2(.5f, 1) : new Vector2(1, 1), compact ? new Vector2(73, -244) : new Vector2(-316, -104), new Vector2(292, 76));
        _journalPanel.OffsetBottom = 116 + Math.Clamp(size.Y - (compact ? 390 : 230), 260, 664);
        _settingsPanel.OffsetBottom = 116 + Math.Clamp(size.Y - (compact ? 390 : 230), 260, 640);
        _toolBadge.OffsetTop = compact ? -285 : -192; _toolBadge.OffsetBottom = _toolBadge.OffsetTop + 26;
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
                foreach (var panel in new[] { _dockPanel, _timePanel, _cameraPanel, _journalPanel, _settingsPanel })
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
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", HudStyle.Box(HudStyle.Surface, 14, padding)); HudStyle.Float(panel, anchor, offset, size); parent.AddChild(panel); return panel;
    }
    private static Label Metric(HBoxContainer row, string title, string value, int size)
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; stack.AddThemeConstantOverride("separation", 2); row.AddChild(stack); stack.AddChild(HudStyle.Label(title, 11, true));
        var metric = HudStyle.Label(value, size); stack.AddChild(metric); return metric;
    }
    private static Godot.Button ActionButton(BoxContainer parent, string label, Action action)
    { var button = new Godot.Button { Text = label }; HudStyle.Button(button); parent.AddChild(button); button.Pressed += action; return button; }
    private void SetSpeed(int speed) { _speed = speed; foreach (var pair in _speedButtons) { pair.Value.SetPressedNoSignal(pair.Key == speed); } }
    private void ShowJournal(bool visible) { _journalPanel.Visible = visible; _journalButton.SetPressedNoSignal(visible); if (visible) { FadeIn(_journalPanel); } }
    private void ShowSettings(bool visible)
    { _settingsOpen = visible; _settingsButton.SetPressedNoSignal(visible); _settingsPanel.Visible = visible; _brushPanel.Visible = !visible && Tool != PlayerTool.Inspect; if (visible) { FadeIn(_settingsPanel); } }
    private void FadeIn(Control panel) { panel.Modulate = new Color(1, 1, 1, 0); CreateTween().TweenProperty(panel, "modulate:a", 1f, .18); }
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
            var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); _paletteRow.AddChild(stack);
            var button = new Godot.Button { CustomMinimumSize = new Vector2(76, 48), ExpandIcon = true, ToggleMode = true, TooltipText = ToolNames[(int)tool] };
            button.AddThemeConstantOverride("icon_max_width", 40);
            HudStyle.Button(button);
            button.Icon = ToolGlyphs.For(tool);
            button.AddThemeColorOverride("icon_pressed_color", HudStyle.Ink);
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
    }
}
