using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>
/// 把物资存进仓库（M3）。
///
/// 与 M2 的"存放"（往地上堆）是**同一件事的两个层次**：
/// 地上堆是"临时共用"，仓库是"公共财产"。
/// 有了仓库之后，个体应当优先往仓库放 —— 这正是"聚落形成"在数据上的第一个迹象。
///
/// 实现上它和 Deposit 的区别只有两点：
///   1. 目标是**建筑**而不是空地（因此这里会失败于"附近没有仓库"）；
///   2. 门槛更高（仓库容量大，值得多跑一趟），因此它比地上堆更"正式"。
/// </summary>
internal static class StoreInBuildingAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        AiConfig w = ctx.Ai;
        BuildingConfig buildings = ctx.Config.Buildings;
        var builder = new ScoreBuilder(ActionKind.StoreInBuilding, w.BlockedUtilityMultiplier);

        bool hasStorage = BuildAction.TryFindStorageNear(in ctx, out int storageIndex, out int _);

        // 有仓库才谈得上"存进仓库"：这是真正的**前置条件**（不是加分），
        // 因为"附近没有仓库"和"附近有仓库但我不想存"是两件事。
        builder.Consider("附近有仓库", hasStorage ? 1f : 0f, UtilityCurve.Threshold(0.5f), 1.0f);

        float carried = ctx.Store.InventoryTotalOf(ctx.Slot);
        float surplus = SimMath.Clamp01((carried - ctx.Config.GroundStocks.SurplusThreshold)
            / System.Math.Max(1f, ctx.Config.GroundStocks.SurplusThreshold));

        // 仓库容量还剩多少：快满了就别往里塞（这才叫"考虑现实"，而不是无脑搬运）
        float room = 1f;
        if (hasStorage && ctx.Storage != null)
        {
            float capacity = ctx.Storage.CapacityOf(storageIndex);
            float used = ctx.Storage.TotalOf(storageIndex);
            room = capacity <= 0f ? 0f : SimMath.Clamp01(1f - (used / capacity));
        }

        builder.Consider("随身物资过剩", surplus, UtilityCurve.Survival, w.DepositSurplusWeight * 1.1f);
        builder.Consider("仓库还有空间", room, UtilityCurve.Linear, w.DepositPileBonusWeight, isBonus: true);

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus * 0.5f, isBonus: true);

        _ = buildings;
        return builder.Build();
    }

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
    {
        if (ctx.Buildings == null || ctx.Storage == null) { return null; }
        if (ctx.Store.InventoryTotalOf(ctx.Slot) <= 0f) { return null; }

        if (!BuildAction.TryFindStorageNear(in ctx, out int storageIndex, out int _)) { return null; }

        Int2 position = ctx.Buildings.PositionOf(storageIndex);

        pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
        PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, position.X, position.Y, out Int2 _);
        return result.Success ? position : null;
    }
}
