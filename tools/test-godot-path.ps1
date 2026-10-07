Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('sandboxsim-godot-path-' + [Guid]::NewGuid().ToString('N'))
$fixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
if (-not $fixtureRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture outside temporary directory' }
$files = @{
    windows = Join-Path $fixtureRoot 'win/Godot_console.exe'
    linux = Join-Path $fixtureRoot 'linux/Godot_v4.7.2-stable_mono_linux.x86_64'
    macos = Join-Path $fixtureRoot 'mac/Godot.app/Contents/MacOS/Godot'
}
$previousPath = $env:PATH
try {
    $env:PATH = ''
    foreach ($file in $files.Values) { New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null; [IO.File]::WriteAllText($file, 'fixture') }
    foreach ($platform in $files.Keys) {
        $actual = Resolve-GodotExecutable -InstallRoot $fixtureRoot -Platform $platform
        if ($actual -ne [IO.Path]::GetFullPath($files[$platform])) { throw "Wrong $platform executable: $actual" }
        Write-Host "PASS: $platform installed layout"
    }
    $bundle = Join-Path $fixtureRoot 'mac/Godot.app'
    if ((Resolve-GodotExecutable -Explicit $bundle -Platform macos) -ne [IO.Path]::GetFullPath($files.macos)) { throw 'Bundle path did not resolve' }
    $rejected = $false
    try { Resolve-GodotExecutable -Explicit (Join-Path $fixtureRoot 'missing') -InstallRoot $fixtureRoot | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid explicit path silently fell back' }
    Write-Host 'PASS: bundle path and invalid explicit path'
} finally {
    $env:PATH = $previousPath
    foreach ($file in $files.Values) { if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file } }
    # Only remove empty fixture directories; unexpected contents are preserved.
    if (Test-Path -LiteralPath $fixtureRoot) {
        Get-ChildItem -LiteralPath $fixtureRoot -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { if (-not @(Get-ChildItem -LiteralPath $_.FullName -Force).Count) { Remove-Item -LiteralPath $_.FullName } }
        if (-not @(Get-ChildItem -LiteralPath $fixtureRoot -Force).Count) { Remove-Item -LiteralPath $fixtureRoot }
    }
}
