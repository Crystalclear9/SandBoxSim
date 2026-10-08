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
    private Label _place = null!, _phase = null!, _pinText = null!;
    private RichTextLabel _stories = null!;
    private Label _land = null!;
    private LineEdit _landName = null!;
    private RichTextLabel _marks = null!;
    private WorldMiniMap _miniMap = null!;
    private Godot.Button _pinLink = null!;
    public override void _Ready()
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); scroll.AddChild(body);
        VBoxContainer Card()
        {
            var panel=new PanelContainer();var box=HudStyle.Box(new Color("#2f3b32"),5,14,false);panel.AddThemeStyleboxOverride("panel",box);body.AddChild(panel);
            var content=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};content.AddThemeConstantOverride("separation",8);panel.AddChild(content);return content;
        }
        body.AddChild(HudStyle.Label("田野 · 随手记", 10, true));
        _place = HudStyle.Label("", 27); body.AddChild(_place);
        _phase = HudStyle.Label("", 12, true); _phase.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_phase);
        _miniMap = new WorldMiniMap { Game = Game, CustomMinimumSize = new Vector2(280, 146) }; body.AddChild(_miniMap);
        _land = HudStyle.Label("", 13, true); _land.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_land);
        var naming = new HBoxContainer(); body.AddChild(naming);
        _landName = new LineEdit { PlaceholderText = "为这里起个名字", MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill }; naming.AddChild(_landName);
        var remember = new Godot.Button { Text = "记下" }; HudStyle.Button(remember); naming.AddChild(remember);
        remember.Pressed += () => { Game.RememberPlace(_landName.Text); _landName.Text = ""; Refresh(); };
        _marks = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false }; body.AddChild(_marks);
        _marks.MetaClicked += meta => {
            string[] parts = meta.AsString().Split(':');
            if(parts.Length!=3 || !int.TryParse(parts[1],out int x) || !int.TryParse(parts[2],out int y))return;
            if(parts[0]=="forget") { Game.Wild.Forget(x,y); Refresh(); } else { Game.LookAtPlace(x,y); }
        };
        var residentCard=Card(); residentCard.AddChild(HudStyle.Heading("居民的故事", 16));
        _pinText = HudStyle.Label("", 13, true); _pinText.AutowrapMode = TextServer.AutowrapMode.WordSmart; residentCard.AddChild(_pinText);
        _pinLink = new Godot.Button { Text = "查看关注的故事 →", Alignment = HorizontalAlignment.Left }; HudStyle.Button(_pinLink); _pinLink.Pressed += () => Game.FocusPinned(); residentCard.AddChild(_pinLink);
        var storyCard=Card(); storyCard.AddChild(HudStyle.Heading("世界的回声", 16));
        _stories = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true, CustomMinimumSize = new Vector2(0, 80) };
        _stories.MetaClicked += meta =>
        {
            var fields = meta.AsString().Split(':');
            if (fields.Length == 3 && long.TryParse(fields[0], out long person) && int.TryParse(fields[1], out int x) && int.TryParse(fields[2], out int y)) { Game.FocusStory(person, x, y); }
        };
        storyCard.AddChild(_stories); Refresh();
    }
    public void Refresh()
    {
        if (_place == null) { return; }
        var sim = Game.Sim;
        _place.Text = SandboxScenarios.Names[Game.Scenario];
        string weather = sim.World.Weather.Kind switch { SandBoxSim.Core.Environment.WeatherKind.Clear => "晴朗", SandBoxSim.Core.Environment.WeatherKind.Cloudy => "多云",
            SandBoxSim.Core.Environment.WeatherKind.Rain => "降雨", SandBoxSim.Core.Environment.WeatherKind.Storm => "风暴",
            SandBoxSim.Core.Environment.WeatherKind.Drought => "旱季", _ => "降雪" };
        _phase.Text = weather + " · " + WildPlaces.PhaseName(sim.Clock / sim.Config.Clock.TicksPerDay) + " · 第 " + sim.World.Calendar.Day + " 天";
        var local=Game.Wild.At(Game.SelectedX,Game.SelectedY);
        _land.Text=local==null ? $"({Game.SelectedX}, {Game.SelectedY}) · 单击土地查看这里的水土与痕迹。" : Game.Wild.Describe(sim,local);
        float traffic=sim.World.TileAt(Game.SelectedX,Game.SelectedY).FootTraffic;
        if(traffic>.02f)_land.Text+=$"\n反复踩踏 · 压实 {traffic:P0} · 植被恢复变慢";
        var notes=new StringBuilder();
        foreach(var mark in Game.Wild.Marks)
            notes.AppendLine($"[url=place:{mark.X}:{mark.Y}][color=#c7b992]{mark.Name.Replace("[","[lb]")}[/color][/url]  [url=forget:{mark.X}:{mark.Y}]×[/url]");
        _marks.Text=notes.ToString();
        var pinned = sim.Society.Find(Game.PinnedPerson); _pinLink.Visible = pinned != null;
        _pinText.Text = pinned == null ? "尚未关注居民。"
            : pinned.Alive ? pinned.Name + $" · 健康 {sim.Agents.HealthOf(pinned.Slot):P0}\n饥饿 {sim.Agents.HungerOf(pinned.Slot):P0} · " + (sim.Agents.DwellingOf(pinned.Slot) >= 0 ? "已有居所" : "仍在寻找家园")
            : pinned.Name + "已离世。\n他的家庭与个人历史仍保留在人物页。";
        var text = new StringBuilder();
        foreach (var ev in sim.Society.History.Where(e => IsStory((WorldEventType)e.Type)).TakeLast(3).Reverse())
        {
            text.AppendLine($"[color=#a8b3a5]第 {ev.Tick / sim.Config.Clock.TicksPerDay + 1} 天[/color]  " + ev.Description.Replace("[", "[lb]"));
            if (ev.Actor != 0 || ev.X >= 0 && ev.Y >= 0) { text.AppendLine($"[url={ev.Actor}:{ev.X}:{ev.Y}][color=#c7b992]追踪故事 →[/color][/url]"); }
            text.AppendLine();
        }
        _stories.Text = text.Length == 0 ? "[color=#a8b3a5]居民的第一次选择，将成为这里的故事。[/color]" : text.ToString();
        _miniMap.QueueRedraw();
    }
    public void ValidateNavigation() => _miniMap.ValidateMapping();
    private static bool IsStory(WorldEventType type) => type is WorldEventType.AgentBorn or WorldEventType.AgentDied
        or WorldEventType.Marriage or WorldEventType.BuildingCompleted or WorldEventType.SettlementFounded
        or WorldEventType.TradeRouteEstablished or WorldEventType.WarDeclared or WorldEventType.Innovation or WorldEventType.LeaderElected;
}
