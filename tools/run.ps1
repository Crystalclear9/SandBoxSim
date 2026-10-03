<#
.SYNOPSIS
    构建并运行 SandBoxSim（交互 TUI 或 headless 模式）。

.EXAMPLE
    .\tools\run.ps1                                        # 交互 TUI（默认 seed 839102）
    .\tools\run.ps1 -Seed 12345 -Width 120 -Height 120
    .\tools\run.ps1 -Mode headless -Days 100               # 100 天无人干预长跑
    .\tools\run.ps1 -Mode digest -Ticks 144000             # 200 天确定性摘要校验
    .\tools\run.ps1 -Mode batch -SeedRange 1..20 -Days 100 # 多 seed 批量体检
    .\tools\run.ps1 -Mode snapshot -Days 30 -SnapshotDays 5
#>
[CmdletBinding()]
param(
    [ValidateSet('run', 'headless', 'digest', 'batch', 'snapshot')]
    [string]$Mode = 'run',
    [ValidateSet('auto', 'sdk', 'csc')]
    [string]$Channel = 'auto',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [int]$Seed = 839102,
    [int]$Days = 100,
    [int]$Ticks = 0,
    [int]$Width = 0,
    [int]$Height = 0,
    [string]$ConfigPath = '',
    [string]$OutDir = '',
    [int]$SnapshotDays = 0,
    [int[]]$Seeds = @(),

    <#
      -SeedRange 用字符串传种子列表，支持 "1..20" 与 "1,2,7"。
      为什么不用 -Seeds：`powershell -File run.ps1 -Seeds 1,2,3` 里的逗号是**参数分隔符**，
      实际只会收到 "1"，静默变成"只跑一个种子" —— 很容易让人误判批量结果。
    #>
    [string]$SeedRange = '',

    [switch]$NoColor,
    [switch]$ParallelBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 先设置点源开关，再加载函数库。
# 库文件（build-lib.ps1）刻意不带 param 块，否则它的参数默认值会覆盖本脚本的变量
# （实测过的坑：被点源文件里的 $Mode 默认值会把本脚本的 -Mode digest 覆盖成 build）。
$SandBoxSimDotSource = $true
. (Join-Path $PSScriptRoot 'build-lib.ps1')

Invoke-Run -Mode $Mode -Channel $Channel -Configuration $Configuration -SdkRoot '' `
    -Seed $Seed -Days $Days -Ticks $Ticks -Width $Width -Height $Height `
    -ConfigPath $ConfigPath -OutDir $OutDir -SnapshotDays $SnapshotDays `
    -Seeds $Seeds -SeedRange $SeedRange -NoColor $NoColor.IsPresent `
    -ParallelBuild $ParallelBuild.IsPresent
