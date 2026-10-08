using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Core;

/// <summary>
/// 一次逐日统计采样（第 59 节数据统计系统）。
/// </summary>
public struct DailySample
{
    public int Day;
    public int Population;
    public float Food;
    public float Wood;
    public float Stone;
    public float Iron;
    public float AverageMoisture;
    public float AverageTemperature;
    public int Births;
    public int Deaths;
    public int Migrations;
    public int SettlementCount;
    public int BuildingCount;
    public int BurningTiles;
    public int ForestTiles;
    public int FarmlandTiles;
    public float Scarcity;

    public override string ToString()
        => "D" + Day + " pop=" + Population + " food=" + Food.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
         + " wood=" + Wood.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// 统计系统（第 59 / 91 节）。
///
/// 设计要点：统计**只读**模拟状态，绝不反过来影响模拟 —— 否则"打开统计面板"
/// 就会改变世界演化，玩家实验的可复现性直接消失。
/// 采样频率是每天一次（DailySample），小时级指标只在需要时计算。
/// </summary>
public sealed class SimulationStats
{
    private readonly System.Collections.Generic.List<DailySample> _daily = new System.Collections.Generic.List<DailySample>(512);

    /// <summary>累计出生数。</summary>
    public int TotalBirths { get; private set; }

    /// <summary>累计死亡数。</summary>
    public int TotalDeaths { get; private set; }

    /// <summary>累计迁移次数（Agent 离开原聚落并加入/新建其他聚落）。</summary>
    public int TotalMigrations { get; private set; }

    /// <summary>历史最高人口。</summary>
    public int PeakPopulation { get; private set; }

    /// <summary>最低人口（世界重建后重置）。</summary>
    public int LowestPopulation { get; private set; } = int.MaxValue;

    /// <summary>当前是否处于饥荒状态（食物不足以支撑人口）。</summary>
    public bool FamineActive { get; private set; }

    /// <summary>历史上曾发生过饥荒的次数（用于"世界故事"时间线）。</summary>
    public int FamineEpisodeCount { get; private set; }

    public System.Collections.Generic.IReadOnlyList<DailySample> Daily => _daily;

    public DailySample Latest => _daily.Count > 0 ? _daily[_daily.Count - 1] : default;

    public void RecordBirth() => TotalBirths++;
    public void RecordDeath() => TotalDeaths++;
    public void RecordMigration() => TotalMigrations++;

    /// <summary>
    /// 读档时恢复累计计数。
    ///
    /// 为什么这是**必须的**而不是"报告才用得上"：这三个计数直接参与状态摘要
    /// （见 <see cref="StateHash.Compute"/>）。不恢复它们会让"读档后摘要不一致"，
    /// 从而把整条确定性验收变成假失败 —— 而且失败点看起来像"读档坏了"，
    /// 实际上是统计没跟上。凡进摘要的状态都必须能被恢复，这条没有例外。
    /// </summary>
    public void RestoreCounters(int totalBirths, int totalDeaths, int totalMigrations)
    {
        TotalBirths = totalBirths;
        TotalDeaths = totalDeaths;
        TotalMigrations = totalMigrations;
    }

    /// <summary>
    /// 记录一天的样本。famine 判定也在这里做：这样"饥荒"是统计口径的结果，
    /// 而不是某处硬编码的剧情开关（第 2 条涌现性）。
    /// </summary>
    public void RecordDay(DailySample sample, float foodPerCapitaThreshold)
    {
        _daily.Add(sample);

        if (sample.Population > PeakPopulation) { PeakPopulation = sample.Population; }
        if (sample.Population < LowestPopulation) { LowestPopulation = sample.Population; }

        bool famine = sample.Population > 0 && sample.FoodPerCapita() < foodPerCapitaThreshold;
        if (famine && !FamineActive)
        {
            FamineEpisodeCount++;
            FamineActive = true;
        }
        else if (!famine)
        {
            FamineActive = false;
        }
    }

    public int DaysRecorded => _daily.Count;
    public JsonValue EncodeHistory()
    {
        var daily = JsonValue.Array();
        foreach (var sample in _daily) { daily.Add(JsonBinder.ToJson(sample)); }
        return JsonValue.Object().Set("daily", daily).Set("peak", JsonValue.From(PeakPopulation))
            .Set("lowest", JsonValue.From(LowestPopulation)).Set("famine", JsonValue.From(FamineActive))
            .Set("episodes", JsonValue.From(FamineEpisodeCount));
    }
    public void RestoreHistory(JsonValue value)
    {
        _daily.Clear();
        var daily = value.Get("daily");
        if (daily.IsArray)
            foreach (var entry in daily.Items)
            {
                object boxed = new DailySample();
                JsonBinder.Bind(entry, boxed, new System.Collections.Generic.List<string>(), "");
                _daily.Add((DailySample)boxed);
            }
        PeakPopulation = value.GetInt("peak");
        LowestPopulation = value.GetInt("lowest", int.MaxValue);
        FamineActive = value.GetBool("famine");
        FamineEpisodeCount = value.GetInt("episodes");
    }

    /// <summary>取最近 n 天的样本（UI 画曲线用）。</summary>
    public DailySample[] RecentDays(int count)
    {
        if (count <= 0) { return System.Array.Empty<DailySample>(); }
        int start = _daily.Count - count;
        if (start < 0) { start = 0; }
        int length = _daily.Count - start;
        var result = new DailySample[length];
        for (int i = 0; i < length; i++) { result[i] = _daily[start + i]; }
        return result;
    }

    public void Reset()
    {
        _daily.Clear();
        TotalBirths = 0;
        TotalDeaths = 0;
        TotalMigrations = 0;
        PeakPopulation = 0;
        LowestPopulation = int.MaxValue;
        FamineActive = false;
        FamineEpisodeCount = 0;
    }
}

/// <summary>
/// DailySample 的补充属性：人均食物。放在这里而不是结构体字段，
/// 是为了保证"人均"永远由当前公式计算，而不是某处算错后被冻结进历史数据。
/// </summary>
public static class DailySampleExtensions
{
    public static float FoodPerCapita(this DailySample sample)
    {
        if (sample.Population <= 0) { return sample.Food; }
        return sample.Food / sample.Population;
    }
}

/// <summary>
/// 状态摘要（第 76 / 77 节；验收标准 2 的判据）。
///
/// 只对**模拟状态**取摘要：世界种子、tick、地形与资源、agent 状态、库存、聚落、事件计数。
/// 刻意**不包含** UI 状态、渲染缓存、RNG 内部状态（后者单独由存档保证）。
/// 这样"同 seed 两跑 digest 一致"就是一句可以自动化的断言。
/// </summary>
public static class StateHash
{
    public static ulong Compute(Simulation sim)
    {
        ulong hash = Hash64.Begin();

        Environment.World world = sim.World;
        hash = Hash64.Combine(hash, world.Seed);
        hash = Hash64.Combine(hash, (int)world.Tick);
        hash = Hash64.Combine(hash, world.Width);
        hash = Hash64.Combine(hash, world.Height);
        hash = Hash64.Combine(hash, (int)world.Weather.Kind);
        hash = Hash64.Combine(hash, world.Weather.DurationHours);

        // 逐格摘要。资源量量化到 0.01 再哈希：
        // 浮点末位在跨运行时/跨平台下可能有 1 ULP 差异，量化可以在保留"行为等价"判据的同时
        // 避免把无害的表示差异误判为模拟分歧。地形是整数，逐位参与。
        Tile[] tiles = world.Tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            hash = Hash64.Combine(hash, (int)tiles[i].Terrain);
            hash = Hash64.Combine(hash, tiles[i].Height);
            hash = Hash64.Combine(hash, tiles[i].Walkable);
            hash = Hash64.Combine(hash, tiles[i].Buildable);
            hash = Hash64.Combine(hash, tiles[i].Temperature);
            hash = Hash64.Combine(hash, tiles[i].Vegetation);
            if(tiles[i].FootTraffic!=0)hash=Hash64.Combine(hash,tiles[i].FootTraffic);
            hash = Hash64.Combine(hash, tiles[i].Resource.Capacity);
            hash = Hash64.Combine(hash, tiles[i].Resource.RegenerationRate);
            hash = Hash64.Combine(hash, (int)tiles[i].Fire);
            hash = Hash64.Combine(hash, (int)tiles[i].Resource.Kind);
            hash = Hash64.Combine(hash, (int)(tiles[i].Resource.Amount * 100f));
            hash = Hash64.Combine(hash, (int)(tiles[i].Moisture * 1000f));
            hash = Hash64.Combine(hash, (int)(tiles[i].Fertility * 1000f));
            hash = Hash64.Combine(hash, tiles[i].BuildingId);
        }

        hash = sim.HashEntities(hash);

        // 共享库存（M3）：它不在实体集合里（它是"建筑的附属数据"而不是独立实体），
        // 因此必须在这里显式混入 —— 否则"仓库里多了 1000 木材"不会改变摘要，
        // 而那是实实在在的世界状态变化。**任何持久的模拟状态都必须进摘要，
        // 判断标准是"它会不会影响未来的行为"，而不是"它有没有自己的类"。**
        hash = sim.Storage.HashInto(hash);

        hash = Hash64.Combine(hash, sim.ResourceSystem.DepletionEvents);
        hash = Hash64.Combine(hash, sim.Stats.TotalBirths);
        hash = Hash64.Combine(hash, sim.Stats.TotalDeaths);
        hash = Hash64.Combine(hash, sim.Stats.TotalMigrations);

        return hash;
    }

    public static string ComputeDigest(Simulation sim) => Hash64.ToDigestString(Compute(sim));

    /// <summary>
    /// 逐段摘要（调试与存档自校验用）。
    ///
    /// 存在的理由很直接：只报"总摘要不一致"对定位毫无帮助 ——
    /// 世界里有 10 万格地形、几十个个体、还有动物/建筑/库存/统计，
    /// 从一个大数字反推是哪一段漏了状态是不可能的。
    /// 分段之后，失败信息能直接说"是动物那一段不一致"。
    ///
    /// 这也正是实现存档时被真实用到的工具：四个"隐形状态"字段
    /// 都是靠分段对比 + 逐字段对比才定位出来的。
    /// </summary>
    public static string DescribeSegments(Simulation sim)
    {
        Environment.World world = sim.World;

        ulong worldHash = Hash64.Begin();
        worldHash = Hash64.Combine(worldHash, world.Seed);
        worldHash = Hash64.Combine(worldHash, (int)world.Tick);
        worldHash = Hash64.Combine(worldHash, world.Width);
        worldHash = Hash64.Combine(worldHash, world.Height);
        worldHash = Hash64.Combine(worldHash, (int)world.Weather.Kind);
        worldHash = Hash64.Combine(worldHash, world.Weather.DurationHours);

        ulong tileHash = Hash64.Begin();
        Tile[] tiles = world.Tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            tileHash = Hash64.Combine(tileHash, (int)tiles[i].Terrain);
            tileHash = Hash64.Combine(tileHash, tiles[i].Height);
            tileHash = Hash64.Combine(tileHash, tiles[i].Walkable);
            tileHash = Hash64.Combine(tileHash, tiles[i].Buildable);
            tileHash = Hash64.Combine(tileHash, tiles[i].Temperature);
            tileHash = Hash64.Combine(tileHash, tiles[i].Vegetation);
            if(tiles[i].FootTraffic!=0)tileHash=Hash64.Combine(tileHash,tiles[i].FootTraffic);
            tileHash = Hash64.Combine(tileHash, tiles[i].Resource.Capacity);
            tileHash = Hash64.Combine(tileHash, tiles[i].Resource.RegenerationRate);
            tileHash = Hash64.Combine(tileHash, (int)tiles[i].Fire);
            tileHash = Hash64.Combine(tileHash, (int)tiles[i].Resource.Kind);
            tileHash = Hash64.Combine(tileHash, (int)(tiles[i].Resource.Amount * 100f));
            tileHash = Hash64.Combine(tileHash, (int)(tiles[i].Moisture * 1000f));
            tileHash = Hash64.Combine(tileHash, (int)(tiles[i].Fertility * 1000f));
            tileHash = Hash64.Combine(tileHash, tiles[i].BuildingId);
        }

        ulong statsHash = Hash64.Begin();
        statsHash = Hash64.Combine(statsHash, sim.ResourceSystem.DepletionEvents);
        statsHash = Hash64.Combine(statsHash, sim.Stats.TotalBirths);
        statsHash = Hash64.Combine(statsHash, sim.Stats.TotalDeaths);
        statsHash = Hash64.Combine(statsHash, sim.Stats.TotalMigrations);

        // 顺序固定（不依赖字典），因此两边的字符串可以直接比对
        return string.Join(";", new[]
        {
            "world=" + Hash64.ToDigestString(worldHash),
            "tiles=" + Hash64.ToDigestString(tileHash),
            "agents=" + Hash64.ToDigestString(sim.Agents.HashInto(Hash64.Begin())),
            "wildlife=" + Hash64.ToDigestString(sim.Wildlife.HashInto(Hash64.Begin())),
            "buildings=" + Hash64.ToDigestString(sim.Buildings.HashInto(Hash64.Begin())),
            "stocks=" + Hash64.ToDigestString(sim.GroundStocks.HashInto(Hash64.Begin())),
            "storage=" + Hash64.ToDigestString(sim.Storage.HashInto(Hash64.Begin())),
            "stats=" + Hash64.ToDigestString(statsHash),
        });
    }

    /// <summary>
    /// 比较两段分段摘要，返回第一处不同的段落名（全同则返回空串）。
    /// 用于把"总摘要不一致"翻译成"哪一段不一致"。
    /// </summary>
    public static string FirstSegmentDifference(string expected, string actual)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual)) { return string.Empty; }

        string[] a = expected.Split(';');
        string[] b = actual.Split(';');

        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            if (!string.Equals(a[i], b[i], System.StringComparison.Ordinal))
            {
                return a[i] + "（读档后为 " + b[i] + "）";
            }
        }

        return a.Length == b.Length ? string.Empty : "段落数不同";
    }
}
