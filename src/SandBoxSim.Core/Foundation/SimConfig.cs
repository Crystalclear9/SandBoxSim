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

    public float FoodCapacityPerGrassTile = 24f;
    public float FoodInitialFraction = 0.6f;
    public float FoodGrowthRate = 0.03f;
    public float FoodHarvestPerAction = 5f;

    /// <summary>是否每小时做一次再生（false 则每天一次，用于性能对比实验）。</summary>
    public bool RegenerateHourly = true;

    /// <summary>低于该比例时判定为"资源紧张"，会影响采集效用与迁移压力。</summary>
    public float DepletionWarnFraction = 0.2f;
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
