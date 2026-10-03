<#
.SYNOPSIS
    SandBoxSim 构建/运行命令行入口。

.DESCRIPTION
    本仓库刻意支持两条构建通道，参见 docs/12-Milestones.md：

      通道 A（首选）：机器上有 .NET 8 SDK 时，走标准 dotnet build / dotnet run。
      通道 B（降级）：机器上只有 .NET 8 运行时 + Roslyn csc（例如只装了 VS2022）时，
                      直接用 csc.exe 编译 BCL-only 源码，再由 dotnet 运行时托管执行。
                      本仓库不引用任何 NuGet 包，因此两条通道行为一致。

    脚本会自动探测通道，也可用 -Channel sdk|csc 强制指定。

    具体实现在 tools/build-lib.ps1（函数库，无 param 块）。
    之所以拆成两个文件：包装脚本（run/test/dev）需要 dot-source 函数库，
    而被 dot-source 的文件不能有自己的 param 块 —— 否则它的参数默认值会覆盖
    调用方的同名变量（实测过：run.ps1 的 $Mode=digest 会被覆盖成 build）。
    详见 build-lib.ps1 顶部的说明。

.EXAMPLE
    .\tools\build.ps1                       # 自动探测，构建全部
    .\tools\build.ps1 -Mode run             # 构建并启动 TUI
    .\tools\build.ps1 -Mode test            # 构建并运行测试，失败返回非零退出码
    .\tools\build.ps1 -Mode snapshot -SnapshotDays 5 -Days 30
#>
[CmdletBinding()]
param(
    [ValidateSet('build', 'run', 'test', 'snapshot', 'digest', 'headless', 'batch', 'clean')]
    [string]$Mode = 'build',

    [ValidateSet('auto', 'sdk', 'csc')]
    [string]$Channel = 'auto',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # 透传给 SandBoxSim.Console 的参数
    [int]$Seed = 839102,
    [int]$Days = 100,
    [int]$Ticks = 0,
    [int]$Width = 0,
    [int]$Height = 0,
    [string]$ConfigPath = '',
    [string]$OutDir = '',
    [int]$SnapshotDays = 0,
    [int]$Agents = 0,
    [int]$AgentRadius = 0,
    [int[]]$Seeds = @(),
    [switch]$NoColor,

    # 构建期参数
    [string]$SdkRoot = '',

    # 并行构建：默认关闭。受限环境（沙箱/企业策略）会拦截 MSBuild 的命名管道，
    # 导致 restore 静默失败。确认环境允许命名管道时打开它可把全量构建从 ~20 秒压到几秒。
    [switch]$ParallelBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SandBoxSimDotSource = $true
. (Join-Path $PSScriptRoot 'build-lib.ps1')

switch ($Mode) {
    'clean' { Invoke-Clean; exit 0 }
    'build' {
        $null = Invoke-Build -Channel $Channel -Configuration $Configuration -SdkRoot $SdkRoot `
            -ParallelBuild $ParallelBuild.IsPresent
        exit 0
    }
    default {
        # 续行反引号必须紧跟在行尾（后面不能有空格）—— 否则 PowerShell 会把下一行
        # 当成独立语句解析，报出与真正原因无关的类型转换错误。
        Invoke-Run -Mode $Mode -Channel $Channel -Configuration $Configuration -SdkRoot $SdkRoot `
            -Seed $Seed -Days $Days -Ticks $Ticks -Width $Width -Height $Height `
            -ConfigPath $ConfigPath -OutDir $OutDir -SnapshotDays $SnapshotDays `
            -Seeds $Seeds -NoColor $NoColor.IsPresent `
            -Agents $Agents -AgentRadius $AgentRadius `
            -ParallelBuild $ParallelBuild.IsPresent
    }
}
