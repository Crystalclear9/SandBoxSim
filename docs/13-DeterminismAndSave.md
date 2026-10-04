# 13 — Determinism & Save

模拟游戏最容易被忽视、也最昂贵的能力是**可复现**：

- 没有它，玩家报告"我的村庄莫名灭亡了"时无法复现，只能猜；
- 没有它，调参变成玄学（"我改了木材再生率，人口曲线变了，但也许是别的原因"）；
- 没有它，"涌现性"这个卖点根本无法验证。

本文件规定确定性契约、状态摘要的定义、以及存档格式。

---

## 1. 确定性契约（写代码时的硬约束）

以下行为在 `SandBoxSim.Core` 里**禁止**：

| 禁止 | 原因 | 替代方案 |
|---|---|---|
| `System.Random` | 实现细节可能随运行时版本变化 | `SimRandom.Get(RngStream.X)` |
| `DateTime.Now` / `DateTime.UtcNow` | 结果依赖真实时间 | `World.Calendar.Tick` |
| `Stopwatch` / `Environment.TickCount` | 同上 | 同上（表现层可用，Core 不可用） |
| 遍历 `Dictionary` / `HashSet` | 迭代顺序不保证 | 索引数组 / `List` / 有序集合 |
| `object.GetHashCode()` | .NET 对 string 等类型加随机化种子 | `Hash64` |
| `float.ToString()` / `double.ToString()` 参与逻辑判定 | 文化设置影响格式与舍入 | 直接数值比较 |
| `Math.Pow`（热路径） | 跨平台实现可能有 1 ULP 差异 | `Square` / 手写整数幂 |
| 并行写入同一状态 | 归约顺序影响浮点结果 | 单线程；未来只允许"只读并行 + 串行写回" |
| 渲染层修改模拟状态 | 破坏"模拟只由 tick 推进" | 走 `Simulation.Intervene*` 接口 |
| 渲染层读取 `Simulation.Random` | 会消耗随机数，改变后续序列 | 用独立的视觉噪声 `NoiseHash` |

### 1.1 随机数分流（第 77 条）

```csharp
enum RngStream { WorldGen, Weather, Agents, Events, Combat, Misc, Reserve }
```

**为什么必须分流**：如果用同一条 RNG，那么"多生成一个 NPC"或"多记一条事件"
就会改变天气序列，整个世界历史被无关改动污染。

测试：`RandomTests.DrawingFromOneStreamDoesNotDisturbOthers`
（在 `WorldGen` 流上多消耗 100 个随机数后，`Weather` 流的第一个值必须不变）。

**新增随机用途时的规则**：

1. 优先复用已有流（并注明用途）；
2. 确实需要新语义才用 `Reserve`；
3. **绝不改变已有流的语义或顺序** —— 那会让所有旧存档的续跑结果变化。

### 1.2 浮点与量化

跨运行时的浮点运算在 **同一条指令序列** 下是逐位一致的；
但以下两种情形会产生差异，摘要计算必须容忍：

1. 编译器/运行时的 JIT 差异（例如向量化、FMA 融合）；
2. 允许重排的运算顺序（例如并行归约）。

因此 `StateHash` 对连续量做**量化后再哈希**：

| 字段 | 量化 | 理由 |
|---|---|---|
| `Resource.Amount` | `(int)(Amount × 100)` | 精确到 0.01 —— 小于它的差异对行为无影响 |
| `Moisture` | `(int)(Moisture × 1000)` | 精确到 0.001 |
| `Fertility` | `(int)(Fertility × 1000)` | 同上 |
| 地形 / 火状态 / 资源种类 / 建筑索引 | 整数，逐位参与 | 离散量不允许容差 |

这个取舍是刻意的：**"行为等价"比"位完全一致"更重要**。
如果坚持位级一致，任何一次 JIT 升级都会让所有历史快照失效，而收益为零。

---

## 2. 状态摘要（State Digest）

```csharp
ulong StateHash.Compute(Simulation sim);
string StateHash.ComputeDigest(Simulation sim);   // 16 位小写十六进制
```

### 2.1 覆盖范围

| 类别 | 具体内容 |
|---|---|
| 世界身份 | `World.Seed`、`Width`、`Height` |
| 时间 | `World.Tick` |
| 天气 | `Weather.Kind`、`DurationHours` |
| 全部 Tile | 地形、火状态、资源种类、资源量（×100）、湿度（×1000）、肥沃度（×1000）、建筑索引 |
| 全部实体 | 每个 `ISimEntitySet.HashInto` 的贡献（M1 起：agent 位置/需求/库存/状态；M3 起：建筑） |
| 系统计数 | `DepletionEvents`、`TotalBirths`、`TotalDeaths`、`TotalMigrations` |

### 2.2 刻意**不**包含的内容

| 不包含 | 理由 |
|---|---|
| 渲染缓存 / 相机 / 选中状态 | 属于表现层，不该影响模拟身份 |
| RNG 内部状态 | 由存档单独保存；摘要只表达"世界状态"，不表达"随机流位置" |
| 事件日志内容 | 日志是**派生**的观察数据，且容量有限（环形缓冲），不适合作为身份 |
| 统计曲线 | 同上（可从 Tile 与计数重算） |
| 性能计数（DirtyCount 等） | 缓存状态，与模拟语义无关 |

### 2.3 使用方式

```powershell
# 端到端校验：同 seed 连跑两遍 + 换 seed 对照
.\tools\run.ps1 -Mode digest -Days 30

# 输出（真实运行结果）
#   第 1 次：digest=dc94d8d5daf8d42d  用时=0.48s
#   第 2 次：digest=dc94d8d5daf8d42d  用时=0.48s
#   对照 seed=4243：digest=18cb2ff5386c29c6（与基准不同，符合预期）
#   确定性校验通过
```

退出码：一致 → `0`；不一致 → `1`（可直接被 CI 使用）。

**摘要不一致的排查顺序**（按经验命中率排序）：

1. 是否用了 `System.Random`？（`grep -r "System.Random" src/SandBoxSim.Core`）
2. 是否遍历了字典/哈希集合？
3. 是否读了真实时间？
4. 是否在渲染层调用了 `sim.Random`（消耗了随机数）？
5. 是否有系统在"没有变化"时也写了状态（导致标脏顺序差异）？
6. 新增系统是否漏了 `HashInto`（于是摘要变化未被捕捉，或反之误报）？

---

## 3. 存档（Save / Load）

### 3.1 设计目标

| 目标 | 含义 |
|---|---|
| **精确续跑** | 读档后继续运行的轨迹，必须与"从未中断地跑"完全一致 |
| **人类可读** | 出问题时可以用文本工具查看与手改（模拟调试的刚需） |
| **可诊断** | 存档里包含"当时生效的配置"和"当时的事件日志尾部" |
| **向前兼容** | 新增字段时旧存档仍可读（缺失字段用默认值） |

第一版刻意选择**文本格式**（而非二进制）：体积大一点，但调试价值高得多。
模拟游戏的状态爆炸类 bug 几乎无法在二进制存档上排查。

### 3.2 内容清单（与实现 1:1）

```text
{
  version: 2                  // 不匹配 → 明确拒绝，不做静默降级
  seed, width, height          // seed 必须恢复：它参与状态摘要
  tick, day
  weather:   { kind, durationHours, hoursUntilChange, accumulatedRain, droughtHours }
  tiles:     { terrain[], fire[], resourceKind[], resourceAmount[], resourceCapacity[],
               resourceRegenerationRate[],      // ← 见 3.2.1 第 6 条
               moisture[], temperature[], fertility[], vegetation[], buildingId[] }
  chunks:    { fields[][16], cellCounts[], dirty[] }   // ← 见 3.2.1 第 5 条
  rng:       [ 8 条流 × { s0,s1,s2,s3,drawCount } ]     // 以字符串写 ulong，避免 double 丢精度
  agents:    { totalBorn, totalDied, peakPopulation, list[] }
  wildlife:  { liveOrder[], nextFreeHint, totalBorn, totalDied, totalHunted, list[] }
  buildings: { totalBuilt, totalDemolished, list[] }
  storage:   [ { slot, capacity, food, wood, stone, iron } ]
  groundStocks: { totalDeposited, totalWithdrawn, list[] }
  stats:     { totalBirths, totalDeaths, totalMigrations, migrationSystemMigrations,
               totalHarvested, depletionEvents, totalFoodEaten, totalHunted }
  config:       { ... 完整生效配置 ... }
  digest:       "<写档瞬间的状态摘要>"
  segments:     "world=..;tiles=..;agents=..;wildlife=..;buildings=..;stocks=..;storage=..;stats=.."
  configDigest: "<配置指纹>"
}
```

与早期文档的差异（**文档以实现为准**）：
不保存 `walkable/buildable`（地形派生值，读档时 `ApplyTerrainRules` 重算）、
不保存事件日志（只影响可读性，不影响演化）、
不保存 `UtilityBreakdown`（它是"解释"不是"状态"，下一次决策会重算 —— 恢复它反而会显示一个过期 tick 的解释）。

### 3.2.1 判据：什么必须进存档

判据只有一条：**"它会不会影响未来的行为？"** —— 而不是"它看起来像不像状态"。

这条判据是在 Phase 0 用**六次分叉**换来的。六次里有五次是"读档瞬间摘要完全一致，
续跑几步后分叉"，因为**状态摘要是量化的**（资源量量化到 0.01），
微小差异会被整除截断吞掉。逐条记录如下：

| # | 漏掉的字段 | 它是什么 | 不存的表现 |
|---|---|---|---|
| 1 | `ActionSystem.MoveProgress` | 小数步进度 | 第 2 tick 分叉，位置差一格 |
| 2 | `AgentStore.HasPathStep / PathStep` | 寻路下一步缓存 | 重新算路径，节奏错开 |
| 3 | `MigrationSystem.CooldownUntil` | 迁移冷却 | 全体立刻重新评估搬家 |
| 4 | `WildlifeStore` 存活列表的**顺序** | 系统按它遍历并消耗随机数 | **第 1 tick** 分叉（摘要按槽位升序算，顺序问题在摘要里看不见） |
| 5 | `ChunkGrid` 聚合统计 | **派生数据**，但 AI 读它（`WaterTiles`） | 第 31 tick 分叉 |
| 6 | `Tile.Resource.RegenerationRate` | **按格写死**的基础再生率 | 第 60 tick（第一个小时边界）分叉，3637/10000 格同时跑偏 |

第 6 条是最贵的一条，因为它同时具备两个"看起来不该存"的特征：
它像是配置的派生值（`resources` 段里确实有 `foodGrowthRate`），
而且读档路径**确实会重新生成地形**。但它**按格存在 Tile 里、读档时不会被重算**，
所以用不同 seed 构造目标世界时，新地形会把自己的再生率留在格子上，
制造出"种类是食物（r=0.03）、再生率却是木材的 0.02"这种自相矛盾的状态。

**两条防漏机制**（都已在测试里）：

1. `SaveCoversEveryTileField` —— 用**反射枚举** `Tile` 与 `ResourceNode` 的公开字段，
   要求每一个都有存档键、或被显式列为"派生值"。
   于是"新增字段却忘了存"直接测试失败，而不是在某个遥远的 tick 上表现为世界跑偏。
2. `TilesRoundTripBitExactUnderDifferentSeed` —— 读档测试**必须用一个不同的 seed** 构造目标世界。
   用相同 seed 时，地形生成会替存档兜住漏掉的按格字段，测试会假通过。
   *教训：验证"恢复了什么"的测试，必须让**输入**与**期望值**不同。*

### 3.3 为什么必须保存 RNG 状态

如果不保存 RNG 位置，读档后随机序列会从头开始（或从某个不一致的位置继续），
于是"读档后的世界"与"从未中断的世界"分叉。这会毁掉整个实验的可信度。

`DeterministicRandom.ExportState()` 返回 `s0, s1, s2, s3, drawCount`，
`ImportState` 恢复后必须能**精确续跑**。

测试：`RandomTests.StateRoundTripResumesExactly`、`RandomTests.SimRandomStateRoundTrip`。

### 3.4 为什么必须保存配置

玩家/开发者在运行中可能调整过参数（第 96.7 条要求关键数值可实时调整）。
如果读档时不恢复当时的配置，"读档后世界变得不一样"就无法归因。

因此存档里保存**完整生效配置**，并且报告目录里也会写一份 `config.effective.json`
（M0 已实现：每次 headless 运行都会写出）。

**但"存了"不等于"校验了"** —— 这是一个必须写清楚的边界：
`StateHash` **不包含配置**，所以"用一套不同的规则去读同一份存档"会
**通过摘要自校验**，然后跑出一个不同的世界。

这不是错误（"同一世界、换一套规则再跑"是正当的对照实验），
但**绝不能是静默的**。因此存档额外写入一个 `configDigest`（配置的 FNV 指纹），
读档时比对并在不一致时明确提示，例如：

```text
提示：本次读档使用的配置与存档时**不同**（存档 a1b2..，当前 c3d4..）。
      世界状态来自存档，但**演化规则来自当前配置** ——
      因此结果不会与存档时的实验一致。这是正当的对照实验，但请确认你是有意的。
```

测试：`SaveLoadTests.ConfigMismatchIsDetected`
（同时断言"配置变化**不影响**读档瞬间的摘要一致"，这正是需要单独指纹的原因）。

### 3.5 读档校验

```text
Load(saveFile):
  1. 解析并校验 formatVersion（不认识的版本 → 明确报错，不猜测）
  2. 恢复 config、seed、tiles、chunks、天气、日历、RNG
  3. 重建空间索引缓存（或恢复保存的缓存）
  4. 计算摘要，与存档里的 digest 比对
       一致 → 继续
       不一致 → 报错并指出"哪一类状态"不匹配（便于定位是哪个系统漏存了字段）
  5. 恢复事件日志与统计计数
```

**摘要比对是存档系统的核心保险**：它把"某个系统忘了保存字段"这种隐蔽 bug
变成"读档立刻报错"。

### 3.5.1 恢复顺序是硬约束

顺序敏感的地方写成清单，而不是散在代码里。实现里的顺序：

```text
Load(saveFile):
  1. 校验 version（不认识 → 明确报错，不猜测、不降级）
  2. 校验 width/height 与当前世界一致
  3. 恢复 tiles → weather → seed → tick        // 地形必须先于建筑与空间索引
  4. 导入全部 8 条 RNG 流                       // 必须早于任何实体恢复
  5. 恢复实体：agents（含迁移冷却、小数步进度、寻路缓存）
                → wildlife（含存活顺序）→ buildings → storage → groundStocks
  6. 恢复统计计数（含 SimulationStats，它进摘要）
  7. RefreshSpatialIndex + NotifyAfterLoad
       ⚠ NotifyAfterLoad **不得**重算被显式恢复过的字段
         （早期版本在这里重新均分决策相位，把恢复出来的相位又打乱了一次）
  8. **最后**恢复 chunk 聚合统计与脏标记      // 必须晚于所有 RefreshSpatialIndex
  9. 计算 digest / segments / configDigest 并与存档比对
```

**三重校验各管一件事**：

* `digest` 把"漏了字段"从"几万 tick 后的神秘分叉"变成"读档立刻报错"；
* `segments` 把"一个大数字对不上"缩小到"是动物那一段"；
* `configDigest` 覆盖摘要的盲区（配置）。

**同时要记住 `digest` 的局限**：资源量量化到 0.01，
所以**小于 0.01 的差异在摘要里看不见**。这就是为什么
`TilesRoundTripBitExactUnderDifferentSeed` 必须做**逐位**比对 ——
六次分叉里有五次都是被量化藏起来、到后面才放大的。

### 3.6 版本演进策略

| 情形 | 处理 |
|---|---|
| 新增字段 | 提升 `version`。**当前策略是严格拒绝旧版本**（见下） |
| 字段语义变化 | 提升 `version`，并在读档路径里写**显式迁移** |
| 新增 `RngStream` | 只能在枚举**末尾追加**（`SimRandom.StreamCount` 由枚举长度算出） |

**当前策略是"严格拒绝"而不是"静默补默认值"**，理由是：
静默补默认值会让"确定性验收"变成假通过 —— 载入一个缺字段的档案，
之后发现演化分叉，却不知道该怀疑格式还是模拟。
宁可让玩家看到"版本不匹配，请用旧版本或重新生成"，也不要让他拿到一个悄悄不同的世界。

> **M9 的待办**：docs/12 的 M9 要求"存档版本化与向后兼容策略"。
> 计划是把 `vN → vN+1` 的升级写成**显式的 `SaveMigration` 步骤**（每步一条迁移 + 一条测试），
> 未知或更高的版本仍然明确拒绝。这样"向后兼容"是可验证的，而不是靠祈祷。

---

### 3.7 随机流的"归因隔离"原则（M5 新增第 9 条流）

随机流按**归因边界**划分，而不是按"谁调用方便"。目前共 9 条：

| 流 | 用途 | 为什么必须独立 |
|---|---|---|
| `WorldGen` | 地形生成 | 与运行时完全隔离，保证"同 seed 同地图" |
| `Weather` | 天气转移 | 天气是跨系统传播的上游 |
| `Agents` | 个体决策 / 性格 / 遗传 | 任何扰动都会改变所有人的决策序列 |
| `Events` | 世界自发事件（灾害、动物） | 与决策隔离 |
| `Combat` | 战斗 | 与决策隔离（M8） |
| `Misc` / `Reserve` | 杂项与预留 | — |
| `Intervention` | **玩家干预** | 见下 |
| `Fire` | **火灾（M5）** | 见下 |

**判据：如果一个新机制会让"我没做的事"发生变化，它就必须有自己的流。**

两次实测都是这条判据的代价：

1. **Phase 0 的干预流**：干预原先借用 `Events`，于是"多撒几只动物"
   会改变接下来几天的天气 —— 玩家改了一个条件，却扰动了一堆无关的东西。
2. **M5 的火灾流**：火灾原先也借用 `Events`，而天气在用它。
   结果是"点燃一片森林"改变了天气序列，对照实验里两组世界连天气都不同，
   **因果无法归因**。症状极具迷惑性：实验组的床位反而**多于**对照组（24 vs 20），
   看起来像"火没起作用"，实际是信号被天气扰动淹没了。

独立成流之后的契约：

> **不干预 ⇒ 与之前版本逐 tick 完全一致；干预 ⇒ 只有被直接改动的状态变化，
> 其它随机序列逐位相同。**

一个附带的好性质：新增流**不会**扰动已有的流
（每条流由 `(seed, 流序号)` 独立播种），所以 M5 加 `Fire` 之后，
`--digest --seed 4242 --days 30` 在"没有发生火灾"的窗口里仍然是原来的值 ——
这本身就是"新机制没有静默改变旧行为"的一个强证据。

> **新增流只能在枚举末尾追加。** 插在中间会让后面所有流的序号平移，
> 于是所有历史存档与基准摘要全部失效 —— 而且**没有任何报错**。
> 同理，`SimRandom.StreamCount` 必须**由枚举长度算出**，不能写死常数。

---

## 4. 相关测试清单

| 测试 | 覆盖 |
|---|---|
| `RandomTests.SameSeedSameSequence` | 随机源本身可复现 |
| `RandomTests.StateRoundTripResumesExactly` | 单流状态往返 |
| `RandomTests.SimRandomStateRoundTrip` | 多流状态往返 |
| `RandomTests.DrawingFromOneStreamDoesNotDisturbOthers` | 分流隔离 |
| `WorldTests.GenerationIsDeterministic` | 世界生成逐格一致 |
| `WorldTests.GenerationDiffersBySeed` | 种子真的生效 |
| `WorldTests.StateDigestIsReproducible` | 同 seed 两跑摘要一致 |
| `WorldTests.StateDigestDiffersBySeed` | 不同 seed 摘要不同 |
| `WorldTests.InvariantsHold` | 不变量（NaN / 越界 / 负值） |
| `ConfigTests.CloneIsDeepCopy` | 配置克隆不会串改原对象 |
| `--digest` 端到端 | 上述约束在真实运行路径上成立 |
| `SaveLoadTests.SaveLoadRoundTripPreservesDigest` | 存档→读档→续跑 与 直接跑 逐 tick 一致 |
| `SaveLoadTests.TilesRoundTripBitExactUnderDifferentSeed` | **逐位**往返，且**用不同 seed** 构造目标世界（防"地形生成替存档兜底"） |
| `SaveLoadTests.SaveCoversEveryTileField` | 反射枚举 `Tile`/`ResourceNode` 字段，防将来再漏 |
| `SaveLoadTests.InvisibleButBehaviouralStateIsPersisted` | 前五个"隐形状态"逐个锁定 |
| `SaveLoadTests.ConfigMismatchIsDetected` | 配置指纹覆盖摘要的盲区 |
| `SaveLoadTests.WrongVersionIsRejected` | 版本不匹配明确拒绝（且测试自身不会因版本提升而失效） |
| `SaveDivergenceProbe`（`SBOX_SIM_PROBE=1`） | 诊断工具：分段 + 逐格 + 随机流三重对比，定位分叉点 |
| `--batch` 端到端 | 多 seed 无异常、无状态爆炸 |

---

## 5. 不变量（`Simulation.ValidateInvariants`）

每 64 tick 检查一次（可配置关闭）。任何一条失败都立刻抛异常并给出**位置与数值**，
因为数值崩坏一旦扩散到历史统计里就很难回溯。

| 不变量 | 判据 |
|---|---|
| 有限性 | `Moisture` / `Fertility` / `Temperature` / `Resource.Amount` 都不是 NaN/Infinity |
| 湿度范围 | `Moisture ∈ [0,1]` |
| 资源非负 | `Resource.Amount ≥ 0` |
| 资源不超容 | `Resource.Amount ≤ Capacity + 1e-3` |
| 建筑索引合法 | `BuildingId ≥ -1` |
| 通行/可建造一致 | 与 `TerrainInfo` 一致（显式覆盖除外） |

**为什么用"抛异常"而不是"打日志继续跑"**：模拟游戏的数值崩坏具有传染性 ——
一个 NaN 会在几 tick 内污染整个地图与统计，之后再查已经没有任何线索。
当场抛出，堆栈就是第一现场。
