using System.Collections.Generic;
using System.Globalization;

namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 模拟配置根。所有影响涌现行为的数值都必须在这里可调（第 96.6 / 96.7 条）。
///
/// 每个字段的含义与影响链见 docs/15-ConfigReference.md。
/// 修改这里必须同步修改 config/sim.default.json —— 测试 <c>ConfigTests.DefaultsMatchJson</c> 会强制这一点。
/// </summary>
public sealed class SimConfig
{
    public int Version = 1;

    public ClockConfig Clock = new ClockConfig();
    public WorldConfig World = new WorldConfig();
    public WorldGenConfig WorldGen = new WorldGenConfig();
    public ResourceConfig Resources = new ResourceConfig();
    public NeedsConfig Needs = new NeedsConfig();
    public AiConfig Ai = new AiConfig();
    public DebugConfig Debug = new DebugConfig();

    /// <summary>深拷贝：世界重置（换 seed）时保持参数不变。</summary>
    public SimConfig Clone()
    {
        SimConfig copy = new SimConfig();
        JsonBinder.Bind(JsonBinder.ToJson(this), copy, new List<string>(), string.Empty);
        return copy;
    }
}

/// <summary>
/// 时间系统参数。核心约定：1 Tick = 1 游戏分钟，所有系统按 tick 计数调度，
/// 表现层的倍速只改变"每秒推进多少 tick"，绝不改变 tick 的语义（第 82 条）。
/// </summary>
public sealed class ClockConfig
{
    /// <summary>每个 tick 代表多少游戏分钟。固定为 1，保留字段是为了未来支持更粗/更细粒度。</summary>
    public int MinutesPerTick = 1;

    /// <summary>一个游戏小时多少 tick。</summary>
    public int TicksPerHour = 60;

    /// <summary>一个游戏天多少小时。</summary>
    public int HoursPerDay = 24;

    /// <summary>1× 速度下每秒推进多少 tick（10 TPS 是 100×100 地图 + 数百 agent 的舒适区）。</summary>
    public int TicksPerSecondAt1x = 10;

    /// <summary>可选倍速（0 = 暂停）。</summary>
    public int[] SpeedMultipliers = { 0, 1, 2, 4, 8 };

    /// <summary>渲染目标帧率。与模拟 tick 率完全独立。</summary>
    public int TargetFramesPerSecond = 60;

    /// <summary>单帧最多补多少 tick，防止卡顿后"雪崩式追赶"。</summary>
    public int MaxCatchUpTicksPerFrame = 40;

    public int TicksPerDay => TicksPerHour * HoursPerDay;

    public int TicksPerGameMinute => MinutesPerTick;
}

/// <summary>世界静态参数。</summary>
public sealed class WorldConfig
{
    public int Width = 100;
    public int Height = 100;

    /// <summary>空间索引的 chunk 边长（第 74 条）。16 在"查询精度"与"内存/维护成本"之间最优。</summary>
    public int ChunkSize = 16;

    /// <summary>世界基准气温 [0,1]，影响蒸发、作物与（未来）取暖。</summary>
    public float AmbientTemperature = 0.55f;

    /// <summary>世界基准湿度 [0,1]。</summary>
    public float AmbientMoisture = 0.5f;
}

/// <summary>
/// 世界生成参数（第 83 / 84 条）。同 seed + 同参数 ⇒ 逐格一致。
/// </summary>
public sealed class WorldGenConfig
{
    public int Seed = 839102;

    // 大陆高度图（决定海陆与山脉）
    public float ContinentFrequency = 0.018f;
    public int ContinentOctaves = 4;
    public float ContinentPersistence = 0.5f;

    // 湿度图（决定森林与肥沃度）
    public float MoistureFrequency = 0.035f;
    public int MoistureOctaves = 3;
    public float MoisturePersistence = 0.55f;

    /// <summary>细节噪声频率：制造海岸线碎屑与土壤微差异，避免大片均匀色块。</summary>
    public float DetailFrequency = 0.12f;

    /// <summary>
    /// 水面占比（地图格子比例）。这是**设计意图**：世界生成会按分位数取阈值，
    /// 保证每张地图都真的有水，而不是把噪声值直接拿去和阈值比。
    /// </summary>
    public float WaterLevel = 0.28f;

    /// <summary>山地占比（地图格子比例）。</summary>
    public float MountainLevel = 0.16f;

    /// <summary>水面占比的种子间波动（±）。让不同 seed 的地图"海陆比例"不同。</summary>
    public float WaterShareJitter = 0.10f;

    /// <summary>山地占比的种子间波动（±）。</summary>
    public float MountainShareJitter = 0.07f;

    /// <summary>海平面以上多宽的一条带判定为沙地（沙滩）。</summary>
    public float SandBand = 0.035f;

    /// <summary>湿度超过该阈值且高度适中时生成森林。</summary>
    public float ForestMoistureThreshold = 0.52f;

    /// <summary>森林生成概率的额外加成（湿度越高越容易成林）。</summary>
    public float ForestDensityBonus = 0.18f;

    public bool FertilityFromHeight = true;
    public float FertilityBase = 0.55f;
    public float FertilityNoiseFrequency = 0.06f;
}

/// <summary>
/// 资源参数（第 9 / 62 / 63 条）。每个 Tile 持有局部资源节点，
/// 再生用离散 Logistic：A ← A + r·A·(1 − A/K)，采集直接扣除。
/// </summary>
public sealed class ResourceConfig
{
    public float WoodCapacityPerForestTile = 100f;
    public float WoodInitialFraction = 0.85f;
    public float WoodGrowthRate = 0.02f;
    public float WoodHarvestPerAction = 6f;

    public float StoneCapacityPerMountainTile = 80f;
    public float StoneInitialFraction = 0.9f;
    public float StoneHarvestPerAction = 4f;

    public float IronCapacityPerMountainTile = 30f;
    public float IronInitialFraction = 0.6f;
    public float IronHarvestPerAction = 2f;

    public float FoodCapacityPerGrassTile = 60f;
    public float FoodInitialFraction = 0.6f;
    public float FoodGrowthRate = 0.03f;
    public float FoodHarvestPerAction = 24f;

    /// <summary>是否每小时做一次再生（false 则每天一次，用于性能对比实验）。</summary>
    public bool RegenerateHourly = true;

    /// <summary>低于该比例时判定为"资源紧张"，会影响采集效用与迁移压力。</summary>
    public float DepletionWarnFraction = 0.2f;
}

/// <summary>
/// 需求系统参数（M1 引入）。
///
/// 所有速率都是"每天增加多少需求"：1.0 表示"一整天什么都不做才会从满足到极端"。
/// 需要快速验证饥荒场景时，把 <c>HungerPerDay</c> 调到 5 以上即可 ——
/// 参数可调是刻意的（第 96.7 条），因为"世界运行的节奏"是本项目最需要实验的旋钮。
/// </summary>
public sealed class NeedsConfig
{
    /// <summary>饥饿累积速率（/天）。1.0 ⇒ 不吃不喝一天就到极限。</summary>
    public float HungerPerDay = 1.0f;

    /// <summary>疲劳累积速率（/天）。略低于饥饿：人还是渴睡而不是饿死更常见？不，饿死更快。</summary>
    public float FatiguePerDay = 1.15f;

    /// <summary>干渴累积速率（/天）。比饥饿更快：缺水比缺粮致命得多。</summary>
    public float ThirstPerDay = 1.5f;

    /// <summary>社交需求累积速率（/天）。M6 才真正驱动行为，M1 只累积。</summary>
    public float SocialPerDay = 0.5f;

    /// <summary>睡眠时疲劳的恢复速率（/天，0.6 ⇒ 睡 10 小时回满）。</summary>
    public float SleepRecoveryPerDay = 1.45f;

    /// <summary>饥饿导致掉血的起始阈值。</summary>
    public float StarvationDamageThreshold = 0.85f;

    /// <summary>极度饥饿时每天掉多少血（1.0 ⇒ 一天掉光）。</summary>
    public float StarvationDamagePerDay = 0.25f;

    /// <summary>干渴导致掉血的起始阈值。</summary>
    public float DehydrationDamageThreshold = 0.9f;

    /// <summary>极度干渴时每天掉多少血。</summary>
    public float DehydrationDamagePerDay = 0.45f;

    /// <summary>健康时的自然恢复（每天回多少血）。</summary>
    public float HealthRecoveryPerDay = 0.1f;

    /// <summary>成年年龄下限（天）。</summary>
    public int AdulthoodDays = 16;

    /// <summary>老年起点（天）。</summary>
    public int ElderDays = 55;

    /// <summary>老年后每天的自然死亡概率（越小活得越久）。</summary>
    public float ElderMortalityPerDay = 0.012f;

    /// <summary>硬性寿命上限（天）：超过必死，避免极端长寿个体堆积。</summary>
    public int MaxLifespanDays = 90;
}

/// <summary>
/// Utility AI 参数（M1 引入；第 15–17、33 节）。
///
/// 结构说明：每个动作的效用 = 若干 Consideration（输入 × 曲线 × 权重）的组合。
/// 所有数值都可配置，是因为"什么行为在什么时候更值得做"正是这个游戏最需要实验的部分。
/// </summary>
public sealed class AiConfig
{
    /// <summary>决策间隔（tick）。600 = 每游戏 10 小时才重新决策一次（默认，省 CPU）。</summary>
    public int DecisionIntervalTicks = 600;

    /// <summary>
    /// 分批数：每 tick 只让 1/batchCount 的个体决策。
    /// 这是"大量个体也能跑"的关键（第 73 条）。0 = 自动（按人口自适应）。
    /// </summary>
    public int BatchCount = 0;

    /// <summary>自动分批时的目标：每 tick 最多决策多少个体。</summary>
    public int TargetDecisionsPerTick = 12;

    /// <summary>个体视野半径（格）。NPC 不允许全知（第 19 条）。</summary>
    public int SearchRadius = 12;

    /// <summary>漫游时的最大目标距离（格）。</summary>
    public int WanderRadius = 6;

    /// <summary>探索时的目标距离范围（格）。</summary>
    public int ExploreMinDistance = 15;

    public int ExploreMaxDistance = 40;

    /// <summary>每次决策保存多少个动作分数（供检查器显示；太多会拖慢且没人看）。</summary>
    public int TopScoresToKeep = 6;

    /// <summary>移动速度：每 tick 前进多少格（0.35 ⇒ 走一格约 3 tick，即游戏时间 3 分钟/格）。</summary>
    public float MoveSpeedPerTick = 0.35f;

    // ---- 各动作的权重（数值越大越"想做"） ----

    /// <summary>漫游基础倾向。设得低一点，好让"有事做"时自然被压过去。</summary>
    public float WanderWeight = 0.18f;

    /// <summary>探索倾向（受好奇/勤劳影响）。</summary>
    public float ExploreWeight = 0.22f;

    /// <summary>进食的饥饿驱动权重。</summary>
    public float EatHungerWeight = 2.0f;

    /// <summary>进食时需要手上有食物的权重（M2 起生效）。</summary>
    public float EatFoodAvailableWeight = 1.2f;

    /// <summary>喝水的干渴驱动权重。</summary>
    public float DrinkThirstWeight = 2.2f;

    /// <summary>睡眠的疲劳驱动权重。</summary>
    public float SleepFatigueWeight = 2.0f;

    /// <summary>夜间强制睡眠的权重（夜晚困意效果更强）。</summary>
    public float SleepNightBonus = 0.6f;

    /// <summary>
    /// 采集食物：饥饿驱动。
    ///
    /// 这个值刻意比"木材/石料可得性"（≈1.4）大得多。
    /// 原因不是平衡，而是**正确性**：如果三者接近，"手上没木没石"的人会一直砍柴，
    /// 明明快饿死也不去采食物（实测出现过"40 人全饿死，采集统计里食物为 0"）。
    /// 生存类动作必须在效用尺度上明确压过非生存类动作。
    /// </summary>
    public float GatherFoodHungerWeight = 2.5f;

    /// <summary>采集食物：附近有没有目标资源。</summary>
    public float GatherFoodAvailabilityWeight = 1.0f;

    /// <summary>采集木材：木材储备的抑制权重（越足越不想砍）。</summary>
    public float GatherWoodNeedWeight = 1.4f;

    /// <summary>采集木材的可得性权重。</summary>
    public float GatherWoodAvailabilityWeight = 1.2f;

    /// <summary>采集石头的可得性权重。</summary>
    public float GatherStoneAvailabilityWeight = 1.0f;

    /// <summary>勤劳性格对所有"工作类"动作的加成幅度。</summary>
    public float IndustriousnessWorkBonus = 0.55f;

    /// <summary>社交冲动权重（M6 起生效）。</summary>
    public float SocializeWeight = 0.5f;

    /// <summary>漫游/探索时的"离家惩罚"：离开自己的活动中心越远越不想去（暂时用出生区域代替"家"）。</summary>
    public float HomeAttachmentWeight = 0.35f;

    /// <summary>超过"多远"开始计入离家惩罚（格）。</summary>
    public float HomeAttachmentRadius = 25f;

    /// <summary>被"硬门"挡住时，效用乘以多少（0.15 表示基本不会选，但仍可能选）。</summary>
    public float BlockedUtilityMultiplier = 0.15f;

    /// <summary>动作的耐心上限（tick）：超过这个时间没完成就放弃，避免永远卡在一个目标上。</summary>
    public int ActionPatienceTicks = 900;
}

/// <summary>调试开关。</summary>
public sealed class DebugConfig
{
    /// <summary>是否记录每次 AI 决策（会显著拖慢速度，仅排障时开）。</summary>
    public bool LogDecisions = false;

    /// <summary>每隔多少 tick 计算一次状态摘要并写日志（0 = 关闭）。</summary>
    public int StateHashEveryTicks = 1440;

    /// <summary>是否在每 tick 后检查不变量（NaN/负值/越界）。开发期开，发布可关。</summary>
    public bool AssertInvariants = true;
}

/// <summary>
/// 配置加载。注意：这是 Core 里**唯一**允许触碰文件系统的位置，
/// 且只发生在世界创建之前，不参与模拟循环（见 docs/13 的确定性约束）。
/// </summary>
public static class ConfigLoader
{
    /// <summary>从文件加载；文件不存在时返回默认配置（不视为错误）。</summary>
    public static ConfigLoadResult<T> Load<T>(string path) where T : class, new()
    {
        var result = new ConfigLoadResult<T> { Source = path ?? string.Empty };

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            result.Error = null; // 用默认值不是错误
            result.Warnings.Add("未找到配置文件，使用内置默认值：" + (path ?? "(未指定)"));
            return result;
        }

        string text;
        try
        {
            text = System.IO.File.ReadAllText(path);
        }
        catch (System.Exception ex)
        {
            result.Error = "读取配置失败：" + ex.Message;
            return result;
        }

        return LoadFromJson<T>(text, path);
    }

    /// <summary>从 JSON 文本加载（测试与存档重放用）。</summary>
    public static ConfigLoadResult<T> LoadFromJson<T>(string json, string sourceName = "<memory>") where T : class, new()
    {
        var result = new ConfigLoadResult<T> { Source = sourceName };
        JsonValue root;
        try
        {
            root = JsonParser.Parse(json);
        }
        catch (JsonParseException ex)
        {
            result.Error = "配置 JSON 解析失败：" + ex.Message;
            return result;
        }

        if (!root.IsObject)
        {
            result.Error = "配置根节点必须是 JSON 对象";
            return result;
        }

        var target = new T();
        JsonBinder.Bind(root, target, result.Warnings, string.Empty);
        result.Value = target;
        return result;
    }

    /// <summary>把配置序列化为 JSON 文本（写"生效参数快照"用）。</summary>
    public static string ToJson(object config) => JsonBinder.ToJson(config).ToJson();
}
