<#
.SYNOPSIS
    构建并运行全部测试；失败返回非零退出码（供 CI 使用）。

.EXAMPLE
    .\tools\test.ps1
    .\tools\test.ps1 -Filter Determinism
    .\tools\test.ps1 -Channel csc -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('auto', 'sdk', 'csc')]
    [string]$Channel = 'auto',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Filter = '',
    [string]$SdkRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SandBoxSimDotSource = $true
. (Join-Path $PSScriptRoot 'build-lib.ps1')

$build = Invoke-Build -Channel $Channel -Configuration $Configuration -SdkRoot $SdkRoot
$hostExe = Get-DotnetHost -Sdk $build.Sdk -Runtime $build.Runtime

$argv = @($build.Paths.Tests)
if ($Filter) { $argv += @('--filter', $Filter) }

Write-Host ''
Write-Host '=== Running tests ===' -ForegroundColor Cyan
& $hostExe @argv
$code = $LASTEXITCODE
Write-Host ''
if ($code -eq 0) {
    Write-Host 'All tests passed' -ForegroundColor Green
} else {
    Write-Host "TESTS FAILED (exit code $code)" -ForegroundColor Red
}
exit $code
