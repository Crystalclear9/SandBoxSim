using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

internal static class GatherIronAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
    {
        var score = new ScoreBuilder(ActionKind.GatherIron, ctx.Ai.BlockedUtilityMultiplier);
        bool available = ActionSearch.TryFindResource(in ctx, ResourceKind.Iron, out Int2 _);
        int settlement = ctx.Society?.TerritoryAt(ctx.X, ctx.Y) ?? 0;
        bool industrialDemand = settlement != 0 && ctx.Civilizations?.OfSettlement(settlement) != null;
        float inventory = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Iron);
        score.Consider("附近发现铁矿", available ? 1 : 0, UtilityCurve.Threshold(0.5f), 1);
        score.Consider("文明工具与武器需求", industrialDemand ? 1 : 0, UtilityCurve.Threshold(0.5f), 1);
        score.Consider("铁矿储备缺口", 1 - SimMath.Clamp01(inventory / 8), UtilityCurve.Linear, 0.5f);
        score.Consider("随身物资已够多", ActionSearch.Overstock01(in ctx), UtilityCurve.Survival, -ctx.Ai.GatherOverstockWeight);
        score.Consider("勤劳性格", ctx.Store.PersonalityOf(ctx.Slot).Industriousness, UtilityCurve.Linear,
            ctx.Ai.IndustriousnessWorkBonus, isBonus: true);
        if (settlement != 0 && ctx.Civilizations != null)
            score.Consider("本地铁矿价格", SimMath.Clamp01(ctx.Civilizations.Price(settlement, ResourceKind.Iron) / 12),
                UtilityCurve.Linear, 0.4f, isBonus: true);
        var built = score.Build();
        return industrialDemand && available ? built
            : new ActionScore(ActionKind.GatherIron, built.Considerations, 0, built.WeightedAverage, built.GeometricMean, true);
    }
    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder path)
    {
        int settlement = ctx.Society?.TerritoryAt(ctx.X, ctx.Y) ?? 0;
        if (settlement == 0 || ctx.Civilizations?.OfSettlement(settlement) == null) { return null; }
        return ActionSearch.TryFindResource(in ctx, ResourceKind.Iron, out Int2 target) ? target : null;
    }
}
