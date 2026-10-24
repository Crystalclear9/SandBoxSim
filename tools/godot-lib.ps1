# Shared Godot executable discovery; no build or network side effects.
function Publish-GodotPackage {
    param([Parameter(Mandatory)][string]$Stage, [Parameter(Mandatory)][string]$Destination)
    $repo = Split-Path -Parent $PSScriptRoot
    $source = [IO.Path]::GetFullPath($Stage)
    $target = [IO.Path]::GetFullPath($Destination)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    foreach ($pair in @(@($source, '.tools/export-staging'), @($target, 'artifacts/game'))) {
        $allowed = [IO.Path]::GetFullPath((Join-Path $repo $pair[1])) + [IO.Path]::DirectorySeparatorChar
        if (-not $pair[0].StartsWith($allowed, $comparison)) { throw 'Package publication escaped project output directories.' }
    }
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Package staging directory does not exist.' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    $backup = $target + '.previous-' + [Guid]::NewGuid().ToString('N')
    $hadPrevious = Test-Path -LiteralPath $target -PathType Container
    if ($hadPrevious) { [IO.Directory]::Move($target, $backup) }
    try {
        # Move the complete verified package; overlay copying retains obsolete
        # assemblies and can leave a mixture of versions after a copy failure.
        [IO.Directory]::Move($source, $target)
    } catch {
        if ($hadPrevious) { [IO.Directory]::Move($backup, $target) }
        throw
    }
    if ($hadPrevious) { Remove-Item -LiteralPath $backup -Recurse -Force }
}

function Assert-GodotExportLog {
    param([int]$ExitCode, [string]$Log)
    # Some editor failures return zero and still write an executable containing
    # an old assembly. The process status alone is not an export contract.
    if ($ExitCode -ne 0 -or $Log -match '(?im)^\s*ERROR:(?! Failed to read the root certificate store\.)' -or
        $Log -match '(?i)failed to build project|project export .*completed with warnings') {
        throw 'Godot export failed; inspect the project-local export log. Previous game package was preserved.'
    }
}
function Invoke-GodotTextureReimport {
    param([string]$GodotPath,[string]$GameRoot,[string]$LogFile,[int]$TimeoutSeconds=180)
    if ($GodotPath.EndsWith('_console.exe',[StringComparison]::OrdinalIgnoreCase)) {
        $native=$GodotPath.Substring(0,$GodotPath.Length-'_console.exe'.Length)+'.exe'
        if (Test-Path -LiteralPath $native -PathType Leaf) { $GodotPath=$native }
    }
    $options=@{FilePath=$GodotPath;PassThru=$true;ArgumentList=@('--headless','--editor','--path',"`"$GameRoot`"",'--log-file',"`"$LogFile`"",'--','--reimport-textures');RedirectStandardOutput=$LogFile+'.stdout';RedirectStandardError=$LogFile+'.stderr'}
    if ($IsWindows) { $options.WindowStyle='Hidden' }
    $process=Start-Process @options
    if (-not $process.WaitForExit($TimeoutSeconds*1000)) {
        $process.Kill($true);$process.WaitForExit();throw 'Texture import exceeded its time limit.'
    }
    $log=[IO.File]::ReadAllText($options.RedirectStandardOutput)+[IO.File]::ReadAllText($options.RedirectStandardError)
    Assert-GodotExportLog -ExitCode $process.ExitCode -Log $log
    if ($log -notmatch 'TEXTURE_REIMPORT_PASS: [1-9][0-9]* actual mip chains verified') { throw 'Texture import did not verify actual mip data.' }
}
function Resolve-GodotExecutable {
    param([string]$Explicit, [string]$InstallRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) '.tools/godot-4.7.2'),
        [ValidateSet('windows','linux','macos')][string]$Platform = $(if ($IsWindows) {'windows'} elseif ($IsMacOS) {'macos'} else {'linux'}))
    if ($Explicit) {
        $target = $Explicit
        if ($Platform -eq 'macos' -and (Test-Path -LiteralPath $target -PathType Container)) { $target = Join-Path $target 'Contents/MacOS/Godot' }
        if (Test-Path -LiteralPath $target -PathType Leaf) { return [IO.Path]::GetFullPath($target) }
        $command = Get-Command $Explicit -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { return $command.Source }
        throw "Explicit Godot executable not found: $Explicit"
    }
    if (Test-Path -LiteralPath $InstallRoot -PathType Container) {
        $found = Get-ChildItem -LiteralPath $InstallRoot -Recurse -File | Where-Object {
            if ($Platform -eq 'windows') { $_.Name -like '*console.exe' }
            elseif ($Platform -eq 'macos') { $_.Name -eq 'Godot' -and $_.Directory.Name -eq 'MacOS' }
            else { $_.Name -match '^Godot.*(x86_64|arm64)$' }
        } | Sort-Object FullName | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    foreach ($name in @('godot-mono','godot','godot4')) {
        $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { return $command.Source }
    }
    throw 'Godot 4.7.2 .NET executable missing. Use GODOT_EXE, -GodotPath or tools/setup-godot.ps1.'
}
