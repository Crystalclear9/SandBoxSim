Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
foreach ($case in @(
    @{ExitCode=1;Log=''},
    @{ExitCode=0;Log='ERROR: .NET Sdk not found.'},
    @{ExitCode=0;Log='System.InvalidOperationException: Failed to build project.'},
    @{ExitCode=0;Log='WARNING: Project export for preset "Windows Desktop" completed with warnings.'},
    @{ExitCode=0;Log='ERROR: Cannot open packed resource.'}
)) {
    $rejected=$false
    try { Assert-GodotExportLog @case } catch { $rejected=$true }
    if (-not $rejected) { throw "Invalid export accepted: $($case.Log)" }
}
Assert-GodotExportLog -ExitCode 0 -Log '[ DONE ] savepack'
Assert-GodotExportLog -ExitCode 0 -Log "ERROR: Failed to read the root certificate store.`n   at: get_system_ca_certificates"
Write-Host 'GODOT_EXPORT_GUARD_PASS: nonzero exit, zero-exit build failure, warnings, missing resources and known certificate diagnostic'
