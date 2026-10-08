# 项目文档

文档说明河山当前的使用方式、模拟规则、模型结构与维护方法。命令默认在仓库根目录通过 PowerShell 7 执行。图形世界与命令行模拟的职责不同，各主题会说明对应边界。

## 阅读路径

| 使用场景 | 阅读顺序 |
|---|---|
| 第一次运行 | [安装与启动](guides/getting-started.md) → [玩家指南](guides/player-guide.md) |
| 探索与观察世界 | [自由探索](guides/exploration.md) → [世界系统](guides/world-systems.md) → [聚落与居民](guides/settlements.md) → [天气与生态](guides/ecology.md) |
| 查看人物和界面 | [观察与界面](guides/gameplay-observation.md) → [3D 模型系统](development/models.md) |
| 修改界面或模型 | [开发指南](development/developer-guide.md) → [项目架构](development/architecture.md) → [3D 模型系统](development/models.md) → [美术资源](development/assets.md) |
| 性能优化与模型评测 | [性能维护](development/performance.md) → [评测接口](development/benchmark.md) → [模型系统](development/models.md) |
| 在线控制与效率研究 | [在线控制与效率实验](development/online-efficiency.md) → [评测接口](development/benchmark.md) |
| 修改模拟规则 | [开发指南](development/developer-guide.md) → [扩展指南](development/extending.md) → [配置](development/configuration.md) → [存档与确定性](development/saving.md) |
| 更新与排错 | [版本更新与兼容](guides/updating.md) → [故障排查](guides/troubleshooting.md) |

## 使用指南 · guides

| 文档 | 内容 |
|---|---|
| [安装与启动](guides/getting-started.md) | 环境、工具链位置与三平台源码启动 |
| [玩家指南](guides/player-guide.md) | 鼠标镜头、时间、自由工具、人物观察与保存 |
| [自由探索](guides/exploration.md) | 自然地点、生长周期、有限物资与地点手记 |
| [世界系统](guides/world-systems.md) | 地形、资源、需求、生态与社会的因果关系 |
| [聚落与居民](guides/settlements.md) | 自主施工、材料、住房、职业与社会记录 |
| [天气与生态](guides/ecology.md) | 直接改变水土与天气，观察生态条件和资源变化 |
| [观察与界面](guides/gameplay-observation.md) | HUD、肖像、当前行为、图层与建筑预览 |
| [故障排查](guides/troubleshooting.md) | 工具链、构建、导入、存档及玩法问题 |
| [版本更新与兼容](guides/updating.md) | 更新主分支、有效规则、存档与资源兼容 |

## 开发维护 · development

| 文档 | 内容 |
|---|---|
| [开发指南](development/developer-guide.md) | 环境、源码入口、日常修改与检查选择 |
| [项目架构](development/architecture.md) | 工程依赖、tick、日界、会话与只读表现 |
| [扩展指南](development/extending.md) | 自然内容、动作、建筑、AI、界面与模型扩展 |
| [目录与文件管理](development/repository-layout.md) | 文件职责、提交范围、归档与移动规则 |
| [性能采样与渲染维护](development/performance.md) | 重复采样、CPU 区段、建筑增量同步与网格缓存 |
| [评测接口与实验运行](development/benchmark.md) | 可复现运行、墙钟性能、视觉输入、界面回放与研究边界 |
| [构建与运行](development/build.md) | SDK/csc、命令行模式、测试与 CI |
| [配置](development/configuration.md) | 模拟默认规则、客户端设置与历史数据边界 |
| [存档与确定性](development/saving.md) | 格式版本、恢复、客户端元数据与续跑 |
| [3D 模型系统](development/models.md) | 建筑、人物、手部蒙皮、面部、动物与独立预览 |
| [美术资源](development/assets.md) | 素材来源、图集、模型、材质与导入约定 |

## 文档与图片位置

`guides/` 和 `development/` 描述当前实现；`images/` 保存这些说明使用的正式运行配图。[archive](archive/README.md) 保存原始要求、历史设计与旧界面配图，旧资料中的任务流程和阶段结论不代表当前玩法。临时查看用的截图与录制帧位于仓库的 `runs/screenshots/`，不作为文档素材提交。

[更新记录](../CHANGELOG.md) 记录用户可见变化和维护变化；[贡献指南](../CONTRIBUTING.md) 说明提交约定。修改或移动文档后执行 `./tools/check-docs.ps1` 与 `git diff --check`，核对本地链接、大小写和空白。链接检查不访问外部网站，也不验证页内标题锚点。
