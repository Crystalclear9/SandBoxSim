# SandBoxSim 项目文档

文档描述 SandBoxSim 当前的游戏、在线控制和研究工具。命令默认从仓库根目录运行，PowerShell 入口使用 PowerShell 7，Python 工具使用 Python 3.10+。原始任务书和素材来源单独保存在 [来源资料](archive/README.md)。

## 当前实现

| 领域 | 现在可以观察或使用的内容 |
|---|---|
| 自由沙盒 | 通过地貌、资源与生态工具改变世界，观察天气、火灾、踩踏、植被恢复，以及居民自主形成的聚落和生活经济。见 [玩家指南](guides/player-guide.md) 与 [世界系统](guides/world-systems.md) |
| 人物与动作 | 成人采用来源体型、连续衣装和肩肘腕解算。实际位移驱动行走，同速度下的不同外观可呈现不同步幅、节奏和转身方式。作业与徒手交互经过接近、停稳、动作和回收，个人状态和能力决定作业节奏。见 [3D 模型系统](development/models.md) |
| 林地、山坡与河岸 | 场景树石保留通路净空，坡面碎石沿地势分布，草丛对近处人物和动物活动产生弯折反馈。伐木与采石使用同格站位，鹿狼按足部跨度寻找岸边支撑，饮水活动产生短时涟漪。见 [地形与场景接触](guides/terrain-and-traces.md) |
| 观察与回放 | 世界手记、图层和可搜索居民名录读取实际模拟状态，人物窗可查看动作、需求、物资与目的地。公开场景可复现原生图像、界面操作和性能样本。见 [观察与界面](guides/gameplay-observation.md) 与 [视觉评测](development/benchmark.md) |
| 在线与源码研究 | 命令行和 HTTP 提供内核模拟与固定步进，研究工具提供同状态下的程序对照、源码候选编译与留出世界，以及改进器的分叉和撤销对照。见 [在线控制](development/online-efficiency.md) 与 [真实源码研究](development/sandbox-research.md) |

林地与山坡的绕行覆盖普通树干与山石的水平包络。草叶弯折和饮水涟漪是显示反馈，长期压实由实际模拟活动累积。没有可用局部绕行点时，角色可能短跳到逻辑目标。见 [场景交互边界](guides/terrain-and-traces.md)。

程序性能对照、视觉评测和代理改进分别使用各自的协议。公开夹具验证接口与实验设计，真实模型的改进能力仍需在独立留出任务中测量。见 [代理改进协议](development/rsi-improver.md) 与 [更新记录](../CHANGELOG.md)。

## 按用途阅读

| 用途 | 文档入口 |
|---|---|
| 安装和游玩 | [安装与启动](guides/getting-started.md) → [玩家指南](guides/player-guide.md) → [观察与界面](guides/gameplay-observation.md) |
| 自由探索 | [荒野探索](guides/exploration.md) → [世界系统](guides/world-systems.md) → [地形与活动痕迹](guides/terrain-and-traces.md) |
| 理解居民与生态 | [聚落与居民](guides/settlements.md)、[天气与生态](guides/ecology.md) |
| 开发模拟和客户端 | [开发指南](development/developer-guide.md) → [项目架构](development/architecture.md) → [扩展指南](development/extending.md) |
| 修改模型和材质 | [模型系统](development/models.md)、[人物素材转换](development/human-assets.md)、[环境素材转换](development/environment-assets.md)、[美术资源](development/assets.md)、[正式配图](images/README.md) |
| 构建、配置和存档 | [构建与运行](development/build.md)、[配置](development/configuration.md)、[存档与确定性](development/saving.md) |
| 性能和视觉实验 | [性能维护](development/performance.md)、[渲染与视觉评测](development/benchmark.md) |
| 在线控制和程序效率 | [HTTP 服务与效率实验](development/online-efficiency.md) |
| 在真实工程上研究代码 | [真实沙盒源码研究](development/sandbox-research.md) |
| 理解农业与长期土地变化 | [农业、地力与食物流动](guides/living-economy.md) |
| 比较重复研究效果与退步 | [真实源码研究的重复对照](development/sandbox-research-series.md) |
| 代理自我改进实验 | [代理改进协议与三种后端](development/rsi-improver.md)、[连续世界课程与实验报告](development/stateful-rsi.md)、[改进器效率与成本回收](development/rsi-efficiency.md) |
| 更新和排错 | [版本更新](guides/updating.md)、[故障排查](guides/troubleshooting.md) |
| 管理文件和贡献 | [目录与文件管理](development/repository-layout.md)、[本机存储维护](development/local-storage.md)、[贡献指南](../CONTRIBUTING.md) |

## 运行入口

| 入口 | 所需环境 | 作用 |
|---|---|---|
| `Play-3D.cmd` | Windows，已有完整独立游戏包 | 优先启动 `artifacts/game/windows/` 的成品；没有成品时使用源码工具链 |
| `tools/godot.ps1 -Mode run` | PowerShell 7、.NET 8 SDK、Godot 4.7.2 .NET | 3D 自由沙盒与客户端荒野作用 |
| `tools/run.ps1` | PowerShell 7、.NET 8 工具链 | 独立内核 TUI、无人干预运行和摘要 |
| `tools/online.py` | Python、已构建的 .NET 8 Console | 调用者控制的 loopback HTTP 会话 |
| `tools/evaluate.py` | Python、Godot .NET 客户端 | 图像、界面回放、渲染墙钟采样 |
| `tools/efficiency.py` | Python、两份在线服务 | 正确性约束下的程序性能对照 |
| `tools/rsi_benchmark.py` | Python，HTTP 后端另需在线服务 | 代理自修改、迁移、后代分叉和方法撤销 |
| `tools/rsi_efficiency_study.py` | Python、已完成的代理实验归档 | 实际时间预算、后代收益率与乐观成本回收分析 |
| `tools/sandbox_research_compare.py` | Python、三个真实源码研究归档或 .NET SDK | [冻结父/子/撤销程序对照](development/sandbox-research-comparison.md) |
| `tools/sandbox_research.py` | Python、Git、.NET 8 SDK | 源码调查、候选编译与语义检查、真实开发/留出世界 |

HTTP 和 Console 推进独立内核世界。额外的荒野日界作用由图形会话管理，3D 截图通过 Godot 评测入口获取。正式配图的场景、帧号与版本见 [配图说明](images/README.md)。

## 文件位置与维护

`guides/` 是使用说明，`development/` 是开发与协议说明，`images/` 是正式配图，`archive/` 只保留原始任务书与素材生成来源。脚本索引见 [tools](../tools/README.md)，源码入口见 [src](../src/README.md)，默认配置见 [config](../config/README.md)，公开实验样例见 [benchmarks](../benchmarks/README.md)，本机输出见 [runs](../runs/README.md)。

修改文档后执行 `./tools/check-docs.ps1` 和 `git diff --check`。检查器核对本地文件链接和大小写；外部网址、页内锚点及命令语义需要另外核对。历史说明保留当时的事实，不改写为当前功能；新增功能进入对应现行主题与 [更新记录](../CHANGELOG.md)。
