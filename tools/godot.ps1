[CmdletBinding()]
param(
    [ValidateSet('build', 'run', 'test', 'editor')]
    [string]$Mode = 'run',
    [string]$GodotPath = $env:GODOT_EXE,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$ReimportTextures
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'build' -and $ReimportTextures) { throw 'Use -Mode test, run or editor with -ReimportTextures.' }
. (Join-Path $PSScriptRoot 'build-lib.ps1')
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$gameRoot = Join-Path $repositoryRoot 'src/SandBoxSim.Godot'
$runtime = Get-DotnetHost -Sdk (Resolve-DotnetSdk)
$env:PATH = (Split-Path $runtime -Parent) + [System.IO.Path]::PathSeparator + $env:PATH
$project = Join-Path $gameRoot 'SandBoxSim.Godot.csproj'
if ($Mode -ne 'build') { $Configuration = 'Debug' } # The editor loads Debug assemblies, including headless runs.
. (Join-Path $PSScriptRoot 'godot-build-manifest.ps1')
$sourceBefore = Get-GodotSourceHashes $repositoryRoot
& $runtime build $project -c $Configuration --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw "Godot C# build failed ($LASTEXITCODE)" }
$sourceAfter = Get-GodotSourceHashes $repositoryRoot
if (($sourceBefore | ConvertTo-Json -Compress) -cne ($sourceAfter | ConvertTo-Json -Compress)) { throw 'C# source changed during build; rebuild before evaluation.' }
$binaryRoot = Join-Path $gameRoot ".godot/mono/temp/bin/$Configuration"
$manifest = @{ schemaVersion = 1; configuration = $Configuration; sourceFiles = $sourceAfter;
    assemblySha256 = (Get-FileHash -LiteralPath (Join-Path $binaryRoot 'SandBoxSim.Godot.dll') -Algorithm SHA256).Hash.ToLowerInvariant();
    coreSha256 = (Get-FileHash -LiteralPath (Join-Path $binaryRoot 'SandBoxSim.Core.dll') -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $binaryRoot 'build-source.json') -Encoding utf8
if ($Mode -eq 'build') { exit 0 }
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
$godotEnvironment=Get-GodotEnvironmentSnapshot
try {
Set-GodotProcessEnvironment -Root $repositoryRoot
$GodotPath = Resolve-GodotExecutable -Explicit $GodotPath
Set-GodotEditorTrust -Executable $GodotPath -Root $repositoryRoot
$engineLog = Join-Path $repositoryRoot '.tools/godot-runtime.log'
$engineVersion = & $GodotPath --headless --version
if ($LASTEXITCODE -ne 0 -or $engineVersion -notmatch "4\.7\.2.*mono") { throw "Godot 4.7.2 .NET executable required: $GodotPath" }
& $GodotPath --log-file $engineLog --headless --path $gameRoot --editor --import --quit
Assert-GodotRunLog -ExitCode $LASTEXITCODE -Log ([IO.File]::ReadAllText($engineLog))
if ($ReimportTextures) { Invoke-GodotTextureReimport -GodotPath $GodotPath -GameRoot $gameRoot -LogFile (Join-Path $repositoryRoot '.tools/texture-import.log') }
if ($Mode -eq 'editor') {
    & $GodotPath --log-file $engineLog --path $gameRoot --editor
} elseif ($Mode -eq 'test') {
    & $GodotPath --log-file $engineLog --headless --path $gameRoot -- --self-test
} else {
    & $GodotPath --log-file $engineLog --path $gameRoot
}
Assert-GodotRunLog -ExitCode $LASTEXITCODE -Log ([IO.File]::ReadAllText($engineLog)) -SelfTest:($Mode -eq 'test')
} finally { Restore-GodotEnvironment -Snapshot $godotEnvironment }
