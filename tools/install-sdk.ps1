<#
.SYNOPSIS
    安装 .NET 8 SDK 到仓库外的工具目录（幂等，三平台可用）。

.DESCRIPTION
    为什么不在仓库内安装：仓库可能处于只读沙箱策略下，仓库外的工具目录更稳；
    同时避免把几百 MB 的二进制带进 git 历史（见 .gitignore 的 .tools/ 规则）。

    为什么用 curl 而不是 Invoke-WebRequest：本机 PowerShell 5.1 的 Schannel 在部分
    受限环境下会报 SEC_E_NO_CREDENTIALS，而 curl 走自己的 TLS 栈，实测可用。

    平台差异（CI 在 windows / ubuntu / macos 上跑同一套脚本）：
      * 默认安装目录由 build-lib.ps1 的 Get-DefaultSdkRoot 决定，**不硬编码盘符**；
      * 可执行文件名 Windows 是 dotnet.exe，其它平台是 dotnet；
      * 官方安装器：Windows/PowerShell 用 dotnet-install.ps1，
        类 Unix 用 dotnet-install.sh（两者都是官方脚本，行为一致）。

.EXAMPLE
    .\tools\install-sdk.ps1
    .\tools\install-sdk.ps1 -Channel 8.0 -InstallDir D:\tools\net8
#>
[CmdletBinding()]
param(
    [string]$Channel = '8.0',
    [string]$InstallDir = '',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host '=== SandBoxSim: install .NET SDK ===' -ForegroundColor Cyan

# 复用构建库里的平台探测（点源即可；它刻意不带 param 块，不会污染本脚本的变量）
. (Join-Path $PSScriptRoot 'build-lib.ps1')

if (-not $InstallDir) {
    $InstallDir = if ($env:SANDBOXSIM_DOTNET_ROOT) { $env:SANDBOXSIM_DOTNET_ROOT } else { Get-DefaultSdkRoot }
}
$exeName = Get-DotnetExeName
$dotnetExe = Join-Path $InstallDir $exeName

Write-Host "Target directory: $InstallDir"

if ((Test-Path $dotnetExe) -and -not $Force) {
    $existing = & $dotnetExe --list-sdks 2>&1
    if ($LASTEXITCODE -eq 0 -and ($existing | Where-Object { $_ -match '^8\.' })) {
        Write-Host "Usable SDK already present: $existing" -ForegroundColor Green
        exit 0
    }
}

$toolRoot = Split-Path -Parent $InstallDir
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# 与 build-lib.ps1 相同的平台判定（这里独立算一次，避免依赖点源顺序）
$installOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

function Invoke-Curl {
    param([string[]]$CurlArgs)

    # Windows 也叫 curl.exe；类 Unix 上就是 curl。两种都试。
    $curlName = if ($installOnWindows) { 'curl.exe' } else { 'curl' }
    $curl = Get-Command $curlName -CommandType Application -ErrorAction SilentlyContinue
    if (-not $curl) {
        throw 'curl not found. Install curl, or download the official dotnet-install script manually and rerun with the file already in place.'
    }

    & $curl.Source @CurlArgs
    return $LASTEXITCODE
}

if ($installOnWindows) {
    $scriptPath = Join-Path $toolRoot 'dotnet-install.ps1'
    if (-not (Test-Path $scriptPath)) {
        Write-Host 'Downloading dotnet-install.ps1 ...'
        $code = Invoke-Curl @('-f', '-sS', '-L', '--max-time', '120', '-o', $scriptPath, 'https://dot.net/v1/dotnet-install.ps1')
        if ($code -ne 0 -or -not (Test-Path $scriptPath)) {
            throw "Failed to download dotnet-install.ps1. If the network is restricted, download it manually and place it at $scriptPath"
        }
        Write-Host "  downloaded: $((Get-Item $scriptPath).Length) bytes"
    }

    Write-Host "Installing SDK (channel $Channel) into $InstallDir ..."
    & $scriptPath -Channel $Channel -InstallDir $InstallDir -NoPath -SkipNonVersionedFiles
    if ($LASTEXITCODE -ne 0) { throw "dotnet-install FAILED (exit code $LASTEXITCODE)" }
}
else {
    $scriptPath = Join-Path $toolRoot 'dotnet-install.sh'
    if (-not (Test-Path $scriptPath)) {
        Write-Host 'Downloading dotnet-install.sh ...'
        $code = Invoke-Curl @('-f', '-sS', '-L', '--max-time', '120', '-o', $scriptPath, 'https://dot.net/v1/dotnet-install.sh')
        if ($code -ne 0 -or -not (Test-Path $scriptPath)) {
            throw "Failed to download dotnet-install.sh. If the network is restricted, download it manually and place it at $scriptPath"
        }
        Write-Host "  downloaded: $((Get-Item $scriptPath).Length) bytes"
    }

    Write-Host "Installing SDK (channel $Channel) into $InstallDir ..."
    & bash $scriptPath --channel $Channel --install-dir $InstallDir --no-path
    if ($LASTEXITCODE -ne 0) { throw "dotnet-install FAILED (exit code $LASTEXITCODE)" }
}

$sdks = & $dotnetExe --list-sdks
Write-Host 'Installed SDKs:' -ForegroundColor Green
$sdks | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host 'How to use it:' -ForegroundColor Cyan
Write-Host "  `$env:SANDBOXSIM_DOTNET_ROOT = '$InstallDir'"
Write-Host '  ./tools/build.ps1 -Mode build'
Write-Host '(build.ps1 probes this default directory, so setting the env var is usually unnecessary.)'
