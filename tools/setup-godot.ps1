[CmdletBinding()]
param(
    [string]$Version = '4.7.2',
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.sandboxsim-tool/godot-4.7.2')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if ($IsWindows) { $platform = if ($architecture -eq 'arm64') { 'windows_arm64' } else { 'win64' }; $pattern = '*console.exe' }
elseif ($IsMacOS) { $platform = 'macos.universal'; $pattern = 'Godot' }
elseif ($IsLinux) { $platform = if ($architecture -eq 'arm64') { 'linux_arm64' } else { 'linux_x86_64' }; $pattern = 'Godot*' }
else { throw 'Unsupported operating system' }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$editorRoot = Join-Path $InstallRoot 'editor'
$asset = "Godot_v$Version-stable`_mono_$platform.zip"
$baseUrl = "https://github.com/godotengine/godot-builds/releases/download/$Version-stable"
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
$archive = Join-Path $InstallRoot $asset
$manifest = Join-Path $InstallRoot 'SHA512-SUMS.txt'
if (-not (Test-Path -LiteralPath $archive)) {
    Write-Host "Downloading official Godot $Version .NET editor"
    Invoke-WebRequest -Uri "$baseUrl/$asset" -OutFile $archive
}
Invoke-WebRequest -Uri "$baseUrl/SHA512-SUMS.txt" -OutFile $manifest
$checksumLine = Get-Content -LiteralPath $manifest | Where-Object { $_ -match ([regex]::Escape($asset) + '$') } | Select-Object -First 1
if (-not $checksumLine) { throw "Official checksum missing for $asset" }
$expected = ($checksumLine -split '\s+')[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected) { throw "Godot checksum mismatch: $archive" }
if (-not (Test-Path -LiteralPath $editorRoot)) { Expand-Archive -LiteralPath $archive -DestinationPath $editorRoot }
$executable = Get-ChildItem -LiteralPath $editorRoot -Recurse -File -Filter $pattern |
    Where-Object { if ($IsLinux) { $_.Name -match '^Godot.*(x86_64|arm64)$' } else { $true } } | Select-Object -First 1
if (-not $executable) { throw "Editor executable missing in $editorRoot" }
if (-not $IsWindows) { & chmod +x $executable.FullName; if ($LASTEXITCODE -ne 0) { throw 'Cannot enable editor execution' } }
$versionOutput = & $executable.FullName --headless --version
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch ([regex]::Escape($Version) + '.*mono')) { throw 'Installed Godot version does not match the .NET project' }
Write-Output $executable.FullName
