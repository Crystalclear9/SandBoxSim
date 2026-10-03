# Changelog

本项目的版本历史。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [Unreleased]

### 计划中（M2）

- 生存经济：共享物资（堆料点）、资源枯竭的连锁反应、第一次被迫迁徙
- 动物雏形（植被 → 猎物 → 猎人产出）
- `docs/06-ResourceModel.md`、`docs/08-PopulationModel.md`、`docs/09-EmergentStories.md`

---

## [0.2.0] — M1：Agent、需求、Utility AI 与寻路

这一版让地图上出现**会自己决定做什么、并且能自己活下来**的个体。

它没有停在"会走路的小人"上：M1 让居民自己找吃的、喝水、睡觉、砍柴采石，
并且会老去、会饿死、会脱水而死 —— 死因会写进事件日志。
换句话说，M1 已经能跑出"一群人的一生"，虽然他们还没有家庭、没有村庄。

### Added — 个体与需求

- `Agents/AgentTypes.cs`：`AgentRef`（槽位 + 代次，防止"看错人"）、`AgentState`、
  `ActionKind`（26 种，M1 实装 8 种）、`ActionPhase`、`JobType`、`LifeStage`、
  `Personality`（六维 + 抽样 + 遗传接口）、`NeedIndex`、`DeathCause`、`ActionFailReason`
- `Agents/AgentStore.cs`：SoA 存储、槽位复用 + 代次校验、确定性名字生成、
  完整访问器、统计聚合（饥饿人数/移动人数/职业分布）、`HashInto`、按需扩容
- `Systems/NeedsSystem.cs`：饥饿/疲劳/干渴/社交累积、睡眠反解疲劳、
  饥饿与脱水掉血、每日年龄与老年死亡（含硬性寿命上限）、`DeathRecord` 明细

### Added — Utility AI

- `Ai/UtilityBreakdown.cs`：`Consideration`（名字/输入/曲线/权重/分数/贡献）、
  `ActionScore`、`UtilityBreakdown`；`UtilityCombiner` 用"加权算术 × 加权几何"折中
  （避免纯算术的"硬做"与纯几何的僵硬），惩罚项规范化后仍可参与几何均值
- `Systems/ScoreBuilder.cs`：效用打分的流式构造器
- `Systems/ActionDef.cs`：`ActionEvaluator` / `TargetSelector` 自定义委托
  （`Func<in T,R>` 不支持变体修饰符，会编译失败）、`ActionDef`、`ActionRegistry`（带缓存）
- `Systems/AiSystem.cs`：自适应分批决策、候选回退（最优动作无目标则试次优）、
  Top-N 打分保存、**生存需求可打断决策冷却**、按动作统计选择/评估次数
- 8 个动作（数据驱动，新增动作不需改决策循环）：
  `Eat` / `Drink` / `Sleep` / `GatherFood` / `GatherWood` / `GatherStone` / `Explore` / `Wander`
- `Foundation/UtilityCurve` 新增 **`Survival` 曲线**（`1−(1−x)²`）：让生存类动作在低需求区
  就拿到高分，从而压过带补偿因子的下界

### Added — 寻路与执行

- `Pathing/AStarPathfinder.cs`：八方向 A\*、二叉堆、octile 启发、
  **禁止斜穿两块不可走地形**、扩展节点上限（4000）、确定性平局、复用缓冲、诊断计数
- `Systems/ActionSystem.cs`：逐格重算下一跳的移动（不抱过期路径）、
  进食/饮水/睡眠/采集结算、耐心上限、完成与失败统计
- `Systems/Actions/ActionSearch.cs`：**两级资源搜索**（chunk 聚合 → 精确扫描），
  搜索范围更大（约 48×48 格）而扫描量下降一个数量级

### Added — 表现层与工具

- `--agents` / `--agent-radius` 参数；按 chunk 评分选择宜居落脚点（而不是地图中心）
- 复现命令与报告自动带上放置人数（否则别人跑出来是空世界）
- CI 升级：确定性校验、100 天长跑、多 seed 批量全部带上个体

### Added — 文档

- `docs/05-UtilityAI.md`：考虑项、曲线语义、合成公式推导、8 个动作的完整公式、
  选靶与全知限制、执行管线、分批决策、可解释输出、测试契约
- `docs/14-Performance.md`：性能预算与实测、分批决策上界表、空间索引三级对比、
  内存与 GC 取舍、**未做的优化及理由**、比 tick/秒更重要的可观测指标
- README / 02 系统架构 / 03 Tick 架构 / 12 里程碑同步更新

### Fixed（M1 联调过程中发现并修掉的问题）

- **40 人两天内全部饿死，且食物采集量为 0**：木材/石料的可得性权重压过了食物采集的饥饿项。
  提高 `GatherFoodHungerWeight` 到 2.5，并明确"生存类动作必须在效用尺度上说过非生存类动作"
  属于**正确性**而非平衡
- **采集与进食统计都在涨、人还是在死**：饥饿只能在 600 tick 一次的决策窗口里被处理，
  一天最多吃两顿，物理上不可行。新增"生存需求打断决策冷却"（阈值 0.7）
- **"有点饿时不想采食物"**：效用合成器带补偿因子 ⇒ 被门挡住的动效停留在下界上。
  新增 `Survival` 曲线解决
- **口粮账不平衡**（每人每天需约 11 食物，实际只采约 9）：食物容量 24→60/格、每次采集 5→24
- **`GatherWood/GatherStone` 的"需求"符号反了**（`1 − 储量/参考量` 会让"没有木材"被判为"完全不缺"）：
  改为"储量充足度"作为负权重项
- **`func<in T,R>` 编译失败**（CS1960）：变体修饰符只允许出现在接口与委托声明上，改用自定义委托
- **`TileAtClamped` 无法返回引用**（CS8156）：越界安全读取天然是按值返回
- **测试自己制造失败**：① 断言"对角移动两侧都必须可走"比实际规则更严；
  ② 用 `DeathsThisTick` 事后检查（已被清空，应看累计计数）；
  ③ 未固定起终点（随机地图上可能是水）；
  ④ 断言"饿会致死"却让个体自己采到食物活了下来（应把因果链缩到只有饥饿一个环节）
- **`Assert.Fail` 不可访问 / 命名冲突**：统一为公开的 `Fail` + 内部 `FailInternal`
- **PowerShell 多行字符串替换失效**：CRLF 与 LF 混用导致模式匹配不上（改用逐行编辑）

---

## [0.1.0] — M0：世界、时间与观察器

第一个可运行、可验证、可复现的版本。这一版**还没有居民**，它交付的是"世界的地基"：
世界能生成、时间能推进、状态可复现、玩家能观察、并且这些承诺都有自动化测试兜底。

### Added — 模拟内核（`SandBoxSim.Core`）

- **确定性随机源**：`DeterministicRandom`（xoshiro256\*\* + SplitMix64）+ `SimRandom` 七条用途分流
  （WorldGen / Weather / Agents / Events / Combat / Misc / Reserve）+ 状态导出导入
- **噪声**：`PerlinNoise`（置换表由 seed 确定性生成）+ fBm
- **哈希**：`Hash64`（FNV-1a64，状态摘要用）、`NoiseHash`（坐标散列，视觉扰动用）
- **数学**：`SimMath`（Clamp / Lerp / InverseLerp / SmoothStep / MapClamped …）
- **效用曲线**：`UtilityCurve` 八种形状（Linear / Quadratic / Sqrt / Logistic / InverseLogistic / Step / SmoothStep / Constant）
- **JSON 与配置**：手写 `JsonParser`（容忍注释、带行列号报错）、`JsonBinder`（反射绑定、未知键警告）、
  `SimConfig` 及子配置、`ConfigLoader`
- **地形规则**：`TerrainInfo` 唯一规则表（可通行 / 可建造 / 可开垦 / 移动代价）
- **资源模型**：`ResourceNode`（离散 Logistic 再生 + 采集结算）、`ResourceStock`
- **世界生成**：分位数地形分类（保证每张地图都有水有山）、临水湿度加成、海拔温度梯度、铁矿稀有分布
- **空间索引**：`ChunkGrid`（16×16 chunk、16 个统计量、脏标记惰性重算）
- **世界容器**：`World` —— 唯一 Tile 写入口，自动同步空间索引；越界安全读取
- **天气**：六种天气、状态驱动转移（不是每小时乱抽）、直接写入 Tile 湿度/温度
- **日历**：唯一时钟（1 tick = 1 游戏分钟）、昼夜光照、`CompletedDay`
- **资源系统**：`ResourceSystem`（再生 / 采集结算 / 注入 / 枯竭统计）
- **事件日志**：`EventLog` 环形缓冲（2 万条）、重要度分级、按实体检索
- **统计与摘要**：`SimulationStats`（逐日样本、饥荒判定）、`StateHash`（FNV-1a64 + 量化）
- **Tick 管线**：`Simulation`（顺序固定、每小时/每天事件、分批钩子、不变量校验、玩家干预接口）

### Added — 表现层（`SandBoxSim.Console`）

- **终端抽象**：`Terminal`（颜色能力探测 + 三级降级）、`AnsiWriter`（差异输出）、`RenderBuffer`（单次写入）
- **TUI**：半方块像素渲染、`Camera`（整数倍缩放、视图夹取）、`Palette`（地形/资源/UI 配色）
- **九种叠加层**：地形 / 肥沃 / 湿度 / 温度 / 木材 / 食物 / 植被 / 火险 / 可通行
- **UI**：状态栏、右侧信息面板（世界概况 / 气候 / 资源 / 选中格 / 最近事件）、帮助浮层
- **PNG 导出**：手写 `Deflate` / `Inflater` / `Checksums` / `PngWriter` / `PngReader`
- **世界快照**：`WorldSnapshot`（与 TUI 共用配色，保证"屏幕上"与"报告里"一致）
- **报告**：`stats.json` / `report.md`（含趋势迷你图）/ `events.md` / `digest.txt` / `config.effective.json`
- **五种运行模式**：play（交互）/ headless / digest（确定性校验）/ batch（多 seed 体检）/ snapshot

### Added — 测试与工具

- **零依赖反射测试运行器**（`[Fact]` / `[Theory]` + 断言库），97 个用例覆盖
  随机源、哈希、曲线、噪声、JSON、配置、地形规则、资源收支、空间索引、Tick 调度、确定性、玩家干预
- **双通道构建脚本**：有 SDK 走 `dotnet build`（通道 A），无 SDK 走 Roslyn `csc`（通道 B）
- **辅助脚本**：`run.ps1` / `test.ps1` / `dev.ps1` / `install-sdk.ps1`
- **CI**：三平台构建 + 测试 + 确定性校验 + 100 天 headless 冒烟 + artifact 上传

### Added — 文档

- `README.md`、`docs/00-Index.md`（含系统连接度自检表）
- `docs/01-GameDesign.md`（愿景 / 核心循环 / 涌现设计 / 胜负与沙盒哲学）
- `docs/02-SystemArchitecture.md`（依赖方向、"谁读谁/谁改谁"矩阵、反馈环清单）
- `docs/03-SimulationArchitecture.md`（Tick 管线、更新频率表、性能实测）
- `docs/04-DataStructures.md`（全部数据结构与字段语义、关键算法）
- `docs/12-Milestones.md`（M0–M9 拆解、验收标准、构建通道、踩坑记录）
- `docs/13-DeterminismAndSave.md`（确定性契约、状态摘要、存档设计与版本策略）

### Fixed（开发过程中发现并修掉的问题，记录下来供后续参考）

- **小地图上没有水域和山地**：Perlin fBm 的取值聚集在 0.5 附近，固定阈值永不命中。
  改为**分位数阈值**，并把 `WaterLevel` / `MountainLevel` 的语义改成"目标占比"
- **逆 Logistic 曲线方向反了**（`1 − L(x)` 单调递减）：改为 `1 − L(1−x)`，并加单调性测试锁定
- **日统计日期虚报一天**：日边界上 `Calendar.Day` 已翻页，新增 `Calendar.CompletedDay`
- **快照文件名与序列错位**：改为按**目标 tick**命名，而不是落盘时的当前 tick
- **空间索引每 tick 全图重算**：资源再生在"没有变化"时也标脏，改为仅在值真的改变时标脏
- **注入资源会静默覆盖已有资源**：改为"种类不同即拒绝 + 以容量为上限"的保守规则
- **JSON 数组元素读成 null**：`json[0]` 会被编译成字符串索引器（`0` → `"0"`），必须写 `json.Items[0]`
- **PowerShell 脚本在 Windows PowerShell 5.1 下语法报错**：中文注释 + 无 BOM 被按 ANSI 读取；
  脚本统一带 BOM，`tools/*.ps1` 的用户可见输出改为英文
- **构建脚本三元运算符不兼容**：PS 5.1 不支持 `? :`，改显式 `if/else`
- **`$args` 被脚本参数覆盖**：局部 `$args` 会覆盖自动变量，改名 `$buildArgs`
- **函数返回值被管道污染**：`Copy-Item` / 外部命令输出进入管道，函数返回变成对象数组；
  统一 `Out-Null` / `Out-Host`
- **受限环境 restore 静默失败**：MSBuild 多进程构建依赖命名管道，沙箱下被拦截；
  默认改为 `-nodeReuse:false -maxCpuCount:1`（可用 `-ParallelBuild` 找回并行）
- **被点源脚本的 `param` 默认值污染调用方变量**：`. build.ps1 -Mode dotSource` 不可靠，
  拆分为 `build-lib.ps1`（纯函数库，无 param）+ `build.ps1`（CLI），并改用显式开关变量
