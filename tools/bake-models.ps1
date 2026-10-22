[CmdletBinding()]
param([string]$BlenderPath='', [string]$HumanSource='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'build-lib.ps1')
. (Join-Path $PSScriptRoot 'godot-lib.ps1')
$repositoryRoot=Split-Path -Parent $PSScriptRoot
if(-not $HumanSource){$HumanSource=Join-Path $repositoryRoot '.tools/sources/realistic_human_base.blend'}
if(-not $BlenderPath){
    $name=if($IsWindows){'blender.exe'}else{'blender'}
    $found=Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.tools/blender-4.5.14') -File -Recurse -Filter $name | Select-Object -First 1
    if($found){$BlenderPath=$found.FullName}
}
if(-not $BlenderPath -or -not (Test-Path -LiteralPath $BlenderPath -PathType Leaf)){throw 'Place Blender 4.5.14 in .tools/blender-4.5.14 or pass -BlenderPath.'}
if(-not (Test-Path -LiteralPath $HumanSource -PathType Leaf)){throw 'Place the licensed human source in .tools/sources or pass -HumanSource. See docs/development/human-assets.md.'}
$env:BLENDER_USER_CONFIG=Join-Path $repositoryRoot '.tools/blender-config'
$env:BLENDER_USER_SCRIPTS=Join-Path $repositoryRoot '.tools/blender-scripts'
$runtime=Get-DotnetHost -Sdk (Resolve-DotnetSdk)
$env:PATH=(Split-Path -Parent $runtime)+[IO.Path]::PathSeparator+$env:PATH
& (Join-Path $PSScriptRoot 'godot.ps1') -Mode build
if($LASTEXITCODE -ne 0){throw 'Build failed before animal source export'}
$editor=Resolve-GodotExecutable -Explicit $env:GODOT_EXE
$sourceRoot=Join-Path $repositoryRoot '.tools/animal-sources'
& $editor --headless --path (Join-Path $repositoryRoot 'src/SandBoxSim.Godot') --log-file (Join-Path $repositoryRoot '.tools/model-export.log') -- "--export-animal-sources=$sourceRoot"
if($LASTEXITCODE -ne 0){throw 'Animal source export failed'}
& $BlenderPath --background --disable-autoexec $HumanSource --python-exit-code 2 --python (Join-Path $PSScriptRoot 'export_human_forearm.py') -- --output (Join-Path $repositoryRoot 'src/SandBoxSim.Godot/assets/models/human')
if($LASTEXITCODE -ne 0){throw 'Anatomical forearm export failed'}
& $BlenderPath --background --disable-autoexec $HumanSource --python (Join-Path $PSScriptRoot 'export_human_hands.py') -- --output (Join-Path $repositoryRoot 'src/SandBoxSim.Godot/assets/models/human')
if($LASTEXITCODE -ne 0){throw 'Anatomical hand export failed'}
& $BlenderPath --background --factory-startup --python (Join-Path $PSScriptRoot 'export_hand_lod.py') -- --source (Join-Path $repositoryRoot 'src/SandBoxSim.Godot/assets/models/human/human-hand.mesh.json') --output (Join-Path $repositoryRoot 'src/SandBoxSim.Godot/assets/models/human/human-hand-lod.mesh.json')
if($LASTEXITCODE -ne 0){throw 'Anatomical hand LOD export failed'}
& $BlenderPath --background --factory-startup --python (Join-Path $PSScriptRoot 'bake_animal_surfaces.py') -- --source $sourceRoot --output (Join-Path $repositoryRoot 'src/SandBoxSim.Godot/assets/models/animals')
if($LASTEXITCODE -ne 0){throw 'Animal surface bake failed'}
Write-Host 'Bakes finished. Review printed hashes against the runtime constants, then run tools/godot.ps1 -Mode test.'
