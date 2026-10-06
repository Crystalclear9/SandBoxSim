# 项目文档

这里说明当前项目怎样使用、怎样修改和怎样维护。命令默认在仓库根目录通过 PowerShell 7 执行；图形世界与命令行模拟的区别在各主题中注明。

## 从你的任务开始

| 任务 | 阅读顺序 |
|---|---|
| 第一次运行游戏 | [安装与启动](guides/getting-started.md) → [玩家指南](guides/player-guide.md) |
| 理解居民和世界 | [自由探索](guides/exploration.md) | 活地貌、生长周期、遗迹物资与自选地点手记 |
| [世界系统](guides/world-systems.md) → [聚落营造](guides/settlements.md) → [生态工坊](guides/ecology.md) |
| 修改界面或模型 | [开发指南](development/developer-guide.md) → [架构](development/architecture.md) → [扩展指南](development/extending.md) → [素材](development/assets.md) |
| 修改模拟规则 | [开发指南](development/developer-guide.md) → [配置](development/configuration.md) → [存档与确定性](development/saving.md) |
| 更新项目或排错 | [版本更新](guides/updating.md)、[故障排查](guides/troubleshooting.md) |

## 使用指南 · guides

| 文档 | 内容 |
|---|---|
| [安装与启动](guides/getting-started.md) | 环境、工具链位置与三平台源码启动 |
| [玩家指南](guides/player-guide.md) | 镜头、时间、人物、工具、工程、试炼与保存 |
| [自由探索](guides/exploration.md) | 活地貌、生长周期、遗迹物资与自选地点手记 |
| [世界系统](guides/world-systems.md) | 地形、资源、需求、生态与社会的因果关系 |
| [聚落营造](guides/settlements.md) | 房屋、材料施工、建筑卡片与蓝图 |
| [生态工坊](guides/ecology.md) | 土地工程、维护、费用及 JSON 配方 |
| [观察与界面](guides/gameplay-observation.md) | HUD、肖像、建筑预览、图层和正式配图 |
| [故障排查](guides/troubleshooting.md) | 工具链、构建、导入、存档及玩法问题 |
| [版本更新与兼容](guides/updating.md) | 更新步骤、参数、存档与资源兼容 |

## 开发维护 · development

| 文档 | 内容 |
|---|---|
| [开发指南](development/developer-guide.md) | 首次构建、源码入口、日常修改和验证选择 |
| [项目架构](development/architecture.md) | 依赖关系、tick、日界、会话与只读观察 |
| [扩展指南](development/extending.md) | 配方、动作、建筑、AI、界面和模型扩展 |
| [目录与文件管理](development/repository-layout.md) | 文件职责、提交范围、移动与清理规则 |
| [构建与运行](development/build.md) | SDK/csc、命令行模式、测试和 CI |
| [配置](development/configuration.md) | 默认参数、客户端设置与工程数据 |
| [存档与确定性](development/saving.md) | 格式版本、恢复、客户端元数据和续跑 |
| [美术资源](development/assets.md) | 来源、图集、模型、材质与导入约定 |

[更新记录](../CHANGELOG.md) 记录用户可见变化和维护变化；[贡献指南](../CONTRIBUTING.md) 说明提交约定。正式配图保留在 `images/`，历史资料在 [archive](archive/README.md)。历史资料中的阶段结论用于理解演变，当前操作以本导航链接的主题文档为准。

修改或移动文档后执行 `./tools/check-docs.ps1`，检查本地文件链接与大小写。它不访问网站，也不检查页内标题锚点。

模型结构、材质和独立预览见 [3D 模型系统](development/models.md)。
