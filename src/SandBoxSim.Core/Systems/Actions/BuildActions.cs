using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>
/// 建造动作的共用逻辑（M3）。
///
/// 四种建筑（住房/仓库/农田/矿场）的效用公式与选点规则**完全同构**，
/// 差别只在配方里的数值。把共用部分抽到这里，是为了让"新增一种建筑"只需要
/// 在 <see cref="BuildingRegistry"/> 加一条配方 —— 而不是四处复制一遍动作代码。
/// 这是本项目控制复杂度的主要手段：**同类机制共用代码路径，用数据区分**。
/// </summary>
internal static class BuildAction
{
    /// <summary>
    /// 通用效用：**缺口驱动**（第 29 / 30 条）。
    ///
    /// 关键设计：建造的驱动力是"缺口"而不是"资源多"。
    /// 如果写成"木材越多越想建"，就会出现"资源丰富的地方房子泛滥"这种荒谬结果；
    /// 写成"缺口越大越想建"才能让房子数量自然收敛到人口规模。
    /// </summary>
    public static ActionScore Evaluate(in ActionContext ctx, ActionKind action, BuildingKind kind)
    {
        AiConfig w = ctx.Ai;
        BuildingConfig cfg = ctx.Config.Buildings;
        BuildingRecipe recipe = BuildingRegistry.Of(kind);
        var builder = new ScoreBuilder(action, w.BlockedUtilityMultiplier);

        // ---- 材料可得性 ----
        // 三个来源：随身、附近仓库（共享）、附近地面堆。顺序与实际扣料顺序一致。
        float carriedWood = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Wood);
        float carriedStone = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Stone);
        float wood = carriedWood + PooledAmount(in ctx, ResourceKind.Wood);
        float stone = carriedStone + PooledAmount(in ctx, ResourceKind.Stone);

        float woodEnough = recipe.WoodCost <= 0f ? 1f : SimMath.Clamp01(wood / recipe.WoodCost);
        float stoneEnough = recipe.StoneCost <= 0f ? 1f : SimMath.Clamp01(stone / recipe.StoneCost);
        float material = System.Math.Min(woodEnough, stoneEnough);

        // 材料齐备度用**门槛**语义（不是 bonus）。
        //
        // 这一点踩过一次严重的坑：最初把它写成 bonus，于是一个
        // "住房缺口 = 1.0 而手上一点木材都没有"的个体，建造效用仍然高达 1.0 ——
        // 他会一直选中"建造"、跑到工地、失败、再选中"建造"……
        // **采集类动作永远排不上队**，结果 40 个人在 10 天内饿到只剩 3 个，
        // 而 60 天里一栋房子都没建成。
        //
        // 正确的语义是："材料不够 ⇒ 这件事现在不能做"，于是效用被压到门槛之下，
        // 采集木材才有机会被选中。**门槛与加分的区别在这里是生死攸关的。**
        builder.Consider("材料齐备", material, UtilityCurve.Threshold(0.98f), cfg.BuildMaterialWeight);

        // ---- 缺口（真正的驱动） ----
        float gap;
        switch (kind)
        {
            case BuildingKind.House:
            {
                // 住房缺口 = (人口 − 床位) / 人口。用比例而不是绝对数，
                // 这样"40 人的村子缺 4 张床"与"4 个人的村子缺 4 张床"的压力不同（后者更急）。
                int beds = ctx.Buildings?.TotalBeds ?? 0;
                int population = ctx.Store.LiveCount;
                int missing = population - beds;
                gap = population <= 0 ? 0f : SimMath.Clamp01((float)missing / population);

                // 已经有很多床位却还在建 ⇒ 抑制（防止"房子比人还多"）
                if (missing <= 0) { gap = 0f; }
                break;
            }

            case BuildingKind.Storage:
            {
                // 仓库缺口：从"聚落手上攒了多少可用物资"推断。
                //
                // 为什么不只看"地面堆的数量"：地面堆只有在个体随身超过门槛时才会出现，
                // 而"个人刚好存了点东西"与"确实需要一个公共仓库"是两件事。
                // 只看地面堆会让仓库永远不建（实测 60 天 0 个仓库）——
                // 而"攒了一堆木料却没地方放"本身就是最清楚的仓库需求信号。
                int piles = ctx.GroundStocks?.LiveCount ?? 0;
                float pooled = (ctx.GroundStocks?.TotalOf(ResourceKind.Wood) ?? 0f)
                             + SettledWood(in ctx);
                float pileGap = SimMath.Clamp01(piles / 3f);
                float storedGap = SimMath.Clamp01(pooled / 120f);
                gap = System.Math.Max(pileGap, storedGap);
                if ((ctx.Buildings?.CompletedStorages ?? 0) > 0)
                {
                    // 已经有仓库：缺口降一半（第二个仓库仍然可能有用，但不那么急）
                    gap *= 0.5f;
                }
                break;
            }

            case BuildingKind.Farm:
            {
                // 农田缺口（M4 会真正驱动）：M3 只作为一个低优先级的"有粮就开垦"
                float food = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);
                gap = SimMath.Clamp01(food / 40f) * 0.5f;
                break;
            }

            default:
                gap = 0f;
                break;
        }

        builder.Consider("缺口", gap, UtilityCurve.Survival, cfg.BuildGapWeight);

        // ---- 选址可得性（bonus） ----
        bool siteOk = SelectSite(in ctx, kind, out Int2 _, out float distance);
        builder.Consider("附近有合适空地", siteOk ? 1f : 0f, UtilityCurve.Linear, cfg.BuildSiteWeight, isBonus: true);

        // ---- 距离惩罚：跑太远盖房子不合理 ----
        if (siteOk)
        {
            float far = SimMath.Clamp01(distance / System.Math.Max(1f, w.SearchRadius));
            builder.Consider("工地太远", far, UtilityCurve.Quadratic, -0.8f);
        }

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus, isBonus: true);

        return builder.Build();
    }

    /// <summary>聚落手上已经攒下的木材总量（随身 + 地面堆 + 仓库）——仓库需求的判据。</summary>
    private static float SettledWood(in ActionContext ctx)
    {
        float total = ctx.GroundStocks?.TotalOf(ResourceKind.Wood) ?? 0f;

        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            total += ctx.Store.InventoryOf(slots[k], ResourceKind.Wood);
        }

        return total;
    }

    /// <summary>共享库存 + 地面堆里可用的数量（建造可以用公共物资）。</summary>
    private static float PooledAmount(in ActionContext ctx, ResourceKind kind)
    {
        float total = 0f;

        if (ctx.GroundStocks != null
            && ctx.GroundStocks.TryFindNearby(ctx.X, ctx.Y, kind, ctx.Config.GroundStocks.SearchRadius, out int pile, out int _))
        {
            total += ctx.GroundStocks.AmountOf(pile, kind);
        }

        if (ctx.Storage != null
            && ctx.Buildings != null
            && TryFindStorageNear(in ctx, out int storageIndex, out int _))
        {
            total += ctx.Storage.AmountOf(storageIndex, kind);
        }

        return total;
    }

    /// <summary>在视野半径内找最近的仓库（与 BuildingSystem 里的规则保持一致）。</summary>
    public static bool TryFindStorageNear(in ActionContext ctx, out int index, out int distance)
    {
        index = -1;
        distance = int.MaxValue;
        if (ctx.Buildings == null) { return false; }

        int radius = ctx.Config.Buildings.StorageSearchRadius;
        for (int k = 0; k < ctx.Buildings.LiveCount; k++)
        {
            int candidate = ctx.Buildings.LiveAt(k);
            if (!ctx.Buildings.IsAlive(candidate)) { continue; }
            if (ctx.Buildings.KindOf(candidate) != BuildingKind.Storage) { continue; }
            if (ctx.Buildings.StateOf(candidate) != BuildingState.Complete) { continue; }

            int d = System.Math.Max(
                System.Math.Abs(ctx.Buildings.XOf(candidate) - ctx.X),
                System.Math.Abs(ctx.Buildings.YOf(candidate) - ctx.Y));

            if (d < distance && d <= radius) { distance = d; index = candidate; }
        }

        return index >= 0;
    }

    /// <summary>
    /// 选点（第 31 条）。
    ///
    /// 规则很简单，但每一条都有理由：
    ///   1. **离自己近**（不想跑 30 格去盖房子）：在视野半径内按距离由近到远找；
    ///   2. **不能和已有建筑重叠**（`CanPlaceAt` 保证）；
    ///   3. **住房要离水不太远**（生活需要）；太近又会占掉河边的好地，因此是"1~6 格"；
    ///   4. **农田必须临水**（配方里的 `RequiresWaterAccess`）。
    ///
    /// 输出 <paramref name="distance"/> 供效用函数做距离惩罚 ——
    /// 选址与"值不值得建"用的是同一份信息，因此不会出现"AI 觉得近、动作却觉得远"。
    /// </summary>
    public static bool SelectSite(in ActionContext ctx, BuildingKind kind, out Int2 site, out float distance)
    {
        site = default;
        distance = float.MaxValue;

        World world = ctx.World;
        int radius = ctx.Config.Buildings.SiteSearchRadius;
        BuildingRecipe recipe = BuildingRegistry.Of(kind);

        // 以"自己"为圆心向外按环扫描（与资源搜索同一个模式：先近后远、确定性）
        for (int ring = 1; ring <= radius; ring++)
        {
            int bestIndex = -1;
            int bestDistance = int.MaxValue;

            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    int cheb = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    if (cheb != ring) { continue; }

                    int x = ctx.X + dx;
                    int y = ctx.Y + dy;
                    if (!world.IsInBounds(x, y)) { continue; }
                    if (!BuildingStore.CanPlaceAt(world, kind, x, y)) { continue; }

                    // 住房额外要求：离水 1~6 格（够用但不必占着河边）
                    if (kind == BuildingKind.House)
                    {
                        int waterDistance = DistanceToWater(world, x, y, 8);
                        if (waterDistance < 1 || waterDistance > 6) { continue; }
                    }

                    // 不要挡在别人正在走的地方（"道路/路口"上留空）
                    if (IsBusyTile(ctx, x, y)) { continue; }

                    int d = (dx * dx) + (dy * dy);
                    if (d < bestDistance) { bestDistance = d; bestIndex = (y * world.Width) + x; }
                }
            }

            if (bestIndex >= 0)
            {
                int x = bestIndex % world.Width;
                int y = bestIndex / world.Width;
                site = new Int2(x, y);
                distance = (float)System.Math.Sqrt(bestDistance);
                return true;
            }
        }

        _ = recipe;
        return false;
    }

    /// <summary>到水的切比雪夫距离（在 <paramref name="maxRadius"/> 内找不到就返回一个大于它的值）。</summary>
    private static int DistanceToWater(World world, int x, int y, int maxRadius)
    {
        for (int ring = 0; ring <= maxRadius; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    int cheb = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    if (cheb != ring) { continue; }

                    int nx = x + dx;
                    int ny = y + dy;
                    if (!world.IsInBounds(nx, ny)) { continue; }
                    if (world.TileAt(nx, ny).Terrain == TerrainKind.Water) { return cheb; }
                }
            }
        }
        return maxRadius + 1;
    }

    /// <summary>这一格是不是"别人正在用的地方"（有人站在上面）。</summary>
    private static bool IsBusyTile(in ActionContext ctx, int x, int y)
        => ctx.Store.IsSlotOccupied(ctx.Slot, x, y);

    /// <summary>选靶：返回工地位置。</summary>
    public static Int2? SelectTarget(in ActionContext ctx, BuildingKind kind, AStarPathfinder pathfinder)
    {
        if (!SelectSite(in ctx, kind, out Int2 site, out float _)) { return null; }

        // 用一次寻路确认可达：否则会出现"想建但走不过去"的个体一直卡着。
        // 这是少数几个"值得为选靶付一次 A*"的地方 —— 建造是低频行为。
        pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
        PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, site.X, site.Y, out Int2 _);
        if (!result.Success) { return null; }

        return site;
    }
}

/// <summary>建造住房（M3）。</summary>
internal static class BuildHouseAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
        => BuildAction.Evaluate(in ctx, ActionKind.BuildHouse, BuildingKind.House);

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
        => BuildAction.SelectTarget(in ctx, BuildingKind.House, pathfinder);
}

/// <summary>建造仓库（M3）。</summary>
internal static class BuildStorageAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
        => BuildAction.Evaluate(in ctx, ActionKind.BuildStorage, BuildingKind.Storage);

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
        => BuildAction.SelectTarget(in ctx, BuildingKind.Storage, pathfinder);
}

/// <summary>建造农田（M3 只做"能建"，真正的产出在 M4）。</summary>
internal static class BuildFarmAction
{
    public static ActionScore Evaluate(in ActionContext ctx)
        => BuildAction.Evaluate(in ctx, ActionKind.BuildFarm, BuildingKind.Farm);

    public static Int2? SelectTarget(in ActionContext ctx, AStarPathfinder pathfinder)
        => BuildAction.SelectTarget(in ctx, BuildingKind.Farm, pathfinder);
}
