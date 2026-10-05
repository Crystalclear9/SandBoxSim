using System;
using System.Linq;
using Godot;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private Label _trialTitle = null!, _trialForecast = null!, _trialValues = null!, _trialGoal = null!, _trialNotice = null!, _concern = null!;
    private ProgressBar _trialProgress = null!;
    private VBoxContainer _trialChoices = null!;
    private Godot.Button _retryTrial = null!, _leaveTrial = null!;
    private Int2 _concernLocation;
    private PlayerTool _concernTool = PlayerTool.Food;
    private void AdvanceWorld(int ticks)
    {
        while (ticks > 0)
        {
            int toBoundary = Sim.Config.Clock.TicksPerDay - (int)(Sim.Clock % Sim.Config.Clock.TicksPerDay);
            int step = Math.Min(ticks, toBoundary);
            string before = Trial.Notice;
            Sim.Tick(step); ticks -= step; Projects.Advance(Sim, Trial); Trial.Advance(Sim);
            if (Trial.Notice != before && _status != null) { _status.Text = Trial.Notice; }
        }
    }
    public void OpenTrials() { ShowJournal(true); _drawer.CurrentTab = 4; RefreshTrialPanel(); }
    private void StartTrial(int kind)
    {
        if (Trial.Running) { return; }
        if (Trial.Start(Sim, kind))
        {
            _checkpoint = EncodeClientWorld(); _experimentLabel = WorldTrial.Names[kind] + " · 试炼起点";
            _status.Text = "试炼起点已记下，可以回溯尝试不同策略"; SetSpeed(8);
            _map.Focus(Trial.Location.X, Trial.Location.Y, 16);
        }
        OpenTrials();
    }
    private void TrialWeather(WeatherKind kind)
    {
        if (!Trial.TryWeather()) { _status.Text = Trial.Notice; RefreshTrialPanel(); return; }
        Sim.InterveneForceWeather(kind, 24);
    }
    private void BuildTrialPanel()
    {
        var scroll = new ScrollContainer { Name = "试炼", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _drawer.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); scroll.AddChild(body);
        _trialTitle = HudStyle.Label("世界试炼", 21); body.AddChild(_trialTitle);
        _trialForecast = HudStyle.Label("", 13, true); _trialForecast.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_trialForecast);
        _trialValues = HudStyle.Label("", 15); body.AddChild(_trialValues);
        _trialProgress = new ProgressBar { CustomMinimumSize = new Vector2(0, 8), ShowPercentage = false, MaxValue = 2 };
        _trialProgress.AddThemeStyleboxOverride("background", HudStyle.Box(HudStyle.Wash, 4, 0, false));
        _trialProgress.AddThemeStyleboxOverride("fill", HudStyle.Box(HudStyle.Accent, 4, 0, false)); body.AddChild(_trialProgress);
        _trialGoal = HudStyle.Label("", 13, true); _trialGoal.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_trialGoal);
        _trialNotice = HudStyle.Label("", 14); _trialNotice.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_trialNotice);
        _trialChoices = new VBoxContainer(); _trialChoices.AddThemeConstantOverride("separation", 10); body.AddChild(_trialChoices);
        for (int i = 0; i < WorldTrial.Names.Length; i++)
        {
            int kind = i;
            var button = ActionButton(_trialChoices, WorldTrial.Names[i] + "  →", () => StartTrial(kind));
            button.Alignment = HorizontalAlignment.Left;
            var brief = HudStyle.Label(WorldTrial.Briefs[i], 12, true); brief.AutowrapMode = TextServer.AutowrapMode.WordSmart; _trialChoices.AddChild(brief);
        }
        var ending = new HBoxContainer(); body.AddChild(ending);
        _retryTrial = ActionButton(ending, "回溯起点", () => { RestoreCheckpoint(); OpenTrials(); });
        _leaveTrial = ActionButton(ending, "自由沙盒", () => { Trial.Leave(); RefreshTrialPanel(); });
        body.AddChild(new HSeparator()); body.AddChild(HudStyle.Label("居民现在需要什么", 17));
        _concern = HudStyle.Label("", 13, true); _concern.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_concern);
        var help = new HBoxContainer(); body.AddChild(help);
        ActionButton(help, "定位诉求", () => { _map.Focus(_concernLocation.X, _concernLocation.Y, 24); });
        ActionButton(help, "准备干预", () => PrepareAid(_concernTool, _concernLocation));
        var aids = new HBoxContainer(); aids.AddThemeConstantOverride("separation", 5); body.AddChild(aids);
        foreach (var tool in new[] { PlayerTool.Rain, PlayerTool.Food, PlayerTool.Heal })
        {
            var chosen = tool;
            ActionButton(aids, tool == PlayerTool.Rain ? "降雨" : tool == PlayerTool.Food ? "粮食" : "治愈", () => PrepareAid(chosen, _concernLocation));
        }
        RefreshTrialPanel();
    }
    private void PrepareAid(PlayerTool tool, Int2 location)
    {
        SetCategory(tool is PlayerTool.Heal or PlayerTool.Rain ? "祝福" : tool is PlayerTool.River or PlayerTool.Water ? "地貌" : tool == PlayerTool.Wood ? "资源" : "生命"); SelectTool(tool);
        _radius.Value = 4; _strength.Value = tool == PlayerTool.Rain ? 70 : tool == PlayerTool.Food ? 35 : 10;
        _map.Focus(location.X, location.Y, 24);
        _status.Text = "在需要帮助的地点点击施加干预；改变条件后，观察居民的行动";
    }
    private void RefreshTrialPanel()
    {
        if (_trialTitle == null) { return; }
        Trial.Refresh(Sim);
        _trialTitle.Text = Trial.Kind < 0 ? "世界试炼" : WorldTrial.Names[Trial.Kind] + (Trial.Running ? "" : Trial.Won ? " · 完成" : " · 结束");
        _trialForecast.Text = Trial.Kind < 0 ? "自由沙盒中无限干预。试炼中额度有限，危机会真正改变这个世界。" : Trial.Forecast;
        _trialValues.Text = Trial.Kind < 0 ? "" : $"干预额度  {(int)Trial.Influence} / 200\n原居民  {Trial.Survivors}/{Trial.OriginalCount}   ·   安居  {Trial.Housed}";
        _trialProgress.Visible = _trialGoal.Visible = Trial.Kind >= 0;
        _trialProgress.Value = Trial.StableDays;
        _trialGoal.Text = $"连续两天达成：存活 ≥ {Trial.TargetSurvivors}，饥饿 ≤ 25%，安居 ≥ 30%" + (Trial.Kind == 2 ? "，患病 ≤ 10%" : "")
            + $"\n第 8 天开始检验 · 当前稳定 {Trial.StableDays}/2 天\n每天恢复 8 点 · 全球天气消耗 35 点";
        _trialNotice.Text = Trial.Notice; _trialChoices.Visible = !Trial.Running;
        _retryTrial.Visible = Trial.Kind >= 0 && _checkpoint.Length > 0; _leaveTrial.Visible = Trial.Kind >= 0;
        int hungry = 0, thirsty = 0, sick = 0, homeless = 0; int target = -1; float urgency = -1;
        foreach (int slot in Sim.Agents.AliveSlots())
        {
            bool ill = Sim.Diseases.OfSlot(slot)?.Active == true; bool hunger = Sim.Agents.HungerOf(slot) > .7f;
            bool thirst = Sim.Agents.ThirstOf(slot) > .7f; bool home = Sim.Agents.DwellingOf(slot) < 0;
            if (ill) { sick++; } if (hunger) { hungry++; } if (thirst) { thirsty++; } if (home) { homeless++; }
            float priority = ill ? 4 : hunger ? 3 + Sim.Agents.HungerOf(slot) : thirst ? 2 + Sim.Agents.ThirstOf(slot) : home ? 1 : 0;
            if (priority > urgency)
            {
                urgency = priority; target = slot;
                _concernTool = ill ? PlayerTool.Heal : hunger ? PlayerTool.Food : thirst ? PlayerTool.River : home ? PlayerTool.Wood : PlayerTool.Forest;
            }
        }
        _concernLocation = target >= 0 ? Sim.Agents.PositionOf(target) : new Int2(Sim.World.Width / 2, Sim.World.Height / 2);
        int burning = 0;
        for (int i = 0; i < Sim.World.Tiles.Length; i++)
            if (Sim.World.Tiles[i].Fire == FireState.Burning)
            {
                if (burning == 0) { _concernLocation = new Int2(i % Sim.World.Width, i / Sim.World.Width); _concernTool = PlayerTool.Rain; }
                burning++;
            }
        string suggestion = sick > 0 ? "先救治病人；疫病会降低劳动能力。"
            : hungry > 0 ? "食物优先；也可开辟可达的采集地。"
            : thirsty > 0 ? "居民需要可抵达的水域；湿润土地不能直接解渴。"
            : homeless > 0 ? "木材、石料与建造地点决定能否安居。"
            : "居民暂时稳定。试试分隔资源，观察迁徙或贸易。";
        _concern.Text = $"饥饿 {hungry}   缺水 {thirsty}\n患病 {sick}   无居所 {homeless}" + (burning > 0 ? $"\n燃烧地块 {burning} · 优先扑救火情" : "") + $"\n\n{suggestion}";
        if (Trial.Running && Tool != PlayerTool.Inspect) { _toolBadge.Text = ToolNames[(int)Tool] + $" · 消耗 {WorldTrial.Cost(Tool, Radius, Strength)} / 余 {(int)Trial.Influence}"; }
    }
}
