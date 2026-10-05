using System.Linq;
using Godot;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private sealed class LandCard
    {
        public PanelContainer Panel = null!;
        public Label Title = null!, State = null!, Conditions = null!;
        public ProgressBar Progress = null!;
        public HBoxContainer Policies = null!;
        public Godot.Button Cancel = null!, Detail = null!;
        public Godot.Button[] Choices = new Godot.Button[3];
        public RichTextLabel Report = null!;
    }
    private void RefreshLandCards()
    {
        foreach (int id in _landViews.Keys.ToArray())
            if (!Projects.Items.Any(p => p.Id == id)) { _landViews[id].Panel.QueueFree(); _landViews.Remove(id); }
        foreach (var plan in Projects.Items.Reverse())
        {
            if (!_landViews.TryGetValue(plan.Id, out var card)) { card = CreateLandCard(plan.Id); _landViews[plan.Id] = card; }
            var recipe = Projects.Recipe(plan.Kind);
            card.Title.Text = recipe.Name;
            card.State.Text = plan.Active ? $"第 {plan.Stage}/{plan.Duration} 阶段 · 还有 {plan.Duration - plan.Stage} 天"
                : plan.Cancelled ? "建设已停止" : LandProjects.Policies[plan.Policy] + (plan.Managed ? $" · {recipe.Upkeep} 点/天" : " · 无维护费用");
            card.Progress.Visible = plan.Active; card.Progress.MaxValue = plan.Duration; card.Progress.Value = plan.Stage;
            card.Cancel.Visible = plan.Active; card.Policies.Visible = !plan.Active && !plan.Cancelled;
            for (int i = 0; i < 3; i++) { card.Choices[i].SetPressedNoSignal(plan.Policy == i); }
            var current = LocalConditions.Observe(Sim, plan.X, plan.Y, plan.Radius);
            card.Conditions.Text = $"地表粮食  {current.Food:0}     湿度  {current.Moisture:P0}\n植被  {current.Vegetation:P0}     本地居民  {current.Residents}"
                + (plan.Managed ? "\n" + plan.LastNotice : "");
            card.Detail.Visible = !plan.Active;
            card.Report.Text = $"[color=#d0ae78]建设前 → 完工时[/color]\n粮食 {plan.Before.Food:0} → {plan.After.Food:0}\n湿度 {plan.Before.Moisture:P0} → {plan.After.Moisture:P0}\n植被 {plan.Before.Vegetation:P0} → {plan.After.Vegetation:P0}\n火情 {plan.Before.Burning} → {plan.After.Burning}\n[color=#9aa597]对照已冻结；上方显示当前区域状态。[/color]";
        }
        if (Projects.Items.Count == 0)
        {
            _projectLog.Visible = true; _projectLog.FitContent = true;
        }
        else { _projectLog.Visible = false; }
    }
    private LandCard CreateLandCard(int id)
    {
        var p = Projects.Items.First(p => p.Id == id); var recipe = Projects.Recipe(p.Kind);
        var view = new LandCard();
        view.Panel = new PanelContainer(); view.Panel.AddThemeStyleboxOverride("panel", HudStyle.Box(HudStyle.Wash, 4, 12, false)); _landCards.AddChild(view.Panel);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 9); view.Panel.AddChild(body);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 10); body.AddChild(header);
        header.AddChild(new TextureRect { Texture = ProjectArt(recipe.Art), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(52, 44), MouseFilter = MouseFilterEnum.Ignore });
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; names.AddThemeConstantOverride("separation", 3); header.AddChild(names);
        view.Title = HudStyle.Label("", 17); names.AddChild(view.Title); view.State = HudStyle.Label("", 11, true); names.AddChild(view.State);
        view.Progress = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5) };
        view.Progress.AddThemeStyleboxOverride("background", HudStyle.Box(HudStyle.Border, 1, 0, false));
        view.Progress.AddThemeStyleboxOverride("fill", HudStyle.Box(HudStyle.Accent, 1, 0, false)); body.AddChild(view.Progress);
        view.Policies = new HBoxContainer(); view.Policies.AddThemeConstantOverride("separation", 4); body.AddChild(view.Policies);
        for (int i = 0; i < 3; i++)
        {
            int policy = i; var choice = ActionButton(view.Policies, i == 0 ? "自然" : i == 1 ? "维护" : "采集", () =>
            {
                var current = Projects.Items.FirstOrDefault(p => p.Id == id);
                if (current == null || current.Policy == policy) { return; }
                if (!Projects.SetPolicy(Sim, id, policy)) { _status.Text = "最多同时管理四片土地"; }
                RefreshOperations();
            });
            choice.ToggleMode = true; choice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            choice.TooltipText = LandProjects.Policies[i] + (i == 0 ? "：停止额外干预，让世界继续变化" : i == 1 ? "：达到土壤条件才执行每日维护" : "：优先补充资源，消耗土壤与植被");
            view.Choices[i] = choice;
        }
        view.Conditions = HudStyle.Label("", 12, true); view.Conditions.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(view.Conditions);
        var actions = new HBoxContainer(); body.AddChild(actions);
        ActionButton(actions, "前往 ↗", () => FocusLocation(p.X, p.Y));
        view.Cancel = ActionButton(actions, "停止工程", () => { Projects.Cancel(Sim, id); RefreshOperations(); });
        view.Detail = ActionButton(actions, "完工对照", () => view.Report.Visible = !view.Report.Visible);
        view.Report = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Visible = false, SelectionEnabled = true }; body.AddChild(view.Report);
        return view;
    }
}
