<#
.SYNOPSIS
    安装 .NET 8 SDK 到仓库外的工具目录（幂等）。

.DESCRIPTION
    为什么不在仓库内安装：仓库可能处于只读沙箱策略下，仓库外的工具目录更稳；
    同时避免把几百 MB 的二进制带进 git 历史（见 .gitignore 的 .tools/ 规则）。

    为什么用 curl 而不是 Invoke-WebRequest：本机 PowerShell 5.1 的 Schannel 在部分
    受限环境下会报 SEC_E_NO_CREDENTIALS，而 curl 走自己的 TLS 栈，实测可用。

.EXAMPLE
    .\tools\install-sdk.ps1
    .\tools\install-sdk.ps1 -Channel 8.0 -InstallDir D:\tools\net8
#>
[CmdletBinding()]
param(
    [string]$Channel = '8.0',
    [string]$InstallDir = 'C:\Users\70454\.sandboxsim-tool\net8',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host '=== SandBoxSim: install .NET SDK ===' -ForegroundColor Cyan

$dotnetExe = Join-Path $InstallDir 'dotnet.exe'
if ((Test-Path $dotnetExe) -and -not $Force) {
    $existing = & $dotnetExe --list-sdks 2>&1
    if ($LASTEXITCODE -eq 0 -and ($existing | Where-Object { $_ -match '^8\.' })) {
        Write-Host "Usable SDK already present: $existing" -ForegroundColor Green
        Write-Host "Install directory: $InstallDir"
        exit 0
    }
}

$toolRoot = Split-Path -Parent $InstallDir
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

$scriptPath = Join-Path $toolRoot 'dotnet-install.ps1'
if (-not (Test-Path $scriptPath)) {
    Write-Host 'Downloading dotnet-install.ps1 ...'
    & curl.exe -sS -L --max-time 120 -o $scriptPath 'https://dot.net/v1/dotnet-install.ps1'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $scriptPath)) {
        throw 'Failed to download dotnet-install.ps1. If the network is restricted, download it manually and place it at ' + $scriptPath
    }
    Write-Host "  downloaded: $((Get-Item $scriptPath).Length) bytes"
}

Write-Host "Installing SDK (channel $Channel) into $InstallDir ..."
& $scriptPath -Channel $Channel -InstallDir $InstallDir -NoPath -SkipNonVersionedFiles
if ($LASTEXITCODE -ne 0) { throw "dotnet-install FAILED (exit code $LASTEXITCODE)" }

$sdks = & $dotnetExe --list-sdks
Write-Host 'Installed SDKs:' -ForegroundColor Green
$sdks | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host 'How to use it:' -ForegroundColor Cyan
Write-Host "  `$env:SANDBOXSIM_DOTNET_ROOT = '$InstallDir'"
Write-Host '  .\tools\build.ps1 -Mode build'
Write-Host '(tools/build.ps1 probes this default directory, so setting the env var is usually unnecessary.)'
