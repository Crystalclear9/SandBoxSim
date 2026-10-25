Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
$pass='GODOT_SELF_TEST_PASS: fixture completed'
foreach($case in @(
    @{ExitCode=1;Log=$pass},
    @{ExitCode=0;Log=''},
    @{ExitCode=0;Log="SHADER ERROR: Unknown identifier`n$pass"},
    @{ExitCode=0;Log="SCRIPT ERROR: Invalid call`n$pass"},
    @{ExitCode=0;Log="ERROR: Shader compilation failed.`n$pass"}
)) {
    $rejected=$false
    try { Assert-GodotRunLog @case -SelfTest } catch { $rejected=$true }
    if(-not $rejected){throw 'Runtime validation accepted an incomplete self-test or a zero-exit engine error.'}
}
Assert-GodotRunLog -ExitCode 0 -Log $pass -SelfTest
Assert-GodotRunLog -ExitCode 0 -Log 'Import complete'
Write-Host 'GODOT_RUNTIME_LOG_PASS: shader/script errors override a zero exit and success marker; self-test completion required'
foreach ($case in @(
    @{ExitCode=1;Log=''},
    @{ExitCode=0;Log='ERROR: .NET Sdk not found.'},
    @{ExitCode=0;Log='System.InvalidOperationException: Failed to build project.'},
    @{ExitCode=0;Log='WARNING: Project export for preset "Windows Desktop" completed with warnings.'},
    @{ExitCode=0;Log='ERROR: Cannot open packed resource.'},
    @{ExitCode=0;Log='ERROR: Failed to read the root certificate store.'}
)) {
    $rejected=$false
    try { Assert-GodotExportLog @case } catch { $rejected=$true }
    if (-not $rejected) { throw "Invalid export accepted: $($case.Log)" }
}
Assert-GodotExportLog -ExitCode 0 -Log '[ DONE ] savepack'
Write-Host 'GODOT_EXPORT_GUARD_PASS: nonzero exit, zero-exit build failure, warnings, missing resources and certificate errors'
$repo = Split-Path -Parent $PSScriptRoot
$id = [Guid]::NewGuid().ToString('N')
$stage = [IO.Path]::GetFullPath((Join-Path $repo ".tools/export-staging/test-$id"))
$destination = [IO.Path]::GetFullPath((Join-Path $repo "artifacts/game/test-$id"))
try {
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'current.txt'), 'new')
    [IO.File]::WriteAllText((Join-Path $destination 'obsolete.dll'), 'old')
    Publish-GodotPackage -Stage $stage -Destination $destination
    if ((Get-Content -LiteralPath (Join-Path $destination 'current.txt') -Raw) -ne 'new' -or
        (Test-Path -LiteralPath (Join-Path $destination 'obsolete.dll'))) { throw 'Package replacement retained stale files.' }
    $rejected = $false
    try { Publish-GodotPackage -Stage $stage -Destination $destination } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath (Join-Path $destination 'current.txt'))) { throw 'Missing stage damaged previous package.' }
    $rejected = $false
    try { Publish-GodotPackage -Stage $destination -Destination $repo } catch { $rejected = $true }
    if (-not $rejected) { throw 'Publication accepted paths outside its output roots.' }
    Write-Host 'GODOT_PACKAGE_REPLACE_PASS: exact replacement, missing-stage preservation and output path guards'
} finally {
    # Both absolute paths above are fixed repository subdirectories plus a fresh GUID.
    foreach ($path in @($stage, $destination)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}
