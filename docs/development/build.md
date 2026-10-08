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

`SandBoxSim.sln` 包含 Core、Console 和 Tests，图形工程单独通过 Godot 脚本构建。`godot.ps1` 的 run、editor、test 使用 Debug，因为编辑器加载 Debug 程序集；`-Mode build -Configuration Release` 单独生成 Release，不启动编辑器。

`tools/dev.ps1 -SkipRun` 是 Core/Console 的构建与测试流程；不包含图形启动。图形开发使用 `godot.ps1 -Mode editor`。

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

外部评测的 Python 测试和 PowerShell 引擎路径测试也在三个平台运行，覆盖 `.exe`、Linux 二进制和 macOS `.app/Contents/MacOS/Godot`。Ubuntu 额外使用 Xvfb/Mesa 软件显示执行原生截图、问题生成和产物验证；这是接口冒烟，不设硬件 FPS 门槛。Windows/macOS 的原生显示、不同 GPU/驱动及 ARM64 设备的实机表现仍需分别运行；x64 通过不能推导所有架构都已实测。

在线控制 CI 在三个平台启动两份真实 .NET HTTP 服务，检查鉴权、并发幂等、版本冲突、独立寻路判题、配对效率及两轮证据驱动器，保留 JSON/日志 artifact。服务无需 Godot；详情见 [在线控制与效率实验](online-efficiency.md)。CI 检查协议正确性，不跨机器比较速度，也不代表真实模型已产生 RSI。

引擎启动默认目录为用户主目录的 `.sandboxsim-tool/godot-4.7.2`，可用 `GODOT_EXE` 或 `-GodotPath` 覆盖；显式路径无效会报错。SDK 的 Windows 旧路径仍由 `test-build.ps1` 验证不会导致非 Windows 的盘符崩溃。CI 运行与当前代码版本应对照同一个提交 SHA，不能把旧版通过记录当成新提交的验证结果。

## 文档整理的检查范围

仅调整说明与本机输出分类时，执行文档链接、文件大小写、Markdown 结构与 Git 空白检查即可；工程、资源和工具路径保持一致。移动源码、修改协议或模拟状态时，按 [开发指南](developer-guide.md) 执行对应实际回归。当前 CI 的结果需查看具体提交，文档不能把配置了检查等同于该提交已通过。

---

[文档导航](../README.md) · [项目首页](../../README.md)
