> 归档资料：保留原始要求与当时实现记录。页内版本、命令、任务流程和配图不代表当前功能；使用与开发请从 [现行文档](../README.md) 阅读。

# 04 — Data Structures

本文件描述模拟内核的**全部数据结构与字段语义**，与源码 1:1 对照。

| 类型 | 源文件 |
|---|---|
| `Int2` | `Foundation/Int2.cs` |
| `DeterministicRandom` / `SimRandom` / `RngStream` | `Foundation/DeterministicRandom.cs` |
| `Hash64` / `NoiseHash` | `Foundation/Hash64.cs`、`NoiseHash.cs` |
| `SimMath` / `UtilityCurve` / `PerlinNoise` | `Foundation/SimMath.cs`、`UtilityCurve.cs`、`PerlinNoise.cs` |
| `JsonValue` / `JsonParser` / `JsonBinder` | `Foundation/JsonValue.cs`、`JsonParser.cs`、`JsonBinder.cs` |
| `SimConfig` 及子配置 | `Foundation/SimConfig.cs` |
| `TerrainKind` / `TerrainInfo` | `World/TerrainKind.cs` |
| `ResourceKind` / `ResourceNode` / `ResourceStock` / `ResourceInfo` | `World/ResourceKind.cs` |
| `Tile` / `FireState` | `World/Tile.cs` |
| `WeatherKind` / `WeatherInfo` / `Weather` | `World/Weather.cs` |
| `Calendar` | `World/Calendar.cs` |
| `ChunkGrid` / `ChunkStats` / `ChunkStatsReadOnly` / `ChunkField` | `World/ChunkGrid.cs` |
| `World` | `World/World.cs` |
| `WorldGenerator.Result` | `World/WorldGenerator.cs` |
| `WorldEvent` / `WorldEventType` / `EventImportance` / `EventLog` | `History/EventLog.cs` |
| `DailySample` / `SimulationStats` / `StateHash` | `SimulationStats.cs` |
| `ResourceSystem` | `ResourceSystem.cs` |
| `Simulation` / `ISimEntitySet` | `Simulation.cs` |
| `AgentRef` / `AgentState` / `ActionKind` / `ActionPhase` / `JobType` / `LifeStage` / `Personality` / `NeedIndex` / `DeathCause` / `ActionFailReason` | `Agents/AgentTypes.cs` |
| `AgentStore` | `Agents/AgentStore.cs` |
| `Consideration` / `ActionScore` / `UtilityBreakdown` / `UtilityCombiner` | `Ai/UtilityBreakdown.cs` |
| `ActionDef` / `ActionRegistry` / `ActionEvaluator` / `TargetSelector` | `Systems/ActionDef.cs` |
| `ScoreBuilder` | `Systems/ScoreBuilder.cs` |
| `ActorContext` / `AiSystem` / `ActionSystem` / `NeedsSystem` / `DeathRecord` | `Systems/*.cs` |
| `AStarPathfinder` / `PathResult` / `BinaryHeap` / `GridPool` | `Pathing/AStarPathfinder.cs` |
| `UtilityCurve`（含 `Survival` 曲线） | `Foundation/UtilityCurve.cs` |

---

## 1. 基础类型

### 1.1 `Int2`

```csharp
readonly struct Int2 { int X; int Y; }
```

| 成员 | 语义 |
|---|---|
| `X`, `Y` | 格子坐标（整数） |
| `ChebyshevDistance(a,b)` | 八方向移动下的"走几步"距离 |
| `ManhattanDistance(a,b)` | 四方向距离 |
| `Distance(a,b)` / `SquaredDistance(a,b)` | 欧氏距离（效用衰减用；优先用平方距离避免开方） |
| `GetHashCode()` | `(X*397)^Y` —— **不用** `System.HashCode`（后者带运行时随机化种子） |

**约定**：任何需要"两个格子坐标"的地方都必须传 `Int2` 而不是两个 int 参数
（参数顺序写反在模拟代码里极难发现）。

### 1.2 `DeterministicRandom`

```csharp
sealed class DeterministicRandom {
    void Reset(ulong seed);
    ulong NextULong();
    int NextInt(int bound);            // [0,bound)
    int NextInt(int min, int maxEx);   // [min,max)
    double NextDouble();               // [0,1)
    float NextFloat();
    bool Chance(double probability);
    double Jitter(double magnitude);   // [-m,+m]
    int SampleWeightedIndex(double[] weights, int count);
    void Shuffle<T>(T[] array, int count);
    ulong[] ExportState();             // s0,s1,s2,s3,drawCount
    void ImportState(ulong[] state);
}
```

算法：**xoshiro256\*\*** + SplitMix64 初始化。

为什么选它：
1. 纯整数运算 → 跨平台/跨运行时**逐位一致**（不依赖浮点库实现）；
2. 周期 2^256−1，统计质量足够；
3. 状态只有 4 个 `ulong`，可以直接写进存档并精确续跑。

`NextInt` 用 Lemire 拒绝采样（无偏），`NextDouble` 取 53 位尾数（避免精度抖动影响分支判定）。

### 1.3 `SimRandom` 与 `RngStream`

```csharp
enum RngStream {
    WorldGen = 0,  // 世界生成：只在地图创建时使用
    Weather  = 1,  // 天气演变
    Agents   = 2,  // 决策抖动、出生性别与名字、性格抽样
    Events   = 3,  // 火灾点燃、灾害、稀有事件
    Combat   = 4,  // 战斗判定（M8）
    Misc     = 5,  // 杂项（必须注释说明用途）
    Reserve  = 6,  // 预留：避免后续加流破坏旧存档
}

sealed class SimRandom {
    ulong Seed;
    DeterministicRandom Get(RngStream stream);
    long TotalDrawCount;
    ulong[][] ExportState();
    void ImportState(ulong[][] state);
    void Reseed(ulong seed);
}
```

**分流的意义（第 77 条）**：如果用同一条 RNG，"多生成一个 NPC" 或"多记一条事件"
就会改变天气序列，整个世界历史被无关改动污染。分流后每个系统只吃自己的序列。

测试保证：`DrawingFromOneStreamDoesNotDisturbOthers`。

### 1.4 `Hash64`

FNV-1a 64 位。`Combine(hash, T)` 针对 `int/long/ulong/float/double/bool/string/Int2` 重载。

`float` / `double` 的哈希基于**位模式**（`BitConverter`），
不用 `Round` 也不用格式化字符串 —— 任何文化敏感的转换都会破坏跨机器一致性。

`ToDigestString(ulong)` → 固定 16 位小写十六进制。

### 1.5 `SimMath`

`Clamp`（int/long/float/double）、`Clamp01`、`Lerp`、`InverseLerp`（退化区间返回 0 而非除零）、
`Fract`、`SmoothStep`、`MapClamped`、`NearlyEqual`、`IsFinite`、`Square`。

**约束**：模拟内核禁止使用 `Math.Pow`（平台差异风险）与任何查找表近似；
需要平方就用 `Square`。

### 1.6 `UtilityCurve`

```csharp
sealed class UtilityCurve {
    enum Shape { Linear, Quadratic, Sqrt, Logistic, InverseLogistic, Step, SmoothStep, Constant }
    float Steepness;        // Logistic 的 k
    float Midpoint;         // Logistic 的 x0 / Step 的阈值
    float Edge0, Edge1;     // SmoothStep 下沿/上沿
    float ConstantValue;
    float Evaluate(float x);         // 输入裁剪到 [0,1]，输出保证 [0,1] 且有限
    static Shape ParseShape(string name);
}
```

| 形状 | 用途与直觉 |
|---|---|
| `Linear` | 中性：随需求平缓上升 |
| `Quadratic` | 生存紧迫感："不太饿时完全不想吃，快饿死了急剧上升" |
| `Sqrt` | 温和/勤劳型："稍有余量就愿意去做" |
| `Logistic` | 阈值型："低于阈值几乎不做，越过阈值立刻想做" |
| `InverseLogistic` | 机会型："条件越好越想抓"（实现为 `1 − L(1−x)`，**单调递增**） |
| `Step` | 硬条件：只有确实可行时才考虑 |
| `SmoothStep` | 平滑 S 曲线 |
| `Constant` | 纯权重项 |

**逆 Logistic 的坑（已修）**：早期写成 `1 − L(x)` 会得到**单调递减**曲线，
在大量动作里会静默反转行为倾向（"条件越好越不想做"）。
测试 `CurvesAreMonotonicAndBounded` 锁定"除常数外必须单调不减"。

### 1.7 `PerlinNoise`

```csharp
sealed class PerlinNoise {
    PerlinNoise(ulong seed);
    float Sample(float x, float y);              // 约 [-1,1]
    float Sample01(float x, float y);            // [0,1]
    float Fbm01(float x, float y, int octaves, float persistence, float lacunarity = 2f);
}
```

置换表由 `DeterministicRandom(seed)` 洗牌生成，因此**同 seed 逐点一致**。

**已知性质（很重要）**：fBm 叠加结果**聚集在 0.5 附近**（中心极限效应），
取值范围随"频率 × 倍频数 × 地图尺寸"变化。因此**不能**把它的绝对值直接与固定阈值比较
—— 见 §7.2 的分位数方案。

### 1.8 `JsonValue` / `JsonParser` / `JsonBinder`

```csharp
sealed class JsonValue {
    enum Kind { Null, Bool, Number, String, Array, Object }
    JsonValue NextInt/GetInt/GetFloat/GetDouble/GetBool/GetString/GetLong/GetULong/GetIntArray(string key, ...)
    JsonValue Get(string key);
    JsonValue this[int index];      // 数组下标（越界返回 Null）
    JsonValue this[string key];     // 对象按键
    List<JsonValue> Items;
    Dictionary<string, JsonValue> Fields;
    string ToJson(bool indented = true);
}
```

| 组件 | 职责 |
|---|---|
| `JsonParser` | 递归下降解析，**容忍 `//` 与 `/* */` 注释**（配置文件要给人读），错误带行列号 |
| `JsonBinder` | 反射把 JSON 映射到强类型配置；以 `$` 开头的键视为注释跳过；未知键产生**警告**而不是静默忽略 |
| `ConfigLoader` | 文件/文本加载入口，返回 `ConfigLoadResult<T>{Value, Warnings, Error, Source}` |

**踩坑记录**：`json[0]` 会被 C# 解析成**字符串索引器**（`0` 可隐式转 `"0"`），
于是它变成"按键取值"，静默返回 `Null`。读数组元素必须写 `json.Items[i]`。

---

## 2. 配置结构（`SimConfig`）

```csharp
sealed class SimConfig {
    int Version;
    ClockConfig   Clock;
    WorldConfig   World;
    WorldGenConfig WorldGen;
    ResourceConfig Resources;
    DebugConfig   Debug;
    SimConfig Clone();          // 深拷贝（换 seed 时保持参数不变）
}
```

### 2.1 `ClockConfig`

| 字段 | 默认 | 含义 |
|---|---|---|
| `MinutesPerTick` | 1 | 每 tick 游戏分钟数 |
| `TicksPerHour` | 60 | 一小时多少 tick |
| `HoursPerDay` | 24 | 一天多少小时 |
| `TicksPerSecondAt1x` | 10 | 1× 速度的 TPS |
| `SpeedMultipliers` | `[0,1,2,4,8]` | 倍速档（0 = 暂停） |
| `TargetFramesPerSecond` | 60 | 渲染目标（与模拟解耦） |
| `MaxCatchUpTicksPerFrame` | 40 | 单帧追赶上限 |
| `TicksPerDay`（派生） | 1440 | — |

### 2.2 `WorldConfig`

| 字段 | 默认 | 含义 |
|---|---|---|
| `Width` / `Height` | 100 / 100 | 地图尺寸 |
| `ChunkSize` | 16 | 空间索引块边长 |
| `AmbientTemperature` | 0.55 | 世界基准气温 [0,1] |
| `AmbientMoisture` | 0.5 | 世界基准湿度 [0,1] |

### 2.3 `WorldGenConfig`

| 字段 | 默认 | 含义 |
|---|---|---|
| `Seed` | 839102 | 默认种子 |
| `ContinentFrequency` | 0.018 | 高度图基频（越小大陆越大） |
| `ContinentOctaves` | 4 | 高度图倍频层数 |
| `ContinentPersistence` | 0.5 | 高度图振幅衰减 |
| `MoistureFrequency` | 0.035 | 湿度图基频 |
| `MoistureOctaves` | 3 | 湿度图倍频层数 |
| `MoisturePersistence` | 0.55 | 湿度图振幅衰减 |
| `DetailFrequency` | 0.12 | 细节噪声频率（海岸线碎屑、成林斑点） |
| **`WaterLevel`** | 0.28 | **水面目标占比**（格子比例，见 §7.2） |
| **`MountainLevel`** | 0.16 | **山地目标占比** |
| `WaterShareJitter` | 0.10 | 水面占比的种子间波动 ± |
| `MountainShareJitter` | 0.07 | 山地占比的种子间波动 ± |
| `SandBand` | 0.035 | 沙滩带宽（相对于山-水高度差的比例） |
| `ForestMoistureThreshold` | 0.52 | 成林所需最低湿度 |
| `ForestDensityBonus` | 0.18 | 成林概率基础加成 |
| `FertilityFromHeight` | true | 是否让海拔影响肥沃度 |
| `FertilityBase` | 0.55 | 肥沃度基准 |
| `FertilityNoiseFrequency` | 0.06 | 肥沃度噪声频率（造"沃土带"） |

### 2.4 `ResourceConfig`

| 字段 | 默认 | 含义 |
|---|---|---|
| `WoodCapacityPerForestTile` | 100 | 森林格木材容量 K |
| `WoodInitialFraction` | 0.85 | 初始存量 / K |
| `WoodGrowthRate` | 0.02 /天 | Logistic 增长率 r |
| `WoodHarvestPerAction` | 6 | 一次采伐动作的采集量 |
| `StoneCapacityPerMountainTile` | 80 | 山地格石头容量 |
| `StoneInitialFraction` | 0.9 | 初始比例 |
| `StoneHarvestPerAction` | 4 | — |
| `IronCapacityPerMountainTile` | 30 | 铁矿容量 |
| `IronInitialFraction` | 0.6 | 初始比例 |
| `IronHarvestPerAction` | 2 | — |
| `FoodCapacityPerGrassTile` | 24 | 野生食物容量基准（再乘肥沃度系数） |
| `FoodInitialFraction` | 0.6 | 初始比例 |
| `FoodGrowthRate` | 0.03 /天 | 野生食物再生率 |
| `FoodHarvestPerAction` | 5 | — |
| `RegenerateHourly` | true | 按小时再生（false = 按天，用于性能对比） |
| `DepletionWarnFraction` | 0.2 | 低于此比例判定"资源紧张" |

**为什么木材采集（6/动作）远大于再生（2/天）**：这是刻意的不对称。
如果再生速度接近采集速度，"过度采集 → 资源枯竭 → 迁移"这条链就永远不会触发，
整个世界会退化成"资源永远够用"的静态世界。

### 2.5 `DebugConfig`

| 字段 | 默认 | 含义 |
|---|---|---|
| `LogDecisions` | false | 记录每次 AI 决策（显著变慢，仅排障） |
| `StateHashEveryTicks` | 1440 | 每多少 tick 计算一次状态摘要（0 = 关） |
| `AssertInvariants` | true | 每 64 tick 检查一次不变量 |

---

## 3. 环境结构

### 3.1 `TerrainKind` 与 `TerrainInfo`

```csharp
enum TerrainKind : byte {
    Grass, Forest, Water, Mountain, Sand,     // M0 世界生成产出
    Farmland, Road,                            // 模拟过程产出（M3/M4）
    Snow, Swamp, Desert, Lava                  // 预留
}
```

`TerrainInfo` 是**唯一**的地形规则表（第 7 条）：

| 查询 | 说明 |
|---|---|
| `IsWalkable` | 是否可通行（Water / Lava = false） |
| `IsBuildable` | 是否可建造（Water / Farmland / Lava = false） |
| `IsFarmable` | 是否可开垦（仅 Grass） |
| `MoveCost` | A\* 进入代价：Grass 1、Forest 1.5、Mountain 3、Road 0.6、不可走 99 |
| `ProvidesWater` | 是否提供饮水/灌溉加成 |
| `IsVegetation` | 是否可燃/可伐/可供动物栖息 |
| `NameOf` / `TryParse` | 稳定的字符串标识（UI 与存档用） |

**为什么集中在一张表**：如果寻路、建造、AI 各写一份判断，三处很快会不一致，
表现为"小人穿墙"或"建筑盖在水里"这类极难定位的 bug。
测试 `EveryTerrainHasRules` 强制：不可通行地形必须给出高代价（> 10），
否则寻路会产生"零成本穿水"的路径。

### 3.2 `Tile`

```csharp
struct Tile {
    TerrainKind  Terrain;       // 地形
    FireState    Fire;          // None / Burning / Burnt
    int          BuildingId;    // 地上建筑索引（-1 = 无）
    float        Fertility;     // [0,1] 肥沃度
    float        Moisture;      // [0,1] 湿度
    float        Temperature;   // [0,1] 归一化温度（不是摄氏度）
    float        Vegetation;    // [0,1] 植被量（可燃物/栖息地）
    ResourceNode Resource;      // 本格资源节点
    bool         Walkable;      // 可通行（默认由地形决定，可被桥/河改道覆盖）
    bool         Buildable;     // 可建造（同上）
}
```

约 44 字节。100×100 = 1 万格 → 约 440 KB。放大到 500×500 也只有 11 MB。

**不变量**（`Simulation.ValidateInvariants` 检查）：

- `Moisture` / `Fertility` / `Temperature` / `Vegetation` 均为有限数且 ∈ [0,1]；
- `Resource.Amount ≥ 0` 且 `≤ Capacity`；
- `BuildingId ≥ -1`；
- `Walkable` / `Buildable` 与地形规则一致（**除非**显式被桥/道路等覆盖 —— 这类覆盖必须走 `SetWalkable`）。

### 3.3 资源

```csharp
enum ResourceKind : byte { None, Food, Wood, Stone, Iron }

struct ResourceNode {
    ResourceKind Kind;
    float Amount;              // 当前存量
    float Capacity;            // 环境容量 K（Logistic 上限）
    float RegenerationRate;    // 每天增长率 r（矿产为 0 = 不可再生）
    float Fraction { get; }    // Amount / Capacity 裁剪到 [0,1]
    float Harvest(float wanted);                 // 扣除并返回实际取得量
    static float RegenerateLogistic(float a, float k, float r, double days);
}

struct ResourceStock {         // 库存（4 种资源具名字段）
    float Food, Wood, Stone, Iron;
    float Get(ResourceKind);
    void  Set(ResourceKind, float);
    void  Add(ResourceKind, float);   // 负数会被裁剪到 0
    float Total { get; }
}
```

**离散 Logistic 再生**（第 63 条）：

```text
A ← clamp( A + r · A · (1 − A/K) · Δdays , 0, K )
```

**为什么不用固定 +2/天**：Logistic 在存量低时再生也慢，
因此"砍光了这片林子"会留下真实且短期无法恢复的损伤。这是资源压力链的物理基础。

### 3.4 天气

```csharp
enum WeatherKind : byte { Clear, Cloudy, Rain, Storm, Drought, Snow }
```

`WeatherInfo` 提供每种天气的 4 个系数：`MoistureDeltaPerHour`、`TemperatureDeltaPerHour`、
`CropFactor`（作物系数 [0,1.5]）、`FireRiskFactor`、`MovementFactor`。

```csharp
sealed class Weather {
    WeatherKind Kind;
    int DurationHours;          // 已持续小时
    float AccumulatedRain;
    int DroughtHours;           // 干旱累计（事件触发用）
    void AdvanceHour(DeterministicRandom rng, float avgMoisture, float avgTemperature);
    void ForceKind(WeatherKind kind, int durationHours);   // 玩家工具用
}
```

**天气转移用"状态驱动 + 少量随机"**（第 44 条），不是纯随机：

| 天气 | 权重 |
|---|---|
| Clear | 0.35 × 干燥度 |
| Cloudy | 0.25 |
| Rain | 0.30 × 湿度 |
| Storm | 0.10 × 湿度 |
| Drought | 0.15 × 干燥度² |
| Snow | 0.40 × 寒冷度 |

每次切换后持续 4~16 小时。这样才会形成"连续干旱 → 火灾风险升高 → 饥荒"的长链条，
而不是每小时的天气白噪声。

### 3.5 `Calendar`

| 成员 | 含义 |
|---|---|
| `Tick` | 累计 tick（模拟唯一时间基准） |
| `Day` | 第几天（1 起） |
| `CompletedDay` | **刚结束的那一天** —— 日统计必须用它（见下） |
| `Hour` / `Minute` | 当天时分 |
| `TicksPerDay` | 派生：1440 |
| `IsDay` / `IsNight` | 06:00–19:00 为白天 |
| `LightLevel` | [0,1] 光照（正午 1.0，午夜约 0.05） |
| `IsHourBoundary` / `IsDayBoundary` | 供 Tick 管线判定 |
| `DisplayStamp()` | `"Day 12 07:30"` |

**`CompletedDay` 的必要性（踩坑）**：日边界上 `Day` 已经翻到新的一天，
但当日统计描述的是**刚结束的那一天**。早期用 `Day` 导致报告里所有日期虚报一天。

---

## 4. 空间索引

```csharp
enum ChunkField {
    FertilitySum, MoistureSum, TemperatureSum, VegetationSum,
    ForestCount, WaterCount, MountainCount, GrassCount, FarmlandCount,
    WalkableCount, BuildableCount,
    WoodAmount, FoodAmount, StoneAmount, IronAmount, BurningCount,
}

struct ChunkStats { double[] Fields; int CellCount; bool Dirty; }

readonly struct ChunkStatsReadOnly {           // 只读视图（AI/UI 用）
    bool IsValid; int CellCount;
    double Raw(ChunkField); double Average(ChunkField);
    int ForestTiles, WaterTiles, MountainTiles, GrassTiles, FarmlandTiles,
        WalkableTiles, BuildableTiles, BurningTiles;
    float WoodAmount, FoodAmount, StoneAmount, IronAmount;
    float AverageFertility, AverageMoisture, AverageTemperature, AverageVegetation;
    float AmountOf(ResourceKind);
}

sealed class ChunkGrid {
    int Width, Height, ChunkSize, ChunkCols, ChunkRows, ChunkCount, DirtyCount;
    int ChunkIndexAt(int x, int y);
    int ChunkIndex(int chunkX, int chunkY);
    bool IsValidChunk(int cx, int cy);
    void GetBounds(int cx, int cy, out int minX, out int minY, out int maxX, out int maxY);
    ChunkStatsReadOnly Read(int cx, int cy);
    ChunkStatsReadOnly ReadIndex(int index);
    ChunkStatsReadOnly ReadAt(int tileX, int tileY);   // ← AI 最常用的入口
    void MarkDirty(int chunkIndex);
    void MarkAtDirty(int tileX, int tileY);
    void MarkAllDirty();
    void Refresh(Tile[] tiles);                        // 重算所有脏块
    void ExportState(out double[][] fields, out int[] cellCounts, out bool[] dirty);
    void RestoreState(double[][]?, int[]?, bool[]?);
}
```

**语义要点**：

- 这是**缓存**，不是真相；真相永远在 `Tile[]`。
- `CellCount` 是边缘块的真实格子数（不满），用于把"求和"转成"均值"。
- 脏块重算发生在 `Simulation.Tick` 的最后一步，保证同一 tick 内所有读者看到一致快照。

测试 `ChunkStatsMatchGroundTruth` 逐块与逐格真值比对 —— 空间索引出错是
"AI 去不存在的森林砍树"这类 bug 的根源，必须有强测试。

---

## 5. `World`（唯一的 Tile 写入口）

### 5.1 状态字段

```csharp
sealed class World {
    SimConfig Config;
    int Width, Height, Seed, Revision;
    Tile[] Tiles;                 // 真相，行主序：index = y * Width + x
    ChunkGrid Chunks;             // 空间索引缓存
    Weather Weather;              // 天气状态
    Calendar Calendar;            // 时间
    long Tick { get; }
}
```

### 5.2 读取

| 方法 | 说明 |
|---|---|
| `TileAt(int index)` / `TileAt(int x,int y)` / `TileAt(Int2)` | 返回 `ref readonly Tile`（零拷贝，热路径用） |
| `TileAtClamped(x, y)` | **越界安全**：返回"深水空 Tile"，让渲染/邻域扫描不必写边界判断 |
| `TerrainAt(x, y)` | 取地形 |
| `IsInBounds` / `IsBuildableAt` / `IsWalkableAt(index)` | 判定 |
| `CountTerrain()` | 全图地形计数（统计用，别放热路径） |
| `TotalResource(kind)` / `TotalCapacity(kind)` | 全图资源总量/总容量 |
| `AverageMoisture()` / `AverageTemperature()` | 全图均值（每小时调用） |
| `DefaultResourceFor(terrain, fertility)` | **按地形给出默认资源节点的唯一表** |

### 5.3 写入（全部会自动同步空间索引）

| 方法 | 语义 |
|---|---|
| `ReplaceAllTiles(Tile[], seed)` | 整图替换（世界生成/重置）；全量标脏 |
| `SetTerrain(x, y, kind)` | 改地形 + 同步通行/可建造规则 + 清火（非森林） |
| `SetFertility` / `SetMoisture` / `SetTemperature` / `SetVegetation` | 裁剪到 [0,1] |
| `SetWalkable` / `SetBuildable` | 显式覆盖规则（桥、道路等） |
| `SetFire(x, y, state)` | 火灾状态 |
| `SetBuilding(x, y, id)` | 建筑索引 |
| `SetResource(x, y, node)` / `ClearResource(x, y)` | 直接改资源节点 |
| `ApplyDefaultResource(x, y)` | 按当前地形重置资源（开垦/地形改造后调用） |
| `MarkDirtyAt(x, y)` | 批量修改后手动标脏（避免逐格维护） |
| `RefreshSpatialIndex()` | 由 `Simulation` 每 tick 调用一次 |

**为什么写入口必须收窄**：所有对 Tile 的写入都必须同步空间索引，
否则 chunk 统计会悄悄过期，AI 就会去一片已经不存在的森林里砍树。

### 5.4 `WorldGenerator.Result`

```csharp
sealed class Result {
    int Seed;
    float MinHeight, MaxHeight, AverageHeight;
    int WaterTiles, MountainTiles, ForestTiles, GrassTiles, SandTiles;
    float TotalWood, TotalForage;
    float WaterShare, MountainShare;   // 本张地图实际采用的水/山占比
    string ToString();                 // 单行摘要（启动提示与批量报告用）
}
```

---

## 6. 历史与统计

### 6.1 事件

```csharp
struct WorldEvent {
    long Tick;
    WorldEventType Type;
    int Actor, Target;          // 实体索引（-1 = 无）
    Int2 Location;              // (-1,-1) = 无地点
    EventImportance Importance; // Trivial < Minor < Normal < Important < Critical
    string Description;
    string Cause;               // 因果标签（第 92 条可解释性）
}

sealed class EventLog {
    const int DefaultCapacity = 20000;
    int Count, Capacity;  long TotalRecorded;
    WorldEvent this[int index];                    // 0 = 最旧
    WorldEvent Latest;
    void Record(long tick, WorldEventType type, string desc, ...);
    void Record(in WorldEvent ev);
    bool TryFindLast(Func<WorldEvent,bool>, out WorldEvent);
    int CountOf(WorldEventType);
    WorldEvent[] Recent(int n, EventImportance min);   // 时间正序
    WorldEvent[] ForActor(int actorId, int max);       // 个人时间线
    void Clear();
}
```

**环形缓冲的意义**：事件只增不删，但玩家只关心最近若干条。
保留最近 2 万条让长期运行的模拟不会无限吃内存（第 73 条）。

`WorldEventType` 现存分组：世界级（1–6）、火灾与灾害（10–13）、
人物（20–26）、建筑（30–32）、聚落（40–44）、社会（50–59）。
新增类型必须同步更新 `docs/10` 的事件表与报告的"关键事件"筛选规则。

### 6.2 统计

```csharp
struct DailySample {
    int Day, Population, Births, Deaths, Migrations, SettlementCount, BuildingCount;
    float Food, Wood, Stone, Iron, AverageMoisture, AverageTemperature, Scarcity;
    int BurningTiles, ForestTiles, FarmlandTiles;
    float FoodPerCapita();      // 扩展方法
}

sealed class SimulationStats {
    int TotalBirths, TotalDeaths, TotalMigrations, PeakPopulation, LowestPopulation;
    bool FamineActive;  int FamineEpisodeCount;
    IReadOnlyList<DailySample> Daily;  DailySample Latest;
    void RecordBirth() / RecordDeath() / RecordMigration();
    void RecordDay(DailySample sample, float foodPerCapitaThreshold);
    DailySample[] RecentDays(int count);
    void Reset();
}
```

**饥荒是统计口径的结果，不是剧情开关**：`RecordDay` 在"人均食物低于阈值"时
翻转 `FamineActive` 并累加 `FamineEpisodeCount`。UI 直接显示这个状态。

**统计只读**：`SimulationStats` 不允许修改任何模拟状态 ——
否则"打开统计面板"就会改变世界，实验的可复现性直接消失。

### 6.3 状态摘要

```csharp
static class StateHash {
    static ulong Compute(Simulation sim);
    static string ComputeDigest(Simulation sim);   // 16 位十六进制
}
```

覆盖内容与量化策略见 [13-DeterminismAndSave](13-DeterminismAndSave.md)。

---

## 7. 关键算法

### 7.1 Logistic 再生（`ResourceNode.RegenerateLogistic`）

```text
输入：A（当前存量）、K（容量）、r（每日增长率）、days（本次覆盖的天数份额）
ratio  = A / K
growth = r · A · (1 − ratio) · days
输出   = clamp(A + growth, 0, K)
```

性质：
- `A → 0` 时 growth → 0（枯竭后难以恢复）；
- `A → K` 时 growth → 0（不会超容量）；
- 中间最陡（`A = K/2` 时单位时间增量最大）。

测试：`LogisticRegenerationCapsAtCapacity`、`RegenerationSlowsWhenDepleted`。

### 7.2 世界生成的地形分类（分位数方案）

```text
1. 用 3 个独立噪声场算出每格：
     height     = fBm(ContinentFrequency, ContinentOctaves, Persistence) + 细节扰动
     moisture   = fBm(MoistureFrequency, MoistureOctaves, Persistence)
                   − 0.5 + AmbientMoisture + 临水加成(距离水体 ≤ 12 格)
     temperature = AmbientTemperature − (归一化高度 − 0.5) × 0.5
2. 统计高度分位数（对高度数组排序后插值取样）：
     水面阈值   = Quantile(heights, 1 − waterShare)       // 取最干的 waterShare 那一部分
     山地阈值   = Quantile(heights, mountainShare)         // 取最高的 mountainShare 那一部分
     其中 waterShare   = WaterLevel   + seed 抖动(± WaterShareJitter)
          mountainShare = MountainLevel + seed 抖动(± MountainShareJitter)
3. 逐格分类：
     height ≤ 水面阈值 → Water（湿度强制为 1）
     height ≥ 山地阈值 → Mountain
     距离水体 ≤ 2 且高度接近水面 → Sand（沙滩）
     否则按湿度与噪声决定 Forest / Grass
4. 归一化高度 = (height − minHeight) / (maxHeight − minHeight)   // 供温度/肥沃度用
5. 资源：Forest→Wood、Mountain→Stone（约 14% 位置改为 Iron）、Grass/Sand→Food
```

**为什么用分位数而不是固定阈值（第 83 条的实证修正）**：

fBm 的取值聚集在 0.5 附近，范围又随地图尺寸与频率变化。
早期版本直接用 `WaterLevel = 0.34` / `MountainLevel = 0.72` 与噪声比较，
结果是**小地图上完全没有水域和山地** —— 而这两者是寻路、资源分布、聚落选址的舞台，
缺了它们后续所有涌现行为都无从发生。

分位数方案把 `WaterLevel` / `MountainLevel` 的语义从"绝对高度阈值"改为
**"水面/山地的目标占比"**，于是"每张地图都有水有山"变成结构性保证，
同时用种子派生的比例抖动保留"不同地图海陆比例不同"的多样性。

测试：`GenerationProducesAllCoreTerrains`（多 seed 都必须五种地形齐全）、
`TilePropertiesAreSane`（属性范围与规则一致性）、`WaterTilesAreWet`（水域湿度饱和）。

---

## 8. 个体（M1）

### 8.1 `AgentRef`（稳定引用）

```csharp
readonly struct AgentRef { int Slot; int Generation; bool IsNone; }
```

**为什么不用索引当 ID**：Agent 会死，索引会被回收给新个体。
外部（检查器、事件日志、关系系统）拿着裸索引，个体死亡 + 槽位复用之后就会"看错人" ——
这类 bug 不报错，只是数据串了。

因此引用 = (槽位, 代次)：槽位复用时代次 +1，旧引用**立刻失效**（`AgentStore.IsValid` 返回 false）。

### 8.2 `AgentStore`（SoA）

所有个体字段都是**下标对齐的数组**（约 40 个），而不是 `List<Agent>`：

| 分组 | 字段 |
|---|---|
| 生命周期 | `_generation[]`、`_alive[]`、`_deathCause[]`、`_birthTick[]`、`_deathTick[]` |
| 位置 | `_x[]`、`_y[]`、`_prevX[]`、`_prevY[]`、`_homeX[]`、`_homeY[]`、`_facing[]` |
| 需求 | `_hunger[]`、`_fatigue[]`、`_thirst[]`、`_social[]` |
| 生理 | `_health[]`、`_ageDays[]`、`_lifeStage[]`、`_job[]` |
| 库存 | `_invFood[]`、`_invWood[]`、`_invStone[]`、`_invIron[]` |
| 性格 | `_aggression[]`、`_greed[]`、`_kindness[]`、`_bravery[]`、`_industriousness[]`、`_sociability[]` |
| 动作 | `_state[]`、`_action[]`、`_actionPhase[]`、`_failReason[]`、`_targetX/Y[]`、`_pathNextX/Y[]`、`_hasPathStep[]`、`_actionTicks[]` |
| 决策 | `_decisionPhase[]`、`_nextDecisionTick[]`、`_lastDecision[]`（`UtilityBreakdown`） |

**为什么用 SoA**：
1. 决策与需求更新会遍历**全部个体的同一类字段**，SoA 的访问是连续的，缓存命中率远高于对象数组；
2. 没有对象引用 ⇒ GC 压力接近零，长期运行（几十万 tick）不会出现停顿；
3. 状态摘要可以按数组顺序遍历，天然确定。

**关键方法**：`FindSpawnPosition`（螺旋找可走格）、`Add`（返回 `AgentRef`）、
`MarkDead(slot, cause, tick)`（**保留数组内容**，只改存活标志与代次，
这样死亡时刻的位置/年龄/死因还能被日志读到）、`AssignDecisionPhases(batchCount)`、
`AliveSlots()`（按槽位顺序，确定性）、`NameOrOverride`、`HashInto`。

### 8.3 `Personality`（六维 [0,1]）

`Aggression` / `Greed` / `Kindness` / `Bravery` / `Industriousness` / `Sociability`。

抽样用"两个均匀数取平均"（中间偏高、两端稀有），因此**极端性格是稀有的** ——
这样个体差异才有观察价值。M1 只有勤劳真正进入效用；其余在 M6 接入。

### 8.4 `UtilityBreakdown`（可解释性）

```csharp
readonly struct Consideration {
    string Name; float Input; UtilityCurve Curve; float Weight;
    float Score; float Contribution;    // Score = Curve(Input)，Contribution = Weight × Score
}
readonly struct ActionScore {
    ActionKind Action; float Utility; Consideration[] Considerations;
    float WeightedAverage; float GeometricMean; bool Blocked;
}
readonly struct UtilityBreakdown {
    ActionKind Chosen; ActionScore[] Scores; float ChosenUtility; long Tick;
}
```

每个个体保存最近一次决策的 `UtilityBreakdown`（Top-N 个动作，含全部分解）。
它的存在让"为什么他没去吃饭"变成可以直接读出来的事实，而不是需要推断的猜测。

### 8.5 `AStarPathfinder`

```csharp
struct PathResult { bool Success; int Length; float Cost; int ExpandedNodes; }

sealed class AStarPathfinder {
    PathResult FindPath(int sx, int sy, int gx, int gy, Int2[] outPath);
    PathResult FindNextStep(int sx, int sy, int gx, int gy, out Int2 nextStep);
    int MaxExpandedNodes;                  // 默认 4000
    long TotalSearches, FailedSearches;    // 诊断
}
```

**确定性保证**：`BinaryHeap` 在 f 值相同时按格子索引升序，因此同输入必得同路径。

**corner cutting 的处理**：对角移动要求**两侧不同时不可走**
（而不是"两侧都必须可走"）。后者会把"从拐角外侧正常绕过"也判为非法 ——
这个区别在测试里踩过坑（见 [12-Milestones](12-Milestones.md) M1 联调记录）。

### 8.6 `ActionContext`

一次决策需要的全部上下文（世界、个体、配置、chunk 聚合、是否夜晚、家、随机源）。
动作函数因此是**纯函数风格**的：输入全在参数里，不会偷偷读 UI 状态或消耗随机数。
