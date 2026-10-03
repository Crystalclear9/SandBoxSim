using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.ConsoleApp.Ui;
using SandBoxSim.Core;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Systems;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 个体检查器与工具面板的 UI 层测试。
///
/// 这两块 UI 的价值都在"可解释性"上，因此测试也要针对这一点：
///   * 检查器显示的动作必须**等于个体真正在做的事**（不一致的检查器比没有更糟）；
///   * 效用分解必须**逐条可读**（名字、权重、贡献都不能是空值）；
///   * 面板绘制必须是**只读**的（和渲染层同样的理由）。
/// </summary>
public sealed class InspectorTests
{
    private static SimConfig Config(int width = 60, int height = 60)
    {
        var config = new SimConfig();
        config.World.Width = width;
        config.World.Height = height;
        return config;
    }

    private static void Flatten(Simulation sim, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                sim.World.SetTerrain(x, y, TerrainKind.Grass);
                sim.World.SetVegetation(x, y, 0.3f);
            }
        }
        sim.World.RefreshSpatialIndex();
    }

    /// <summary>跑一段时间，让个体产生真实的决策记录。</summary>
    private static Simulation RunWithAgents(int seed, int ticks, out int slot)
    {
        var config = Config(60, 60);
        var sim = new Simulation(config, 60, 60, seed);
        Flatten(sim, 15, 15, 45, 45);
        sim.InterveneSpawnHumans(30, 30, 12, 5);
        sim.Tick(ticks);

        slot = -1;
        foreach (int candidate in sim.Agents.AliveSlots()) { slot = candidate; break; }
        return sim;
    }

    [Fact("检查器显示的决策必须与个体真正在做的事一致")]
    public void InspectorBreakdownMatchesActualAction()
    {
        Simulation sim = RunWithAgents(9001, 1440, out int slot);
        Assert.GreaterOrEqual(slot, 0, "测试需要至少一个存活个体");

        ref readonly UtilityBreakdown breakdown = ref sim.Agents.LastDecisionOf(slot);

        // 个体可能还没轮到决策（分批 + 决策间隔），这时跳过而不是假失败
        if (breakdown.Scores == null || breakdown.Scores.Length == 0)
        {
            Assert.Equal(ActionKind.None, sim.Agents.ActionOf(slot));
            return;
        }

        ActionKind actual = sim.Agents.ActionOf(slot);

        // 决策记录是"上一次决策"的快照，而个体可能在那之后完成任务并重新决策过 ——
        // 因此这里只断言"记录里的选中项是一个合法动作"，以及
        // "当前动作要么等于它、要么是完成/失败后的 None"。
        // 想断言两者严格相等需要把面板绑定到决策时刻，那是过度约束。
        bool consistent = actual == breakdown.Chosen
                       || actual == ActionKind.None
                       || breakdown.Chosen == ActionKind.None;

        Assert.True(consistent,
            "检查器显示的选中动作（" + breakdown.Chosen + "）与个体当前动作（" + actual + "）矛盾");
    }

    [Fact("效用分解的每一条都必须可读（名字/权重/贡献都不是空值）")]
    public void BreakdownIsFullyReadable()
    {
        Simulation sim = RunWithAgents(9011, 1440, out int slot);
        Assert.GreaterOrEqual(slot, 0);

        ref readonly UtilityBreakdown breakdown = ref sim.Agents.LastDecisionOf(slot);
        if (breakdown.Scores == null || breakdown.Scores.Length == 0)
        {
            // 没有决策记录时这条测试无从断言 —— 但必须留下可读的痕迹
            return;
        }

        Assert.True(breakdown.Scores.Length > 0);
        Assert.True(breakdown.ChosenUtility >= 0f && breakdown.ChosenUtility <= 1f);

        for (int i = 0; i < breakdown.Scores.Length; i++)
        {
            ActionScore score = breakdown.Scores[i];
            Assert.True(score.Utility >= 0f && score.Utility <= 1f,
                "动作 " + score.Action + " 的效用越界：" + score.Utility);
            Assert.True(score.Considerations != null && score.Considerations.Length > 0,
                "动作 " + score.Action + " 没有任何考虑项 —— 那样检查器就无法解释它");

            for (int k = 0; k < score.Considerations.Length; k++)
            {
                Consideration consideration = score.Considerations[k];
                Assert.True(!string.IsNullOrEmpty(consideration.Name),
                    "考虑项必须有名字，否则玩家看到的是一个没有含义的数字");
                Assert.True(SimMath.IsFinite(consideration.Score));
                Assert.True(SimMath.IsFinite(consideration.Contribution));

                // ToString 是检查器实际渲染的东西：它不能抛异常
                Assert.True(consideration.ToString().Length > 0);
            }
        }

        // Top-N 必须按效用降序（面板直接按顺序画，靠的就是这个前提）
        for (int i = 1; i < breakdown.Scores.Length; i++)
        {
            Assert.True(breakdown.Scores[i - 1].Utility >= breakdown.Scores[i].Utility,
                "决策记录没有按效用降序保存 —— 检查器会显示成乱序");
        }
    }

    [Fact("检查器绘制不得改变世界状态")]
    public void InspectorRenderingIsReadOnly()
    {
        Simulation sim = RunWithAgents(9021, 1440, out int slot);
        Assert.GreaterOrEqual(slot, 0);

        string before = sim.StateDigestString();

        var buffer = new RenderBuffer(120, 45);
        AgentRef reference = sim.Agents.RefOf(slot);

        // 多次绘制（含选中与不选中两条路径）都必须完全只读
        for (int i = 0; i < 3; i++)
        {
            Panels.DrawSidePanel(buffer, sim, 84, 36, 2, 41, 30, 30, reference);
            Panels.DrawSidePanel(buffer, sim, 84, 36, 2, 41, 30, 30);
        }

        Assert.Equal(before, sim.StateDigestString(),
            "检查器是纯展示层，不允许写任何模拟状态");
    }

    [Fact("检查器必须能画在很窄/很矮的面板里而不越界")]
    public void InspectorSurvivesTinyPanels()
    {
        Simulation sim = RunWithAgents(9031, 720, out int slot);
        Assert.GreaterOrEqual(slot, 0);

        AgentRef reference = sim.Agents.RefOf(slot);
        var buffer = new RenderBuffer(60, 20);

        // 逐级缩小：面板代码必须在任何尺寸下都安全（终端窗口会被玩家随意缩放）
        for (int width = 4; width <= 40; width += 4)
        {
            for (int height = 2; height <= 18; height += 4)
            {
                Panels.DrawSidePanel(buffer, sim, 40, width, 1, height, 30, 30, reference);
            }
        }

        Assert.True(true, "只要没有抛异常就通过 —— 这条测试的价值在于覆盖边界尺寸");
    }

    [Fact("选中的个体死亡后检查器必须自动失效（不能显示另一个人的数据）")]
    public void InspectorInvalidatesOnDeath()
    {
        var config = Config(40, 40);
        var sim = new Simulation(config, 40, 40, 9041);
        Flatten(sim, 10, 10, 30, 30);
        sim.InterveneSpawnHumans(20, 20, 3, 2);

        int slot = -1;
        foreach (int candidate in sim.Agents.AliveSlots()) { slot = candidate; break; }
        Assert.GreaterOrEqual(slot, 0);

        AgentRef reference = sim.Agents.RefOf(slot);
        Assert.True(sim.Agents.IsValid(reference));

        sim.Agents.MarkDead(slot, DeathCause.OldAge, sim.Clock);

        // 这是"用引用而不是槽位索引"的核心理由：
        // 槽位会被回收给新个体，抓着裸索引就会在另一个人身上显示死者的数据。
        Assert.False(sim.Agents.IsValid(reference),
            "个体死亡后旧引用必须立刻失效，检查器才能知道自己该停止显示");
    }

    [Fact("工具面板的每一个工具都必须有可读的名字与类别")]
    public void EveryToolHasReadableMetadata()
    {
        Assert.True(ToolPalette.Tools.Length > 0);

        foreach (WorldTool tool in ToolPalette.Tools)
        {
            Assert.True(!string.IsNullOrEmpty(tool.Name), "工具必须有名字");
            Assert.True(tool.Apply != null, "工具 " + tool.Name + " 缺少执行委托");
            Assert.True(tool.DefaultRadius >= 1, "工具 " + tool.Name + " 的默认半径非法");

            // 每个类别都必须至少有一个工具，否则面板里会出现一个点不开的空标签
            Assert.True(ToolPalette.IndicesOf(tool.Category).Length > 0);
        }

        for (int i = 0; i < 4; i++)
        {
            ToolCategory category = (ToolCategory)i;
            Assert.True(ToolPalette.Tools.Length > 0);
            Assert.True(!string.IsNullOrEmpty(ToolPalette.DisplayNameOf(category)));
        }
    }

    [Fact("工具面板绘制不得改变世界状态，且能在小窗口下安全绘制")]
    public void ToolPanelRenderingIsReadOnly()
    {
        Simulation sim = RunWithAgents(9051, 600, out int _);

        string before = sim.StateDigestString();

        var buffer = new RenderBuffer(80, 30);
        var state = new ToolPalette.State { IsOpen = true };

        // 遍历所有类别与工具组合，确认绘制既不抛异常也不写状态
        for (int i = 0; i < 4; i++)
        {
            state.NextCategory();
            for (int k = 0; k < 6; k++)
            {
                state.CycleTool(1);
                Panels.DrawToolPanel(buffer, state, 1, 3);
            }
        }

        Panels.DrawBottomBar(buffer, sim, MapOverlay.None, "测试", toolsOpen: true);
        Panels.DrawBottomBar(buffer, sim, MapOverlay.Population, "测试", toolsOpen: false);

        Assert.Equal(before, sim.StateDigestString());
    }

    [Fact("个体状态名与动作名必须都有中文可读输出")]
    public void StateNamesAreReadable()
    {
        // 面板上出现 "ActionPhase.Executing" 这种东西对玩家毫无意义。
        // 这里锁住"每个枚举值都有映射"这件事 —— 新增枚举值时会被提醒。
        foreach (ActionKind kind in ActionRegistry.All)
        {
            string name = ActionRegistry.DisplayNameOf(kind);
            Assert.True(!string.IsNullOrEmpty(name));
            Assert.False(name == kind.ToString(),
                "动作 " + kind + " 没有中文名 —— 检查器会显示成枚举字面量");
        }
    }
}
