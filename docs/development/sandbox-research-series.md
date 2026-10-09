# 真实源码研究的重复对照

`sandbox_research_series.py` 将[冻结研究程序对照](sandbox-research-comparison.md)重复执行为独立运行块。每块包含父、子、撤销三个完整真实 C# 研究归档，候选仍经过编译、语义检查、开发选择和留出验证。它用于估计研究方法效果的运行波动、检查退步和保留失败成本；不认证代理自修改谱系。

## 使用

```powershell
python tools/sandbox_research_series.py run benchmarks/sandbox-research-comparison-smoke.json --blocks 3 --output runs/evaluation/research-series --dotnet /absolute/path/to/dotnet
python tools/sandbox_research_series.py analyze runs/evaluation/research-series/block-000 runs/evaluation/research-series/block-001 runs/evaluation/research-series/block-002 --output runs/reports/research-series.json
python -m unittest discover -s tools -p test_sandbox_research_series.py
```

输出目录与报告必须是新的。每块使用独立代理调用、编译、世界执行与留出结果；控制器轮换执行顺序，使三个角色依次占据不同位置。只运行一块也能归档，但不能通过方法增益门槛。耗时随块数增加。

## 统计与回归门槛

聚合前重新验证完整研究归档，要求参考源码和研究条件跨块一致。重复目录、解析到相同路径的归档、非有限值及条件变化被拒绝。运行块是置信区间的重采样单位，不把同一块中的世界、episode 重复次数当成独立研究样本。

子减父和子减撤销分别报告逐块节省差异、均值与固定种子 5000 次运行块 bootstrap 区间；研究效率差异使用包含失败与留出验证的控制器成本。每个留出世界同时检查回归：相对控制组的时间节省下降超过 0.05 时列出块、世界和对照组，平均改善不能隐藏它。

`frozenMethodGainSupported` 要求至少三个块、执行顺序位置计数均衡、全部子组留出正确、没有超过容差的逐世界回归，并且两种对照的节省差异与研究效率差异区间下界都大于零。这是探索性的冻结方法门槛；少量运行的 bootstrap 区间不等同于可靠的总体显著性证明。

输出始终保留 `recursiveEvidenceSupported: false`。即使冻结方法门槛通过，也不表示已经观察到 RSI；真实递归改进还需要[自修改协议](rsi-improver.md)中的谱系、方法撤销、后代生成和旧能力保留证据。固定留出世界的重复执行只估计运行波动，不能证明向新任务族泛化。

## 测试与扩展

2026-10-08 本机真实执行三个块、九份 C# 研究归档：三种执行顺序位置均衡，九次预期编译失败计入成本，总控制器时间约 199.80 秒，三个子组留出均正确。相同脚本仍产生五个超过逐世界容差的计时差异，不能解释为代码真实退化；方法增益门槛未通过，递归证据为 false。完整数据位于 `runs/evaluation/real-research-series-null-20261008/series.json`，包含逐块报告、成本、区间和失败归档。图形构建、自检和截图与部分研究测量重叠，可能增加计时波动；该次运行用于空效应控制与集成验证，不用于证明性能收益。

八项单元测试覆盖空效应、平均改善下的单世界退步、正向冻结方法与递归证据的区分、样本不足、错误子组、重复归档、条件变化、确定性 bootstrap、执行顺序失衡、负效率和非有限数。它们使用构造数据检查判分规则，不冒充真实模型能力。

公开 smoke 使用相同的三个非 LLM 研究脚本，作为真实工程空效应控制。接入模型时可替换配置中的三个研究适配器，保持共享世界、测试和预算条件；外部模型记忆、token/算力预算和操作系统隔离不由此工具保证。

---

[文档导航](../README.md) · [真实源码研究](sandbox-research.md) · [冻结研究对照](sandbox-research-comparison.md)
