# 贡献指南

感谢你对 SandBoxSim 感兴趣。这个项目的目标不是"功能最多的模拟游戏"，而是
**"规则最少、耦合最强、最可解释、最有生命力"的模拟世界**。因此贡献的第一原则是：

> 加一个新机制之前，先读 [docs/00-Index.md](docs/00-Index.md) 的系统连接度自检表，
> 并回答三个问题：它增加**涌现性**吗？增加**玩家能动性**吗？增加**可观察性**吗？
> 三个都答不上来 → 这个改动大概率不该做（见 [docs/11-MVP-Scope.md](docs/11-MVP-Scope.md)）。

---

## 一、环境准备

```powershell
# 一键：确保 SDK（没有就装到仓库外）→ 构建 → 测试 → 启动
.\tools\dev.ps1

# 只构建 + 跑测试
.\tools\dev.ps1 -SkipRun
```

仓库**不依赖任何 NuGet 包**（测试框架、JSON、PNG/zlib 全部自带），因此只要有
.NET 8 SDK 或"运行时 + Roslyn csc"就能构建。细节见
[docs/12-Milestones.md](docs/12-Milestones.md) 的《构建通道》一节。

### 受限环境（沙箱 / 企业策略）

本仓库的构建脚本默认使用 `-nodeReuse:false -maxCpuCount:1`（串行构建）。
原因是 MSBuild 的多进程构建依赖**命名管道**，在部分沙箱环境下会被拦截，
表现为 `restore` 静默失败（输出"生成失败 / 0 个错误"，没有错误行，极难排查）。

如果你的环境允许命名管道，加 `-ParallelBuild` 找回并行速度：

```powershell
.\tools\build.ps1 -Mode test -ParallelBuild
```

---

## 二、提交前的检查清单

```powershell
.\tools\test.ps1                 # 1) 全部测试必须绿
.\tools\run.ps1 -Mode digest -Days 30   # 2) 确定性校验必须通过
.\tools\run.ps1 -Mode batch -SeedRange 1..5 -Days 100  # 3) 多 seed 不崩
```

如果你改了任何影响模拟行为的代码，还应该：

```powershell
.\tools\run.ps1 -Mode snapshot -Days 100 -SnapshotDays 25
# 打开 runs/<runId>/report.md 与 World_*.png，确认世界仍在"活着"（出现建造/人口/迁移等现象）
```

---

## 三、代码约定（模拟内核尤其严格）

### 3.1 `SandBoxSim.Core` 的硬约束

| 禁止 | 替代 | 为什么 |
|---|---|---|
| `System.Random` | `SimRandom.Get(RngStream.X)` | 运行时实现可能变化，破坏可复现性 |
| `DateTime.Now` / `Stopwatch` | `World.Calendar.Tick` | 结果不能依赖真实时间 |
| 遍历 `Dictionary` / `HashSet` | 索引数组 / `List` | 迭代顺序不保证 |
| `object.GetHashCode()` | `Hash64` | .NET 对 string 加了随机化种子 |
| `Math.Pow`（热路径） | `Square` / 手写整数幂 | 跨平台可能有 1 ULP 差异 |
| 直接改 `Tile` 字段 | `World.SetXxx(...)` | 必须同步空间索引 |
| 在 Core 里做 I/O / 控制台输出 | 通过 `Simulation` 暴露查询接口 | 模拟要能脱离 UI 重放 |

**唯一例外**：`Foundation/SimConfig.cs` 里的 `ConfigLoader` 会读文件，
但它只在世界创建前运行一次，不参与模拟循环。

### 3.2 新增系统时的接入检查清单

1. 它属于哪一层？（能不能放在更下层？）
2. 读什么、写什么？更新 [docs/02-SystemArchitecture.md](docs/02-SystemArchitecture.md) 的矩阵。
3. 有正/负反馈吗？更新反馈环清单。
4. 需要新随机数吗？用哪条 `RngStream`？（**不要随便开新流**，会影响旧存档）
5. 需要新配置项吗？加进 `SimConfig` + `config/sim.default.json`，
   并同步 `ConfigTests.DefaultsMatchJson`（这个测试会强制两者一致）。
6. 状态怎么进摘要？实现 `ISimEntitySet.HashInto` 或加进 `StateHash`。
7. Debug 手段是什么？检查器字段 / 热力图 / 日志。
8. 可能造出什么故事？写进 `docs/09-EmergentStories.md`。
9. 有测试吗？至少覆盖：确定性、边界、不变量。
10. 现有测试还全绿吗？

### 3.3 代码风格

- 4 空格缩进，`max_line_length = 120`（`.editorconfig` 已配置）；
- **注释写"为什么"，不写"是什么"**。反例：`// 把 i 加一`。
  正例：`// 用分位数阈值而不是固定阈值：fBm 的值聚集在 0.5 附近，固定阈值会让小地图上没有水`；
- **踩过的坑要写进注释**。这个项目里最有价值的注释往往是"为什么不能这么写"；
- 公共 API 必须有 XML 文档注释；模拟规则、公式、阈值必须能追溯到 `docs/`。

### 3.4 测试约定

- `[Fact("中文说明")]`：普通用例，方法必须无参；
- `[Theory("中文说明")]` + 方法内遍历 `Theory.Cases(...)` 数据表：参数化用例。
  **不要**把用例数据写进特性实参 —— C# 特性实参不能是对象构造表达式（CS0182）；
- 断言消息要带**足够定位问题的现场信息**（数值、索引、地形、时间），
  不要只写"期望 true"；
- 确定性相关的测试不允许有容差（除非在摘要量化精度之内）。

---

## 四、文档约定

每个机制在 `docs/` 里必须能回答五问：

1. 它解决什么问题？
2. 读哪些数据？
3. 改哪些数据？
4. 与其他系统如何连接？
5. 有哪些 Debug 方法、可能产生什么涌现行为？

**未落地的新机制不要先写空文档** —— 那只会生产"读起来很美、实现时全不对"的纸面设计。
文档进度表见 [docs/00-Index.md](docs/00-Index.md)。

---

## 五、提交与推送

本项目按里程碑推进，**每个里程碑一个可运行的提交**，历史本身就是项目成长过程的记录。

```
<里程碑>: <做了什么>

- 关键改动 1
- 关键改动 2

验收：
- .\tools\test.ps1 → N/N 通过
- .\tools\run.ps1 -Mode digest -Days 30 → digest=xxxx（两次一致）
```

- 推送前请确保 `.\tools\test.ps1` 全绿；
- 不要提交构建产物（`bin/`、`obj/`、`artifacts/`）与运行产物（`runs/` 下的报告/PNG）；
- 如果一次改动改变了世界演化结果，请在提交信息里**明确说明**，
  并附上新旧摘要对比 —— 这类改动需要额外验证，不能悄悄合入。

---

## 六、不要做的事（第一阶段禁项）

见 [docs/12-Milestones.md](docs/12-Milestones.md) 结尾的禁项清单：完整科技树、几十种资源、
复杂战斗与外交、金融经济、超大地图、多人联机、3D 美术、任务系统、剧情系统。

理由不是"做不了"，而是：**真正需要先验证的是"10~30 个 NPC 在地图上自己生活是否有趣"。
如果这个核心没有乐趣，更多内容不会让游戏变好。**
