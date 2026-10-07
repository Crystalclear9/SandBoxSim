# Shared Godot executable discovery; no build or network side effects.
function Resolve-GodotExecutable {
    param([string]$Explicit, [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.sandboxsim-tool/godot-4.7.2'),
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
