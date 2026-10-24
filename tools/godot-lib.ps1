# Shared Godot executable discovery; no build or network side effects.
function Get-GodotEnvironmentSnapshot {
    $snapshot=@{}
    foreach($name in @('APPDATA','LOCALAPPDATA','XDG_CONFIG_HOME','XDG_DATA_HOME','XDG_CACHE_HOME','SANDBOXSIM_GODOT_PROFILE_POLICY','SANDBOXSIM_ORIGINAL_APPDATA')){
        $snapshot[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    }
    return $snapshot
}
function Restore-GodotEnvironment {
    param([hashtable]$Snapshot)
    foreach($name in $Snapshot.Keys){
        if($null -eq $Snapshot[$name]){Remove-Item -LiteralPath ('Env:'+$name) -ErrorAction SilentlyContinue}
        else{[Environment]::SetEnvironmentVariable($name,$Snapshot[$name],'Process')}
    }
}

function Set-GodotProcessEnvironment {
    param([string]$Root=(Split-Path -Parent $PSScriptRoot),
        [ValidateSet('windows','linux','macos')][string]$Platform=$(if($IsWindows){'windows'}elseif($IsMacOS){'macos'}else{'linux'}))
    $state=Join-Path ([IO.Path]::GetFullPath($Root)) '.tools/godot-state'
    foreach($name in @('config','data','cache')){[IO.Directory]::CreateDirectory((Join-Path $state $name)) | Out-Null}
    if($Platform -eq 'windows'){
        if(-not $env:SANDBOXSIM_ORIGINAL_APPDATA){$env:SANDBOXSIM_ORIGINAL_APPDATA=$env:APPDATA}
        $env:APPDATA=Join-Path $state 'config';$env:LOCALAPPDATA=Join-Path $state 'cache'
    }
    elseif($Platform -eq 'linux'){$env:XDG_CONFIG_HOME=Join-Path $state 'config';$env:XDG_DATA_HOME=Join-Path $state 'data';$env:XDG_CACHE_HOME=Join-Path $state 'cache'}
    $env:SANDBOXSIM_GODOT_PROFILE_POLICY=if($Platform -eq 'macos'){'platform-user-v1'}else{'project-local-v1'}
}

function Set-GodotEditorTrust {
    param([string]$Executable,[string]$Root=(Split-Path -Parent $PSScriptRoot))
    # Configure only this checkout's self-contained editor, never a shared
    # installation or the user's global EditorSettings.
    $rootPath=[IO.Path]::GetFullPath($Root)
    $editor=[IO.Path]::GetFullPath($Executable)
    $comparison=if($IsWindows){[StringComparison]::OrdinalIgnoreCase}else{[StringComparison]::Ordinal}
    if(-not $editor.StartsWith((Join-Path $rootPath '.tools')+[IO.Path]::DirectorySeparatorChar,$comparison)){return}
    $folder=Split-Path -Parent $editor
    if(-not (Test-Path -LiteralPath (Join-Path $folder '_sc_')) -and -not (Test-Path -LiteralPath (Join-Path $folder '._sc_'))){return}
    $bundle=Join-Path $rootPath 'src/SandBoxSim.Godot/assets/runtime/mozilla-ca.crt'
    $manifest=Get-Content -LiteralPath (Join-Path (Split-Path $bundle -Parent) 'manifest.json') -Raw | ConvertFrom-Json
    if((Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256){throw 'Runtime CA bundle hash mismatch.'}
    $settings=Join-Path $folder 'editor_data/editor_settings-4.7.tres'
    $text=if(Test-Path -LiteralPath $settings){[IO.File]::ReadAllText($settings)}else{"[gd_resource type=`"EditorSettings`" format=3]`n`n[resource]`n"}
    $pattern='(?m)^network/tls/editor_tls_certificates\s*=\s*(.*)$'
    $match=[regex]::Match($text,$pattern)
    $value=$bundle.Replace('\','/') | ConvertTo-Json -Compress
    $line='network/tls/editor_tls_certificates = '+$value
    if($match.Success){
        $existing=$match.Groups[1].Value.Trim()
        if($existing -ne '""' -and $existing -ne $value){return} # Retain explicit private trust choices.
        $text=$text.Substring(0,$match.Index)+$line+$text.Substring($match.Index+$match.Length)
    }else{$text+="`n$line`n"}
    [IO.Directory]::CreateDirectory((Split-Path $settings -Parent)) | Out-Null
    [IO.File]::WriteAllText($settings,$text,[Text.UTF8Encoding]::new($false))
}

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
    if ($ExitCode -ne 0 -or $Log -match '(?im)^\s*ERROR:' -or
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
