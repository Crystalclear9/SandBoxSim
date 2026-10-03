<#
.SYNOPSIS
    SandBoxSim 开发入口：确保 SDK → 构建 → 测试 → 运行。一条命令从零到可玩。

.EXAMPLE
    .\tools\dev.ps1                 # 全流程 + 启动 TUI
    .\tools\dev.ps1 -SkipRun        # 只构建与测试
#>
[CmdletBinding()]
param(
    [ValidateSet('auto', 'sdk', 'csc')]
    [string]$Channel = 'auto',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [int]$Seed = 839102,
    [switch]$SkipTests,
    [switch]$SkipRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host '=== SandBoxSim dev pipeline ===' -ForegroundColor Cyan

$SandBoxSimDotSource = $true
. (Join-Path $PSScriptRoot 'build-lib.ps1')

# 1) 确保有 SDK（没有就走通道 B，并提示如何补齐）
if ($Channel -ne 'csc') {
    $sdk = Resolve-DotnetSdk -Explicit 'C:\Users\70454\.sandboxsim-tool\net8'
    if (-not $sdk) {
        Write-Host 'No .NET 8 SDK found; trying automatic install ...' -ForegroundColor Yellow
        try { & (Join-Path $PSScriptRoot 'install-sdk.ps1') }
        catch {
            Write-Host "SDK install failed: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host 'Continuing with channel B (Roslyn csc) - functionally equivalent.' -ForegroundColor Yellow
        }
    }
}

# 2) 构建
$build = Invoke-Build -Channel $Channel -Configuration $Configuration -SdkRoot ''
$hostExe = Get-DotnetHost -Sdk $build.Sdk -Runtime $build.Runtime

# 3) 测试
if (-not $SkipTests) {
    Write-Host ''
    Write-Host '=== Running tests ===' -ForegroundColor Cyan
    & $hostExe $build.Paths.Tests
    if ($LASTEXITCODE -ne 0) { throw "TESTS FAILED (exit code $LASTEXITCODE)" }
}

# 4) 运行
if (-not $SkipRun) {
    Write-Host ''
    Write-Host '=== Starting SandBoxSim ===' -ForegroundColor Cyan
    & $hostExe $build.Paths.Console --seed $Seed
}
