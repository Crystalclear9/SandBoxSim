[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string[]]$Paths = @()
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if ($Paths.Count -eq 0) {
    $Paths = @(git -C $RepositoryRoot ls-files --cached --others --exclude-standard -- '*.md' | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository documentation with Git.' }
}
$failures = [Collections.Generic.List[string]]::new()
$linkCount = 0
foreach ($relative in $Paths) {
    $document = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relative))
    if (-not (Test-Path -LiteralPath $document -PathType Leaf)) { $failures.Add("${relative}: document missing"); continue }
    $source = [IO.File]::ReadAllText($document)
    # Markdown links and image links. External URLs and heading-only links are excluded.
    foreach ($match in [regex]::Matches($source, '\]\((?<target><[^>]+>|[^)\r\n]+)\)')) {
        $target = $match.Groups['target'].Value.Trim().Trim([char[]]'<>')
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $target.StartsWith('#')) { continue }
        $target = [Uri]::UnescapeDataString(($target -split '#', 2)[0])
        if (-not $target) { continue }
        $linkCount++
        $absolute = [IO.Path]::GetFullPath((Join-Path (Split-Path $document -Parent) $target))
        $relativeTarget = [IO.Path]::GetRelativePath($RepositoryRoot, $absolute)
        if ([IO.Path]::IsPathRooted($relativeTarget) -or $relativeTarget -eq '..' -or $relativeTarget -match '^\.\.[/\\]') {
            $failures.Add("${relative}: link leaves repository: $target"); continue
        }
        if (-not (Test-Path -LiteralPath $absolute)) { $failures.Add("${relative}: missing target: $target"); continue }
        # Verify actual casing even on Windows, where Test-Path alone would accept a broken Linux link.
        $cursor = $RepositoryRoot
        foreach ($part in ($relativeTarget -split '[/\\]')) {
            if ($part -eq '.') { continue }
            $exact = @([IO.Directory]::EnumerateFileSystemEntries($cursor) | Where-Object { [IO.Path]::GetFileName($_) -ceq $part })
            if ($exact.Count -eq 0) { $failures.Add("${relative}: target casing differs: $target"); break }
            $cursor = $exact[0]
        }
    }
}
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Output $failure }
    throw "$($failures.Count) documentation link errors."
}
Write-Output "DOC_LINKS_PASS: $($Paths.Count) documents, $linkCount local links; paths and casing verified."
