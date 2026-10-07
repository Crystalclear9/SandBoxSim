using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Ai;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Pathing;

namespace SandBoxSim.Core.Systems.Actions;

/// <summary>动作实现的共享工具：附近资源搜索与目标校验。</summary>
internal static class ActionSearch
{
    /// <summary>
    /// 在视野半径内寻找最近的、含有指定资源的可走格子。
    ///
    /// 实现方式是**两级搜索**，这是性能与视野半径能同时兼顾的关键：
    ///   1. 先用 chunk 聚合统计（第 74 条）找出附近"确实有这种资源"的 chunk，
    ///      按距离由近到远遍历（最多 <paramref name="chunkRadius"/> 圈）；
    ///   2. 依照稳定区块顺序扫描，跳过不可达格；当前区块无有效目标时继续。
    ///
    /// 为什么不能退化成"扫半径内的每一格"：
    /// 视野半径 20 意味着 41×41 = 1681 格；100 个个体 × 每次决策都扫一遍
    /// 就是这个模拟里最大的一笔开销。用 chunk 统计先把候选缩到 1~2 个块，
    /// 扫描量下降一个数量级，而搜索范围反而**更大**（chunk 半径 2 覆盖约 48×48 格）。
    ///
    /// 代价：找到的是"最近一圈中首个可达资源区块的最近格子"，不一定全局最近。
    /// 这个近似是刻意的 —— 行为上看不出差别，但性能差别很大。
    /// </summary>
    public static bool TryFindResource(in ActionContext ctx, ResourceKind kind, out Int2 found, int chunkRadius = 2)
    {
        found=default;if(kind==ResourceKind.None)return false;
        var chunks=ctx.World.Chunks;int centerX=ctx.X/chunks.ChunkSize,centerY=ctx.Y/chunks.ChunkSize,width=ctx.World.Width;
        for(int ring=0;ring<=chunkRadius;ring++)
        {
            int best=-1,bestDistance=int.MaxValue;
            for(int cy=centerY-ring;cy<=centerY+ring;cy++)for(int cx=centerX-ring;cx<=centerX+ring;cx++)
            {
                if(cx<0||cy<0||cx>=chunks.ChunkCols||cy>=chunks.ChunkRows||System.Math.Max(System.Math.Abs(cx-centerX),System.Math.Abs(cy-centerY))!=ring)continue;
                var stats=chunks.Read(cx,cy);if(!stats.IsValid||stats.AmountOf(kind)<=.01f)continue;
                best=-1;bestDistance=int.MaxValue;
                chunks.GetBounds(cx,cy,out int minX,out int minY,out int maxX,out int maxY);
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    int index=y*width+x;ref readonly var tile=ref ctx.World.Tiles[index];
                    if(tile.Resource.Kind!=kind||tile.Resource.Amount<=.01f||!tile.Walkable)continue;
                    int dx=x-ctx.X,dy=y-ctx.Y,distance=dx*dx+dy*dy;
                    if(distance>bestDistance||(distance==bestDistance&&best>=0&&index>=best))continue;
                    if(ctx.Reachability!=null&&!ctx.Reachability.CanReach(ctx.X,ctx.Y,x,y))continue;
                    best=index;bestDistance=distance;
                }
                if(best>=0){found=new Int2(best%width,best/width);return true;}
            }
        }
        return false;
    }

    /// <summary>
    /// 在附近的 chunk 里找一片水源并提供"取水点"（紧邻水域的可走格）。
    /// 水域不可通行，因此人只能站在岸边取水。
    /// </summary>
    public static bool TryFindWaterAccess(in ActionContext ctx, out Int2 found, int chunkRadius = 2)
    {
        found = default;

        ChunkGrid chunks = ctx.World.Chunks;
        int centerChunkX = ctx.X / chunks.ChunkSize;
        int centerChunkY = ctx.Y / chunks.ChunkSize;
        int width = ctx.World.Width;
        Tile[] tiles = ctx.World.Tiles;

        for (int ring = 0; ring <= chunkRadius; ring++)
        {
            int bestDistance = int.MaxValue;
            int bestIndex = -1;

            for (int cy = centerChunkY - ring; cy <= centerChunkY + ring; cy++)
            {
                if (cy < 0 || cy >= chunks.ChunkRows) { continue; }

                for (int cx = centerChunkX - ring; cx <= centerChunkX + ring; cx++)
                {
                    if (cx < 0 || cx >= chunks.ChunkCols) { continue; }

                    int chebyshev = System.Math.Max(System.Math.Abs(cx - centerChunkX), System.Math.Abs(cy - centerChunkY));
                    if (chebyshev != ring) { continue; }

                    ChunkStatsReadOnly stats = chunks.Read(cx, cy);
                    if (!stats.IsValid || stats.WaterTiles == 0) { continue; }

                    chunks.GetBounds(cx, cy, out int minX, out int minY, out int maxX, out int maxY);

                    for (int y = minY; y <= maxY; y++)
                    {
                        if (y < 1 || y >= ctx.World.Height - 1) { continue; }
                        int rowBase = y * width;

                        for (int x = minX; x <= maxX; x++)
                        {
                            if (x < 1 || x >= width - 1) { continue; }

                            int index = rowBase + x;
                            if (!tiles[index].Walkable) { continue; }

                            bool adjacentWater =
                                tiles[index - 1].Terrain == TerrainKind.Water ||
                                tiles[index + 1].Terrain == TerrainKind.Water ||
                                tiles[index - width].Terrain == TerrainKind.Water ||
                                tiles[index + width].Terrain == TerrainKind.Water;

                            if (!adjacentWater) { continue; }
                            if(ctx.Reachability!=null&&!ctx.Reachability.CanReach(ctx.X,ctx.Y,x,y))continue;

                            int dx = x - ctx.X;
                            int dy = y - ctx.Y;
                            int distance = (dx * dx) + (dy * dy);
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestIndex = index;
                            }
                        }
                    }
                }
            }

            if (bestIndex >= 0)
            {
                found = new Int2(bestIndex % width, bestIndex / width);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 随机选一个"合法且可达"的目标格。
    /// 用于漫游与探索：它天然分散了个体的去向，不需要任何全局调度。
    ///
    /// 尝试若干次后放弃（避免在封闭小空间里无限重试）。
    /// </summary>
    public static bool TryPickRandomReachable(
        in ActionContext ctx,
        AStarPathfinder pathfinder,
        int minDistance,
        int maxDistance,
        out Int2 found)
    {
        found = default;
        if (maxDistance <= 0) { return false; }

        int width = ctx.World.Width;
        int height = ctx.World.Height;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            // 均匀取半径：sqrt 让点在圆盘上分布均匀（否则会过度聚集在近处）
            float radiusFraction = (float)System.Math.Sqrt(ctx.Rng.NextDouble());
            float radius = minDistance + ((maxDistance - minDistance) * radiusFraction);
            double angle = ctx.Rng.NextDouble() * 6.283185307179586;

            int tx = ctx.X + (int)System.Math.Round(System.Math.Cos(angle) * radius);
            int ty = ctx.Y + (int)System.Math.Round(System.Math.Sin(angle) * radius);

            if (tx < 0 || ty < 0 || tx >= width || ty >= height) { continue; }
            if (tx == ctx.X && ty == ctx.Y) { continue; }
            if (!ctx.World.TileAt(tx, ty).Walkable) { continue; }

            PathResult path = pathfinder.FindNextStep(ctx.X, ctx.Y, tx, ty, out Int2 _);

            // 注意这里用的是 FindNextStep：如果目标不可达它会返回失败，
            // 但成功时我们只需要"下一跳" —— 移动系统会逐步前进并定期重算，
            // 因此不需要在这里保存整条路径（那样反而会抱着过期路线撞墙）。
            if (path.Success)
            {
                found = new Int2(tx, ty);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 随身物资"已经够多"的程度 [0,1]（0 = 空手，1 = 达到或超过舒适上限）。
    ///
    /// 采集类动作共用它作为**压制项**（负权重）。理由见 <c>AiConfig.InventoryComfort</c>：
    /// 没有"够了"这个信号，采集会无限囤积，并把整个行为空间淹掉。
    /// </summary>
    public static float Overstock01(in ActionContext ctx, ResourceKind gathering = ResourceKind.None)
    {
        float comfort = ctx.Ai.InventoryComfort;
        if (comfort <= 0.01f) { return 0f; }

        // 食物储备不能被木石库存冒充；缺粮时必须仍能采食。
        if (gathering == ResourceKind.Food)
        { return SimMath.Clamp01(ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Food) / comfort); }
        float carried = ctx.Store.InventoryTotalOf(ctx.Slot);
        if (StorageDemand01(in ctx) > 0f)
        {
            BuildingRecipe recipe = BuildingRegistry.Of(BuildingKind.Storage);
            carried -= System.Math.Min(ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Wood), recipe.WoodCost);
            carried -= System.Math.Min(ctx.Store.InventoryOf(ctx.Slot, ResourceKind.Stone), recipe.StoneCost);
        }
        return SimMath.Clamp01(carried / comfort);
    }

    /// <summary>
    /// "附近有建造需求"的程度 [0,1]（M3）。
    ///
    /// 它是把"个人采集"变成"公共生产"的那个信号：
    ///   * 有住房缺口（人口 > 床位） ⇒ 需要木材；
    ///   * 有仓库缺口（地上堆了很多东西） ⇒ 需要木材 + 石料；
    ///   * 已经建成足够多 ⇒ 需求归零，采伐自然停下（不会无限砍树）。
    ///
    /// 为什么这个信号必须存在（实测）：只按"自己缺不缺木材"驱动时，
    /// 个体会在随身攒到 30 左右就停手，40 天累计采伐只有 241 木材 ——
    /// 而一间房子要 20，也就是几乎永远盖不起房子。
    /// **"谁来负责攒公共物资"是任何经济系统都必须回答的问题**，
    /// 这里用"建造缺口"作为答案：缺口越大，越多人去采。
    /// </summary>
    public static float BuildDemand01(in ActionContext ctx, ResourceKind kind)
    {
        BuildingStore? buildings = ctx.Buildings;
        if (buildings == null) { return 0f; }

        float demand = 0f;

        if (kind == ResourceKind.Wood)
        {
            // 住房缺口：人口多于床位
            int population = ctx.Store.LiveCount;
            int missing = population - PlannedBeds(buildings);
            demand = population <= 0 ? 0f : SimMath.Clamp01((float)missing / population);

        }

        // 仓库需求与建造效用使用同一人口条件。不能要求先囤够木材才
        // 采仓库材料，也不能让住房数量把尚未满足的仓库需求归零。
        if (kind == ResourceKind.Wood || kind == ResourceKind.Stone)
        {
            BuildingRecipe recipe = BuildingRegistry.Of(BuildingKind.Storage);
            float cost = kind == ResourceKind.Wood ? recipe.WoodCost : recipe.StoneCost;
            float missing = cost <= 0f ? 0f : 1f - SimMath.Clamp01(ctx.Store.InventoryOf(ctx.Slot, kind) / cost);
            float storageDemand = StorageDemand01(in ctx) * missing;
            demand = System.Math.Max(demand, storageDemand);
        }
        return demand;
    }

    public static int PlannedBeds(BuildingStore buildings)
    {
        int beds = buildings.TotalBeds;
        for (int k = 0; k < buildings.LiveCount; k++)
        {
            int index = buildings.LiveAt(k);
            if (buildings.StateOf(index) == BuildingState.UnderConstruction)
            { beds += BuildingRegistry.Of(buildings.KindOf(index)).Beds; }
        }
        return beds;
    }

    public static float StorageDemand01(in ActionContext ctx)
    {
        if (ctx.DecisionCache?.StorageDemand is float cached) { return cached; }
        float value = ComputeStorageDemand(in ctx);
        if (ctx.DecisionCache != null) { ctx.DecisionCache.StorageDemand = value; }
        return value;
    }

    private static float ComputeStorageDemand(in ActionContext ctx)
    {
        BuildingStore? buildings = ctx.Buildings;
        if (buildings == null) { return 0f; }
        float capacity = 0f;
        for (int k = 0; k < buildings.LiveCount; k++)
        {
            int index = buildings.LiveAt(k);
            if (buildings.KindOf(index) != BuildingKind.Storage) { continue; }
            // 正在建的仓库先完成，再评估是否需要更多容量，避免多人重复开工。
            if (buildings.StateOf(index) == BuildingState.UnderConstruction) { return 0f; }
            capacity += BuildingRegistry.Of(BuildingKind.Storage).StorageCapacity;
        }
        if (capacity <= 0f) { return SimMath.Clamp01((ctx.Store.LiveCount - 3) / 6f); }
        float stock = 0f;
        for (int k = (int)ResourceKind.Food; k <= (int)ResourceKind.Iron; k++)
        {
            ResourceKind kind = (ResourceKind)k;
            stock += CarriedOf(in ctx, kind) + (ctx.GroundStocks?.TotalOf(kind) ?? 0f)
                + (ctx.Storage?.GrandTotalOf(kind, buildings.Capacity) ?? 0f);
        }
        return SimMath.Clamp01((stock - capacity) / System.Math.Max(1f, stock));
    }

    /// <summary>全体存活个体随身携带的某种资源总量。</summary>
    public static float CarriedOf(in ActionContext ctx, ResourceKind kind)
    {
        float total = 0f;
        int[] slots = ctx.Store.LiveSlotsRaw(out int liveCount);
        for (int k = 0; k < liveCount; k++)
        {
            total += ctx.Store.InventoryOf(slots[k], kind);
        }
        return total;
    }

    /// <summary>把个体的"离家距离"归一化成 [0,1]（0 = 在出生点，1 = 极远）。</summary>
    public static float HomeDistance01(in ActionContext ctx)
    {
        float radius = ctx.Ai.HomeAttachmentRadius;
        if (radius <= 0f) { return 0f; }
        double distance = Int2.Distance(new Int2(ctx.X, ctx.Y), new Int2(ctx.HomeX, ctx.HomeY));
        return SimMath.Clamp01((float)(distance / radius));
    }

    /// <summary>是否离家太远（用于"该回家了"这类判定，M7 会用到）。</summary>
    public static bool IsFarFromHome(in ActionContext ctx, float threshold01)
        => HomeDistance01(in ctx) >= threshold01;
}
