# Bind native evaluations to the C# source that was actually built.
function Get-GodotSourceHashes([string]$Root) {
    $result = [ordered]@{}
    foreach ($name in @('SandBoxSim.Core', 'SandBoxSim.Godot')) {
        Get-ChildItem -LiteralPath (Join-Path $Root "src/$name") -Recurse -File |
            Where-Object { $_.Extension -in @('.cs', '.csproj') -and $_.FullName -notmatch '[\\/](bin|obj|\.godot)[\\/]' } |
            Sort-Object FullName | ForEach-Object {
                $relative = [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
                $result[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
    }
    foreach ($name in @('Directory.Build.props', 'global.json', 'NuGet.Config')) {
        $path = Join-Path $Root $name
        if (Test-Path -LiteralPath $path) { $result[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    return $result
}
