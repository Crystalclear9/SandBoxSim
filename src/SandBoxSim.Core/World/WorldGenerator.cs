using System.Collections.Generic;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Environment;

/// <summary>
/// 世界生成（第 83 / 84 条）。
///
/// 流程：高度图 → 湿度图 → 地形判定 → 肥沃度/植被 → 资源节点。
/// 全程只使用 <see cref="PerlinNoise"/> 与纯整数/浮点运算，因此
/// "同 seed + 同参数 ⇒ 逐格一致"（验收标准 2 的基础）。
///
/// 判定顺序很重要，且是刻意安排的：
///   水（最低） → 山（最高） → 沙滩（临界水边） → 森林（湿度足够） → 草地（默认）
/// 顺序一旦变化，地貌分布就会整体改变，所以这里不做"可配置优先级"——
/// 那会把一个简单可控的规则变成难以推理的组合爆炸。
/// </summary>
public static class WorldGenerator
{
    /// <summary>生成结果里包含的辅助信息（统计与调试用）。</summary>
    public sealed class Result
    {
        public int Seed;
        public float MinHeight = float.MaxValue;
        public float MaxHeight = float.MinValue;
        public float AverageHeight;
        public int WaterTiles;
        public int MountainTiles;
        public int ForestTiles;
        public int GrassTiles;
        public int SandTiles;
        public float TotalWood;
        public float TotalForage;

        /// <summary>本张地图实际采用的水面占比（配置值 + 种子抖动）。</summary>
        public float WaterShare;

        /// <summary>本张地图实际采用的山地占比。</summary>
        public float MountainShare;

        public override string ToString()
            => "seed=" + Seed
             + " water=" + WaterTiles + "(" + (WaterShare * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%)"
             + " mountain=" + MountainTiles + "(" + (MountainShare * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%)"
             + " forest=" + ForestTiles
             + " grass=" + GrassTiles
             + " sand=" + SandTiles
             + " wood=" + TotalWood.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 取升序数组的分位值（分数 f ∈ [0,1]）。用线性插值避免"格子数少时阈值跳变"。
    /// </summary>
    private static float Quantile(float[] sorted, float fraction)
    {
        if (sorted.Length == 0) { return 0.5f; }
        if (sorted.Length == 1) { return sorted[0]; }

        float f = SimMath.Clamp01(fraction);
        float position = f * (sorted.Length - 1);
        int lower = (int)position;
        int upper = lower + 1 >= sorted.Length ? sorted.Length - 1 : lower + 1;
        float t = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * t);
    }

    /// <summary>
    /// 生成一整张地图的 Tile 数组。
    /// </summary>
    /// <param name="config">模拟配置（使用其中的 WorldGen / World / Resources 段）。</param>
    /// <param name="width">地图宽。</param>
    /// <param name="height">地图高。</param>
    /// <param name="seed">地图种子。相同 seed 与相同配置 ⇒ 相同结果。</param>
    public static Tile[] Generate(SimConfig config, int width, int height, int seed, out Result result)
    {
        if (width <= 0) { throw new System.ArgumentOutOfRangeException(nameof(width)); }
        if (height <= 0) { throw new System.ArgumentOutOfRangeException(nameof(height)); }

        WorldGenConfig wg = config.WorldGen;
        ResourceConfig rc = config.Resources;

        var tiles = new Tile[width * height];
        var info = new Result { Seed = seed };

        // 两个独立的噪声场：高度（大陆+山脉）与湿度（森林+肥沃度）。
        // 用不同的派生种子，避免两张图出现相同的纹路（那会让地图看起来"印花布"一样假）。
        var heightNoise = new PerlinNoise(MixSeed((ulong)seed, 0x1111UL));
        var moistureNoise = new PerlinNoise(MixSeed((ulong)seed, 0x2222UL));
        var detailNoise = new PerlinNoise(MixSeed((ulong)seed, 0x3333UL));

        var heights = new float[tiles.Length];
        float heightSum = 0f;

        // ---- 第一遍：高度与湿度 ----
        for (int y = 0; y < height; y++)
        {
            int rowBase = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = rowBase + x;

                float h = heightNoise.Fbm01(
                    x * wg.ContinentFrequency,
                    y * wg.ContinentFrequency,
                    wg.ContinentOctaves,
                    wg.ContinentPersistence);

                // 细节噪声制造海岸线碎屑与土壤差异，避免大片等值区域。
                float detail = detailNoise.Sample01(x * wg.DetailFrequency, y * wg.DetailFrequency);
                h = SimMath.Clamp01(h + ((detail - 0.5f) * 0.06f));

                // 这里**不做**对比度归一化：地形分类改用分位数阈值（见下方说明），
                // 因此保留原始噪声值作为排序依据即可。
                heights[idx] = h;
                heightSum += h;
                if (h < info.MinHeight) { info.MinHeight = h; }
                if (h > info.MaxHeight) { info.MaxHeight = h; }
            }
        }

        info.AverageHeight = tiles.Length > 0 ? heightSum / tiles.Length : 0f;

        // ---- 按分位数确定水面与山地的阈值（关键设计决定）----
        // 为什么不用固定阈值直接比噪声：
        //   fBm 的取值天然聚集在 0.5 附近，且范围随"地图尺寸 × 频率 × 倍频数"变化。
        //   固定阈值会导致小地图上完全没有水域/山地 —— 而这两者是寻路、资源分布
        //   与聚落选址的舞台，缺了它们后续所有涌现行为都无从发生（历史上真的踩过这个坑）。
        // 分位数方案让"每张地图都有水有山"成为结构性保证，
        // 同时用 seed 派生的比例抖动保留"不同地图海陆比例不同"的多样性。
        var shareRng = new DeterministicRandom(MixSeed((ulong)seed, 0x4444UL));
        float waterShare = SimMath.Clamp(
            wg.WaterLevel + ((float)shareRng.Jitter(wg.WaterShareJitter)), 0.05f, 0.6f);
        float mountainShare = SimMath.Clamp(
            wg.MountainLevel + ((float)shareRng.Jitter(wg.MountainShareJitter)), 0.03f, 0.45f);

        float[] sortedHeights = (float[])heights.Clone();
        System.Array.Sort(sortedHeights);

        float waterThreshold = Quantile(sortedHeights, waterShare);
        float mountainThreshold = Quantile(sortedHeights, 1f - mountainShare);
        if (mountainThreshold <= waterThreshold) { mountainThreshold = waterThreshold + 1e-4f; }

        // 归一化高度（0=最低，1=最高）：用于温度与肥沃度的高度依赖，
        // 必须连续而不能直接用分位阈值，否则山地内部会出现台阶。
        float minHeight = sortedHeights.Length > 0 ? sortedHeights[0] : 0f;
        float maxHeight = sortedHeights.Length > 0 ? sortedHeights[sortedHeights.Length - 1] : 1f;
        float heightSpan = maxHeight - minHeight;
        if (heightSpan < 1e-5f) { heightSpan = 1f; }

        var normalizedHeights = new float[heights.Length];
        for (int i = 0; i < heights.Length; i++)
        {
            normalizedHeights[i] = SimMath.Clamp01((heights[i] - minHeight) / heightSpan);
        }

        info.WaterShare = waterShare;
        info.MountainShare = mountainShare;

        // ---- 距离水体的步数（多源 BFS）：用于沙滩判定与未来的"临水肥沃" ----
        int[] distanceToWater = ComputeDistanceToWater(heights, width, height, waterThreshold, out int waterTileCount);
        info.WaterTiles = waterTileCount;

        // ---- 第二遍：地形判定 + 属性 ----
        float ambientTemp = config.World.AmbientTemperature;
        float ambientMoisture = config.World.AmbientMoisture;

        for (int y = 0; y < height; y++)
        {
            int rowBase = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = rowBase + x;
                float h = normalizedHeights[idx];

                float moisture = moistureNoise.Fbm01(
                    x * wg.MoistureFrequency,
                    y * wg.MoistureFrequency,
                    wg.MoistureOctaves,
                    wg.MoisturePersistence);

                // 湿度：基准值 + 噪声中心化 + 临水加成（越靠近水越湿）。
                float waterProximity = 0f;
                if (distanceToWater[idx] > 0)
                {
                    waterProximity = SimMath.Clamp01(1f - (distanceToWater[idx] / 12f)) * 0.25f;
                }
                moisture = SimMath.Clamp01(
                    (moisture - 0.5f) + ambientMoisture + waterProximity);

                // 温度：基准 - 海拔降温，保证山上明显更冷（h 已是归一化高度）。
                float temperature = SimMath.Clamp01(ambientTemp - ((h - 0.5f) * 0.5f));

                // 地形判定用**原始高度 + 分位阈值**（等价于按排名取前 N% 为水/山）。
                float rawHeight = heights[idx];
                TerrainKind terrain;
                if (rawHeight <= waterThreshold)
                {
                    terrain = TerrainKind.Water;

                    // 水域的湿度按定义饱和：水里没有"干"的说法。
                    // 这不仅是物理直觉，也让"临水而居"的加成有确定的数据基础。
                    moisture = 1f;
                }
                else if (rawHeight >= mountainThreshold)
                {
                    terrain = TerrainKind.Mountain;
                }
                else if (distanceToWater[idx] <= 2
                    && (rawHeight - waterThreshold) <= (mountainThreshold - waterThreshold) * wg.SandBand * 4f)
                {
                    terrain = TerrainKind.Sand;
                }
                else
                {
                    // 森林概率随湿度上升；同时偏高海拔更易成林（山腰林带）。
                    float forestChance = wg.ForestDensityBonus + ((moisture - wg.ForestMoistureThreshold) * 1.6f);
                    if (h > 0.65f) { forestChance += 0.12f; }

                    float roll = detailNoise.Sample01((x + 1000) * wg.DetailFrequency, (y - 1000) * wg.DetailFrequency);
                    bool forest = moisture >= wg.ForestMoistureThreshold && roll < forestChance;
                    terrain = forest ? TerrainKind.Forest : TerrainKind.Grass;
                }

                float fertility = ComputeFertility(wg, detailNoise, x, y, h, moisture, terrain);
                Tile tile = Tile.CreateDefault(terrain, fertility, moisture, temperature);
                tile.Height = h;

                // 植被：森林拉满，草地中等；山地/沙地/水域没有植被。
                switch (terrain)
                {
                    case TerrainKind.Forest:
                        tile.Vegetation = 1f;
                        break;
                    case TerrainKind.Grass:
                        tile.Vegetation = SimMath.Clamp01(0.35f + (moisture * 0.35f));
                        break;
                    case TerrainKind.Sand:
                        tile.Vegetation = 0.05f;
                        break;
                    default:
                        tile.Vegetation = 0f;
                        break;
                }

                // 资源节点
                switch (terrain)
                {
                    case TerrainKind.Forest:
                    {
                        float cap = rc.WoodCapacityPerForestTile;
                        tile.Resource = new ResourceNode
                        {
                            Kind = ResourceKind.Wood,
                            Capacity = cap,
                            Amount = cap * SimMath.Clamp01(rc.WoodInitialFraction),
                            RegenerationRate = rc.WoodGrowthRate,
                        };
                        info.TotalWood += tile.Resource.Amount;
                        info.ForestTiles++;
                        break;
                    }
                    case TerrainKind.Mountain:
                    {
                        // 山地里只让一部分格子含铁（稀有性），石头则几乎处处都有。
                        float ironRoll = detailNoise.Sample01((x - 5000) * 0.21f, (y + 5000) * 0.21f);
                        if (ironRoll > 0.86f)
                        {
                            float ironCap = rc.IronCapacityPerMountainTile;
                            tile.Resource = new ResourceNode
                            {
                                Kind = ResourceKind.Iron,
                                Capacity = ironCap,
                                Amount = ironCap * SimMath.Clamp01(rc.IronInitialFraction),
                                RegenerationRate = 0f, // 矿产不可再生
                            };
                        }
                        else
                        {
                            float stoneCap = rc.StoneCapacityPerMountainTile;
                            tile.Resource = new ResourceNode
                            {
                                Kind = ResourceKind.Stone,
                                Capacity = stoneCap,
                                Amount = stoneCap * SimMath.Clamp01(rc.StoneInitialFraction),
                                RegenerationRate = 0f,
                            };
                        }
                        info.MountainTiles++;
                        break;
                    }
                    case TerrainKind.Grass:
                    case TerrainKind.Sand:
                    {
                        // 野生食物（采集/浆果/猎物）。肥沃度直接影响存量。
                        float cap = rc.FoodCapacityPerGrassTile * (0.4f + (fertility * 0.6f));
                        tile.Resource = new ResourceNode
                        {
                            Kind = ResourceKind.Food,
                            Capacity = cap,
                            Amount = cap * SimMath.Clamp01(rc.FoodInitialFraction),
                            RegenerationRate = rc.FoodGrowthRate,
                        };
                        info.TotalForage += tile.Resource.Amount;
                        if (terrain == TerrainKind.Grass) { info.GrassTiles++; }
                        else { info.SandTiles++; }
                        break;
                    }
                    default:
                        tile.Resource = default;
                        break;
                }

                // 水域计数已在 ComputeDistanceToWater 里统计（info.WaterTiles），这里不重复累加。
                tiles[idx] = tile;
            }
        }

        result = info;
        return tiles;
    }

    /// <summary>
    /// 肥沃度：高度适中（不是山脊也不是水边）最肥；再叠加噪声制造"肥沃地带"，
    /// 这样农业会自然集中在特定区域，而不会全图均匀铺开 —— 这也是"农业区 → 贸易"的前置条件。
    /// </summary>
    private static float ComputeFertility(
        WorldGenConfig wg, PerlinNoise detailNoise, int x, int y, float height, float moisture, TerrainKind terrain)
    {
        if (terrain == TerrainKind.Water || terrain == TerrainKind.Mountain) { return 0.05f; }
        if (terrain == TerrainKind.Sand) { return 0.15f; }

        float baseFertility = wg.FertilityBase;

        if (wg.FertilityFromHeight)
        {
            // 以 0.5 高度为最优，向两端衰减。
            float heightFactor = 1f - (System.Math.Abs(height - 0.5f) * 1.4f);
            baseFertility *= SimMath.Clamp(heightFactor, 0.2f, 1f);
        }

        float noise = detailNoise.Sample01(x * wg.FertilityNoiseFrequency, y * wg.FertilityNoiseFrequency);
        float fertility = (baseFertility * 0.5f) + (noise * 0.5f);
        fertility = SimMath.Clamp01((fertility * 0.75f) + (moisture * 0.25f));
        return SimMath.Clamp(fertility, 0.05f, 1f);
    }

    /// <summary>
    /// 多源 BFS 求每格到最近水域的步数（四方向）。
    /// 用水域作为源而不是"逐格搜索最近水"，因为网格上的多源 BFS 是 O(n)，
    /// 而逐格搜索是 O(n·寻找)，在放大到 500×500 时差距是数量级。
    /// </summary>
    private static int[] ComputeDistanceToWater(float[] heights, int width, int height, float waterLevel, out int waterTiles)
    {
        int[] dist = new int[heights.Length];
        var queue = new Queue<int>();
        waterTiles = 0;

        for (int i = 0; i < dist.Length; i++)
        {
            if (heights[i] < waterLevel)
            {
                dist[i] = 0;
                queue.Enqueue(i);
                waterTiles++;
            }
            else
            {
                dist[i] = int.MaxValue;
            }
        }

        // 整图无水的极端情况：所有格子都当作"远离水"，避免 int.MaxValue 参与运算溢出。
        if (queue.Count == 0)
        {
            for (int i = 0; i < dist.Length; i++) { dist[i] = 999; }
            return dist;
        }

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % width;
            int cy = cur / width;
            int nextDist = dist[cur] + 1;

            if (cx > 0) { Relax(cur - 1, nextDist); }
            if (cx < width - 1) { Relax(cur + 1, nextDist); }
            if (cy > 0) { Relax(cur - width, nextDist); }
            if (cy < height - 1) { Relax(cur + width, nextDist); }

            void Relax(int neighbor, int candidate)
            {
                if (dist[neighbor] > candidate)
                {
                    dist[neighbor] = candidate;
                    queue.Enqueue(neighbor);
                }
            }
        }

        // 仍未到达的（被山完全围住的封闭盆地）：给一个有限上限，保留"内陆"语义。
        for (int i = 0; i < dist.Length; i++)
        {
            if (dist[i] == int.MaxValue) { dist[i] = 999; }
        }
        return dist;
    }

    private static ulong MixSeed(ulong seed, ulong pipe)
    {
        unchecked
        {
            ulong z = seed + (pipe * 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
