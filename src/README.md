# 源码入口

四个工程共享 .NET 8 目标；内核保持独立，3D 客户端另由 Godot .NET SDK 构建。

| 工程 | 入口与主要目录 | 构建方式 |
|---|---|---|
| `SandBoxSim.Core` | `Simulation.cs`，`World/`、`Agents/`、`Systems/`、`Pathing/`、`Save/` | SDK 或 Roslyn csc |
| `SandBoxSim.Console` | `Program.cs`，`Online/`、TUI 与输出组件 | SDK 或 Roslyn csc |
| `SandBoxSim.Tests` | 项目自带测试运行器与分主题用例 | `tools/test.ps1` |
| `SandBoxSim.Godot` | `project.godot`，`MainGame*.cs`、`WorldView3D*.cs`、`NatureModels*.cs`、`assets/` | `tools/godot.ps1` |

`SandBoxSim.sln` 包含 Core、Console 和 Tests，Godot 工程单独维护。`bin/`、`obj/` 和 `.godot/` 是本机生成文件，不提交。

## 从功能找到文件

| 功能 | 源码入口 |
|---|---|
| tick、小时和日界 | `SandBoxSim.Core/Simulation.cs` |
| 地形、资源与压实 | `SandBoxSim.Core/World/World.cs`、`Tile.cs`，`ResourceSystem.cs` |
| 居民选择与执行 | `SandBoxSim.Core/Systems/AiSystem.cs`、`ActionSystem.cs` |
| 保存与恢复 | `SandBoxSim.Core/Save/` |
| HTTP 固定步进 | `SandBoxSim.Console/Online/StepApi.cs`、`HttpService.cs` |
| 图形会话与自由工具 | `SandBoxSim.Godot/MainGame.Play.cs`、`MainGame.Interface.cs` |
| 世界手记与观察 | `SandBoxSim.Godot/MainGame.Notebook.cs`、`MainGame.Operations.cs` |
| 镜头、地表和天气 | `SandBoxSim.Godot/WorldView3D.cs`、`WorldView3D.Atmosphere.cs` |
| 建筑增量同步 | `SandBoxSim.Godot/WorldView3D.Buildings.cs` |
| 人物姿态与握持 | `SandBoxSim.Godot/ResidentRig.cs`、`ResidentRig.HandPose.cs` |
| 网格、材质和草丛 | `SandBoxSim.Godot/NatureModels*.cs` |
| 固定场景评测 | `SandBoxSim.Godot/MainGame.Evaluation.cs` |

`ProjectCatalog`、`LandProjects`、`SettlementBlueprint`、`WorldTrial` 等保留历史 SDK 能力；正常图形游戏不加载或推进其任务流程。修改自由探索体验从当前 `MainGame` 与 `WildPlaces` 入口进入。

工程规则见 [项目架构](../docs/development/architecture.md)，日常操作见 [开发指南](../docs/development/developer-guide.md)，文件提交范围见 [目录管理](../docs/development/repository-layout.md)。
