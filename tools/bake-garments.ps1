[CmdletBinding()]
param([string]$BlenderPath='',[string]$GodotPath=$env:GODOT_EXE)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'build-lib.ps1')
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
$repositoryRoot=Split-Path $PSScriptRoot -Parent
if(!$BlenderPath){
    $name=if($IsWindows){'blender.exe'}else{'blender'}
    $found=Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.tools/blender-4.5.14') -Filter $name -Recurse -File | Select-Object -First 1
    if($found){$BlenderPath=$found.FullName}
}
if(!$BlenderPath -or !(Test-Path -LiteralPath $BlenderPath -PathType Leaf)){throw 'Place Blender 4.5.14 in .tools/blender-4.5.14 or supply -BlenderPath.'}
$env:BLENDER_USER_CONFIG=Join-Path $repositoryRoot '.tools/blender-config'
$env:BLENDER_USER_SCRIPTS=Join-Path $repositoryRoot '.tools/blender-scripts'
$runtime=Get-DotnetHost -Sdk (Resolve-DotnetSdk)
$env:DOTNET_ROOT=Split-Path $runtime -Parent
$env:PATH=$env:DOTNET_ROOT+[IO.Path]::PathSeparator+$env:PATH
& (Join-Path $PSScriptRoot 'godot.ps1') -Mode build
if($LASTEXITCODE -ne 0){throw 'Build failed before garment export'}
$engine=(Resolve-GodotExecutable -Explicit $GodotPath).Replace('_console.exe','.exe')
$source=Join-Path $repositoryRoot '.tools/garment-sources'
$game=Join-Path $repositoryRoot 'src/SandBoxSim.Godot'
$output=Join-Path $game 'assets/models/garments'
$arguments=@('--headless','--path',"`"$game`"",'--log-file',"`"$repositoryRoot/.tools/garment-source.log`"",'--',"`"--export-garment-sources=$source`"")
$options=@{FilePath=$engine;ArgumentList=$arguments;PassThru=$true;RedirectStandardOutput="$repositoryRoot/.tools/garment-source-stdout.log";RedirectStandardError="$repositoryRoot/.tools/garment-source-stderr.log"}
if($IsWindows){$options.WindowStyle='Hidden'}
$process=Start-Process @options
if(!$process.WaitForExit(60000)){$process.Kill($true);throw 'Garment source export timed out'}
if($process.ExitCode -ne 0){throw 'Garment source export failed'}
& $BlenderPath --background --factory-startup --disable-autoexec --python-exit-code 2 --python (Join-Path $PSScriptRoot 'bake_garment_surfaces.py') -- --source $source --output $output
if($LASTEXITCODE -ne 0){throw 'Garment bake failed'}
$manifest=Get-Content -LiteralPath (Join-Path $output 'manifest.json') -Raw | ConvertFrom-Json
for($cut=0;$cut -lt 3;$cut++){
    $name="garment-$cut.skin.json"
    $path=Join-Path $output $name
    $data=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256.$name -or
       $data.sourceSha256 -ne (Get-FileHash -LiteralPath (Join-Path $source "garment-$cut-source.json") -Algorithm SHA256).Hash.ToLowerInvariant()){
        throw "Garment provenance mismatch: $name"
    }
}
Write-Output 'GARMENT_BAKE_PASS: three source-bound connected skinned garments'
