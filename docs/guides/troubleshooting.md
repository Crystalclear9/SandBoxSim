# 故障排查

先区分环境、构建、资源导入和游戏规则问题。所有命令从仓库根目录运行；保留首条错误及其前后的日志，后续连锁报错通常无法单独定位原因。

## 环境与构建

| 现象 | 处理 |
|---|---|
| `pwsh` 无法运行 | 安装或使用 PowerShell 7；`Play-3D.cmd` 调用的是 `pwsh.exe` |
| 找不到 .NET SDK | 运行 `tools/install-sdk.ps1`，或将 `SANDBOXSIM_DOTNET_ROOT` 设置为本机 SDK 根目录 |
| Linux/macOS 报 `Cannot find drive C` | 检查旧脚本或残留环境变量中的 Windows 路径；使用当前构建脚本和本机 SDK 目录 |
| 找不到 Godot | 设置 `GODOT_EXE` 或 `-GodotPath` 为 .NET 编辑器可执行文件，而不是目录或 ZIP |
| Godot 项目无法构建 C# | 确认是 Godot 4.7.2 .NET，且能找到 .NET 8 SDK；普通编辑器不包含 C# 支持 |
| 并行构建在受限环境失败 | 去掉 `-ParallelBuild`，以串行构建确认错误；保留构建日志 |
| 改代码后编辑器仍显示旧行为 | 用 `tools/godot.ps1 -Mode run` 或 `-Mode test` 构建 Debug；仅 Release 构建不足以更新编辑器 |
| 图标、纹理或脚本引用缺失 | 检查 `res://` 路径、文件大小写、`.uid` 和 `.import`；通过 Godot 脚本重新导入 |

日志可以这样保存：

```powershell
New-Item -ItemType Directory -Path runs/logs -Force | Out-Null
./tools/godot.ps1 -Mode test *> runs/logs/godot-test.log
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk *> runs/logs/core-build.log
```

SDK 路径应指向含 `sdk/` 与 dotnet 主机的根目录。引擎路径应指向可执行文件。安装位置及跨平台设置见 [安装与启动](getting-started.md)。

## 游戏行为

| 现象 | 先检查 |
|---|---|
| 时间或工程没有变化 | 是否暂停；分阶段工程和蓝图需要跨过游戏日界 |
| 顶部有食物，某个居民仍饥饿 | 储备的可达性、分布、个人库存与采集/进食行动 |
| 地表粮食很多，顶部储备为零 | 顶部只统计已采集物资，地表节点仍需采集 |
| 建筑没有开工 | 八格内可达成年居民、材料、地形要求和状态提示 |
| 修改配方后旧档没有变化 | 旧档嵌入原配方；新世界才读取新的外部 JSON |
| 蓝图没有完成 | 统计范围、实际完工建筑/人口/道路，以及连续两个日界的稳定条件 |
| 工具使用后无法观察平移 | Esc 回到观察模式；绘制工具的左键保留连续干预，中键可以平移 |
| 人物定位到另一个居民 | 开发时检查是否把实体槽位当成稳定人物编号缓存 |

建造条件见 [聚落营造](settlements.md)，工程条件见 [生态工坊](ecology.md)，模拟与会话边界见 [架构](../development/architecture.md)。

## 存档问题

先复制原存档，再尝试恢复。核心存档版本不匹配会明确拒绝载入；不要覆盖或手改原档。只看核心摘要无法判断图形工程与蓝图的续跑是否一致，需要同时检查客户端元数据。Console 只继续核心模拟，不继续执行图形会话的工程、试炼和蓝图。

相关规则见 [存档与确定性](../development/saving.md) 与 [版本更新](updating.md)。

## 提供可复现的信息

记录操作系统、PowerShell、SDK、Godot 版本与 Git 提交，再说明种子、初始人数、场景、速度、工具和落点。列出预期结果与实际结果，附必要日志、存档副本或临时截图。截图放在 `runs/screenshots/`，不要为了排查替换正式文档配图。

```powershell
pwsh --version
git rev-parse --short HEAD
git status --short
```

工具链完整路径属于本机设置；提交说明时不需要公开用户目录或其他私人文件。
