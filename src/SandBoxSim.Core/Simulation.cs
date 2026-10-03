using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Core;

/// <summary>
/// 任何"住在世界里的实体集合"（agent、建筑、聚落……）都要实现它，
/// 这样 <see cref="Simulation"/> 不需要为每种实体写一遍"重置/摘要/不变量检查"。
///
/// M0 阶段还没有实体，但接口先立起来：它的存在本身就是架构约束 ——
/// 新系统必须能回答"我的状态怎么摘要、怎么校验"，否则无法接入确定性与调试体系。
/// </summary>
public interface ISimEntitySet
{
    /// <summary>实体数量（0 表示空集合）。</summary>
    int EntityCount { get; }

    /// <summary>把自身状态混入状态摘要（必须遍历固定顺序，不能依赖字典迭代）。</summary>
    ulong HashInto(ulong hash);

    /// <summary>清空（重置世界时调用）。</summary>
    void Reset();
}

/// <summary>
/// 模拟内核的顶层门面（第 4 / 8 节的落地）。
///
/// 它做三件事，也只做三件事：
///   1. 拥有世界与各系统；
///   2. 按固定顺序推进 tick 管线；
///   3. 对表现层暴露只读查询与**玩家干预**接口。
///
/// 它不实现任何具体模拟规则（再生在 ResourceSystem、决策在 AiSystem……），
/// 因此不会长成一个几千行的 God class（第 99 条）。
///
/// tick 管线顺序是**确定性的契约**：任何两个系统之间的顺序改动都会改变世界演化，
/// 因此顺序写死在 <see cref="Tick"/> 里，不做可配置。
/// </summary>
public sealed class Simulation
{
    /// <summary>全局唯一时间基准（游戏分钟）。</summary>
    public long Clock => World.Tick;

    public Environment.World World { get; }
    public SimConfig Config { get; }

    /// <summary>分流随机源 —— 模拟内核唯一允许的随机来源。</summary>
    public SimRandom Random { get; private set; }

    public ResourceSystem ResourceSystem { get; }
    public SimulationStats Stats { get; } = new SimulationStats();

    /// <summary>世界事件日志（第 55 / 56 节）。M0 只记录地形/世界级事件。</summary>
    public History.EventLog Events { get; } = new History.EventLog();

    /// <summary>本 tick 内发生变化的格子数（调试与性能观测用）。</summary>
    public int LastTickDirtyChunks => World.Chunks.DirtyCount;

    /// <summary>当前执行到的 tick 序号（= World.Tick）。</summary>
    public long TickCount => World.Tick;

    /// <summary>世界生成时的信息（菜单与报告显示用）。</summary>
    public Environment.WorldGenerator.Result? GenerationInfo { get; private set; }

    // ---- 小时的周期回调（表现层与后续系统挂接） ----
    public event System.Action<Simulation>? HourAdvanced;
    public event System.Action<Simulation>? DayAdvanced;

    /// <summary>每小时/每天是否已经触发过（供测试确认调度正确）。</summary>
    public int HourEventsFired { get; private set; }
    public int DayEventsFired { get; private set; }

    /// <summary>最近一次每日采样的结果。</summary>
    public DailySample LastDailySample { get; private set; }

    public Simulation(SimConfig config, int width, int height, int seed)
    {
        Config = config ?? throw new System.ArgumentNullException(nameof(config));
        World = new Environment.World(config, width, height, seed);
        Random = new SimRandom(unchecked((ulong)seed));
        ResourceSystem = new ResourceSystem(World, config);
        RegenerateWorld(seed);
    }

    /// <summary>
    /// 重新生成世界（换 seed 或重置）。**不重置日历之外的东西**：
    /// 这是"新世界"操作，不是"回退一步"，因此统计与事件也要一起重置。
    /// </summary>
    public void RegenerateWorld(int seed)
    {
        Tile[] tiles = WorldGenerator.Generate(Config, World.Width, World.Height, seed, out WorldGenerator.Result info);
        World.ReplaceAllTiles(tiles, seed);
        World.RestoreTick(0);
        World.ResetWeather();
        Random = new SimRandom(unchecked((ulong)seed));
        ResourceSystem.ResetStatistics();
        Stats.Reset();
        Events.Clear();
        GenerationInfo = info;

        World.RefreshSpatialIndex();

        Events.Record(0, History.WorldEventType.WorldGenerated, "世界生成：seed=" + seed
            + " 森林=" + info.ForestTiles + " 草地=" + info.GrassTiles
            + " 水域=" + info.WaterTiles + " 山地=" + info.MountainTiles);
    }

    /// <summary>
    /// 推进一个模拟 tick。
    ///
    /// 管线顺序（不可随意调整）：
    ///   1. 时钟 +1
    ///   2. FastTick：施工/生产/火势（每 10 tick）
    ///   3. HourTick：天气 → 资源再生 → 汇总湿度/温度（每 60 tick）
    ///   4. DailyTick：采样 → 年龄/出生/死亡/迁移（每 1440 tick）
    ///   5. 刷新空间索引（所有改变格子的系统都在此之前完成写入）
    /// </summary>
    public void Tick(int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            World.Calendar.Advance(1);

            if (IsFastTick) { TickFast(); }
            if (World.Calendar.IsHourBoundary) { TickHourInternal(); }
            if (World.Calendar.IsDayBoundary) { TickDayInternal(); }

            World.RefreshSpatialIndex();

            if (Config.Debug.AssertInvariants && (World.Tick % 64 == 0))
            {
                ValidateInvariants();
            }
        }
    }

    /// <summary>每 10 个 tick 一次的高频系统（火灾蔓延、施工推进、农场生长）。</summary>
    public const int FastTickInterval = 10;

    public bool IsFastTick => World.Tick % FastTickInterval == 0;

    /// <summary>高频 tick：M0 无内容，M5 接入火灾与施工。</summary>
    public void TickFast()
    {
        // 预留：FireSystem.TickFast(this); ConstructionSystem.TickFast(this);
    }

    private void TickHourInternal()
    {
        WeatherTick();
        if (ResourceSystem.RegeneratesHourly)
        {
            ResourceSystem.Regenerate(1.0 / 24.0);
        }
        TickHour();
        HourEventsFired++;
        HourAdvanced?.Invoke(this);
    }

    private void TickDayInternal()
    {
        if (!ResourceSystem.RegeneratesHourly)
        {
            ResourceSystem.Regenerate(1.0);
        }

        TickDay();

        DailySample sample = BuildDailySample();
        LastDailySample = sample;
        Stats.RecordDay(sample, FoodPerCapitaFamineThreshold);
        DayEventsFired++;
        DayAdvanced?.Invoke(this);
    }

    /// <summary>每小时逻辑（后续各系统在此挂接：AI 分工需求刷新、聚落评估……）。</summary>
    public void TickHour()
    {
        // 预留扩展点。
    }

    /// <summary>每天逻辑（后续各系统在此挂接：年龄、出生、死亡、迁移、聚落升档）。</summary>
    public void TickDay()
    {
        // 预留扩展点。
    }

    /// <summary>
    /// 天气推进（第 42 节）。天气直接写进 Tile 的湿度/温度，
    /// 这样"天气 → 作物 → 食物 → 人口"的因果链是真实数据流，而不是设定文案。
    /// </summary>
    public void WeatherTick()
    {
        float avgMoisture = World.AverageMoisture();
        float avgTemperature = World.AverageTemperature();

        World.Weather.AdvanceHour(Random.Get(RngStream.Weather), avgMoisture, avgTemperature);

        float moistureDelta = WeatherInfo.MoistureDeltaPerHour(World.Weather.Kind);
        float temperatureDelta = WeatherInfo.TemperatureDeltaPerHour(World.Weather.Kind);

        if (moistureDelta == 0f && temperatureDelta == 0f) { return; }

        Tile[] tiles = World.Tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            ref Tile tile = ref tiles[i];
            float targetMoisture = SimMath.Clamp01(tile.Moisture + moistureDelta);
            float targetTemperature = SimMath.Clamp01(tile.Temperature + temperatureDelta);

            // 水域恒为饱和湿度，且温度变化平缓（水体热惯性）。
            if (tile.Terrain == TerrainKind.Water)
            {
                targetMoisture = 1f;
                targetTemperature = SimMath.Clamp01(tile.Temperature + (temperatureDelta * 0.4f));
            }

            if (targetMoisture != tile.Moisture || targetTemperature != tile.Temperature)
            {
                tile.Moisture = targetMoisture;
                tile.Temperature = targetTemperature;
                World.MarkDirtyAt(i % World.Width, i / World.Width);
            }
        }
    }

    /// <summary>饥荒判定的门槛：人均食物低于此值算饥荒（第 8 节人口模型）。</summary>
    public const float FoodPerCapitaFamineThreshold = 2.0f;

    /// <summary>构造当天的统计样本（只读，不改变模拟状态）。</summary>
    public DailySample BuildDailySample()
    {
        Environment.World world = World;
        int[] terrainCounts = world.CountTerrain();

        var sample = new DailySample
        {
            // 用 CompletedDay 而不是 Calendar.Day：日边界上 Day 已经翻页，
            // 样本描述的是刚结束的那一天（见 Calendar.CompletedDay 的注释）。
            Day = world.Calendar.CompletedDay,
            Population = PopulationCount,
            Food = world.TotalResource(ResourceKind.Food),
            Wood = world.TotalResource(ResourceKind.Wood),
            Stone = world.TotalResource(ResourceKind.Stone),
            Iron = world.TotalResource(ResourceKind.Iron),
            AverageMoisture = world.AverageMoisture(),
            AverageTemperature = world.AverageTemperature(),
            Births = Stats.TotalBirths,
            Deaths = Stats.TotalDeaths,
            Migrations = Stats.TotalMigrations,
            SettlementCount = EntityCount,
            BuildingCount = BuildingCount,
            ForestTiles = terrainCounts[(int)TerrainKind.Forest],
            FarmlandTiles = terrainCounts[(int)TerrainKind.Farmland],
            BurningTiles = 0,
            Scarcity = (float)ResourceSystem.GlobalScarcity(),
        };

        // 燃烧格子数需要跨 chunk 求和（用 chunk 缓存避免扫全图）。
        int burning = 0;
        for (int c = 0; c < world.Chunks.ChunkCount; c++)
        {
            burning += world.Chunks.ReadIndex(c).BurningTiles;
        }
        sample.BurningTiles = burning;

        return sample;
    }

    // ---------------------------------------------------------------------
    // 实体集合（M1+ 由各系统注册，Simulation 只做统一转发）
    // ---------------------------------------------------------------------

    private readonly System.Collections.Generic.List<ISimEntitySet> _entitySets = new System.Collections.Generic.List<ISimEntitySet>(4);

    /// <summary>注册一个实体集合（幂等：同一实例只注册一次）。</summary>
    public void RegisterEntitySet(ISimEntitySet set)
    {
        if (set == null) { return; }
        if (!_entitySets.Contains(set)) { _entitySets.Add(set); }
    }

    public System.Collections.Generic.IReadOnlyList<ISimEntitySet> EntitySets => _entitySets;

    /// <summary>当前实体总数（人口/建筑等，取决于已注册的集合）。</summary>
    public int EntityCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _entitySets.Count; i++) { total += _entitySets[i].EntityCount; }
            return total;
        }
    }

    /// <summary>人口计数（M1 之后由 AgentStore 提供；M0 恒为 0）。</summary>
    public int PopulationCount { get; private set; }

    /// <summary>建筑计数（M3 之后由 BuildingStore 提供；M0 恒为 0）。</summary>
    public int BuildingCount { get; private set; }

    /// <summary>由实体系统在每 tick 结束时同步的只读计数（供统计与 UI 使用）。</summary>
    public void SetPopulationAndBuildings(int population, int buildings)
    {
        PopulationCount = population;
        BuildingCount = buildings;
    }

    /// <summary>把全部实体状态混入摘要。</summary>
    public ulong HashEntities(ulong hash)
    {
        for (int i = 0; i < _entitySets.Count; i++)
        {
            hash = _entitySets[i].HashInto(hash);
        }
        return hash;
    }

    /// <summary>当前状态摘要（验收标准 2）。</summary>
    public ulong StateDigest() => StateHash.Compute(this);

    public string StateDigestString() => Hash64.ToDigestString(StateDigest());

    // ---------------------------------------------------------------------
    // 玩家干预接口（第 45 / 46 节：只改变"条件"，不直接产生"结果"）
    //
    // 注意：这里是**上游输入**，因此必须记录事件，让玩家能在日志里看到"我做了什么"。
    // 但干预不参与 tick 管线，也不影响确定性（它改变的是初始条件，不是随机序列）。
    // ---------------------------------------------------------------------

    /// <summary>改变地形（第 45 节 Terrain 工具）。</summary>
    public void InterveneSetTerrain(int x, int y, TerrainKind terrain, bool reapplyResource = true)
    {
        if (!World.IsInBounds(x, y)) { return; }
        World.SetTerrain(x, y, terrain);
        if (reapplyResource)
        {
            World.ApplyDefaultResource(x, y);
        }
        else
        {
            World.ClearResource(x, y);
        }
        Events.Record(Clock, History.WorldEventType.TerrainChanged,
            "玩家改变地形为 " + TerrainInfo.NameOf(terrain) + " @ " + new Int2(x, y));
    }

    /// <summary>增加一批森林（"Grow Forest"）：把草地变为森林并补满木材。</summary>
    public int InterveneGrowForest(int centerX, int centerY, int radius, double density)
    {
        int changed = 0;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (!World.IsInBounds(x, y)) { continue; }
                float dist = (float)System.Math.Max(System.Math.Abs(x - centerX), System.Math.Abs(y - centerY));
                if (dist > radius) { continue; }

                ref readonly Tile tile = ref World.TileAt(x, y);
                if (tile.Terrain != TerrainKind.Grass && tile.Terrain != TerrainKind.Sand) { continue; }

                float chance = (float)density * (1f - (dist / (radius + 1f)));
                if (Random.Get(RngStream.Events).NextDouble() > chance) { continue; }

                World.SetTerrain(x, y, TerrainKind.Forest);
                World.SetVegetation(x, y, 1f);
                World.ApplyDefaultResource(x, y);
                changed++;
            }
        }

        if (changed > 0)
        {
            Events.Record(Clock, History.WorldEventType.TerrainChanged,
                "玩家在 " + new Int2(centerX, centerY) + " 附近催生森林，新增 " + changed + " 格");
        }
        return changed;
    }

    /// <summary>增加资源（第 45 节 Resource 工具）。</summary>
    public float InterveneAddResource(int x, int y, ResourceKind kind, float amount)
    {
        float added = ResourceSystem.Inject(x, y, kind, amount);
        if (added > 0f)
        {
            Events.Record(Clock, History.WorldEventType.ResourceInjected,
                "玩家注入 " + ResourceInfo.NameOf(kind) + " +" + added.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                + " @ " + new Int2(x, y));
        }
        return added;
    }

    /// <summary>按资源种类修改配置倍率（第 45 节 Rules 工具的 M0 版本）。</summary>
    public void InterveneMultiplyRegeneration(float factor)
    {
        Config.Resources.WoodGrowthRate *= factor;
        Config.Resources.FoodGrowthRate *= factor;
        Events.Record(Clock, History.WorldEventType.RuleChanged,
            "玩家调整再生倍率 ×" + factor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>强制天气（Drought / Rain / Storm 等灾害与恩惠工具）。</summary>
    public void InterveneForceWeather(WeatherKind kind, int durationHours)
    {
        World.Weather.ForceKind(kind, durationHours);
        Events.Record(Clock, History.WorldEventType.WeatherForced,
            "玩家强制天气为 " + WeatherInfo.NameOf(kind) + "，持续 " + durationHours + " 小时");
    }

    // ---------------------------------------------------------------------
    // 不变量校验（第 78 节 Debug 体系：尽早发现问题，而不是等数值崩坏）
    // ---------------------------------------------------------------------

    /// <summary>最近一次不变量检查是否通过。</summary>
    public bool LastInvariantCheckPassed { get; private set; } = true;
    public string LastInvariantFailure { get; private set; } = string.Empty;

    /// <summary>
    /// 校验模拟不变量。任何一条失败都说明某个系统写坏了状态 ——
    /// 在模拟游戏里，数值崩坏一旦扩散到历史统计里就很难回溯，所以要当场抛错。
    /// </summary>
    public void ValidateInvariants()
    {
        Tile[] tiles = World.Tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            ref Tile tile = ref tiles[i];
            if (!SimMath.IsFinite(tile.Moisture) || !SimMath.IsFinite(tile.Fertility)
                || !SimMath.IsFinite(tile.Temperature) || !SimMath.IsFinite(tile.Resource.Amount))
            {
                Fail("Tile[" + (i % World.Width) + "," + (i / World.Width) + "] 出现 NaN/Infinity");
                return;
            }
            if (tile.Moisture < 0f || tile.Moisture > 1f)
            {
                Fail("Tile[" + (i % World.Width) + "," + (i / World.Width) + "] 湿度越界：" + tile.Moisture);
                return;
            }
            if (tile.Resource.Amount < 0f)
            {
                Fail("Tile[" + (i % World.Width) + "," + (i / World.Width) + "] 资源为负：" + tile.Resource.Amount);
                return;
            }
            if (tile.Resource.Capacity > 0f && tile.Resource.Amount > tile.Resource.Capacity + 1e-3f)
            {
                Fail("Tile[" + (i % World.Width) + "," + (i / World.Width) + "] 资源超过容量");
                return;
            }
            if (tile.BuildingId < -1)
            {
                Fail("Tile[" + (i % World.Width) + "," + (i / World.Width) + "] 建筑索引非法：" + tile.BuildingId);
                return;
            }
        }

        LastInvariantCheckPassed = true;
        LastInvariantFailure = string.Empty;
    }

    private void Fail(string message)
    {
        LastInvariantCheckPassed = false;
        LastInvariantFailure = message;
        throw new System.InvalidOperationException("模拟不变量被破坏（tick " + World.Tick + "）：" + message);
    }
}
