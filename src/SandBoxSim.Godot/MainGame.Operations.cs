using System;
using System.Linq;
using Godot;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private RichTextLabel _projectLog = null!;
    private Label _projectBudget = null!, _planTitle = null!, _planDescription = null!;
    private PanelContainer _planPanel = null!;
    private Godot.Button _pinButton = null!;
    private LineEdit _residentName = null!;
    private void RenameResident()
    {
        var person = Sim.Society.Find(_selectedPersonId); string name = _residentName.Text.Trim();
        if (person == null || name.Length == 0) { _status.Text = "先选择居民，再填写名字"; return; }
        string previous = person.Name; person.Name = name;
        if (person.Alive) { Sim.Agents.SetNameOverride(person.Slot, name); }
        Sim.InterveneRecordAuxiliary("居民命名：" + previous + " → " + name); RefreshPanels(); _discovery.Refresh();
    }
    public void FocusLocation(int x, int y) { if (Sim.World.IsInBounds(x, y)) { _map.Focus(x, y, 20); } }
    public void RespondTo(WorldAlert alert, bool prepare)
    { FocusLocation(alert.Location.X, alert.Location.Y); if (prepare) { PrepareAid(alert.Tool, alert.Location); } }
    public void OpenProjects() { ShowJournal(true); _drawer.CurrentTab = 5; RefreshOperations(); }
    public void FocusPinned()
    {
        var person = Sim.Society.Find(PinnedPerson);
        if (person != null) { FocusStory(person.Id, person.Alive ? Sim.Agents.XOf(person.Slot) : -1, person.Alive ? Sim.Agents.YOf(person.Slot) : -1); }
    }
    private void TogglePin()
    {
        if (_selectedPersonId == 0) { _status.Text = "先选择一位居民，再关注他的故事"; return; }
        PinnedPerson = PinnedPerson == _selectedPersonId ? 0 : _selectedPersonId;
        RefreshOperations(); _discovery.Refresh();
    }
    private void PrepareProject(int kind)
    {
        SelectTool(PlayerTool.Inspect); PlanningKind = kind; _radius.Value = 5;
        ShowSettings(false); _map.Focus(_concernLocation.X, _concernLocation.Y, 15);
        _status.Text = "点击地图放置工程 · 三个游戏天分阶段完成 · Esc 取消放置";
        RefreshOperations();
    }
    private void CommitProject(int x, int y)
    {
        int kind = PlanningKind, radius = Math.Clamp(Radius, 2, 8);
        if (!Projects.CanQueue(Sim, kind, x, y, radius)) { _status.Text = Projects.ActiveCount >= 4 ? "最多同时推进四个工程" : "这片范围没有适合工程的土地；试试另一处空地"; return; }
        if (!Trial.TrySpendPoints(LandProjects.Cost(radius))) { _status.Text = Trial.Notice; return; }
        var plan = Projects.Queue(Sim, kind, x, y, radius);
        if (plan == null) { return; }
        _map.Effect(x, y, new Color("#9dcec0"), LandProjects.Names[kind]);
        PlanningKind = -1; _status.Text = LandProjects.Names[kind] + "已开始；在工程页查看进度与前后变化";
        RefreshOperations();
    }
    private void BuildProjectPanel()
    {
        var scroll = new ScrollContainer { Name = "工程", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _drawer.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 14); scroll.AddChild(body);
        body.AddChild(HudStyle.Label("改变一片土地", 24));
        var explanation = HudStyle.Label("即时救援争取时间，生态工程改变未来。\n选一项工程，再在世界中决定它的位置。", 13, true); body.AddChild(explanation);
        _projectBudget = HudStyle.Label("", 13); body.AddChild(_projectBudget);
        var choices = new GridContainer { Columns = 2 }; choices.AddThemeConstantOverride("h_separation", 8); choices.AddThemeConstantOverride("v_separation", 8); body.AddChild(choices);
        var icons = new[] { PlayerTool.Food, PlayerTool.Road, PlayerTool.Rain, PlayerTool.Forest };
        for (int i = 0; i < LandProjects.Names.Length; i++)
        {
            int kind = i;
            var button = new Godot.Button { Text = LandProjects.Names[i], Icon = ToolGlyphs.For(icons[i]), ExpandIcon = true,
                CustomMinimumSize = new Vector2(132, 68), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = LandProjects.Briefs[i] };
            button.AddThemeConstantOverride("icon_max_width", 27); HudStyle.Button(button);
            button.AddThemeStyleboxOverride("normal", HudStyle.Box(HudStyle.Wash, 3, 10, false));
            button.Pressed += () => PrepareProject(kind); choices.AddChild(button);
        }
        body.AddChild(HudStyle.Label("工程日志", 18)); _projectLog = TextPanel("工程日志"); _projectLog.CustomMinimumSize = new Vector2(0, 280); body.AddChild(_projectLog);
        _projectLog.MetaClicked += meta =>
        {
            string[] parts = meta.AsString().Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int id))
            {
                var plan = Projects.Items.FirstOrDefault(p => p.Id == id); if (plan == null) { return; }
                if (parts[0] == "cancel") { Projects.Cancel(Sim, id); RefreshOperations(); }
                else { FocusLocation(plan.X, plan.Y); }
            }
        };
    }
    private void BuildPlanningCard(Control overlay)
    {
        _planPanel = Surface(overlay, Vector2.Zero, new Vector2(16, 104), new Vector2(270, 230), 18); _planPanel.Visible = false;
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 10); _planPanel.AddChild(body);
        body.AddChild(HudStyle.Label("生态工程 · 放置预览", 11, true)); _planTitle = HudStyle.Label("", 23); body.AddChild(_planTitle);
        _planDescription = HudStyle.Label("", 14, true); _planDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_planDescription);
        ActionButton(body, "取消放置  Esc", () => { PlanningKind = -1; RefreshOperations(); });
    }
    private void RefreshOperations()
    {
        if (_projectLog == null) { return; }
        _projectBudget.Text = Trial.Running ? $"干预额度 {(int)Trial.Influence} · 标准工程消耗 34 点" : "自由沙盒 · 工程免费 · 同时最多四项";
        var text = new System.Text.StringBuilder();
        foreach (var p in Projects.Items.Reverse())
        {
            text.AppendLine("[color=#526f53][b]" + LandProjects.Names[p.Kind] + "[/b][/color]  " + (p.Cancelled ? "已停止" : p.Active ? $"第 {p.Stage}/3 阶段" : "已完成"))
                .AppendLine($"[url=focus:{p.Id}]前往 ({p.X}, {p.Y}) →[/url]");
            if (p.Active)
            {
                text.AppendLine($"还有 {3 - p.Stage} 个游戏天 · 半径 {p.Radius}")
                    .AppendLine($"[url=cancel:{p.Id}][color=#70776a]停止剩余工程（不撤销已生效部分）[/color][/url]");
            }
            else
            {
                text.AppendLine($"本地粮食 {p.Before.Food:0} → {p.After.Food:0} · 火情 {p.Before.Burning} → {p.After.Burning}")
                    .AppendLine($"湿度 {p.Before.Moisture:P0} → {p.After.Moisture:P0} · 植被 {p.Before.Vegetation:P0} → {p.After.Vegetation:P0}")
                    .AppendLine($"观察范围内饥饿居民 {p.Before.Hungry} → {p.After.Hungry}");
            }
            text.AppendLine();
        }
        _projectLog.Text = text.Length == 0 ? "[color=#70776a]还没有工程。\n\n用三天的准备，换一片土地的长期变化。工程不会直接为居民创建建筑。[/color]" : text.ToString();
        if (_planPanel != null)
        {
            _planPanel.Visible = PlanningKind >= 0 && !_settingsOpen;
            if (PlanningKind >= 0)
            {
                _planTitle.Text = LandProjects.Names[PlanningKind];
                _planDescription.Text = LandProjects.Briefs[PlanningKind] + "\n\n点击地图放置 · 半径 5\n" + (Trial.Running ? "消耗 34 点干预额度" : "自由沙盒 · 免费");
                _toolBadge.Text = "规划 " + LandProjects.Names[PlanningKind] + " · 点击地图放置";
            }
        }
        if (_pinButton != null) { _pinButton.Text = PinnedPerson != 0 && PinnedPerson == _selectedPersonId ? "取消关注" : "关注这个居民"; }
    }
}
