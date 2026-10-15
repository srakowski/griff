<#
  The notes of a GitHub Release: the commits since the previous release tag, newest first, one line each (its subject
  and short hash), so the same two tags always give the same notes. The first release (no earlier v* tag) says so and
  lists nothing. Written as Markdown to -Out, or printed.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release-notes.ps1 -Tag v0.1.0 [-Out notes.md]
#>
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Out
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw "A release tag is vMAJOR.MINOR.PATCH, like v0.1.0, not '$Tag'." }
    git rev-parse --verify --quiet "$Tag^{commit}" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "There is no tag $Tag in this repository." }

    # The nearest earlier release tag that is an ancestor of this one.
    $previous = $null
    foreach ($candidate in (git tag --list 'v[0-9]*.[0-9]*.[0-9]*' --merged "$Tag^" --sort=-v:refname)) {
        if ($candidate -match '^v\d+\.\d+\.\d+$' -and $candidate -ne $Tag) { $previous = $candidate; break }
    }

    $notes = [System.Collections.Generic.List[string]]::new()
    $notes.Add("Griff $($Tag.Substring(1)) for 64-bit Windows.")
    $notes.Add('')
    $notes.Add('Unzip it anywhere you can write to and run `Griff.Editor.exe`. It needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). More at https://griff.games/docs/getting-started/.')
    $notes.Add('')
    if ($previous) {
        $notes.Add("## Changes since $previous")
        $notes.Add('')
        $log = git log "$previous..$Tag" --no-merges --format='- %s (%h)'
        if ($log) { $notes.AddRange([string[]]@($log)) } else { $notes.Add('- No changes to the code.') }
    }
    else {
        $notes.Add('This is the first release.')
    }

    $text = ($notes -join "`n") + "`n"
    if ($Out) { [System.IO.File]::WriteAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out), $text) }
    else { Write-Host $text -NoNewline }
}
finally {
    Pop-Location
}
