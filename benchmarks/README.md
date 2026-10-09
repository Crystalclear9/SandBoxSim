# 公开实验与协议

这里保存可复现的开发场景、Schema、API 定义和非 LLM 代理夹具。正式研究的留出任务与答案由评测者在仓库外保管；公开样例不构成无泄漏排行榜。

## 文件分组

| 位置 | 内容 | 运行入口 |
|---|---|---|
| `scenarios/` | 固定人口渲染、建筑、人物、面部、手部和界面回放 | `tools/evaluate.py` |
| `request.schema.json`、`predictions.schema.json`、`result.schema.json` | Godot 渲染请求、视觉预测和结果协议 v1 | `tools/evaluate.py describe` |
| `interface.json` | 可查询的能力与运行边界 | 渲染接口能力清单 |
| `online.openapi.json` | loopback HTTP 固定步进和只读查询 | `tools/online.py` |
| `efficiency-smoke.json` | 真实程序效率配对样例 | `tools/efficiency.py run` |
| `rsi-rounds.schema.json` | 程序性能历史与父链清单，保留历史文件名 | `tools/efficiency.py experiment/rounds` |
| `rsi-meta.schema.json`、`rsi-meta-smoke.json` | 代理自修改、留出迁移、分叉及方法撤销 | `tools/rsi_benchmark.py` |
| `sandbox-research-smoke.json` | 真实二叉堆源码实验、编译失败反馈及完整世界测量 | `tools/sandbox_research.py` |
| `rsi-budget-smoke.json` | 多尝试短预算非 LLM 控制实验 | `tools/rsi_efficiency_study.py` 分析归档 |
| `rsi-stateful-smoke.json` | 连续世界代码任务、两阶段课程与旧能力保留 | `tools/rsi_benchmark.py` 的 code 后端 |
| `rsi-code-smoke.json` | 实际 Python 候选效率与改进控制 | `tools/rsi_benchmark.py` 的 code 后端 |
| `agents/` | 策略和实际代码生成的非 LLM 集成夹具 | 代理协议示例 |

## 渲染样例

| 场景 | 内容 |
|---|---|
| `render-population` | 300 人、预演 3 天、每帧 2 tick，无截图，30 FPS 墙钟门槛 |
| `visual-architecture` | 五种住房与仓库的无标题裁切图与对象识别题 |
| `visual-characters` | 成人、儿童、鹿和狼的对象识别 |
| `visual-faces` | 面部近景，供人工表面检查 |
| `visual-hands` | 锤、斧、锄的两次握持姿态 |
| `ui-modern-observation` | 自由运行 14 日后的新 UI、面部/手部/全身构图与设置交互 |
| `world-living-paths` | 自由运行 14 日，聚焦实际踩踏最多的土地；不预画道路 |
| `world-living-homes` | 自由运行 14 日，选择实际入住人数最多的完工住房，回放近景按钮和住户链接，分别截取房屋与人物页 |
| `world-living-farms` | 150 人自由运行 14 日，聚焦真实完工农田，显示作物、劳动与地力读数 |
| `ui-portrait` | 人物构图、俯仰、复位和面板关闭顺序 |

```powershell
./tools/godot.ps1 -Mode build
python tools/evaluate.py run benchmarks/scenarios/visual-characters.json --output runs/evaluation/visual-example --audio-driver Dummy --max-fps 60
python tools/evaluate.py verify runs/evaluation/visual-example
```

渲染需要已构建的 Godot .NET 客户端与原生图形设备；纯 HTTP 和代理内核/代码后端不需要 Godot。各测量口径见 [渲染接口](../docs/development/benchmark.md)、[在线效率](../docs/development/online-efficiency.md)、[代理改进协议](../docs/development/rsi-improver.md)。

连续世界的运行与 HTML 报告见 [课程与代理实验](../docs/development/stateful-rsi.md)。报告先回放验证，不覆盖既有文件，不向代理暴露留出题目。

所有结果、失败记录、日志和临时图像保存在 `runs/` 的独立目录，不能覆盖或拆分具有产物哈希的实验目录。夹具通过只说明集成流程有效，不代表真实模型已展示 RSI。

`sandbox-research-comparison-smoke.json` 使用相同脚本的父/子/撤销三组空效应控制，运行方法与解释见 [真实研究对照](../docs/development/sandbox-research-comparison.md)。

同一配置可通过 `tools/sandbox_research_series.py` 进行多块重复对照，轮换顺序并检查逐世界退步、运行块区间和完整成本，见 [重复研究对照](../docs/development/sandbox-research-series.md)。冻结方法通过不等于递归自改进证据。

`sandbox-living-economy-smoke.json` 在真实源码研究中保护农业周期、地力、库存腐损和保存恢复语义，三个开发与三个留出世界跨越日界。适配器仍是非 LLM 夹具，不代表递归改进证据。

`scenarios/visual-model-details.json` 提供衣料、靴子、鹿、狼和两类住房的实际共享网格近景，语义区域标注居民、鹿、狼及住房。它用于检查几何与材质表现，不包含专用展示模型或模拟任务。

`scenarios/visual-wardrobe.json` 用同一身份的实际职业网格展示三种剪裁的正背面，便于观察衣身轮廓、卷袖、背包和下摆。模型使用世界共用网格，图册不实例化模拟居民。

`scenarios/visual-ecology-details.json` 展示与世界共用的阔叶树、针叶树、蕨类、草叶、岩石和灌木，提供 `tree`、`fern`、`grass`、`rock`、`shrub` 标签与无标题裁切。新标签追加到公开对象选项，历史问题保留各自已归档的选项。
