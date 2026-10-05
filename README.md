# 河山 · SandBoxSim
一个观察自然与文明的 3D 沙盒模拟游戏。你改变地形、气候和资源，居民自主生存、建造与迁移；每次选择都会留下新的条件与故事。

![当前实际游戏界面](docs/images/ecology-workshop.png)

## 开始游玩
Windows 已配置环境时，双击 Play-3D.cmd。开发环境需要 PowerShell 7、.NET 8 SDK 与 Godot 4.7.2 .NET。

    ./tools/install-sdk.ps1
    ./tools/setup-godot.ps1
    ./tools/godot.ps1 -Mode run

Linux/macOS 将 setup-godot 输出的引擎路径设为 GODOT_EXE，或通过 godot.ps1 的 GodotPath 参数传入。

暂停世界，点击右上方现场提醒或世界手记，查看小地图与风险。选择一个居民，给他起名并关注他。随后调整条件、安排土地工程或在“聚落营造”中委托建造，再观察变化。观察模式左键拖动平移、右键拖动旋转与俯仰、滚轮缩放；单击选择或确认落点。

## 你可以做什么
- 创造居民与动物，调整土地、水源和资源，施加祝福或灾害。
- 规划六种土地工程，组合水土、森林、粮地和通道；完成后选择自然、维护或采集，观察持续影响。
- 指定住房、仓库、农田和矿场落点，使用真实材料施工；尝试河畔农庄、林间驿站和集市小镇蓝图。
- 关注一个人的需求、决策、住址和人生经历，回看世界历史与每日曲线。
- 在自由沙盒中创造，或挑战有限干预额度的旱季、火情和疫病试炼。
- 保存世界与工程进度，记录起点，回溯尝试不同策略。

## 阅读文档
| 内容 | 文档 |
|---|---|
| 按键、工具、工程、试炼与保存 | [玩家手册](docs/24-PlayerHandbook.md) |
| 资源、需求、生态、聚落如何互相影响 | [世界如何运转](docs/26-WorldGuide.md) |
| 组合策略、持续管理与 JSON 配方扩展 | [生态工坊](docs/28-EcologyWorkshop.md) |
| 不同住房、建造委托、聚落蓝图与鼠标镜头 | [聚落营造](docs/29-SettlementPlay.md) |
| 控制栏、手记、信息层次与视觉风格 | [界面与交互设计](docs/27-InterfaceIteration.md) |
| 源码分层、状态、存档与扩展 | [开发指南](docs/25-DeveloperGuide.md) |
| 设计资料与历史记录 | [文档地图](docs/00-Index.md) |
| 图集、素材来源与导入设置 | [美术资源](docs/22-ArtAssets.md) |

## 开发
Core 内核、Console 和测试运行器独立于 Godot，保留 SDK / csc 构建。Godot 负责 3D 表现、输入与客户端会话。

    ./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
    ./tools/godot.ps1 -Mode build -Configuration Release

src 放源码，tools 放安装和构建脚本，docs 放设计与使用文档，runs 放本机临时输出。引擎与 SDK 安装在仓库外。

当前为开发版，部分环境和建筑使用程序生成美术，独立发布包仍在开发。最新代码通过 [PR #1](https://github.com/Crystalclear9/SandBoxSim/pull/1) 提供。
