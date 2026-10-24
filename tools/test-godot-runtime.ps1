Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
$repo=Split-Path -Parent $PSScriptRoot
$temporary=[IO.Path]::GetFullPath((Join-Path $repo '.tools/tmp'))
$fixture=Join-Path $temporary ('runtime-test-'+[Guid]::NewGuid().ToString('N'))
$variables=@('APPDATA','LOCALAPPDATA','XDG_CONFIG_HOME','XDG_DATA_HOME','XDG_CACHE_HOME','SANDBOXSIM_GODOT_PROFILE_POLICY','SANDBOXSIM_ORIGINAL_APPDATA')
$previous=@{};foreach($name in $variables){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $initial=Get-GodotEnvironmentSnapshot
    try { Set-GodotProcessEnvironment -Root $fixture -Platform windows }
    finally { Restore-GodotEnvironment -Snapshot $initial }
    foreach($name in $initial.Keys){
        if([Environment]::GetEnvironmentVariable($name,'Process') -cne $initial[$name]){throw 'Unset caller variable was not restored'}
    }
    $editor=Join-Path $fixture '.tools/editor/Godot.exe'
    [IO.Directory]::CreateDirectory((Split-Path $editor -Parent)) | Out-Null
    [IO.File]::WriteAllText($editor,'fixture')
    [IO.File]::WriteAllText((Join-Path (Split-Path $editor -Parent) '_sc_'),'')
    $resources=Join-Path $fixture 'src/SandBoxSim.Godot/assets/runtime'
    [IO.Directory]::CreateDirectory($resources) | Out-Null
    foreach($name in @('mozilla-ca.crt','manifest.json')){Copy-Item -LiteralPath (Join-Path $repo "src/SandBoxSim.Godot/assets/runtime/$name") -Destination $resources}
    foreach($platform in @('windows','linux','macos')){
        Set-GodotProcessEnvironment -Root $fixture -Platform $platform
        $policy=if($platform -eq 'macos'){'platform-user-v1'}else{'project-local-v1'}
        if($env:SANDBOXSIM_GODOT_PROFILE_POLICY -ne $policy){throw 'Wrong profile policy'}
    }
    $snapshot=Get-GodotEnvironmentSnapshot
    try { Set-GodotProcessEnvironment -Root (Join-Path $fixture 'nested') -Platform windows }
    finally { Restore-GodotEnvironment -Snapshot $snapshot }
    foreach($name in $snapshot.Keys){
        if([Environment]::GetEnvironmentVariable($name,'Process') -cne $snapshot[$name]){throw 'Caller environment was not restored'}
    }
    $settings=Join-Path (Split-Path $editor -Parent) 'editor_data/editor_settings-4.7.tres'
    [IO.Directory]::CreateDirectory((Split-Path $settings -Parent)) | Out-Null
    [IO.File]::WriteAllText($settings,"[gd_resource type=`"EditorSettings`" format=3]`n[resource]`ncustom/keep = 7`nnetwork/tls/editor_tls_certificates = `"`"`n")
    Set-GodotEditorTrust -Executable $editor -Root $fixture
    $actual=[IO.File]::ReadAllText($settings)
    if($actual -notmatch 'custom/keep = 7' -or $actual -notmatch 'mozilla-ca.crt'){throw 'Portable trust configuration lost settings'}
    [IO.File]::WriteAllText($settings,$actual.Replace(((Join-Path $resources 'mozilla-ca.crt').Replace('\','/')),'private.pem'))
    $private=[IO.File]::ReadAllText($settings)
    Set-GodotEditorTrust -Executable $editor -Root $fixture
    if([IO.File]::ReadAllText($settings) -cne $private){throw 'Explicit private trust overwritten'}
    Set-GodotEditorTrust -Executable (Join-Path $fixture 'shared/Godot.exe') -Root $fixture
    if(Test-Path (Join-Path $fixture 'shared')){throw 'Shared editor settings modified'}
    [IO.File]::AppendAllText((Join-Path $resources 'mozilla-ca.crt'),'tampered')
    $rejected=$false;try{Set-GodotEditorTrust -Executable $editor -Root $fixture}catch{$rejected=$true}
    if(-not $rejected){throw 'Modified trust bundle accepted'}
    Write-Host 'GODOT_RUNTIME_PASS: platform profiles, caller environment restoration, scoped portable settings, preserved private/shared settings and CA integrity'
}finally{
    Restore-GodotEnvironment -Snapshot $previous
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith($temporary+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped temporary directory'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
