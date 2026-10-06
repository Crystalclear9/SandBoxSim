[CmdletBinding()]
param(
    [ValidateSet('build', 'run', 'test', 'editor')]
    [string]$Mode = 'run',
    [string]$GodotPath = $env:GODOT_EXE,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-lib.ps1')
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$gameRoot = Join-Path $repositoryRoot 'src/SandBoxSim.Godot'
$runtime = Get-DotnetHost -Sdk (Resolve-DotnetSdk)
$env:PATH = (Split-Path $runtime -Parent) + [System.IO.Path]::PathSeparator + $env:PATH
$project = Join-Path $gameRoot 'SandBoxSim.Godot.csproj'
if ($Mode -ne 'build') { $Configuration = 'Debug' } # The editor loads Debug assemblies, including headless runs.
& $runtime build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Godot C# build failed ($LASTEXITCODE)" }
if ($Mode -eq 'build') { exit 0 }
if (-not $GodotPath) {
    foreach ($candidate in @('godot', 'godot-mono', 'godot4')) {
        $found = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($found) { $GodotPath = $found.Source; break }
    }
}
if (-not $GodotPath -and $IsWindows) {
    $localRoot = Join-Path $env:USERPROFILE '.sandboxsim-tool/godot-4.7.2'
    if (Test-Path -LiteralPath $localRoot) {
        $found = Get-ChildItem -LiteralPath $localRoot -Recurse -Filter '*console.exe' | Select-Object -First 1
        if ($found) { $GodotPath = $found.FullName }
    }
}
if (-not $GodotPath -or -not (Test-Path -LiteralPath $GodotPath)) {
    throw 'Godot 4.7.2 .NET editor not found. Set GODOT_EXE or pass -GodotPath.'
}
& $GodotPath --headless --path $gameRoot --editor --import --quit
if ($LASTEXITCODE -ne 0) { throw "Godot asset import failed ($LASTEXITCODE)" }
if ($Mode -eq 'editor') {
    & $GodotPath --path $gameRoot --editor
} elseif ($Mode -eq 'test') {
    & $GodotPath --headless --path $gameRoot -- --self-test
} else {
    & $GodotPath --path $gameRoot
}
if ($LASTEXITCODE -ne 0) { throw "Godot failed ($LASTEXITCODE)" }
