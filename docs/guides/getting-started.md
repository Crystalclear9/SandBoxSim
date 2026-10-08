# 安装与启动

## 环境

图形客户端使用 PowerShell 7、.NET 8 SDK 与 Godot 4.7.2 .NET。Godot 普通版不包含 C# 支持，应使用 .NET 版。项目的图形渲染器为 GL Compatibility。

从仓库根目录执行：

```powershell
./tools/install-sdk.ps1
./tools/setup-godot.ps1
./tools/godot.ps1 -Mode run
```

安装脚本把 SDK 与引擎放在仓库外的用户工具目录中，不把安装文件提交到 Git。`setup-godot.ps1` 根据操作系统与架构选择引擎，并输出可执行文件位置。

Windows 配置完成后，双击 `Play-3D.cmd`。脚本使用 PowerShell 启动图形客户端；首次启动会构建 C# 程序并导入资源。

## 指定已有工具链

```powershell
$env:SANDBOXSIM_DOTNET_ROOT = '/absolute/path/to/dotnet-sdk'
$env:GODOT_EXE = '/absolute/path/to/godot-mono'
./tools/godot.ps1 -Mode run
```

也可以直接传入引擎路径：

```powershell
./tools/godot.ps1 -Mode run -GodotPath '/absolute/path/to/godot-mono'
```

Linux/macOS 使用安装脚本输出的引擎路径。环境变量中的路径必须属于本机；不要把 Windows 盘符路径用于其他操作系统。

## 第一局

进入后可以先暂停。在右上方打开“世界手记”，观察当地状态与世界记录；单击居民查看需求和行为。观察模式下左键拖动平移，右键拖动旋转与俯仰，滚轮缩放。

底部“生命”“地貌”“气候”直接展开自由工具；居民自行建造，世界没有指定发展任务。完整操作见 [玩家指南](player-guide.md)。

## 常见问题

| 现象 | 检查方法 |
|---|---|
| 找不到 PowerShell | 确认 `pwsh` 可运行，启动脚本需要 PowerShell 7 |
| 找不到 SDK | 运行安装脚本，或设置 `SANDBOXSIM_DOTNET_ROOT` 指向 SDK 根目录 |
| 找不到 Godot | 检查 `GODOT_EXE` 或 `-GodotPath` 是否指向实际可执行文件 |
| C# 构建或导入失败 | 运行 `tools/godot.ps1 -Mode test`，查看终端中的首条错误 |
| 编译失败却没有正常错误提示 | Core 构建先不使用 `-ParallelBuild`，以串行模式检查环境 |
| 世界没有继续变化 | 检查时间是否暂停；荒野自然作用需要游戏时间推进 |

命令行模拟可以独立于 Godot 使用，见 [构建与运行](../development/build.md)。

更详细的诊断见 [故障排查](troubleshooting.md)。已有项目升级时参考 [版本更新与兼容](updating.md)，保留个人存档和修改过的参数。

---

[文档导航](../README.md) · [项目首页](../../README.md)
