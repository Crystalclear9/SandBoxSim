# 项目文档

文档描述河山当前的游戏、在线控制和研究工具。所有命令默认从仓库根目录运行：PowerShell 入口使用 PowerShell 7，Python 工具使用 Python 3.10+。历史设计单独保存在 [归档](archive/README.md)。

## 按用途阅读

| 用途 | 文档入口 |
|---|---|
| 安装和游玩 | [安装与启动](guides/getting-started.md) → [玩家指南](guides/player-guide.md) → [观察与界面](guides/gameplay-observation.md) |
| 自由探索 | [荒野探索](guides/exploration.md) → [世界系统](guides/world-systems.md) → [地形与活动痕迹](guides/terrain-and-traces.md) |
| 理解居民与生态 | [聚落与居民](guides/settlements.md)、[天气与生态](guides/ecology.md) |
| 开发模拟和客户端 | [开发指南](development/developer-guide.md) → [项目架构](development/architecture.md) → [扩展指南](development/extending.md) |
| 修改模型和材质 | [模型系统](development/models.md)、[美术资源](development/assets.md)、[正式配图](images/README.md) |
| 构建、配置和存档 | [构建与运行](development/build.md)、[配置](development/configuration.md)、[存档与确定性](development/saving.md) |
| 性能和视觉实验 | [性能维护](development/performance.md)、[渲染与视觉评测](development/benchmark.md) |
| 在线控制和程序效率 | [HTTP 服务与效率实验](development/online-efficiency.md) |
| 代理自我改进实验 | [代理改进协议与三种后端](development/rsi-improver.md) |
| 更新和排错 | [版本更新](guides/updating.md)、[故障排查](guides/troubleshooting.md) |
| 管理文件和贡献 | [目录与文件管理](development/repository-layout.md)、[贡献指南](../CONTRIBUTING.md) |

## 运行入口

| 入口 | 所需环境 | 作用 |
|---|---|---|
| `tools/godot.ps1 -Mode run` | PowerShell 7、.NET 8 SDK、Godot 4.7.2 .NET | 3D 自由沙盒与客户端荒野作用 |
| `tools/run.ps1` | PowerShell 7、.NET 8 工具链 | 独立内核 TUI、无人干预运行和摘要 |
| `tools/online.py` | Python、已构建的 .NET 8 Console | 调用者控制的 loopback HTTP 会话 |
| `tools/evaluate.py` | Python、Godot .NET 客户端 | 图像、界面回放、渲染墙钟采样 |
| `tools/efficiency.py` | Python、两份在线服务 | 正确性约束下的程序性能对照 |
| `tools/rsi_benchmark.py` | Python，HTTP 后端另需在线服务 | 代理自修改、迁移、后代分叉和方法撤销 |

HTTP 服务不输出实时 3D 图像；Console 和 HTTP 不执行图形会话的额外荒野日界作用。游戏没有研究任务流程。程序加速、视觉识别与代理改进是不同实验，协议及证据不能混用。

## 文件位置与维护

`guides/` 是使用说明，`development/` 是开发与协议说明，`images/` 是正式配图，`archive/` 是有历史身份标识的原始资料。脚本索引见 [tools](../tools/README.md)，源码入口见 [src](../src/README.md)，默认配置见 [config](../config/README.md)，公开实验样例见 [benchmarks](../benchmarks/README.md)，本机输出见 [runs](../runs/README.md)。

修改文档后执行 `./tools/check-docs.ps1` 和 `git diff --check`。检查器核对本地文件链接和大小写；外部网址、页内锚点及命令语义需要另外核对。历史说明保留当时的事实，不改写为当前功能；新增功能进入对应现行主题与 [更新记录](../CHANGELOG.md)。
