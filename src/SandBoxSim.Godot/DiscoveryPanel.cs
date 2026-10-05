using System;
using System.Linq;
using System.Text;
using Godot;
using SandBoxSim.Core.History;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

/// <summary>Actionable, read-only field notebook. Actual risks and personal stories lead into player choices.</summary>
public partial class DiscoveryPanel : VBoxContainer
{
    public MainGame Game { get; set; } = null!;
    private Label _place = null!, _phase = null!, _pinText = null!, _empty = null!;
    private RichTextLabel _stories = null!;
    private WorldMiniMap _miniMap = null!;
    private Godot.Button _pinLink = null!;
    private readonly PanelContainer[] _cards = new PanelContainer[3];
    private readonly Label[] _titles = new Label[3], _details = new Label[3];
    private System.Collections.Generic.IReadOnlyList<WorldAlert> _alerts = Array.Empty<WorldAlert>();
    public override void _Ready()
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 14); scroll.AddChild(body);
        _place = HudStyle.Label("", 24); body.AddChild(_place);
        _phase = HudStyle.Label("", 12, true); _phase.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_phase);
        _miniMap = new WorldMiniMap { Game = Game, CustomMinimumSize = new Vector2(300, 156) }; body.AddChild(_miniMap);
        var heading = new HBoxContainer(); body.AddChild(heading); var title = HudStyle.Label("此刻，先做什么", 17); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(title);
        var play = new Godot.Button { Text = "试炼 →" }; HudStyle.Button(play); play.Pressed += () => Game.OpenTrials(); heading.AddChild(play);
        _empty = HudStyle.Label("局势暂时稳定。试试改变一片土地。", 13, true); _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_empty);
        for (int i = 0; i < _cards.Length; i++)
        {
            int index = i;
            var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", HudStyle.Box(new Color("#20322c"), 10, 12, false)); body.AddChild(card); _cards[i] = card;
            var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 5); card.AddChild(content);
            _titles[i] = HudStyle.Label("", 16); content.AddChild(_titles[i]);
            _details[i] = HudStyle.Label("", 12, true); _details[i].AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(_details[i]);
            var actions = new HBoxContainer(); content.AddChild(actions);
            var locate = new Godot.Button { Text = "前往现场" }; HudStyle.Button(locate); locate.Pressed += () => { if (index < _alerts.Count) { Game.RespondTo(_alerts[index], false); } }; actions.AddChild(locate);
            var aid = new Godot.Button { Text = "准备救援 →" }; HudStyle.Button(aid); aid.Pressed += () => { if (index < _alerts.Count) { Game.RespondTo(_alerts[index], true); } }; actions.AddChild(aid);
        }
        var projects = new Godot.Button { Text = "规划生态工程   →", Alignment = HorizontalAlignment.Left, Icon = ToolGlyphs.For(PlayerTool.Forest), ExpandIcon = true };
        projects.AddThemeConstantOverride("icon_max_width", 26); HudStyle.Button(projects);
        projects.AddThemeStyleboxOverride("normal", HudStyle.Box(new Color("#31483d"), 10, 14, false)); projects.Pressed += () => Game.OpenProjects(); body.AddChild(projects);
        body.AddChild(new HSeparator()); body.AddChild(HudStyle.Label("一个人的世界", 17));
        _pinText = HudStyle.Label("", 13, true); _pinText.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_pinText);
        _pinLink = new Godot.Button { Text = "查看关注的故事 →", Alignment = HorizontalAlignment.Left }; HudStyle.Button(_pinLink); _pinLink.Pressed += () => Game.FocusPinned(); body.AddChild(_pinLink);
        body.AddChild(new HSeparator()); body.AddChild(HudStyle.Label("刚刚发生", 16));
        _stories = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true, CustomMinimumSize = new Vector2(0, 80) };
        _stories.MetaClicked += meta =>
        {
            var fields = meta.AsString().Split(':');
            if (fields.Length == 3 && long.TryParse(fields[0], out long person) && int.TryParse(fields[1], out int x) && int.TryParse(fields[2], out int y)) { Game.FocusStory(person, x, y); }
        };
        body.AddChild(_stories); Refresh();
    }
    public void Refresh()
    {
        if (_place == null) { return; }
        var sim = Game.Sim;
        _place.Text = SandboxScenarios.Names[Game.Scenario];
        string weather = sim.World.Weather.Kind switch { SandBoxSim.Core.Environment.WeatherKind.Clear => "晴朗", SandBoxSim.Core.Environment.WeatherKind.Cloudy => "多云",
            SandBoxSim.Core.Environment.WeatherKind.Rain => "降雨", SandBoxSim.Core.Environment.WeatherKind.Storm => "风暴",
            SandBoxSim.Core.Environment.WeatherKind.Drought => "旱季", _ => "降雪" };
        _phase.Text = Game.Trial.Running ? "试炼第 " + Game.Trial.Days + " 天 · " + Game.Trial.Forecast : weather + " · 选择一个局势，走进这个世界";
        _alerts = WorldAlerts.Observe(sim); _empty.Visible = _alerts.Count == 0;
        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i].Visible = i < _alerts.Count; if (i >= _alerts.Count) { continue; }
            var a = _alerts[i]; _titles[i].Text = a.Title + "  ·  " + a.Count;
            _titles[i].AddThemeColorOverride("font_color", a.Key == "fire" ? new Color("#efa678") : a.Key == "disease" ? new Color("#c9a5dd") : HudStyle.Accent);
            _details[i].Text = a.Detail;
        }
        var pinned = sim.Society.Find(Game.PinnedPerson); _pinLink.Disabled = pinned == null;
        _pinText.Text = pinned == null ? "在人物页关注一位居民。\n从安居到迁徙，看看他如何回应你的改变。"
            : pinned.Alive ? pinned.Name + $" · 健康 {sim.Agents.HealthOf(pinned.Slot):P0}\n饥饿 {sim.Agents.HungerOf(pinned.Slot):P0} · " + (sim.Agents.DwellingOf(pinned.Slot) >= 0 ? "已有居所" : "仍在寻找家园")
            : pinned.Name + "已离世。\n他的家庭与个人历史仍保留在人物页。";
        var text = new StringBuilder();
        foreach (var ev in sim.Society.History.Where(e => IsStory((WorldEventType)e.Type)).TakeLast(3).Reverse())
        {
            text.AppendLine($"[color=#a2b1a5]第 {ev.Tick / sim.Config.Clock.TicksPerDay + 1} 天[/color]  " + ev.Description.Replace("[", "[lb]"));
            if (ev.Actor != 0 || ev.X >= 0 && ev.Y >= 0) { text.AppendLine($"[url={ev.Actor}:{ev.X}:{ev.Y}][color=#d8bb84]追踪故事 →[/color][/url]"); }
            text.AppendLine();
        }
        _stories.Text = text.Length == 0 ? "[color=#a2b1a5]居民的第一次选择，将成为这里的故事。[/color]" : text.ToString();
        _miniMap.QueueRedraw();
    }
    public void ValidateNavigation() => _miniMap.ValidateMapping();
    private static bool IsStory(WorldEventType type) => type is WorldEventType.AgentBorn or WorldEventType.AgentDied
        or WorldEventType.Marriage or WorldEventType.BuildingCompleted or WorldEventType.SettlementFounded
        or WorldEventType.TradeRouteEstablished or WorldEventType.WarDeclared or WorldEventType.Innovation or WorldEventType.LeaderElected;
}
