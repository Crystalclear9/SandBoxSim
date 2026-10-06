using System;
using System.Linq;
using System.Text;
using Godot;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Save;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

/// <summary>表现层只推进内核和发送干预；检查器只读快照。</summary>
public partial class MainGame : Control
{
    public Simulation Sim { get; private set; } = null!;
    public bool VisualPaused => _speed == 0;
    public PlayerTool Tool { get; private set; }
    public int Scenario { get; private set; }
    public WildPlaces Wild { get; private set; } = new();
    public int SelectedX => _selectedX;
    public int SelectedY => _selectedY;
    public long PinnedPerson { get; private set; }
    public int Radius => (int)_radius.Value;
    public float Strength => (float)_strength.Value;
    public int SelectedSlot => _selected >= 0 && Sim.Agents.IsSlotAlive(_selected)
        && Sim.Agents.GenerationOf(_selected) == _selectedGeneration ? _selected : -1;
    private MapView _map = null!;
    private StatisticsView _chart = null!;
    private DiscoveryPanel _discovery = null!;
    private OptionButton _toolPicker = null!;
    private Label _toolDescription = null!;
    private string _checkpoint = "";
    private string _experimentLabel = "";
    private Label _summary = null!, _status = null!;
    private RichTextLabel _inspector = null!, _history = null!;
    private SpinBox _radius = null!, _strength = null!, _seed = null!;
    private SpinBox _birth = null!;
    private readonly System.Collections.Generic.List<(CheckBox Control, Func<bool> Read)> _rules = new();
    private FileDialog _save = null!, _load = null!;
    private double _pending, _refresh;
    private int _speed = 4, _selected = -1, _selectedGeneration;
    private long _selectedPersonId;
    private int _selectedAnimal = -1, _selectedAnimalGeneration, _selectedWolf = -1;
    private int _selectedX = 50, _selectedY = 50;
    private bool _selfTest;
    private int _frames;
    private string _capture = "", _captureFrames = "";
    private int _captureFrame;
    private double _captureElapsed;
    private bool _captureRequested;
    private int _initialPopulation = 40;
    private int _previewDays;
    private string _previewView = "";
    private string _previewPanel = "";
    private double _benchmarkSeconds, _benchmarkElapsed, _benchmarkMeasuredSeconds;
    private long _benchmarkFrames;
    private string _benchmarkOutput = "";
    private static readonly string[] ToolNames =
    {
        "观察 / 选择", "创造人类", "创造食草动物", "生长森林", "添加食物", "添加木材", "添加石料", "添加铁矿",
        "草地", "水域", "山脉", "沙地", "农田", "道路", "雪地", "沼泽", "沙漠", "熔岩",
        "升高地形", "降低地形", "河流画笔", "移除水域", "肥力祝福", "生育祝福", "治愈祝福", "生产祝福",
        "火灾", "闪电", "洪水", "干旱", "瘟疫", "陨石", "创造狼", "局部降雨"
    };

    public override void _Ready()
    {
        Theme = new Theme { DefaultFontSize = 16, DefaultFont = new SystemFont
            { FontNames = new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "WenQuanYi Zen Hei", "sans-serif" } } };
        var preferences = new ConfigFile();
        HudStyle.MotionEnabled = preferences.Load("user://interface.cfg") != Error.Ok || preferences.GetValue("interface", "motion", true).AsBool();
        ApplyVisualTheme();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--self-test") { _selfTest = true; }
            if(arg.StartsWith("--capture-frames=",StringComparison.Ordinal)){_captureFrames=arg.Substring(17);System.IO.Directory.CreateDirectory(_captureFrames);}
            if (arg.StartsWith("--capture=", StringComparison.Ordinal)) { _capture = arg.Substring(10); }
            if (arg.StartsWith("--agents=", StringComparison.Ordinal) && int.TryParse(arg.Substring(9), out int population))
                { _initialPopulation = Math.Clamp(population, 0, 2000); }
            if (arg.StartsWith("--benchmark-seconds=", StringComparison.Ordinal))
                { double.TryParse(arg.Substring(20), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _benchmarkSeconds); }
            if (arg.StartsWith("--benchmark-output=", StringComparison.Ordinal)) { _benchmarkOutput = arg.Substring(19); }
            if (arg.StartsWith("--demo-days=", StringComparison.Ordinal)) { int.TryParse(arg.Substring(12), out _previewDays); _previewDays = Math.Clamp(_previewDays, 0, 30); }
            if (arg.StartsWith("--view=", StringComparison.Ordinal)) { _previewView = arg.Substring(7); }
            if (arg.StartsWith("--panel=", StringComparison.Ordinal)) { _previewPanel = arg.Substring(8); }
        }
        NewWorld(839102);
        if (_previewDays > 0) { AdvanceWorld(_previewDays * Sim.Config.Clock.TicksPerDay); }
        BuildInterface();
        if (_previewPanel is "architecture" or "characters" or "naturemodels" or "faces" or "equipment" or "actions" or "motion")
        { SetSpeed(0); AddChild(new ModelGallery { Collection = _previewPanel == "naturemodels" ? "nature" : _previewPanel }); }

        if (_selfTest) { RunSelfTest(); }
    }
    private void NewWorld(int seed)
    {
        Sim = SandboxScenarios.Create(Scenario, seed, _initialPopulation);
        Sim.Config.Buildings.OrganicHousing = true;
        Wild = WildPlaces.Create(Sim);
        PinnedPerson = 0;
        SubscribeVisualEvents();
        _pending = 0; _selected = -1; _selectedPersonId = 0; _selectedAnimal = -1; _selectedWolf = -1;
        SyncControls();
    }
    private static SpinBox Spin(VBoxContainer parent, string label, double min, double max, double value, double step = 1)
    {
        if (label.Length > 0) { parent.AddChild(new Label { Text = label }); }
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value };
        parent.AddChild(spin); return spin;
    }
    private void Rule(VBoxContainer parent, string label, Func<bool> get, Action<bool> set)
    {
        var check = new CheckBox { Text = label, ButtonPressed = get() }; HudStyle.Rule(check); parent.AddChild(check);
        _rules.Add((check, get));
        check.Toggled += value => { set(value); Sim.InterveneRecordAuxiliary(label + " = " + value); };
    }
    private void SyncControls()
    {
        foreach (var rule in _rules) { rule.Control.SetPressedNoSignal(rule.Read()); }
        if (_birth != null) { _birth.SetValueNoSignal(Sim.Config.Birth.BaseChancePerDay); }
        if (_seed != null) { _seed.SetValueNoSignal(Sim.World.Seed); }
    }
    private static void Button(BoxContainer parent, string label, Action action)
    { var button = new Godot.Button { Text = label }; parent.AddChild(button); button.Pressed += action; }
    public void SelectTool(PlayerTool tool)
    {
        Tool = tool; _toolPicker.Select((int)tool);
        string hint = tool switch
        {
            PlayerTool.River or PlayerTool.Water => "拖动画出水源；河流能灌溉，也可能隔断道路。",
            PlayerTool.Forest => "森林提供木材和栖息地；观察采伐与生态恢复。",
            PlayerTool.Human => "撒下居民，他们会自己寻找食物、建造与定居。",
            PlayerTool.Wolf => "加入捕食者，观察鹿群与植被的连锁变化。",
            PlayerTool.Fire => "火会蔓延；水源、天气与燃料决定后果。",
            PlayerTool.Food => "增加本地粮食，观察生存、出生与贸易需求。",
            PlayerTool.Rain => "增加局部土壤湿度；强度至少 25 时扑灭范围内的火，保留未烧尽的植被。",
            PlayerTool.Grass or PlayerTool.Road => "改变通行条件，让分隔的居民有机会相遇。",
            PlayerTool.Inspect => "点击人物、动物或聚落，追踪他们的故事。",
            _ => "在地图点击或拖动施加干预；右键 / Esc 返回观察。"
        };
        _toolDescription.Text = hint; _status.Text = ToolNames[(int)tool] + " · " + hint;
        _toolBadge.Text = ToolNames[(int)tool] + (tool == PlayerTool.Inspect ? "" : $"  ·  半径 {Radius}");
        UpdateHudSelection(hint);
    }
    public void FocusStory(long person, int x, int y)
    {
        SelectTool(PlayerTool.Inspect);
        var p = Sim.Society.Find(person);
        if (p != null)
        {
            _selectedPersonId = person; _selected = p.Alive ? p.Slot : -1; _selectedGeneration = p.Generation;
            if (p.Alive) { x = Sim.Agents.XOf(p.Slot); y = Sim.Agents.YOf(p.Slot); }
        }
        else { _selected = -1; _selectedPersonId = 0; }
        _selectedAnimal = -1; _selectedWolf = -1;
        if (Sim.World.IsInBounds(x, y)) { _selectedX = x; _selectedY = y; _map.Focus(x, y, 20); }
        ShowJournal(true); _drawer.CurrentTab = 1;
        RefreshPanels();
    }
    private ulong _lastStoryPulse;
    private void SubscribeVisualEvents()
    {
        Sim.Events.Recorded += ev =>
        {
            if (_map == null || !ev.HasLocation) { return; }
            ulong now = Time.GetTicksMsec(); if (now - _lastStoryPulse < 700) { return; }
            if (ev.Type is SandBoxSim.Core.History.WorldEventType.BuildingCompleted or SandBoxSim.Core.History.WorldEventType.AgentBorn
                or SandBoxSim.Core.History.WorldEventType.SettlementFounded or SandBoxSim.Core.History.WorldEventType.Innovation)
                { _lastStoryPulse = now; _map.Effect(ev.Location.X, ev.Location.Y, new Color("#ddcc99"), ev.Type == SandBoxSim.Core.History.WorldEventType.AgentBorn ? "新生命" : "新的故事"); }
        };
    }
    private void RestoreCheckpoint()
    {
        if (_checkpoint.Length == 0) { _status.Text = "先记下一个实验起点，再尝试不同干预"; return; }
        try
        {
            var root = JsonParser.Parse(_checkpoint); var config = new SimConfig();
            var warnings = new System.Collections.Generic.List<string>(); JsonBinder.Bind(root.Get("config"), config, warnings, "");
            if (warnings.Count > 0) { throw new InvalidOperationException(string.Join("; ", warnings)); }
            var restored = Simulation.CreateForRestore(config, root.GetInt("width"), root.GetInt("height"), root.GetInt("seed"));
            var result = SaveLoader.Load(restored, _checkpoint);
            if (!result.Success || !result.DigestMatches) { throw new InvalidOperationException(result.Error); }
            RestoreClientContext(root);
            Sim = restored; SubscribeVisualEvents(); _pending = 0; _selected = -1; _selectedPersonId = 0; _selectedAnimal = -1; _selectedWolf = -1;
            _map.Focus(46, 50, 13); SyncControls(); _status.Text = "回到 " + _experimentLabel + "；试试另一种选择"; RefreshPanels(); _discovery.Refresh();
        }
        catch (Exception ex) { _status.Text = "回溯失败：" + ex.Message; GD.PushError(ex.ToString()); }
    }
    private void ApplyVisualTheme()
    {
        Theme.SetColor("font_color", "Label", HudStyle.Text);
        Theme.SetColor("default_color", "RichTextLabel", HudStyle.Text);
        Theme.SetColor("font_color", "Button", HudStyle.Text);
        Theme.SetColor("font_hover_color", "Button", HudStyle.Accent);
        foreach (string type in new[] { "CheckBox", "CheckButton", "OptionButton" })
        {
            Theme.SetColor("font_color", type, HudStyle.Text);
            Theme.SetColor("font_hover_color", type, HudStyle.Accent);
            Theme.SetColor("font_pressed_color", type, HudStyle.Text);
        }
        Theme.SetColor("font_color", "LineEdit", HudStyle.Text);
        Theme.SetColor("font_placeholder_color", "LineEdit", HudStyle.Muted);
        Theme.SetColor("caret_color", "LineEdit", HudStyle.Text);
        Theme.SetStylebox("panel", "PopupMenu", HudStyle.Box(HudStyle.Surface, 4, 12));
        Theme.SetColor("font_color", "PopupMenu", HudStyle.Text);
        Theme.SetColor("font_hover_color", "PopupMenu", HudStyle.Surface);
        Theme.SetStylebox("hover", "PopupMenu", HudStyle.Box(HudStyle.Accent, 2, 6, false));
        Theme.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = HudStyle.Border, Thickness = 1 });
        foreach (string type in new[] { "Button", "OptionButton", "LineEdit" })
            foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            {
                var box = new StyleBoxFlat { BgColor = state == "hover" ? HudStyle.Wash : HudStyle.Surface,
                    BorderColor = state == "focus" ? HudStyle.Accent : HudStyle.Border, BorderWidthBottom = 1, BorderWidthTop = 1,
                    BorderWidthLeft = 1, BorderWidthRight = 1, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                    CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, ContentMarginLeft = 10, ContentMarginRight = 10,
                    ContentMarginTop = 6, ContentMarginBottom = 6 };
                Theme.SetStylebox(state, type, box);
            }
        Theme.SetStylebox("panel", "TabContainer", new StyleBoxFlat { BgColor = HudStyle.Surface, ContentMarginLeft = 14,
            ContentMarginRight = 14, ContentMarginTop = 14, ContentMarginBottom = 14, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        Theme.SetColor("font_selected_color", "TabContainer", HudStyle.Accent);
        Theme.SetColor("font_unselected_color", "TabContainer", HudStyle.Muted);
        Theme.SetFontSize("font_size", "TabContainer", 13);
        var selected = HudStyle.Box(new Color(0, 0, 0, 0), 0, 8, false); selected.BorderWidthBottom = 2; selected.BorderColor = HudStyle.Accent;
        Theme.SetStylebox("tab_selected", "TabContainer", selected);
        Theme.SetStylebox("tab_unselected", "TabContainer", HudStyle.Box(new Color(0, 0, 0, 0), 0, 8, false));
        Theme.SetStylebox("tab_hovered", "TabContainer", HudStyle.Box(HudStyle.Wash, 2, 8, false));
        Theme.SetFontSize("normal_font_size", "RichTextLabel", 14); Theme.SetConstant("line_separation", "RichTextLabel", 5);
        Theme.SetColor("default_color", "RichTextLabel", HudStyle.Text);
        Theme.SetColor("font_color", "TooltipLabel", HudStyle.Text);
        var tooltip = HudStyle.Box(new Color("#202721"), 3, 12);
        tooltip.BorderColor = new Color(HudStyle.Accent, .5f);
        Theme.SetStylebox("panel", "TooltipPanel", tooltip);
        Theme.SetFontSize("font_size", "TooltipLabel", 13);
        Theme.SetConstant("outline_size", "TooltipLabel", 0);
        Theme.SetColor("selection_color", "LineEdit", new Color(HudStyle.Accent, .28f));
        Theme.SetStylebox("read_only", "LineEdit", HudStyle.Box(HudStyle.Ink, 3, 10));
        Theme.SetStylebox("scroll", "VScrollBar", HudStyle.Box(new Color(0, 0, 0, .18f), 2, 3, false));
        Theme.SetStylebox("grabber_pressed", "VScrollBar", HudStyle.Box(HudStyle.Accent.Lightened(.12f), 3, 3, false));
        Theme.SetStylebox("grabber", "VScrollBar", HudStyle.Box(new Color(HudStyle.Muted, .35f), 3, 3, false));
        Theme.SetStylebox("grabber_highlight", "VScrollBar", HudStyle.Box(HudStyle.Accent, 3, 3, false));
    }
    private static RichTextLabel TextPanel(string name) => new() { Name = name, BbcodeEnabled = true,
        ScrollActive = true, SelectionEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill };
    private FileDialog Dialog(FileDialog.FileModeEnum mode)
    {
        var dialog = new FileDialog { FileMode = mode, Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.json ; SandBoxSim 世界" }, CurrentFile = "world.json" };
        AddChild(dialog); return dialog;
    }
    private void SaveWorld(string path)
    {
        try
        {
            string temporary = path + ".tmp";
            System.IO.File.WriteAllText(temporary, EncodeClientWorld(true), Encoding.UTF8);
            System.IO.File.Move(temporary, path, true);
            _status.Text = "已保存 " + path;
        }
        catch (Exception ex) { _status.Text = "保存失败：" + ex.Message; GD.PushError(ex.Message); }
    }
    private void LoadWorld(string path)
    {
        try
        {
            string json = System.IO.File.ReadAllText(path);
            var root = JsonParser.Parse(json); var config = new SimConfig();
            var warnings = new System.Collections.Generic.List<string>();
            JsonBinder.Bind(root.Get("config"), config, warnings, "");
            if (warnings.Count > 0) { throw new InvalidOperationException(string.Join("; ", warnings)); }
            var restored = Simulation.CreateForRestore(config, root.GetInt("width"), root.GetInt("height"), root.GetInt("seed"));
            var result = SaveLoader.Load(restored, json);
            if (!result.Success || !result.DigestMatches) { throw new InvalidOperationException(result.Error + " " + result.SegmentDifference); }
            RestoreClientContext(root, true);
            Sim = restored; SubscribeVisualEvents(); _selected = -1; _selectedPersonId = 0; _selectedAnimal = -1; _selectedWolf = -1; _pending = 0; _map.Center(); SyncControls();
            _status.Text = "已恢复第 " + result.Day + " 天"; RefreshPanels(); _discovery.Refresh();
        }
        catch (Exception ex) { _status.Text = "载入失败：" + ex.Message; GD.PushError(ex.Message); }
    }
    private string EncodeClientWorld(bool includeCheckpoint = false)
    {
        var client = JsonValue.Object().Set("scenario", JsonValue.From(Scenario))
            .Set("pin", JsonValue.From(PinnedPerson.ToString(System.Globalization.CultureInfo.InvariantCulture))).Set("wild", Wild.Encode());
        if (includeCheckpoint) { client.Set("checkpoint", JsonValue.From(_checkpoint)).Set("experiment", JsonValue.From(_experimentLabel)); }
        return JsonParser.Parse(SaveFile.Encode(Sim)).Set("client", client).ToJson(true);
    }
    private void RestoreClientContext(JsonValue root, bool restoreCheckpoint = false)
    {
        Scenario = Math.Clamp(root.Get("client").GetInt("scenario", Scenario), 0, SandboxScenarios.Names.Length - 1);
        Wild = WildPlaces.Decode(root.Get("client").Get("wild"));
        long.TryParse(root.Get("client").GetString("pin"), out long pin); PinnedPerson = pin;
        if (restoreCheckpoint)
        {
            _checkpoint = root.Get("client").GetString("checkpoint", "");
            _experimentLabel = root.Get("client").GetString("experiment", "实验起点");
        }
        _scenarioPicker.Select(Scenario);
    }
    public override void _Process(double delta)
    {
        _frames++;
        if (_frames == 3)
        {
            _map.Focus(43, 50, 16);
            if (_previewView.Length > 0) { ((WorldView3D)_map).SetPerspective(_previewView == "near" ? "近景" : _previewView == "top" ? "俯视" : "斜视"); }
            if (_previewPanel == "chart") { ShowJournal(true); _drawer.CurrentTab = 3; }
            if (_previewPanel == "settings") { ShowSettings(true); }
            if (_previewPanel is "wild" or "meadow" or "wetland" or "fallenwood")
            {
                var place = Wild.Places.Where(p => p.Kind == (_previewPanel=="meadow" ? WildPlaceKind.Meadow : _previewPanel=="wetland" ? WildPlaceKind.Wetland : _previewPanel=="fallenwood" ? WildPlaceKind.FallenWood : WildPlaceKind.Ruins)).OrderBy(p => (p.X-50)*(p.X-50)+(p.Y-50)*(p.Y-50)).FirstOrDefault();
                if (place != null) { ClickTile(place.X, place.Y); _map.Focus(place.X, place.Y, _previewPanel=="wild" ? 30 : 48); SetSpeed(0); }
            }
            if (_previewPanel == "village") { _map.Focus(43, 50, 38); SetSpeed(0); }
            if (_previewPanel=="terrain") { SetCategory("地貌");ShowTools(true); }
            if (_previewPanel == "field") { ShowJournal(true); _drawer.CurrentTab = 0; }
            if (_previewPanel == "tools") { SetCategory("地貌"); ShowTools(true); }
            if (_previewPanel == "brush") { SetCategory("地貌"); SelectTool(PlayerTool.River); }
            if (_previewPanel == "person")
            {
                _selected = Sim.Agents.AliveSlots().FirstOrDefault(-1);
                if (_selected >= 0)
                {
                    _selectedGeneration = Sim.Agents.GenerationOf(_selected); _selectedPersonId = Sim.Society.Identity(_selected);
                    var point = Sim.Agents.PositionOf(_selected); _map.Focus(point.X, point.Y, 30);
                    ShowJournal(true); _drawer.CurrentTab = 1;
                }
            }
            if (_selfTest) { SelectTool(PlayerTool.Inspect); _drawer.CurrentTab = 0; }
        }
        if (!_selfTest && _speed > 0)
        {
            _pending += delta * Sim.Config.Clock.TicksPerSecondAt1x * _speed;
            int ticks = Math.Min((int)_pending, Sim.Config.Clock.MaxCatchUpTicksPerFrame);
            AdvanceWorld(ticks); _pending -= ticks;
        }
        _refresh += delta;
        _map.QueueRedraw();
        if (_refresh >= 0.2) { _refresh = 0; RefreshPanels(); RefreshOperations(); SyncControls(); _chart.QueueRedraw(); _discovery.Refresh(); }
        _captureElapsed += delta;
        if(_captureFrames.Length>0&&_captureFrame<56&&_captureElapsed>=.25+_captureFrame/8.0)CaptureFrame(_captureFrame++);
        if (_capture.Length > 0 && !_captureRequested && _captureElapsed >= .7) { _captureRequested = true; Capture(); }
        if (_selfTest && _frames >= 15) { GetTree().Quit(0); }
        if (_benchmarkSeconds > 0)
        {
            _benchmarkElapsed += delta;
            if (_benchmarkElapsed > 5) { _benchmarkMeasuredSeconds += delta; _benchmarkFrames++; }
            if (_benchmarkElapsed >= _benchmarkSeconds)
            {
                double fps = _benchmarkFrames / Math.Max(0.001, _benchmarkMeasuredSeconds);
                var result = JsonValue.Object().Set("seconds", JsonValue.From(_benchmarkElapsed))
                    .Set("averageFPS", JsonValue.From(fps)).Set("initialPopulation", JsonValue.From(_initialPopulation))
                    .Set("population", JsonValue.From(Sim.Agents.LiveCount)).Set("tick", JsonValue.From(Sim.Clock))
                    .Set("buildings", JsonValue.From(Sim.Buildings.TotalCompleted)).Set("events", JsonValue.From(Sim.Events.TotalRecorded))
                    .Set("births", JsonValue.From(Sim.Stats.TotalBirths)).Set("deaths", JsonValue.From(Sim.Stats.TotalDeaths));
                if (_benchmarkOutput.Length > 0) { System.IO.File.WriteAllText(_benchmarkOutput, result.ToJson(true)); }
                GD.Print("GODOT_BENCHMARK " + result.ToJson(false)); GetTree().Quit(fps >= 30 ? 0 : 1);
                _benchmarkSeconds = 0;
            }
        }
    }
    private async void CaptureFrame(int frame)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_captureFrames,$"frame-{frame:D3}.png"));
    }
    private async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var screenshot = GetViewport().GetTexture().GetImage();
        screenshot.SavePng(_capture); GD.Print("CAPTURE " + _capture);
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Space) { SetSpeed(_speed == 0 ? 1 : 0); }
            if (key.Keycode == Key.F) { _map.Center(); }
            if (key.Keycode == Key.Escape) { SelectTool(PlayerTool.Inspect); }
        }
    }
    public void ClickTile(int x, int y)
    {
        if (!Sim.World.IsInBounds(x, y)) { return; }
        _selectedX = x; _selectedY = y;
        if (Tool != PlayerTool.Inspect)
        {
            int before = Sim.Agents.LiveCount;
            PlayerTools.Apply(Sim, Tool, x, y, Radius, Strength); _map.QueueRedraw();
            _map.Effect(x, y, Tool >= PlayerTool.Fire && Tool <= PlayerTool.Meteor ? new Color("#e59970") : new Color("#c5dda0"), ToolNames[(int)Tool]);
            _status.Text = $"{ToolNames[(int)Tool]} · ({x}, {y}) · 半径 {Radius}" + (Tool == PlayerTool.Human ? $" · 新增 {Sim.Agents.LiveCount - before} 人" : " · 观察接下来的变化");
        }
        else
        {
            _selected = -1; _selectedAnimal = -1; _selectedWolf = -1;
            var localPlace = Wild.At(x,y);
            int best = localPlace != null && (localPlace.X-x)*(localPlace.X-x)+(localPlace.Y-y)*(localPlace.Y-y) <= 4 ? 0 : 10;
            foreach (int slot in Sim.Agents.AliveSlots())
            {
                int distance = Int2.SquaredDistance(new Int2(x, y), Sim.Agents.PositionOf(slot));
                if (distance < best) { _selected = slot; best = distance; }
            }
            foreach (int animal in Sim.Wildlife.AliveIndices())
            {
                int distance = Int2.SquaredDistance(new Int2(x, y), Sim.Wildlife.PositionOf(animal));
                if (distance < best) { _selected = -1; _selectedAnimal = animal; best = distance; _selectedAnimalGeneration = Sim.Wildlife.GenerationOf(animal); }
            }
            foreach (var wolf in Sim.Predators.Wolves)
            {
                int distance = Int2.SquaredDistance(new Int2(x, y), new Int2(wolf.X, wolf.Y));
                if (distance < best) { _selected = -1; _selectedAnimal = -1; _selectedWolf = wolf.Id; best = distance; }
            }
            if (_selected >= 0) { _selectedGeneration = Sim.Agents.GenerationOf(_selected); }
            _selectedPersonId = _selected >= 0 ? Sim.Society.Identity(_selected) : 0;
            ShowJournal(true); _drawer.CurrentTab = 1;
        }
        RefreshPanels();
    }
    private float CollectedStock(ResourceKind kind)
    {
        float total = Sim.GroundStocks.TotalOf(kind) + Sim.Storage.GrandTotalOf(kind, Sim.Buildings.Capacity);
        foreach (int slot in Sim.Agents.AliveSlots()) { total += Sim.Agents.InventoryOf(slot, kind); }
        return total;
    }
    private void RefreshPanels()
    {
        if (_summary == null) { return; }
        var sample = Sim.Observe();
        _summary.Text = $"第 {Sim.World.Calendar.Day} 天  ·  {Sim.Agents.LiveCount} 位居民  ·  {WeatherInfo.NameOf(Sim.World.Weather.Kind)}\n{Sim.Settlements.ActiveCount} 个聚落  ·  {Sim.Buildings.TotalCompleted} 栋建筑  ·  出生 {Sim.Stats.TotalBirths} / 死亡 {Sim.Stats.TotalDeaths}";
        _dayLabel.Text = "第 " + Sim.World.Calendar.Day + " 天 · " + WildPlaces.PhaseName(Sim.Clock / Sim.Config.Clock.TicksPerDay);
        UpdateMetric(_foodLabel, CollectedStock(ResourceKind.Food).ToString("0")); UpdateMetric(_woodLabel, CollectedStock(ResourceKind.Wood).ToString("0")); UpdateMetric(_stoneLabel, CollectedStock(ResourceKind.Stone).ToString("0"));
        UpdateMetric(_populationLabel, Sim.Agents.LiveCount.ToString()); UpdateMetric(_settlementLabel, Sim.Settlements.ActiveCount.ToString()); UpdateMetric(_buildingLabel, Sim.Buildings.TotalCompleted.ToString());
        var text = new StringBuilder();
        var archive = Sim.Society.Find(_selectedPersonId);
        var wolfView = Sim.Predators.Wolves.FirstOrDefault(w => w.Id == _selectedWolf);
        if (wolfView != null)
        {
            text.AppendLine($"狼 #{wolfView.Id} · {wolfView.AgeDays} 天\n动作 {wolfView.Action}");
            text.AppendLine($"能量 {wolfView.Energy:P0} · 干渴 {wolfView.Thirst:P0} · 疲劳 {wolfView.Fatigue:P0}");
            text.AppendLine($"捕猎 {wolfView.HuntUtility:F2}\n回避 {wolfView.FleeUtility:F2}\n饮水 {wolfView.DrinkUtility:F2}\n睡眠 {wolfView.SleepUtility:F2}");
        }
        else if (_selectedAnimal >= 0 && Sim.Wildlife.IsAlive(_selectedAnimal)
            && Sim.Wildlife.GenerationOf(_selectedAnimal) == _selectedAnimalGeneration)
        {
            int i = _selectedAnimal; var animals = Sim.Wildlife;
            text.AppendLine($"食草动物 #{i} · {animals.AgeDaysOf(i)} 天\n动作 {animals.ActionOf(i)}");
            text.AppendLine($"能量 {animals.EnergyOf(i):P0} · 干渴 {animals.ThirstOf(i):P0} · 疲劳 {animals.FatigueOf(i):P0}");
            text.AppendLine($"觅食 {1 - animals.EnergyOf(i):F2}\n饮水 {animals.ThirstOf(i) * animals.ThirstOf(i):F2}\n睡眠 {animals.FatigueOf(i) * animals.FatigueOf(i):F2}");
            text.AppendLine("威胁优先回避；湿润植被、水边提供饮水；繁殖受能量和栖息地容量约束。");
        }
        else if (archive != null && (!archive.Alive || !Sim.Agents.IsSlotAlive(archive.Slot)
            || Sim.Society.Identity(archive.Slot) != archive.Id))
        {
            text.AppendLine(archive.Name + " · 已故").AppendLine("年龄 " + archive.Age);
            text.AppendLine("死亡日期 " + archive.DiedTick / 1440);
            text.AppendLine("子女 " + string.Join("、", Sim.Society.ChildrenOf(archive.Id).Select(p => p.Name)));
            foreach (var ev in Sim.Society.TimelineOf(archive.Id).TakeLast(30)) { text.AppendLine(ev.Description + "\n" + ev.Cause); }
        }
        else if (_selected >= 0 && Sim.Agents.IsSlotAlive(_selected) && Sim.Agents.GenerationOf(_selected) == _selectedGeneration)
        {
            var a = Sim.Agents; int slot = _selected; var person = Sim.Society.OfSlot(slot);
            text.AppendLine(a.NameOrOverride(slot)).AppendLine($"年龄 {a.AgeDaysOf(slot)} 天 · {LifeStageName(a.LifeStageOf(slot))} · {JobName(a.JobOf(slot))}");
            text.AppendLine("\n身体与需求");
            text.AppendLine($"生命 {a.HealthOf(slot):P0}   饥饿 {a.HungerOf(slot):P0}   干渴 {a.ThirstOf(slot):P0}");
            text.AppendLine($"精力 {1 - a.FatigueOf(slot):P0}   位置 {a.PositionOf(slot)}");
            text.AppendLine(a.HasTarget(slot) ? "目的地 " + a.TargetOf(slot) : "原地活动");
            text.AppendLine($"正在{ActionRegistry.DisplayNameOf(a.ActionOf(slot))} · {PhaseName(a.PhaseOf(slot))}");
            text.AppendLine("\n随身物资");
            text.AppendLine($"食物 {a.InventoryOf(slot, ResourceKind.Food):F1}  木材 {a.InventoryOf(slot, ResourceKind.Wood):F1}");
            text.AppendLine($"石料 {a.InventoryOf(slot, ResourceKind.Stone):F1}  铁矿 {a.InventoryOf(slot, ResourceKind.Iron):F1}");
            var personality = a.PersonalityOf(slot);
            text.AppendLine("\n性格倾向");
            text.AppendLine($"勤劳 {personality.Industriousness:F2} 勇敢 {personality.Bravery:F2} 好斗 {personality.Aggression:F2}");
            text.AppendLine($"亲善 {personality.Kindness:F2} 社交 {personality.Sociability:F2} 贪心 {personality.Greed:F2}");
            if (person != null)
            {
                string PersonName(long id) => Sim.Society.Find(id)?.Name ?? "无";
                text.AppendLine("\n居所与关系");
                text.AppendLine($"聚落 {Sim.Society.SettlementName(person.Settlement)}\n居所 {person.Home}  工作地 {person.Workplace}");
                text.AppendLine($"母亲 {PersonName(person.Mother)}\n父亲 {PersonName(person.Father)}\n伴侣 {PersonName(person.Partner)}");
                text.AppendLine("子女 " + string.Join("、", Sim.Society.ChildrenOf(person.Id).Select(p => p.Name)));
                text.AppendLine("兄弟姐妹 " + string.Join("、", Sim.Society.SiblingsOf(person.Id).Select(p => p.Name)));
                text.AppendLine("朋友 " + string.Join("、", a.AliveSlots().Where(other => other != slot
                    && Sim.Relationships.KindOf(slot, other) == RelationshipKind.Friend).Select(a.NameOrOverride)));
                text.AppendLine("敌人 " + string.Join("、", a.AliveSlots().Where(other => other != slot
                    && Sim.Relationships.KindOf(slot, other) == RelationshipKind.Enemy).Select(a.NameOrOverride)));
                text.AppendLine("\n个人历史");
                foreach (var ev in Sim.Society.TimelineOf(person.Id).TakeLast(12)) { text.AppendLine(ev.Description); }
            }
            text.AppendLine("\n上次决策 / 效用");
            var breakdown = a.LastDecisionOf(slot);
            if (breakdown.Scores != null)
                foreach (var score in breakdown.Scores.OrderByDescending(s => s.Utility).Take(8))
                {
                    text.AppendLine($"{ActionRegistry.DisplayNameOf(score.Action)}  {score.Utility:F3}" + (score.Blocked ? "（条件不满足）" : ""));
                    if (score.Action == breakdown.Chosen)
                        foreach (var c in score.Considerations) { text.AppendLine($"  {c.Name}  {c.Input:F2} → {c.Contribution:F2}"); }
                }
        }
        else
        {
            var tile = Sim.World.TileAtClamped(_selectedX, _selectedY);
            var place = Wild.At(_selectedX, _selectedY);
            text.AppendLine(place?.Name ?? "土地");
            text.AppendLine($"({_selectedX}, {_selectedY}) · {TerrainLabel(tile.Terrain)}");
            if (place != null) { text.AppendLine().AppendLine(Wild.Describe(Sim, place).Substring(place.Name.Length).TrimStart('\n')).AppendLine(); }
            text.AppendLine($"地表{ResourceLabel(tile.Resource.Kind)}  {tile.Resource.Amount:F1}");
            int settlement = Sim.Society.TerritoryAt(_selectedX, _selectedY);
            var s = Sim.Civilizations.Settlement(settlement);
            if (s != null)
            {
                text.AppendLine($"\n{Sim.Society.SettlementName(s.Value.Id)} · {s.Value.Tier}\n人口 {s.Value.Population}  住房 {s.Value.Houses}  仓库 {s.Value.Storages}");
                text.AppendLine("领袖 " + (Sim.Society.Find(Sim.Society.LeaderOf(settlement))?.Name ?? "无"));
                foreach (ResourceKind kind in new[] { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone, ResourceKind.Iron })
                    text.AppendLine($"{ResourceLabel(kind)}  库存 {Sim.Civilizations.Stock(settlement, kind):F1} / 需求 {Sim.Civilizations.Need(settlement, kind):F1} / 价格 {Sim.Civilizations.Price(settlement, kind):F2}");
                var civil = Sim.Civilizations.OfSettlement(settlement);
                if (civil != null) { text.AppendLine($"\n{civil.Name}\n工具 {civil.Tools}  武器 {civil.Weapons}\n尚武 {civil.Militarism:F2}  扩张 {civil.Expansionism:F2}\n贸易 {civil.TradePreference:F2}  技术 {civil.TechnologyFocus:F2}"); }
                foreach (var rel in Sim.Civilizations.Relations.Where(d => d.A == settlement || d.B == settlement))
                    text.AppendLine($"外交 {rel.A} ↔ {rel.B}  信任 {rel.Trust:F2}  压力 {rel.Pressure:F2}  {(rel.War ? "战争" : rel.Alliance ? "联盟" : "和平")}");
                text.AppendLine("\n聚落历史");
                foreach (var ev in Sim.Society.History.Where(e => e.Settlement == settlement).TakeLast(20))
                    text.AppendLine(ev.Description + " · " + ev.Cause);
            }
        }
        _drawer.SetTabTitle(1, _selectedPersonId != 0 ? "人物" : _selectedAnimal >= 0 || _selectedWolf >= 0 ? "动物" : "土地");
        RefreshResidentVitals();
        _inspector.Text = FormatInspector(text.ToString());
        var history = new StringBuilder();
        for (int i = Sim.Events.Count - 1, count = 0; i >= 0 && count < 80; i--)
        {
            var ev = Sim.Events[i]; if (ev.Importance < SandBoxSim.Core.History.EventImportance.Normal) { continue; }
            history.AppendLine($"[color=#806644]第 {ev.Tick / 1440} 天[/color]")
                .AppendLine("[b]" + EscapeMarkup(ev.Description) + "[/b]")
                .AppendLine("[color=#747866]" + EscapeMarkup(ev.Cause) + "[/color]").AppendLine(); count++;
        }
        _history.Text = history.ToString();
    }
    private void RunSelfTest()
    {
        try
        {
            new NatureModels().ValidateCraftedModels();
            new NatureModels().ValidateResidentMeshes();
            new NatureModels().ValidateWildPlaceMeshes();
            Tool = PlayerTool.Forest; ClickTile(20, 20);
            if (Sim.World.TerrainAt(20, 20) != TerrainKind.Forest) { throw new Exception("Forest tool failed"); }
            Tool = PlayerTool.Inspect; ClickTile(50, 50);
            Sim.Tick(1);
            string before = StateHash.ComputeDigest(Sim); RefreshPanels();
            ((WorldView3D)_map).ValidateViewControls();
            ((WorldView3D)_map).ValidateObservationLayers();
            int observed = Sim.Agents.AliveSlots().FirstOrDefault(-1);
            if (observed >= 0) { var position = Sim.Agents.PositionOf(observed); FocusStory(Sim.Society.Identity(observed), position.X, position.Y); }
            ValidateHudLayout();
            ShowTools(true);
            if (!_dockPanel.Visible) { throw new Exception("Contextual tool menu failed to open"); }
            SelectTool(PlayerTool.Inspect);
            if (_dockPanel.Visible) { throw new Exception("Observation mode did not dismiss the tool menu"); }
            if (observed >= 0)
            {
                float originalFood = Sim.Agents.InventoryOf(observed, ResourceKind.Food);
                float collected = CollectedStock(ResourceKind.Food);
                try
                {
                    Sim.Agents.SetInventory(observed, ResourceKind.Food, originalFood + 7); RefreshPanels();
                    if (_foodLabel.Text != (collected + 7).ToString("0")) { throw new Exception("Resource bar ignored carried reserves"); }
                }
                finally { Sim.Agents.SetInventory(observed, ResourceKind.Food, originalFood); RefreshPanels(); }
            }
            if (observed >= 0) { _residentPortrait.ValidatePresentation(); }
            _discovery.ValidateNavigation();
            if (observed >= 0) { TogglePin(); if (PinnedPerson != Sim.Society.Identity(observed)) { throw new Exception("Resident pin lost stable identity"); } }
            _discovery.Refresh();
            _decisionDetails = true; RefreshPanels(); _decisionDetails = false;
            _drawer.CurrentTab = 3; _chart._GuiInput(new InputEventMouseMotion { Position = new Vector2(150, 200) });
            SetCategory("地貌"); SelectTool(PlayerTool.River); SetCategory("生命"); SelectTool(PlayerTool.Inspect);
            ShowSettings(true); ShowSettings(false); ShowJournal(false); ShowJournal(true); ValidateHudMotion();
            if (before != StateHash.ComputeDigest(Sim)) { throw new Exception("Inspector mutated simulation"); }
            if (observed >= 0)
            {
                _residentName.Text = "林溪[一]"; RenameResident();
                if (Sim.Society.Find(PinnedPerson)?.Name != "林溪[一]") { throw new Exception("Resident naming lost stable identity"); }
            }
            string wildMetadata = Wild.Encode().ToJson();
            string saved = EncodeClientWorld();
            if (WildPlaces.Decode(JsonParser.Parse(saved).Get("client").Get("wild")).Encode().ToJson() != wildMetadata)
                { throw new Exception("Wild place client metadata failed to round-trip"); }
            RememberPlace("自选河湾");
            var remembered = WildPlaces.Decode(JsonParser.Parse(EncodeClientWorld()).Get("client").Get("wild"));
            if (!remembered.Marks.Any(m => m.Name == "自选河湾")) { throw new Exception("Place note missing from client save"); }
            Wild = WildPlaces.Decode(JsonParser.Parse(wildMetadata));
            if (JsonParser.Parse(saved).Get("client").GetInt("scenario", -1) != Scenario) { throw new Exception("Scenario context missing from client save"); }
            var restored = Simulation.CreateForRestore(Sim.Config.Clone(), 100, 100, 1);
            var result = SaveLoader.Load(restored, saved);
            if (!result.Success || !result.DigestMatches) { throw new Exception("Client save round trip failed"); }
            var legacy=JsonParser.Parse(saved);
            legacy.Get("client").Set("trial",new WorldTrial().Encode().Set("kind",JsonValue.From(1)).Set("running",JsonValue.From(true)).Set("influence",JsonValue.From(0))).Set("projects",new LandProjects().Encode()).Set("blueprint",new SettlementBlueprint().Encode());
            RestoreClientContext(legacy);
            var freeClient=JsonParser.Parse(EncodeClientWorld()).Get("client");
            if(!freeClient.Get("trial").IsNull || !freeClient.Get("projects").IsNull || !freeClient.Get("blueprint").IsNull || _drawer.GetTabCount()!=4)
                throw new Exception("Legacy objectives survived in free exploration client");
            PlayerTools.Apply(Sim,PlayerTool.Forest,20,20,1,10); Sim.Fire.Ignite(20,20,Sim.Clock,"火情自检");
            string hazardBefore=StateHash.ComputeDigest(Sim); ((WorldView3D)_map).ValidateHazardVisuals();
            if(hazardBefore!=StateHash.ComputeDigest(Sim)) throw new Exception("Hazards observation mutated world");
            SelectTool(PlayerTool.Food);string freeBefore=StateHash.ComputeDigest(Sim);ClickTile(20,20);
            if(freeBefore==StateHash.ComputeDigest(Sim)) throw new Exception("Legacy quota blocked free creation");
            SelectTool(PlayerTool.Inspect);
            var original = Sim;
            try
            {
                var rectangular = new Simulation(Sim.Config.Clone(), 60, 44, 17);
                string rectangularSave = SaveFile.Encode(rectangular);
                Sim = Simulation.CreateForRestore(rectangular.Config.Clone(), 60, 44, 17);
                var rectangularResult = SaveLoader.Load(Sim, rectangularSave);
                if (!rectangularResult.Success || !rectangularResult.DigestMatches) { throw new Exception("Rectangular world restore failed"); }
                string rectangularBefore = StateHash.ComputeDigest(Sim);
                ((WorldView3D)_map).ValidateWorldDimensions();
                _discovery.ValidateNavigation();
                if (rectangularBefore != StateHash.ComputeDigest(Sim)) { throw new Exception("Rectangular world rendering mutated simulation"); }
            }
            finally { Sim = original; ((WorldView3D)_map).ValidateWorldDimensions(); }
            GD.Print("GODOT_SELF_TEST_PASS: resident meshes/poses/portrait gestures, tools, observer purity, save/load, minimap, resident pin, objective-free save migration, hazards, mouse drag/orbit/zoom/release and 1280/1600/1920 layouts");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
}
