# 12 — Milestones

本文件是**唯一的开发进度真相**。每个里程碑必须满足两个条件才算完成：

1. **可运行**：能启动、能观察、能操作（第 89 条：不允许"开发三个月后才出现第一个可玩版本"）；
2. **有验收**：验收项可机测或可肉眼复核，且写在本文件里。

---

## 0. 构建通道（所有里程碑共用）

本仓库刻意支持两条构建通道，让"能不能跑"不取决于开发机装了什么：

| 通道 | 前置条件 | 命令 | 说明 |
|---|---|---|---|
| **A（首选）** | .NET 8 SDK | `.\tools\build.ps1 -Mode build` | 自动探测 SDK（参数 → 环境变量 → 默认目录 → PATH） |
| **B（降级）** | .NET 8 运行时 + Roslyn `csc` | `.\tools\build.ps1 -Channel csc` | 直接用 `csc` 编译 BCL-only 源码；引用 assembly 从 targeting pack 取，取不到则从 `shared/Microsoft.NETCore.App/8.x` 过滤出托管 DLL |

**为什么不用 NuGet**：一旦引入任何包（哪怕是 xUnit），通道 B 立刻失效，
"没有 SDK 也能构建"这个承诺就没了。因此：

- 测试用自带的反射运行器（`Tests/Framework` + `Tests/TestRunner.cs`）；
- JSON 用手写的解析器/绑定器（`Foundation/JsonParser.cs`、`JsonBinder.cs`）；
- PNG/zlib 用手写的编码器（`Console/Png`）。

**何时允许引入 NuGet**：仅当出现"自己写明显不划算"的需求（例如需要成熟数学库或
高性能并行框架），且必须同时保留通道 B（用条件编译或替换实现）。

---

## M0 — 世界、时间、观察器（✅ 已完成）

### 目标

让"世界存在、时间流动、玩家能看见"这三件事成立，并把后续所有里程碑要依赖的地基
（确定性、空间索引、Tick 管线、观察工具、测试基建）一次性打牢。

### 交付内容

| 模块 | 文件 | 说明 |
|---|---|---|
| 随机源 | `Foundation/DeterministicRandom.cs` | xoshiro256\*\* + 7 条分流 + 状态导出/导入 |
| 噪声 | `Foundation/PerlinNoise.cs` | Perlin + fBm，置换表由 seed 确定性生成 |
| 哈希 | `Foundation/Hash64.cs`、`NoiseHash.cs` | FNV-1a64（状态摘要）、坐标散列（视觉扰动） |
| 数学与曲线 | `Foundation/SimMath.cs`、`UtilityCurve.cs` | 含 8 种效用曲线 |
| JSON 与配置 | `Foundation/JsonValue.cs`、`JsonParser.cs`、`JsonBinder.cs`、`SimConfig.cs` | 容忍注释、带行列号报错、未知键产生警告 |
| 地形规则 | `World/TerrainKind.cs` | 唯一的地形规则表 |
| 资源模型 | `World/ResourceKind.cs` | `ResourceNode`（Logistic 再生）+ `ResourceStock` |
| 世界生成 | `World/WorldGenerator.cs` | 分位数地形分类、临水湿度、资源分布 |
| 空间索引 | `World/ChunkGrid.cs` | 16×16 chunk、16 个统计量、脏标记惰性重算 |
| 世界容器 | `World/World.cs` | 唯一 Tile 写入口、越界安全读取 |
| 天气 | `World/Weather.cs` | 状态驱动转移、写回 Tile 湿度/温度 |
| 日历 | `World/Calendar.cs` | 唯一时钟、昼夜光照、`CompletedDay` |
| 资源系统 | `ResourceSystem.cs` | 再生、采集结算、注入、枯竭统计 |
| 事件日志 | `History/EventLog.cs` | 环形缓冲、按实体检索、重要度分级 |
| 统计与摘要 | `SimulationStats.cs` | 逐日样本、饥荒判定、`StateHash` |
| Tick 管线 | `Simulation.cs` | 顺序固定、分批钩子、玩家干预接口、不变量校验 |
| TUI | `Console/Tui/*`、`Render/*`、`Ui/*`、`GameLoop.cs` | 半方块像素渲染、相机、9 种热力图、状态栏与检查器 |
| PNG | `Console/Png/*` | 手写 Deflate/Inflater + PNG 编解码 + 世界快照 |
| 报告 | `Console/Reporting/RunReport.cs` | `stats.json` / `report.md` / `events.md` / 摘要 |
| CLI | `Console/Program.cs`、`Cli/Args.cs` | play / headless / digest / batch / snapshot |
| 测试 | `Tests/*` | 97 个用例，自研反射运行器 |
| 脚本 | `tools/*.ps1` | build / run / test / dev / install-sdk |
| 文档 | `docs/00–04, 12, 13`、`README.md` | 愿景、架构、Tick、数据结构、里程碑、确定性 |

### 验收（全部已通过）

| # | 验收项 | 判据 | 状态 |
|---|---|---|---|
| 1 | 测试全绿 | `.\tools\test.ps1` → 97/97 通过 | ✅ |
| 2 | 同 seed 可复现 | `--digest` 两次摘要完全一致 | ✅ |
| 3 | 种子真的生效 | 换 seed 摘要必须不同 | ✅ |
| 4 | 多 seed 鲁棒 | `--batch --seeds 1..20` 无崩溃/无 NaN | ✅ |
| 5 | 地形完整 | 每个 seed 都必须有 水/山/林/草 | ✅ |
| 6 | 空间索引正确 | 逐块与逐格真值比对一致 | ✅ |
| 7 | 世界可观察 | TUI 可平移缩放、切 9 种热力图、看格详情 | ✅ |
| 8 | 快照可归档 | `--snapshot` 产出 PNG，且与 TUI 共用配色 | ✅ |

### M0 明确*没有*做的事（留给后续）

- 没有任何 Agent（人口恒为 0）；
- 没有资源采集者（资源只会再生与枯竭，没人消耗）；
- 没有火灾、没有建造、没有聚落、没有存档。

这些不是遗漏，而是**刻意的顺序**：先把"世界的物理规则 + 可复现 + 可观察"做对，
再往上加"活着的东西"。反过来的话，一旦人口与行为进来，任何地基性改动都会
让已经跑出来的世界历史全部失效，调试成本会指数上升。

### 踩坑记录（供后续里程碑参考）

| 现象 | 根因 | 修法 |
|---|---|---|
| 小地图上完全没有水域和山地 | fBm 值聚集在 0.5，固定阈值永不命中 | 改成分位数阈值（`WaterLevel`/`MountainLevel` 语义变为"目标占比"） |
| 报告里日期虚报一天 | 日边界上 `Calendar.Day` 已翻页 | 新增 `Calendar.CompletedDay`，日统计用它 |
| 快照文件名与序列错位 | 用"当前 tick"命名，而批量推进让实际 tick 略微超过目标 | `WriteSnapshot` 接受**目标 tick** |
| 断言自身制造失败 | 保留 `ref readonly Tile` 后读值，注入后读到的是新值 | 断言前先把值取成 `float` |
| 数组元素读成 null | `json[0]` 被解析成字符串索引器 | 必须写 `json.Items[i]` |
| 逆 Logistic 曲线方向反了 | 写成 `1 − L(x)`（单调递减） | 改为 `1 − L(1−x)`（单调递增），并加单调性测试 |
| 类型名与命名空间同名 | `namespace SandBoxSim.Core.World` + `class World` | 命名空间改为 `Core.Environment`（目录名仍保留 `World`） |
| PowerShell 脚本报语法错误 | 中文注释 + UTF-8 无 BOM，Windows PowerShell 5.1 按 ANSI 读 | `.ps1` 统一带 BOM；`tools/*.ps1` 输出信息用英文 |
| 构建脚本用三元运算符失败 | PS 5.1 不支持 `? :` | 改显式 `if/else` |

---

## M1 — Agent、寻路与 Utility AI（🚧 进行中）

### 目标

让地图上出现**会自己决定做什么**的个体，并让玩家能看清"他为什么这么决定"。

### 交付内容

| 模块 | 内容 |
|---|---|
| `Agents/AgentStore.cs` | SoA（结构体数组）存储：位置、需求、库存、年龄、状态；对象池 |
| `Agents/Needs.cs` | `Hunger` / `Energy` / `Health` 的累积与恢复，全部归一化到 [0,1] |
| `Agents/Inventory.cs` | 个人库存（复用 `ResourceStock`） |
| `Ai/Consideration.cs` | 一条考虑项：输入取值函数 + `UtilityCurve` + 权重 |
| `Ai/UtilityScorer.cs` | 加权几何均值 + 补偿因子；平局用固定优先级 + 坐标序 |
| `Ai/UtilityBreakdown.cs` | **可解释性数据结构**：每项的名字/输入/曲线/权重/贡献 |
| `Ai/AiSystem.cs` | 分批决策循环（`phase = id % batchCount`），保存 Top-N 打分 |
| `Actions/ActionKind.cs` | `Eat / Sleep / GatherFood / GatherWood / GatherStone / BuildHouse / BuildFarm / Deposit / Migrate / Wander` |
| `Actions/ActionPipeline.cs` | 统一执行结构：Condition → TargetSelection → Movement → Execution → Result |
| `Pathing/AStar.cs` | A\*（二叉堆、八方向、对角代价、确定性平局） |
| `Pathing/PathCache.cs` | 路径缓存 + 失败记忆（避免每 tick 重算不可达目标） |
| `Pathing/Movement.cs` | 沿路径推进、天气移动系数 |
| `Settlements/*` | （仅占位）个体归属字段，M7 才成为实体 |
| Console | 点击/光标选人、Agent 检查器（需求条、当前动作、**效用分解表**、路径可视化） |
| Console | 新热力图：AI 状态、人员分布 |
| `docs/05-UtilityAI.md` | 完整效用设计文档（公式、考虑项、曲线、选靶、执行、解释输出） |
| `docs/14-Performance.md` | 分批与空间索引的实测数据 |

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | 100 个 Agent 各自寻路移动 | 同屏可见、无穿墙、无卡死 |
| 2 | 寻路正确性 | 绕水测试、不可达返回失败、代价随地形变化 |
| 3 | 确定性不破 | `--digest` 在有 Agent 的情况下依然两次一致 |
| 4 | 分批有效 | 单 tick 决策数有上界；1000 Agent 时帧率不低于 30 FPS |
| 5 | 可解释 | 检查器能显示"为什么选 Eat 而不是 Sleep"的完整分解 |
| 6 | 效用单调性 | 需求越高、对应动作效用必须越高（测试锁定） |
| 7 | 性能基线 | 200 Agent、10 万 tick 的耗时记录进 `docs/14` |

### M1 明确先不做的

- 不做行为树（Utility AI 足够，且更可解释）；
- 不做 agent 之间的通信/传话；
- 不做记忆与传闻系统（先用 `SearchRadius` 限制视野，第 19 条）；
- 不做 ECS 框架（SoA + 系统类已足够，避免框架成本）。

---

## M2 — 生存：需求、采集、进食、死亡

### 目标

让"活着"本身成为一件需要努力的事：人必须找吃的、喝水、睡觉，否则会死，
而且玩家能看出他是怎么死的。

### 交付内容

- 需求系统完整化：`Hunger` 累积与进食、`Energy` 累积与睡眠、`Health` 与饥饿/干渴衰减；
- 资源采集动作落地：`GatherFood`（采集野生食物）、`Drink`（临水格）；
- `Eat` 动作与个人库存消耗；
- 死亡系统（第一阶段只做 **饥饿** 与 **年龄**），写 `CauseOfDeath` 并记事件；
- 事件日志接入人物事件（出生/进食/饥饿/死亡）；
- `docs/06-ResourceModel.md`（采集/消耗/再生/存储/枯竭的完整公式）；
- 死亡与饥荒热力图、`docs/09-EmergentStories.md` 起头（≥6 个故事）。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | 无人干预 100 天不崩 | 人口曲线合理（不会瞬间清零，也不会无限增长） |
| 2 | 有人能活下来 | 存在存活超过 60 天的个体 |
| 3 | 有人会饿死 | 在食物稀缺场景下出现饥饿死亡，且死因正确 |
| 4 | 采集影响环境 | 局部食物存量显著下降（热力图与统计可证） |
| 5 | 事件可读 | `events.md` 中有出生/死亡/饥荒记录，含死因与地点 |

---

## M3 — 木材、石材与建造

### 目标

让"攒资源 → 盖房子 → 住进去"这条链条自己跑起来。

### 交付内容

- `Wood` / `Stone` / `Iron` 采集动作；
- `Buildings/Building.cs`、`BuildingSystem.cs`：House（Wood 20）、Storage（Wood 40 + Stone 10）；
- 建造流程：选点（需可建造格、附近无冲突）→ 扣料 → 施工进度（FastTick，10 tick）→ 完成；
- `Work` 需求的引入：住房缺口与资源缺口驱动职业选择（`JobType` 四类）；
- `BuildingSystem` 写入 `Tile.BuildingId`、占用格；
- 事件：`BuildingStarted` / `BuildingCompleted` / `BuildingDestroyed`；
- `docs/07-Buildings.md`。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | 伐木可观察 | 森林格木材存量下降、植被下降、颜色变黄褐 |
| 2 | 房屋会自己出现 | 100 天内有房屋建成事件 |
| 3 | 森林会枯竭 | 高强度采集下出现 `DepletionEvents > 0` |
| 4 | 建造受资源约束 | 木材不足时建造效用明显下降，且不扣料 |
| 5 | 建造位置合理 | 房屋不建在水里/农田上（测试锁定） |

---

## M4 — 农业、人口与迁移雏形

### 目标

出现**反馈环**：人口增长 → 食物压力 → 开垦农田 → 森林减少 → 资源枯竭 → 迁出。

### 交付内容

- Farm 建造（需 Grass + 肥沃度阈值），开垦后 `Tile.Terrain = Farmland`；
- 农业产量公式：`BaseYield × Fertility × MoistureFactor × WeatherFactor`；
- 聚落共享存储（`Storage` 建筑 + `Settlement.Storage`）；
- `Deposit` 动作（把个人库存存入共享库存）；
- 出生系统：`P_birth = P_base × FoodFactor × HousingFactor × HealthFactor`，需满足
  `Food > 阈值 ∧ 空床位 > 0 ∧ Population ≥ 2`；
- 人口压力与迁移雏形（`U_migrate > 阈值` 时离开）；第一版可以只是"离开并消失"，
  真正的新聚落建立留给 M7；
- 统计曲线完整（人口/食物/木材/农田/紧张度），`docs/08-PopulationModel.md`。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | 农田出现 | 100 天内有 Farmland 格 |
| 2 | 人口真的增长 | 人口曲线出现上升段，且与食物供给正相关 |
| 3 | 出现负反馈 | 出现至少一次"食物下降 → 人口下降或迁出"的片段 |
| 4 | 出生条件严格 | 无房/无粮时出生数为 0（测试锁定） |
| 5 | 迁移发生 | 100 天内出现迁出事件 |

---

## M5 — 玩家工具、灾害与存档

### 目标

让玩家从"观察者"变成"实验者"：能改条件、能造灾害、能保存实验、能导出证据。

### 交付内容

- 上帝工具面板：创造（人/动物/森林/资源）、地形（抬升/下沉/河流/山脉/水域）、
  资源（四种注入）、恩惠（肥沃度/出生率/产量/治愈）、灾害（火/闪电/洪水/干旱/瘟疫/陨石）；
- 规则开关：`NoDeath` / `HighBirthRate` / `FastAging` / `DoubleResource` / `PeaceMode`；
- **火灾系统**：`P_fire = Base × Dryness × TemperatureFactor × VegetationFactor`，
  按邻居（植被/湿度/风向）传播，烧过后进入 `Burnt`（植被清零、恢复极慢）；
- 天气灾害（洪水/干旱）与作物减产联动；
- 存档/读档：`Seed + RNG 流状态 + 全量状态`，读档后可精确续跑；
- Debug Overlay 全套（含寻路视图与 AI 状态）；
- 报告升级：自动产出快照 PNG 序列 + 事件时间线；
- `docs/10-DebugAndObservation.md`、`docs/11-MVP-Scope.md`、`docs/15-ConfigReference.md`。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | **玩家烧森林 → 人口增速下降** | 对照组 vs 实验组的 100 天人口曲线可测差异（第 68 / 94 条的最小证明） |
| 2 | 火灾会自己蔓延 | 一次点燃后火势扩张并最终熄灭，留下焦土 |
| 3 | 灾害有后果 | 干旱期间作物产量与食物存量下降 |
| 4 | 存档往返一致 | `Save → Load → 同 tick` 的摘要与直接跑完全一致 |
| 5 | 规则开关生效 | 开启 `NoDeath` 后 100 天死亡数为 0 |

---

## M6 — 性格、关系与家庭

### 目标

让"一个模拟单位"变成"玩家认识的人"。

### 交付内容

- `Personality`（六维 [0,1]：`aggression / greed / kindness / bravery / industriousness / sociability`），
  出生时抽样、可遗传（带突变）；
- 性格进入效用权重（第 33 条）：工作/分享/攻击/迁移/社交;
- `Relationship`（双向 [-100,100]）与变化规则（帮助 +10、分享 +5、偷窃 −20、攻击 −40、共同战斗 +15）；
- 家庭：伴侣、父母、子女、兄弟姐妹；婚配与生育（孩子带父母关系）；
- 生命阶段（儿童 / 成年 / 老年）与能力差异；
- 新动作：`Socialize` / `ShareFood` / `Flee` / `Attack` / `Marry`；
- **个人时间线**（点击 NPC 看到一生流水账，第 57 条）与聚落历史（第 58 条）；
- 事件日志扩充（关系、婚配、出生、死亡、领袖）。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | 性格真的改变行为 | 同一场景下高侵略性 vs 低侵略性个体行为分布显著不同（统计测试） |
| 2 | 关系是对称且有历史的 | `Rel(A,B) == Rel(B,A)`；关系变化都有事件记录 |
| 3 | 家庭结构自洽 | 任何个体的父母/子女关系互相对应（测试锁定） |
| 4 | 时间线可读 | 点击 NPC 能看到"出生 → 成为农民 → 建房 → 丧偶 → 迁移" |
| 5 | 死亡有情绪价值 | 死亡事件包含年龄、死因、家庭关系 |

---

## M7 — 聚落、领土与迁移

### 目标

让"一群人长期一起生活"自然形成具名村庄，并让村庄之间出现人口流动。

### 交付内容

- `Settlement` 正式实体：名称生成、中心、成员、建筑、共享库存、领土、规模档、历史；
- **形成条件不是"人数阈值"**（第 29 条），而是持续满足：
  长期共居 + 共享 Storage + 共享建筑 + 稳定住所；
- 规模档：`Camp → Village → Town → City`，由人口/建筑数/产出自动判定；
- 领土：由建筑与成员活动范围生成（简化版：占据格集合 + 边界扩张）；
- **迁移完整化**：`U_migrate = FoodScarcity + HousingScarcity + Danger + PopulationPressure
  + NearbyOpportunity − HomeAttachment`，迁出后**加入既有聚落或新建聚落**；
- 聚落历史与聚落统计（人口/产出/建筑时间线）；
- `docs/09-EmergentStories.md` 补到 ≥12 个故事。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | 自发形成具名村庄 | 20 个 seed 中 ≥15 个在 150 天内形成 ≥1 个具名聚落 |
| 2 | 多聚落并存 | ≥5 个 seed 在 200 天内出现 ≥2 个聚落 |
| 3 | 迁移可归因 | 迁移事件必须带主因（食物/住房/危险/机会） |
| 4 | 聚落会衰亡 | 出现至少一次"聚落人口下降/被遗弃"的完整过程 |
| 5 | 聚落历史可读 | 每个聚落有"建立 → 人口峰值 → 首次饥荒 → 迁出"的时间线 |

---

## M8 — 贸易、外交与冲突

### 目标

让文明之间的互动（贸易与战争）从资源与关系里自己长出来。

### 交付内容

- 聚落间贸易：供求价格 `Price = Base × ((Demand+ε)/(Supply+ε))^α`、资源互补交易、贸易路线；
- 职业受价格影响（价格 → 收益 → 职业选择 → 供给 → 价格 的负反馈，第 49 条）；
- 聚落外交关系值，贸易提升、冲突降低；
- `WarPressure = ResourceConflict + TerritoryConflict + Aggression + HistoricalHostility
  − TradeBenefit − Relationship`；超过阈值时冲突概率显著提高；
- 战斗结算与伤亡、停战、结盟；
- Leader 产生（影响力 = 关系 + 贡献 + 性格）；
- 文明组合（多个聚落 → `Civilization`，带 `militarism / expansionism / tradePreference /
  technologicalFocus / isolationism`）；
- `docs/09` 补全"文明兴衰"类故事。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | 贸易自发发生 | 互补资源的两个聚落在 200 天内出现贸易事件 |
| 2 | 价格负反馈成立 | 缺口越大价格越高；供给增加后价格回落（测试锁定） |
| 3 | 战争不是随机的 | 战争必须由 `WarPressure` 超阈值触发，且事件里带压力分解 |
| 4 | 战争有后果 | 战后双方人口/资源/关系均可测下降 |
| 5 | 长跑不崩 | 500 天、多文明场景无异常、无状态爆炸 |

---

## M9 — 打磨与交付

### 目标

把项目变成"别人 clone 下来就能跑、能读懂、能继续做"的东西。

### 交付内容

- 性能优化（以 `docs/14` 的实测数据为准，不做无基线优化）；
- 存档版本化与向后兼容策略；
- 文档收尾：补齐 05–11、14、15；
- CI（GitHub Actions）：构建 + 测试 + 100 天 headless 冒烟 + 摘要比对 + 上传报告与快照；
- README 收尾（截图、快速上手、常见问题）；
- 发布 `v1.0.0` tag + Release Notes。

### 验收

| # | 验收项 | 判据 |
|---|---|---|
| 1 | CI 绿 | 每次推送自动构建、测试、长跑冒烟 |
| 2 | 性能达标 | 100 天 / 200 Agent headless < 10 秒；300 Agent TUI ≥ 30 FPS |
| 3 | 文档完整 | 文档清单全部 ✅，且与代码一致 |
| 4 | 新机器可跑 | 干净环境（仅 .NET 8 SDK）按 README 一次成功 |
| 5 | 终极判据 | 玩家什么都不做，30 分钟内出现值得观察的变化 |

---

## 优先级原则（防止 Feature Creep）

任何新功能进入计划前必须回答三个问题（第 90 条）：

1. 它**增加涌现性**吗？（会让系统之间产生新的相互作用链吗）
2. 它**增加玩家能动性**吗？（多一个"条件旋钮"而不是"结果按钮"吗）
3. 它**增加可观察性**吗？（玩家能更容易理解发生了什么吗）

三个都答不上来 → 不做。见 [11-MVP-Scope](11-MVP-Scope.md) 的禁项清单。

**第一阶段的禁项**（第 72 条）：完整科技树、几十种资源、几十种职业、复杂装备系统、
复杂战斗系统、外交系统、政治系统、宗教系统、完整金融经济、超大地图、多人联机、
3D 高质量美术、复杂任务系统、完整剧情系统。

理由不是"做不了"，而是：**真正需要先验证的是"10~30 个 NPC 在地图上自己生活是否有趣"。
如果这个核心没有乐趣，更多内容不会让游戏变好。**
