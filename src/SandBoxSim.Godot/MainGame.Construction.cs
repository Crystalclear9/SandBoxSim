using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private Label _blueprintState = null!;
    public void OpenConstruction()
    {
        ShowTools(false);
        ShowJournal(true); _drawer.CurrentTab = 5;
        _projectLibrary.Visible = _projectManagement.Visible = false; _constructionPanel.Visible = true;
        _libraryTab.SetPressedNoSignal(false); _managementTab.SetPressedNoSignal(false); _constructionTab.SetPressedNoSignal(true);
        RefreshOperations();
    }
    private void BuildConstructionChoices(VBoxContainer parent)
    {
        parent.AddChild(new HSeparator()); parent.AddChild(HudStyle.Label("聚落建造委托", 19));
        var grid = new GridContainer { Columns = 2 }; parent.AddChild(grid);
        foreach (var kind in BuildingRegistry.Buildable)
        {
            var chosen = kind; var recipe = BuildingRegistry.Of(kind);
            var button = ActionButton(grid, recipe.DisplayName + $"\n木 {recipe.WoodCost:0} · 石 {recipe.StoneCost:0}", () => PrepareConstruction(chosen));
            button.CustomMinimumSize = new Vector2(132, 58);
            button.AddThemeStyleboxOverride("normal", HudStyle.Box(HudStyle.Wash, 4, 10));
        }
        var note = HudStyle.Label("你决定落点，居民材料支付。\n八格内需要可达的成年居民；住房靠水，农田邻水，矿场依赖矿区。", 12, true);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; parent.AddChild(note);
        ActionButton(parent, "组合玩法：先路网，再建聚落 →", () =>
        {
            _status.Text = "先用道路留出通行线，再安排住房和仓库；在水边布置农田，外围湿地维护水土。";
            SetCategory("地貌"); SelectTool(PlayerTool.Road); ShowSettings(true);
        });
        parent.AddChild(new HSeparator()); parent.AddChild(HudStyle.Label("可选聚落蓝图", 19));
        _blueprintState = HudStyle.Label("", 12, true); _blueprintState.AutowrapMode = TextServer.AutowrapMode.WordSmart; parent.AddChild(_blueprintState);
        for (int i = 0; i < SettlementBlueprint.Names.Length; i++)
        {
            int kind = i;
            ActionButton(parent, SettlementBlueprint.Names[i] + " →", () =>
            {
                SelectTool(PlayerTool.Inspect); _planningBlueprint = kind; ShowSettings(false); RefreshOperations();
            });
            var brief = HudStyle.Label(SettlementBlueprint.Briefs[i], 11, true); brief.AutowrapMode = TextServer.AutowrapMode.WordSmart; parent.AddChild(brief);
        }
        ActionButton(parent, "停止蓝图追踪", () => { Blueprint.Leave(); RefreshOperations(); });
        ActionButton(parent, "前往蓝图中心 ↗", () => { if (Blueprint.Kind >= 0) FocusLocation(Blueprint.X, Blueprint.Y); });
    }
    private void RefreshBlueprint()
    {
        if (_blueprintState == null) return;
        if (Blueprint.Kind < 0) { _blueprintState.Text = "选择一种生活方式，单击地图确定中心。\n目标可随时更换，已有建设保留。"; return; }
        var state = Blueprint.Observe(Sim);
        _blueprintState.Text = SettlementBlueprint.Names[Blueprint.Kind] + (Blueprint.Completed ? " · 已达成" : $" · 稳定 {Blueprint.StableDays}/2 天")
            + $"\n中心 ({Blueprint.X}, {Blueprint.Y}) · 半径 10\n住房 {state.Houses} · 仓库 {state.Stores} · 农田 {state.Farms}\n居民 {state.People} · 道路 {state.Roads} · 森林 {state.Forest}\n" + SettlementBlueprint.Briefs[Blueprint.Kind];
    }
    private void PrepareConstruction(BuildingKind kind)
    {
        SelectTool(PlayerTool.Inspect); PlanningBuilding = kind;
        ShowSettings(false); RefreshOperations();
        _status.Text = "建造委托：单击落点；左键拖动平移，右键拖动旋转";
    }
    private void RefreshConstructionPreview()
    {
        var r = BuildingRegistry.Of(PlanningBuilding); _planTitle.Text = r.DisplayName + " · 建造委托";
        _planDescription.Text = $"材料 木 {r.WoodCost:0} / 石 {r.StoneCost:0}\n使用附近居民背包、仓库与地面物资。材料不足时不会扣料或留下工地。\n\n八格内需有可达的成年居民。\n"
            + (PlanningBuilding == BuildingKind.House ? "靠近水源，完成后提供真实床位。" : PlanningBuilding == BuildingKind.Storage ? "集中存放资源，为周边建造与交易提供库存。"
            : PlanningBuilding == BuildingKind.Farm ? "需要邻接水域；完工后仍需要农民劳动。" : "在允许的矿区落点；开采需要居民劳动。")
            + (Trial.Running ? "\n\n另消耗 15 点委托额度。" : "\n\n可连续安排多处落点，Esc 结束。") ;
        _toolBadge.Text = "委托 " + r.DisplayName + " · 单击落点";
    }
    private void CommitConstruction(int x, int y)
    {
        if (Trial.Running && Trial.Influence < 15) { _status.Text = "委托额度不足：需要 15 点"; return; }
        if (!ConstructionOrders.TryStart(Sim, PlanningBuilding, x, y, out int _, out string reason)) { _status.Text = reason; return; }
        Trial.TrySpendPoints(15); _map.Effect(x, y, HudStyle.Accent, "开工");
        _status.Text = BuildingRegistry.NameOf(PlanningBuilding) + "已经开工 · 可继续单击安排，Esc 结束";
        RefreshPanels();
    }
}
