using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>
/// M6 的四个"人与人之间"的动作：社交、分享、逃跑、攻击。
///
/// # 这一组动作与前面所有动作的根本区别
///
/// 在前面四个里程碑里，每个动作都是"个体对世界"：吃地里的食物、砍树、盖房。
/// 这一组是"个体对个体"，于是它们第一次需要回答**"我和他是什么关系"**。
///
/// 这也是 M6 的核心价值：任务书第 55 / 60 条要的不是"多几个动作"，
/// 而是**关系本身成为一个可以被观察、被影响的量**。
/// 一个村子里的仇人不会凭空消失，一次分享会在几十年后仍然影响两个人怎么对待彼此。
///
/// # 性格在这里的作用方式
///
/// 性格**不改变动作能不能做**（那是门），而是改变**有多想做**（那是权重）。
/// 这个区分在 M4 已经付过一次代价：健康一度被误做成"配对资格"，
/// 于是某人一旦虚弱，伴侣关系就被清理规则拆掉，出生数恒为 0。
/// **把因子误做成门的症状是"某个机制完全失效"，而不是"数值偏小"。**
///
/// 因此这里：`Kindness` 只加权"分享"、`Aggression` 只加权"攻击"、
/// `Sociability` 只加权"社交"、`Bravery` 以补值形式加权"逃跑"。
/// 极端性格会让某些人**明显更常**做某件事，但不会让任何人**完全不能**做。
/// </summary>
internal static class SocialActions
{
    /// <summary>
    /// 找最近的另一个个体。
    ///
    /// 用 `LiveSlotsRaw`（槽位升序）而不是空间索引：人口规模在这个游戏里
    /// 最多几百，而"找最近的邻居"只发生在**决策时刻**（已被分批摊薄）。
    /// 用升序数组还有一个更重要的好处：**结果与遍历顺序无关**，
    /// 平局时取槽位较小者，因此确定性是构造出来的、不是碰巧的。
    /// </summary>
    internal static bool TryFindNearestAgent(
        in ActionContext ctx, float radius, int excludeSlot, out int found, out float distance)
    {
        found = -1;
        distance = float.MaxValue;

        float radiusSq = radius * radius;
        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);

        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == excludeSlot) { continue; }

            float dx = ctx.Store.XOf(other) - ctx.X;
            float dy = ctx.Store.YOf(other) - ctx.Y;
            float distSq = (dx * dx) + (dy * dy);
            if (distSq > radiusSq) { continue; }
            if (distSq >= distance * distance) { continue; }

            distance = System.MathF.Sqrt(distSq);
            found = other;
        }

        return found >= 0;
    }

    /// <summary>把目标设为某个个体所在的位置（移动过去）。</summary>
    internal static Int2? TargetOf(in ActionContext ctx, AStarPathfinder pathfinder, int targetSlot)
    {
        if (targetSlot < 0) { return null; }
        int tx = ctx.Store.XOf(targetSlot);
        int ty = ctx.Store.YOf(targetSlot);

        pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
        PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, tx, ty, out Int2 _);

        // 与目标相邻也算"到了" —— 否则两个人会争抢同一格，
        // 表现为"贴在一起却永远到不了"（实测过：社交动作成功率几乎为 0）。
        if (result.Success) { return new Int2(tx, ty); }
        if (System.Math.Max(System.Math.Abs(tx - ctx.X), System.Math.Abs(ty - ctx.Y)) <= 1)
        {
            return new Int2(tx, ty);
        }
        return null;
    }
}

/// <summary>
/// 社交（M6）：和附近的人待一会儿。
///
/// 驱动力是**孤独**（`social` 需求低）而不是"想聊天"：
/// 一个刚和别人待过的人不会立刻再去找人，否则会出现两个人
/// 面对面无限社交的空转（与 M2 的"存放—取回"同一类问题）。
/// </summary>
internal static class SocializeAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        var builder = new ScoreBuilder(ActionKind.Socialize, w.BlockedUtilityMultiplier);

        if (ctx.Relationships == null) { return builder.Build(); }

        bool hasCompany = SocialActions.TryFindNearestAgent(
            in ctx, ctx.Config.Relationship.SocializeRadius, ctx.Slot, out int _, out float distance);

        // 门：附近得有人，否则这个动作无处可做。
        builder.Consider("附近有人", hasCompany ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        // 孤独程度：社交需求越低越想社交。
        float loneliness = 1f - SimMath.Clamp01(ctx.Store.SocialOf(ctx.Slot));
        builder.Consider("孤独", loneliness, UtilityCurve.Survival, w.SocializeWeight);

        // 性格是**加成**：社交性强的人更常去社交，但孤独仍然是主要驱动。
        float sociability = ctx.Store.PersonalityOf(ctx.Slot).Sociability;
        builder.ConsiderScore("社交性格", sociability, sociability, w.SociabilityBonus, isBonus: true);

        // 远了就别去了（走半张地图只为聊一句不划算）
        if (hasCompany)
        {
            float proximity = SimMath.Clamp01(1f - (distance / System.Math.Max(1f, ctx.Config.Relationship.SocializeRadius)));
            builder.Consider("距离", proximity, UtilityCurve.Linear, w.SocializeDistanceWeight, isBonus: true);
        }

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.Relationships == null) { return null; }
        if (!SocialActions.TryFindNearestAgent(
                in ctx, ctx.Config.Relationship.SocializeRadius, ctx.Slot, out int other, out float _))
        {
            return null;
        }
        return SocialActions.TargetOf(in ctx, pathfinder, other);
    }
}

/// <summary>
/// 分享食物（M6）：把随身食物分一部分给附近最饿的人。
///
/// 这个动作的意义在于它是**第一个有真实代价的利他行为**：
/// 分享者真的会少一份食物。因此它必须由两条相反的性格拉扯出来 ——
/// `Kindness` 提高意愿、`Greed` 压低意愿 ——
/// 而不是"所有人在食物多的时候都会分"。
/// </summary>
internal static class ShareFoodAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        RelationshipConfig rel = ctx.Config.Relationship;
        var builder = new ScoreBuilder(ActionKind.ShareFood, w.BlockedUtilityMultiplier);

        if (ctx.Relationships == null) { return builder.Build(); }

        float carried = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);

        // 门：自己得有余粮。这不是"小气"，是**没有东西可分**。
        bool hasSpare = carried > rel.ShareFoodAmount * 2f;
        builder.Consider("有余粮", hasSpare ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        // 找附近最饿的人
        int hungriest = -1;
        float worstHunger = 0f;
        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == ctx.Slot) { continue; }

            float dx = ctx.Store.XOf(other) - ctx.X;
            float dy = ctx.Store.YOf(other) - ctx.Y;
            if ((dx * dx) + (dy * dy) > rel.SocializeRadius * rel.SocializeRadius) { continue; }

            float hunger = ctx.Store.HungerOf(other);
            if (hunger <= worstHunger) { continue; }

            worstHunger = hunger;
            hungriest = other;
        }

        // 门：附近得有人真的需要。否则"分享"会变成把食物扔给不饿的人。
        builder.Consider("有人需要", hungriest >= 0 ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        // 对方有多饿
        builder.Consider("对方饥饿", worstHunger, UtilityCurve.Survival, w.ShareFoodNeedWeight);

        // 性格拉扯：善良提高、贪婪压低。注意贪婪不是"门"，而是**负权重**，
        // 于是"又善良又贪婪"的人会表现出"犹豫"—— 这正是我们希望观察到的。
        Personality personality = ctx.Store.PersonalityOf(ctx.Slot);
        builder.ConsiderScore("善良", personality.Kindness, personality.Kindness, w.KindnessShareBonus, isBonus: true);

        float stinginess = 1f - SimMath.Clamp01(carried / System.Math.Max(1f, ctx.Config.Ai.InventoryComfort));
        builder.Consider("自留余量", stinginess, UtilityCurve.Linear, w.GreedSharePenalty);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.Relationships == null) { return null; }
        if (ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food) <= ctx.Config.Relationship.ShareFoodAmount * 2f)
        {
            return null;
        }

        RelationshipConfig rel = ctx.Config.Relationship;
        int hungriest = -1;
        float worstHunger = 0f;
        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == ctx.Slot) { continue; }

            float dx = ctx.Store.XOf(other) - ctx.X;
            float dy = ctx.Store.YOf(other) - ctx.Y;
            if ((dx * dx) + (dy * dy) > rel.SocializeRadius * rel.SocializeRadius) { continue; }

            float hunger = ctx.Store.HungerOf(other);
            if (hunger <= worstHunger) { continue; }

            worstHunger = hunger;
            hungriest = other;
        }

        return SocialActions.TargetOf(in ctx, pathfinder, hungriest);
    }
}

/// <summary>
/// 逃跑（M6）：附近有比自己更强且关系敌对的人时躲开。
///
/// 权重的主导项是 `1 − Bravery`：勇敢的人**更不容易**逃跑，
/// 但不是"勇敢的人不会逃跑" —— 当对方强得多时，勇敢的人也会走。
/// 这正是"性格是权重、不是门"的又一个具体体现。
/// </summary>
internal static class FleeAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        RelationshipConfig rel = ctx.Config.Relationship;
        var builder = new ScoreBuilder(ActionKind.Flee, w.BlockedUtilityMultiplier);

        if (ctx.Relationships == null) { return builder.Build(); }

        // 找附近最有威胁的人：敌意 × 对方侵略性
        float threat = 0f;
        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == ctx.Slot) { continue; }

            float dx = ctx.Store.XOf(other) - ctx.X;
            float dy = ctx.Store.YOf(other) - ctx.Y;
            if ((dx * dx) + (dy * dy) > 8f * 8f) { continue; }

            float affinity = ctx.Relationships.AffinityOf(ctx.Slot, other);
            if (affinity > rel.HostileAffinityThreshold) { continue; }

            float hostility = SimMath.Clamp01(-affinity);
            float theirAggression = ctx.Store.PersonalityOf(other).Aggression;
            float candidate = hostility * (0.5f + (0.5f * theirAggression));
            if (candidate > threat) { threat = candidate; }
        }

        // 门：没人构成威胁就没什么可逃的。
        builder.Consider("有威胁", threat > 0f ? 1f : 0f, UtilityCurve.Threshold(0.05f), 1.0f);

        if (threat <= 0f) { return builder.Build(); }

        builder.Consider("威胁程度", threat, UtilityCurve.Survival, w.FleeThreatWeight);

        // 勇敢压低逃跑意愿；健康低则提高（伤了就别硬撑）
        float bravery = ctx.Store.PersonalityOf(ctx.Slot).Bravery;
        builder.Consider("胆量", 1f - bravery, UtilityCurve.Linear, w.BraveryFleePenalty, isBonus: true);

        float frailty = 1f - ctx.Store.HealthOf(ctx.Slot);
        builder.Consider("伤势", frailty, UtilityCurve.Linear, w.FleeInjuryWeight, isBonus: true);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.Relationships == null) { return null; }

        // 找一个"远离威胁"的落脚点：在四个方向上取离威胁最远的一格。
        // 只试四个方向而不是全图搜索，是为了让代价与地图无关（也会更自然：
        // 慌不择路的人不会做全局最优规划）。
        RelationshipConfig rel = ctx.Config.Relationship;
        int threatSlot = -1;
        float bestThreat = 0f;

        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == ctx.Slot) { continue; }

            float dx = ctx.Store.XOf(other) - ctx.X;
            float dy = ctx.Store.YOf(other) - ctx.Y;
            if ((dx * dx) + (dy * dy) > 8f * 8f) { continue; }

            float affinity = ctx.Relationships.AffinityOf(ctx.Slot, other);
            if (affinity > rel.HostileAffinityThreshold) { continue; }

            float hostility = SimMath.Clamp01(-affinity) * (0.5f + (0.5f * ctx.Store.PersonalityOf(other).Aggression));
            if (hostility > bestThreat) { bestThreat = hostility; threatSlot = other; }
        }

        if (threatSlot < 0) { return null; }

        int threatX = ctx.Store.XOf(threatSlot);
        int threatY = ctx.Store.YOf(threatSlot);

        int stepX = ctx.X - threatX;
        int stepY = ctx.Y - threatY;
        stepX = stepX == 0 ? (ctx.Slot % 2 == 0 ? 1 : -1) : System.Math.Sign(stepX);
        stepY = stepY == 0 ? 0 : System.Math.Sign(stepY);

        // 按固定顺序试四个方向（确定性与方向偏好都固定）
        int[] orderX = new int[4];
        int[] orderY = new int[4];
        orderX[0] = stepX; orderY[0] = stepY;
        orderX[1] = stepX; orderY[1] = 0;
        orderX[2] = 0; orderY[2] = stepY;
        orderX[3] = -stepX; orderY[3] = 0;

        for (int i = 0; i < 4; i++)
        {
            int nx = ctx.X + (orderX[i] * 3);
            int ny = ctx.Y + (orderY[i] * 3);
            if (!ctx.World.IsInBounds(nx, ny)) { continue; }
            if (!ctx.World.TileAt(nx, ny).Walkable) { continue; }

            pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
            PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, nx, ny, out Int2 _);
            if (result.Success) { return new Int2(nx, ny); }
        }

        return null;
    }
}

/// <summary>
/// 攻击（M6）：对关系敌对的近邻动手。
///
/// **`Rules.PeaceMode` 在这里生效**：打开和平时攻击效用直接归零。
/// 这正是"规则开关"这种干预方式的样子 —— 它不是"减少攻击概率"，
/// 而是让这条行为通路不存在，于是世界会长成完全不同的样子。
/// </summary>
internal static class AttackAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        RelationshipConfig rel = ctx.Config.Relationship;
        var builder = new ScoreBuilder(ActionKind.Attack, w.BlockedUtilityMultiplier);

        if (ctx.Relationships == null) { return builder.Build(); }

        // 规则开关：和平模式下这条通路整体关闭。
        if (ctx.Config.Rules.PeaceMode) { return builder.Build(); }

        // 只有成年人才会动手（儿童阶段在 M4 已经存在）
        if (ctx.Store.LifeStageOf(ctx.Slot) == LifeStage.Child) { return builder.Build(); }

        int target = -1;
        float bestHostility = 0f;
        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == ctx.Slot) { continue; }
            if (ctx.Store.LifeStageOf(other) == LifeStage.Child) { continue; }

            float dx = ctx.Store.XOf(other) - ctx.X;
            float dy = ctx.Store.YOf(other) - ctx.Y;
            if ((dx * dx) + (dy * dy) > rel.AttackRange * rel.AttackRange) { continue; }

            float affinity = ctx.Relationships.AffinityOf(ctx.Slot, other);
            if (affinity > rel.HostileAffinityThreshold) { continue; }

            float hostility = SimMath.Clamp01(-affinity);
            if (hostility > bestHostility) { bestHostility = hostility; target = other; }
        }

        // 门：附近得有敌对的人。
        builder.Consider("有仇敌在附近", target >= 0 ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        if (target < 0) { return builder.Build(); }

        builder.Consider("敌意", bestHostility, UtilityCurve.Survival, w.AttackHostilityWeight);

        // 侵略性是加成：好斗的人更容易先动手
        float aggression = ctx.Store.PersonalityOf(ctx.Slot).Aggression;
        builder.ConsiderScore("侵略性格", aggression, aggression, w.AggressionAttackBonus, isBonus: true);

        // 自己越虚弱越不想打（这是"条件"而不是"性格"）
        builder.Consider("自身健康", ctx.Store.HealthOf(ctx.Slot), UtilityCurve.Linear, w.AttackHealthWeight, isBonus: true);

        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.Config.Rules.PeaceMode) { return null; }
        if (ctx.Relationships == null) { return null; }

        RelationshipConfig rel = ctx.Config.Relationship;
        int target = -1;
        float bestHostility = 0f;
        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            int other = slots[k];
            if (other == ctx.Slot) { continue; }
            if (ctx.Store.LifeStageOf(other) == LifeStage.Child) { continue; }

            float dx = ctx.Store.XOf(other) - ctx.X;
            float dy = ctx.Store.YOf(other) - ctx.Y;
            if ((dx * dx) + (dy * dy) > rel.AttackRange * rel.AttackRange) { continue; }

            float affinity = ctx.Relationships.AffinityOf(ctx.Slot, other);
            if (affinity > rel.HostileAffinityThreshold) { continue; }

            float hostility = SimMath.Clamp01(-affinity);
            if (hostility > bestHostility) { bestHostility = hostility; target = other; }
        }

        return SocialActions.TargetOf(in ctx, pathfinder, target);
    }
}
