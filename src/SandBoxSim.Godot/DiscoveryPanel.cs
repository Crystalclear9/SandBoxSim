using System;
using System.Linq;
using System.Text;
using Godot;
using SandBoxSim.Core.History;

namespace SandBoxSim.Client;

/// <summary>Questions and stories derived from actual simulation; never grants scripted social outcomes.</summary>
public partial class DiscoveryPanel : VBoxContainer
{
    public MainGame Game { get; set; } = null!;
    private Label _question = null!, _vitals = null!, _discoveries = null!, _sceneName = null!;
    private readonly Label[] _valueLabels = new Label[4];
    private RichTextLabel _stories = null!;
    private long _eventCount = -1;
    private int _scenario = -1;
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 18);
        _sceneName = HudStyle.Label("河谷新生", 20); AddChild(_sceneName);
        _question = HudStyle.Label("", 14, true); _question.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_question);
        AddChild(new HSeparator());
        var values = new GridContainer { Columns = 2 }; values.AddThemeConstantOverride("h_separation", 32); values.AddThemeConstantOverride("v_separation", 14); AddChild(values);
        string[] captions = { "安居的居民", "饥饿的居民", "食草动物", "完成的交易" };
        for (int i = 0; i < 4; i++)
        {
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; column.AddThemeConstantOverride("separation", 0); values.AddChild(column);
            column.AddChild(HudStyle.Label(captions[i], 11, true)); _valueLabels[i] = HudStyle.Label("0", 24); column.AddChild(_valueLabels[i]);
        }
        _vitals = HudStyle.Label("", 12, true); _vitals.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_vitals);
        _discoveries = HudStyle.Label("", 13); _discoveries.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_discoveries);
        AddChild(new HSeparator());
        AddChild(HudStyle.Label("此刻的故事", 17));
        _stories = new RichTextLabel { BbcodeEnabled = true, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true };
        _stories.MetaClicked += meta =>
        {
            string[] fields = meta.AsString().Split(':');
            if (fields.Length == 3 && long.TryParse(fields[0], out long person) && int.TryParse(fields[1], out int x) && int.TryParse(fields[2], out int y))
                { Game.FocusStory(person, x, y); }
        };
        AddChild(_stories);
        Refresh();
    }
    public void Refresh()
    {
        if (_question == null) { return; }
        var sim = Game.Sim;
        _sceneName.Text = SandBoxSim.Core.Systems.SandboxScenarios.Names[Game.Scenario];
        _question.Text = SandBoxSim.Core.Systems.SandboxScenarios.Questions[Game.Scenario];
        int hungry = 0, sick = 0, housed = 0;
        foreach (int slot in sim.Agents.AliveSlots())
        {
            if (sim.Agents.HungerOf(slot) > .7f) { hungry++; }
            if (sim.Diseases.OfSlot(slot)?.Active == true) { sick++; }
            if (sim.Agents.DwellingOf(slot) >= 0) { housed++; }
        }
        int trade = sim.Civilizations.Relations.Sum(r => r.Trades);
        _valueLabels[0].Text = housed.ToString(); _valueLabels[1].Text = hungry.ToString(); _valueLabels[2].Text = sim.Wildlife.LiveCount.ToString(); _valueLabels[3].Text = trade.ToString();
        _vitals.Text = $"狼群 {sim.Predators.Wolves.Count}   ·   患病 {sick}   ·   交战 {sim.Civilizations.Relations.Count(r => r.War)}";
        bool[] found = { sim.Buildings.TotalCompleted > 0, sim.Settlements.ActiveCount > 0, sim.Stats.TotalBirths > 0, trade > 0,
            sim.Civilizations.Civilizations.Any(c => c.Tools > 0), sim.Civilizations.Relations.Any(r => r.Alliance) };
        string[] names = { "第一栋建筑", "聚落诞生", "下一代", "互通有无", "工具革新", "结成联盟" };
        int discovered = found.Count(v => v), next = Array.FindIndex(found, value => !value);
        _discoveries.Text = $"文明见闻   {discovered} / {found.Length}\n" + (next >= 0 ? "可观察：" + names[next] : "六种文明变化都已发生");
        if (_eventCount == sim.Events.TotalRecorded && _scenario == Game.Scenario) { return; }
        _eventCount = sim.Events.TotalRecorded; _scenario = Game.Scenario;
        var text = new StringBuilder();
        var recent = sim.Society.History.Where(e => IsStory((WorldEventType)e.Type)).TakeLast(100).Reverse()
            .GroupBy(e => e.Type).SelectMany(group => group.Take(2)).OrderByDescending(e => e.Tick).Take(8);
        foreach (var ev in recent)
        {
            string description = ev.Description;
            int location = description.IndexOf(" @ ", StringComparison.Ordinal); if (location > 0) { description = description.Substring(0, location); }
            if ((WorldEventType)ev.Type == WorldEventType.AgentMigrated) { description = (sim.Society.Find(ev.Actor)?.Name ?? "一位居民") + " 寻找新的家园"; }
            text.AppendLine($"[color=#9dac9f]第 {ev.Tick / sim.Config.Clock.TicksPerDay + 1} 天[/color]")
                .AppendLine("[b]" + Escape(description) + "[/b]");
            if (ev.X >= 0 && ev.Y >= 0 || ev.Actor != 0) { text.AppendLine($"[url={ev.Actor}:{ev.X}:{ev.Y}][color=#d8bb84]查看故事  →[/color][/url]"); }
            text.AppendLine();
        }
        if (text.Length == 0) { text.Append("[color=#a2b1a5]故事从一片土地开始。\n\n居民正在寻找水与食物。靠近他们，看看下一步会发生什么。[/color]"); }
        _stories.Text = text.ToString();
    }
    private static string Escape(string value) => value.Replace("[", "[lb]");
    private static bool IsStory(WorldEventType type) => type is WorldEventType.AgentBorn or WorldEventType.AgentDied
        or WorldEventType.AgentMigrated or WorldEventType.Marriage or WorldEventType.BuildingCompleted
        or WorldEventType.BuildingDestroyed or WorldEventType.SettlementFounded or WorldEventType.SettlementAbandoned
        or WorldEventType.TradeRouteEstablished or WorldEventType.WarDeclared or WorldEventType.WarEnded
        or WorldEventType.AllianceFormed or WorldEventType.Innovation or WorldEventType.Disease or WorldEventType.LeaderElected;
}
