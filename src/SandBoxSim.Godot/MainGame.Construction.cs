using Godot;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;
public partial class MainGame
{
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
        parent.AddChild(new HSeparator()); parent.AddChild(HudStyle.Heading("聚落营造", 21));
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10); parent.AddChild(grid);
        foreach (var kind in BuildingRegistry.Buildable)
        {
            var chosen = kind; var recipe = BuildingRegistry.Of(kind);
            var button = ActionButton(grid, "", () => PrepareConstruction(chosen));
            button.CustomMinimumSize = new Vector2(144, 151); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.TooltipText = recipe.DisplayName + " · 选择后单击地图安排工地";
            button.AddThemeStyleboxOverride("normal", HudStyle.Box(new Color("#272a27"), 2, 8));
            var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            content.OffsetLeft = content.OffsetTop = 8; content.OffsetRight = content.OffsetBottom = -8;
            content.AddThemeConstantOverride("separation", 3); button.AddChild(content);
            var portrait = new BuildingPortrait { Kind = kind }; content.AddChild(portrait);
            button.MouseEntered += () => portrait.Highlighted = true; button.MouseExited += () => portrait.Highlighted = false;
            var title = HudStyle.Heading(recipe.DisplayName, 17); title.HorizontalAlignment = HorizontalAlignment.Center; content.AddChild(title);
            var cost = HudStyle.Label($"木 {recipe.WoodCost:0}   石 {recipe.StoneCost:0}", 11, true); cost.HorizontalAlignment = HorizontalAlignment.Center; content.AddChild(cost);
        }
        var note = HudStyle.Label("你决定落点，居民材料支付。\n八格内需要可达的成年居民；住房靠水，农田邻水，矿场依赖矿区。", 12, true);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; parent.AddChild(note);
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
        _planDescription.Text = $"木材 {r.WoodCost:0}    石料 {r.StoneCost:0}\n\n八格内需有可达的成年居民，使用背包、仓库与地面物资。\n"
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
