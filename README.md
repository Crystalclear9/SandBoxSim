# SandBoxSim

> 一个由少量规则驱动、系统之间强耦合，从而自行涌现出故事与文明的**沙盒模拟游戏**。

最高设计原则：

> **玩家创造"条件"，而不是直接创造"结果"。**

没有"创建城市"按钮。玩家能做的是放人、种树、改地形、调气候、给资源、制造灾害、修改世界规则。
至于这些人会不会留下、会不会成村、会不会闹饥荒、会不会迁徙、会不会打起来 —— 交给模拟系统自己决定。

---

## 当前状态

| 里程碑 | 内容 | 状态 |
|---|---|---|
| **M0** | 世界生成 / Tile 模型 / Tick 架构 / 空间索引 / TUI 观察器 / PNG 快照 / 测试地基 | ✅ **已完成** |
| **M1** | Agent 实体 / 需求系统 / Utility AI / A\* 寻路 / 8 个动作 / 自给自足的生存 | ✅ **已完成** |
| M2 | 生存经济：共享物资 / 资源枯竭连锁 / 第一次被迫迁徙 / 饥荒 | 🚧 下一步 |
| M3 | 木材石材 / 建造系统（House）/ 库存与共享存储 | ⏳ 计划中 |
| M4 | 农业（Farm）/ 出生与人口 / 迁移 | ⏳ 计划中 |
| M5 | 玩家上帝工具 / 灾害 / 存档 / 完整 Debug 体系 | ⏳ 计划中 |
| M6 | 性格 / 关系 / 家庭 / 个人时间线 | ⏳ 计划中 |
| M7 | 聚落实体 / 领土 / 多聚落 / 完整迁移 | ⏳ 计划中 |
| M8 | 贸易 / 价格 / 外交 / 冲突（WarPressure） | ⏳ 计划中 |
| M9 | 性能优化 / 文档收尾 / CI / v1.0.0 | ⏳ 计划中 |

完整的里程碑拆解、每阶段验收标准与联调踩坑记录见 [docs/12-Milestones.md](docs/12-Milestones.md)。

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
```

### 现在的世界会发生什么

放 40 个人进一张 100×100 的地图，然后什么都不做：

- 他们会**自己找吃的**：饿到一定程度就去采野果（`GatherFood`），采到就吃（`Eat`）；
- **自己找水**：走到河边喝水（`Drink`）；
- **自己睡觉**：夜里困倦度上升，就地睡下（`Sleep`）；
- 顺带**砍柴采石**：为将来的建造攒物资（M3 才会真正用上）；
- **老去并死亡**：55 天后进入老年，有死亡概率；90 天是硬性寿命上限；
- 少数人可能**脱水而死**（走得太远又没找到水），死因会写进事件日志。

也就是说：**M1 已经能自己跑出"一群人的一生"** —— 虽然他们还没有家庭、没有村庄。

### M1 交互键位

| 按键 | 作用 |
|---|---|
| `方向键` / `WASD` | 平移视角（按住 Shift 加速） |
| `+` / `-` | 缩放（整数倍缩放，保证渲染可复现） |
| `SPACE` | 暂停 / 继续 |
| `0` `1` `2` `4` `8` | 速度档（0 = 暂停） |
| `O` | 循环切换叠加层：地形 / 肥沃 / 湿度 / 温度 / 木材 / 食物 / 植被 / 火险 / 可通行 |
| `Enter` | 选中视野中心格（右侧面板显示该格全部属性） |
| `Home` | 相机复位到地图中心 |
| `N` | 用新种子重新生成世界 |
| `R` | 用同一种子重置世界（可复现实验） |
| `H` / `F1` | 帮助浮层 |
| `Q` / `ESC` | 退出 |

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
3. `C:\Users\70454\.sandboxsim-tool\net8`（`install-sdk.ps1` 的默认位置）
4. `PATH` 上的 `dotnet`

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
| [docs/12-Milestones.md](docs/12-Milestones.md) | 里程碑拆解、每阶段可玩验收、构建通道说明、联调踩坑记录 |
| [docs/13-DeterminismAndSave.md](docs/13-DeterminismAndSave.md) | 确定性契约与存档格式 |
| [docs/14-Performance.md](docs/14-Performance.md) | 分批决策、空间索引、实测性能数据 |

其余文档（06 资源、07 建筑、08 人口、09 涌现故事、10 Debug、11 MVP 范围、15 参数总表）
随对应里程碑落地时补齐 —— 见 [docs/00-Index.md](docs/00-Index.md) 的进度表。

---

## 许可

[MIT](LICENSE)
