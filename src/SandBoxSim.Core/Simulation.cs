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

    /// <summary>建造系统（M3）：每 10 tick 推进施工。M4 起还负责农田产出与建筑衰减。</summary>
    public BuildingSystem BuildingSystem { get; }

    /// <summary>
    /// 出生系统（M4）—— 这个世界唯一缺失的机制。
    /// 没有它，人口单调下降，前几个里程碑做出来的经济与建造最终都走向同一个结局。
    /// </summary>
    public BirthSystem Births { get; }

    /// <summary>
    /// 火灾系统（M5）。
    /// 它是"玩家烧森林 → 人口增速下降"这条最小因果证明的起点，
    /// 也是玩家第一次能**毁掉条件**而不只是"加东西"。
    /// </summary>
    public FireSystem Fire { get; }

    /// <summary>
    /// 个体之间的关系（M6）。对称性由数据结构保证（键是 (min,max) 打包的 long），
    /// 因此 `Rel(A,B) == Rel(B,A)` 不可能被调用方破坏。
    /// </summary>
    public RelationshipStore Relationships { get; }

    /// <summary>
    /// 聚落（M7）。它是**涌现**的：由"持续共处 + 共享住房与仓库"形成，玩家无法直接创建。
    /// </summary>
    public SettlementStore Settlements { get; }

    /// <summary>
    /// 贸易与价格（M8）。它是**现有状态的纯函数** —— 不存档、不进摘要，
    /// 读档后重算一遍就得到完全相同的值。详见 TradeSystem 的注释。
    /// </summary>
    public TradeSystem Trade { get; }

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
        : this(config, width, height, seed, restoreMode: false)
    {
    }

    /// <summary>只生成地形、不初始化任何实体与统计（读档专用，见 <see cref="Save.SaveLoader"/>）。</summary>
    public static Simulation CreateForRestore(SimConfig config, int width, int height, int seed)
        => new Simulation(config, width, height, seed, restoreMode: true);

    private Simulation(SimConfig config, int width, int height, int seed, bool restoreMode)
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

        // M4：出生系统。它需要 AgentStore + BuildingStore（床位是出生的硬门），
        // 因此必须在这两者之后构造。
        Births = new BirthSystem(this, Agents);

        // M5：火灾系统。它只读写 World 的格子与 Events 流，依赖最少。
        Fire = new FireSystem(this);

        // M6：关系系统。它是稀疏的（只存真正发生过的关系），
        // 且**不带任何对世界的引用** —— 它纯粹是一张表，因此依赖最少、最容易测。
        Relationships = new RelationshipStore(Config.Relationship);

        // 注册进实体集合，从而自动参与状态摘要（见 RegisterEntitySet 的约定）。
        RegisterEntitySet(Relationships);

        // M7：聚落。它只读 Agents 与 Buildings，不写它们 —— 是一个纯观察者，
        // 但它自己有状态（候选持续性计数、已成立的聚落），因此也要入档。
        Settlements = new SettlementStore(this);
        RegisterEntitySet(Settlements);

        // M8：贸易与价格。**刻意不注册进实体集合** —— 它没有自己的持久状态，
        // 注册进去反而会让人以为"它有需要存档的东西"。
        Trade = new TradeSystem(this);

        // 注册进实体集合：世界重建时会自动 Reset，摘要会自动覆盖
        RegisterEntitySet(Agents);
        RegisterEntitySet(GroundStocks);
        RegisterEntitySet(Wildlife);
        RegisterEntitySet(Buildings);

        // 读档模式下**只生成地形**，不做"新世界"那一套（清空实体、撒初始动物、记事件）。
        //
        // 为什么必须区分（这是读档最容易踩的坑）：`RegenerateWorld` 会
        // 重置随机源、清空全部实体集合、并**撒下一批初始野生动物**。
        // 如果读档走这条路，随后恢复的实体是在"已经被重新播种的世界"上叠加的 ——
        // 表现为读档后动物数量翻倍、或多出一批不存在的事件，
        // 而存档里那些"看起来对"的字段反而掩盖了问题。
        if (restoreMode) { GenerateTerrainOnly(seed); }
        else { RegenerateWorld(seed); }
    }

    /// <summary>
    /// 只生成地形与资源，不触碰任何实体、统计与随机源之外的东西。
    /// 用于读档：地形随后会被存档里的逐格数据覆盖，这里生成只是为了
    /// 让 <see cref="World"/> 有一个尺寸正确、字段合法的 Tile 数组。
    /// </summary>
    private void GenerateTerrainOnly(int seed)
    {
        Tile[] tiles = WorldGenerator.Generate(Config, World.Width, World.Height, seed, out WorldGenerator.Result info);
        World.ReplaceAllTiles(tiles, seed);
        World.RestoreTick(0);
        World.ResetWeather();
        GenerationInfo = info;
        World.RefreshSpatialIndex();
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

            // M4：死亡必须**释放床位**并解开伴侣关系。
            // 床位占用是计数的，漏释放会让"床位够不够"永远偏向"不够"，
            // 出生率缓慢掉到 0 —— 不报错，只表现为"这个世界的孩子越来越少"。
            Births.OnAgentRemoved(death.Slot);

        // M6：同时忘掉这个人的全部关系。
        // 不清理会有两个后果：条目无限增长；以及**槽位被复用时**
        // 新个体凭空继承前一个人的关系（"我刚出生就有一个死敌"）。
        Relationships.Forget(death.Slot);

            Stats.RecordDeath();
        }
    }

    /// <summary>每 10 个 tick 一次的高频系统（建造施工、火灾蔓延、农场生长）。</summary>
    public const int FastTickInterval = 10;

    public bool IsFastTick => World.Tick % FastTickInterval == 0;

    /// <summary>
    /// 资源再生时长的规则修正（M5 的 `DoubleResource`）。
    ///
    /// 用"把时长乘 2"而不是"把再生率乘 2"：`ResourceSystem.Regenerate` 的签名收的是
    /// **经过的天数**，而 Logistic 再生对时长是非线性的。
    /// 乘时长恰好等价于"这块地有双倍的时间恢复"，
    /// 语义上直接对应玩家看到的"资源长得更快"，也不需要改动再生公式本身。
    /// </summary>
    private double RegenerationDays(double days)
        => Config.Rules.DoubleResource ? days * 2.0 : days;

    /// <summary>
    /// 高频 tick：M3 接入建造施工；M5 接入火灾。
    /// </summary>
    public void TickFast()
    {
        BuildingSystem.TickFast(World.Tick);
        Fire.TickFast(World.Tick);
    }

    private void TickHourInternal()
    {
        WeatherTick();
        if (ResourceSystem.RegeneratesHourly)
        {
            ResourceSystem.Regenerate(RegenerationDays(1.0 / 24.0));
        }
        TickHour();
        HourEventsFired++;
        HourAdvanced?.Invoke(this);
    }

    private void TickDayInternal()
    {
        if (!ResourceSystem.RegeneratesHourly)
        {
            ResourceSystem.Regenerate(RegenerationDays(1.0));
        }

        // M6：关系每天向 0 回落（"长期不来往就变淡"）。
        // 放在 TickDay 之前：让本日的社会互动先发生、再统一衰减，
        // 顺序固定即可（确定性只要求顺序稳定，不要求它必须是某一个特定顺序）。
        // M6：怨恨先于回落 —— 让"本日的条件"先产生关系变化，再统一衰减。
        // 顺序固定即可（确定性只要求顺序稳定）。
        Relationships.TickResentment(
            Agents,
            Config.Relationship.ResentmentHungerThreshold,
            Config.Relationship.ResentmentSurplusThreshold,
            Config.Relationship.ResentmentPerDay,
            Config.Relationship.ResentmentRadius,
            World.Tick);

        Relationships.TickDay(World.Tick);

        // M7：聚落评估（在关系之后、日常事务之前 —— 顺序固定即可）
        Settlements.TickDay(World.Tick);

        // M8：逐日算价（纯派生，不写需要存档的状态）
        Trade.TickDay(World.Tick);

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
    ///
    /// 顺序有讲究，而且每一条都有理由：
    ///   1. **年龄推进 + 老年死亡**：先让"谁能生育"这件事定下来；
    ///   2. **记录死亡**：床位在这之后才释放，因此同一天的出生看不到刚死的人的床；
    ///   3. **配对**：伴侣关系按"同住/同地"近似（M4），在生育之前建立；
    ///   4. **出生**：必须在年龄之后（否则今天刚成年的人要等到明天）；
    ///   5. **农田结算与建筑衰减**：产出与损耗都在人口变化之后，反映"这一天结束时的账"；
    ///   6. **野生动物 / 迁移**：与人口无直接耦合，放最后。
    /// </summary>
    public void TickDay()
    {
        Needs.TickAging(Agents, Random.Get(RngStream.Agents), World.Tick);
        RecordDeathsFromNeeds(World.Tick);

        // M4：伴侣配对要在出生之前 —— 否则"今天刚搬来的人"永远配不上对
        Births.TickPairing(World.Tick);
        Births.TickDay(World.Tick);

        WildlifeSystem.TickDay(World.Tick);

        // 迁移评估放在年龄/死亡/出生之后：世界已经稳定到"今天的样子"再考虑搬家。
        Migration.TickDay(World.Tick);

        // 农田产出与建筑衰减放最后：它们是"这一天的收支结算"。
        BuildingSystem.TickDay(World.Tick);

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

            // # 湿度推进必须是**渐近**的，不能是"加法 + 截断"
            //
            // 原先写的是 `clamp01(moisture + delta)`。那看起来无害，实际上有一个
            // 很严重的后果：**下雨会把湿度顶到 1.0 并把它钉在那里**。
            // 因为变湿的增量与"已经多湿"无关，而变干的增量是固定的小负数 ——
            // 一场雨赚到的湿度，要很多个晴天才能还回去，于是常年贴着上限。
            //
            // 实测（100×100、200 天）：平均湿度**最小 0.73、最大 1.0**，
            // 也就是说这个世界长期是饱和的。三个后果同时发生：
            //   * 火灾不可能发生（干燥度≈0，M5 联调时"200 天 0 起火"的根因）；
            //   * 农田产量里的湿度因子 `(0.25 + 0.75×moisture)` 被钉在 1.0，
            //     这个因子实际上是**死**的；
            //   * "干旱"永远无法真正把地弄干，它只在 `CropFactor` 上体现。
            //
            // 改成渐近形式之后：
            //   * 变湿：`m += delta × (1 − m)` —— 越接近饱和越难再湿（永远到不了 1）；
            //   * 变干：`m += delta × m`       —— 蒸发与地表水量成正比（越湿干得越快）。
            //
            // 两者都是"朝某个吸引子指数逼近"，因此**不存在钉死的上限**，
            // 而且物理上更自洽：蒸发量本来就该与可蒸发的水量成正比。
            // 这也让"干旱"第一次成为一个能真正改变地表条件的天气。
            float targetMoisture = moistureDelta > 0f
                ? tile.Moisture + (moistureDelta * (1f - tile.Moisture))
                : tile.Moisture + (moistureDelta * tile.Moisture);

            targetMoisture = SimMath.Clamp01(targetMoisture);
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
    ///   * 用 <see cref="RngStream.Intervention"/> 流，而不是 <see cref="RngStream.Agents"/>：
    ///     干预是外部输入，绝不能扰动模拟内核自己的随机序列
    ///     （否则"玩家撒了几只动物"会改变接下来几天的天气 —— 见 RngStream.Intervention 的注释）。
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

        DeterministicRandom rng = Random.Get(RngStream.Intervention);
        int spawned = 0;

        // 初始居民的年龄**必须散布**，否则会造出一个现实中不存在的"人口波"。
        //
        // 这是一个真实踩到的坑：原先所有人都是 `ageDays: 0`，也就是"同一天出生"。
        // 于是他们会在同一天进入老年、又在同一天撞上寿命上限 ——
        // 实测 100 天时人口曲线看着不错（40 → 47），但 90–100 天之间**整批人同时老死**，
        // 200 天必然归零。而这不是"平衡没调好"，是初始条件本身不成立：
        // 没有任何真实聚落的成员年龄完全一致。
        //
        // 散布范围取 [成年, 老年)，也就是都处于生育年龄 —— 与"放一批定居者"的语义一致。
        int adulthood = Config.Needs.AdulthoodDays;
        int elder = Config.Needs.ElderDays > adulthood ? Config.Needs.ElderDays : adulthood + 1;
        int ageSpan = elder - adulthood;

        for (int i = 0; i < count; i++)
        {
            // 散布：在半径内取一个随机偏移（用 sqrt 保证在圆盘上均匀）
            float radiusFraction = (float)System.Math.Sqrt(rng.NextDouble());
            double angle = rng.NextDouble() * 6.283185307179586;
            int offsetX = (int)System.Math.Round(System.Math.Cos(angle) * radius * radiusFraction);
            int offsetY = (int)System.Math.Round(System.Math.Sin(angle) * radius * radiusFraction);

            int spawnX = SimMath.Clamp(centerX + offsetX, 0, World.Width - 1);
            int spawnY = SimMath.Clamp(centerY + offsetY, 0, World.Height - 1);

            int ageDays = adulthood + (int)(rng.NextDouble() * ageSpan);

            AgentRef reference = Agents.Add(World, spawnX, spawnY, rng, ageDays: ageDays, birthTick: Clock);
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
                // 用 Intervention 流而不是 Events：后者属于天气/火灾/灾害。
                // 借用它会让"玩家催生了一片森林"改变接下来几天的天气（见 RngStream.Intervention）。
                if (Random.Get(RngStream.Intervention).NextDouble() > chance) { continue; }

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

    /// <summary>
    /// 让工具记录一条"玩家做了什么"的事件（M5）。
    ///
    /// 为什么工具需要这个：干预分两类 —— 一类自己就会产生事件（放人、改地形、
    /// 注入资源都走各自系统的记录），另一类只是**改了一个数**（烘干一片地、
    /// 让一片人生病）。后者如果不留痕，玩家在事件时间线里就看不到自己做过什么，
    /// 而"可回溯"是实验可信度的前提。
    /// </summary>
    public void InterveneRecordAuxiliary(string message)
    {
        if (string.IsNullOrEmpty(message)) { return; }
        Events.Record(Clock, History.WorldEventType.RuleChanged, message,
            History.EventImportance.Normal);
    }

    /// <summary>强制天气（Drought / Rain / Storm 等灾害与恩惠工具）。</summary>
    public void InterveneForceWeather(WeatherKind kind, int durationHours)
    {
        World.Weather.ForceKind(kind, durationHours);
        Events.Record(Clock, History.WorldEventType.WeatherForced,
            "玩家强制天气为 " + WeatherInfo.NameOf(kind) + "，持续 " + durationHours + " 小时");
    }

    // ---------------------------------------------------------------------
    // 存档 / 读档（第 76 节）
    //
    // 具体格式与字段清单在 Save/SaveFile.cs（写）与 Save/SaveLoader.cs（读）。
    // Simulation 只提供"从哪进、从哪出"这两个入口，不参与字段细节 ——
    // 这样新增一个存档字段时只需要改 Save 目录下的文件，不需要动模拟内核。
    // ---------------------------------------------------------------------

    /// <summary>把当前全部状态编码成存档文本（不写磁盘）。</summary>
    public string SaveToText() => Save.SaveFile.Encode(this);

    /// <summary>
    /// 把当前状态写入存档文件。
    ///
    /// **这是 Core 里除 ConfigLoader 之外的第二处文件 I/O**，而且是刻意的例外：
    /// 存档必须能在 headless / 批处理 / 测试里用，如果做成"只有 UI 才能存"，
    /// 那么"Save → Load → 摘要一致"这条最关键的验收就只能在 TUI 里手测。
    /// 它不参与 tick 管线，也不读时间/环境，因此不破坏确定性契约。
    /// </summary>
    public void SaveToFile(string path)
    {
        if (string.IsNullOrEmpty(path)) { throw new System.ArgumentException("存档路径为空", nameof(path)); }

        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
        if (!System.IO.Directory.Exists(directory)) { System.IO.Directory.CreateDirectory(directory); }

        System.IO.File.WriteAllText(path, SaveToText(), new System.Text.UTF8Encoding(false));
    }

    /// <summary>
    /// 从一个存档文件**恢复到一个已存在的、尺寸相同的 Simulation**。
    ///
    /// 为什么是"恢复到已存在实例"而不是"新建实例"：表现层（TUI）持有一大堆
    /// 指向 <see cref="World"/> 与各系统的引用（相机、渲染器、面板缓存），
    /// 换一个新实例会让它们全部悬空。原地恢复只需要重建一格状态，场景里的引用都还有效。
    /// </summary>
    public Save.SaveFile.LoadResult LoadFromFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            return new Save.SaveFile.LoadResult { Error = "存档文件不存在：" + path };
        }

        string json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
        return Save.SaveLoader.Load(this, json);
    }

    /// <summary>从存档文本恢复（测试与内存内往返用）。</summary>
    public Save.SaveFile.LoadResult LoadFromText(string json) => Save.SaveLoader.Load(this, json);

    /// <summary>
    /// 读档收尾（由 <see cref="Save.SaveLoader"/> 在全部状态恢复之后调用）。
    ///
    /// 存在的理由：读档之后必须把**所有派生缓存**重新算一遍，否则会留下
    /// "状态是新的、缓存是旧的"这种最难查的不一致。目前需要重算的有三项：
    /// 人口/建筑计数、AI 分批数、以及决策相位与分批数的匹配关系。
    ///
    /// 为什么把它做成 Simulation 的方法而不是让 SaveLoader 直接改这些字段：
    /// 这些字段是 private set 的，让外部改会绕过"谁负责维护它"的边界。
    /// </summary>
    public void NotifyAfterLoad()
    {
        PopulationCount = Agents.LiveCount;
        BuildingCount = Buildings.TotalCompleted;

        // 分批数与决策相位都由存档恢复，因此这里**什么都不用做**。
        //
        // 曾经这里调用的是 `Ai.RefreshBatchCount()`，而它在分批数变化时
        // 会调用 `AssignDecisionPhases` —— 于是读档顺手重排了所有人的决策相位。
        // 因为 `decisionPhase` 当时不在摘要里，读档自校验完全通过，
        // 症状要到约一个决策间隔（600 tick）之后才显现。
        // 现在改由 `SaveLoader` 调 `Ai.AdoptBatchCountAfterLoad`（只采用、不重排）。

        // **刻意不调用 Agents.AssignDecisionPhases()** —— 这是一个踩过的坑：
        // 决策相位是**被保存并恢复**的状态（它决定谁在哪一分钟决策），
        // 在这里重新均分等于把恢复出来的相位又一次打乱。
        //
        // 换句话说：**凡是被显式恢复的字段，读档收尾时都不能"顺手重算"一遍。**

        // 空间索引必须在**地形恢复之后**刷新：早于地形恢复会让 AI 依据过期的
        // 资源分布做决定（见 SaveLoader 顶部的顺序清单）。
        World.RefreshSpatialIndex();
    }

    /// <summary>
    /// 改变一片区域的肥沃度（第 45 节的 Blessing 工具：Increase Fertility）。
    ///
    /// 这是"玩家创造条件"里最典型的一类：它**不直接给食物**，
    /// 只改变"这片土地将来能长多少东西"。玩家能立刻看到的结果只有颜色变化，
    /// 真正的后果要等农业（M4）与采集把它放大出来。
    ///
    /// 用 <see cref="RngStream.Intervention"/> 流的随机数（**不是** Events 流）：
    /// 干预必须与天气/火灾/灾害的随机序列完全隔离，
    /// 否则玩家改一个条件就会连带改变天气，因果就再也无法归因（见 docs/13 与 RngStream.Intervention）。
    /// </summary>
    /// <returns>实际被修改的格子数。</returns>
    public int InterveneSetFertility(int centerX, int centerY, int radius, float delta)
    {
        int changed = 0;

        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (!World.IsInBounds(x, y)) { continue; }

                float dist = (float)System.Math.Max(System.Math.Abs(x - centerX), System.Math.Abs(y - centerY));
                if (dist > radius) { continue; }

                // 越靠中心效果越强：让工具的作用范围看起来像"以光标为中心的一圈"，
                // 而不是一个硬边方框（后者在视觉上很难判断自己改到了哪里）。
                float falloff = radius <= 0 ? 1f : 1f - (dist / (radius + 1f));

                float before = World.TileAt(x, y).Fertility;
                float after = SimMath.Clamp01(before + (delta * falloff));
                if (System.Math.Abs(after - before) < 1e-4f) { continue; }

                World.SetFertility(x, y, after);
                changed++;
            }
        }

        if (changed > 0)
        {
            Events.Record(Clock, History.WorldEventType.FertilityChanged,
                "玩家在 " + new Int2(centerX, centerY) + " 附近调整肥沃度 " + delta.ToString("+0.##;-0.##", System.Globalization.CultureInfo.InvariantCulture)
                + "，影响 " + changed + " 格");
        }

        return changed;
    }

    /// <summary>
    /// 放置野生动物（第 45 节的 Create 工具：Spawn Animal）。
    ///
    /// 为什么这是一个**重要的**工具而不是补全清单：动物种群是"砍树 → 猎物减少"
    /// 这条延迟因果链的中间环节。玩家能直接往某处撒猎物，就能做一件事：
    /// **在森林旁边放一群鹿，然后观察这群鹿会不会因为自己的人口增长而消失。**
    /// 这是任务书第 67 节要求的最小可玩实验之一。
    /// </summary>
    /// <returns>实际放入的动物数量。</returns>
    public int InterveneSpawnAnimals(int centerX, int centerY, int count, int radius = 5)
    {
        if (count <= 0) { return 0; }

        Wildlife.EnsureCapacity(Wildlife.LiveCount + count);
        DeterministicRandom rng = Random.Get(RngStream.Intervention);
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            float radiusFraction = (float)System.Math.Sqrt(rng.NextDouble());
            double angle = rng.NextDouble() * 6.283185307179586;
            int offsetX = (int)System.Math.Round(System.Math.Cos(angle) * radius * radiusFraction);
            int offsetY = (int)System.Math.Round(System.Math.Sin(angle) * radius * radiusFraction);

            int x = SimMath.Clamp(centerX + offsetX, 0, World.Width - 1);
            int y = SimMath.Clamp(centerY + offsetY, 0, World.Height - 1);

            if (Wildlife.Add(World, x, y, 4, rng) < 0) { continue; }
            spawned++;
        }

        if (spawned > 0)
        {
            Events.Record(Clock, History.WorldEventType.WildlifeSpawned,
                "玩家在 " + new Int2(centerX, centerY) + " 附近放入 " + spawned + " 只动物");
        }

        return spawned;
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
