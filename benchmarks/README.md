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
| `ui-portrait` | 人物构图、俯仰、复位和面板关闭顺序 |

```powershell
python tools/evaluate.py run benchmarks/scenarios/visual-characters.json --output runs/evaluation/visual-example --audio-driver Dummy --max-fps 60
python tools/evaluate.py verify runs/evaluation/visual-example
```

渲染需要已构建的 Godot .NET 客户端与原生图形设备；纯 HTTP 和代理内核/代码后端不需要 Godot。各测量口径见 [渲染接口](../docs/development/benchmark.md)、[在线效率](../docs/development/online-efficiency.md)、[代理改进协议](../docs/development/rsi-improver.md)。

连续世界的运行与 HTML 报告见 [课程与代理实验](../docs/development/stateful-rsi.md)。报告先回放验证，不覆盖既有文件，不向代理暴露留出题目。

所有结果、失败记录、日志和临时图像保存在 `runs/` 的独立目录，不能覆盖或拆分具有产物哈希的实验目录。夹具通过只说明集成流程有效，不代表真实模型已展示 RSI。
