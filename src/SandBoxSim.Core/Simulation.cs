using SandBoxSim.Core.Foundation;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Agents;
using SandBoxSim.Core.Pathing;
using SandBoxSim.Core.Systems;

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

    /// <summary>个体存储（M1 引入）。所有个体数据都在这里，Simulation 只做转发与调度。</summary>
    public AgentStore Agents { get; }

    /// <summary>寻路服务（Simulation 持有，因为它是无状态的共享资源，且需要世界尺寸）。</summary>
    public AStarPathfinder Pathfinder { get; }

    /// <summary>Utility AI 决策系统（M1）。</summary>
    public AiSystem Ai { get; }

    /// <summary>动作执行系统（M1）。</summary>
    public ActionSystem Actions { get; }

    /// <summary>需求系统（M1）。</summary>
    public NeedsSystem Needs { get; }

    /// <summary>地面物资堆（M2）：让"攒东西"在空间上可见。</summary>
    public GroundStockStore GroundStocks { get; }

    /// <summary>野生动物存储（M2）：生态链的前半段。</summary>
    public WildlifeStore Wildlife { get; }

    /// <summary>野生动物系统（M2）：吃、逃、繁殖、死。</summary>
    public WildlifeSystem WildlifeSystem { get; }

    /// <summary>迁移系统（M2）：每天评估一次"该不该搬走"。</summary>
    public MigrationSystem Migration { get; }

    /// <summary>建筑存储（M3）：住房/仓库/农田/矿场。</summary>
    public BuildingStore Buildings { get; }

    /// <summary>共享库存（M3）：每个仓库一份，按建筑槽位对齐。</summary>
    public StorageStore Storage { get; }

    /// <summary>建造系统（M3）：每 10 tick 推进施工。</summary>
    public BuildingSystem BuildingSystem { get; }

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

        // 实体与系统（顺序有讲究）：
        //   Agents 先建 ⇒ Pathfinder 依赖世界尺寸 ⇒ Ai/Actions 依赖前两者 ⇒ Needs 只依赖配置。
        Agents = new AgentStore();
        Pathfinder = new AStarPathfinder(World);
        Ai = new AiSystem(this, Agents, Pathfinder);
        Actions = new ActionSystem(this, Agents, Pathfinder);
        Needs = new NeedsSystem(config);

        GroundStocks = new GroundStockStore();
        Wildlife = new WildlifeStore();
        WildlifeSystem = new WildlifeSystem(this, Wildlife);
        Migration = new MigrationSystem(this, Agents);

        Buildings = new BuildingStore();
        Storage = new StorageStore();
        BuildingSystem = new BuildingSystem(this, Buildings);

        // 注册进实体集合：世界重建时会自动 Reset，摘要会自动覆盖
        RegisterEntitySet(Agents);
        RegisterEntitySet(GroundStocks);
        RegisterEntitySet(Wildlife);
        RegisterEntitySet(Buildings);

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

        // 实体集合清空 + 各系统统计归零。
        // 注意顺序：先清实体，再重置依赖实体的系统统计（否则统计会读到上一局的残留）。
        for (int i = 0; i < _entitySets.Count; i++) { _entitySets[i].Reset(); }
        Ai.ResetStatistics();
        Actions.ResetStatistics();
        Needs.ResetStatistics();
        WildlifeSystem.ResetStatistics();
        Migration.ResetStatistics();
        BuildingSystem.ResetStatistics();
        Storage.Reset();
        PopulationCount = 0;
        BuildingCount = 0;

        World.RefreshSpatialIndex();

        // 初始野生动物种群：按植被分布撒一遍（生态链的起点）
        WildlifeSystem.SeedInitialPopulation();

        Events.Record(0, History.WorldEventType.WorldGenerated, "世界生成：seed=" + seed
            + " 森林=" + info.ForestTiles + " 草地=" + info.GrassTiles
            + " 水域=" + info.WaterTiles + " 山地=" + info.MountainTiles
            + " 初始动物=" + Wildlife.LiveCount);
    }

    /// <summary>
    /// 推进一个模拟 tick。
    ///
    /// 管线顺序（不可随意调整）：
    ///   1. 时钟 +1
    ///   2. 动作推进（移动/执行/结算）—— 必须先在"上一轮决定"上往前走
    ///   3. 需求累积（含睡眠反解疲劳、饥饿掉血、死亡判定）
    ///   4. AI 分批决策 —— 必须在需求之后，否则会基于过期的需求做决定
    ///   5. FastTick：施工/生产/火势（每 10 tick）
    ///   6. HourTick：天气 → 资源再生 → 汇总湿度/温度（每 60 tick）
    ///   7. DailyTick：年龄 → 采样（每 1440 tick）
    ///   8. 刷新空间索引（所有改变格子的系统都在此之前完成写入）
    ///
    /// 第 2–4 步的顺序是本版最关键的确定性契约：
    /// **动作 → 需求 → 决策**。任何交换都会让"这一 tick 的饥饿"与"这一 tick 的决定"错位，
    /// 表现为个体行为整体迟滞一拍（很难查，但会明显影响人口曲线）。
    /// </summary>
    public void Tick(int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            World.Calendar.Advance(1);

            long tick = World.Tick;
            bool isNight = World.Calendar.IsNight;

            // ---- 个体层（M1）----
            Actions.Tick(tick);
            Needs.TickNeeds(Agents, tick, World.Calendar.TicksPerDay, isNight);
            RecordDeathsFromNeeds(tick);
            Ai.Tick(tick, isNight);

            // ---- 生态层（M2）----
            // 动物每 tick 更新（它们数量多、动作简单），
            // 但种群级事件（繁殖/自然死亡/容量重算）只在日边界发生。
            WildlifeSystem.Tick(tick);

            // ---- 环境层 ----
            if (IsFastTick) { TickFast(); }
            if (World.Calendar.IsHourBoundary) { TickHourInternal(); }
            if (World.Calendar.IsDayBoundary) { TickDayInternal(); }

            World.RefreshSpatialIndex();

            PopulationCount = Agents.LiveCount;
            BuildingCount = Buildings.TotalCompleted;

            if (Config.Debug.AssertInvariants && (tick % 64 == 0))
            {
                ValidateInvariants();
            }
        }
    }

    /// <summary>
    /// 把需求系统判定出的死亡写进事件日志与统计。
    ///
    /// 为什么要单独一步：<see cref="NeedsSystem"/> 只负责"谁该死"，事件与统计属于 History 层。
    /// 让它直接写事件日志会把两个关注点混在一起，也会让需求系统无法单独测试。
    ///
    /// 只遍历需求系统给出的死亡明细（而不是扫全部槽位）——
    /// 后者是"看起来无害的 O(容量)"，在长期运行时会被死过的槽位反复扫到。
    /// </summary>
    private void RecordDeathsFromNeeds(long tick)
    {
        System.Collections.Generic.IReadOnlyList<DeathRecord> deaths = Needs.Deaths;
        for (int i = 0; i < deaths.Count; i++)
        {
            DeathRecord death = deaths[i];
            string name = Agents.NameOrOverride(death.Slot);
            string causeLabel = NeedsSystem.DescribeCause(death.Cause);

            Events.Record(
                tick,
                History.WorldEventType.AgentDied,
                name + " 死亡（" + causeLabel + "，" + death.AgeDays + " 天）",
                History.EventImportance.Important,
                new Int2(death.X, death.Y),
                death.Slot,
                -1,
                "死因：" + causeLabel);

            Stats.RecordDeath();
        }
    }

    /// <summary>每 10 个 tick 一次的高频系统（建造施工、火灾蔓延、农场生长）。</summary>
    public const int FastTickInterval = 10;

    public bool IsFastTick => World.Tick % FastTickInterval == 0;

    /// <summary>高频 tick：M3 接入建造施工；M5 接入火灾。</summary>
    public void TickFast()
    {
        BuildingSystem.TickFast(World.Tick);
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

    /// <summary>每小时逻辑（后续各系统在此挂接：分工需求刷新、聚落评估……）。</summary>
    public void TickHour()
    {
        // 预留扩展点。
    }

    /// <summary>
    /// 每天逻辑。
    /// 目前包含：年龄推进 + 老年死亡 + 分批数重算（人口变化后需要重新分配决策相位）。
    /// 出生/迁移/聚落升档会在 M4/M7 挂到这里。
    /// </summary>
    public void TickDay()
    {
        Needs.TickAging(Agents, Random.Get(RngStream.Agents), World.Tick);
        RecordDeathsFromNeeds(World.Tick);

        WildlifeSystem.TickDay(World.Tick);

        // 迁移评估放在年龄/死亡之后：刚死掉的人不该再被考虑迁移。
        Migration.TickDay(World.Tick);

        PopulationCount = Agents.LiveCount;
        BuildingCount = Buildings.TotalCompleted;
        Ai.RefreshBatchCount();
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
            Migrations = Migration.TotalMigrations,

            // SettlementCount 在 M1–M6 期间还没有聚落实体，用 0 而不是"实体总数"：
            // 实体总数里包含个体，把它当聚落数会让报告里的"聚落数"等于人口数（很误导）。
            // M7 引入 SettlementStore 后这里会接上真实计数。
            SettlementCount = _settlementCountProvider?.Invoke() ?? 0,
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

    /// <summary>
    /// 聚落数量提供者（M7 会注册真实实现）。
    /// 用委托而不是硬依赖 SettlementStore：在聚落系统还不存在时，统计代码不必写 if 分支，
    /// 也不会因为"为了统计而先建一个空系统"而引入假结构。
    /// </summary>
    private System.Func<int>? _settlementCountProvider;

    public void SetSettlementCountProvider(System.Func<int> provider) => _settlementCountProvider = provider;

    /// <summary>当前实体总数（人口 + 建筑 + 聚落，取决于已注册的集合）。</summary>
    public int EntityCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _entitySets.Count; i++) { total += _entitySets[i].EntityCount; }
            return total;
        }
    }

    /// <summary>当前人口（存活个体数）。由 <see cref="AgentStore"/> 提供，每 tick 同步。</summary>
    public int PopulationCount { get; private set; }

    /// <summary>建筑计数（M3 之后由 BuildingStore 提供；当前恒为 0）。</summary>
    public int BuildingCount { get; private set; }

    /// <summary>由实体系统同步的只读计数（供统计与 UI 使用）。</summary>
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
    public void InterveneSetTerrain(int x, int y, TerrainKind terrain, bool reapplyResource = true)    {
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

    /// <summary>
    /// 放置一批居民（第 45 节的 Spawn Human 工具；也是 M1 验证的主要入口）。
    ///
    /// 设计要点：
    ///   * 出生点呈**高斯散布**而不是全部堆在同一格 —— 堆在一起会让寻路"互相挤"，
    ///     而且第一批行为会完全同步，看起来像克隆人；
    ///   * 同时给每个个体一个**散布的初始决策相位**，让决策天然错开（第 73 条的分批）；
    ///   * 用 <see cref="RngStream.Agents"/> 流，保证"同 seed + 同放置指令"结果一致。
    /// </summary>
    /// <param name="centerX">期望中心 X。</param>
    /// <param name="centerY">期望中心 Y。</param>
    /// <param name="count">人数。</param>
    /// <param name="radius">散布半径（格）。</param>
    /// <returns>实际成功放置的人数（可能少于请求，例如中心在深水里）。</returns>
    public int InterveneSpawnHumans(int centerX, int centerY, int count, int radius = 6)
    {
        if (count <= 0) { return 0; }

        // 容量不够先扩：否则 Add 会返回 None，表现为"玩家放了人但什么都没发生"
        Agents.EnsureCapacity(Agents.LiveCount + count);

        DeterministicRandom rng = Random.Get(RngStream.Agents);
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            // 散布：在半径内取一个随机偏移（用 sqrt 保证在圆盘上均匀）
            float radiusFraction = (float)System.Math.Sqrt(rng.NextDouble());
            double angle = rng.NextDouble() * 6.283185307179586;
            int offsetX = (int)System.Math.Round(System.Math.Cos(angle) * radius * radiusFraction);
            int offsetY = (int)System.Math.Round(System.Math.Sin(angle) * radius * radiusFraction);

            int spawnX = SimMath.Clamp(centerX + offsetX, 0, World.Width - 1);
            int spawnY = SimMath.Clamp(centerY + offsetY, 0, World.Height - 1);

            AgentRef reference = Agents.Add(World, spawnX, spawnY, rng, ageDays: 0, birthTick: Clock);
            if (reference.IsNone) { continue; }

            spawned++;

            Events.Record(Clock, History.WorldEventType.AgentSpawned,
                Agents.NameOrOverride(reference.Slot) + " 出现在 " + Agents.PositionOf(reference.Slot),
                History.EventImportance.Normal,
                Agents.PositionOf(reference.Slot),
                reference.Slot,
                -1,
                "玩家放置");
        }

        if (spawned > 0)
        {
            // 相位重新分配：新个体的槽位可能落在任意相位上，重新均分能避免"某一批特别重"
            Agents.AssignDecisionPhases(Ai.BatchCount);
            PopulationCount = Agents.LiveCount;
        }

        Events.Record(Clock, History.WorldEventType.AgentSpawned,
            "玩家在 " + new Int2(centerX, centerY) + " 附近放置了 " + spawned + " 名居民",
            History.EventImportance.Important,
            new Int2(centerX, centerY));

        return spawned;
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
            // BuildingId 约定：0 = 无建筑，>0 = 建筑槽位 + 1（见 Tile.BuildingId 的注释）
            if (tile.BuildingId < 0)
            {
                Fail("Tile[" + (i % World.Width) + "," + (i / World.Width) + "] 建筑索引非法：" + tile.BuildingId);
                return;
            }
            if (tile.BuildingId > Buildings.Capacity)
            {
                Fail("Tile[" + (i % World.Width) + "," + (i / World.Width) + "] 建筑索引越界：" + tile.BuildingId);
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
