# SandBoxSim

> 一个由少量规则驱动、系统之间强耦合，从而自行涌现出故事与文明的**沙盒模拟游戏**。

最高设计原则：

> **玩家创造"条件"，而不是直接创造"结果"。**

没有"创建城市"按钮。玩家能做的是放人、种树、改地形、调气候、给资源、制造灾害、修改世界规则。
至于这些人会不会留下、会不会成村、会不会闹饥荒、会不会迁徙、会不会打起来 —— 交给模拟系统自己决定。

---

## 当前状态

2026-10-05，Godot 4 + C# 图形开发版已接入真实 3D 场景与重新设计的浮动 HUD。
镜头支持缩放、旋转、俯视、近景与跟随；手记包含故事、人物、历史与可回看的每日曲线。
视觉方向、操作与实测边界见 [3D 界面验收](docs/21-VisualInterface.md)。

```powershell
# 图形游戏：需要 .NET 8 SDK 与 Godot 4.7.2 .NET
./tools/godot.ps1 -Mode run

# 导入资源并检查工具、镜头、人物跳转、只读观察、存档与窗口布局
./tools/godot.ps1 -Mode test
```

图形客户端的 NuGet 与引擎依赖仅用于表现层；内核、Console 和测试仍支持原有 SDK / csc 双通道。
Windows 已配置开发环境时，也可双击根目录的 `Play-3D.cmd` 启动图形游戏。
原任务书逐项状态见 [完整交付清单](docs/20-FullDeliveryChecklist.md)。最新证据、修复项和未完成内容见
[计划实现核验](docs/17-ImplementationAudit.md) 与 [当前缺陷修复交付](docs/18-CurrentDefectRepairs.md)。以下状态不代表全部长期计划已完成。

| 里程碑 | 内容 | 状态 |
|---|---|---|
| **M0** | 世界生成 / Tile 模型 / Tick 架构 / 空间索引 / TUI 观察器 / PNG 快照 / 测试地基 | ✅ **已完成** |
| **M1** | Agent 实体 / 需求系统 / Utility AI / A\* 寻路 / 8 个动作 / 自给自足的生存 | ✅ **已完成** |
| **M2** | 生存经济：地面物资堆 / 野生动物与狩猎 / 局部枯竭与迁徙 | ✅ **已完成** |
| **M3** | 建造：住房 / 仓库 / 农田 / 矿场 / 三层物资存储 | 建造与铁矿采集链已实现，完整交付核验中 |
| **M4a** | 实体可见性（人 / 动物 / 建筑上屏）+ 上帝工具面板（4 类 19 个工具） | ✅ **已完成** |
| **M4b** | 个体检查器（效用分解）+ 存档 / 读档（含自校验摘要） | ✅ **已完成** |
| **M4** | **农业产出 / 出生与人口 / 儿童阶段 / 建筑衰减** | ✅ **已完成** |
| **M5** | 火灾 / 灾害工具 / 规则开关 / 报告升级 | 核心已实现；已修复长期温度贴零、火势同次连锁与上限漏算 |
| M6 | 性格 / 关系 / 家庭 / 个人时间线 | 家庭档案、亲属关系、个人历史与 Godot 检查器已接入 |
| M7 | 聚落实体 / 领土 / 多聚落 / 完整迁移 | 归属与领土已接入；本地批量记录 20/20 形成、11/20 曾同时多聚落 |
| M8 | 贸易 / 价格 / 外交 / 冲突（WarPressure） | 地方库存与价格、实体商队、文明、联盟和战争机制已接入，完整核验中 |
| M9 | 性能优化 / 文档收尾 / CI / v1.0.0 | ⏳ 计划中 |

完整的里程碑拆解、每阶段验收标准与联调踩坑记录见 [docs/12-Milestones.md](docs/12-Milestones.md)。

> **M4 的意义**：在它之前，这个世界**必然灭绝** —— 没有出生，人口单调降到 0
> （实测 100 天：38 例衰老 + 2 例脱水）。
> 现在同一配置下人口稳定在 **46 人（床位 46/46），200 天不灭绝也不爆炸**，
> 死因只剩自然衰老。农业、出生、住房约束与建筑衰减四条链都真正在运转。

> **M5 的意义**：玩家第一次能**毁掉条件**而不只是"加东西" ——
> 点燃一片森林，后果会沿"木材 → 盖房 → 床位 → 出生"自己扩散出去。
> 实测 200 天：101 次自然起火、101 格焦土、人口稳定在 46。
> 上述数字是历史运行记录。本轮已修复长期温度贴零与火势传播时序，最新证据见 docs/18。
>
> 自然火次数和规模仍受地形、燃料与天气影响；历史运行数字不代表所有种子的目标值。
> 存档确定性方面，Phase 0（第六个隐形状态）与 Phase 1c（第七、八个）都已修复，
> 见 [CHANGELOG](CHANGELOG.md) 与 [docs/13](docs/13-DeterminismAndSave.md)。

---

## 现在能做什么

```powershell
# 1) 交互观察世界：放 30 个居民，看他们自己找吃的、喝水、睡觉
.\tools\run.ps1 -Agents 30

# 2) 无人干预长跑 100 天（40 人），产出报告 / 统计 / 事件清单 / 世界快照
.\tools\run.ps1 -Mode snapshot -Days 100 -Agents 40 -SnapshotDays 25

# 3) 确定性校验：同 seed 连跑两遍（带个体），状态摘要必须完全一致
.\tools\run.ps1 -Mode digest -Days 30 -Agents 30

# 4) 多 seed 批量体检（鲁棒性）
#    注意用 -SeedRange（字符串）而不是 -Seeds：`run.ps1 -Seeds 1,2,3` 里的逗号
#    会被 PowerShell 当成参数分隔符，实际只会跑 seed 1（很容易误判结果的坑）。
.\tools\run.ps1 -Mode batch -SeedRange 1..20 -Days 60 -Agents 20

# 5) 跑全部测试
.\tools\test.ps1

# 6) 存档与读档：跑 20 天存档，然后从存档继续（世界状态完全来自存档）
.\tools\run.ps1 -Seed 555 -Days 20 -Agents 30 -SaveFile runs\lab\w.simsave
.\tools\run.ps1 -LoadFile runs\lab\w.simsave -Days 20
#    读档失败会**明确报错并中止**，不会静默退回一个新世界。
#    读档后若状态摘要与写档时不一致，会打印"第一个不同的段落"以便定位。
```

> **`--save-file` / `--load-file` 是"实验留痕"的基础**：
> 一个跑了几十分钟的长跑可以在关键节点存档，之后从那里反复尝试不同的干预，
> 而每次都从一个**完全相同**的起点出发 —— 这是"改一个条件看后果"能成立的前提。

### 现在的世界会发生什么

放 40 个人进一张 100×100 的地图，然后什么都不做：

- 他们会**自己找吃的**：饿到一定程度就去采野果（`GatherFood`），采到就吃（`Eat`）；
- **自己找水**：走到河边喝水（`Drink`）；
- **自己睡觉**：夜里困倦度上升，就地睡下（`Sleep`）；
- **打猎**：附近有鹿就去追（`Hunt`）—— 但鹿会跑，所以经常扑空；
- **攒物资**：随身东西太多了就放到地上（`Deposit`），缺了再去拿（`Take`），
  于是地图上会慢慢出现几堆物资 —— 那是他们"活动中心"的痕迹；
- **把一片地方吃光然后搬走**（`Migrate`）：出生地周围的资源被 40 个人采稀之后，
  会有一批人同时决定离开，走向几十格外的另一片地方；
- **盖房子**（`BuildHouse`）：一旦有人没床位，就有人去砍柴、开工地、盖起住房；
  10 天左右会自己长出二三十间房子；
- **盖仓库**（`BuildStorage`）：攒下的木料多到"没地方放"时，会有人去采石并建仓库 ——
  从那以后物资有了**公共**的去处（而不再只是堆在地上）；
- **开垦农田**（`BuildFarm`）：在临水的草地上开出农田（M4 才会真正产出粮食）；
- **老去并死亡**：55 天后进入老年，有死亡概率；90 天是硬性寿命上限；
- 少数人可能**脱水而死**（走得太远又没找到水），死因会写进事件日志。

也就是说：**现在能自己跑出"一群人把一片地方用光、搬家、盖起房子、建起仓库"的完整过程** ——
后续也可出现子女、家庭与聚落；这些变化来自资源、住房和居民行为的共同作用。

### 用诊断看"世界是否在动"

跑一次 20 天的经济诊断，可以直接看到每个动作被选中多少次、
动物种群是否可持续、有多少物资在地面上流动：

```powershell
.\tools\test.ps1 -Filter EconomyDiagnostics
```

输出里最值得看的三个数字：

| 数字 | 健康的样子 | 不健康的样子 |
|---|---|---|
| `ChosenByAction` 分布 | 采集/进食/喝水/睡眠/存放都有一定比例 | 某个动作恒为 0（机制静默失效），或某个动作占 80%+（自反馈失控） |
| `动物 / 容量` | 稳定在容量的 50%–90% 之间波动 | 掉到 0（狩猎把种群打绝，这条路永久关闭） |
| `人均随身物资` | 稳定在"舒适上限"附近 | 无限增长（采集没有"够了"的信号） |

### 交互键位

| 按键 | 作用 |
|---|---|
| `方向键` / `WASD` | 平移视角（按住 Shift 加速）；**工具面板打开时改为在面板内导航** |
| `+` / `-` | 缩放（整数倍缩放，保证渲染可复现） |
| `SPACE` | 暂停 / 继续 |
| `0` `1` `2` `4` `8` | 速度档（0 = 暂停） |
| `O` | 循环切换叠加层：地形 / 肥沃 / 湿度 / 温度 / 木材 / 食物 / 植被 / 火险 / 可通行 / **人口** / **AI 状态** / **建筑** |
| `Enter` | 没有选中工具时：选中视野中心格附近最近的人（右侧面板显示他的需求、性格与**效用分解**）；工具面板打开时：在光标处执行当前工具 |
| `TAB` | 开关**上帝工具面板** |
| `[` / `]` | 调整工具作用半径（1–10 格） |
| `F` | 相机跟随选中的个体 |
| `Home` | 相机复位到地图中心 |
| `N` | 用新种子重新生成世界 |
| `R` | 用同一种子重置世界（可复现实验） |
| `H` / `F1` | 帮助浮层 |
| `ESC` | 依次：收起工具面板 → 取消选中 → 退出 |
| `Q` | 退出 |

#### 上帝工具面板（`TAB`）

工具只能改**条件**，不能直接产出**结果** —— 面板里没有"造一座城""开始一场战争"。

| 类别 | 工具 |
|---|---|
| **创造** | 放 1 / 5 名居民、放 5 只动物、催生森林 |
| **地形** | 草地 / 森林 / 水域 / 山地 / 沙地 |
| **资源** | 注入食物 / 木材 / 石料 / 铁矿 |
| **恩惠** | 肥沃度 ±0.3、资源再生 ×2 / ×0.5、降雨 / 干旱（24 小时） |

面板内：`←→` 换类别，`↑↓` 换工具，`[` `]` 调作用半径，`Enter` 执行。
每个工具执行后底栏会回显"做了什么" —— 因为"恩惠"类工具（地力 / 再生 / 天气）
点下去只看到颜色变化，没有回显会让人以为工具坏了。

> **它们的效果分两类**：地形 / 资源 / 放人 / 放动物是**立刻可见**的；
> 地力 / 再生倍率 / 天气是**延迟生效**的 —— 点下去只看到数值变化，
> 真正的后果要等模拟把它放大出来。后者才是这个游戏的玩法。

---

## 构建与运行

### 两条构建通道

本仓库刻意支持两条通道，原因是**"能不能跑"不该取决于开发机装了什么**：

| 通道 | 前置条件 | 命令 |
|---|---|---|
| **A（首选）** | .NET 8 SDK | `.\tools\build.ps1 -Mode build`（自动探测） |
| **B（降级）** | 只有 .NET 8 运行时 + Roslyn `csc`（例如只装了 VS2022） | `.\tools\build.ps1 -Channel csc` |

仓库**不依赖任何 NuGet 包**（连测试框架都是自己的反射运行器），因此通道 B 与通道 A
产出的行为一致。这是刻意的工程约束，不是省事：模拟内核的确定性不该被包版本波动影响。

```powershell
# 一键：确保 SDK → 构建 → 测试 → 启动
.\tools\dev.ps1

# 如果机器上没有 SDK，可以装到仓库外的工具目录（不进 git）
.\tools\install-sdk.ps1
```

SDK 默认探测位置（按顺序）：

1. `-SdkRoot` 参数指定的目录
2. 环境变量 `SANDBOXSIM_DOTNET_ROOT`
3. 环境变量 `DOTNET_ROOT`（CI 的 setup-dotnet 安装目录）
4. 用户主目录下的 `.sandboxsim-tool/net8`（`install-sdk.ps1` 的默认位置）
5. `PATH` 上的 `dotnet`
6. 当前平台的系统安装目录

失效路径会跳过；Windows 使用 `dotnet.exe`，Linux/macOS 使用 `dotnet`。
脚本通过 `dotnet --list-sdks` 确认稳定版 .NET 8 SDK，支持 PATH 中的符号链接。
三平台建议使用 PowerShell 7（`pwsh`），Windows PowerShell 5.1 也保留兼容。

```powershell
pwsh -NoProfile -File ./tools/test-build.ps1
pwsh -NoProfile -File ./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
pwsh -NoProfile -File ./tools/build.ps1 -Mode test -Configuration Release -Channel sdk -ParallelBuild
```

本次修复、验收记录和文件管理约定见 [构建与交付说明](docs/16-BuildAndDelivery.md)。

> 为什么 SDK 装在仓库外：本仓库可能被放在只读沙箱里（例如 AI 协作环境），
> 仓库外的工具目录更稳；同时避免几百 MB 的二进制进入 git 历史。

### 直接使用 dotnet

```powershell
dotnet build .\SandBoxSim.sln -c Release
dotnet .\src\SandBoxSim.Console\bin\Release\net8.0\SandBoxSim.Console.dll --headless --days 100
dotnet .\src\SandBoxSim.Tests\bin\Release\net8.0\SandBoxSim.Tests.dll
```

---

## 仓库结构

```text
SandBoxSim/
├─ config/                 # 全部可调参数（JSON，带 $comment 注释）
├─ docs/                   # 设计文档：从 GDD 到实现细节
├─ runs/                   # 运行产物（报告/统计/PNG），默认不入库
├─ src/
│  ├─ SandBoxSim.Core/     # 纯模拟内核：零 I/O、零时钟、可确定性重放
│  │  ├─ Foundation/       #   随机源、噪声、哈希、数学、曲线、JSON、配置
│  │  ├─ World/            #   Tile、地形规则、世界生成、空间索引、天气、日历
│  │  ├─ History/          #   事件日志（世界故事与个人时间线的数据基础）
│  │  ├─ ResourceSystem.cs #   资源再生 / 采集结算 / 枯竭统计
│  │  └─ Simulation.cs     #   Tick 管线 + 玩家干预接口 + 不变量校验
│  ├─ SandBoxSim.Console/  # 全部表现层（Core 永远不知道它存在）
│  │  ├─ Tui/  Render/ Ui/ #   终端渲染、相机、调色板、面板
│  │  ├─ Png/              #   手写 zlib/PNG 编解码 + 世界快照
│  │  ├─ Reporting/        #   headless 报告（stats.json / report.md / events.md）
│  │  └─ Cli/ Program.cs   #   参数解析 + 五种运行模式
│  └─ SandBoxSim.Tests/    # 零依赖反射测试运行器 + 全部测试
└─ tools/                  # 构建 / 测试 / 运行 / 安装 SDK 脚本
```

### 架构铁律

1. **依赖单向**：`Core` 不认识 `Console`，`Console` 只能只读查询 + 调用干预接口。
2. **只有一份时钟**：`Simulation.World.Calendar.Tick`。禁止 `DateTime.Now` / `Stopwatch` 参与模拟。
3. **只有一份随机源**：`Foundation.SimRandom`（7 条分流）。禁止 `System.Random`。
4. **写 Tile 只有一条路**：`World` 的写入方法（会自动同步空间索引）。
5. **渲染与模拟解耦**：60 FPS 渲染与 10 TPS 模拟互不影响，倍速只改"每帧推几步"。

这些约束都有对应的自动化测试或代码审阅点，详见 [docs/13-DeterminismAndSave.md](docs/13-DeterminismAndSave.md)。

---

## 可复现性（本项目的核心承诺）

同一个 seed 必须给出同一个世界，**逐格一致**：

```powershell
.\tools\run.ps1 -Mode digest -Days 30
#   第 1 次：digest=dc94d8d5daf8d42d
#   第 2 次：digest=dc94d8d5daf8d42d   ← 必须完全一致
#   对照 seed 变更后的摘要必须不同     ← 证明种子真的生效
```

`digest` 是 `FNV-1a64` 状态摘要，覆盖：seed、tick、全部 Tile（地形/资源/湿度/肥沃度/火/建筑索引）、
全部实体、以及各系统计数。任何一处引入不确定性（时钟、字典迭代顺序、未分流的随机数）都会让它失效。

---

## 设计文档

| 文档 | 内容 |
|---|---|
| [docs/00-Index.md](docs/00-Index.md) | 文档地图 + 系统连接度自检表 |
| [docs/01-GameDesign.md](docs/01-GameDesign.md) | 游戏愿景、核心幻想、核心循环、涌现设计、成败哲学 |
| [docs/02-SystemArchitecture.md](docs/02-SystemArchitecture.md) | 系统依赖图，"谁读谁 / 谁改谁"矩阵 |
| [docs/03-SimulationArchitecture.md](docs/03-SimulationArchitecture.md) | Tick 架构与各系统更新频率 |
| [docs/04-DataStructures.md](docs/04-DataStructures.md) | 全部数据结构与字段语义（与源码 1:1 对照） |
| [docs/05-UtilityAI.md](docs/05-UtilityAI.md) | Utility AI：考虑项、曲线、合成公式、选靶、执行、可解释输出 |
| [docs/06-ResourceModel.md](docs/06-ResourceModel.md) | 资源分布、采集公式、口粮账、Logistic 再生、生态链、物资堆 |
| [docs/07-Buildings.md](docs/07-Buildings.md) | 建筑造价表、选址规则、施工流程、三层物资存储、因果链 |
| [docs/08-PopulationModel.md](docs/08-PopulationModel.md) | 生命历程、需求、死因判定、出生模型与反馈环 |
| [docs/09-EmergentStories.md](docs/09-EmergentStories.md) | 12 个可复现的涌现故事（含真实运行数据与复现命令） |
| [docs/10-DebugAndObservation.md](docs/10-DebugAndObservation.md) | 叠加层、诊断命令、个体检查器、如何留下可复核的证据 |
| [docs/11-MVP-Scope.md](docs/11-MVP-Scope.md) | 必须做 / 可以做 / 暂时不做（区分"已实现"与"已规划"） |
| [docs/12-Milestones.md](docs/12-Milestones.md) | 里程碑拆解、每阶段可玩验收、构建通道说明、联调踩坑记录 |
| [docs/13-DeterminismAndSave.md](docs/13-DeterminismAndSave.md) | 确定性契约、状态摘要、存档格式与自校验 |
| [docs/14-Performance.md](docs/14-Performance.md) | 分批决策、空间索引、实测性能数据 |
| [docs/15-ConfigReference.md](docs/15-ConfigReference.md) | 每个可调参数的含义、默认值、影响链、调参流程与踩坑记录 |

---

## 许可

[MIT](LICENSE)
