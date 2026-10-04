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

        // 材料评估与实际扣料采用同一工地坐标。
        Int2 fundedSite = default;
        float distance = float.MaxValue;
        float carriedWood = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Wood);
        float carriedStone = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Stone);
        // 全世界共享料的上界都不足时，不扫描每一块候选工地。此过滤不会屏蔽任何可行建造。
        float possibleWood = carriedWood + (ctx.GroundStocks?.TotalOf(ResourceKind.Wood) ?? 0)
            + (ctx.Storage?.GrandTotalOf(ResourceKind.Wood, ctx.Buildings?.Capacity ?? 0) ?? 0);
        float possibleStone = carriedStone + (ctx.GroundStocks?.TotalOf(ResourceKind.Stone) ?? 0)
            + (ctx.Storage?.GrandTotalOf(ResourceKind.Stone, ctx.Buildings?.Capacity ?? 0) ?? 0);
        bool canFund = possibleWood + 0.001f >= recipe.WoodCost && possibleStone + 0.001f >= recipe.StoneCost;
        bool siteOk = HasDemand(in ctx, kind) && canFund && SelectSite(in ctx, kind, out fundedSite, out distance);
        int materialX = siteOk ? fundedSite.X : ctx.X;
        int materialY = siteOk ? fundedSite.Y : ctx.Y;
        float wood = carriedWood + PooledAmountAt(in ctx, ResourceKind.Wood, materialX, materialY);
        float stone = carriedStone + PooledAmountAt(in ctx, ResourceKind.Stone, materialX, materialY);

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
                int beds = ctx.Buildings == null ? 0 : ActionSearch.PlannedBeds(ctx.Buildings);
                int population = ctx.Store.LiveCount;
                int missing = population - beds;
                gap = population <= 0 ? 0f : SimMath.Clamp01((float)missing / population);

                // 已经有很多床位却还在建 ⇒ 抑制（防止"房子比人还多"）
                if (missing <= 0) { gap = 0f; }
                break;
            }

            case BuildingKind.Storage:
            {
                // 与材料采集共用需求，计入在建容量，容量满足后停止扩建。
                gap = ActionSearch.StorageDemand01(in ctx);
                break;
            }

            case BuildingKind.Farm:
            {
                // 农田缺口（M4）：**由食物短缺驱动**，而不是"手上有粮就开垦"。
                //
                // M3 的版本是 `clamp01(food / 40) * 0.5`，方向恰好是反的：
                // 它让"有粮食的人"更想开田，而"快饿死的人"完全不想。
                // 结果是世界永远不建农田（实测 100 天 0 块田），
                // 于是 M4 的农业根本无法接入 —— 而出生系统又正是被食物卡住的。
                //
                // 正确的方向是缺口：**人均食物越低，越需要开垦**。
                // 这与住房/仓库的缺口驱动是同一个道理（第 29 / 30 条）。
                float food = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food);
                float personalShortfall = 1f - SimMath.Clamp01(food / System.Math.Max(1f, ctx.Config.Buildings.FarmBaseYieldPerDay * 2f));

                // 已有农田越多，缺口越小（避免把整张地图开成田）
                int farms = 0;
                if (ctx.Buildings != null)
                {
                    for (int k = 0; k < ctx.Buildings.LiveCount; k++)
                    {
                        int candidate = ctx.Buildings.LiveAt(k);
                        if (!ctx.Buildings.IsAlive(candidate)) { continue; }
                        if (ctx.Buildings.KindOf(candidate) == BuildingKind.Farm) { farms++; }
                    }
                }

                int population = System.Math.Max(1, ctx.Store.LiveCount);
                // 目标：每 4 个人一块田。达到目标后缺口归零。
                float farmGap = 1f - SimMath.Clamp01(farms / System.Math.Max(1f, population / 4f));
                gap = System.Math.Max(personalShortfall, farmGap) * farmGap;
                break;
            }

            case BuildingKind.Mine:
                gap = HasDemand(in ctx, kind) ? 0.6f : 0;
                break;
            default:
                gap = 0f;
                break;
        }

        builder.Consider("缺口", gap, UtilityCurve.Survival, cfg.BuildGapWeight);

        // ---- 选址可得性（bonus） ----
        builder.Consider("附近有合适空地", siteOk ? 1f : 0f, UtilityCurve.Linear, cfg.BuildSiteWeight, isBonus: true);

        // ---- 距离惩罚：跑太远盖房子不合理 ----
        if (siteOk)
        {
            float far = SimMath.Clamp01(distance / System.Math.Max(1f, w.SearchRadius));
            builder.Consider("工地太远", far, UtilityCurve.Quadratic, -0.8f);
        }

        float industriousness = ctx.Store.PersonalityOf(ctx.Slot).Industriousness;
        builder.ConsiderScore("勤劳性格", industriousness, industriousness, w.IndustriousnessWorkBonus, isBonus: true);

        ActionScore score = builder.Build();
        // 建造需求已满足时必须不可选，不能靠通用的软门留下最低建造效用。
        return gap <= 0f || !siteOk
            ? new ActionScore(action, score.Considerations, 0f, score.WeightedAverage, score.GeometricMean, true)
            : score;
    }

    /// <summary>统计指定工地附近的共享仓库和地面物资，与开工扣料规则一致。</summary>
    private static float PooledAmountAt(in ActionContext ctx, ResourceKind kind, int x, int y)
    {
        if (ctx.DecisionCache == null) { return ReadPooledAmount(in ctx, kind, x, y); }
        int tile = y * ctx.World.Width + x;
        if (!ctx.DecisionCache.TryMaterials(tile, out var stock))
        {
            stock = (ReadPooledAmount(in ctx, ResourceKind.Wood, x, y), ReadPooledAmount(in ctx, ResourceKind.Stone, x, y));
            ctx.DecisionCache.StoreMaterials(tile, stock.Wood, stock.Stone);
        }
        return kind == ResourceKind.Wood ? stock.Wood : stock.Stone;
    }

    private static float ReadPooledAmount(in ActionContext ctx, ResourceKind kind, int x, int y)
    {
        float total = 0f;

        if (ctx.GroundStocks != null
            && ctx.GroundStocks.TryFindNearby(x, y, kind, ctx.Config.GroundStocks.SearchRadius, out int pile, out int _))
        {
            total += ctx.GroundStocks.AmountOf(pile, kind);
        }

        if (ctx.Storage != null
            && ctx.Buildings != null
            && TryFindStorageAt(in ctx, x, y, out int storageIndex, out int _))
        {
            total += ctx.Storage.AmountOf(storageIndex, kind);
        }

        return total;
    }




    /// <summary>在视野半径内找最近的仓库（与 BuildingSystem 里的规则保持一致）。</summary>
    public static bool TryFindStorageNear(in ActionContext ctx, out int index, out int distance)
        => TryFindStorageAt(in ctx, ctx.X, ctx.Y, out index, out distance);

    private static bool TryFindStorageAt(in ActionContext ctx, int x, int y, out int index, out int distance)
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
                System.Math.Abs(ctx.Buildings.XOf(candidate) - x),
                System.Math.Abs(ctx.Buildings.YOf(candidate) - y));

            if (d <= radius && (d < distance || (d == distance && (index < 0 || candidate < index))))
            { distance = d; index = candidate; }
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

        // 全局共享量是本地可用量的上界；不足时无需扫描每个候选工地。
        float woodUpper = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Wood)
            + (ctx.GroundStocks?.TotalOf(ResourceKind.Wood) ?? 0f)
            + (ctx.Storage?.GrandTotalOf(ResourceKind.Wood, ctx.Buildings?.Capacity ?? 0) ?? 0f);
        float stoneUpper = ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Stone)
            + (ctx.GroundStocks?.TotalOf(ResourceKind.Stone) ?? 0f)
            + (ctx.Storage?.GrandTotalOf(ResourceKind.Stone, ctx.Buildings?.Capacity ?? 0) ?? 0f);
        if (woodUpper + 1e-3f < recipe.WoodCost || stoneUpper + 1e-3f < recipe.StoneCost) { return false; }

        if (kind == BuildingKind.Farm && ctx.DecisionCache != null)
        {
            int bestRing = int.MaxValue, bestSquare = int.MaxValue, best = -1;
            // Only locally visible sites participate. The cache is a geometry index, not NPC knowledge.
            foreach (int candidate in ctx.DecisionCache.FarmSites(in ctx))
            {
                int x = candidate % world.Width, y = candidate / world.Width, dx = x - ctx.X, dy = y - ctx.Y;
                int ring = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)), square = dx * dx + dy * dy;
                if (ring < 1 || ring > radius || ring > bestRing || ring == bestRing && square >= bestSquare) { continue; }
                if (!BuildingStore.CanPlaceAt(world, kind, x, y) || IsBusyTile(in ctx, x, y) || !HasMaterialsAt(in ctx, recipe, x, y)) { continue; }
                bestRing = ring; bestSquare = square; best = candidate;
            }
            if (best < 0) { return false; }
            site = new Int2(best % world.Width, best / world.Width); distance = (float)System.Math.Sqrt(bestSquare); return true;
        }

        // 以"自己"为圆心向外按环扫描（与资源搜索同一个模式：先近后远、确定性）
        for (int ring = 1; ring <= radius; ring++)
        {
            int bestIndex = -1;
            int bestDistance = int.MaxValue;

            for (int dy = -ring; dy <= ring; dy++)
            {
                // Interior rows contain only the two edge cells. Preserve the original row/column tie order.
                for (int dx = -ring; dx <= ring; dx += System.Math.Abs(dy) == ring ? 1 : 2 * ring)
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
                        int waterDistance = world.DistanceToWater(x, y, 8);
                        if (waterDistance < 1 || waterDistance > 6) { continue; }
                    }

                    // 不要挡在别人正在走的地方（"道路/路口"上留空）
                    if (IsBusyTile(ctx, x, y)) { continue; }

                    // 评估、选靶和开工都按工地附近的实际材料判定。
                    if (!HasMaterialsAt(in ctx, recipe, x, y)) { continue; }

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

    private static bool HasMaterialsAt(in ActionContext ctx, BuildingRecipe recipe, int x, int y)
        => ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Wood) + PooledAmountAt(in ctx, ResourceKind.Wood, x, y) + 1e-3f >= recipe.WoodCost
            && ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Stone) + PooledAmountAt(in ctx, ResourceKind.Stone, x, y) + 1e-3f >= recipe.StoneCost;

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
        => ctx.DecisionCache?.Occupied(in ctx, x, y) ?? ctx.Store.IsSlotOccupied(ctx.Slot, x, y);

    /// <summary>选靶：返回工地位置。</summary>
    public static Int2? SelectTarget(in ActionContext ctx, BuildingKind kind, AStarPathfinder pathfinder)
    {
        if (!HasDemand(in ctx, kind)) { return null; }
        if (!SelectSite(in ctx, kind, out Int2 site, out float _)) { return null; }

        // 用一次寻路确认可达：否则会出现"想建但走不过去"的个体一直卡着。
        // 这是少数几个"值得为选靶付一次 A*"的地方 —— 建造是低频行为。
        pathfinder.MarkOrigin(AStarPathfinder.SearchOrigin.Targeting);
        PathResult result = pathfinder.FindNextStep(ctx.X, ctx.Y, site.X, site.Y, out Int2 _);
        if (!result.Success) { return null; }

        return site;
    }

    public static bool HasDemand(in ActionContext ctx, BuildingKind kind)
    {
        if (ctx.Buildings == null) { return false; }
        if (kind == BuildingKind.House) { return ActionSearch.PlannedBeds(ctx.Buildings) < ctx.Store.LiveCount; }
        if (kind == BuildingKind.Storage) { return ActionSearch.StorageDemand01(in ctx) > 0f; }
        if (kind == BuildingKind.Farm)
        {
            int farms = 0;
            for (int i = 0; i < ctx.Buildings.LiveCount; i++)
            { if (ctx.Buildings.KindOf(ctx.Buildings.LiveAt(i)) == BuildingKind.Farm) { farms++; } }
            return farms < System.Math.Max(1f, ctx.Store.LiveCount / 4f);
        }
        if (kind == BuildingKind.Mine)
        {
            if (ctx.Buildings.CountOf(BuildingKind.Storage) == 0 || ctx.Buildings.CountOf(BuildingKind.House) * 4 < ctx.Store.LiveCount) { return false; }
            int mines = 0;
            for (int i = 0; i < ctx.Buildings.LiveCount; i++)
                if (ctx.Buildings.KindOf(ctx.Buildings.LiveAt(i)) == BuildingKind.Mine) { mines++; }
            return ctx.Store.LiveCount >= 6 && mines < System.Math.Max(1, ctx.Store.LiveCount / 20);
        }
        return false;
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
