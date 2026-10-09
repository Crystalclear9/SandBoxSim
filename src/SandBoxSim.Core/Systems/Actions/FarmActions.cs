using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>
/// 耕种（M4）：走到一块农田上干活，把"劳动量"记到那块田上。
///
/// # 为什么"耕种"必须是一个独立动作
///
/// 因为任务书要的因果链是 **人 → 耕种 → 食物 → 人口**。
/// 如果农田只是"建成之后自动产出"，那条链就断在中间：
/// 玩家看到的是"粮食自己变多"，而"有没有人下地"这件事没有后果。
///
/// 有了这个动作之后，三件事第一次成立：
///   1. **有人种与没人种的产量不同**（`FarmUnattendedFactor` vs 劳动加成）；
///   2. **劳动力会被别的需求抢走**（饥荒时大家都去找吃的，农田反而没人管 —— 这是真实的恶性循环）；
///   3. **玩家可以干预**（往里放人 / 用工具提高地力）。
///
/// # 与 `BuildFarm` 的区别（很容易混）
///
///   * `BuildFarm` 是**开垦**：一次性消耗木材与工时，把 Grass 变成 Farmland，产出一栋农田建筑；
///   * `Farm` 是**耕种**：每天重复的劳作，把劳动量记到已建成的农田上。
/// 前者是"造出条件"，后者是"持续投入"。把两者合成一个动作会让
/// "农田建好之后就再也不需要人" —— 那正是我们要避免的。
/// </summary>
internal static class FarmAction
{
    /// <summary>
    /// 效用：**由食物缺口驱动**，而不是"有田就去种"。
    ///
    /// 用缺口驱动有两条直接好处：
    ///   * 粮食充足时大家会去干别的事（建造、采集），不会一群人围着一块田空转；
    ///   * 粮食紧张时耕种会自然升到高优先级，形成"缺粮 → 更努力种地"的正反馈，
    ///     而人口下降又会让缺口缩小、优先级回落 ⇒ 系统自己能找到平衡点。
    /// </summary>
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        BuildingConfig cfg = ctx.Config.Buildings;
        var builder = new ScoreBuilder(ActionKind.Farm, w.BlockedUtilityMultiplier);

        // 儿童不做重体力劳动（M4 的生命阶段差异）。
        // 这是一道**门**而不是惩罚：孩子就是不该去种地，而不是"不太想去"。
        bool adult = ctx.Store.LifeStageOf(ctx.Slot) != LifeStage.Child;
        builder.Consider("成年劳动力", adult ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        // 有没有可耕种的田（已建成、且今天的劳动量还没满）
        bool canWork = TryFindFarm(in ctx, out int farmIndex, out float distance);
        builder.Consider("附近有农田", canWork ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        // 缺口驱动：食物越少越想种地
        float food = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);
        float personal = SimMath.Clamp01(1f - (food / System.Math.Max(1f, cfg.FarmBaseYieldPerDay * 2f)));
        builder.Consider("自己缺粮", personal, UtilityCurve.Survival, w.GatherFoodHungerWeight);
        int settlement = ctx.Society?.TerritoryAt(ctx.X, ctx.Y) ?? 0;
        if (settlement != 0 && ctx.Civilizations != null)
        {
            float price = ctx.Civilizations.Price(settlement, ResourceKind.Food);
            builder.Consider("本地粮价", SimMath.Clamp01(price / (ctx.Config.Trade.BaseFoodPrice * 8)),
                UtilityCurve.Linear, 0.3f, isBonus: true);
            builder.Consider("农民专业分工", ctx.Store.JobOf(ctx.Slot) == JobType.Farmer ? 1 : 0,
                UtilityCurve.Linear, 0.2f, isBonus: true);
        }

        // 田里还没人干活的紧迫度：劳动量越低越该去
        if (canWork)
        {
            if(cfg.LivingAgricultureEnabled)
            {
                var p=ctx.Buildings!.PositionOf(farmIndex);
                float cycle=LivingAgriculture.YieldFactor(LivingAgriculture.CropAt(ctx.World.Seed,p.X,p.Y),LivingAgriculture.Phase(ctx.World.Tick,ctx.World.Calendar.TicksPerDay));
                builder.Consider("当前作物产能",SimMath.Clamp01(cycle/1.65f),UtilityCurve.Linear,.6f);
            }
            float labor = ctx.Buildings?.LaborOf(farmIndex) ?? 0f;
            float idle = 1f - SimMath.Clamp01(labor / System.Math.Max(0.01f, cfg.FarmLaborPerDayCap));
            builder.Consider("田里缺人手", idle, UtilityCurve.Survival, 1.4f);

            float far = SimMath.Clamp01(distance / System.Math.Max(1f, (float)w.SearchRadius));
            builder.Consider("农田太远", far, UtilityCurve.Quadratic, -0.7f);
        }

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus, isBonus: true);

        return builder.Build();
    }

    /// <summary>选靶：视野内最近的、还没被干满的农田。</summary>
    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (!TryFindFarm(in ctx, out int farmIndex, out float _)) { return null; }

        Int2 position = ctx.Buildings!.PositionOf(farmIndex);
        if (position.X == ctx.X && position.Y == ctx.Y) { return position; }

        // 与建造同款：用一次寻路确认可达，避免"想耕地但走不过去"的个体一直卡着。
        // 耕种与建造一样是**低频**行为（每天最多几次），因此这一次 A* 是可以接受的。
        pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
        PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, position.X, position.Y, out Int2 _);
        return result.Success ? position : (Int2?)null;
    }

    /// <summary>
    /// 找最近的"可以干活"的农田：已建成、且当日劳动量还没到上限。
    ///
    /// 用**线性扫描**而不是空间索引：农田数量很少（几十个），
    /// 而"耕地"是每天每人评估若干次的动作 —— 与 M2 那条教训一致，
    /// 真正的开销从来不是"扫了几十个建筑"，而是"扫了上万个格子"。
    /// </summary>
    private static bool TryFindFarm(in ActionContext ctx, out int index, out float distance)
    {
        index = -1;
        distance = float.MaxValue;
        float bestCost=float.MaxValue;

        BuildingStore? buildings = ctx.Buildings;
        if (buildings == null) { return false; }

        float laborCap = System.Math.Max(0.01f, ctx.Config.Buildings.FarmLaborPerDayCap);
        int radius = ctx.Ai.SearchRadius;

        for (int k = 0; k < buildings.LiveCount; k++)
        {
            int candidate = buildings.LiveAt(k);
            if (!buildings.IsAlive(candidate)) { continue; }
            if (buildings.KindOf(candidate) != BuildingKind.Farm) { continue; }
            if (buildings.StateOf(candidate) != BuildingState.Complete) { continue; }
            if (buildings.LaborOf(candidate) >= laborCap) { continue; }

            float dx = buildings.XOf(candidate) - ctx.X;
            float dy = buildings.YOf(candidate) - ctx.Y;
            float d = (float)System.Math.Sqrt((dx * dx) + (dy * dy));
            if (d > radius) { continue; }
            float cycle=ctx.Config.Buildings.LivingAgricultureEnabled?LivingAgriculture.YieldFactor(LivingAgriculture.CropAt(ctx.World.Seed,buildings.XOf(candidate),buildings.YOf(candidate)),LivingAgriculture.Phase(ctx.World.Tick,ctx.World.Calendar.TicksPerDay)):1;
            float cost=d/System.Math.Max(.15f,cycle);
            if (cost < bestCost) { bestCost=cost;distance = d; index = candidate; }
        }

        return index >= 0;
    }
}
