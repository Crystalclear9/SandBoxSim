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
        //
        // # 这里用**个体当前位置**，而不是工地（一次实测之后的结论）
        //
        // 曾经把它改成"在工地坐标上算材料"，想让 `Evaluate` 与
        // `BuildingSystem.TryStartBuilding`（它按工地算）口径一致。
        // **实测证明这条路走不通**：动作管线是"决策 → 移动 → 执行"，
        // `Evaluate` 时刻选的工地与真正走到之后用的工地**不是同一个**
        // （`SelectTarget` 会重新选一次）。改成工地口径只是把不一致换了个地方，
        // 而且行为整体偏移（实测 seed 70138 的住房从 18 变成 0）。
        //
        // 真正的结论：**这道门无法靠"统一坐标"修好，因为问题的本质是物流** ——
        // 个体需要先把材料搬到工地。那属于 M8 的物资搬运链。
        //
        // 现在保留"个体当前位置"口径，并把它记为一个**已知的设计缺口**：
        // 一个身上没材料、但附近有堆料的个体会选中"建造"，走到工地后失败。
        // 实测 seed 70138 就这样空转了 1297 次。详见 docs/12 的 M7 未达标项。
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

        // # 第二道门：**手上必须真的有一部分材料**（M8 物流）
        //
        // 这一条是 M7 批量验收逼出来的。原来只有上面那道门，而它的口径是
        // "随身 + **个体附近的**仓库/地面堆" —— 而个体马上要**走开**去工地。
        // 于是出现了一个很坏的状态：身上一件材料都没有的人也能通过门槛、
        // 赢得建造竞争，走到工地后因为"工地附近没有材料"而失败。
        //
        // 实测（seed 70138）：`BuildStorage` 被选中 **1297 次**，开工 **0** 次，
        // 随身木材 **0** —— 每一次失败都花掉了一个本该用于"去砍木头"的决策。
        // **这不是机制不可达，是机制在空转。**
        //
        // 为什么门槛取"成本的一定比例"而不是"全部成本"：
        // 仓库需要 40 木材 + 10 石料，而 `Ai.InventoryComfort` 是 45 ——
        // 要求"全部拿在手上"会让仓库**永远建不起来**（又一次把机制堵死）。
        // 取 25% 的效果是：**手上空空的人不再空转，而正常采集过的人仍然够得着。**
        float needWood = recipe.WoodCost * 0.25f;
        float needStone = recipe.StoneCost * 0.25f;
        float woodShare = needWood <= 0f ? 1f : SimMath.Clamp01(carriedWood / needWood);
        float stoneShare = needStone <= 0f ? 1f : SimMath.Clamp01(carriedStone / needStone);
        float inHand = System.Math.Min(woodShare, stoneShare);

        builder.Consider("手上有材料", inHand, UtilityCurve.Threshold(0.98f), cfg.BuildMaterialWeight);

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

                // # 第三条来源：**人口**（这是被 M7 批量验收逼出来的）
                //
                // 前两条都依赖"地面上堆起来了"这个偶然事件，而它又依赖
                // `GroundStocks.SurplusThreshold`：M2 定 28 时地面堆很常见，
                // M4 为了治"存放挤掉生存动作"把门槛提到 70 ⇒ **地面堆变得罕见** ⇒
                // `pileGap` 常年为 0。后果是 20 个种子里有 5 个**永远不建仓库**，
                // 而 M7 的聚落判据要求"共享至少一座仓库" ⇒ 那些世界人口 40+ 却从不形成聚落。
                //
                // 实测（M7ClusterDiagnostics）：三个不成立的种子分别是
                //   住房/仓库 = 18/0、20/0、3/0 —— 房子很多，仓库恒为 0。
                //
                // 这是一个典型的**跨里程碑参数耦合**：M4 调一个门槛，静默地让 M7 的机制不可达。
                // 判据改用人口，是因为"一个住着 40 人的村子需要一个公共仓库"本来就是常识 ——
                // 它不该取决于某个个体恰好攒够了 70 份木材。
                float populationGap = SimMath.Clamp01((ctx.Store.LiveCount - 3) / 6f);

                gap = System.Math.Max(System.Math.Max(pileGap, storedGap), populationGap);
                if ((ctx.Buildings?.CompletedStorages ?? 0) > 0)
                {
                    // 已经有仓库：缺口降一半（第二个仓库仍然可能有用，但不那么急）
                    gap *= 0.5f;
                }
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
    /// <summary>
    /// 在**指定坐标附近**统计可用物资（M7 修复：口径必须与 `TryStartBuilding` 一致）。
    ///
    /// 仓库那一项仍然走 `TryFindStorageNear`（它按个体位置找最近的仓库）——
    /// 这是一个**已知的残留近似**：严格来说应当在工地附近找仓库。
    /// 之所以先留着：地面堆这一项（本次实测里的真凶）已经统一到工地口径，
    /// 而仓库的那处差异要等"物资搬运"这条链真正建立起来（M8 的物流）再一起处理。
    /// </summary>
    private static float PooledAmountAt(in ActionContext ctx, ResourceKind kind, int x, int y)
    {
        float total = 0f;

        if (ctx.GroundStocks != null
            && ctx.GroundStocks.TryFindNearby(x, y, kind, ctx.Config.GroundStocks.SearchRadius, out int pile, out int _))
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

    private static float PooledAmount(in ActionContext ctx, ResourceKind kind)
        => PooledAmountAt(in ctx, kind, ctx.X, ctx.Y);


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
