# 16 — 构建修复与交付记录

> 历史设计与验收记录。当前版本操作见 [玩家手册](24-PlayerHandbook.md)，架构见 [开发指南](25-DeveloperGuide.md)，系统玩法见 [世界如何运转](26-WorldGuide.md)。

日期：2026-10-03。本次仅修复构建、验证现有 M3 原型并整理文档，不扩展游戏系统。

## 问题与修复

Linux/macOS CI 在 `Join-Path $Explicit 'dotnet.exe'` 失败，是因为构建脚本沿用了某台 Windows 机器的 `C:` SDK 路径。
现在先验证目录存在，再拼接当前平台的可执行文件名。SDK 探测依次尝试：显式参数、
`SANDBOXSIM_DOTNET_ROOT`、`DOTNET_ROOT`、用户主目录 `.sandboxsim-tool/net8`、PATH、系统目录。
失效的覆盖值不会隐藏默认目录。

通过 `dotnet --list-sdks` 获取真实安装位置，解决 Unix 符号链接入口没有相邻 `sdk/` 目录的问题。
只选择稳定版 .NET 8，按数值版本排序；没有符合条件的 SDK 时返回空值，不触发 StrictMode 属性异常。

同时修复了 PowerShell 7 的 `IsWindows` 只读变量冲突、函数库残留的执行入口、csc Release 的无效
`-debug:none` 参数，以及多版本安装时引用包/编译器可能选到其他 .NET 主版本的问题。
安装脚本与构建脚本共享默认目录和可执行名称；脚本保留 BOM 以兼容 Windows PowerShell 5.1。

## 复验命令

在仓库根目录执行（需 .NET 8 SDK 和 PowerShell 7）：

```powershell
pwsh -NoProfile -File ./tools/test-build.ps1
pwsh -NoProfile -File ./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
pwsh -NoProfile -File ./tools/build.ps1 -Mode test -Configuration Release -Channel sdk -ParallelBuild
pwsh -NoProfile -File ./tools/build.ps1 -Mode test -Configuration Release -Channel csc
pwsh -NoProfile -File ./tools/build.ps1 -Mode digest -Seed 4242 -Ticks 43200 -Agents 30 -Configuration Release -Channel sdk -ParallelBuild
pwsh -NoProfile -File ./tools/build.ps1 -Mode snapshot -Seed 839102 -Days 100 -Agents 40 -SnapshotDays 25 -OutDir runs/validation -Configuration Release -Channel sdk -ParallelBuild
pwsh -NoProfile -File ./tools/build.ps1 -Mode batch -SeedRange 1..6 -Days 60 -Agents 20 -OutDir runs/validation -Configuration Release -Channel sdk -ParallelBuild
```

受限环境若不允许 MSBuild 并行进程，移除 `-ParallelBuild` 即可。

## 本机验证

| 验证 | 结果 |
|---|---|
| Windows / PowerShell 7 / SDK Release 并行构建 | 通过，0 警告、0 错误 |
| PowerShell 7 和 Windows PowerShell 5.1 脚本回归 | 各 12/12 |
| SDK 与 csc 的模拟测试 | 各 145/145 |
| seed 4242、43200 ticks、30 人，同 seed 两次 | 两通道均通过；摘要 `ecd27ac29e067a9f` |
| 100 天、40 人、25 天一次快照 | 通过，生成报告、事件、统计和 5 张 PNG |
| seed 1–6、60 天、20 人 | 全部通过 |

这些检查不证明完整游戏已经完成：出生、农业生产、存档、正式聚落及文明系统仍在后续里程碑。
macOS 需要 GitHub Actions 原生 runner 验证，本地 Windows 测试不能替代它。

## 文件管理

| 目录 | 职责 | 是否提交 |
|---|---|---|
| `src/` | Core 模拟、Console 表现、Tests 回归 | 源码提交；bin/obj 忽略 |
| `tools/` | 构建、运行、安装与脚本回归 | 提交；函数库不执行 CLI |
| `config/` | 默认参数 | 提交 |
| `docs/` | 设计、实现状态、验收记录 | 提交 |
| `.github/workflows/` | Windows/Linux/macOS CI | 提交 |
| `artifacts/` | 构建输出 | 忽略，可重新生成 |
| `runs/` | 实验报告、快照、验证日志 | 忽略，只保留目录说明 |

保留已有源码布局及用户实验产物，不做无关重命名或删除。CI 的三平台任务都会跑脚本回归、SDK 构建与测试；
Ubuntu 额外执行 csc、确定性、100 天冒烟和多 seed 实验，并上传运行产物。
