using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>进食：消耗背包里的食物，把饥饿压回去。</summary>
internal static class EatAction
{
    /// <summary>
    /// 效用 = 饥饿驱动 × 有食物可吃。
    ///
    /// 两处刻意的设计：
    ///   1. 「手上有食物」用 Threshold(0.5) 做**硬门**：一粒粮食都没有时不该"假装吃饭"；
    ///   2. 饥饿曲线用 Quadratic：不太饿时几乎不想吃（避免高频无意义进食），
    ///      快饿死时优先级急剧上升 —— 这正是第 17 条要的"紧迫感"。
    /// </summary>
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.Eat, w.BlockedUtilityMultiplier);

        float hunger = ctx.Store.HungerOf(ctx.Slot);
        float food = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);
        float foodGate = SimMath.InverseLerp(0f, 1f, food);

        builder.Consider("饥饿", hunger, UtilityCurve.Survival, w.EatHungerWeight);
        builder.Consider("手上有食物", foodGate, UtilityCurve.Threshold(0.5f), w.EatFoodAvailableWeight);

        return builder.Build();
    }
}

/// <summary>饮水：走到临水格取水，把干渴压回去。</summary>
internal static class DrinkAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.Drink, w.BlockedUtilityMultiplier);

        float thirst = ctx.Store.ThirstOf(ctx.Slot);
        float water = ctx.Chunk.WaterTiles > 0 ? 1f : 0f;

        // 干渴用 Quadratic：缺水比缺粮致命，因此基础权重更高。
        builder.Consider("干渴", thirst, UtilityCurve.Survival, w.DrinkThirstWeight);
        builder.Consider("附近有水", water, UtilityCurve.Linear, 0.8f);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ActionSearch.TryFindWaterAccess(in ctx, out Int2 water))
        {
            return water;
        }
        return null;
    }
}

/// <summary>睡觉：原地恢复疲劳。</summary>
internal static class SleepAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.Sleep, w.BlockedUtilityMultiplier);

        float fatigue = ctx.Store.FatigueOf(ctx.Slot);

        builder.Consider("疲劳", fatigue, UtilityCurve.Survival, w.SleepFatigueWeight);

        // 夜里更困：这不是"设定"，而是让昼夜循环真正影响行为的最短路径。
        if (ctx.IsNight)
        {
            builder.ConsiderScore("夜间困意", 1f, 1f, w.SleepNightBonus);
        }

        return builder.Build();
    }
}

/// <summary>
/// 采集野生食物。
///
/// 这里有一个**必须解释清楚**的权重设计（它踩过一次坑）：
/// "饥饿"这一项的权重（<c>GatherFoodHungerWeight = 2.5</c>）必须**显著大于**
/// 木材/石料采集的可得性权重（≈1.4），否则会出现这样的荒谬结果：
/// 手上既没木也没石（"需求"=1）的人，采集木石的效用（≈0.39）会高于
/// 采集食物的效用，于是**一群人饿死前一直在砍柴**。
/// 也就是说：生存类动作必须在效用尺度上"说过"非生存类动作，这不是平衡问题，是正确性问题。
/// </summary>
internal static class GatherFoodAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.GatherFood, w.BlockedUtilityMultiplier);

        float hunger = ctx.Store.HungerOf(ctx.Slot);

        // 背包里的食物越多，越不需要去采（避免"病态囤积"）
        float stock = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);
        float stockPenalty = SimMath.Clamp01(stock / 25f);

        bool hasTarget = ActionSearch.TryFindResource(in ctx, ResourceKind.Food, out Int2 _);
        builder.Consider("饥饿", hunger, UtilityCurve.Survival, w.GatherFoodHungerWeight);
        builder.Consider("附近有食物", hasTarget ? 1f : 0f, UtilityCurve.Linear, w.GatherFoodAvailabilityWeight);
        builder.Consider("背包里已有食物", stockPenalty, UtilityCurve.Quadratic, -0.9f);

        if (ctx.Ai.IndustriousnessWorkBonus > 0f)
        {
            float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
            builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus);
        }

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ActionSearch.TryFindResource(in ctx, ResourceKind.Food, out Int2 target)) { return target; }
        return null;
    }
}

/// <summary>
/// 采伐木材。
///
/// "缺少木材"的输入用**"储量充足度"**（库存 / 参考量）而不是"需求"（1 − 参考量）：
/// 后者的反向项在库存为 0 时会给出"完全不缺"的分（1 − 0 = 1），语义正好搞反了。
/// 这类符号错误在效用系统里很难发现 —— 它不报错，只是行为悄悄反了。
/// </summary>
internal static class GatherWoodAction
{
    private const float ReferenceStock = 30f;

    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.GatherWood, w.BlockedUtilityMultiplier);

        float wood = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Wood);
        float stocked = SimMath.Clamp01(wood / ReferenceStock);

        bool hasTarget = ActionSearch.TryFindResource(in ctx, ResourceKind.Wood, out Int2 _);
        builder.Consider("木材储备充足（越足越不想砍）", stocked, UtilityCurve.Quadratic, -w.GatherWoodNeedWeight);
        builder.Consider("手上有木材基础需求", 1f, UtilityCurve.Constant(w.GatherWoodNeedWeight * 0.4f), 1f);
        builder.Consider("附近有森林", hasTarget ? 1f : 0f, UtilityCurve.Linear, w.GatherWoodAvailabilityWeight);

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ActionSearch.TryFindResource(in ctx, ResourceKind.Wood, out Int2 target)) { return target; }
        return null;
    }
}

/// <summary>开采石料。</summary>
internal static class GatherStoneAction
{
    private const float ReferenceStock = 20f;

    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.GatherStone, w.BlockedUtilityMultiplier);

        float stone = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Stone);
        float stocked = SimMath.Clamp01(stone / ReferenceStock);

        bool hasTarget = ActionSearch.TryFindResource(in ctx, ResourceKind.Stone, out Int2 _);
        builder.Consider("石料储备充足（越足越不想采）", stocked, UtilityCurve.Quadratic, -w.GatherStoneAvailabilityWeight);
        builder.Consider("手上有石料基础需求", 1f, UtilityCurve.Constant(w.GatherStoneAvailabilityWeight * 0.35f), 1f);
        builder.Consider("附近有石矿", hasTarget ? 1f : 0f, UtilityCurve.Linear, w.GatherStoneAvailabilityWeight);

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ActionSearch.TryFindResource(in ctx, ResourceKind.Stone, out Int2 target)) { return target; }
        return null;
    }
}
