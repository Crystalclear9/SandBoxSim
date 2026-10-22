Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'build-lib.ps1')
. (Join-Path $PSScriptRoot 'godot-build-manifest.ps1')
$fixtureRoot=[IO.Path]::GetFullPath((Join-Path $env:TEMP ('manifest-test-'+[Guid]::NewGuid().ToString('N'))))
$temporaryRoot=[IO.Path]::GetFullPath($env:TEMP)+[IO.Path]::DirectorySeparatorChar
try {
    foreach($project in @('SandBoxSim.Core','SandBoxSim.Godot')) {
        $folder=Join-Path $fixtureRoot "src/$project"
        [IO.Directory]::CreateDirectory($folder) | Out-Null
        [IO.File]::WriteAllText((Join-Path $folder 'Example.cs'),'class Example {}')
    }
    foreach($name in @('Directory.Build.props','global.json','NuGet.config')) {
        [IO.File]::WriteAllText((Join-Path $fixtureRoot $name),'first')
    }
    $before=Get-GodotSourceHashes $fixtureRoot
    if (-not ($before.Keys -ccontains 'NuGet.config') -or ($before.Keys -ccontains 'NuGet.Config')) { throw 'Build identity omitted or mis-cased the repository NuGet.config.' }
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'NuGet.config'),'changed package sources')
    $after=Get-GodotSourceHashes $fixtureRoot
    if($before['NuGet.config'] -eq $after['NuGet.config']) { throw 'Package-source change did not change the build identity.' }
    Write-Host 'GODOT_MANIFEST_PASS: canonical NuGet.config identity and configuration change detection'
} finally {
    if(-not $fixtureRoot.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped project temporary directory.' }
    if(Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
