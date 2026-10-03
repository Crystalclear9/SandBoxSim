<#
.SYNOPSIS
    构建并运行 SandBoxSim（交互 TUI 或 headless 模式）。

.EXAMPLE
    .\tools\run.ps1                                        # 交互 TUI（默认 seed 839102）
    .\tools\run.ps1 -Seed 12345 -Width 120 -Height 120
    .\tools\run.ps1 -Mode headless -Days 100               # 100 天无人干预长跑
    .\tools\run.ps1 -Mode headless -Days 100 -Agents 40    # 放 40 个人再跑 100 天
    .\tools\run.ps1 -Mode digest -Ticks 144000             # 确定性摘要校验
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

    # M1：初始放置的居民数量与散布半径（0 = 不放人，观察纯环境演化）
    [int]$Agents = 0,
    [int]$AgentRadius = 0,

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

# 注意：续行反引号必须紧跟在**行尾**，后面不能有任何字符（包括空格）。
# 之前一次批量替换把 "` + CRLF" 写成了 "`r`n" 两个字符，导致 PowerShell 把
# "-Agents $Agents" 当成独立语句解析，报出很难懂的类型转换错误。
Invoke-Run -Mode $Mode -Channel $Channel -Configuration $Configuration -SdkRoot '' `
    -Seed $Seed -Days $Days -Ticks $Ticks -Width $Width -Height $Height `
    -ConfigPath $ConfigPath -OutDir $OutDir -SnapshotDays $SnapshotDays `
    -Seeds $Seeds -SeedRange $SeedRange -NoColor $NoColor.IsPresent `
    -Agents $Agents -AgentRadius $AgentRadius `
    -ParallelBuild $ParallelBuild.IsPresent
