[CmdletBinding()]
param(
    [ValidateSet('build', 'run', 'test', 'editor')]
    [string]$Mode = 'run',
    [string]$GodotPath = $env:GODOT_EXE,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$ReimportTextures,
    [switch]$ResidentMotionOnly,
    [switch]$HandContinuityOnly,
    [switch]$UpperArmFlexOnly,
    [switch]$BodyFramesOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if($ResidentMotionOnly -and $Mode -ne 'test'){throw 'ResidentMotionOnly requires -Mode test.'}
if($HandContinuityOnly -and ($Mode -ne 'test' -or $ResidentMotionOnly)){throw 'HandContinuityOnly requires -Mode test without ResidentMotionOnly.'}
if($BodyFramesOnly -and ($Mode -ne 'test' -or $ResidentMotionOnly -or $HandContinuityOnly)){throw 'BodyFramesOnly requires -Mode test without another focused test.'}
if($UpperArmFlexOnly -and ($Mode -ne 'test' -or $ResidentMotionOnly -or $HandContinuityOnly -or $BodyFramesOnly)){throw 'UpperArmFlexOnly requires -Mode test without another focused test.'}
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
    if($UpperArmFlexOnly){
        & $GodotPath --log-file $engineLog --headless --path $gameRoot -- --test-upper-arm-flex
        if([IO.File]::ReadAllText($engineLog) -notmatch '(?m)^UPPER_ARM_TEST_PASS:'){throw 'Upper-arm flex tests did not complete.'}
    } elseif($BodyFramesOnly){
        & $GodotPath --log-file $engineLog --headless --path $gameRoot -- --test-body-frames
        if([IO.File]::ReadAllText($engineLog) -notmatch '(?m)^BODY_FRAMES_TEST_PASS:'){throw 'Body frame tests did not complete.'}
    } elseif($HandContinuityOnly){
        & $GodotPath --log-file $engineLog --headless --path $gameRoot -- --test-hand-continuity
        if([IO.File]::ReadAllText($engineLog) -notmatch '(?m)^HAND_CONTINUITY_TEST_PASS:'){throw 'Hand continuity tests did not complete.'}
    } elseif($ResidentMotionOnly){
        & $GodotPath --log-file $engineLog --headless --path $gameRoot -- --test-resident-motion
        if([IO.File]::ReadAllText($engineLog) -notmatch '(?m)^RESIDENT_MOTION_TEST_PASS:'){throw 'Resident motion tests did not complete.'}
    } else { & $GodotPath --log-file $engineLog --headless --path $gameRoot -- --self-test }
} else {
    & $GodotPath --log-file $engineLog --path $gameRoot
}
Assert-GodotRunLog -ExitCode $LASTEXITCODE -Log ([IO.File]::ReadAllText($engineLog)) -SelfTest:($Mode -eq 'test' -and -not $ResidentMotionOnly -and -not $HandContinuityOnly -and -not $BodyFramesOnly -and -not $UpperArmFlexOnly)
} finally { Restore-GodotEnvironment -Snapshot $godotEnvironment }
