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
    $release = '^v\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$'
    if ($Tag -notmatch $release) { throw "A release tag is vMAJOR.MINOR.PATCH, like v0.1.0, or a pre-release like v0.1.0-alpha.1, not '$Tag'." }
    $prerelease = $Tag.Contains('-')
    git rev-parse --verify --quiet "$Tag^{commit}" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "There is no tag $Tag in this repository." }

    # The nearest earlier release tag (a release's or a pre-release's) on this one's history: the one the fewest commits back.
    $previous = $null
    $nearest = [int]::MaxValue
    foreach ($candidate in (git tag --list 'v[0-9]*' --merged "$Tag^")) {
        if ($candidate -notmatch $release -or $candidate -eq $Tag) { continue }
        $distance = [int](git rev-list --count "$candidate..$Tag")
        if ($distance -lt $nearest) { $previous = $candidate; $nearest = $distance }
    }

    $notes = [System.Collections.Generic.List[string]]::new()
    $channel = if ($prerelease) { 'prerelease' } else { 'stable' }
    $notes.Add("Griff $($Tag.Substring(1)) for 64-bit Windows$(if ($prerelease) { ', a pre-alpha: expect rough edges, and projects made with it may need changes in later versions' }).")
    $notes.Add('')
    $notes.Add("**Install it**: download ``GriffEditor-$channel-Setup.exe`` and run it. It installs Griff for you (no administrator needed, and the .NET 10 Desktop Runtime if it's missing), adds it to the Start menu and the desktop, and keeps it up to date: Griff looks for a newer $(if ($prerelease) { 'pre-release (or release)' } else { 'release' }) after it starts and asks before restarting into it. The installer isn't code-signed yet, so Windows SmartScreen may warn that it's from an unknown publisher: pick **More info**, then **Run anyway**.")
    $notes.Add('')
    $notes.Add('**Or the portable copy**: unzip `Griff-' + $Tag.Substring(1) + '-win-x64.zip` anywhere you can write to and run `Griff.Editor.exe`. It needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) and doesn''t update itself.')
    $notes.Add('')
    $notes.Add('More at https://griff.games/docs/getting-started/. The `.nupkg` and `releases.*.json` files are the installed editor''s updates.')
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
