# 连续世界的代理改进实验

`sandbox-stream-v1` 把代码优化问题改成连续变化的世界事件流。代理需要写实际 Python 实现，在正确处理更新的前提下降低成本，再修改自己的优化过程。它沿用代理协议 v2 的迁移、冻结、后代分叉和方法撤销，并在后续阶段加入旧能力保留门槛。

## 三类连续任务

| 任务族 | 世界变化 | 容易出现的错误 |
|---|---|---|
| `resource_ledger` | 资源库存耗尽与补充，反复寻找仍有存量的最近同类资源 | 缓存继续返回已耗尽节点；同距离编号选择错误 |
| `dynamic_routes` | 道路代价与阻塞持续改变，查询带权四邻域最短路径 | 旧路径缓存未失效；起终点阻塞仍可通行；把最少步数当最低代价 |
| `scene_lifecycle` | 实体删除、槽位复用、新旧世代更新，观察局部集合 | 旧世代删除新实体；旧更新覆盖新模型；重复选择生成重复行 |

每道题包含初始状态和按顺序执行的命令，`solve(problem)` 返回每次查询的答案。代理获得格式说明及一个有答案的小型开发样例；留出输入、种子、参考答案和私有更新率不会出现在请求里。开发反馈区分输出是否正确、错了几组、CPU/墙钟加速和内存门槛，不再只给无法解释的零分。

这些是从沙盒问题抽取的独立 Python 工作负载，不是整个 C# 游戏世界，也不验证模型视觉或自主游玩。真实游戏程序集优化使用 [HTTP 后端](rsi-improver.md)。

## 课程与旧能力保留

阶段改变开发更新率和观察范围；私有留出包含读取密集和高变更两种分布。第二阶段起，父、子代理还会在每个旧阶段的 **12 个新私有实例** 上重新比较，既不重复原来的问题，也不给代理留出反馈。

保留门槛同时要求：

- 每个旧任务族的能力变化区间下界不低于负的 `effectMargin`，不能用其他族的增长掩盖一个族的回归。
- 父代理原本正确的任务仍由子代理正确完成；即使双方的加速收益都为零，正确变错误也拒绝通过。

旧能力保留通过只表示没有触发这些回归筛选线；其本身不是能力改进证据。仍须通过原来的五项迁移与因果对照，并确实改变 `improve` 方法。少量题目的 bootstrap 区间是开发筛选依据，不是普适统计证明。

## 运行与报告

只需要 Python 3.10+，不需要 Godot 或 .NET。先运行公开的非 LLM 夹具：

```powershell
python tools/rsi_benchmark.py run benchmarks/rsi-stateful-smoke.json --output runs/evaluation/stateful-example
python tools/rsi_benchmark.py verify runs/evaluation/stateful-example
python tools/rsi_benchmark.py report runs/evaluation/stateful-example --output runs/reports/stateful-example.html
```

输出目录和报告文件必须是新的。HTML 可用浏览器打开，包含阶段能力曲线、五项证据及区间、未通过的原因、各组成本、旧能力正确性和失败分叉；阶段可以跳转，控制组与失败详情可展开。报告生成前重新验证源码链、任务、轨迹和汇总，篡改后的实验不能生成有效报告。

报告不会输出私有题目或答案，但包含留出汇总，实验结束前不要把它反馈给受测代理。它不重新测量硬件成本，也不把字节数换算成 token。

成本、低预算搜索与部署回收的分析见 [改进器效率研究](rsi-efficiency.md)。报告现在同时展示这些研究结果，原协议的分数和实验归档保持原样。

## 接入自己的代理

复制公开配置，改为自己的 `agent` 文件并设 `fixtureOnly: false`，保留 `backend: "code"`、`taskSuite: "sandbox-stream-v1"`。代理提供顶层 `propose(request)` 和 `improve(request)`，stdin/stdout 传 JSON；前者返回 `proposal.source`，后者返回实际下一代源码。单文件代理会复制到独立调用目录，应自包含或依赖已安装模块；所需密钥只通过 `envAllowlist` 传入。

`stages` 范围 1–5，`probeTasks` 是 12–60 的三倍数，后代分叉 3–5 次。`workloadScale` 范围 1–4，固定放大实体数和命令轮数；默认 1 是快速集成规模。增加规模要重新运行，不能和不同规模的样本直接混成一个速度比较。候选仍受八秒进程限制，超时和失败保留为零收益，不能删除失败样本。

七组不同输入由独立参考实现判题，沿用实际 CPU、墙钟与 Python 分配峰值门槛；Windows 短测量使用保守的 CPU 时钟余量，正确代码可能仍没有可确认的速度收益。计时范围和限制见 [代码后端](rsi-improver.md)。

公开夹具只演示实际代码、缓存失效和课程保留；它没有让改进方法本身变好，不能当作真实模型 RSI 结果。运行器仅面向可信本地代码，没有操作系统隔离、防作弊签名或独立模型身份认证。

## 文件入口

| 文件 | 职责 |
|---|---|
| `tools/rsi_stateful.py` | 连续命令生成、小型开发样例与独立语义参考 |
| `tools/rsi_code.py` | 七组实际候选/参考成本及正确性 |
| `tools/rsi_benchmark.py` | 自修改、控制组、旧能力门槛和回放 |
| `tools/rsi_report.py` | 验证后的独立 HTML 报告 |
| `benchmarks/agents/stateful_improver.py` | 明确标记的非 LLM 集成夹具 |
| `tools/test_rsi_stateful.py` | 库存、路径、世代、错误缓存与回归门槛检查 |

---

[文档导航](../README.md) · [代理协议](rsi-improver.md) · [项目首页](../../README.md)
