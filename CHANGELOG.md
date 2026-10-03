# Changelog

本项目的版本历史。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [Unreleased]

### 计划中（M1）

- Agent 实体（SoA 存储）、需求系统（Hunger / Energy / Health）
- Utility AI：考虑项 → 曲线 → 加权组合 → 决策，并输出**可解释的效用分解**
- A\* 寻路（二叉堆、八方向、确定性平局）+ 路径缓存 + 移动
- 第一批动作：Eat / Sleep / GatherFood / GatherWood / GatherStone / Wander
- Console：点选个体、Agent 检查器、路径可视化
- 文档：`docs/05-UtilityAI.md`、`docs/14-Performance.md`

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
