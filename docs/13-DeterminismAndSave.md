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

### 3.2 内容清单

```text
SaveFile {
  formatVersion: 1
  world: { seed, width, height, revision }
  time: { tick }
  weather: { kind, durationHours, hoursUntilChange, accumulatedRain, droughtHours }
  rng: { 每条流的 5 个 ulong }             // s0,s1,s2,s3,drawCount
  config: { ... 完整 SimConfig ... }        // 读档必须复用当时的参数
  chunks: { fields[][], cellCounts[], dirty[] }
  tiles: [ 每格一行：terrain fire buildingId fertility moisture temperature
                     vegetation resKind resAmount resCapacity resRate walkable buildable ]
  entities: { ... 由各实体系统实现读写 ... }   // M1 起：agents；M3 起：buildings
  events: [ 最近 N 条 ]                     // 用于读档后仍能看到最近历史
  counters: { depletionEvents, births, deaths, migrations }
  digest: "<读档时校验用的摘要>"
}
```

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

### 3.6 版本演进策略

| 情形 | 处理 |
|---|---|
| 新增字段 | 读旧档时字段缺失 → 用默认值；写新档时带上 `formatVersion+1` |
| 字段语义变化 | 提升 `formatVersion`，并在读档路径里写**显式迁移**（不允许静默猜测） |
| 删除字段 | 读档时忽略并记 warning |
| 新增 `RngStream` | 旧档只有 7 条流 → 缺失的流用 `Reseed(Mix(seed, index))` 派生，保证可续跑 |

**向后兼容的底线**：只要 `formatVersion` 能被识别，读档就必须成功或给出明确原因。
不允许出现"读档后世界能跑但结果不对"的静默错误。

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
