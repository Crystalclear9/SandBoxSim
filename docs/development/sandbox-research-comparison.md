# 真实沙盒中的研究程序对照

`sandbox_research_compare.py` 在真实 C# 沙盒上比较三个冻结的研究程序：父程序、子程序和撤销改进方法的程序。每组独立提交源码、编译、执行语义检查，再在开发与留出世界中运行完整 episode。这里的组名由操作者指定，工具不会自动把一个源码优化认定为递归自我改进。

## 执行和比较

```powershell
python tools/sandbox_research_compare.py run benchmarks/sandbox-research-comparison-smoke.json --output runs/evaluation/research-comparison --dotnet /absolute/path/to/dotnet
python tools/sandbox_research_compare.py compare --parent runs/evaluation/research-comparison/parent --child runs/evaluation/research-comparison/child --reverted runs/evaluation/research-comparison/reverted --output runs/reports/research-comparison.json
```

`run` 的配置包含 `agents`（三个 Python 研究适配器路径）、`order`（每组恰好执行一次）和共享的 `research` 条件。适配器路径相对配置目录；源码修改仍遵循 [真实源码研究协议](sandbox-research.md)。目录和输出文件必须是新的，不修改主源码或 Git 状态。

`compare` 先验证三个完整归档，要求参考源码逐文件一致，开发/留出世界、允许编辑文件、测试、尝试数、调用时限和环境变量白名单一致。每组只根据自己的开发反馈选择候选；组之间不共享失败反馈或留出结果。实验执行顺序保存于 `comparison.json`。

## 输出含义

每个留出世界按重复配对的完整 episode 墙钟时间计算中位加速比，再换算时间节省比例 `1 - 1 / speedup`。负值表示变慢；没有候选或任何留出正确性检查失败时，优化收益记为零，失败尝试和总成本保留。

输出包含逐世界节省、平均节省、控制器全程耗时、节省比例除以研究秒数，以及子减父、子减撤销组的配对差异。总成本包含快照、代理、构建、测试、开发测量和留出验证。这是事后研究流程比较，不是部署节省、模型 token 效率或实时搜索吞吐率。

公开 smoke 配置的三个适配器完全相同，是空效应控制。它用于检查相同程序是否会因测量噪声被误称为 RSI，没有模型调用或自动改进过程。该工具始终输出 `recursiveEvidenceSupported: false`。

## 已运行的空效应控制

2026-10-08 本机执行三个独立真实研究归档，共 6 次代理调用；每组保留一次编译失败，第二次候选均通过留出语义检查，三组都没有确认程序加速。每组使用三个开发和三个留出世界、三次重复配对，参考/候选共保留完整 episode 与日志。

三组脚本相同但观察到的时间节省仍不同，说明单次小样本差异可以来自计时噪声。工具没有据此输出 RSI 支持。完整运行位于 `runs/evaluation/real-research-null-comparison-20261008`；加入细分成本账本后的重新验证分析位于 `runs/reports/real-research-null-comparison-current-20261008.json`，原分析保留。

## 能够支持的结论

需要重复运行、运行块不确定性和逐世界回归门槛时，使用[真实源码研究的重复对照](sandbox-research-series.md)。该扩展继续区分冻结研究方法的改善与递归自修改证据。

这条接口将父/子研究程序对照接入真实模拟世界，但没有认证谱系或“只撤销方法”的修改范围，也不控制外部模型记忆。初始性能剖析分别测量，反馈并非逐字相同。相同调用时限与尝试数不等于强制相同的全程算力预算；顺序执行和单次研究样本也不足以支持显著性结论。

已有 [代理自修改协议](rsi-improver.md) 仍承担谱系、方法撤销和后代能力证据。两类结果必须分别解释，不能用这个描述性对照替代原证据门槛。可信本地进程的使用边界与源码研究相同。

---

[文档导航](../README.md) · [真实源码研究](sandbox-research.md) · [效率与成本](rsi-efficiency.md)
