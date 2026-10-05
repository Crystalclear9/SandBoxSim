using System;
using System.Linq;
using Godot;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private RichTextLabel _projectLog = null!;
    private Label _projectBudget = null!, _planTitle = null!, _planDescription = null!;
    private PanelContainer _planPanel = null!;
    private Godot.Button _pinButton = null!, _pulseButton = null!, _libraryTab = null!, _managementTab = null!;
    private LineEdit _residentName = null!;
    private TextureRect _planArt = null!;
    private VBoxContainer _projectLibrary = null!, _projectManagement = null!;
    private VBoxContainer _constructionPanel = null!;
    private Godot.Button _constructionTab = null!;
    private VBoxContainer _landCards = null!;
    private readonly System.Collections.Generic.Dictionary<int, LandCard> _landViews = new();
    private GridContainer _recipeGrid = null!;
    private ProjectCatalog? _displayCatalog;
    private Texture2D? _projectAtlas;
    private readonly System.Collections.Generic.Dictionary<int, Godot.Button> _scopeButtons = new();
    private static ProjectCatalog LoadProjectCatalog()
    {
        try { return ProjectCatalog.Parse(Godot.FileAccess.GetFileAsString("res://assets/gameplay/projects.json")); }
        catch (Exception ex) { GD.PushWarning("工程配方加载失败，使用默认配方：" + ex.Message); return ProjectCatalog.Default; }
    }
    private Texture2D ProjectArt(int art)
    {
        _projectAtlas ??= GD.Load<Texture2D>("res://assets/illustrations/ecology-projects-v1.png");
        Vector2 cell = _projectAtlas.GetSize() / 2;
        return new AtlasTexture { Atlas = _projectAtlas, Region = new Rect2(new Vector2(art % 2, art / 2) * cell, cell) };
    }
    private void BuildWorldPulse(Control overlay)
    {
        _pulseButton = new Godot.Button { Text = "现场 · 走进世界 →", Alignment = HorizontalAlignment.Left };
        HudStyle.Button(_pulseButton); _pulseButton.AddThemeStyleboxOverride("normal", HudStyle.Box(HudStyle.Surface, 3, 14));
        HudStyle.Float(_pulseButton, new Vector2(1, 0), new Vector2(-360, 104), new Vector2(344, 48)); overlay.AddChild(_pulseButton);
        _pulseButton.Pressed += () => { ShowJournal(true); _drawer.CurrentTab = 0; };
    }
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
    public void OpenProjects() { ShowJournal(true); _drawer.CurrentTab = 5; SwitchProjectView(false); RefreshOperations(); }
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
        if (kind < 0 || kind >= Projects.Catalog.Recipes.Count) { return; }
        SelectTool(PlayerTool.Inspect); PlanningKind = kind; _radius.Value = 5;
        ShowSettings(false); _map.Focus(_concernLocation.X, _concernLocation.Y, 15);
        _status.Text = "选择范围，再点击地图落点 · 工程完成后可持续管理这片土地"; RefreshOperations();
    }
    private void CommitProject(int x, int y)
    {
        int kind = PlanningKind, radius = Math.Clamp(Radius, 2, 8);
        if (!Projects.CanQueue(Sim, kind, x, y, radius)) { _status.Text = Projects.ActiveCount >= 4 ? "最多同时推进四个工程" : "这片范围没有适合工程的土地；试试另一处空地"; return; }
        if (!Trial.TrySpendPoints(Projects.Recipe(kind).Cost(radius))) { _status.Text = Trial.Notice; return; }
        var plan = Projects.Queue(Sim, kind, x, y, radius); if (plan == null) { return; }
        _map.Effect(x, y, new Color("#9dcec0"), Projects.Recipe(kind).Name);
        PlanningKind = -1; _status.Text = Projects.Recipe(kind).Name + "已开始 · 在土地管理页查看进度";
        SwitchProjectView(true); RefreshOperations();
    }
    private void BuildProjectPanel()
    {
        var scroll = new ScrollContainer { Name = "工程", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _drawer.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); scroll.AddChild(body);
        body.AddChild(HudStyle.Label("LANDSCAPE  /  生态工坊", 10, true));
        body.AddChild(HudStyle.Label("经营这片土地", 25));
        _projectBudget = HudStyle.Label("", 12, true); _projectBudget.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_projectBudget);
        var tabs = new HBoxContainer(); body.AddChild(tabs);
        _libraryTab = ActionButton(tabs, "工程图册", () => SwitchProjectView(false)); _libraryTab.ToggleMode = true;
        _managementTab = ActionButton(tabs, "土地管理", () => SwitchProjectView(true)); _managementTab.ToggleMode = true;
        _constructionTab = ActionButton(tabs, "聚落营造", OpenConstruction); _constructionTab.ToggleMode = true;
        _projectLibrary = new VBoxContainer(); _projectLibrary.AddThemeConstantOverride("separation", 10); body.AddChild(_projectLibrary);
        _recipeGrid = new GridContainer { Columns = 2 }; _recipeGrid.AddThemeConstantOverride("h_separation", 8); _recipeGrid.AddThemeConstantOverride("v_separation", 10); _projectLibrary.AddChild(_recipeGrid);
        var note = HudStyle.Label("先改善水土，再选择生产方式。\n插画展示工程主题；地形与结果由真实模拟决定。", 12, true);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; _projectLibrary.AddChild(note);
        _constructionPanel = new VBoxContainer(); _constructionPanel.AddThemeConstantOverride("separation", 10); body.AddChild(_constructionPanel);
        BuildConstructionChoices(_constructionPanel);
        _projectManagement = new VBoxContainer(); _projectManagement.AddThemeConstantOverride("separation", 10); body.AddChild(_projectManagement);
        _projectLog = TextPanel("土地管理"); _projectLog.Visible = false; _projectManagement.AddChild(_projectLog);
        _landCards = new VBoxContainer(); _landCards.AddThemeConstantOverride("separation", 12); _projectManagement.AddChild(_landCards);
        _projectLog.MetaClicked += meta =>
        {
            string[] parts = meta.AsString().Split(':');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int id)) { return; }
            var plan = Projects.Items.FirstOrDefault(p => p.Id == id); if (plan == null) { return; }
            if (parts[0] == "cancel") { Projects.Cancel(Sim, id); }
            else if (parts[0] == "policy" && parts.Length == 3 && int.TryParse(parts[2], out int policy))
                { if (!Projects.SetPolicy(Sim, id, policy)) { _status.Text = "最多管理四片土地，或这项工程尚未完工"; } }
            else { FocusLocation(plan.X, plan.Y); }
            RefreshOperations();
        };
        SwitchProjectView(false);
    }
    private void RebuildRecipeCards()
    {
        _displayCatalog = Projects.Catalog;
        foreach (var view in _landViews.Values) { view.Panel.QueueFree(); }
        _landViews.Clear();
        foreach (Node child in _recipeGrid.GetChildren()) { _recipeGrid.RemoveChild(child); child.QueueFree(); }
        for (int i = 0; i < Projects.Catalog.Recipes.Count; i++)
        {
            int kind = i; var recipe = Projects.Recipe(kind);
            var button = new Godot.Button { CustomMinimumSize = new Vector2(132, 120), TooltipText = recipe.Brief,
                SizeFlagsHorizontal = SizeFlags.ExpandFill };
            HudStyle.Button(button); button.AddThemeStyleboxOverride("normal", HudStyle.Box(HudStyle.Wash, 4, 5, false));
            _recipeGrid.AddChild(button); button.Pressed += () => PrepareProject(kind);
            var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            content.OffsetLeft = content.OffsetTop = 5; content.OffsetRight = content.OffsetBottom = -5;
            content.AddThemeConstantOverride("separation", 4); button.AddChild(content);
            content.AddChild(new TextureRect { Texture = ProjectArt(recipe.Art), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(0, 76), MouseFilter = MouseFilterEnum.Ignore });
            var title = HudStyle.Label(recipe.Name, 14); title.MouseFilter = MouseFilterEnum.Ignore; content.AddChild(title);
            var time = HudStyle.Label(recipe.Steps.Length + " 天建立  ·  可持续管理", 10, true); time.MouseFilter = MouseFilterEnum.Ignore; content.AddChild(time);
        }
    }
    private void SwitchProjectView(bool management)
    {
        _projectLibrary.Visible = !management; _projectManagement.Visible = management;
        _constructionPanel.Visible = false; _constructionTab.SetPressedNoSignal(false);
        _libraryTab.SetPressedNoSignal(!management); _managementTab.SetPressedNoSignal(management);
    }
    private void BuildPlanningCard(Control overlay)
    {
        _planPanel = Surface(overlay, Vector2.Zero, new Vector2(16, 104), new Vector2(280, 425), 16); _planPanel.Visible = false;
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 10); _planPanel.AddChild(body);
        _planArt = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(0, 128) }; body.AddChild(_planArt);
        _planTitle = HudStyle.Label("", 24); body.AddChild(_planTitle);
        _planDescription = HudStyle.Label("", 13, true); _planDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_planDescription);
        var scope = new HBoxContainer(); body.AddChild(scope);
        foreach (int radius in new[] { 3, 5, 8 })
        {
            int chosen = radius; var button = ActionButton(scope, radius == 3 ? "小片 · 3" : radius == 5 ? "标准 · 5" : "广域 · 8", () => { _radius.Value = chosen; RefreshOperations(); });
            button.ToggleMode = true; _scopeButtons[radius] = button;
        }
        ActionButton(body, "取消放置  Esc", () => SelectTool(PlayerTool.Inspect));
    }
    private void RefreshOperations()
    {
        if (_projectLog == null) { return; }
        if (!ReferenceEquals(_displayCatalog, Projects.Catalog)) { RebuildRecipeCards(); }
        _projectBudget.Text = (Trial.Running ? $"额度 {(int)Trial.Influence} · 维护也会消耗额度" : "自由沙盒 · 干预不限额度")
            + $"\n在建 {Projects.ActiveCount}/4   ·   管理 {Projects.ManagedCount}/4";
        var text = new System.Text.StringBuilder();
        foreach (var p in Projects.Items.Reverse())
        {
            var recipe = Projects.Recipe(p.Kind);
            text.AppendLine("[color=#386653][b]" + recipe.Name + "[/b][/color]  " + (p.Cancelled ? "已停止" : p.Active ? $"第 {p.Stage}/{p.Duration} 阶段" : LandProjects.Policies[p.Policy]))
                .AppendLine($"[url=focus:{p.Id}]前往 ({p.X}, {p.Y}) →[/url]");
            if (p.Active)
            {
                text.AppendLine($"还有 {p.Duration - p.Stage} 个游戏天 · 半径 {p.Radius}")
                    .AppendLine($"[url=cancel:{p.Id}][color=#697367]停止剩余工程[/color][/url]");
            }
            else
            {
                if (!p.Cancelled)
                {
                    for (int policy = 0; policy < 3; policy++)
                        { text.Append($"[url=policy:{p.Id}:{policy}]" + (p.Policy == policy ? "[b]" : "") + LandProjects.Policies[policy] + (p.Policy == policy ? "[/b]" : "") + "[/url]  "); }
                    text.AppendLine().AppendLine("维护费用 " + recipe.Upkeep + " 点/天 · 条件不足或缺额度时暂停");
                    if (p.LastNotice.Length > 0) { text.AppendLine(p.LastNotice); }
                    var live = LocalConditions.Observe(Sim, p.X, p.Y, p.Radius);
                    text.AppendLine($"当前：粮食 {live.Food:0} · 湿度 {live.Moisture:P0} · 植被 {live.Vegetation:P0}");
                }
                text.AppendLine($"完工对照：粮食 {p.Before.Food:0} → {p.After.Food:0}")
                    .AppendLine($"湿度 {p.Before.Moisture:P0} → {p.After.Moisture:P0} · 火情 {p.Before.Burning} → {p.After.Burning}");
            }
            text.AppendLine();
        }
        _projectLog.Text = text.Length == 0 ? "[color=#697367]还没有土地工程。\n\n去图册选择一项改变，工程完成后再决定如何经营。[/color]" : text.ToString();
        RefreshLandCards();
        RefreshBlueprint();
        if (_planPanel != null)
        {
            _planPanel.Visible = (PlanningKind >= 0 || PlanningBuilding != BuildingKind.None || _planningBlueprint >= 0) && !_settingsOpen;
            _planPanel.OffsetBottom = _planPanel.OffsetTop + (PlanningKind >= 0 ? 425 : 320);
            _planArt.Visible = PlanningBuilding == BuildingKind.None && _planningBlueprint < 0;
            foreach (var pair in _scopeButtons) { pair.Value.Visible = PlanningBuilding == BuildingKind.None && _planningBlueprint < 0; }
            if (PlanningBuilding != BuildingKind.None) { RefreshConstructionPreview(); }
            if (_planningBlueprint >= 0)
            {
                _planTitle.Text = SettlementBlueprint.Names[_planningBlueprint];
                _planDescription.Text = SettlementBlueprint.Briefs[_planningBlueprint] + "\n\n单击确定中心，十格半径内统计真实人口、完工建筑与地块。连续两个日界达成。\n\n蓝图不会强制建造、移民或改变文明。左键拖动调整落点。";
                _toolBadge.Text = "蓝图中心 · 单击落点";
            }
            if (PlanningKind >= 0 && PlanningKind < Projects.Catalog.Recipes.Count)
            {
                var recipe = Projects.Recipe(PlanningKind); _planTitle.Text = recipe.Name; _planArt.Texture = ProjectArt(recipe.Art);
                _planDescription.Text = recipe.Brief + $"\n\n{recipe.Steps.Length} 天建立 · 半径 {Radius}\n" + (Trial.Running ? $"消耗 {recipe.Cost(Radius)} 点 · 管理 {recipe.Upkeep} 点/天" : "自由沙盒 · 不限额度");
                foreach (var pair in _scopeButtons) { pair.Value.SetPressedNoSignal(pair.Key == Radius); }
                _toolBadge.Text = "规划 " + recipe.Name + " · 点击地图确认落点";
            }
        }
        if (_pinButton != null) { _pinButton.Text = PinnedPerson != 0 && PinnedPerson == _selectedPersonId ? "取消关注" : "关注这个居民"; }
        if (_pulseButton != null)
        {
            var alerts = WorldAlerts.Observe(Sim);
            _pulseButton.Text = alerts.Count == 0 ? "现场  ·  暂时安稳，走进世界 →" : "现场  ·  " + alerts[0].Title + "  " + alerts[0].Count + "  →";
        }
    }
}
