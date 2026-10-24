[CmdletBinding()]
param([string]$GodotPath=$env:GODOT_EXE, [string]$TemplatePath='', [ValidateRange(30,3600)][int]$TimeoutSeconds=600)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'build-lib.ps1')
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
if (-not $IsWindows) { throw 'This entry exports and executes the Windows package on Windows. Use the Linux/macOS presets on their respective hosts.' }
$repositoryRoot=Split-Path -Parent $PSScriptRoot
$godotEnvironment=Get-GodotEnvironmentSnapshot
try {
Set-GodotProcessEnvironment -Root $repositoryRoot
$GodotPath=Resolve-GodotExecutable -Explicit $GodotPath
Set-GodotEditorTrust -Executable $GodotPath -Root $repositoryRoot
# The Windows console wrapper can remain waiting after the editor has exited.
# Launch the actual editor binary and capture its streams directly.
if ($GodotPath.EndsWith('_console.exe',[StringComparison]::OrdinalIgnoreCase)) {
    $native=$GodotPath.Substring(0,$GodotPath.Length-'_console.exe'.Length)+'.exe'
    if (Test-Path -LiteralPath $native -PathType Leaf) { $GodotPath=$native }
}
function Wait-ExportProcess($Process) {
    if (-not $Process.WaitForExit($TimeoutSeconds*1000)) {
        $Process.Kill($true);$Process.WaitForExit()
        throw "Export stage exceeded $TimeoutSeconds seconds; previous game package was preserved."
    }
}
$runtime=Get-DotnetHost -Sdk (Resolve-DotnetSdk)
$env:DOTNET_ROOT=Split-Path -Parent $runtime
$env:PATH=$env:DOTNET_ROOT+[IO.Path]::PathSeparator+$env:PATH
if (-not $TemplatePath) { $TemplatePath=Join-Path $repositoryRoot '.tools/godot-4.7.2/templates/windows_release_x86_64.exe' }
if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) { throw 'Place the matching Godot Mono Windows release template in .tools/godot-4.7.2/templates or supply -TemplatePath.' }
$version=& $GodotPath --headless --version
if ($LASTEXITCODE -ne 0 -or $version -notmatch '4\.7\.2.*mono') { throw 'Godot 4.7.2 .NET editor required.' }
$stageRoot=[IO.Path]::GetFullPath((Join-Path $repositoryRoot '.tools/export-staging'))
$stage=Join-Path $stageRoot ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$gameRoot=Join-Path $repositoryRoot 'src/SandBoxSim.Godot'
Invoke-GodotTextureReimport -GodotPath $GodotPath -GameRoot $gameRoot -LogFile (Join-Path $repositoryRoot '.tools/texture-import.log') -TimeoutSeconds $TimeoutSeconds
$preset=Join-Path $gameRoot 'export_presets.cfg'
$original=[IO.File]::ReadAllBytes($preset)
$log=Join-Path $repositoryRoot '.tools/game-export.log'
$stdout=Join-Path $repositoryRoot '.tools/game-export-stdout.log'
$stderr=Join-Path $repositoryRoot '.tools/game-export-stderr.log'
$executable=Join-Path $stage 'SandBoxSim.exe'
try {
    # The editor's snapshot profiler opens user:// during export even on a first
    # launch. Resolve this project's normal profile through Godot itself;
    # never infer a platform-specific AppData path or move existing preferences.
    $prepare=Join-Path $stage 'prepare-profile.gd'
    [IO.File]::WriteAllText($prepare,"extends SceneTree`nfunc _initialize():`n    print(`"SANDBOXSIM_PROFILE=`" + OS.get_user_data_dir())`n    quit()`n")
    $process=Start-Process -FilePath $GodotPath -WindowStyle Hidden -PassThru -ArgumentList @('--headless','--path',"`"$gameRoot`"",'--script',"`"$prepare`"",'--log-file',"`"$log`"") -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Wait-ExportProcess $process
    Assert-GodotExportLog -ExitCode $process.ExitCode -Log (([IO.File]::ReadAllText($stdout))+([IO.File]::ReadAllText($stderr)))
    $profileMatch=[regex]::Match([IO.File]::ReadAllText($stdout),'(?m)^SANDBOXSIM_PROFILE=(.+)\r?$')
    if (-not $profileMatch.Success) { throw 'Godot did not resolve the game profile directory.' }
    $profile=[IO.Path]::GetFullPath($profileMatch.Groups[1].Value.Trim())
    if ([IO.Path]::GetFileName($profile) -ne 'SandBoxSim') { throw 'Unexpected project profile directory; no files were moved.' }
    [IO.Directory]::CreateDirectory($profile) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $profile 'objectdb_snapshots')) | Out-Null
    Remove-Item -LiteralPath $prepare
    $text=[Text.Encoding]::UTF8.GetString($original)
    $template=[IO.Path]::GetFullPath($TemplatePath).Replace('\','/')
    [IO.File]::WriteAllText($preset,$text.Replace('[preset.0.options]',"[preset.0.options]`ncustom_template/release=`"$template`""))
    try {
        $process=Start-Process -FilePath $GodotPath -WindowStyle Hidden -PassThru -ArgumentList @('--headless','--path',"`"$gameRoot`"",'--log-file',"`"$log`"",'--export-release','"Windows Desktop"',"`"$executable`"",'--quit') -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        Wait-ExportProcess $process
        Assert-GodotExportLog -ExitCode $process.ExitCode -Log (([IO.File]::ReadAllText($stdout))+([IO.File]::ReadAllText($stderr)))
    } finally { [IO.File]::WriteAllBytes($preset,$original) }
    $data=Join-Path $stage 'data_SandBoxSim.Godot_windows_x86_64'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $data 'SandBoxSim.Godot.dll') -PathType Leaf)) { throw 'Export is missing its executable or self-contained data directory.' }
    $testLog=Join-Path $repositoryRoot '.tools/game-export-selftest.log'
    $process=Start-Process -FilePath $executable -WindowStyle Hidden -PassThru -ArgumentList @('--headless','--log-file',"`"$testLog`"",'--','--self-test') -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Wait-ExportProcess $process
    $output=[IO.File]::ReadAllText($stdout)
    Assert-GodotExportLog -ExitCode $process.ExitCode -Log ($output+([IO.File]::ReadAllText($stderr)))
    if ($output -notmatch 'GODOT_SELF_TEST_PASS:') { throw 'Exported game did not complete its self-test.' }
    $destination=Join-Path $repositoryRoot 'artifacts/game/windows'
    Publish-GodotPackage -Stage $stage -Destination $destination
    Write-Host "GAME_EXPORT_PASS: $destination (exported package self-test passed)"
} finally {
    # This random staging directory belongs exclusively to this invocation.
    $resolved=[IO.Path]::GetFullPath($stage)
    if (-not $resolved.StartsWith($stageRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Export staging cleanup escaped project directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
} finally { Restore-GodotEnvironment -Snapshot $godotEnvironment }
