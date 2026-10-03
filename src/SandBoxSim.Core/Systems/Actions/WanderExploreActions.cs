using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>
/// 漫游：没有更迫切需求时的默认行为。
///
/// 它的作用不只是"让人动起来"，而是提供**基线行为**：
/// 当世界变糟时（饿了、累了），漫游的效用会被惩罚项压下去，
/// 于是行为分布的变化本身就成了可观察信号（检查器里能直接看到）。
/// </summary>
internal static class WanderAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.Wander, w.BlockedUtilityMultiplier);

        // 疲劳是"负权重"，因此这里用"未疲劳"作为输入：越累越不想乱走。
        float restfulness = 1f - ctx.Store.FatigueOf(ctx.Slot);

        builder.Consider("基础闲逛倾向", 1f, UtilityCurve.Constant(w.WanderWeight), 1f);
        builder.Consider("精力（不累才想走）", restfulness, UtilityCurve.Linear, 0.6f);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        int radius = ctx.Ai.WanderRadius;
        if (ActionSearch.TryPickRandomReachable(in ctx, pathfinder, 2, radius, out Int2 target))
        {
            return target;
        }
        return null;
    }
}

/// <summary>
/// 探索：走得比漫游远得多，是"发现新地方"的雏形（M7 的迁移与建村都依赖它）。
///
/// 为什么需要它：如果所有个体只在出生点附近打转，
/// 世界就永远只有那一片区域被使用 —— 新聚落、贸易、冲突都不会有舞台。
/// 探索提供了"向外扩散"的动力，而离家的心理成本（HomeAttachment）保证扩散是渐进的。
/// </summary>
internal static class ExploreAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.Explore, w.BlockedUtilityMultiplier);

        float restfulness = 1f - ctx.Store.FatigueOf(ctx.Slot);
        float homeDistance = ActionSearch.HomeDistance01(in ctx);

        builder.Consider("基础探索倾向", 1f, UtilityCurve.Constant(w.ExploreWeight), 1f);
        builder.Consider("精力（不累才想走）", restfulness, UtilityCurve.Linear, 0.5f);

        // 离家越远越不想继续往外：这是"渐进扩散"而不是"一窝蜂出走"的关键。
        builder.Consider("离家距离（惩罚）", homeDistance, UtilityCurve.Quadratic, -w.HomeAttachmentWeight);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        int min = ctx.Ai.ExploreMinDistance;
        int max = ctx.Ai.ExploreMaxDistance;
        if (max < min) { max = min; }

        if (ActionSearch.TryPickRandomReachable(in ctx, pathfinder, min, max, out Int2 target))
        {
            return target;
        }

        // 远处不可达时退一步：不是所有人都能走出很远（被水/山围住的区域尤其如此）
        if (ActionSearch.TryPickRandomReachable(in ctx, pathfinder, 3, min, out Int2 nearer))
        {
            return nearer;
        }

        return null;
    }
}
