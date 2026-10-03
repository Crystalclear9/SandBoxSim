<# Script regression tests. Requires .NET 8 SDK; no Pester or NuGet dependencies. #>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Loading the library must not require caller variables or execute the CLI.
. (Join-Path $PSScriptRoot 'build-lib.ps1')

$script:CheckCount = 0
function Assert-Build {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:CheckCount++
    Write-Host "PASS: $Message"
}

$expectedExe = if ($script:SandBoxSimIsWindows) { 'dotnet.exe' } else { 'dotnet' }
Assert-Build ((Get-DotnetExeName) -eq $expectedExe) 'Native executable name'
Assert-Build ($null -eq (Select-CompatibleSdk -Installed @())) 'Empty SDK list is safe under StrictMode'
Assert-Build ($null -eq (Select-CompatibleSdk -Installed @('9.0.100 [/sdk]', '8.0.100-preview.1 [/sdk]'))) 'Only stable .NET 8 SDK is accepted'
$sdkFolder = Join-Path $script:RepoRoot 'sdk'
$selected = Select-CompatibleSdk -Installed @("8.0.99 [$sdkFolder]", "8.0.100 [$sdkFolder]", "9.0.100 [$sdkFolder]")
Assert-Build ($selected.Version.ToString() -eq '8.0.100') 'SDK versions sort numerically'
Assert-Build ($selected.Root -eq $script:RepoRoot) 'SDK root comes from host output, not PATH symlink location'

$previousRoot = $env:SANDBOXSIM_DOTNET_ROOT
try {
    # This is deliberately a foreign/stale Windows path on Unix runners.
    $env:SANDBOXSIM_DOTNET_ROOT = 'C:\__sandboxsim_missing_sdk__\net8'
    $sdk = Resolve-DotnetSdk -Explicit 'C:\__sandboxsim_missing_sdk__\net8'
    Assert-Build ($null -ne $sdk) 'Stale Windows explicit/env paths fall back to installed SDK'
    Assert-Build (Test-Path -LiteralPath $sdk.Exe -PathType Leaf) 'Resolved SDK host exists'
    $env:SANDBOXSIM_DOTNET_ROOT = $null
    Assert-Build ([IO.Path]::IsPathRooted((Get-DefaultSdkRoot))) 'Default SDK directory is absolute'
}
finally { $env:SANDBOXSIM_DOTNET_ROOT = $previousRoot }

# Mixed SDK installations must select .NET 8 ref packs and compiler together.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sandboxsim-build-test-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($leaf in @('packs/Microsoft.NETCore.App.Ref/8.0.9/ref/net8.0',
                       'packs/Microsoft.NETCore.App.Ref/9.0.1/ref/net9.0',
                       'sdk/8.0.100/Roslyn/bincore', 'sdk/9.0.100/Roslyn/bincore')) {
        New-Item -ItemType Directory -Path (Join-Path $fixture $leaf) -Force | Out-Null
    }
    New-Item -ItemType File -Path (Join-Path $fixture 'sdk/8.0.100/Roslyn/bincore/csc.dll') | Out-Null
    $refs = Resolve-ReferenceAssemblies -DotnetRoot $fixture -RuntimeDir ''
    Assert-Build ($refs.Kind -eq 'refpack' -and $refs.Dir -match '8\.0\.9') 'Newer .NET ref pack does not hide net8 references'
    $compiler = Resolve-RoslynCsc -DotnetRoot $fixture
    Assert-Build ($compiler.Path -match '8\.0\.100') 'Compiler uses the matching SDK major version'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}

$argv = @(Get-ConsoleArgs -Mode batch -Seed 42 -Days 60 -SeedRange '1..6' -Agents 20)
$seedIndex = [Array]::IndexOf($argv, '--seeds')
$agentIndex = [Array]::IndexOf($argv, '--agents')
Assert-Build ($seedIndex -ge 0 -and $argv[$seedIndex + 1] -eq '1..6') 'Batch seed range reaches the console intact'
Assert-Build ($agentIndex -ge 0 -and $argv[$agentIndex + 1] -eq '20') 'Agent count reaches the console intact'
Write-Host "Build script checks passed: $script:CheckCount"
