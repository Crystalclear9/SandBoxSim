using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>
/// 狩猎（M2）：猎杀附近的野生动物换食物。
///
/// 为什么狩猎必须存在：只有采集的话，"森林"只是"可以砍的木材"。
/// 加上动物之后，森林同时是**猎场**，于是砍树有了生态代价（第 41 条）：
///
///     森林 ↓ → 动物栖息地 ↓ → 猎物 ↓ → 打猎收益 ↓ → 人的食物来源 ↓
///
/// 这条链是 M2 里最"涌现"的一段：玩家砍树的短期收益很明确，
/// 而代价要过很久才以"打不到猎了"的形式显现出来。
/// </summary>
internal static class HuntAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.Hunt, w.BlockedUtilityMultiplier);

        float hunger = ctx.Store.HungerOf(ctx.Slot);
        bool hasPrey = ctx.Wildlife != null && ctx.Wildlife.HasPreyNearby(ctx.X, ctx.Y, w.SearchRadius);

        // 背包里食物够多时就不打猎（与采集共享同一个"别囤积"的抑制项）
        float stock = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);
        float stockPenalty = SimMath.Clamp01(stock / 25f);

        builder.Consider("饥饿", hunger, UtilityCurve.Survival, w.HuntHungerWeight);
        builder.Consider("附近有猎物", hasPrey ? 1f : 0f, UtilityCurve.Linear, w.HuntAvailabilityWeight, isBonus: true);
        builder.Consider("背包里已有食物", stockPenalty, UtilityCurve.Quadratic, -0.9f);

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.Wildlife == null) { return null; }

        // 猎物本身的位置：直接选中它所在格。
        // 注意动物会移动，所以目标是"此刻快照" —— ActionSystem 在到达后会重新确认
        // 附近还有没有猎物（否则会站在一只已经跑掉的鹿的位置上发呆）。
        if (!ctx.Wildlife.TryFindNearest(ctx.X, ctx.Y, ctx.Ai.SearchRadius, out int index, out int _))
        {
            return null;
        }

        Int2 position = ctx.Wildlife.PositionOf(index);

        // 猎物会跑：如果它此刻站在不可走格上（比如刚好贴着水边），就放弃这次机会。
        // 这里刻意**不做可达性探测** —— 猎杀本来就是"可能扑空"的行为，
        // 而每次探测都是一次 A*，热路径上不能有这种开销。
        if (!ctx.World.TileAtClamped(position.X, position.Y).Walkable) { return null; }

        return position;
    }
}

/// <summary>
/// 存放物资（M2）：把随身多余的东西放到地面物资堆上。
///
/// 这是"共享库存"的雏形（M3 会把它变成 Storage 建筑）。
/// 它的观察价值在于：地图上会逐渐出现**一堆一堆的东西** ——
/// 玩家可以据此判断"这群人主要在哪里活动"。
///
/// 为什么需要"存放"这个动作：如果物资只存在个人口袋里，
/// 那么"谁富谁穷"完全不可见，而"物资集中"是聚落形成的前置条件（第 29 条）。
/// </summary>
internal static class DepositAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        GroundStockConfig stock = ctx.Config.GroundStocks;
        var builder = new ScoreBuilder(ActionKind.Deposit, w.BlockedUtilityMultiplier);

        // 触发条件是"随身物资超过携带舒适量"。
        //
        // 门槛必须显著**高于**个体平时携带的量，否则会出现"存放—取回"空转：
        // 实测门槛 14（而人均随身约 80）时，40 天里 229,855 次决策有 201,484 次选中"存放"，
        // 把 26 万单位物资搬进 4 个堆又搬回来 —— 统计上"经济极其活跃"，实际全是空转。
        //
        // 反方向的错误同样发生过：第一版门槛 25（看着合理）时，机制是完全的**死代码**
        // （存/取统计全为 0）。两个方向都不报错，只能靠看统计发现。
        float carried = ctx.Store.InventoryTotalOf(ctx.Slot);
        float surplus = SimMath.Clamp01((carried - stock.SurplusThreshold) / System.Math.Max(1f, stock.SurplusThreshold * 0.5f));

        bool pileNearby = ctx.GroundStocks != null
            && ctx.GroundStocks.TryFindNearby(ctx.X, ctx.Y, ResourceKind.Wood, stock.SearchRadius, out int _, out int _);

        builder.Consider("随身物资过剩", surplus, UtilityCurve.Survival, w.DepositSurplusWeight);

        // 附近已有物资堆 ⇒ 更倾向于往那里放（形成"物资集中"的正反馈萌芽）。
        // 这是 bonus：没有堆也应该可以就地放一堆，否则机制在初期永远不启动。
        builder.Consider("附近有物资堆", pileNearby ? 1f : 0f, UtilityCurve.Linear, w.DepositPileBonusWeight, isBonus: true);

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus * 0.5f, isBonus: true);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.GroundStocks == null) { return null; }

        GroundStockConfig stock = ctx.Config.GroundStocks;

        // 优先去已有的堆；没有的话就地建一个新堆（"就地堆放"是自然行为）
        for (int kind = (int)ResourceKind.Food; kind <= (int)ResourceKind.Iron; kind++)
        {
            ResourceKind resource = (ResourceKind)kind;
            if (ctx.Store.InventoryOf(ctx.Slot, resource) <= 0f) { continue; }

            if (ctx.GroundStocks.TryFindNearby(ctx.X, ctx.Y, resource, stock.SearchRadius, out int pile, out int _))
            {
                Int2 position = ctx.GroundStocks.PositionOf(pile);
                pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
                PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, position.X, position.Y, out Int2 _);
                if (result.Success) { return position; }
            }
        }

        // 没有现成的堆：就走几步放下（在附近找一个可走格）
        return ctx.X == ctx.Y && false ? null : new Int2(ctx.X, ctx.Y);
    }
}

/// <summary>
/// 取回物资（M2）：从地面物资堆里拿走自己缺的东西。
///
/// 与存放配对，形成"物资循环"：采 → 存 → 缺 → 取 → 用。
/// 这一步看着朴素，但它是 M3 共享仓库与 M8 贸易的**同一个机制**的最小版本 ——
/// 差别只在于"堆"是地上一堆东西，还是仓库/另一个聚落。
/// </summary>
internal static class TakeAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        GroundStockConfig stock = ctx.Config.GroundStocks;
        var builder = new ScoreBuilder(ActionKind.Take, w.BlockedUtilityMultiplier);

        bool hasPile = ctx.GroundStocks != null
            && ctx.GroundStocks.TryFindNearby(ctx.X, ctx.Y, ResourceKind.Food, stock.SearchRadius, out int _, out int _);

        // 只在"手上真的空了"时才去取。
        //
        // 门槛必须**远低于**存放门槛（见 DepositAction 的注释），否则两个动作会互相喂养：
        // 放下 ⇒ 变少 ⇒ 去取 ⇒ 变多 ⇒ 再放下。实测存放门槛 14 时，
        // "取回"被选中 20,650 次，与"存放"构成一个完全空转的循环。
        float food = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);
        float foodNeed = 1f - SimMath.Clamp01(food / System.Math.Max(1f, stock.TakeNeedThreshold));
        float hunger = ctx.Store.HungerOf(ctx.Slot);

        builder.Consider("手上食物不足", foodNeed, UtilityCurve.Survival, w.TakeNeedWeight);
        builder.Consider("饥饿驱动", hunger, UtilityCurve.Survival, 0.8f);
        builder.Consider("附近有物资堆", hasPile ? 1f : 0f, UtilityCurve.Linear, w.TakeAvailabilityWeight, isBonus: true);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.GroundStocks == null) { return null; }

        GroundStockConfig stock = ctx.Config.GroundStocks;

        // 取自己最缺的那种（食物优先，然后是木材 —— 与"想干什么活"对应）
        for (int kind = (int)ResourceKind.Food; kind <= (int)ResourceKind.Iron; kind++)
        {
            ResourceKind resource = (ResourceKind)kind;
            if (ctx.Store.InventoryOf(ctx.Slot, resource) >= stock.TakeAmount) { continue; }

            if (!ctx.GroundStocks.TryFindNearby(ctx.X, ctx.Y, resource, stock.SearchRadius, out int pile, out int _))
            {
                continue;
            }

            Int2 position = ctx.GroundStocks.PositionOf(pile);
            PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, position.X, position.Y, out Int2 _);
            if (result.Success) { return position; }
        }

        return null;
    }
}

/// <summary>
/// 迁往新住地（M2）。
///
/// 这个动作**不自己决定**要不要迁 —— 那是 <see cref="MigrationSystem"/> 的判断
/// （每天评估一次，考虑资源紧张、饥饿、拥挤、机会与依恋），
/// 并且它会把"意愿"写进 <c>AgentStore.IsMigrating</c>。
/// 动作层只做一件事：**在意愿还存在时**朝新的"家"走。
///
/// 这个"意图与执行分离"是必须的，不是洁癖。早期版本用
/// "离新家有多远"直接当效用，结果形成了一个自我强化的循环：
///
///     离家远 ⇒ 迁移效用高 ⇒ 选中迁移 ⇒ 继续朝新家走 ⇒ 离家更远（效用更高）
///
/// 于是实测 106,354 次决策里有 92,656 次（87%）都在"迁移"，
/// 采集、存放、狩猎全部被饿死 —— 世界看起来在正常运转，实际上什么经济都没发生。
/// 教训：**用"结果"（距离）当"意愿"的判据，会产生一个自我强化的循环。**
/// </summary>
internal static class MigrateAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        var builder = new ScoreBuilder(ActionKind.Migrate, ctx.Ai.BlockedUtilityMultiplier);

        // 没有迁移意愿 ⇒ 这个动作几乎不可用。
        // 这里用硬门（Threshold）而不是给一个小权重：小权重仍然会在"其它动作都低分"时被选中，
        // 从而表现为"没想搬家却一直在往远处走"。
        bool willing = ctx.Store.IsMigrating(ctx.Slot, ctx.Tick);

        builder.Consider("有迁移意愿", willing ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        double distanceFromHome = Int2.Distance(new Int2(ctx.X, ctx.Y), new Int2(ctx.HomeX, ctx.HomeY));
        float awayFromHome = SimMath.Clamp01((float)(distanceFromHome / System.Math.Max(1, ctx.Config.Migration.HomeRegionRadius)));

        // 离家越远，越该把这段路走完。这是"执行阶段"的强度，不是"意愿"本身。
        builder.Consider("离新家还有多远", awayFromHome, UtilityCurve.Survival, 0.9f);
        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (!ctx.Store.IsMigrating(ctx.Slot, ctx.Tick)) { return null; }

        Int2 home = new Int2(ctx.HomeX, ctx.HomeY);

        if (ctx.X == home.X && ctx.Y == home.Y) { return null; }

        // 先试"直接走向新家"：多数情况下这一步就成功。
        pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
        if (pathfinder.FindNextStep(ctx.X, ctx.Y, home.X, home.Y, out Int2 next).Success)
        {
            return next;
        }

        // 长距离目标常常"不可达"（中间隔着水/山），这并不意味着不该迁移。
        // 这时改为朝家的方向选一个**可达的中间点**：先走一段，再重新评估。
        // 这正是"迁移是慢过程"的具体体现 —— 玩家能看到一群人陆续往外挪。
        //
        // 注意尝试次数被刻意压到 4 次：每次 FindNextStep 都是一次完整的 A*。
        // 早期版本用"4 圈 × 6 次 = 最多 24 次"的穷举试探，在 40 个个体每天都评估时
        // 直接贡献了每天数千次寻路 —— 收益却只是"偶尔选到一个更远的中间点"。
        // **在热路径上做穷举式试探，代价通常远大于收益。**
        for (int attempt = 0; attempt < 4; attempt++)
        {
            float t = 0.25f * (attempt + 1);
            int stepX = ctx.X + (int)System.Math.Round((home.X - ctx.X) * t);
            int stepY = ctx.Y + (int)System.Math.Round((home.Y - ctx.Y) * t);

            if (!ctx.World.IsInBounds(stepX, stepY)) { continue; }
            if (!ctx.World.TileAt(stepX, stepY).Walkable) { continue; }
            pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
            if (!pathfinder.FindNextStep(ctx.X, ctx.Y, stepX, stepY, out Int2 _).Success) { continue; }

            return new Int2(stepX, stepY);
        }

        return null;
    }
}
