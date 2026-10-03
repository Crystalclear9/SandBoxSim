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
    ///   2. 只对第一个命中的 chunk 内的格子做精确扫描。
    ///
    /// 为什么不能退化成"扫半径内的每一格"：
    /// 视野半径 20 意味着 41×41 = 1681 格；100 个个体 × 每次决策都扫一遍
    /// 就是这个模拟里最大的一笔开销。用 chunk 统计先把候选缩到 1~2 个块，
    /// 扫描量下降一个数量级，而搜索范围反而**更大**（chunk 半径 2 覆盖约 48×48 格）。
    ///
    /// 代价：找到的是"最近的且有该资源的 chunk 内的最近格子"，不一定全局最近。
    /// 这个近似是刻意的 —— 行为上看不出差别，但性能差别很大。
    /// </summary>
    public static bool TryFindResource(in ActionContext ctx, ResourceKind kind, out Int2 found, int chunkRadius = 2)
    {
        found = default;
        if (kind == ResourceKind.None) { return false; }

        ChunkGrid chunks = ctx.World.Chunks;
        int centerChunkX = ctx.X / chunks.ChunkSize;
        int centerChunkY = ctx.Y / chunks.ChunkSize;

        int bestChunkIndex = -1;
        int bestChunkDistance = int.MaxValue;

        for (int ring = 0; ring <= chunkRadius; ring++)
        {
            for (int cy = centerChunkY - ring; cy <= centerChunkY + ring; cy++)
            {
                if (cy < 0 || cy >= chunks.ChunkRows) { continue; }

                for (int cx = centerChunkX - ring; cx <= centerChunkX + ring; cx++)
                {
                    if (cx < 0 || cx >= chunks.ChunkCols) { continue; }

                    // 只看当前这一圈（内部块已经在之前的迭代里检查过）
                    int chebyshev = System.Math.Max(System.Math.Abs(cx - centerChunkX), System.Math.Abs(cy - centerChunkY));
                    if (chebyshev != ring) { continue; }

                    ChunkStatsReadOnly stats = chunks.Read(cx, cy);
                    if (!stats.IsValid) { continue; }
                    if (stats.AmountOf(kind) <= 0.01f) { continue; }

                    int distance = chebyshev;
                    if (distance < bestChunkDistance)
                    {
                        bestChunkDistance = distance;
                        bestChunkIndex = chunks.ChunkIndex(cx, cy);
                    }
                }
            }

            // 找到这圈的候选就停止扩张：最近的一圈优先
            if (bestChunkIndex >= 0) { break; }
        }

        if (bestChunkIndex < 0) { return false; }

        int foundChunkX = bestChunkIndex % chunks.ChunkCols;
        int foundChunkY = bestChunkIndex / chunks.ChunkCols;
        chunks.GetBounds(foundChunkX, foundChunkY, out int minX, out int minY, out int maxX, out int maxY);

        Tile[] tiles = ctx.World.Tiles;
        int width = ctx.World.Width;

        int bestDistance = int.MaxValue;
        int bestIndex = -1;

        for (int y = minY; y <= maxY; y++)
        {
            int rowBase = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                ref readonly Tile tile = ref tiles[rowBase + x];
                if (tile.Resource.Kind != kind) { continue; }
                if (tile.Resource.Amount <= 0.01f) { continue; }
                if (!tile.Walkable) { continue; }

                int dx = x - ctx.X;
                int dy = y - ctx.Y;
                int distance = (dx * dx) + (dy * dy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = rowBase + x;
                }
            }
        }

        if (bestIndex < 0) { return false; }

        found = new Int2(bestIndex % width, bestIndex / width);
        return true;
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
    public static float Overstock01(in ActionContext ctx)
    {
        float comfort = ctx.Ai.InventoryComfort;
        if (comfort <= 0.01f) { return 0f; }

        float carried = ctx.Store.InventoryTotalOf(ctx.Slot);
        return SimMath.Clamp01(carried / comfort);
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
