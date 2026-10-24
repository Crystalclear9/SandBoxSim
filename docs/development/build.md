# 构建与运行

以下命令从仓库根目录运行，使用 PowerShell 7。

## 构建工程

```powershell
# Core、Console 与测试：.NET 8 SDK
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild

# Godot C# 客户端
./tools/godot.ps1 -Mode build -Configuration Release

# 图形运行与编辑器
./tools/godot.ps1 -Mode run
./tools/godot.ps1 -Mode editor
```

Core、Console 与测试另支持 `-Channel csc`，适用于已有 .NET 运行时与 Roslyn 编译器的环境；Godot 使用其 .NET SDK。自动通道通过 `-Channel auto` 选择。SDK 探测、平台可执行名称与默认安装位置集中在 `tools/build-lib.ps1`。

`-ParallelBuild` 开启并行构建；不传时使用串行模式。构建输出位于 `artifacts/<Configuration>` 和项目的 `bin/obj`；Godot 缓存位于客户端 `.godot` 目录，均不提交。

`SandBoxSim.sln` 包含 Core、Console 和 Tests，图形工程单独通过 Godot 脚本构建。 `src/SandBoxSim.Godot/SandBoxSim.Godot.sln` 提供编辑器和原生导出的解决方案入口，包含图形客户端及 Core；自包含 ExportDebug/ExportRelease 配置允许恢复官方 NuGet 运行时包。`godot.ps1` 的 run、editor、test 使用 Debug，因为编辑器加载 Debug 程序集；`-Mode build -Configuration Release` 单独生成 Release，不启动编辑器。

`tools/dev.ps1 -SkipRun` 是 Core/Console 的构建与测试流程；不包含图形启动。图形开发使用 `godot.ps1 -Mode editor`。

## 独立游戏导出

Windows 导出使用与 Godot 4.7.2 .NET 编辑器匹配的 Mono 模板。将模板解压在项目 `.tools/godot-4.7.2/templates/`，其中应包含 `windows_release_x86_64.exe`；已有模板可通过 `-TemplatePath` 指定。模板和 SDK 属于项目本地开发依赖，不提交 Git。

```powershell
./tools/export-game.ps1
# 可选：指定已有的编辑器与对应 Mono Windows 模板
./tools/export-game.ps1 -GodotPath $env:GODOT_EXE -TemplatePath './.tools/godot-4.7.2/templates/windows_release_x86_64.exe'
```

脚本解析 SDK 并设置子进程的 `PATH`/`DOTNET_ROOT`，在项目内的新暂存目录导出；结束后原样恢复导出预设。Godot 有时退出 0 但报告程序集构建失败，因此同时检查日志、完整数据目录和导出包的实际 `--self-test`。首次导出会解析并建立游戏自身的用户配置目录与编辑器快照子目录，不移动已有配置；编辑器进程有可配置的超时上限，避免包装进程退出后仍一直等待。只有通过后才将完整暂存包移入 `artifacts/game/windows/`；发布前把旧包移到同级备份，替换失败时恢复旧包，成功后删除备份，避免覆盖复制留下过期 DLL。此过程不支持并发导出，发布时请先退出游戏；进程被强制终止时可能留下同级 `.previous-*` 备份，目录切换并非断电事务。失败的导出不会替换现有游戏包，暂存目录随后清理。已知本机证书库诊断单独保留，其他导出错误和带警告的失败导出均拒绝发布。日志位于 `.tools/game-export*.log`。

EXE 与相邻 `data_SandBoxSim.Godot_windows_x86_64/` 共同构成自包含游戏，不能只复制 EXE。网格、贴图与素材署名嵌入 PCK；普通游玩不依赖 SDK、编辑器或 Blender。`Play-3D.cmd` 优先启动该包；源码修改后需重新导出。

当前自动导出入口在 Windows 上导出并运行 Windows 成品自检。Linux/macOS 仍使用相应 Godot 预设，需在各自系统独立导出和启动验证；三平台源码 CI 不等于三平台发行包都已验证。导出日志判定回归可独立运行 `./tools/test-godot-export.ps1`。

## 命令行模拟

```powershell
# 交互 TUI
./tools/run.ps1

# 指定种子与居民，运行 30 天
./tools/run.ps1 -Mode headless -Seed 839102 -Days 30 -Agents 40

# 同种子重放摘要
./tools/run.ps1 -Mode digest -Seed 839102 -Days 30 -Agents 40

# 定期快照与多种子模拟
./tools/run.ps1 -Mode snapshot -Days 30 -Agents 40 -SnapshotDays 5
./tools/run.ps1 -Mode batch -SeedRange 1..5 -Days 30 -Agents 40
```

`-ConfigPath` 指定模拟配置，`-Width/-Height` 设置地图，`-OutDir` 指定输出目录。默认运行数据放在 `runs/`，包含统计、事件、模拟记录、摘要与地图快照。Console 运行内核，不继续执行图形会话的荒野日界作用。

## 测试

```powershell
./tools/test.ps1 -Configuration Release -Channel sdk
./tools/test.ps1 -Filter TerrainImpactTests -Configuration Release
./tools/test.ps1 -Filter WildPlaces -Configuration Release
./tools/godot.ps1 -Mode test
./tools/test-build.ps1
python tools/fetch-surface-materials.py --verify
./tools/check-docs.ps1
git diff --check
```

自带测试运行器支持名称过滤、通过/失败/跳过统计和失败退出码。客户端自检覆盖只读观察、保存、旧任务字段忽略、自由工具、模型、鼠标手势与窗口布局。图形改动还应检查实际窗口；无头自检不替代画面检查。

测试通过 `tools/test.ps1` 执行项目自带运行器，`dotnet test` 不会代替它。文档检查不需要 SDK 或 Godot，但默认使用 Git 枚举当前 Markdown 文件；检查本地文件是否存在、大小写是否一致，不检查外部网址与页内锚点。

各类修改需要的验证范围见 [开发指南](developer-guide.md)，运行资料位置见 [文件管理](repository-layout.md)。

## 渲染采样

可复现研究采样采用 [评测接口与实验运行](benchmark.md) 的帧预算、固定步长和墙钟入口。以下为保留的旧演示采样方式，不能与新协议结果直接比较。


图形客户端支持有界的实际世界采样。先构建 Debug 并设置 `GODOT_EXE`，从仓库根目录运行：

```powershell
$samplePath = Join-Path (Get-Location).Path 'runs/render-sample.json'
& $env:GODOT_EXE --path src/SandBoxSim.Godot -- --agents=300 --demo-days=3 --panel=field --benchmark-seconds=20 "--benchmark-output=$samplePath"
```

前五秒作为预热，不计入平均帧率；输出包含平均帧率、引擎进程时间监视器均值、绘制调用和图元数量，以及实际人口、tick、建筑与事件。内置检查线为 30 FPS，低于此值返回非零退出码。对照时保持窗口、渲染器、场景、人口与推进速度一致；单次本机样本不代表跨平台性能。JSON 和日志放在 `runs/`，不作为正式配图提交。

## 持续集成

`.github/workflows/ci.yml` 在 Windows、Linux 和 macOS 上检查文档链接、构建与运行核心回归，并在 Linux 上运行确定性和模拟检查；`godot.yml` 检查图形构建、资源导入和客户端自检。服务端执行结果以 GitHub 对相应提交的检查为准。

安装工具链见 [安装与启动](../guides/getting-started.md)，目录职责见 [项目架构](architecture.md)。

## 跨平台覆盖

内核 CI 在 Windows、Ubuntu 和 macOS 运行构建、脚本回归、完整测试与文档路径大小写检查。Godot CI 在同三平台下载校验过的 .NET 编辑器，执行 Release 构建、资源导入和 headless 客户端自检；它验证初始化与交互状态，不等于三平台显卡下的像素或性能一致。

外部评测的 Python 测试、PowerShell 引擎路径测试和扫描表面原始文件的离线大小/SHA-256 校验也在三个平台运行，覆盖 `.exe`、Linux 二进制和 macOS `.app/Contents/MacOS/Godot`。Ubuntu 额外使用 Xvfb/Mesa 软件显示执行原生截图、问题生成和产物验证；这是接口冒烟，不设硬件 FPS 门槛。Windows/macOS 的原生显示、不同 GPU/驱动及 ARM64 设备的实机表现仍需分别运行；x64 通过不能推导所有架构都已实测。

在线控制 CI 在三个平台启动两份真实 .NET HTTP 服务，检查鉴权、并发幂等、版本冲突、独立寻路判题、配对效率及两轮证据驱动器，保留 JSON/日志 artifact。服务无需 Godot；详情见 [在线控制与效率实验](online-efficiency.md)。CI 检查协议正确性，不跨机器比较速度，也不代表真实模型已产生 RSI。

引擎启动默认目录为当前仓库的 `.tools/godot-4.7.2`，可用 `GODOT_EXE` 或 `-GodotPath` 覆盖；显式路径无效会报错。SDK 的 Windows 旧路径仍由 `test-build.ps1` 验证不会导致非 Windows 的盘符崩溃。CI 运行与当前代码版本应对照同一个提交 SHA，不能把旧版通过记录当成新提交的验证结果。

## 文档整理的检查范围

仅调整说明与本机输出分类时，执行文档链接、文件大小写、Markdown 结构与 Git 空白检查即可；工程、资源和工具路径保持一致。移动源码、修改协议或模拟状态时，按 [开发指南](developer-guide.md) 执行对应实际回归。当前 CI 的结果需查看具体提交，文档不能把配置了检查等同于该提交已通过。

连续世界代码实验在三平台 CI 执行两阶段非 LLM 夹具、旧能力回放和 HTML 报告，并归档证据；不要求夹具通过递归能力筛选线。入口见 [连续世界实验](stateful-rsi.md)。

## 纹理导入维护

使用 `./tools/godot.ps1 -Mode test -ReimportTextures` 可在真实编辑器生命周期中重建模型与扫描表面的 mip 链，并检查实际缓存；独立包导出也自动执行。它不要求 Blender，不修改原始 PNG/JPG 像素。

## 原生评测的启动诊断

`evaluate.py` 对引擎日志中的 `ERROR:` 保持严格拒绝：进程退出码为 0、截图完整或完成阶段已记录，都不能把这一运行视为有效评测。Windows 主机若出现 `Failed to read the root certificate store`，需要检查运行账户的系统证书库访问；`Can't create shader cache folder` 则需要检查 Godot 报告的游戏用户目录与写入权限。它们与脚本／网格自检断言分开诊断，不应删除日志、改小判分范围或放宽错误检测来取得通过。

当前独立包导出入口会解析并创建本项目正常的用户目录，保留已有设置；证书库访问属于主机环境，工具不会修改全局信任库。导出自检与严格视觉评测的日志规则不同，导出通过不能替代 `evaluate.py verify`。

---

[文档导航](../README.md) · [项目首页](../../README.md)
