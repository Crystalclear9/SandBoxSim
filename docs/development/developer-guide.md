# 开发指南

本指南面向接手源码的开发者。先确认改动属于模拟规则、图形会话还是表现层，再选择对应入口；四个工程的依赖关系见 [项目架构](architecture.md)。

## 建立开发环境

按 [安装与启动](../guides/getting-started.md) 配置 PowerShell 7、.NET 8 SDK 与 Godot 4.7.2 .NET。从仓库根目录执行：

```powershell
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
./tools/godot.ps1 -Mode build -Configuration Release
./tools/godot.ps1 -Mode test
./tools/check-docs.ps1
```

`SandBoxSim.sln` 只包括 Core、Console 与 Tests，便于独立构建模拟内核；图形工程位于 `src/SandBoxSim.Godot/SandBoxSim.Godot.csproj`。`tools/godot.ps1 -Mode editor` 打开正确的 Godot 工程，`-Mode run` 构建、导入后运行。运行、编辑器和自检均使用 Debug 程序集，Release 主要用于构建与导出准备。

`tools/dev.ps1` 是内核与 Console 的构建、测试、TUI 流程。它不会启动 Godot；只检查内核时可加 `-SkipRun`。

## 选择修改入口

以下路径相对于仓库根目录：

| 需求 | 先读或修改 | 配套检查 |
|---|---|---|
| 需求增长、居民决策与劳动 | `Core/Systems/NeedsSystem.cs`、`AiSystem.cs`、`ActionSystem.cs` | 生存行为、同种子重放与续跑 |
| 施工、材料和建筑类型 | `Core/World/BuildingStore.cs`、`Core/Systems/BuildingSystem.cs`、`ConstructionOrders.cs` | 扣料、拒绝落点、床位/生产与存档 |
| 自然地点与日界作用 | `Core/Systems/WildPlaces.cs`、`Godot/MainGame.Play.cs` | 地理生成、生态条件、有限资源与恢复 |
| 新建图形世界的起始条件 | `Core/Systems/SandboxScenarios.cs`、`Godot/MainGame.cs` | 新世界与旧存档分开检查 |
| 主界面、控制栏与档案 | `Godot/MainGame.Interface.cs`、`HudStyle.cs`、`HudSymbols.cs` | 自检、实际窗口与鼠标遮挡 |
| 3D 镜头和地表 | `Godot/WorldView3D.cs`、`WorldView3D.Atmosphere.cs` | 拖动、缩放、释放、图层与暂停 |
| 居民动作与握持 | `Godot/ResidentRig.cs`、`ResidentRig.HandPose.cs`、`NatureModels.HandSkin.cs`、`NatureModels.Face.cs` | 工具取放、手部表面、肘腕范围、近远景与暂停 |
| 建筑和人物外观 | `Godot/NatureModels*.cs`、`BuildingPortrait.cs`、`ResidentPortrait.cs` | 近远景、变体、动作与显示缓存 |
| 自由探索界面 | `Godot/MainGame.Interface.cs`、`MainGame.Notebook.cs` | 阅读层次、直接工具与无目标导航 |
| 跨平台工具链 | `tools/build-lib.ps1` 与具体入口脚本 | 脚本回归和受影响构建通道 |

表中 `Core/`、`Godot/` 分别缩写 `src/SandBoxSim.Core/`、`src/SandBoxSim.Godot/`。实体槽位可以复用；人物关注、命名和关系应使用稳定身份，不能缓存槽位作为永久人物标识。

## 日常修改流程

1. 用固定种子和明确操作复现问题，记下暂停/速度、工具、落点与预期行为。
2. 修改负责该状态的层。图形按钮调用正式干预或会话方法，观察与动画保持只读。
3. 运行与改动有关的检查，再运行下表要求的整体检查。
4. 更新玩家能感知的说明、配置或存档契约，并在根目录 `CHANGELOG.md` 记录结果。
5. 查看 `git status --short` 与 `git diff --check`，提交源码、资源和文档；日志、个人存档与临时截图留在 `runs/`。

模拟随机性使用内核随机流；不要让 UI、光照、模型变体消耗它。荒野自然作用通过 `MainGame.AdvanceWorld` 的日界推进，直接调用 `Simulation.Tick` 只推进内核。

## 按改动选择检查

| 改动 | 运行入口 |
|---|---|
| 模拟规则、状态或持久化 | 相关名称过滤测试，然后 `./tools/test.ps1 -Configuration Release -Channel sdk` 完整回归 |
| Godot UI、镜头或模型 | `./tools/godot.ps1 -Mode test`、Release 构建和实际窗口检查 |
| 荒野自然作用与会话 | `WildPlaces` 检查，加 Godot 自检与存读档 |
| 构建脚本 | `./tools/test-build.ps1`，加受影响的 SDK/csc 构建 |
| 文档或文件位置 | `./tools/check-docs.ps1`、`git diff --check`；资源移动另做 Godot 导入 |

例如：

```powershell
./tools/test.ps1 -Filter WildPlaces -Configuration Release -Channel sdk
./tools/test.ps1 -Filter SaveLoad -Configuration Release -Channel sdk
./tools/test.ps1 -Filter Building -Configuration Release -Channel sdk
```

自带测试运行器使用 `[Fact]`、`[Theory]` 和名称过滤。`dotnet test` 不是本项目测试入口，构建成功也不表示测试已执行。不要只比较 `CoreDigest` 来判断图形会话续跑：还要比较荒野地点、日界与关注人物的元数据，见 [存档与确定性](saving.md)。

## 保存运行资料

人工日志放在 `runs/logs/`，临时画面放在 `runs/screenshots/`，专题模拟放在独立子目录。正式配图放在 `docs/images/`，游戏资源放在客户端 `assets/`。清理调试截图时只处理明确的临时文件，保留配图、素材、日志和世界存档，见 [文件管理](repository-layout.md)。

新增内容的具体接入顺序见 [扩展指南](extending.md)，发布前更新环境、参数和存档说明见 [版本更新](../guides/updating.md)。
