# 工具入口

从仓库根目录运行。PowerShell 脚本使用 PowerShell 7；Python 工具使用 Python 3.10+ 标准库。子目录位置固定，命令中的 `tools/` 路径与 CI 保持一致。

## 安装、运行与构建

| 文件 | 用途 |
|---|---|
| `install-sdk.ps1` | 安装 .NET 8 SDK 到仓库外的用户工具目录 |
| `setup-godot.ps1` | 按平台安装并校验 Godot 4.7.2 .NET 编辑器 |
| `build.ps1` / `build-lib.ps1` | Core、Console、Tests 的 SDK/csc 构建与执行 |
| `godot.ps1` / `godot-lib.ps1` | Godot 独立构建、导入、运行、编辑器与自检 |
| `run.ps1` | TUI、headless、digest、snapshot 和 batch 模式 |
| `test.ps1` | 自带测试运行器与名称过滤 |
| `dev.ps1` | 内核构建、测试与可选 TUI 开发流程 |
| `check-docs.ps1` | 本地 Markdown 文件链接与大小写检查 |
| `test-build.ps1` / `test-godot-path.ps1` | 构建探测与平台引擎路径回归 |

```powershell
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
./tools/godot.ps1 -Mode run
./tools/check-docs.ps1
```

SDK 与 Godot 工具链由参数或环境变量覆盖，不提交本机绝对路径。详细参数见 [构建与运行](../docs/development/build.md)。

## 模型素材转换

`export_human_head.py` 仅由 Blender 后台执行，用于重建已授权的头脸数据。普通游戏构建不依赖 Blender；原文件校验、转换参数与署名要求见 [人物头脸素材](../docs/development/human-assets.md)。

`export_environment_models.py` 在 Blender 后台转换五种 CC0 环境模型，按完整叶片抽样并独立简化木质表面。来源、原文件哈希、贴图及运行路径见 [环境素材](../docs/development/environment-assets.md)。

## 实验与接口

| 文件 | 用途与依赖 |
|---|---|
| `online.py` | 启动已构建的 Console HTTP 服务，以及固定步进 Python 客户端 |
| `evaluate.py` | Godot 场景运行、产物验证、视觉评分和渲染配对 |
| `efficiency.py` | 真实 HTTP 程序性能对照、留出套件及性能历史 |
| `observe_benchmark.py` | 完整观察工作负载的程序集与客户端配对 |
| `sandbox_research.py` / `sandbox_research_report.py` | 真实 C# 源码研究、编译、语义检查、开发/留出世界与 diff 报告；需要 Git、.NET 8 SDK |
| `test_sandbox_research.py` | 修改范围、源码哈希、事务性编辑与种子隔离回归 |
| `rsi_benchmark.py` | 代理自修改、迁移、冻结对照、后代分叉与方法撤销 |
| `rsi_stateful.py` | 库存、带权道路、实体世代事件流与独立参考 |
| `rsi_report.py` | 回放验证后的自包含 HTML 证据报告 |
| `rsi_efficiency_study.py` / `test_rsi_efficiency_study.py` | 时间预算、后代收益率、部署回收与拒绝推断回归 |
| `test_rsi_stateful.py` | 连续状态、缓存失效、正确性保留与任务规模回归 |
| `rsi_tasks.py` | 内核策略后端的独立任务、答案与工作量 |
| `rsi_code.py` / `rsi_code_worker.py` | 实际 Python 候选的正确性、CPU/墙钟和分配峰值测量 |
| `sandbox_research_compare.py` / `test_sandbox_research_compare.py` | [真实沙盒中冻结父/子/撤销程序对照](../docs/development/sandbox-research-comparison.md)，保留失败与成本 |
| `sandbox_research_series.py` / `test_sandbox_research_series.py` | [重复真实研究对照](../docs/development/sandbox-research-series.md)，运行块区间、顺序轮换与逐世界回归检查 |
| `godot-build-manifest.ps1` | 图形构建的源码与程序集绑定，供原生评测核对 |
| `test_evaluate.py`、`test_efficiency.py` | 渲染协议与程序效率回归 |
| `test_rsi_benchmark.py`、`test_rsi_code.py` | 代理证据与代码后端回归 |
| `test_online_service.py` | 启动两份真实 .NET 服务检查 API 与配对证据 |

配置与 Schema 在 [benchmarks](../benchmarks/README.md)，结果写入 [runs](../runs/README.md)。代理和候选执行仅面向可信本地代码，工具不提供操作系统安全隔离；完整契约见 [代理协议](../docs/development/rsi-improver.md)。

按入口选择说明：[渲染评测](../docs/development/benchmark.md)、[真实程序效率](../docs/development/online-efficiency.md)、[连续世界代理实验](../docs/development/stateful-rsi.md)、[改进器成本研究](../docs/development/rsi-efficiency.md)、[真实工程源码研究](../docs/development/sandbox-research.md)。源码研究不会自动采用候选，成本报告不会把流程通过转成真实模型 RSI 结果。

修改工具时同步对应协议、样例、文档和检查。文档整理不要求重跑耗时模拟或改动判分器；源码与测量范围改变后须执行实际回归和重新采样。
