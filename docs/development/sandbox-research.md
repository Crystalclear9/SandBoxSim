# 在真实沙盒中研究和修改代码

`sandbox_research.py` 执行真实 C# 工程的研究循环：给代理源码和开发世界剖析，接收假设与修改，编译、检查语义，启动参考和候选服务，比较完整世界轨迹，再独立验证被选候选。代理不再仅返回一个已经启动的 HTTP 地址，也不局限于独立 Python 算法题。

该循环是实际代码研究基础设施。它不单独证明代理的改进方法越来越强，也不把某次程序加速认定为 RSI；二阶证据仍使用 [代理改进协议](rsi-improver.md)。

## 一次研究如何执行

1. 复制当前工作区里 Git 跟踪及未跟踪但未被忽略的 Core、Console、Tests、配置与构建约定，创建独立参考源码快照，保存逐文件 SHA-256。新源码在提交前也能参与研究，构建输出仍排除；已删除的跟踪文件不进入快照。不会切换分支、写主工作区或操作 PR。
2. 构建 Release Console 与 Tests，执行配置指定的语义检查。默认覆盖居民确定性、寻路和资源可达性；检查失败或没有匹配测试会阻止继续。
3. 在每个公开开发世界上跑一次真实参考服务，剖析完整 episode、模拟步进、路径查询和初始化/释放成本。单次冷测只提供调查线索，不是稳定性能结论。
4. 代理可以检查独立源码快照，并收到允许修改文件的完整内容、源码哈希、开发套件、基线剖析和此前失败/开发反馈。返回研究假设和实际文件修改。
5. 每次候选从相同参考源码重新复制。先验证全部修改的路径、原始哈希、大小和重复项，再一次性应用；当前只允许控制器明确列出的已有 Core `.cs` 文件。Tests、Console、项目文件、配置和判分器保持参考内容。
6. 编译候选并执行相同语义检查。失败保留源码、命令、原始日志、耗时与诊断，进入下一次开发反馈，不删除、不自动重试。
7. 候选服务与固定参考服务做完整 episode 配对：世界建立、预演、独立 Dijkstra 寻路判题、居民模拟步进、状态摘要、地图和释放。交替执行顺序，冷启动也计入成本。
8. 仅按正确候选的开发集加速中位数选择一个候选。所有研究调用结束后，才在独立种子的留出世界上测量。留出输入和结果不会加入请求或后续反馈。

无论留出结果如何，都保留候选；工具不把候选自动复制回主源码。没有确认加速时 `heldoutImprovementDetected` 为 false。有正确候选但其开发速度下降时，仍可测量其留出行为，不能把“被选中测量”误认为“已经改进”。

三个冻结研究程序的真实世界对照见 [研究程序对照](sandbox-research-comparison.md)。

## 运行

需要 Python 3.10+、Git 与 .NET 8 SDK，不需要 Godot。默认优先使用用户工具目录的 `dotnet`，可用 `--dotnet` 指定完整 SDK 主程序路径。

```powershell
python tools/sandbox_research.py run benchmarks/sandbox-research-smoke.json --output runs/evaluation/research-example
python tools/sandbox_research.py verify runs/evaluation/research-example
python tools/sandbox_research_report.py runs/evaluation/research-example --output runs/reports/research-example.html
```

目录和报告必须是新的。报告展示每次真实研究假设、修改 diff、编译失败、开发与留出结论，以及留出世界的人口、物资、建筑和完整摘要轨迹一致性。报告与归档包含留出汇总，实验结束前不要将其反馈给受测代理。

## 接入代理

公开配置使用 `schemaVersion: 1`，协议名称为 `real-sandbox-research-v1`。这是源码研究协议，不是代理自修改 v2 的同一 ABI。

`agent` 指向独立 Python 文件，通过 stdin 收一个 JSON 请求，stdout 返回一个 JSON 对象。请求 `mode` 为 `research`，包含 `sourceWorkspace`、`files`、`developmentSuite`、`developmentProfile`、`feedback` 和 `attempt`。代理可以自行检查快照里的真实模块、测试与配置；`editableFiles` 是允许返回修改的范围，不是固定算法菜单。控制器本机完整源码快照可被可信代理读写，因此该契约不是操作系统隔离。

```json
{
  "hypothesis": "解释成本来源、优化机制和语义风险",
  "edits": [{
    "path": "src/SandBoxSim.Core/Pathing/AStarPathfinder.cs",
    "baseSha256": "请求给出的原始哈希",
    "content": "修改后完整 UTF-8 C# 源码"
  }]
}
```

代理文件复制到独立调用目录。真实模型适配器在文件中调用所需服务，密钥名称通过 `envAllowlist` 声明；不会由控制器自动选择或调用供应商。`fixtureOnly: false` 只是操作者声明，不是模型身份认证。公开夹具第一轮故意制造编译失败，第二轮提交保留优先级与编号平局规则的二叉堆上浮实现；它没有模型调用或自动发现能力。

`attempts` 范围 1–8，配对 `repeats` 范围 3–20。公开套件的三个世界、三次重复共九组配对；加速仍要求正确性通过、改变二进制且区间下界超过 1.05。开发与留出种子必须互不重复。`testFilters` 可以扩展为与修改相关的检查，候选不能修改这些测试。

## 产物与研究边界

归档保留 `reference/`、`candidate-N/`、`calls/`、`reference-logs/`、`development-profile/`、`attempt-N/`、`heldout/`、源码清单、配置和结果。控制器总墙钟包含快照、代理、编译、语义检查、服务启动、开发与留出测量；每条命令和每次服务配对另外记录成本。命令超时默认 120 秒，代理超时由 `agentSeconds` 控制；没有统一的整轮算力预算或真实 token 账单。

`verify` 核对参考与候选源码、代理调用、开发反馈顺序、程序集身份、构建检查日志、开发选择及留出判分，拒绝修改结果结论、替换源码/二进制或把私有结果用于候选选择。它不重新测量相同硬件耗时，也不提供防恶意代码认证。

当前测量的是 Console/HTTP 的真实模拟内核与寻路，**不包含 Godot 图像、渲染或图形会话额外的荒野日界作用**。短世界通过不能保证全部长期行为；扩大人口、步数、地图与检查范围，需要新的开发/留出配置和实际重跑。公开样例不是私有排行榜，应由独立控制器提供真实研究的保密套件。

运行器只适合可信本地代理与候选，普通进程可以访问本机文件与网络。路径白名单、源码哈希与独立目录是工程约束，不是 OS 安全边界。暂不修改配置或存档格式，因此这个循环专注语义保持的程序改进。

---

[文档导航](../README.md) · [真实程序效率](online-efficiency.md) · [代理改进协议](rsi-improver.md) · [项目首页](../../README.md)
